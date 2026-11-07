using System;
using System.Collections.Generic;
using System.Text;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The loggers and their records on the way to LogsData frames. Records may come from any thread: Unity calls the log
	/// callback on the thread that logged, and loggers are safe to share. They only go into a queue of fixed size.
	/// <see cref="Drain"/> and <see cref="Flush"/> run on the main thread and do the rest.
	/// </summary>
	internal sealed class TelemetryLogs
	{
		const int QueueCapacity = 1024;

		/// <summary>The frame buffer. When a record no longer fits, the frame is written and a new one begins.</summary>
		const int BufferSize = 64 * 1024;

		/// <summary>Characters kept of a body, a message or a stack trace.</summary>
		const int FieldLimit = 4096;

		/// <summary>Characters kept of an attribute value.</summary>
		internal const int ValueLimit = 1024;

		const long UnixEpochTicks = 621355968000000000;
		const ulong NanosecondsPerTick = 100;

		internal struct Entry
		{
			internal TelemetryLogger Logger;
			internal long Ticks;
			internal TelemetrySeverity Severity;
			internal string Body;
			internal string Stack;
			internal Exception Exception;
			internal bool IsExceptionText;
			internal bool IsTemplate;
			internal int Count;
			internal TelemetryValue Value0;
			internal TelemetryValue Value1;
			internal TelemetryValue Value2;
			internal TelemetryValue Value3;

			internal readonly TelemetryValue Value(int index)
			{
				switch (index)
				{
					case 0:
						return Value0;
					case 1:
						return Value1;
					case 2:
						return Value2;
					default:
						return Value3;
				}
			}
		}

		readonly object m_Lock = new object();
		readonly TelemetryEnvelope m_Envelope;
		readonly TelemetryCounter m_DroppedCounter;
		readonly ProtobufWriter m_Writer = new ProtobufWriter(BufferSize);
		readonly StringBuilder m_Text = new StringBuilder();
		readonly Dictionary<string, TelemetryLogger> m_Loggers = new Dictionary<string, TelemetryLogger>(StringComparer.Ordinal);
		readonly Dictionary<string, TelemetrySeverity> m_Levels = new Dictionary<string, TelemetrySeverity>(StringComparer.Ordinal);
		TelemetrySeverity m_DefaultLevel = TelemetrySeverity.Info;
		Entry[] m_Incoming = new Entry[QueueCapacity];
		Entry[] m_Draining = new Entry[QueueCapacity];
		int m_IncomingCount;
		int m_Dropped;
		int m_ResourceLogs;
		int m_ScopeLogs;
		TelemetryLogCategory m_Scope;
		int m_Records;

		internal TelemetryLogs(TelemetryMetrics metrics)
		{
			m_Envelope = metrics.Envelope;
			m_DroppedCounter = metrics.Counter("dexc.telemetry.logs.dropped", "{record}");
		}

		/// <summary>Whether the body is the text with the values filled in, instead of the template.</summary>
		internal bool IncludeFormattedMessage { get; set; }

		/// <summary>The level of the categories without one of their own.</summary>
		internal TelemetrySeverity DefaultLevel
		{
			get
			{
				lock (m_Lock)
				{
					return m_DefaultLevel;
				}
			}
			set
			{
				lock (m_Lock)
				{
					m_DefaultLevel = value;
					ApplyLevels();
				}
			}
		}

		/// <summary>The logger of a category, made on first use.</summary>
		internal TelemetryLogger Logger(string category)
		{
			if (string.IsNullOrEmpty(category))
			{
				throw new ArgumentException("A logger needs a category.", nameof(category));
			}
			lock (m_Lock)
			{
				if (!m_Loggers.TryGetValue(category, out var logger))
				{
					logger = new TelemetryLogger(this, new TelemetryLogCategory(category, LevelOf(category)));
					m_Loggers.Add(category, logger);
				}
				return logger;
			}
		}

		/// <summary>Sets the level of a category and of those under it: <c>Game</c> also covers <c>Game.Stage</c>.</summary>
		internal void SetLevel(string category, TelemetrySeverity level)
		{
			lock (m_Lock)
			{
				m_Levels[category] = level;
				ApplyLevels();
			}
		}

		internal void ClearLevel(string category)
		{
			lock (m_Lock)
			{
				m_Levels.Remove(category);
				ApplyLevels();
			}
		}

		/// <summary>Queues a record. When the queue is full the record is dropped and counted.</summary>
		internal void Add(in Entry entry)
		{
			var ticks = DateTime.UtcNow.Ticks;
			lock (m_Lock)
			{
				if (m_IncomingCount == QueueCapacity)
				{
					m_Dropped++;
					return;
				}
				ref var slot = ref m_Incoming[m_IncomingCount++];
				slot = entry;
				slot.Ticks = ticks;
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
				entry = default;
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

		/// <summary>Writes a string attribute whose key is a part of <paramref name="key"/>.</summary>
		internal static void WriteAttribute(ProtobufWriter writer, string key, int start, int count, in TelemetryValue value)
		{
			var attribute = writer.BeginMessage(OtlpFields.LogRecordAttributes);
			writer.WriteStringField(OtlpFields.KeyValueKey, key, start, count);
			var any = writer.BeginMessage(OtlpFields.KeyValueValue);
			value.Write(writer);
			writer.EndMessage(any);
			writer.EndMessage(attribute);
		}

		/// <summary>The characters to keep, without cutting a surrogate pair in two.</summary>
		internal static int Limit(string value, int start, int length, int limit)
		{
			if (length <= limit)
			{
				return length;
			}
			return char.IsHighSurrogate(value[start + limit - 1]) ? limit - 1 : limit;
		}

		TelemetrySeverity LevelOf(string category)
		{
			var level = m_DefaultLevel;
			var longest = -1;
			foreach (var rule in m_Levels)
			{
				if (rule.Key.Length > longest && Covers(rule.Key, category))
				{
					longest = rule.Key.Length;
					level = rule.Value;
				}
			}
			return level;
		}

		void ApplyLevels()
		{
			foreach (var logger in m_Loggers.Values)
			{
				logger.CategoryState.SetLevel(LevelOf(logger.Category));
			}
		}

		static bool Covers(string rule, string category)
		{
			return category.StartsWith(rule, StringComparison.Ordinal) && (category.Length == rule.Length || category[rule.Length] == '.');
		}

		void Encode(in Entry entry, TelemetryFrameWriter frames)
		{
			if (m_Records == 0)
			{
				Begin();
			}
			var mark = m_Writer.Position;
			var scope = m_Scope;
			var scopeLogs = m_ScopeLogs;
			WriteInScope(entry);
			if (m_Writer.HasOverflowed)
			{
				m_Writer.Truncate(mark);
				m_Scope = scope;
				m_ScopeLogs = scopeLogs;
				Flush(frames);
				Begin();
				WriteInScope(entry);
				if (m_Writer.HasOverflowed)
				{
					// Larger than a whole frame. Only an adapter or many long values get here.
					m_DroppedCounter.Add(1, TelemetryDetail.Basic);
					return;
				}
			}
			m_Records++;
		}

		void Begin()
		{
			m_Writer.Reset();
			m_Scope = null;
			m_ResourceLogs = m_Writer.BeginMessage(OtlpFields.LogsDataResourceLogs);
			m_Writer.WriteRaw(m_Envelope.Resource, m_Envelope.Resource.Length);
		}

		/// <summary>Records of one category sit under one ScopeLogs. A change of category starts the next one.</summary>
		void WriteInScope(in Entry entry)
		{
			var category = entry.Logger.CategoryState;
			if (m_Scope != category)
			{
				if (m_Scope != null)
				{
					m_Writer.EndMessage(m_ScopeLogs);
				}
				m_ScopeLogs = m_Writer.BeginMessage(OtlpFields.ResourceLogsScopeLogs);
				m_Writer.WriteRaw(category.Scope, category.Scope.Length);
				m_Scope = category;
			}
			WriteRecord(entry);
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
			WriteBody(writer, entry);
			var attributes = entry.Logger.Attributes;
			writer.WriteRaw(attributes, attributes.Length);
			if (entry.IsTemplate)
			{
				WriteTemplateValues(writer, entry);
			}
			if (entry.Exception != null)
			{
				WriteException(writer, entry.Exception);
			}
			else if (entry.IsExceptionText)
			{
				WriteExceptionText(writer, entry);
			}
			else if (entry.Severity >= TelemetrySeverity.Warning && !string.IsNullOrEmpty(entry.Stack))
			{
				WriteText(writer, "code.stacktrace", entry.Stack, 0, entry.Stack.Length);
			}
			writer.EndMessage(record);
		}

		void WriteBody(ProtobufWriter writer, in Entry entry)
		{
			var text = entry.Body ?? string.Empty;
			if (entry.IsTemplate && IncludeFormattedMessage)
			{
				m_Text.Length = 0;
				TelemetryLogTemplate.Format(m_Text, text, entry);
				text = m_Text.ToString();
			}
			var body = writer.BeginMessage(OtlpFields.LogRecordBody);
			writer.WriteStringField(OtlpFields.AnyValueString, text, 0, Limit(text, 0, text.Length, FieldLimit));
			writer.EndMessage(body);
		}

		/// <summary>The names in the template become the keys. A value with no hole for it keeps its position as the key.</summary>
		static void WriteTemplateValues(ProtobufWriter writer, in Entry entry)
		{
			var template = entry.Body ?? string.Empty;
			var position = 0;
			var index = 0;
			while (index < entry.Count && TelemetryLogTemplate.NextHole(template, ref position, out var hole))
			{
				WriteAttribute(writer, template, hole.NameStart, hole.NameLength, entry.Value(index));
				index++;
			}
			for (; index < entry.Count; index++)
			{
				var key = PositionKey(index);
				WriteAttribute(writer, key, 0, key.Length, entry.Value(index));
			}
		}

		static void WriteException(ProtobufWriter writer, Exception exception)
		{
			var type = exception.GetType().Name;
			var message = exception.Message ?? string.Empty;
			var stack = exception.ToString();
			WriteText(writer, "exception.type", type, 0, type.Length);
			WriteText(writer, "exception.message", message, 0, message.Length);
			WriteText(writer, "exception.stacktrace", stack, 0, stack.Length);
		}

		/// <summary>An adapter may only have the text. Unity gives an exception as "Type: message".</summary>
		static void WriteExceptionText(ProtobufWriter writer, in Entry entry)
		{
			var body = entry.Body ?? string.Empty;
			var separator = body.IndexOf(": ", StringComparison.Ordinal);
			WriteText(writer, "exception.type", body, 0, separator < 0 ? body.Length : separator);
			if (separator >= 0)
			{
				WriteText(writer, "exception.message", body, separator + 2, body.Length - separator - 2);
			}
			if (!string.IsNullOrEmpty(entry.Stack))
			{
				WriteText(writer, "exception.stacktrace", entry.Stack, 0, entry.Stack.Length);
			}
		}

		static void WriteText(ProtobufWriter writer, string key, string value, int start, int length)
		{
			TelemetryEnvelope.WriteAttribute(writer, OtlpFields.LogRecordAttributes, key, value, start, Limit(value, start, length, FieldLimit));
		}

		static string PositionKey(int index)
		{
			switch (index)
			{
				case 0:
					return "arg0";
				case 1:
					return "arg1";
				case 2:
					return "arg2";
				default:
					return "arg3";
			}
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
