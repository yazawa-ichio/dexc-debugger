using System;
using UnityEngine;

namespace Dexc.Telemetry
{
	/// <summary>
	/// Log records on their way to LogsData frames. <see cref="Add"/> and <see cref="OnUnityLog"/> are the one exception to
	/// main thread only in this package: Unity calls the log callback on the thread that logged. They only put the record
	/// in a queue of fixed size, and everything else happens on the main thread.
	/// </summary>
	internal sealed class TelemetryLogs
	{
		const int QueueCapacity = 1024;

		/// <summary>The frame buffer. When a record no longer fits, the frame is written and a new one begins.</summary>
		const int BufferSize = 64 * 1024;

		/// <summary>Characters kept of a body or a stack trace, so that three of them always fit a frame.</summary>
		const int FieldLimit = 4096;

		const long UnixEpochTicks = 621355968000000000;
		const ulong NanosecondsPerTick = 100;

		struct Entry
		{
			internal long Ticks;
			internal TelemetrySeverity Severity;
			internal string Body;
			internal string Stack;
			internal bool IsException;
		}

		readonly object m_Lock = new object();
		readonly TelemetryEnvelope m_Envelope;
		readonly TelemetryCounter m_DroppedCounter;
		readonly ProtobufWriter m_Writer = new ProtobufWriter(BufferSize);
		Entry[] m_Incoming = new Entry[QueueCapacity];
		Entry[] m_Draining = new Entry[QueueCapacity];
		int m_IncomingCount;
		int m_Dropped;
		int m_ResourceLogs;
		int m_ScopeLogs;
		int m_Records;

		internal TelemetryLogs(TelemetryMetrics metrics)
		{
			m_Envelope = metrics.Envelope;
			m_DroppedCounter = metrics.Counter("dexc.telemetry.logs.dropped", "{record}");
		}

		/// <summary>The lowest severity kept. It can only silence what the compile symbols kept.</summary>
		internal TelemetrySeverity Minimum { get; set; } = TelemetrySeverity.Info;

		/// <summary>Queues a record, on any thread. When the queue is full the record is dropped and counted.</summary>
		internal void Add(TelemetrySeverity severity, string body, string stack, bool isException)
		{
			if (severity < Minimum)
			{
				return;
			}
			var ticks = DateTime.UtcNow.Ticks;
			lock (m_Lock)
			{
				if (m_IncomingCount == QueueCapacity)
				{
					m_Dropped++;
					return;
				}
				ref var entry = ref m_Incoming[m_IncomingCount++];
				entry.Ticks = ticks;
				entry.Severity = severity;
				entry.Body = body;
				entry.Stack = stack;
				entry.IsException = isException;
			}
		}

		/// <summary>The callback of <see cref="Application.logMessageReceivedThreaded"/>.</summary>
		internal void OnUnityLog(string condition, string stack, LogType type)
		{
			switch (type)
			{
				case LogType.Log:
					Add(TelemetrySeverity.Info, condition, stack, false);
					break;
				case LogType.Warning:
					Add(TelemetrySeverity.Warning, condition, stack, false);
					break;
				case LogType.Exception:
					Add(TelemetrySeverity.Error, condition, stack, true);
					break;
				default:
					Add(TelemetrySeverity.Error, condition, stack, false);
					break;
			}
		}

		/// <summary>Encodes what is queued. An error goes out at once: the game may be about to end.</summary>
		internal void Drain(TelemetryFrameWriter frames)
		{
			int count;
			int dropped;
			lock (m_Lock)
			{
				count = m_IncomingCount;
				dropped = m_Dropped;
				m_IncomingCount = 0;
				m_Dropped = 0;
				var swapped = m_Incoming;
				m_Incoming = m_Draining;
				m_Draining = swapped;
			}
			m_DroppedCounter.Add(dropped, TelemetryDetail.Basic);
			var urgent = false;
			for (var i = 0; i < count; i++)
			{
				ref var entry = ref m_Draining[i];
				Encode(entry, frames);
				urgent |= entry.Severity >= TelemetrySeverity.Error;
				entry.Body = null;
				entry.Stack = null;
			}
			if (urgent)
			{
				Flush(frames);
			}
		}

		/// <summary>Writes the records encoded so far.</summary>
		internal void Flush(TelemetryFrameWriter frames)
		{
			if (m_Records == 0)
			{
				return;
			}
			m_Writer.EndMessage(m_ScopeLogs);
			m_Writer.EndMessage(m_ResourceLogs);
			frames.WriteFrame(m_Writer.Buffer, m_Writer.Position);
			frames.Flush();
			m_Records = 0;
		}

