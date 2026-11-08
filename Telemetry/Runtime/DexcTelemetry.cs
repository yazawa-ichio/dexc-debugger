using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The entry to telemetry. Make the instruments once and record through them. What is recorded before
	/// <see cref="Start"/> is written by the first export after it. Main thread only.
	/// </summary>
	public static class DexcTelemetry
	{
		const string SdkName = "dexc-telemetry";

		/// <summary>True between <see cref="Start"/> and <see cref="Stop"/>, unless a write failed and ended the output.</summary>
		public static bool IsRunning => TelemetryRoot.Current.IsRunning;

		/// <summary>
		/// Opens new files for this run, one per signal, and starts writing to them periodically. The command line can set
		/// the folder with <c>-dexc-telemetry-directory</c> and add to the resource with <c>-dexc-telemetry-resource key=value</c>,
		/// over the options, as can <c>OTEL_RESOURCE_ATTRIBUTES</c> and <c>OTEL_SERVICE_NAME</c>.
		/// </summary>
		/// <returns>The path of the metrics file. The logs are beside it, named the same with <c>logs</c> for <c>metrics</c>.</returns>
		/// <exception cref="InvalidOperationException">It is already running.</exception>
		public static string Start(TelemetryOptions options = null)
		{
			options = options ?? new TelemetryOptions();
			if (options.ExportIntervalSeconds <= 0 || options.RecordFlushSeconds <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(options), "The export interval and the record flush must be above zero.");
			}
			var root = TelemetryRoot.Current;
			if (root.IsRunning)
			{
				throw new InvalidOperationException("Telemetry is already running.");
			}
			var arguments = Environment.GetCommandLineArgs();
			var directory = TelemetryLaunch.Directory(arguments) ?? options.Directory ?? Path.Combine(Application.persistentDataPath, "Telemetry");
			var run = Guid.NewGuid().ToString("N");
			var files = new TelemetryFileSet(
				directory,
				run,
				options.MaximumFileBytes,
				options.MaximumDirectoryBytes,
				options.MaximumPendingBytes,
				options.WhenWritingFallsBehind,
				options.BacklogWaitSeconds
			);
			var metrics = files.Open("metrics");
			var logs = files.Open("logs");
			var traces = files.Open("traces");
			root.Start(metrics, logs, traces, options, Resource(options, run, arguments), Time.realtimeSinceStartupAsDouble, files);
			return metrics.Path;
		}

		/// <summary>
		/// Captures the next frames: from the next frame on, records at <paramref name="detail"/> and writes the metrics every
		/// frame, inside a <c>dexc.capture</c> span with a <c>unity.frame</c> span per frame. Then the detail and the interval
		/// come back. It is a plain static call, so that an eval can make it. What the compile symbols removed stays removed.
		/// </summary>
		/// <param name="frames">How many frames.</param>
		/// <param name="detail">The detail during the capture. A finer detail already set stays.</param>
		/// <param name="unityProfiler">
		/// Also logs the Unity Profiler to <c>capture-&lt;run&gt;-&lt;n&gt;.raw</c> beside the telemetry, where it exists: the Editor
		/// and a Development Build. The Profiler window of the Editor opens it. It counts toward the limit of the folder.
		/// </param>
		/// <returns>False when telemetry is not running or a capture is already on.</returns>
		/// <exception cref="ArgumentOutOfRangeException">The frames are zero or fewer.</exception>
		public static bool Capture(int frames, TelemetryDetail detail = TelemetryDetail.Detailed, bool unityProfiler = false)
		{
			return TelemetryRoot.Current.Capture(frames, detail, unityProfiler);
		}

		/// <summary>True from <see cref="Capture"/> until its last frame.</summary>
		public static bool IsCapturing => TelemetryRoot.Current.IsCapturing;

		/// <summary>Ends the capture at the next frame. One that has not begun is dropped.</summary>
		public static void StopCapture()
		{
			TelemetryRoot.Current.StopCapture();
		}

		/// <summary>
		/// Records the calls of the methods the profile of the IL processing chose, while telemetry runs: each call is a span
		/// with its allocation. A method the profile did not choose has no measuring to turn on, and nothing happens. Only
		/// a build with <c>DEXC_TELEMETRY_WATCH</c> has the measuring at all.
		/// </summary>
		/// <param name="name">The full name of the type, a dot and the method, such as <c>Game.Stage.Load</c>, or a prefix that ends with <c>*</c>, such as <c>Game.Stage.*</c>.</param>
		public static void Watch(string name)
		{
			TelemetryRoot.Current.Methods.Watch(name);
		}

		/// <inheritdoc cref="Watch(string)"/>
		/// <param name="method">The method, as <c>typeof(Stage).GetMethod(nameof(Stage.Load))</c> gives it. Its overloads are watched with it.</param>
		public static void Watch(System.Reflection.MethodBase method)
		{
			TelemetryRoot.Current.Methods.Watch(TelemetryMethods.NameOf(method));
		}

		/// <inheritdoc cref="Watch(string)"/>
		/// <param name="method">A delegate to the method, such as <c>(Action)stage.Load</c>.</param>
		public static void Watch(Delegate method)
		{
			Watch(method.Method);
		}

		/// <summary>Stops what <see cref="Watch(string)"/> started with the same name or prefix.</summary>
		public static void Unwatch(string name)
		{
			TelemetryRoot.Current.Methods.Unwatch(name);
		}

		/// <inheritdoc cref="Unwatch(string)"/>
		public static void Unwatch(System.Reflection.MethodBase method)
		{
			TelemetryRoot.Current.Methods.Unwatch(TelemetryMethods.NameOf(method));
		}

		/// <inheritdoc cref="Unwatch(string)"/>
		public static void Unwatch(Delegate method)
		{
			Unwatch(method.Method);
		}

		/// <summary>Stops watching every method.</summary>
		public static void UnwatchAll()
		{
			TelemetryRoot.Current.Methods.UnwatchAll();
		}

		/// <summary>
		/// Starts sending the files to an OTLP/HTTP endpoint, as the Push rules of the specification say: every interval, when
		/// <see cref="Seal"/> closes files, and when the application is paused, without waiting there. What is sent is kept
		/// beside each file, so a restart does not send it twice, and a file sent whole is deleted. Use it or the taking of
		/// files (<see cref="Files"/>), not both: both delete what they took. The logs are not sent unless the options say so.
		/// It ends with <see cref="Stop"/>.
		/// </summary>
		/// <returns>False when telemetry is not running or is already sending.</returns>
		/// <exception cref="ArgumentException">The endpoint is missing, or a size or time is zero or less.</exception>
		public static bool StartUpload(TelemetryUploadOptions options)
		{
			if (options == null || string.IsNullOrEmpty(options.Endpoint))
			{
				throw new ArgumentException("The upload needs an endpoint.", nameof(options));
			}
			if (options.MaximumRequestBytes <= 0 || options.IntervalSeconds <= 0 || options.TimeoutSeconds <= 0)
			{
				throw new ArgumentException("The request size, the interval and the timeout must be above zero.", nameof(options));
			}
			return TelemetryRoot.Current.StartUpload(options, Time.realtimeSinceStartupAsDouble);
		}

		/// <summary>True between <see cref="StartUpload"/> and <see cref="StopUpload"/> or <see cref="Stop"/>.</summary>
		public static bool IsUploading => TelemetryRoot.Current.IsUploading;

		/// <summary>Stops sending. A request under way is left, and sent again from where it was at the next start.</summary>
		public static void StopUpload()
		{
			TelemetryRoot.Current.StopUpload();
		}

		/// <summary>Writes what is left and closes the file. Does nothing when not running.</summary>
		public static void Stop()
		{
			TelemetryRoot.Current.Stop();
		}

		/// <summary>Writes what is recorded now, without waiting for the interval, and waits until it is in the files.</summary>
		public static void Flush()
		{
			var root = TelemetryRoot.Current;
			root.Flush();
			root.WaitWritten();
		}

		/// <summary>
		/// Writes everything recorded and closes the files, which take their closed names, then goes on in new ones. Call it
		/// just before the files are taken, as from an eval. Does nothing when not running.
		/// </summary>
		public static void Seal()
		{
			TelemetryRoot.Current.Seal();
		}

		/// <summary>
		/// The telemetry files in the folder of the current or last run, or the default folder, oldest first. For taking them
		/// where the files cannot be reached directly. Main thread.
		/// </summary>
		public static IReadOnlyList<TelemetryFile> Files()
		{
			var folder = Folder;
			if (!Directory.Exists(folder))
			{
				return Array.Empty<TelemetryFile>();
			}
			return new DirectoryInfo(folder).GetFiles()
				.Where(file => TelemetryFileSet.IsOwn(file.Name))
				.OrderBy(file => file.LastWriteTimeUtc)
				.Select(file => new TelemetryFile(file.Name, file.Length, file.Name.EndsWith(TelemetryFileSet.OpenExtension, StringComparison.OrdinalIgnoreCase)))
				.ToList();
		}

		/// <summary>Reads part of a telemetry file into the buffer.</summary>
		/// <returns>How many bytes were read: zero at the end of the file.</returns>
		/// <exception cref="ArgumentException">The name is not a telemetry file of the folder.</exception>
		public static int ReadFile(string name, long offset, byte[] buffer)
		{
			using (var stream = new FileStream(PathOf(name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			{
				stream.Position = offset;
				return stream.Read(buffer, 0, buffer.Length);
			}
		}

		/// <summary>Deletes a telemetry file once it is taken. A file this Player is writing is not deleted.</summary>
		/// <returns>Whether it was deleted.</returns>
		/// <exception cref="ArgumentException">The name is not a telemetry file of the folder.</exception>
		public static bool DeleteFile(string name)
		{
			var path = PathOf(name);
			if (TelemetryRoot.Current.IsOpen(path) || !File.Exists(path))
			{
				return false;
			}
			File.Delete(path);
			return true;
		}

		/// <summary>The folder of the files: of the current run, or of the last one, or the default before any.</summary>
		public static string Folder => TelemetryRoot.Current.Folder ?? Path.Combine(Application.persistentDataPath, "Telemetry");

		static string PathOf(string name)
		{
			if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || !TelemetryFileSet.IsOwn(name))
			{
				throw new ArgumentException("Not a telemetry file: " + name, nameof(name));
			}
			return Path.Combine(Folder, name);
		}

		/// <summary>
		/// Says what the game is doing now, such as the stage or the mode, so that the metrics of each frame (the frame time,
		/// the main thread time, the allocation) are split by it into series. Null takes the key off. Set it when it changes,
		/// not every frame. Two keys at most: each combination of values is a series, up to 32 per metric, and the rest are
		/// counted as "other".
		/// </summary>
		/// <exception cref="ArgumentException">The key is empty or over 63 characters, or the value is over 255.</exception>
		/// <exception cref="InvalidOperationException">Two other keys are set already.</exception>
		public static void Annotate(string key, string value)
		{
			TelemetryRoot.Current.Metrics.Annotations.Set(key, value);
		}

		/// <summary>The counter of this name, made on first use.</summary>
		/// <exception cref="ArgumentException">The name or unit is invalid, or the name is taken by another kind or unit.</exception>
		public static TelemetryCounter Counter(string name, string unit = "")
		{
			return TelemetryRoot.Current.Metrics.Counter(name, unit);
		}

		/// <summary>The gauge of this name, made on first use.</summary>
		/// <exception cref="ArgumentException">The name or unit is invalid, or the name is taken by another kind or unit.</exception>
		public static TelemetryGauge Gauge(string name, string unit = "")
		{
			return TelemetryRoot.Current.Metrics.Gauge(name, unit);
		}

		/// <summary>The histogram of this name, made on first use.</summary>
		/// <param name="name">The name of the metric.</param>
		/// <param name="unit">The unit of the values.</param>
		/// <param name="bounds">The upper bounds of the buckets, increasing. By default those of the OpenTelemetry SDK.</param>
		/// <exception cref="ArgumentException">An argument is invalid, or the name is taken with another kind, unit or bounds.</exception>
		public static TelemetryHistogram Histogram(string name, string unit = "", double[] bounds = null)
		{
			return TelemetryRoot.Current.Metrics.Histogram(name, unit, bounds);
		}

		/// <summary>The tracer of this name, made on first use.</summary>
		/// <exception cref="ArgumentException">The name is empty.</exception>
		public static TelemetryTracer Tracer(string name)
		{
			return TelemetryRoot.Current.Traces.Tracer(name);
		}

		/// <summary>The current span, when <see cref="TelemetryOptions.AmbientContext"/> is on. Any thread.</summary>
		public static TelemetryContext CurrentContext => TelemetryRoot.Current.Traces.Current;

		/// <summary>The logger of a category, made on first use. Any thread.</summary>
		/// <exception cref="ArgumentException">The category is empty.</exception>
		public static TelemetryLogger Logger(string category)
		{
			return TelemetryRoot.Current.Logs.Logger(category);
		}

		/// <summary>
		/// The level of the categories without one of their own. It works on the calls the compile symbols kept, so a build
		/// that keeps Verbose can start at Info and collect Verbose later.
		/// </summary>
		public static TelemetrySeverity LogLevel
		{
			get => TelemetryRoot.Current.Logs.DefaultLevel;
			set => TelemetryRoot.Current.Logs.DefaultLevel = value;
		}

		/// <summary>Sets the level of a category and those under it: <c>Game</c> also covers <c>Game.Stage</c>. The longest match wins.</summary>
		public static void SetLogLevel(string category, TelemetrySeverity level)
		{
			TelemetryRoot.Current.Logs.SetLevel(category, level);
		}

		/// <summary>Removes what <see cref="SetLogLevel"/> set for this category.</summary>
		public static void ClearLogLevel(string category)
		{
			TelemetryRoot.Current.Logs.ClearLevel(category);
		}

		/// <summary>
		/// The resource of the run, a later layer winning for the same key: what Unity says of the game and the device, the
		/// options of the code, the environment variables of OpenTelemetry, and last the command line, which the tool that
		/// started this Player wrote. The id of the run and the name of the SDK are always these.
		/// </summary>
		static List<KeyValuePair<string, string>> Resource(TelemetryOptions options, string run, string[] arguments)
		{
			var unity = new[]
			{
				new KeyValuePair<string, string>("service.name", options.ServiceName ?? Application.productName),
				new KeyValuePair<string, string>("service.version", options.ServiceVersion ?? Application.version),
				new KeyValuePair<string, string>("device.model.identifier", SystemInfo.deviceModel),
				new KeyValuePair<string, string>("os.description", SystemInfo.operatingSystem),
			};
			var environment = TelemetryLaunch.EnvironmentResource(
				Environment.GetEnvironmentVariable(TelemetryLaunch.ResourceVariable),
				Environment.GetEnvironmentVariable(TelemetryLaunch.ServiceNameVariable)
			);
			var fixedPairs = new[]
			{
				new KeyValuePair<string, string>("service.instance.id", run),
				new KeyValuePair<string, string>("telemetry.sdk.name", SdkName),
			};
			return TelemetryLaunch.Merge(unity, options.ResourceAttributes, environment, TelemetryLaunch.CommandLineResource(arguments), fixedPairs);
		}
	}
}
