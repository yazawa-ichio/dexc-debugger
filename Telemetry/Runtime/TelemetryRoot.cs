using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The root of the running telemetry and the only mutable static variable in this package. Every other piece of state
	/// hangs off this instance, so replacing it resets the package completely. It stands apart from the root of Dexc because
	/// this package works without Dexc and stays in a build Dexc was removed from.
	/// </summary>
	internal sealed class TelemetryRoot
	{
		static TelemetryRoot s_Current;

		internal static TelemetryRoot Current => s_Current ?? (s_Current = new TelemetryRoot());

		/// <summary>The root if there is one, without making it: for the measured methods, which run in every game.</summary>
		internal static TelemetryRoot Existing => s_Current;

		internal readonly TelemetryMetrics Metrics;
		internal readonly TelemetryLogs Logs;
		internal readonly TelemetryTraces Traces;
		internal readonly TelemetryMethods Methods;

		TelemetryFrameWriter m_MetricsFrames;
		TelemetryFrameWriter m_LogFrames;
		TelemetryFrameWriter m_TraceFrames;
		readonly List<ITelemetryLogAdapter> m_Adapters = new List<ITelemetryLogAdapter>();
		readonly TelemetryCapture m_Capture;
		readonly TelemetryCounter m_DroppedFrames;
		TelemetryDriver m_Driver;
		TelemetrySources m_Sources;
		TelemetryFileSet m_Files;
		TelemetryUploader m_Uploader;
		double m_Interval;
		double m_NextExport;
		double m_RecordInterval;
		double m_NextRecordFlush;
		string m_Folder;
		bool m_RestartsInterval;
		bool m_IsInPlayerLoop;
		bool m_AnnotatesScene;

		internal bool IsRunning => m_MetricsFrames != null;

		internal bool IsCapturing => m_Capture.IsCapturing;

		internal bool IsUploading => m_Uploader != null;

		TelemetryRoot()
		{
			Metrics = new TelemetryMetrics();
			Traces = new TelemetryTraces(Metrics);
			Traces.WriteFailed = Fail;
			Logs = new TelemetryLogs(Metrics, Traces);
			m_Capture = new TelemetryCapture(Metrics, Traces);
			Methods = new TelemetryMethods(Metrics, Traces, m_Capture);
			m_DroppedFrames = Metrics.Counter("dexc.telemetry.frames.dropped", "{frame}");
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetForPlayMode()
		{
			Reset();
		}

		/// <summary>Closes the current root. The next one is made on first use: a game that never uses telemetry pays nothing.</summary>
		internal static void Reset()
		{
			s_Current?.Stop();
			s_Current = null;
		}

		/// <summary>Starts writing to the frames, which this root owns from here on. Without files a capture writes no Unity Profiler file.</summary>
		internal void Start(
			TelemetryFrameWriter metricsFrames,
			TelemetryFrameWriter logFrames,
			TelemetryFrameWriter traceFrames,
			TelemetryOptions options,
			IReadOnlyList<KeyValuePair<string, string>> resource,
			double now,
			TelemetryFileSet files = null
		)
		{
			m_Files = files;
			m_Run++;
			m_RestartsInterval = false;
			m_Capture.BeginRun();
			Metrics.SetResource(resource);
			Metrics.Detail = options.Detail;
			Logs.DefaultLevel = options.MinimumSeverity;
			Logs.IncludeFormattedMessage = options.IncludeFormattedMessage;
			m_MetricsFrames = metricsFrames;
			m_LogFrames = logFrames;
			m_TraceFrames = traceFrames;
			Traces.Begin(traceFrames, options.AmbientContext);
			if (options.CollectUnityLogs)
			{
				m_Adapters.Add(new UnityLogAdapter());
			}
			if (options.LogAdapters != null)
			{
				m_Adapters.AddRange(options.LogAdapters);
			}
			foreach (var adapter in m_Adapters)
			{
				adapter.Attach();
			}
			m_Interval = options.ExportIntervalSeconds;
			m_NextExport = now + m_Interval;
			m_RecordInterval = options.RecordFlushSeconds;
			m_NextRecordFlush = now + m_RecordInterval;
			m_Folder = files?.Folder ?? m_Folder;
			if (options.CollectUnityMetrics)
			{
				m_Sources = new TelemetrySources(Metrics, Logs, options);
			}
			if (options.AnnotateScene)
			{
				m_AnnotatesScene = true;
				Metrics.Annotations.Set(SceneAnnotation, SceneManager.GetActiveScene().name);
				SceneManager.activeSceneChanged += OnActiveSceneChanged;
			}
			if (Application.isPlaying)
			{
				var driver = new GameObject("DexcTelemetry") { hideFlags = HideFlags.HideAndDontSave };
				UnityEngine.Object.DontDestroyOnLoad(driver);
				m_Driver = driver.AddComponent<TelemetryDriver>();
				m_Driver.Root = this;
				TelemetryPlayerLoop.Install(OnPlayerLoop);
				m_IsInPlayerLoop = true;
			}
		}

		internal const string SceneAnnotation = "unity.scene";

		TelemetryHistogram m_LoadDuration;

		/// <summary>Counts the runs, so that a load of an earlier run does not end one of this run.</summary>
		int m_Run;

		/// <summary>Starts a load. Without telemetry running it measures nothing and leaves the frames alone.</summary>
		internal TelemetryLoad BeginLoad(string name, TelemetryLoadKind kind)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("A load needs a name.", nameof(name));
			}
			if (!IsRunning)
			{
				return new TelemetryLoad(this, name, kind, default, -1);
			}
			var span = Traces.Tracer(TelemetryCapture.TracerName).Start(TelemetryLoad.SpanName, default, TelemetryDetail.Basic);
			span.Set(TelemetryLoad.NameAttribute, name);
			span.Set(TelemetryLoad.KindAttribute, TelemetryLoad.KindName(kind));
			Metrics.LoadsInProgress++;
			return new TelemetryLoad(this, name, kind, span, m_Run);
		}

		internal void EndLoad(TelemetryLoad load, double milliseconds)
		{
			if (load.Run != m_Run || !IsRunning)
			{
				// Begun outside this run: before telemetry ran, or before a stop that closed it.
				return;
			}
			Metrics.LoadsInProgress--;
			m_LoadDuration = m_LoadDuration ?? Metrics.Histogram("dexc.load.duration", "ms", new double[] { 100, 250, 500, 1000, 2000, 3000, 5000, 10000, 20000, 30000, 60000 });
			m_LoadDuration.Series(TelemetryLoad.KindAttribute, TelemetryLoad.KindName(load.Kind), TelemetryLoad.NameAttribute, load.Name)
				.Record(milliseconds, TelemetryDetail.Basic);
		}

		void OnActiveSceneChanged(Scene previous, Scene next)
		{
			Metrics.Annotations.Set(SceneAnnotation, next.name);
		}

		/// <summary>Asks for a capture from the next frame boundary. False when not running or already capturing.</summary>
		internal bool Capture(int frames, TelemetryDetail detail, bool unityProfiler)
		{
			if (frames <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(frames), "A capture needs at least one frame.");
			}
			if (!IsRunning || m_Capture.IsCapturing)
			{
				return false;
			}
			m_Capture.Request(frames, detail, unityProfiler);
			return true;
		}

		internal void StopCapture()
		{
			m_Capture.RequestStop();
		}

		/// <summary>Starts sending the files of the folder. False when not running with files, or already sending.</summary>
		internal bool StartUpload(TelemetryUploadOptions options, double now)
		{
			if (!IsRunning || m_Files == null || m_Uploader != null)
			{
				return false;
			}
			m_Uploader = new TelemetryUploader(options, m_Files, Metrics, now);
			return true;
		}

		/// <summary>Stops sending. A request under way is left; it is sent again at the next start.</summary>
		internal void StopUpload()
		{
			m_Uploader?.Dispose();
			m_Uploader = null;
		}

		/// <summary>
		/// The application is paused: what is recorded goes to the writing thread, and nothing waits for it. Telemetry is not
		/// a crash report, so what an operating system that ends the paused application takes with it is let go.
		/// </summary>
		internal void Pause()
		{
			Flush();
		}

		void OnPlayerLoop()
		{
			OnFrame(Time.unscaledDeltaTime, Time.frameCount);
			Tick(Time.realtimeSinceStartupAsDouble);
		}

		/// <summary>Takes the numbers of the frame that just ended, at the boundary before the frame of this number.</summary>
		internal void OnFrame(double deltaSeconds, int frame = 0)
		{
			m_Sources?.OnFrame(deltaSeconds);
			Methods.OnFrame();
			if (!IsRunning || HasWriteFailed())
			{
				return;
			}
			try
			{
				Logs.Drain(m_LogFrames);
				Traces.Drain();
				Methods.Drain();
				if (m_Capture.IsActive)
				{
					if (m_Capture.NextFrame(frame))
					{
						m_RestartsInterval = true;
						Flush();
					}
					else
					{
						ExportMetrics(TelemetrySources.Timestamp());
					}
				}
				if (m_Capture.IsPending)
				{
					// What came before goes out first, so that the first frame of the capture stands alone.
					Flush();
					m_Capture.Begin(frame, m_Files);
				}
			}
			catch (IOException exception)
			{
				Fail(exception);
			}
		}

		/// <summary>Writes at the interval. A capture writes every frame instead, and the interval starts again after it.</summary>
		internal void Tick(double now)
		{
			m_Uploader?.Update(now);
			if (IsRunning && now >= m_NextRecordFlush)
			{
				m_NextRecordFlush = now + m_RecordInterval;
				FlushRecords();
			}
			if (!IsRunning || m_Capture.IsCapturing)
			{
				return;
			}
			if (m_RestartsInterval)
			{
				m_RestartsInterval = false;
				m_NextExport = now + m_Interval;
				return;
			}
			if (now >= m_NextExport)
			{
				m_NextExport = now + m_Interval;
				Flush();
			}
		}

		/// <summary>Writes what is recorded now. A failed write ends the output: telemetry never takes the game down.</summary>
		internal void Flush()
		{
			if (!IsRunning)
			{
				return;
			}
			if (HasWriteFailed())
			{
				return;
			}
			try
			{
				var started = TelemetrySources.Timestamp();
				Logs.Drain(m_LogFrames);
				Logs.Flush(m_LogFrames);
				Methods.Drain();
				Traces.Flush();
				m_DroppedFrames.Add(m_Files?.Writing.TakeDropped() ?? 0, TelemetryDetail.Basic);
				ExportMetrics(started);
			}
			catch (IOException exception)
			{
				Fail(exception);
			}
		}

		/// <summary>Writes the logs and spans recorded so far, without the metrics, whose interval is their own.</summary>
		void FlushRecords()
		{
			try
			{
				Logs.Drain(m_LogFrames);
				Logs.Flush(m_LogFrames);
				Methods.Drain();
				Traces.Flush();
			}
			catch (IOException exception)
			{
				Fail(exception);
			}
		}

		/// <summary>Writes everything and closes the files, which take their closed names, and goes on in new ones.</summary>
		internal void Seal()
		{
			Flush();
			if (!IsRunning)
			{
				return;
			}
			try
			{
				m_MetricsFrames.Rotate();
				m_LogFrames.Rotate();
				m_TraceFrames.Rotate();
			}
			catch (IOException exception)
			{
				Fail(exception);
			}
			WaitWritten();
			m_Uploader?.Kick();
		}

		/// <summary>Writes everything and has it reach the disk, at the end of the output.</summary>
		internal void Persist()
		{
			Flush();
			if (!IsRunning)
			{
				return;
			}
			try
			{
				m_MetricsFrames.FlushToDisk();
				m_LogFrames.FlushToDisk();
				m_TraceFrames.FlushToDisk();
			}
			catch (IOException exception)
			{
				Fail(exception);
			}
			WaitWritten();
		}

		/// <summary>
		/// Waits for the writing thread to finish what was asked. Only the calls that promise their data is in the files
		/// wait: an explicit flush, a seal, a stop, a pause. The periodic writes never do.
		/// </summary>
		internal void WaitWritten()
		{
			if (m_Files != null && !m_Files.Writing.Wait(TimeSpan.FromSeconds(WaitSeconds)))
			{
				Debug.LogWarning("Dexc Telemetry: the files were not written within " + WaitSeconds + " seconds.");
			}
			HasWriteFailed();
		}

		const int WaitSeconds = 5;

		/// <summary>Ends the output when the writing thread failed. True when it did.</summary>
		bool HasWriteFailed()
		{
			var error = m_Files?.Writing.TakeError();
			if (error == null)
			{
				return false;
			}
			Fail(error as IOException ?? new IOException(error.Message, error));
			return true;
		}

		/// <summary>The folder of the files: of the run, or of the last one. Null before the first run with files.</summary>
		internal string Folder => m_Folder;

		/// <summary>Whether the file is being written by this Player.</summary>
		internal bool IsOpen(string path)
		{
			return m_Files != null && m_Files.IsOpen(path);
		}

		void ExportMetrics(long started)
		{
			m_Sources?.BeforeExport();
			var bytes = Metrics.Export(m_MetricsFrames);
			m_Sources?.AfterExport(started, bytes);
		}

		/// <summary>Writes what is left and closes the output. A capture ends first, so that its spans are written.</summary>
		internal void Stop()
		{
			m_Capture.End();
			Persist();
			Close();
		}

		void Fail(IOException exception)
		{
			// Closed first: the log below must not come back into the output that just failed.
			Close();
			Debug.LogException(exception);
		}

		void Close()
		{
			foreach (var adapter in m_Adapters)
			{
				adapter.Detach();
			}
			m_Adapters.Clear();
			m_MetricsFrames?.Dispose();
			m_MetricsFrames = null;
			m_LogFrames?.Dispose();
			m_LogFrames = null;
			Traces.End();
			m_TraceFrames?.Dispose();
			m_TraceFrames = null;
			m_Sources?.Dispose();
			m_Sources = null;
			// A load open across a stop is not measured: its span is not written either.
			Metrics.LoadsInProgress = 0;
			if (m_AnnotatesScene)
			{
				SceneManager.activeSceneChanged -= OnActiveSceneChanged;
				Metrics.Annotations.Set(SceneAnnotation, null);
				m_AnnotatesScene = false;
			}
			m_Capture.End();
			StopUpload();
			// What the frames queued, their closing included, is written before the thread ends.
			m_Files?.Writing.Stop(TimeSpan.FromSeconds(WaitSeconds));
			m_Files = null;
			if (m_IsInPlayerLoop)
			{
				TelemetryPlayerLoop.Uninstall();
				m_IsInPlayerLoop = false;
			}
			if (m_Driver != null)
			{
				UnityEngine.Object.Destroy(m_Driver.gameObject);
			}
			m_Driver = null;
		}
	}
}