		void Encode(in Entry entry, TelemetryFrameWriter frames)
		{
			if (m_Records == 0)
			{
				Begin();
			}
			var mark = m_Writer.Position;
			WriteRecord(entry);
			if (m_Writer.HasOverflowed)
			{
				m_Writer.Truncate(mark);
				Flush(frames);
				Begin();
				WriteRecord(entry);
			}
			m_Records++;
		}

		void Begin()
		{
			m_Writer.Reset();
			m_ResourceLogs = m_Writer.BeginMessage(OtlpFields.LogsDataResourceLogs);
			m_Writer.WriteRaw(m_Envelope.Resource, m_Envelope.Resource.Length);
			m_ScopeLogs = m_Writer.BeginMessage(OtlpFields.ResourceLogsScopeLogs);
			m_Writer.WriteRaw(m_Envelope.Scope, m_Envelope.Scope.Length);
		}

		void WriteRecord(in Entry entry)
		{
			var writer = m_Writer;
			var record = writer.BeginMessage(OtlpFields.ScopeLogsLogRecords);
			var time = (ulong)(entry.Ticks - UnixEpochTicks) * NanosecondsPerTick;
			writer.WriteFixed64Field(OtlpFields.LogRecordTime, time);
			writer.WriteFixed64Field(OtlpFields.LogRecordObservedTime, time);
			writer.WriteVarintField(OtlpFields.LogRecordSeverityNumber, (ulong)entry.Severity);
			writer.WriteStringField(OtlpFields.LogRecordSeverityText, SeverityText(entry.Severity));
			var body = writer.BeginMessage(OtlpFields.LogRecordBody);
			WriteLimited(writer, OtlpFields.AnyValueString, entry.Body);
			writer.EndMessage(body);
			if (entry.IsException)
			{
				WriteException(writer, entry);
			}
			else if (entry.Severity >= TelemetrySeverity.Warning && !string.IsNullOrEmpty(entry.Stack))
			{
				TelemetryEnvelope.WriteAttribute(writer, OtlpFields.LogRecordAttributes, "code.stacktrace", entry.Stack, 0, Limit(entry.Stack, 0));
			}
			writer.EndMessage(record);
		}

		/// <summary>Unity gives an exception as "Type: message". It becomes the attributes of the semantic conventions.</summary>
		static void WriteException(ProtobufWriter writer, in Entry entry)
		{
			var body = entry.Body;
			var separator = body.IndexOf(": ", StringComparison.Ordinal);
			var typeLength = separator < 0 ? Limit(body, 0) : Limit(body, 0, separator);
			TelemetryEnvelope.WriteAttribute(writer, OtlpFields.LogRecordAttributes, "exception.type", body, 0, typeLength);
			if (separator >= 0)
			{
				var start = separator + 2;
				TelemetryEnvelope.WriteAttribute(writer, OtlpFields.LogRecordAttributes, "exception.message", body, start, Limit(body, start));
			}
			if (!string.IsNullOrEmpty(entry.Stack))
			{
				TelemetryEnvelope.WriteAttribute(writer, OtlpFields.LogRecordAttributes, "exception.stacktrace", entry.Stack, 0, Limit(entry.Stack, 0));
			}
		}

		static void WriteLimited(ProtobufWriter writer, int field, string value)
		{
			var text = value ?? string.Empty;
			writer.WriteStringField(field, text, 0, Limit(text, 0));
		}

		static int Limit(string value, int start)
		{
			return Limit(value, start, value.Length - start);
		}

		/// <summary>The characters to keep, without cutting a surrogate pair in two.</summary>
		static int Limit(string value, int start, int length)
		{
			if (length <= FieldLimit)
			{
				return length;
			}
			return char.IsHighSurrogate(value[start + FieldLimit - 1]) ? FieldLimit - 1 : FieldLimit;
		}

		static string SeverityText(TelemetrySeverity severity)
		{
			switch (severity)
			{
				case TelemetrySeverity.Verbose:
					return "TRACE";
				case TelemetrySeverity.Debug:
					return "DEBUG";
				case TelemetrySeverity.Info:
					return "INFO";
				case TelemetrySeverity.Warning:
					return "WARN";
				case TelemetrySeverity.Error:
					return "ERROR";
				default:
					return "FATAL";
			}
		}
	}
}
