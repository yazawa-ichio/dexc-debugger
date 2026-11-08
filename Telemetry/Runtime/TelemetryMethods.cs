using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The calls of the methods the IL processing measures. During a capture each call is a span, a child of the measured
	/// call it runs in, or of the frame. Calls are thinned by <see cref="TelemetryMethodAttribute.Every"/> and capped by
	/// <see cref="TelemetryMethodAttribute.PerFrame"/>, and what is left out is counted. Calls on other threads, where asked
	/// for, go through a lock into a queue that the main thread writes.
	/// </summary>
	internal sealed class TelemetryMethods
	{
		const int StackCapacity = 64;
		const int QueueCapacity = 1024;

		sealed class Calls
		{
			internal TelemetryTracer Tracer;
			internal int Count;
			internal int Frame;
			internal int InFrame;
		}

		struct Ended
		{
			internal string Scope;
			internal string Name;
			internal long StartTicks;
			internal long EndTicks;
			internal int Thread;
		}

		/// <summary>The names come from string constants, which are the same object at every call.</summary>
		sealed class SameString : IEqualityComparer<string>
		{
			public bool Equals(string x, string y)
			{
				return ReferenceEquals(x, y);
			}

			public int GetHashCode(string value)
			{
				return RuntimeHelpers.GetHashCode(value);
			}
		}

		readonly TelemetryMetrics m_Metrics;
		readonly TelemetryTraces m_Traces;
		readonly TelemetryCapture m_Capture;
		readonly TelemetryCounter m_Skipped;
		readonly TelemetryCounter m_Dropped;
		readonly Dictionary<string, Calls> m_Calls = new Dictionary<string, Calls>(new SameString());
		readonly object m_Lock = new object();
		readonly Dictionary<string, Calls> m_OtherCalls = new Dictionary<string, Calls>(new SameString());
		TelemetryContext[] m_Stack;
		int m_Depth;
		Ended[] m_Ended;
		Ended[] m_Draining;
		int m_EndedCount;
		int m_OtherSkipped;
		int m_OtherDropped;
		int m_Pending;

		internal TelemetryMethods(TelemetryMetrics metrics, TelemetryTraces traces, TelemetryCapture capture)
		{
			m_Metrics = metrics;
			m_Traces = traces;
			m_Capture = capture;
			m_Skipped = metrics.Counter("dexc.telemetry.method_calls.skipped", "{call}");
			m_Dropped = metrics.Counter("dexc.telemetry.method_calls.dropped", "{call}");
		}

		internal TelemetryMethodProbe Enter(string scope, string name, TelemetryDetail detail, int every, int perFrame, bool anyThread)
		{
			if (!m_Capture.IsActive || detail > m_Metrics.Detail)
			{
				return default;
			}
			if (Environment.CurrentManagedThreadId != m_Traces.MainThread)
			{
				return anyThread ? EnterOtherThread(scope, name, every, perFrame) : default;
			}
			if (!m_Calls.TryGetValue(name, out var calls))
			{
				calls = new Calls { Tracer = m_Traces.Tracer(scope) };
				m_Calls.Add(name, calls);
			}
			if (!Admit(calls, every, perFrame, m_Capture.Frame, out var skipped))
			{
				(skipped ? m_Skipped : m_Dropped).Add(1, TelemetryDetail.Basic);
				return default;
			}
			var parent = m_Depth == 0 ? m_Capture.FrameContext : m_Stack[Math.Min(m_Depth, StackCapacity) - 1];
			var span = m_Traces.Start(calls.Tracer, name, parent, TelemetryDetail.Basic, false);
			if (!span.Context.IsValid)
			{
				return default;
			}
			if (every > 1)
			{
				span.Set("dexc.method.every", every);
			}
			m_Stack = m_Stack ?? new TelemetryContext[StackCapacity];
			if (m_Depth < StackCapacity)
			{
				m_Stack[m_Depth] = span.Context;
			}
			m_Depth++;
			return new TelemetryMethodProbe(this, span.Index, span.Generation);
		}

		internal void Exit(int index, int generation)
		{
			m_Depth--;
			m_Traces.End(index, generation);
		}

		/// <summary>Counts the call, and says whether it is recorded. A call left out is skipped by thinning or dropped by the cap.</summary>
		static bool Admit(Calls calls, int every, int perFrame, int frame, out bool skipped)
		{
			calls.Count++;
			skipped = every > 1 && calls.Count % every != 0;
			if (skipped)
			{
				return false;
			}
			if (calls.Frame != frame)
			{
				calls.Frame = frame;
				calls.InFrame = 0;
			}
			if (calls.InFrame >= perFrame)
			{
				return false;
			}
			calls.InFrame++;
			return true;
		}

		TelemetryMethodProbe EnterOtherThread(string scope, string name, int every, int perFrame)
		{
			lock (m_Lock)
			{
				if (!m_OtherCalls.TryGetValue(name, out var calls))
				{
					calls = new Calls();
					m_OtherCalls.Add(name, calls);
				}
				if (!Admit(calls, every, perFrame, m_Capture.Frame, out var skipped))
				{
					if (skipped)
					{
						m_OtherSkipped++;
					}
					else
					{
						m_OtherDropped++;
					}
					m_Pending++;
					return default;
				}
			}
			return new TelemetryMethodProbe(this, scope, name, DateTime.UtcNow.Ticks);
		}

		internal void ExitOtherThread(string scope, string name, long startTicks)
		{
			var ended = new Ended
			{
				Scope = scope,
				Name = name,
				StartTicks = startTicks,
				EndTicks = DateTime.UtcNow.Ticks,
				Thread = Environment.CurrentManagedThreadId,
			};
			lock (m_Lock)
			{
				if (m_Ended == null)
				{
					m_Ended = new Ended[QueueCapacity];
					m_Draining = new Ended[QueueCapacity];
				}
				if (m_EndedCount == QueueCapacity)
				{
					m_OtherDropped++;
				}
				else
				{
					m_Ended[m_EndedCount++] = ended;
				}
				m_Pending++;
			}
		}

		/// <summary>Writes the calls that ended on other threads. Main thread. Without any, it does not take the lock.</summary>
		internal void Drain()
		{
			if (Volatile.Read(ref m_Pending) == 0)
			{
				return;
			}
			int count;
			int skipped;
			int dropped;
			lock (m_Lock)
			{
				count = m_EndedCount;
				skipped = m_OtherSkipped;
				dropped = m_OtherDropped;
				m_EndedCount = 0;
				m_OtherSkipped = 0;
				m_OtherDropped = 0;
				m_Pending = 0;
				var swapped = m_Ended;
				m_Ended = m_Draining;
				m_Draining = swapped;
			}
			m_Skipped.Add(skipped, TelemetryDetail.Basic);
			m_Dropped.Add(dropped, TelemetryDetail.Basic);
			for (var i = 0; i < count; i++)
			{
				ref var ended = ref m_Draining[i];
				m_Traces.Record(m_Traces.Tracer(ended.Scope), ended.Name, m_Capture.FrameContext, ended.StartTicks, ended.EndTicks, "thread.id", ended.Thread);
				ended = default;
			}
		}
	}
}
