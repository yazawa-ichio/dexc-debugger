namespace Dexc.Telemetry
{
	/// <summary>The field numbers of the opentelemetry-proto messages this package writes.</summary>
	internal static class OtlpFields
	{
		internal const int MetricsDataResourceMetrics = 1;

		internal const int ResourceMetricsResource = 1;
		internal const int ResourceMetricsScopeMetrics = 2;

		internal const int ResourceAttributes = 1;

		internal const int ScopeMetricsScope = 1;
		internal const int ScopeMetricsMetrics = 2;

		internal const int ScopeName = 1;

		internal const int KeyValueKey = 1;
		internal const int KeyValueValue = 2;
		internal const int AnyValueString = 1;

		internal const int MetricName = 1;
		internal const int MetricUnit = 3;
		internal const int MetricGauge = 5;
		internal const int MetricSum = 7;
		internal const int MetricHistogram = 9;

		internal const int GaugeDataPoints = 1;

		internal const int SumDataPoints = 1;
		internal const int SumAggregationTemporality = 2;
		internal const int SumIsMonotonic = 3;

		internal const int HistogramDataPoints = 1;
		internal const int HistogramAggregationTemporality = 2;

		internal const int NumberPointStartTime = 2;
		internal const int NumberPointTime = 3;
		internal const int NumberPointDouble = 4;
		internal const int NumberPointInt = 6;

		internal const int HistogramPointStartTime = 2;
		internal const int HistogramPointTime = 3;
		internal const int HistogramPointCount = 4;
		internal const int HistogramPointSum = 5;
		internal const int HistogramPointBucketCounts = 6;
		internal const int HistogramPointExplicitBounds = 7;
		internal const int HistogramPointMin = 11;
		internal const int HistogramPointMax = 12;

		internal const ulong TemporalityDelta = 1;
	}
}
