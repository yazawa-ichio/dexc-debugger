using System;
using System.Diagnostics;

namespace Dexc.Telemetry
{
	/// <summary>
	/// How values spread over fixed buckets. Each export reports the values recorded since the last one, so the memory it
	/// takes does not depend on the export interval. Main thread only.
	/// </summary>
	public sealed class TelemetryHistogram : TelemetryInstrument
	{
		const int TemplateOverhead = 192;

		readonly double[] m_Bounds;
		readonly ulong[] m_Buckets;
		readonly int m_CountOffset;
		readonly int m_SumOffset;
		readonly int m_BucketsOffset;
		readonly int m_MinimumOffset;
		readonly int m_MaximumOffset;
		ulong m_Count;
		double m_Sum;
		double m_Minimum;
		double m_Maximum;

		internal TelemetryHistogram(TelemetryMetrics owner, string name, string unit, double[] bounds)
			: base(owner, name, unit)
		{
			m_Bounds = bounds;
			m_Buckets = new ulong[bounds.Length + 1];
			var writer = new ProtobufWriter(TemplateOverhead + name.Length + unit.Length + (bounds.Length * 2 + 1) * 8);
			var metric = writer.BeginMessage(OtlpFields.ScopeMetricsMetrics);
			WriteMetricHeader(writer, name, unit);
			var histogram = writer.BeginMessage(OtlpFields.MetricHistogram);
			var point = writer.BeginMessage(OtlpFields.HistogramDataPoints);
			var startTime = writer.WriteReservedFixed64Field(OtlpFields.HistogramPointStartTime);
			var time = writer.WriteReservedFixed64Field(OtlpFields.HistogramPointTime);
			m_CountOffset = writer.WriteReservedFixed64Field(OtlpFields.HistogramPointCount);
			m_SumOffset = writer.WriteReservedFixed64Field(OtlpFields.HistogramPointSum);
			m_BucketsOffset = writer.WriteReservedPackedFixed64Field(OtlpFields.HistogramPointBucketCounts, m_Buckets.Length);
			writer.WritePackedDoubleField(OtlpFields.HistogramPointExplicitBounds, bounds, bounds.Length);
			m_MinimumOffset = writer.WriteReservedFixed64Field(OtlpFields.HistogramPointMin);
			m_MaximumOffset = writer.WriteReservedFixed64Field(OtlpFields.HistogramPointMax);
			writer.EndMessage(point);
			writer.WriteVarintField(OtlpFields.HistogramAggregationTemporality, OtlpFields.TemporalityDelta);
			writer.EndMessage(histogram);
			writer.EndMessage(metric);
			SetTemplate(writer, startTime, time);
		}

		internal bool HasBounds(double[] bounds)
		{
			if (bounds.Length != m_Bounds.Length)
			{
				return false;
			}
			for (var i = 0; i < bounds.Length; i++)
			{
				if (bounds[i] != m_Bounds[i])
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Records a value. NaN is ignored: it would spoil the sum of the whole interval.</summary>
		[Conditional(TelemetrySymbols.DetailBasic), Conditional(TelemetrySymbols.DetailStandard), Conditional(TelemetrySymbols.DetailDetailed)]
		public void RecordBasic(double value)
		{
			Record(value, TelemetryDetail.Basic);
		}

		/// <inheritdoc cref="RecordBasic"/>
		[Conditional(TelemetrySymbols.DetailStandard), Conditional(TelemetrySymbols.DetailDetailed)]
		public void RecordStandard(double value)
		{
			Record(value, TelemetryDetail.Standard);
		}

		/// <inheritdoc cref="RecordBasic"/>
		[Conditional(TelemetrySymbols.DetailDetailed)]
		public void RecordDetailed(double value)
		{
			Record(value, TelemetryDetail.Detailed);
		}

		internal void Record(double value, TelemetryDetail detail)
		{
			if (double.IsNaN(value) || !IsEnabled(detail))
			{
				return;
			}
			m_Buckets[FindBucket(value)]++;
			if (m_Count == 0 || value < m_Minimum)
			{
				m_Minimum = value;
			}
			if (m_Count == 0 || value > m_Maximum)
			{
				m_Maximum = value;
			}
			m_Sum += value;
			m_Count++;
		}

		/// <summary>The first bucket whose upper bound is not below the value. OpenTelemetry buckets include their upper bound.</summary>
		int FindBucket(double value)
		{
			var low = 0;
			var high = m_Bounds.Length;
			while (low < high)
			{
				var middle = (low + high) >> 1;
				if (value <= m_Bounds[middle])
				{
					high = middle;
				}
				else
				{
					low = middle + 1;
				}
			}
			return low;
		}

		private protected override bool HasData => m_Count != 0;

		private protected override void WriteValues(ProtobufWriter writer, int origin)
		{
			writer.PatchFixed64(origin + m_CountOffset, m_Count);
			writer.PatchFixed64(origin + m_SumOffset, (ulong)BitConverter.DoubleToInt64Bits(m_Sum));
			writer.PatchFixed64(origin + m_MinimumOffset, (ulong)BitConverter.DoubleToInt64Bits(m_Minimum));
			writer.PatchFixed64(origin + m_MaximumOffset, (ulong)BitConverter.DoubleToInt64Bits(m_Maximum));
			for (var i = 0; i < m_Buckets.Length; i++)
			{
				writer.PatchFixed64(origin + m_BucketsOffset + i * 8, m_Buckets[i]);
			}
		}

		private protected override void Clear()
		{
			Array.Clear(m_Buckets, 0, m_Buckets.Length);
			m_Count = 0;
			m_Sum = 0;
		}
	}
}
