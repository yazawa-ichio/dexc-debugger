using System;
using System.Collections.Generic;

namespace Dexc.Telemetry
{
	/// <summary>The registered instruments and the encoding of what they hold into MetricsData frames.</summary>
	internal sealed class TelemetryMetrics
	{
		const long UnixEpochTicks = 621355968000000000;
		const ulong NanosecondsPerTick = 100;

		readonly List<TelemetryInstrument> m_Instruments = new List<TelemetryInstrument>();
		readonly Dictionary<string, TelemetryInstrument> m_ByName = new Dictionary<string, TelemetryInstrument>(StringComparer.Ordinal);
		ProtobufWriter m_Writer;
		int m_TemplateBytes;
		ulong m_IntervalStart;

		internal TelemetryMetrics()
		{
			m_IntervalStart = Now();
		}

		internal TelemetryEnvelope Envelope { get; } = new TelemetryEnvelope();

		/// <summary>The clock of every record of this root.</summary>
		internal TelemetryClock Clock { get; } = new TelemetryClock();

		/// <summary>The finest level recorded. Set below what the symbols keep, it silences calls that are compiled in.</summary>
		internal TelemetryDetail Detail { get; set; } = TelemetryDetail.Detailed;

		internal TelemetryCounter Counter(string name, string unit)
		{
			return Find<TelemetryCounter>(name, unit) ?? Add(new TelemetryCounter(this, name, unit));
		}

		internal TelemetryGauge Gauge(string name, string unit)
		{
			return Find<TelemetryGauge>(name, unit) ?? Add(new TelemetryGauge(this, name, unit));
		}

		internal TelemetryHistogram Histogram(string name, string unit, double[] bounds)
		{
			var copy = bounds == null ? DefaultBounds() : (double[])bounds.Clone();
			ValidateBounds(copy);
			var found = Find<TelemetryHistogram>(name, unit);
			if (found == null)
			{
				return Add(new TelemetryHistogram(this, name, unit, copy));
			}
			if (!found.HasBounds(copy))
			{
				throw new ArgumentException("The histogram is already registered with other bounds: " + name, nameof(bounds));
			}
			return found;
		}

		/// <summary>Sets what every frame says about where the data comes from.</summary>
		internal void SetResource(IReadOnlyList<KeyValuePair<string, string>> attributes)
		{
			Envelope.SetResource(attributes);
		}

		/// <summary>Writes what was recorded since the last export as one frame. Without anything recorded there is no frame.</summary>
		/// <returns>The size of the frame, or zero when there was none.</returns>
		internal int Export(TelemetryFrameWriter frames)
		{
			var size = TelemetryEnvelope.Overhead + Envelope.Resource.Length + Envelope.Scope.Length + m_TemplateBytes;
			if (m_Writer == null || m_Writer.Buffer.Length < size)
			{
				m_Writer = new ProtobufWriter(size);
			}
			var writer = m_Writer;
			var time = Now();
			writer.Reset();
			var resourceMetrics = writer.BeginMessage(OtlpFields.MetricsDataResourceMetrics);
			writer.WriteRaw(Envelope.Resource, Envelope.Resource.Length);
			var scopeMetrics = writer.BeginMessage(OtlpFields.ResourceMetricsScopeMetrics);
			writer.WriteRaw(Envelope.Scope, Envelope.Scope.Length);
			var hasData = false;
			for (var i = 0; i < m_Instruments.Count; i++)
			{
				hasData |= m_Instruments[i].Export(writer, m_IntervalStart, time);
			}
			writer.EndMessage(scopeMetrics);
			writer.EndMessage(resourceMetrics);
			m_IntervalStart = time;
			if (!hasData)
			{
				return 0;
			}
			frames.WriteFrame(writer.Buffer, writer.Position);
			frames.Flush();
			return writer.Position;
		}

		T Find<T>(string name, string unit) where T : TelemetryInstrument
		{
			TelemetryInstrument.ValidateName(name);
			TelemetryInstrument.ValidateUnit(unit);
			if (!m_ByName.TryGetValue(name, out var found))
			{
				return null;
			}
			if (!(found is T typed) || found.Unit != unit)
			{
				throw new ArgumentException("The metric is already registered as another kind or unit: " + name, nameof(name));
			}
			return typed;
		}

		T Add<T>(T instrument) where T : TelemetryInstrument
		{
			m_Instruments.Add(instrument);
			m_ByName.Add(instrument.Name, instrument);
			m_TemplateBytes += instrument.Template.Length;
			return instrument;
		}

		/// <summary>The default of the OpenTelemetry SDK, so that a histogram with no bounds of its own compares with others.</summary>
		static double[] DefaultBounds()
		{
			return new double[] { 0, 5, 10, 25, 50, 75, 100, 250, 500, 750, 1000, 2500, 5000, 7500, 10000 };
		}

		static void ValidateBounds(double[] bounds)
		{
			if (bounds.Length == 0)
			{
				throw new ArgumentException("A histogram needs at least one bound.", nameof(bounds));
			}
			for (var i = 0; i < bounds.Length; i++)
			{
				if (double.IsNaN(bounds[i]) || double.IsInfinity(bounds[i]) || (i > 0 && bounds[i] <= bounds[i - 1]))
				{
					throw new ArgumentException("Bounds are finite and strictly increasing.", nameof(bounds));
				}
			}
		}

		ulong Now()
		{
			return (ulong)(Clock.UtcTicks - UnixEpochTicks) * NanosecondsPerTick;
		}
	}
}
