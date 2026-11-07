using System.Diagnostics;

namespace Dexc.Telemetry
{
	/// <summary>A total that only grows. Each export reports what was added since the last one. Main thread only.</summary>
	public sealed class TelemetryCounter : TelemetryInstrument
	{
		const int TemplateOverhead = 96;

		readonly int m_ValueOffset;
		long m_Value;

		internal TelemetryCounter(TelemetryMetrics owner, string name, string unit)
			: base(owner, name, unit)
		{
			var writer = new ProtobufWriter(TemplateOverhead + name.Length + unit.Length);
			var metric = writer.BeginMessage(OtlpFields.ScopeMetricsMetrics);
			WriteMetricHeader(writer, name, unit);
			var sum = writer.BeginMessage(OtlpFields.MetricSum);
			var point = writer.BeginMessage(OtlpFields.SumDataPoints);
			var startTime = writer.WriteReservedFixed64Field(OtlpFields.NumberPointStartTime);
			var time = writer.WriteReservedFixed64Field(OtlpFields.NumberPointTime);
			m_ValueOffset = writer.WriteReservedFixed64Field(OtlpFields.NumberPointInt);
			writer.EndMessage(point);
			writer.WriteVarintField(OtlpFields.SumAggregationTemporality, OtlpFields.TemporalityDelta);
			writer.WriteVarintField(OtlpFields.SumIsMonotonic, 1);
			writer.EndMessage(sum);
			writer.EndMessage(metric);
			SetTemplate(writer, startTime, time);
		}

		/// <summary>Adds to the total. A value below one is ignored: the total only grows.</summary>
		[Conditional(TelemetrySymbols.DetailBasic), Conditional(TelemetrySymbols.DetailStandard), Conditional(TelemetrySymbols.DetailDetailed)]
		public void AddBasic(long value)
		{
			Add(value, TelemetryDetail.Basic);
		}

		/// <inheritdoc cref="AddBasic"/>
		[Conditional(TelemetrySymbols.DetailStandard), Conditional(TelemetrySymbols.DetailDetailed)]
		public void AddStandard(long value)
		{
			Add(value, TelemetryDetail.Standard);
		}

		/// <inheritdoc cref="AddBasic"/>
		[Conditional(TelemetrySymbols.DetailDetailed)]
		public void AddDetailed(long value)
		{
			Add(value, TelemetryDetail.Detailed);
		}

		internal void Add(long value, TelemetryDetail detail)
		{
			if (value > 0 && IsEnabled(detail))
			{
				m_Value += value;
			}
		}

		private protected override bool HasData => m_Value != 0;

		private protected override void WriteValues(ProtobufWriter writer, int origin)
		{
			writer.PatchFixed64(origin + m_ValueOffset, (ulong)m_Value);
		}

		private protected override void Clear()
		{
			m_Value = 0;
		}
	}
}
