using System.Collections.Generic;
using System.Diagnostics;

namespace Dexc.Telemetry
{
	/// <summary>A total that only grows. Each export reports what was added since the last one. Main thread only.</summary>
	public sealed class TelemetryCounter : TelemetryInstrument
	{
		const int TemplateOverhead = 96;

		readonly int m_ValueOffset;
		long m_Value;

		internal TelemetryCounter(TelemetryMetrics owner, string name, string unit, KeyValuePair<string, string>[] attributes = null)
			: base(owner, name, unit, attributes)
		{
			var writer = new ProtobufWriter(TemplateOverhead + name.Length + unit.Length + AttributeBytes(attributes));
			var metric = writer.BeginMessage(OtlpFields.ScopeMetricsMetrics);
			WriteMetricHeader(writer, name, unit);
			var sum = writer.BeginMessage(OtlpFields.MetricSum);
			var point = writer.BeginMessage(OtlpFields.SumDataPoints);
			WriteAttributes(writer, OtlpFields.NumberPointAttributes, attributes);
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

		/// <summary>The series of this counter whose values have the attribute.</summary>
		/// <exception cref="System.ArgumentException">The key is empty or over 63 characters, or the value is null or over 255.</exception>
		/// <exception cref="System.InvalidOperationException">This is a series already.</exception>
		public TelemetryCounter Series(string key, string value)
		{
			return (TelemetryCounter)GetSeries(key, value, null, null);
		}

		/// <inheritdoc cref="Series(string, string)"/>
		public TelemetryCounter Series(string key1, string value1, string key2, string value2)
		{
			return (TelemetryCounter)GetSeries(key1, value1, key2, value2);
		}

		private protected override TelemetryInstrument CreateSeries(KeyValuePair<string, string>[] attributes)
		{
			return new TelemetryCounter(Owner, Name, Unit, attributes);
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
