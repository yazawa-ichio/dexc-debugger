using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The open spans and the encoding of ended ones into TracesData frames. An open span is a slot of a fixed pool, so a
	/// span allocates nothing. The slots are made by the first span. Spans start on the main thread. Attributes and the end
	/// may come from the thread an await went on with: such an end only goes into a queue, and <see cref="Drain"/> encodes it
	/// on the main thread. The queue holds one end per slot at most, so it never fills.
	/// </summary>
	internal sealed class TelemetryTraces
	{
		const int SlotCapacity = 256;
		const int AttributeLimit = 4;
		const ulong SpanKindInternal = 1;
		const ulong StatusCodeError = 2;
		const long UnixEpochTicks = 621355968000000000;
		const ulong NanosecondsPerTick = 100;

		sealed class Slot
		{
			internal readonly string[] Keys = new string[AttributeLimit];
			internal readonly TelemetryValue[] Values = new TelemetryValue[AttributeLimit];
			internal int Generation;
			internal bool IsOpen;
			internal TelemetryTracer Tracer;
			internal string Name;
			internal TelemetryContext Context;
			internal TelemetryContext Parent;
			internal long StartTicks;
			internal int AttributeCount;
			internal int DroppedAttributes;
			internal bool HasError;
			internal string ErrorMessage;
			internal bool IsEnding;
			internal long EndTicks;
		}

		readonly TelemetryMetrics m_Metrics;
		readonly TelemetryBatch m_Batch;
		readonly TelemetryCounter m_Dropped;
		readonly Dictionary<string, TelemetryTracer> m_Tracers = new Dictionary<string, TelemetryTracer>(StringComparer.Ordinal);
		readonly AsyncLocal<TelemetryContext> m_Ambient = new AsyncLocal<TelemetryContext>();
		readonly object m_EndedLock = new object();
		Slot[] m_Slots;
		int[] m_Free;
		int m_FreeCount;
		int[] m_Ended;
		int[] m_Draining;
		int m_EndedCount;
		int m_MainThread;
		TelemetryFrameWriter m_Frames;
		bool m_UsesAmbient;
		ulong m_Random;

		internal TelemetryTraces(TelemetryMetrics metrics)
		{
			m_Metrics = metrics;
			m_Batch = new TelemetryBatch(metrics.Envelope);
			m_Dropped = metrics.Counter("dexc.telemetry.spans.dropped", "{span}");
		}

		/// <summary>Called when a write fails at the end of a span, where there is nobody to catch it.</summary>
		internal Action<IOException> WriteFailed { get; set; }

		/// <summary>The ambient context, on any thread. Default unless the ambient context is on.</summary>
		internal TelemetryContext Current => m_UsesAmbient ? m_Ambient.Value : default;

		internal TelemetryTracer Tracer(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("A tracer needs a name.", nameof(name));
			}
			if (!m_Tracers.TryGetValue(name, out var tracer))
			{
				tracer = new TelemetryTracer(this, name);
				m_Tracers.Add(name, tracer);
			}
			return tracer;
		}

		internal void Begin(TelemetryFrameWriter frames, bool usesAmbient)
		{
			// Ends that came in after the last run are released unwritten.
			Drain();
			m_MainThread = Environment.CurrentManagedThreadId;
			m_Frames = frames;
			m_UsesAmbient = usesAmbient;
			var seed = Guid.NewGuid().ToByteArray();
			m_Random = BitConverter.ToUInt64(seed, 0) | 1;
		}

		/// <summary>Stops recording. Spans still open are not written.</summary>
		internal void End()
		{
			m_Frames = null;
			m_UsesAmbient = false;
			Drain();
		}

		/// <summary>
		/// Encodes the ends that came from other threads and gives their slots back. Main thread. The count is read without
		/// the lock first: most frames have none, and an end that comes in meanwhile waits for the next frame.
		/// </summary>
		internal void Drain()
		{
			if (Volatile.Read(ref m_EndedCount) == 0)
			{
				return;
			}
			int count;
			lock (m_EndedLock)
			{
				count = m_EndedCount;
				m_EndedCount = 0;
				var swapped = m_Ended;
				m_Ended = m_Draining;
				m_Draining = swapped;
			}
			for (var i = 0; i < count; i++)
			{
				var index = m_Draining[i];
				var slot = m_Slots[index];
				Write(slot, slot.EndTicks);
				Release(index, slot);
			}
		}

		internal void Flush()
		{
			Drain();
			if (m_Frames != null)
			{
				m_Batch.Flush(m_Frames);
			}
		}

		internal TelemetrySpan Start(TelemetryTracer tracer, string name, TelemetryContext parent, TelemetryDetail detail)
		{
			if (m_Frames == null || detail > m_Metrics.Detail)
			{
				return default;
			}
			if (m_Slots == null)
			{
				MakeSlots();
			}
			if (m_FreeCount == 0)
			{
				m_Dropped.Add(1, TelemetryDetail.Basic);
				return default;
			}
			var ambient = Current;
			var effective = parent.IsValid ? parent : ambient;
			var index = m_Free[--m_FreeCount];
			var slot = m_Slots[index];
			slot.IsOpen = true;
			slot.Tracer = tracer;
			slot.Name = name;
			slot.Parent = effective;
			slot.Context = effective.IsValid
				? new TelemetryContext(effective.TraceHigh, effective.TraceLow, NextId())
				: new TelemetryContext(NextId(), NextId(), NextId());
			slot.StartTicks = DateTime.UtcNow.Ticks;
			if (m_UsesAmbient)
			{
				m_Ambient.Value = slot.Context;
			}
			return new TelemetrySpan(this, index, slot.Generation, slot.Context);
		}

		internal void SetAttribute(int index, int generation, string key, in TelemetryValue value)
		{
			var slot = Open(index, generation);
			if (slot == null)
			{
				return;
			}
			if (slot.AttributeCount == AttributeLimit)
			{
				slot.DroppedAttributes++;
				return;
			}
			slot.Keys[slot.AttributeCount] = key;
			slot.Values[slot.AttributeCount] = value;
			slot.AttributeCount++;
		}

		internal void SetError(int index, int generation, string message)
		{
			var slot = Open(index, generation);
			if (slot != null)
			{
				slot.HasError = true;
				slot.ErrorMessage = message;
			}
		}

		internal void End(int index, int generation)
		{
			var slot = Open(index, generation);
			if (slot == null)
			{
				return;
			}
			var ticks = DateTime.UtcNow.Ticks;
			if (m_UsesAmbient)
			{
				m_Ambient.Value = slot.Parent;
			}
			if (Environment.CurrentManagedThreadId != m_MainThread)
			{
				slot.EndTicks = ticks;
				slot.IsEnding = true;
				lock (m_EndedLock)
				{
					m_Ended[m_EndedCount++] = index;
				}
				return;
			}
			Write(slot, ticks);
			Release(index, slot);
		}

		/// <summary>The slot of a span still open. An end waiting in the queue counts as closed.</summary>
		Slot Open(int index, int generation)
		{
			var slot = m_Slots[index];
			return slot.IsOpen && !slot.IsEnding && slot.Generation == generation ? slot : null;
		}

		void Write(Slot slot, long endTicks)
		{
			if (m_Frames == null)
			{
				return;
			}
			try
			{
				Encode(slot, endTicks);
			}
			catch (IOException exception)
			{
				WriteFailed(exception);
			}
		}

		void Encode(Slot slot, long endTicks)
		{
			var scope = slot.Tracer;
			var mark = m_Batch.Enter(scope, scope.Scope);
			WriteSpan(slot, endTicks);
			if (m_Batch.HasOverflowed)
			{
				m_Batch.Rollback(mark);
				m_Batch.Flush(m_Frames);
				m_Batch.Enter(scope, scope.Scope);
				WriteSpan(slot, endTicks);
				if (m_Batch.HasOverflowed)
				{
					m_Dropped.Add(1, TelemetryDetail.Basic);
					return;
				}
			}
			m_Batch.Commit();
		}

		void WriteSpan(Slot slot, long endTicks)
		{
			var writer = m_Batch.Writer;
			var span = writer.BeginMessage(OtlpFields.ScopeSpansSpans);
			writer.WriteTraceIdField(OtlpFields.SpanTraceId, slot.Context.TraceHigh, slot.Context.TraceLow);
			writer.WriteSpanIdField(OtlpFields.SpanSpanId, slot.Context.SpanId);
			if (slot.Parent.IsValid)
			{
				writer.WriteSpanIdField(OtlpFields.SpanParentSpanId, slot.Parent.SpanId);
			}
			var name = slot.Name ?? string.Empty;
			writer.WriteStringField(OtlpFields.SpanName, name, 0, TelemetryLogs.Limit(name, 0, name.Length, TelemetryLogs.ValueLimit));
			writer.WriteVarintField(OtlpFields.SpanKind, SpanKindInternal);
			writer.WriteFixed64Field(OtlpFields.SpanStartTime, UnixNanoseconds(slot.StartTicks));
			writer.WriteFixed64Field(OtlpFields.SpanEndTime, UnixNanoseconds(endTicks));
			for (var i = 0; i < slot.AttributeCount; i++)
			{
				var key = slot.Keys[i] ?? string.Empty;
				TelemetryLogs.WriteAttribute(writer, OtlpFields.SpanAttributes, key, 0, key.Length, slot.Values[i]);
			}
			if (slot.DroppedAttributes > 0)
			{
				writer.WriteVarintField(OtlpFields.SpanDroppedAttributes, (ulong)slot.DroppedAttributes);
			}
			if (slot.HasError)
			{
				var status = writer.BeginMessage(OtlpFields.SpanStatus);
				var message = slot.ErrorMessage ?? string.Empty;
				writer.WriteStringField(OtlpFields.StatusMessage, message, 0, TelemetryLogs.Limit(message, 0, message.Length, TelemetryLogs.ValueLimit));
				writer.WriteVarintField(OtlpFields.StatusCode, StatusCodeError);
				writer.EndMessage(status);
			}
			writer.EndMessage(span);
		}

		void Release(int index, Slot slot)
		{
			slot.IsOpen = false;
			slot.IsEnding = false;
			slot.Generation++;
			slot.Tracer = null;
			slot.Name = null;
			slot.ErrorMessage = null;
			slot.HasError = false;
			slot.DroppedAttributes = 0;
			Array.Clear(slot.Keys, 0, slot.AttributeCount);
			Array.Clear(slot.Values, 0, slot.AttributeCount);
			slot.AttributeCount = 0;
			m_Free[m_FreeCount++] = index;
		}

		void MakeSlots()
		{
			m_Slots = new Slot[SlotCapacity];
			m_Free = new int[SlotCapacity];
			m_Ended = new int[SlotCapacity];
			m_Draining = new int[SlotCapacity];
			for (var i = 0; i < SlotCapacity; i++)
			{
				m_Slots[i] = new Slot();
				m_Free[i] = SlotCapacity - 1 - i;
			}
			m_FreeCount = SlotCapacity;
		}

		/// <summary>xorshift64*. An id needs to be unique, not secret, and must not be zero.</summary>
		ulong NextId()
		{
			m_Random ^= m_Random >> 12;
			m_Random ^= m_Random << 25;
			m_Random ^= m_Random >> 27;
			return m_Random * 2685821657736338717UL | 1;
		}

		static ulong UnixNanoseconds(long ticks)
		{
			return (ulong)(ticks - UnixEpochTicks) * NanosecondsPerTick;
		}
	}
}
