using System;
using System.Globalization;
using System.Text;

namespace Dexc.Telemetry
{
	/// <summary>
	/// A value of a log attribute. Numbers, booleans and strings convert to it implicitly, without boxing, so a call that
	/// is not recorded costs nothing. Anything else goes through <see cref="Of"/>, and its text is only made when the record
	/// is written.
	/// </summary>
	public readonly struct TelemetryValue
	{
		enum Kind : byte
		{
			None,
			String,
			Integer,
			Double,
			Boolean,
			Object,
		}

		readonly Kind m_Kind;
		readonly long m_Number;
		readonly object m_Reference;

		TelemetryValue(Kind kind, long number, object reference)
		{
			m_Kind = kind;
			m_Number = number;
			m_Reference = reference;
		}

		public static implicit operator TelemetryValue(string value) => new TelemetryValue(Kind.String, 0, value);
		public static implicit operator TelemetryValue(int value) => new TelemetryValue(Kind.Integer, value, null);
		public static implicit operator TelemetryValue(long value) => new TelemetryValue(Kind.Integer, value, null);
		public static implicit operator TelemetryValue(float value) => new TelemetryValue(Kind.Double, BitConverter.DoubleToInt64Bits(value), null);
		public static implicit operator TelemetryValue(double value) => new TelemetryValue(Kind.Double, BitConverter.DoubleToInt64Bits(value), null);
		public static implicit operator TelemetryValue(bool value) => new TelemetryValue(Kind.Boolean, value ? 1 : 0, null);

		/// <summary>A value of any other type. A struct or an enum is boxed here, so guard such a call with <see cref="TelemetryLogger.IsEnabled"/>.</summary>
		public static TelemetryValue Of(object value)
		{
			return new TelemetryValue(Kind.Object, 0, value);
		}

		/// <summary>Writes the fields of an AnyValue message.</summary>
		internal void Write(ProtobufWriter writer)
		{
			switch (m_Kind)
			{
				case Kind.Integer:
					writer.WriteVarintField(OtlpFields.AnyValueInteger, (ulong)m_Number);
					break;
				case Kind.Double:
					writer.WriteDoubleField(OtlpFields.AnyValueDouble, BitConverter.Int64BitsToDouble(m_Number));
					break;
				case Kind.Boolean:
					writer.WriteVarintField(OtlpFields.AnyValueBoolean, (ulong)m_Number);
					break;
				default:
					var text = Text();
					writer.WriteStringField(OtlpFields.AnyValueString, text, 0, TelemetryLogs.Limit(text, 0, text.Length, TelemetryLogs.ValueLimit));
					break;
			}
		}

		internal void Append(StringBuilder builder, string format)
		{
			switch (m_Kind)
			{
				case Kind.Integer:
					builder.Append(m_Number.ToString(format, CultureInfo.InvariantCulture));
					break;
				case Kind.Double:
					builder.Append(BitConverter.Int64BitsToDouble(m_Number).ToString(format, CultureInfo.InvariantCulture));
					break;
				case Kind.Boolean:
					builder.Append(m_Number != 0);
					break;
				default:
					builder.Append(Text());
					break;
			}
		}

		/// <summary>The text of a string or an object. A ToString that throws must not take the output down with it.</summary>
		string Text()
		{
			try
			{
				return m_Reference?.ToString() ?? string.Empty;
			}
			catch (Exception exception)
			{
				return "<" + exception.GetType().Name + " in ToString>";
			}
		}
	}
}
