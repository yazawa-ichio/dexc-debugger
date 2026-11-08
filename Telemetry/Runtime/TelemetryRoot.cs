using System.Collections.Generic;
using System.IO;
using UnityEngine;

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

		internal readonly TelemetryMetrics Metrics;
		internal readonly TelemetryLogs Logs;
		internal readonly TelemetryTraces Traces;

		TelemetryFrameWriter m_MetricsFrames;
		TelemetryFrameWriter m_LogFrames;
		TelemetryFrameWriter m_TraceFrames;
		readonly List<ITelemetryLogAdapter> m_Adapters = new List<ITelemetryLogAdapter>();
		TelemetryDriver m_Driver;
		TelemetrySources m_Sources;
		double m_Interval;
		double m_NextExport;

		internal bool IsRunning => m_MetricsFrames != null;

		TelemetryRoot()
		{
			Metrics = new TelemetryMetrics();
			Traces = new TelemetryTraces(Metrics);
			Traces.WriteFailed = Fail;
			Logs = new TelemetryLogs(Metrics, Traces);
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

		/// <summary>Starts writing to the frames, which this root owns from here on.</summary>
		internal void Start(
			TelemetryFrameWriter metricsFrames,
			TelemetryFrameWriter logFrames,
			TelemetryFrameWriter traceFrames,
			TelemetryOptions options,
			IReadOnlyList<KeyValuePair<string, string>> resource,
			double now
		)
		{
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
			if (options.CollectUnityMetrics)
			{
				m_Sources = new TelemetrySources(Metrics);
			}
			if (Application.isPlaying)
			{
				var driver = new GameObject("DexcTelemetry") { hideFlags = HideFlags.HideAndDontSave };
				Object.DontDestroyOnLoad(driver);
				m_Driver = driver.AddComponent<TelemetryDriver>();
				m_Driver.Root = this;
			}
		}

		/// <summary>Takes the numbers of the frame that just ended.</summary>
		internal void OnFrame(double deltaSeconds)
		{
			m_Sources?.OnFrame(deltaSeconds);
			if (!IsRunning)
			{
				return;
			}
			try
			{
				Logs.Drain(m_LogFrames);
				Traces.Drain();
			}
			catch (IOException exception)
			{
				Fail(exception);
			}
		}

		internal void Tick(double now)
		{
			if (IsRunning && now >= m_NextExport)
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
			try
			{
				var started = TelemetrySources.Timestamp();
				m_Sources?.BeforeExport();
				Logs.Drain(m_LogFrames);
				Logs.Flush(m_LogFrames);
				Traces.Flush();
				var bytes = Metrics.Export(m_MetricsFrames);
				m_Sources?.AfterExport(started, bytes);
			}
			catch (IOException exception)
			{
				Fail(exception);
			}
		}

		/// <summary>Writes what is left and closes the output.</summary>
		internal void Stop()
		{
			Flush();
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
			if (m_Driver != null)
			{
				Object.Destroy(m_Driver.gameObject);
			}
			m_Driver = null;
		}
	}
}
