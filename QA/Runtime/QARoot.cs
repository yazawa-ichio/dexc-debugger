using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
#if DEXC_QA_HAS_TELEMETRY
using Dexc.Telemetry;
#endif

namespace Dexc.QA
{
	/// <summary>
	/// The root of the running QA and the only mutable static variable of this package. Every other piece of state hangs
	/// off it, so replacing it resets the package. Main thread only, but for <see cref="OnLog"/>, which Unity calls from any
	/// thread and which only queues.
	/// </summary>
	internal sealed class QARoot
	{
		internal const string ReportFile = "report.json";
		internal const string ScreenshotFile = "screenshot.png";
		const string DirectoryArgument = "-dexc-qa-directory";
		const string ResourceArgument = "-dexc-telemetry-resource";
		const int PendingCapacity = 64;
		const int StackInReport = 2000;
		const double SignatureFlushSeconds = 10;

		static QARoot s_Current;

		internal static QARoot Current => s_Current ?? (s_Current = new QARoot());

		sealed class Signature
		{
			internal string Kind;
			internal string Title;
			internal int Count;
			internal readonly List<string> Reports = new List<string>();
			internal string First;
			internal string Last;
		}

		struct LogLine
		{
			internal LogType Type;
			internal string Message;
			internal string Stack;
			internal string Time;
		}

		readonly List<IDexcQAContributor> m_Contributors = new List<IDexcQAContributor>();
		readonly List<IDexcQASink> m_Sinks = new List<IDexcQASink>();
		readonly Dictionary<string, Signature> m_Signatures = new Dictionary<string, Signature>(StringComparer.Ordinal);
		readonly object m_Lock = new object();
		readonly Queue<LogLine> m_Logs = new Queue<LogLine>();
		readonly Queue<LogLine> m_Pending = new Queue<LogLine>();
		DexcQAOptions m_Options;
		QADriver m_Driver;
		string m_Run;
		int m_Written;
		int m_PendingDropped;
		bool m_Dirty;
		double m_NextSignatureFlush;

		internal bool IsRunning => m_Options != null;

