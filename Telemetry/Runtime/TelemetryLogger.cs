using System;

namespace Dexc.Telemetry
{
	/// <summary>
	/// Records logs of one category. Make it once with <see cref="DexcTelemetry.Logger"/> and keep it. Every method here can
	/// be called on any thread, and a call below the level of the category returns before anything is made.
	/// <para>
	/// The methods named after a severity are removed by the compile symbols, arguments included. A call the symbols kept
	/// can be silenced and brought back at run time with <see cref="DexcTelemetry.SetLogLevel"/>.
	/// </para>
	/// </summary>
	public sealed partial class TelemetryLogger
	{
		readonly TelemetryLogs m_Logs;
		readonly TelemetryLogCategory m_Category;

		internal TelemetryLogger(TelemetryLogs logs, TelemetryLogCategory category)
			: this(logs, category, Array.Empty<byte>())
		{
		}

		TelemetryLogger(TelemetryLogs logs, TelemetryLogCategory category, byte[] attributes)
		{
			m_Logs = logs;
			m_Category = category;
			Attributes = attributes;
		}

		public string Category => m_Category.Name;

		internal TelemetryLogCategory CategoryState => m_Category;

		/// <summary>The encoded attributes every record of this logger carries.</summary>
		internal byte[] Attributes { get; }

		/// <summary>Whether a record of this severity would be kept. Guard the calls that have to prepare a value with this.</summary>
		public bool IsEnabled(TelemetrySeverity severity)
		{
			return (int)severity >= m_Category.Level;
		}

		/// <summary>A logger of the same category and level whose records also carry this attribute. Make it once, not per call.</summary>
		public TelemetryLogger With(string key, TelemetryValue value)
		{
			if (string.IsNullOrEmpty(key))
			{
				throw new ArgumentException("An attribute needs a key.", nameof(key));
			}
			var writer = new ProtobufWriter(TelemetryEnvelope.Overhead * 2 + Attributes.Length + (key.Length + TelemetryLogs.ValueLimit) * 3);
			writer.WriteRaw(Attributes, Attributes.Length);
			TelemetryLogs.WriteAttribute(writer, key, 0, key.Length, value);
			var attributes = new byte[writer.Position];
			Buffer.BlockCopy(writer.Buffer, 0, attributes, 0, attributes.Length);
			return new TelemetryLogger(m_Logs, m_Category, attributes);
		}

		/// <summary>
		/// Takes a record from another logger, for an adapter. The message is plain text, so braces in it mean nothing.
		/// </summary>
		/// <param name="severity">The severity.</param>
		/// <param name="message">The text.</param>
		/// <param name="stack">The stack trace, if the other logger gives one as text.</param>
		/// <param name="isException">Whether the message is an exception in the form <c>Type: message</c>.</param>
		public void Emit(TelemetrySeverity severity, string message, string stack = null, bool isException = false)
		{
			if (IsEnabled(severity))
			{
				m_Logs.Add(new TelemetryLogs.Entry
				{
					Logger = this,
					Severity = severity,
					Body = message,
					Stack = stack,
					IsExceptionText = isException,
				});
			}
		}

		/// <summary>Takes a record with an exception object, for an adapter.</summary>
		public void Emit(TelemetrySeverity severity, Exception exception, string message)
		{
			if (IsEnabled(severity))
			{
				m_Logs.Add(new TelemetryLogs.Entry
				{
					Logger = this,
					Severity = severity,
					Body = message,
					Exception = exception,
				});
			}
		}

		void Write(
			TelemetrySeverity severity,
			Exception exception,
			string template,
			int count,
			in TelemetryValue value0 = default,
			in TelemetryValue value1 = default,
			in TelemetryValue value2 = default,
			in TelemetryValue value3 = default
		)
		{
			if (IsEnabled(severity))
			{
				m_Logs.Add(new TelemetryLogs.Entry
				{
					Logger = this,
					Severity = severity,
					Body = template,
					Exception = exception,
					IsTemplate = true,
					Count = count,
					Value0 = value0,
					Value1 = value1,
					Value2 = value2,
					Value3 = value3,
				});
			}
		}
	}
}
