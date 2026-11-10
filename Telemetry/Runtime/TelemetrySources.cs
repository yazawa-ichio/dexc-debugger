using System;
using System.Diagnostics;
using Unity.Profiling;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The metrics of Unity itself and of this package. A counter that this kind of Player does not provide stays invalid
	/// and its metric is simply not written: in a non-development Player that is the graphics memory and the per-frame
	/// allocation. Main thread only.
	/// </summary>
	internal sealed class TelemetrySources : IDisposable
	{
		const double MillisecondsPerSecond = 1000;
		const double MillisecondsPerNanosecond = 1e-6;

		readonly struct Sampled
		{
			internal readonly ProfilerRecorder Recorder;
			internal readonly TelemetryGauge Gauge;

			internal Sampled(ProfilerCategory category, string counter, TelemetryGauge gauge)
			{
				Recorder = ProfilerRecorder.StartNew(category, counter, 1);
				Gauge = gauge;
			}
		}

		readonly Sampled[] m_Memory;
		readonly TelemetryAnnotations m_Annotations;
		readonly TelemetryHistogram m_FrameTime;
		readonly TelemetryHistogram m_MainThreadTime;
		readonly TelemetryHistogram m_FrameAllocated;
		TelemetryHistogram m_FrameTimeNow;
		TelemetryHistogram m_MainThreadTimeNow;
		TelemetryHistogram m_FrameAllocatedNow;
		int m_AnnotationVersion = -1;
		readonly TelemetryMetrics m_Metrics;
		readonly TelemetryFrameEvents m_Events;
		readonly TelemetryCounter m_LoadFrames;
		readonly TelemetryCounter m_Collections;
		readonly TelemetryHistogram m_SourceTime;
		readonly TelemetryHistogram m_ExportTime;
		readonly TelemetryCounter m_ExportSize;
		ProfilerRecorder m_MainThread;
		ProfilerRecorder m_Allocated;
		int m_LastCollections;

		internal TelemetrySources(TelemetryMetrics metrics, TelemetryLogs logs = null, TelemetryOptions options = null)
		{
			if (logs != null && options != null && (options.HitchMilliseconds > 0 || options.PositionSeconds > 0))
			{
				m_Events = new TelemetryFrameEvents(metrics, logs, options);
			}
			m_Metrics = metrics;
			m_Annotations = metrics.Annotations;
			m_LoadFrames = metrics.Counter("dexc.load.frames", "{frame}");
			// 16.7 and 700 bound the slow and the frozen frames of Android vitals and Firebase. The bounds below 4 keep the
			// quantiles of light frames from spreading over a wide first bucket.
			var frameBounds = new[] { 1, 2, 3, 4, 8, 11.2, 16.7, 20, 25, 33.4, 50, 66.7, 100, 250, 700, 1000 };
			var costBounds = new[] { 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10 };
			m_FrameTime = metrics.Histogram("unity.frame.time", "ms", frameBounds);
			m_MainThreadTime = metrics.Histogram("unity.frame.main_thread.time", "ms", frameBounds);
			m_FrameAllocated = metrics.Histogram("unity.gc.frame_allocated", "By", new double[] { 0, 1024, 4096, 16384, 65536, 262144, 1048576, 4194304 });
			m_Collections = metrics.Counter("unity.gc.collections", "{collection}");
			m_SourceTime = metrics.Histogram("dexc.telemetry.source.time", "ms", costBounds);
			m_ExportTime = metrics.Histogram("dexc.telemetry.export.time", "ms", costBounds);
			m_ExportSize = metrics.Counter("dexc.telemetry.export.size", "By");
			m_MainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time", 1);
			m_Allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
			m_Memory = new[]
			{
				new Sampled(ProfilerCategory.Memory, "Total Used Memory", metrics.Gauge("unity.memory.used", "By")),
				new Sampled(ProfilerCategory.Memory, "Total Reserved Memory", metrics.Gauge("unity.memory.reserved", "By")),
				new Sampled(ProfilerCategory.Memory, "System Used Memory", metrics.Gauge("unity.memory.system.used", "By")),
				new Sampled(ProfilerCategory.Memory, "GC Used Memory", metrics.Gauge("unity.gc.used", "By")),
				new Sampled(ProfilerCategory.Memory, "GC Reserved Memory", metrics.Gauge("unity.gc.reserved", "By")),
				new Sampled(ProfilerCategory.Memory, "Gfx Used Memory", metrics.Gauge("unity.memory.gfx.used", "By")),
			};
			m_LastCollections = GC.CollectionCount(0);
		}

		/// <summary>Takes the numbers of the frame that just ended.</summary>
		internal void OnFrame(double deltaSeconds)
		{
			var started = Stopwatch.GetTimestamp();
			var milliseconds = deltaSeconds * MillisecondsPerSecond;
			if (m_Events != null)
			{
				var mainThread = m_MainThread.Valid && m_MainThread.LastValue > 0 ? m_MainThread.LastValue * MillisecondsPerNanosecond : double.NaN;
				m_Events.OnFrame(deltaSeconds, milliseconds, mainThread, m_Allocated.Valid ? m_Allocated.LastValue : -1, m_Metrics.LoadsInProgress > 0);
			}
			if (m_Metrics.LoadsInProgress > 0)
			{
				// A loading frame says how long the load takes, not how the game runs: the load has its own measure.
				m_LoadFrames.Add(1, TelemetryDetail.Basic);
				m_SourceTime.Record(Elapsed(started), TelemetryDetail.Standard);
				return;
			}
			if (m_AnnotationVersion != m_Annotations.Version)
			{
				// The series of what the game is doing now. Looked up only when that changes.
				m_AnnotationVersion = m_Annotations.Version;
				m_FrameTimeNow = m_Annotations.Apply(m_FrameTime);
				m_MainThreadTimeNow = m_Annotations.Apply(m_MainThreadTime);
				m_FrameAllocatedNow = m_Annotations.Apply(m_FrameAllocated);
			}
			m_FrameTimeNow.Record(milliseconds, TelemetryDetail.Basic);
			if (m_MainThread.Valid && m_MainThread.LastValue > 0)
			{
				m_MainThreadTimeNow.Record(m_MainThread.LastValue * MillisecondsPerNanosecond, TelemetryDetail.Basic);
			}
			if (m_Allocated.Valid)
			{
				m_FrameAllocatedNow.Record(m_Allocated.LastValue, TelemetryDetail.Standard);
			}
			m_SourceTime.Record(Elapsed(started), TelemetryDetail.Standard);
		}

		/// <summary>Takes what only has to be read once per export.</summary>
		internal void BeforeExport()
		{
			m_Events?.BeforeExport();
			foreach (var sampled in m_Memory)
			{
				// Before its first frame a recorder reads zero, which is not what the memory is.
				if (sampled.Recorder.Valid && sampled.Recorder.Count > 0)
				{
					sampled.Gauge.Set(sampled.Recorder.LastValue, TelemetryDetail.Basic);
				}
			}
			var collections = GC.CollectionCount(0);
			m_Collections.Add(collections - m_LastCollections, TelemetryDetail.Basic);
			m_LastCollections = collections;
		}

		/// <summary>Notes what an export cost. It goes out with the next export.</summary>
		internal void AfterExport(long started, int bytes)
		{
			m_ExportTime.Record(Elapsed(started), TelemetryDetail.Basic);
			m_ExportSize.Add(bytes, TelemetryDetail.Basic);
		}

		internal static long Timestamp()
		{
			return Stopwatch.GetTimestamp();
		}

		public void Dispose()
		{
			m_MainThread.Dispose();
			m_Allocated.Dispose();
			foreach (var sampled in m_Memory)
			{
				sampled.Recorder.Dispose();
			}
		}

		static double Elapsed(long started)
		{
			return (Stopwatch.GetTimestamp() - started) * MillisecondsPerSecond / Stopwatch.Frequency;
		}
	}
}
