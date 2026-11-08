using System;
using System.Collections.Generic;

namespace Dexc.Telemetry
{
	/// <summary>
	/// What the game is doing now, as one or two attributes: a scene, a stage, a mode. The metrics of each frame go to the
	/// series of the metric that has them, the way the annotations of Android Performance Tuner split its frame times. Few
	/// keys on purpose: each combination of values is a series, and series cost memory. Main thread only.
	/// </summary>
	internal sealed class TelemetryAnnotations
	{
		internal const int KeyLimit = 2;

		string m_Key1;
		string m_Value1;
		string m_Key2;
		string m_Value2;

		/// <summary>Grows on every change, so that a series in use is looked up again only then.</summary>
		internal int Version { get; private set; }

		internal int Count => m_Key1 == null ? 0 : m_Key2 == null ? 1 : 2;

		/// <summary>Sets the value of a key, or takes the key off with null.</summary>
		/// <exception cref="ArgumentException">The key or value is invalid.</exception>
		/// <exception cref="InvalidOperationException">Two other keys are set already.</exception>
		internal void Set(string key, string value)
		{
			if (value != null)
			{
				TelemetryInstrument.ValidateAttribute(key, value);
			}
			else if (string.IsNullOrEmpty(key))
			{
				throw new ArgumentException("An annotation needs a key.", nameof(key));
			}
			if (key == m_Key1)
			{
				if (value == m_Value1)
				{
					return;
				}
				if (value == null)
				{
					(m_Key1, m_Value1, m_Key2, m_Value2) = (m_Key2, m_Value2, null, null);
				}
				else
				{
					m_Value1 = value;
				}
			}
			else if (key == m_Key2)
			{
				if (value == m_Value2)
				{
					return;
				}
				m_Value2 = value;
				if (value == null)
				{
					m_Key2 = null;
				}
			}
			else if (value == null)
			{
				return;
			}
			else if (m_Key1 == null)
			{
				(m_Key1, m_Value1) = (key, value);
			}
			else if (m_Key2 == null)
			{
				// In key order, as the series order their keys.
				if (string.CompareOrdinal(key, m_Key1) < 0)
				{
					(m_Key1, m_Value1, m_Key2, m_Value2) = (key, value, m_Key1, m_Value1);
				}
				else
				{
					(m_Key2, m_Value2) = (key, value);
				}
			}
			else
			{
				throw new InvalidOperationException("At most " + KeyLimit + " annotations: " + m_Key1 + " and " + m_Key2 + " are set.");
			}
			Version++;
		}

		/// <summary>The series of the instrument for the annotations now, or the instrument itself without any.</summary>
		internal T Apply<T>(T instrument) where T : TelemetryInstrument
		{
			return m_Key1 == null ? instrument : (T)instrument.GetSeries(m_Key1, m_Value1, m_Key2, m_Value2);
		}

		/// <summary>The annotations now, for records that carry them as attributes.</summary>
		internal IReadOnlyList<KeyValuePair<string, string>> Pairs()
		{
			switch (Count)
			{
				case 0:
					return Array.Empty<KeyValuePair<string, string>>();
				case 1:
					return new[] { new KeyValuePair<string, string>(m_Key1, m_Value1) };
				default:
					return new[] { new KeyValuePair<string, string>(m_Key1, m_Value1), new KeyValuePair<string, string>(m_Key2, m_Value2) };
			}
		}
	}
}
