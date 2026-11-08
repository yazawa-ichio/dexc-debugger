using System;
using System.Collections.Generic;

namespace Dexc.Telemetry
{
	/// <summary>
	/// A metric. Its message is encoded once, when it is registered, with zeros for the values. All of those are fixed size,
	/// so an export copies the bytes and fills the values in, and recording only adds to a field. A series is the same
	/// metric with one or two attributes, encoded the same way with its own template. Main thread only.
	/// </summary>
	public abstract class TelemetryInstrument
	{
		const int NameLimit = 255;
		const int UnitLimit = 63;
		const int AttributeKeyLimit = 63;
		const int AttributeValueLimit = 255;

		/// <summary>The series one metric may have. Beyond them the values go to the series whose attributes say "other".</summary>
		internal const int SeriesLimit = 32;

		internal const string OtherValue = "other";

		readonly TelemetryMetrics m_Owner;
		readonly KeyValuePair<string, string>[] m_Attributes;
		Dictionary<(string, string, string, string), TelemetryInstrument> m_Series;
		int m_StartTime;
		int m_Time;

		private protected TelemetryInstrument(TelemetryMetrics owner, string name, string unit, KeyValuePair<string, string>[] attributes)
		{
			m_Owner = owner;
			Name = name;
			Unit = unit;
			m_Attributes = attributes;
		}

		public string Name { get; }
		public string Unit { get; }

		/// <summary>The attributes of a series. None for the metric itself.</summary>
		public IReadOnlyList<KeyValuePair<string, string>> Attributes => m_Attributes ?? Array.Empty<KeyValuePair<string, string>>();

		private protected TelemetryMetrics Owner => m_Owner;

		private protected KeyValuePair<string, string>[] SeriesAttributes => m_Attributes;

		/// <summary>
		/// The series with these attributes, made on first use. Keys are taken in order, so the same pair in another order is
		/// the same series. Looking up a series that exists does not allocate; keep the series anyway where it is recorded
		/// every frame.
		/// </summary>
		internal TelemetryInstrument GetSeries(string key1, string value1, string key2, string value2)
		{
			if (m_Attributes != null)
			{
				throw new InvalidOperationException("A series has no series of its own: " + Name);
			}
			if (key2 != null && string.CompareOrdinal(key1, key2) > 0)
			{
				(key1, value1, key2, value2) = (key2, value2, key1, value1);
			}
			var key = (key1, value1, key2, value2);
			if (m_Series != null && m_Series.TryGetValue(key, out var found))
			{
				return found;
			}
			ValidateAttribute(key1, value1);
			if (key2 != null)
			{
				ValidateAttribute(key2, value2);
				if (key1 == key2)
				{
					throw new ArgumentException("The two keys of a series are the same: " + key1, nameof(key2));
				}
			}
			m_Series = m_Series ?? new Dictionary<(string, string, string, string), TelemetryInstrument>();
			if (m_Series.Count >= SeriesLimit)
			{
				// Kept apart from the rest, so that memory does not grow with every value an attribute takes.
				key = (key1, OtherValue, key2, key2 == null ? null : OtherValue);
				if (m_Series.TryGetValue(key, out var other))
				{
					return other;
				}
			}
			var attributes = key.Item3 == null
				? new[] { new KeyValuePair<string, string>(key.Item1, key.Item2) }
				: new[] { new KeyValuePair<string, string>(key.Item1, key.Item2), new KeyValuePair<string, string>(key.Item3, key.Item4) };
			var series = CreateSeries(attributes);
			m_Owner.Register(series);
			m_Series.Add(key, series);
			return series;
		}

		private protected abstract TelemetryInstrument CreateSeries(KeyValuePair<string, string>[] attributes);

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

		/// <summary>What the attributes of a series may take in a template, at most.</summary>
		private protected static int AttributeBytes(KeyValuePair<string, string>[] attributes)
		{
			var bytes = 0;
			if (attributes != null)
			{
				foreach (var pair in attributes)
				{
					bytes += (pair.Key.Length + pair.Value.Length) * 3 + 16;
				}
			}
			return bytes;
		}

		/// <summary>The attributes of the data point of a series. Nothing for the metric itself.</summary>
		private protected static void WriteAttributes(ProtobufWriter writer, int field, KeyValuePair<string, string>[] attributes)
		{
			if (attributes == null)
			{
				return;
			}
			foreach (var pair in attributes)
			{
				TelemetryEnvelope.WriteAttribute(writer, field, pair.Key, pair.Value, 0, pair.Value.Length);
			}
		}

		internal static void ValidateAttribute(string key, string value)
		{
			if (string.IsNullOrEmpty(key) || key.Length > AttributeKeyLimit)
			{
				throw new ArgumentException("An attribute key has 1 to " + AttributeKeyLimit + " characters: " + key, nameof(key));
			}
			if (value == null || value.Length > AttributeValueLimit)
			{
				throw new ArgumentException("An attribute value has at most " + AttributeValueLimit + " characters.", nameof(value));
			}
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
