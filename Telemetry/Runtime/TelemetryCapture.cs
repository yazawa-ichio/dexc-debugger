using System.IO;
using UnityEngine.Profiling;

namespace Dexc.Telemetry
{
	/// <summary>
	/// A capture: a few frames recorded at a finer detail and written one by one, inside a span of its own with a span per
	/// frame. It starts and ends at the frame boundaries of the root. Main thread only.
	/// </summary>
	internal sealed class TelemetryCapture
	{
		const string TracerName = "Dexc.Telemetry";

		readonly TelemetryMetrics m_Metrics;
		readonly TelemetryTraces m_Traces;
		TelemetryTracer m_Tracer;
		int m_Requested;
		TelemetryDetail m_Detail;
		bool m_WantsProfiler;
		int m_Remaining;
		int m_Count;
		TelemetryDetail m_PreviousDetail;
		TelemetrySpan m_Capture;
		TelemetrySpan m_Frame;
		TelemetryFileSet m_Files;
		string m_ProfilerFile;
		bool m_ProfilerWasEnabled;

		internal TelemetryCapture(TelemetryMetrics metrics, TelemetryTraces traces)
		{
			m_Metrics = metrics;
			m_Traces = traces;
		}

		/// <summary>Asked for, and waiting for the next frame boundary.</summary>
		internal bool IsPending { get; private set; }

		/// <summary>Between its first and last frame boundary.</summary>
		internal bool IsActive { get; private set; }

		internal bool IsCapturing => IsPending || IsActive;

		/// <summary>Numbers the captures of a new run from one.</summary>
		internal void BeginRun()
		{
			m_Count = 0;
		}

		internal void Request(int frames, TelemetryDetail detail, bool unityProfiler)
		{
			m_Requested = frames;
			m_Detail = detail;
			m_WantsProfiler = unityProfiler;
			IsPending = true;
		}

		/// <summary>Drops a capture that has not begun. One that has ends at the next frame boundary.</summary>
		internal void RequestStop()
		{
			if (IsPending)
			{
				IsPending = false;
			}
			else if (IsActive)
			{
				m_Remaining = 1;
			}
		}

		/// <summary>Begins at a frame boundary. Without files there is no Unity Profiler file.</summary>
		internal void Begin(int frame, TelemetryFileSet files)
		{
			IsPending = false;
			IsActive = true;
			m_Remaining = m_Requested;
			m_Count++;
			m_PreviousDetail = m_Metrics.Detail;
			if (m_Detail > m_PreviousDetail)
			{
				m_Metrics.Detail = m_Detail;
			}
			m_Tracer = m_Tracer ?? m_Traces.Tracer(TracerName);
			m_Capture = m_Traces.Start(m_Tracer, "dexc.capture", default, TelemetryDetail.Basic, false);
			m_Capture.Set("dexc.capture.frames", m_Requested);
			m_Capture.Set("dexc.capture.detail", m_Detail.ToString());
			if (m_WantsProfiler && files != null)
			{
				var path = files.ProfilerPath(m_Count);
				if (BeginProfiler(path))
				{
					m_Files = files;
					m_ProfilerFile = path;
					m_Capture.Set("dexc.capture.profiler_file", Path.GetFileName(path));
				}
				else
				{
					files.Closed(path);
				}
			}
			m_Frame = StartFrame(frame);
		}

		/// <summary>Ends the frame that just ran and starts the next. True when that was the last, and the capture ended.</summary>
		internal bool NextFrame(int frame)
		{
			m_Frame.Dispose();
			m_Frame = default;
			if (--m_Remaining > 0)
			{
				m_Frame = StartFrame(frame);
				return false;
			}
			End();
			return true;
		}

		/// <summary>Ends now, after the last frame or when telemetry stops, and puts the detail back.</summary>
		internal void End()
		{
			IsPending = false;
			if (!IsActive)
			{
				return;
			}
			IsActive = false;
			m_Frame.Dispose();
			m_Frame = default;
			m_Capture.Dispose();
			m_Capture = default;
			m_Metrics.Detail = m_PreviousDetail;
			if (m_ProfilerFile != null)
			{
				EndProfiler();
				m_Files.Closed(m_ProfilerFile);
				m_Files.Trim();
				m_Files = null;
				m_ProfilerFile = null;
			}
		}

		TelemetrySpan StartFrame(int frame)
		{
			var span = m_Traces.Start(m_Tracer, "unity.frame", m_Capture.Context, TelemetryDetail.Basic, false);
			span.Set("unity.frame.number", frame);
			return span;
		}

		/// <summary>
		/// Logs the Unity Profiler to the file. The profiler exists only in the Editor and a Development Build, and a log that
		/// someone else started is left alone.
		/// </summary>
		bool BeginProfiler(string path)
		{
			if (!Profiler.supported || Profiler.enableBinaryLog)
			{
				return false;
			}
			m_ProfilerWasEnabled = Profiler.enabled;
			Profiler.logFile = path;
			Profiler.enableBinaryLog = true;
			Profiler.enabled = true;
			return true;
		}

		void EndProfiler()
		{
			Profiler.enabled = false;
			// An empty path closes the file, so that the Editor can open it, and turns the binary log off.
			Profiler.logFile = string.Empty;
			Profiler.enabled = m_ProfilerWasEnabled;
		}
	}
}