		internal string Folder { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetForPlayMode()
		{
			Reset();
		}

		/// <summary>A Player given a folder on the command line, as a workspace does, finds defects from its first frame.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
		static void StartFromCommandLine()
		{
			if (Argument(Environment.GetCommandLineArgs(), DirectoryArgument) != null && !Current.IsRunning)
			{
				Current.Start(new DexcQAOptions());
			}
		}

		internal static void Reset()
		{
			s_Current?.Stop();
			s_Current = null;
		}

		internal void Start(DexcQAOptions options)
		{
			if (IsRunning)
			{
				throw new InvalidOperationException("Dexc QA is already running.");
			}
			m_Options = options ?? new DexcQAOptions();
			Folder = Argument(Environment.GetCommandLineArgs(), DirectoryArgument)
				?? m_Options.Directory
				?? Path.Combine(Application.persistentDataPath, "QA");
			Directory.CreateDirectory(Folder);
			m_Run = Guid.NewGuid().ToString("N");
			m_Written = 0;
			m_Signatures.Clear();
			Application.logMessageReceivedThreaded += OnLog;
			if (Application.isPlaying)
			{
				var driver = new GameObject("DexcQA") { hideFlags = HideFlags.HideAndDontSave };
				UnityEngine.Object.DontDestroyOnLoad(driver);
				m_Driver = driver.AddComponent<QADriver>();
				m_Driver.Root = this;
			}
		}

		internal void Stop()
		{
			if (!IsRunning)
			{
				return;
			}
			Application.logMessageReceivedThreaded -= OnLog;
			Pump();
			Flush();
			if (m_Driver != null)
			{
				UnityEngine.Object.Destroy(m_Driver.gameObject);
			}
			m_Driver = null;
			m_Options = null;
		}

		internal void AddContributor(IDexcQAContributor contributor) => m_Contributors.Add(contributor);

		internal void RemoveContributor(IDexcQAContributor contributor) => m_Contributors.Remove(contributor);

		internal void AddSink(IDexcQASink sink) => m_Sinks.Add(sink);

		internal void RemoveSink(IDexcQASink sink) => m_Sinks.Remove(sink);

		/// <summary>From any thread: keeps the line, and queues an exception or error for the main thread to report.</summary>
		internal void OnLog(string message, string stack, LogType type)
		{
			lock (m_Lock)
			{
				var options = m_Options;
				if (options == null)
				{
					return;
				}
				var line = new LogLine { Type = type, Message = message, Stack = stack, Time = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
				m_Logs.Enqueue(line);
				while (m_Logs.Count > options.LogCapacity)
				{
					m_Logs.Dequeue();
				}
				var captured = (type == LogType.Exception && options.CaptureExceptions) || ((type == LogType.Error || type == LogType.Assert) && options.CaptureErrors);
				if (!captured)
				{
					return;
				}
				if (m_Pending.Count >= PendingCapacity)
				{
					m_PendingDropped++;
					return;
				}
				m_Pending.Enqueue(line);
			}
		}

		/// <summary>Once a frame: reports what was logged, and keeps the counts on disk now and then.</summary>
		internal void Pump()
		{
			if (!IsRunning)
			{
				return;
			}
			while (true)
			{
				LogLine line;
				lock (m_Lock)
				{
					if (m_Pending.Count == 0)
					{
						break;
					}
					line = m_Pending.Dequeue();
				}
				var exception = line.Type == LogType.Exception;
				Report(exception ? "exception" : "error", SignatureOf(line.Message, line.Stack, exception), FirstLine(line.Message), line.Stack);
			}
			if (m_Dirty && Time.realtimeSinceStartupAsDouble >= m_NextSignatureFlush)
			{
				Flush();
			}
		}

		/// <summary>Counts the defect, and writes it in full while its signature has had few.</summary>
		/// <returns>The id of the report, or null when it was only counted.</returns>
		internal string Report(string kind, string signature, string title, string detail)
		{
			if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(signature))
			{
				throw new ArgumentException("A report needs a kind and a signature.");
			}
			if (!IsRunning)
			{
				Start(null);
			}
			var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
			if (!m_Signatures.TryGetValue(signature, out var known))
			{
				known = new Signature { Kind = kind, Title = title, First = now };
				m_Signatures.Add(signature, known);
			}
			known.Count++;
			known.Last = now;
			m_Dirty = true;
			if (known.Reports.Count >= m_Options.DetailedReportsPerSignature || m_Written >= m_Options.MaximumReports)
			{
				return null;
			}
			m_Written++;
			var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + m_Run.Substring(0, 8) + "-" + m_Written.ToString("D4", CultureInfo.InvariantCulture);
			var folder = Path.Combine(Folder, id);
			Directory.CreateDirectory(folder);
			var report = new DexcQAReport(kind, signature, title, detail);
			var model = Model(id, report, known.Count, now);
			var errors = new List<object>();
			foreach (var contributor in m_Contributors.ToArray())
			{
				try
				{
					contributor.Contribute(report);
				}
				catch (Exception exception)
				{
					// Noted here, not logged: a log of an error would come back as a report.
					errors.Add(contributor.GetType().FullName + ": " + exception.Message);
				}
			}
			var fields = new List<KeyValuePair<string, object>>();
			foreach (var field in report.Fields)
			{
				fields.Add(new KeyValuePair<string, object>(field.Key, field.Value));
			}
			var attachments = new List<object>();
			foreach (var attachment in report.Attachments)
			{
				File.WriteAllBytes(Path.Combine(folder, attachment.Key), attachment.Value);
				attachments.Add(attachment.Key);
			}
			model.Add(new KeyValuePair<string, object>("fields", fields));
			model.Add(new KeyValuePair<string, object>("attachments", attachments));
			if (errors.Count > 0)
			{
				model.Add(new KeyValuePair<string, object>("contributorErrors", errors));
			}
			var screenshot = m_Options.Screenshot && m_Driver != null && !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
			model.Add(new KeyValuePair<string, object>("screenshot", screenshot ? "pending" : null));
			WriteReport(folder, model);
			if (screenshot)
			{
				m_Driver.StartCoroutine(Screenshot(folder, model));
			}
			known.Reports.Add(id);
			Flush();
			var info = new DexcQAReportInfo(id, kind, signature, title, folder);
			foreach (var sink in m_Sinks.ToArray())
			{
				try
				{
					sink.OnReportWritten(info);
				}
				catch (Exception exception)
				{
					Debug.LogWarning("Dexc QA: a sink failed: " + exception.Message);
				}
			}
			return id;
		}

		/// <summary>Writes the counts of every signature of the run, for the tools that gather the reports.</summary>
		internal void Flush()
		{
			if (Folder == null || m_Run == null)
			{
				return;
			}
			var signatures = new List<object>();
			foreach (var pair in m_Signatures)
			{
				signatures.Add(new List<KeyValuePair<string, object>>
				{
					Member("signature", pair.Key),
					Member("kind", pair.Value.Kind),
					Member("title", pair.Value.Title),
					Member("count", pair.Value.Count),
					Member("first", pair.Value.First),
					Member("last", pair.Value.Last),
					Member("reports", pair.Value.Reports.ToArray()),
				});
			}
			int dropped;
			lock (m_Lock)
			{
				dropped = m_PendingDropped;
			}
			var file = new List<KeyValuePair<string, object>>
			{
				Member("run", m_Run),
				Member("droppedLogs", dropped),
				Member("signatures", signatures),
			};
			try
			{
				var path = Path.Combine(Folder, "signatures-" + m_Run + ".json");
				File.WriteAllText(path + ".tmp", QAJson.Write(file));
				File.Copy(path + ".tmp", path, true);
				File.Delete(path + ".tmp");
				m_Dirty = false;
			}
			catch (IOException exception)
			{
				Debug.LogWarning("Dexc QA: " + exception.Message);
			}
			m_NextSignatureFlush = Time.realtimeSinceStartupAsDouble + SignatureFlushSeconds;
		}

		List<KeyValuePair<string, object>> Model(string id, DexcQAReport report, int occurrence, string now)
		{
			var model = new List<KeyValuePair<string, object>>
			{
				Member("id", id),
				Member("run", m_Run),
				Member("kind", report.Kind),
				Member("signature", report.Signature),
				Member("title", report.Title),
				Member("detail", report.Detail == null || report.Detail.Length <= StackInReport ? report.Detail : report.Detail.Substring(0, StackInReport)),
				Member("occurrence", occurrence),
				Member("time", now),
				Member("realtime", Time.realtimeSinceStartupAsDouble),
				Member("frame", Time.frameCount),
				Member("build", new List<KeyValuePair<string, object>>
				{
					Member("product", Application.productName),
					Member("version", Application.version),
					Member("unity", Application.unityVersion),
					Member("platform", Application.platform.ToString()),
					Member("development", Debug.isDebugBuild),
				}),
				Member("resource", Resource()),
			};
#if DEXC_QA_HAS_TELEMETRY
			var annotations = new List<KeyValuePair<string, object>>();
			foreach (var pair in DexcTelemetry.Annotations)
			{
				annotations.Add(Member(pair.Key, pair.Value));
			}
			model.Add(Member("annotations", annotations));
			model.Add(Member("location", DexcTelemetry.TryGetLocation(out var map, out var x, out var y, out var z)
				? new List<KeyValuePair<string, object>> { Member("map", map), Member("x", x), Member("y", y), Member("z", z) }
				: null));
			model.Add(Member("telemetry", new List<KeyValuePair<string, object>>
			{
				Member("running", DexcTelemetry.IsRunning),
				Member("folder", DexcTelemetry.Folder),
			}));
#endif
			model.Add(Member("logs", Logs()));
			model.Add(Member("diagnostics", Diagnostics()));
			if (m_Options.InspectorWatches)
			{
				model.Add(Member("inspector", QAInspector.Watches()));
			}
			return model;
		}

		/// <summary>The resource the workspace put on the command line: the build, the commit, the seed.</summary>
		static List<KeyValuePair<string, object>> Resource()
		{
			var resource = new List<KeyValuePair<string, object>>();
			var arguments = Environment.GetCommandLineArgs();
			for (var i = 0; i + 1 < arguments.Length; i++)
			{
				if (arguments[i] != ResourceArgument)
				{
					continue;
				}
				var pair = arguments[i + 1];
				var equals = pair.IndexOf('=');
				if (equals > 0)
				{
					resource.Add(Member(pair.Substring(0, equals), pair.Substring(equals + 1)));
				}
			}
			return resource;
		}

		List<object> Logs()
		{
			var lines = new List<object>();
			lock (m_Lock)
			{
				var skip = Math.Max(0, m_Logs.Count - m_Options.LogsInReport);
				foreach (var line in m_Logs)
				{
					if (skip-- > 0)
					{
						continue;
					}
					var withStack = line.Type != LogType.Log && line.Type != LogType.Warning;
					lines.Add(new List<KeyValuePair<string, object>>
					{
						Member("time", line.Time),
						Member("type", line.Type.ToString()),
						Member("message", line.Message),
						Member("stack", withStack && line.Stack != null && line.Stack.Length > 0 ? (line.Stack.Length > StackInReport ? line.Stack.Substring(0, StackInReport) : line.Stack) : null),
					});
				}
			}
			return lines;
		}

		/// <summary>What Dexc noted as wrong: definitions it could not use, commands that failed.</summary>
		static List<object> Diagnostics()
		{
			var diagnostics = new List<object>();
			var page = DebugDiagnostics.GetSummaries(new DebugDiagnosticQuery(DebugDiagnosticSourceFilter.All, DebugFeatureFilter.All, null, new DebugPageRequest(0, 20)));
			foreach (var summary in page.Summaries)
			{
				diagnostics.Add(new List<KeyValuePair<string, object>>
				{
					Member("severity", summary.Severity.ToString()),
					Member("feature", summary.Feature.ToString()),
					Member("code", summary.Code),
					Member("message", summary.Message),
					Member("identity", summary.Identity),
				});
			}
			return diagnostics;
		}

		IEnumerator Screenshot(string folder, List<KeyValuePair<string, object>> model)
		{
			yield return new WaitForEndOfFrame();
			string result = null;
			var texture = ScreenCapture.CaptureScreenshotAsTexture();
			try
			{
				File.WriteAllBytes(Path.Combine(folder, ScreenshotFile), texture.EncodeToPNG());
				result = ScreenshotFile;
			}
			catch (IOException exception)
			{
				Debug.LogWarning("Dexc QA: " + exception.Message);
			}
			finally
			{
				UnityEngine.Object.Destroy(texture);
			}
			model.RemoveAll(member => member.Key == "screenshot");
			model.Add(Member("screenshot", result));
			WriteReport(folder, model);
		}

		/// <summary>Through a temporary file, so that a tool never reads half a report.</summary>
		static void WriteReport(string folder, List<KeyValuePair<string, object>> model)
		{
			var path = Path.Combine(folder, ReportFile);
			File.WriteAllText(path + ".tmp", QAJson.Write(model), new UTF8Encoding(false));
			File.Copy(path + ".tmp", path, true);
			File.Delete(path + ".tmp");
		}

		/// <summary>The frames of the logging, which come before the place of the defect.</summary>
		static bool IsLogging(string frame)
		{
			return frame.StartsWith("UnityEngine.Debug", StringComparison.Ordinal) || frame.StartsWith("UnityEngine.Logger", StringComparison.Ordinal);
		}

		/// <summary>
		/// The frames of the interpreters of bots and evals. The code they run has no frame of its own; what comes after them
		/// is the host that called the bot, the same for every defect of a bot.
		/// </summary>
		static bool IsInterpreter(string frame)
		{
			return frame.StartsWith("System.Reflection.", StringComparison.Ordinal) || frame.StartsWith("ILRuntime.", StringComparison.Ordinal) || frame.StartsWith("Dexc.Autopilot.", StringComparison.Ordinal);
		}

		/// <summary>
		/// What the same defect has in common from one build to the next: the type of the exception, or the message of the
		/// error with its numbers taken out, and the first frames of the stack without their lines and addresses. An
		/// exception with no frame of its own, as one thrown in a bot, adds its message instead.
		/// </summary>
		internal static string SignatureOf(string message, string stack, bool exception)
		{
			var first = FirstLine(message);
			string head;
			if (exception)
			{
				var colon = first.IndexOf(':');
				head = "exception:" + (colon > 0 ? first.Substring(0, colon) : first);
			}
			else
			{
				head = "error:" + Regex.Replace(first.Length > 120 ? first.Substring(0, 120) : first, "[0-9]+", "#");
			}
			var frames = new List<string>();
			foreach (var raw in (stack ?? string.Empty).Split('\n'))
			{
				var line = raw.Trim();
				if (line.Length == 0 || IsLogging(line))
				{
					continue;
				}
				if (IsInterpreter(line))
				{
					break;
				}
				var at = line.IndexOf(" (at ", StringComparison.Ordinal);
				if (at >= 0)
				{
					line = line.Substring(0, at);
				}
				line = Regex.Replace(line, @" \[0x[0-9a-fA-F]+\]", string.Empty).Trim();
				frames.Add(line);
				if (frames.Count == 2)
				{
					break;
				}
			}
			if (frames.Count > 0)
			{
				return head + "@" + string.Join(";", frames);
			}
			return exception ? head + ":" + Regex.Replace(first.Length > 120 ? first.Substring(0, 120) : first, "[0-9]+", "#") : head;
		}

		static string FirstLine(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}
			var end = text.IndexOf('\n');
			return (end < 0 ? text : text.Substring(0, end)).TrimEnd('\r');
		}

		static KeyValuePair<string, object> Member(string key, object value)
		{
			return new KeyValuePair<string, object>(key, value);
		}

		static string Argument(string[] arguments, string name)
		{
			for (var i = 0; i + 1 < arguments.Length; i++)
			{
				if (arguments[i] == name)
				{
					return arguments[i + 1];
				}
			}
			return null;
		}
	}
}
