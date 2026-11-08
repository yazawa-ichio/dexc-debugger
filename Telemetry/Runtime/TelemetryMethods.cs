using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine.Profiling;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The calls of the methods the IL processing measures. A method with <see cref="TelemetryMethodAttribute"/> is recorded
	/// during a capture; one the profile chose is recorded while it is watched. Each recorded call is a span, a child of the
	/// measured call it runs in, or of the frame of a capture. Calls are thinned and capped per frame, and what is left out
	/// is counted. Calls on other threads, where asked for, go through a lock into a queue that the main thread writes.
	/// </summary>
	internal sealed class TelemetryMethods
	{
		const int StackCapacity = 64;
		const int QueueCapacity = 1024;
		const long NoAllocation = -1;
		const long AllocationsByRecorder = -2;

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

		struct Decision
		{
			internal int Version;
			internal bool IsWatched;
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
		readonly HashSet<string> m_WatchedNames = new HashSet<string>(StringComparer.Ordinal);
		readonly List<string> m_WatchedPrefixes = new List<string>();
		readonly Dictionary<string, Decision> m_Decisions = new Dictionary<string, Decision>(new SameString());
		TelemetryContext[] m_Stack;
		int m_Depth;
		int m_Frame;
		int m_WatchCount;
		int m_WatchVersion;
		Ended[] m_Ended;
		Ended[] m_Draining;
		int m_EndedCount;
		int m_OtherSkipped;
		int m_OtherDropped;
		int m_Pending;
		bool? m_CountsBytes;
		Recorder m_Allocations;

		internal TelemetryMethods(TelemetryMetrics metrics, TelemetryTraces traces, TelemetryCapture capture)
		{
			m_Metrics = metrics;
			m_Traces = traces;
			m_Capture = capture;
			m_Skipped = metrics.Counter("dexc.telemetry.method_calls.skipped", "{call}");
			m_Dropped = metrics.Counter("dexc.telemetry.method_calls.dropped", "{call}");
		}

		/// <summary>A new frame: the cap of each method starts again.</summary>
		internal void OnFrame()
		{
			m_Frame++;
		}

		/// <summary>A method with the attribute, recorded during a capture.</summary>
		internal TelemetryMethodProbe Enter(string scope, string name, TelemetryDetail detail, int every, int perFrame, bool anyThread, bool isAsync)
		{
			if (!m_Capture.IsActive || detail > m_Metrics.Detail)
			{
				return default;
			}
			return Record(scope, name, every, perFrame, anyThread, isAsync);
		}

		/// <summary>A method the profile chose, recorded while it is watched. With nothing watched it costs one test.</summary>
		internal TelemetryMethodProbe EnterWatched(string scope, string name, int every, int perFrame, bool anyThread, bool isAsync)
		{
			if (m_WatchCount == 0 || !IsWatched(name))
			{
				return default;
			}
			return Record(scope, name, every, perFrame, anyThread, isAsync);
		}

		/// <summary>The name of a method as the IL processing writes it: the full name of its type, a dot and the method.</summary>
		internal static string NameOf(MethodBase method)
		{
			var type = method.DeclaringType;
			if (type.IsGenericType && !type.IsGenericTypeDefinition)
			{
				type = type.GetGenericTypeDefinition();
			}
			return type.FullName.Replace('+', '.') + "." + method.Name;
		}

		/// <summary>Watches a method by its name, or the methods under a prefix that ends with <c>*</c>.</summary>
		internal void Watch(string pattern)
		{
			if (string.IsNullOrEmpty(pattern))
			{
				throw new ArgumentException("Name a method, or a prefix that ends with *.", nameof(pattern));
			}
			lock (m_Lock)
			{
				if (pattern.EndsWith("*", StringComparison.Ordinal))
				{
					var prefix = pattern.Substring(0, pattern.Length - 1);
					if (!m_WatchedPrefixes.Contains(prefix))
					{
						m_WatchedPrefixes.Add(prefix);
					}
				}
				else
				{
					m_WatchedNames.Add(pattern);
				}
				WatchChanged();
			}
		}

		internal void Unwatch(string pattern)
		{
			lock (m_Lock)
			{
				if (pattern.EndsWith("*", StringComparison.Ordinal))
				{
					m_WatchedPrefixes.Remove(pattern.Substring(0, pattern.Length - 1));
				}
				else
				{
					m_WatchedNames.Remove(pattern);
				}
				WatchChanged();
			}
		}

		internal void UnwatchAll()
		{
			lock (m_Lock)
			{
				m_WatchedNames.Clear();
				m_WatchedPrefixes.Clear();
				WatchChanged();
			}
		}

		void WatchChanged()
		{
			m_WatchCount = m_WatchedNames.Count + m_WatchedPrefixes.Count;
			m_WatchVersion++;
		}

		/// <summary>On the main thread the answer for each name is kept until the watched set changes.</summary>
		bool IsWatched(string name)
		{
			if (Environment.CurrentManagedThreadId != m_Traces.MainThread)
			{
				lock (m_Lock)
				{
					return Matches(name);
				}
			}
			if (m_Decisions.TryGetValue(name, out var decision) && decision.Version == m_WatchVersion)
			{
				return decision.IsWatched;
			}
			var watched = Matches(name);
			m_Decisions[name] = new Decision { Version = m_WatchVersion, IsWatched = watched };
			return watched;
		}

		bool Matches(string name)
		{
			if (m_WatchedNames.Contains(name))
			{
				return true;
			}
			foreach (var prefix in m_WatchedPrefixes)
			{
				if (name.StartsWith(prefix, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		TelemetryMethodProbe Record(string scope, string name, int every, int perFrame, bool anyThread, bool isAsync)
		{
			if (Environment.CurrentManagedThreadId != m_Traces.MainThread)
			{
				return anyThread ? EnterOtherThread(scope, name, every, perFrame) : default;
			}
			if (!m_Calls.TryGetValue(name, out var calls))
			{
				calls = new Calls { Tracer = m_Traces.Tracer(scope) };
				m_Calls.Add(name, calls);
			}
			if (!Admit(calls, every, perFrame, m_Frame, out var skipped))
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
			if (isAsync)
			{
				// An async call ends elsewhere, where the allocation of this thread says nothing about it.
				return new TelemetryMethodProbe(this, span.Index, span.Generation, true, NoAllocation);
			}
			m_Stack = m_Stack ?? new TelemetryContext[StackCapacity];
			if (m_Depth < StackCapacity)
			{
				m_Stack[m_Depth] = span.Context;
			}
			// Last, so that what this package does for the call is not counted as the allocation of the call.
			var allocation = BeginAllocation(m_Depth);
			m_Depth++;
			return new TelemetryMethodProbe(this, span.Index, span.Generation, false, allocation);
		}

		/// <summary>The end of a call. That of an async method may come on another thread, which the span takes care of.</summary>
		internal void Exit(int index, int generation, bool isAsync, long allocation)
		{
			if (!isAsync)
			{
				m_Depth--;
			}
			if (allocation >= 0)
			{
				m_Traces.SetAttribute(index, generation, "dexc.method.allocated_bytes", GC.GetAllocatedBytesForCurrentThread() - allocation);
			}
			else if (allocation == AllocationsByRecorder)
			{
				m_Allocations.enabled = false;
				m_Traces.SetAttribute(index, generation, "dexc.method.allocations", m_Allocations.sampleBlockCount);
			}
			m_Traces.End(index, generation);
		}

		/// <summary>
		/// The allocated bytes of this thread when the runtime counts them. Otherwise the Profiler counts the allocations,
		/// only around the outermost call, since one recorder cannot be nested. Each way is tried once with an allocation:
		/// in an IL2CPP Player the bytes never move, and outside a Development Build the recorder is valid but counts nothing.
		/// Without either, nothing is recorded rather than a zero.
		/// </summary>
		long BeginAllocation(int depth)
		{
			if (m_CountsBytes == null)
			{
				var before = GC.GetAllocatedBytesForCurrentThread();
				GC.KeepAlive(new byte[64]);
				m_CountsBytes = GC.GetAllocatedBytesForCurrentThread() != before;
				if (!m_CountsBytes.Value)
				{
					var recorder = Recorder.Get("GC.Alloc");
					if (recorder.isValid)
					{
						StartRecorder(recorder);
						GC.KeepAlive(new byte[64]);
						recorder.enabled = false;
						m_Allocations = recorder.sampleBlockCount > 0 ? recorder : null;
					}
				}
			}
			if (m_CountsBytes.Value)
			{
				return GC.GetAllocatedBytesForCurrentThread();
			}
			if (depth > 0 || m_Allocations == null)
			{
				return NoAllocation;
			}
			StartRecorder(m_Allocations);
			return AllocationsByRecorder;
		}

		/// <summary>Turning it off first clears what it held, as the allocation check of the Unity tests does.</summary>
		static void StartRecorder(Recorder recorder)
		{
			recorder.enabled = false;
			recorder.FilterToCurrentThread();
			recorder.enabled = true;
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
				if (!Admit(calls, every, perFrame, m_Frame, out var skipped))
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
			return new TelemetryMethodProbe(this, scope, name, m_Metrics.Clock.UtcTicks);
		}

		internal void ExitOtherThread(string scope, string name, long startTicks)
		{
			var ended = new Ended
			{
				Scope = scope,
				Name = name,
				StartTicks = startTicks,
				EndTicks = m_Metrics.Clock.UtcTicks,
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
