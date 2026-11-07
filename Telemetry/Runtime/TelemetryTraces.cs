using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The open spans and the encoding of ended ones into TracesData frames. An open span is a slot of a fixed pool, so a
	/// span allocates nothing. The slots are made by the first span. Main thread only, except <see cref="Current"/>.
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
		}

		readonly TelemetryMetrics m_Metrics;
		readonly TelemetryBatch m_Batch;
		readonly TelemetryCounter m_Dropped;
		readonly Dictionary<string, TelemetryTracer> m_Tracers = new Dictionary<string, TelemetryTracer>(StringComparer.Ordinal);
		readonly AsyncLocal<TelemetryContext> m_Ambient = new AsyncLocal<TelemetryContext>();
		Slot[] m_Slots;
		int[] m_Free;
		int m_FreeCount;
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
		}

		internal void Flush()
		{
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
			if (m_UsesAmbient)
			{
				m_Ambient.Value = slot.Parent;
			}
			if (m_Frames != null)
			{
				try
				{
					Encode(slot, DateTime.UtcNow.Ticks);
				}
				catch (IOException exception)
				{
					WriteFailed(exception);
				}
			}
			Release(index, slot);
		}

		Slot Open(int index, int generation)
		{
			var slot = m_Slots[index];
			return slot.IsOpen && slot.Generation == generation ? slot : null;
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
