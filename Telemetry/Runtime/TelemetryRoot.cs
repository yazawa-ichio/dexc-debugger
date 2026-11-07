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

		internal readonly TelemetryMetrics Metrics = new TelemetryMetrics();

		TelemetryFrameWriter m_MetricsFrames;
		TelemetryDriver m_Driver;
		TelemetrySources m_Sources;
		double m_Interval;
		double m_NextExport;

		internal bool IsRunning => m_MetricsFrames != null;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetForPlayMode()
		{
			Reset();
		}

		/// <summary>Closes the current root and installs a fresh one.</summary>
		internal static void Reset()
		{
			s_Current?.Stop();
			s_Current = new TelemetryRoot();
		}

		/// <summary>Starts writing to the frames, which this root owns from here on.</summary>
		internal void Start(TelemetryFrameWriter metricsFrames, TelemetryOptions options, IReadOnlyList<KeyValuePair<string, string>> resource, double now)
		{
			Metrics.SetResource(resource);
			Metrics.Detail = options.Detail;
			m_MetricsFrames = metricsFrames;
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
				var bytes = Metrics.Export(m_MetricsFrames);
				m_Sources?.AfterExport(started, bytes);
			}
			catch (IOException exception)
			{
				Debug.LogException(exception);
				Close();
			}
		}

		/// <summary>Writes what is left and closes the output.</summary>
		internal void Stop()
		{
			Flush();
			Close();
		}

		void Close()
		{
			m_MetricsFrames?.Dispose();
			m_MetricsFrames = null;
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
