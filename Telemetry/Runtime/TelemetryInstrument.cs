using System;

namespace Dexc.Telemetry
{
	/// <summary>
	/// A metric. Its message is encoded once, when it is registered, with zeros for the values. All of those are fixed size,
	/// so an export copies the bytes and fills the values in, and recording only adds to a field. Main thread only.
	/// </summary>
	public abstract class TelemetryInstrument
	{
		const int NameLimit = 255;
		const int UnitLimit = 63;

		readonly TelemetryMetrics m_Owner;
		int m_StartTime;
		int m_Time;

		private protected TelemetryInstrument(TelemetryMetrics owner, string name, string unit)
		{
			m_Owner = owner;
			Name = name;
			Unit = unit;
		}

		public string Name { get; }
		public string Unit { get; }

		/// <summary>The encoded metric: its field in ScopeMetrics, length included.</summary>
		internal byte[] Template { get; private set; }

		private protected bool IsEnabled(TelemetryDetail detail)
		{
			return detail <= m_Owner.Detail;
		}

		/// <summary>Copies the metric into the writer and starts a new interval. Returns false when there is nothing to report.</summary>
		internal bool Export(ProtobufWriter writer, ulong startTime, ulong time)
		{
			if (!HasData)
			{
				return false;
			}
			var origin = writer.Position;
			writer.WriteRaw(Template, Template.Length);
			writer.PatchFixed64(origin + m_StartTime, startTime);
			writer.PatchFixed64(origin + m_Time, time);
			WriteValues(writer, origin);
			Clear();
			return true;
		}

		private protected abstract bool HasData { get; }

		private protected abstract void WriteValues(ProtobufWriter writer, int origin);

		private protected abstract void Clear();

		private protected void SetTemplate(ProtobufWriter writer, int startTime, int time)
		{
			Template = new byte[writer.Position];
			Buffer.BlockCopy(writer.Buffer, 0, Template, 0, writer.Position);
			m_StartTime = startTime;
			m_Time = time;
		}

		private protected static void WriteMetricHeader(ProtobufWriter writer, string name, string unit)
		{
			writer.WriteStringField(OtlpFields.MetricName, name);
			if (unit.Length > 0)
			{
				writer.WriteStringField(OtlpFields.MetricUnit, unit);
			}
		}

		/// <summary>Holds names to the rule of the OpenTelemetry metrics API, which backends rely on.</summary>
		internal static void ValidateName(string name)
		{
			if (string.IsNullOrEmpty(name) || name.Length > NameLimit || !IsLetter(name[0]))
			{
				throw new ArgumentException("A metric name starts with a letter and has at most " + NameLimit + " characters: " + name, nameof(name));
			}
			foreach (var character in name)
			{
				if (!IsLetter(character) && !(character >= '0' && character <= '9') && "_.-/".IndexOf(character) < 0)
				{
					throw new ArgumentException("A metric name has letters, digits and _ . - / only: " + name, nameof(name));
				}
			}
		}

		internal static void ValidateUnit(string unit)
		{
			if (unit == null || unit.Length > UnitLimit)
			{
				throw new ArgumentException("A unit has at most " + UnitLimit + " characters.", nameof(unit));
			}
			foreach (var character in unit)
			{
				if (character > 127)
				{
					throw new ArgumentException("A unit is ASCII only: " + unit, nameof(unit));
				}
			}
		}

		static bool IsLetter(char character)
		{
			return (character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z');
		}
	}
}
