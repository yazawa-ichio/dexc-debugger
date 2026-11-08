using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Dexc.Telemetry
{
	/// <summary>A value at a moment. Each export reports the last value set since the previous one. Main thread only.</summary>
	public sealed class TelemetryGauge : TelemetryInstrument
	{
		const int TemplateOverhead = 96;

		readonly int m_ValueOffset;
		double m_Value;
		bool m_HasValue;

		internal TelemetryGauge(TelemetryMetrics owner, string name, string unit, KeyValuePair<string, string>[] attributes = null)
			: base(owner, name, unit, attributes)
		{
			var writer = new ProtobufWriter(TemplateOverhead + name.Length + unit.Length + AttributeBytes(attributes));
			var metric = writer.BeginMessage(OtlpFields.ScopeMetricsMetrics);
			WriteMetricHeader(writer, name, unit);
			var gauge = writer.BeginMessage(OtlpFields.MetricGauge);
			var point = writer.BeginMessage(OtlpFields.GaugeDataPoints);
			WriteAttributes(writer, OtlpFields.NumberPointAttributes, attributes);
			var startTime = writer.WriteReservedFixed64Field(OtlpFields.NumberPointStartTime);
			var time = writer.WriteReservedFixed64Field(OtlpFields.NumberPointTime);
			m_ValueOffset = writer.WriteReservedFixed64Field(OtlpFields.NumberPointDouble);
			writer.EndMessage(point);
			writer.EndMessage(gauge);
			writer.EndMessage(metric);
			SetTemplate(writer, startTime, time);
		}

		/// <summary>The series of this gauge whose values have the attribute.</summary>
		/// <exception cref="ArgumentException">The key is empty or over 63 characters, or the value is null or over 255.</exception>
		/// <exception cref="InvalidOperationException">This is a series already.</exception>
		public TelemetryGauge Series(string key, string value)
		{
			return (TelemetryGauge)GetSeries(key, value, null, null);
		}

		/// <inheritdoc cref="Series(string, string)"/>
		public TelemetryGauge Series(string key1, string value1, string key2, string value2)
		{
			return (TelemetryGauge)GetSeries(key1, value1, key2, value2);
		}

		private protected override TelemetryInstrument CreateSeries(KeyValuePair<string, string>[] attributes)
		{
			return new TelemetryGauge(Owner, Name, Unit, attributes);
		}

		/// <summary>Sets the value.</summary>
		[Conditional(TelemetrySymbols.DetailBasic), Conditional(TelemetrySymbols.DetailStandard), Conditional(TelemetrySymbols.DetailDetailed)]
		public void SetBasic(double value)
		{
			Set(value, TelemetryDetail.Basic);
		}

		/// <inheritdoc cref="SetBasic"/>
		[Conditional(TelemetrySymbols.DetailStandard), Conditional(TelemetrySymbols.DetailDetailed)]
		public void SetStandard(double value)
		{
			Set(value, TelemetryDetail.Standard);
		}

		/// <inheritdoc cref="SetBasic"/>
		[Conditional(TelemetrySymbols.DetailDetailed)]
		public void SetDetailed(double value)
		{
			Set(value, TelemetryDetail.Detailed);
		}

		internal void Set(double value, TelemetryDetail detail)
		{
			if (IsEnabled(detail))
			{
				m_Value = value;
				m_HasValue = true;
			}
		}

		private protected override bool HasData => m_HasValue;

		private protected override void WriteValues(ProtobufWriter writer, int origin)
		{
			writer.PatchFixed64(origin + m_ValueOffset, (ulong)BitConverter.DoubleToInt64Bits(m_Value));
		}

		private protected override void Clear()
		{
			m_HasValue = false;
		}
	}
}
