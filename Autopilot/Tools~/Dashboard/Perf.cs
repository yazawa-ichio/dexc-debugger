using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Google.Protobuf.Collections;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace Dexc.Autopilot.Dashboard
{
	/// <summary>
	/// What the Players sent over OTLP/HTTP, kept in memory by build: the points of each series of each metric, the latest
	/// spans and logs, and apart from them the loads and the hitches, which a busy game would push out of the latest. The
	/// build is the dexc.build of the resource, which ws start puts there. Bounded by counts, oldest out first.
	/// </summary>
	internal sealed class Perf
	{
		public const int PointCapacity = 2000;
		public const int RecordCapacity = 500;

		/// <summary>Over the frame of a 60 Hz display, and the frozen frame of Android vitals and Firebase.</summary>
		public const double SlowFrameMilliseconds = 16.7;
		public const double FrozenFrameMilliseconds = 700;

		private sealed class Series
		{
			public string Name;
			public string Metric;
			public JsonObject Attributes;
			public string Unit;
			public string Kind;
			public double[] Bounds;
			public readonly Queue<JsonObject> Points = new Queue<JsonObject>();
		}

		private sealed class BuildPerf
		{
			public readonly Dictionary<string, Series> Metrics = new Dictionary<string, Series>(StringComparer.Ordinal);
			public readonly Queue<JsonObject> Spans = new Queue<JsonObject>();
			public readonly Queue<JsonObject> Logs = new Queue<JsonObject>();
			public readonly Queue<JsonObject> Loads = new Queue<JsonObject>();
			public readonly Queue<JsonObject> Hitches = new Queue<JsonObject>();
		}

		private readonly object m_Gate = new object();
		private readonly Dictionary<string, BuildPerf> m_Builds = new Dictionary<string, BuildPerf>(StringComparer.OrdinalIgnoreCase);

		/// <returns>The builds the request was about.</returns>
		public IReadOnlyList<string> Add(ExportMetricsServiceRequest request)
		{
			var builds = new List<string>();
			lock (m_Gate)
			{
				foreach (var resource in request.ResourceMetrics)
				{
					var build = Of(resource.Resource, builds);
					foreach (var metric in resource.ScopeMetrics.SelectMany(scope => scope.Metrics))
						Add(build, metric);
				}
			}

			return builds;
		}

		public IReadOnlyList<string> Add(ExportTraceServiceRequest request)
		{
			var builds = new List<string>();
			lock (m_Gate)
			{
				foreach (var resource in request.ResourceSpans)
				{
					var build = Of(resource.Resource, builds);
					foreach (var span in resource.ScopeSpans.SelectMany(scope => scope.Spans))
					{
						var record = new JsonObject
						{
							["name"] = span.Name,
							["start"] = Milliseconds(span.StartTimeUnixNano),
							["duration"] = (span.EndTimeUnixNano - span.StartTimeUnixNano) / 1e6,
							["trace"] = Hex(span.TraceId),
							["span"] = Hex(span.SpanId),
							["parent"] = span.ParentSpanId.IsEmpty ? null : Hex(span.ParentSpanId),
							["attributes"] = Attributes(span.Attributes),
						};
						Keep(build.Spans, record);
						if (span.Name == "dexc.load")
							Keep(build.Loads, (JsonObject)record.DeepClone());
					}
				}
			}

			return builds;
		}

		public IReadOnlyList<string> Add(ExportLogsServiceRequest request)
		{
			var builds = new List<string>();
			lock (m_Gate)
			{
				foreach (var resource in request.ResourceLogs)
				{
					var build = Of(resource.Resource, builds);
					foreach (var record in resource.ScopeLogs.SelectMany(scope => scope.LogRecords))
					{
						var entry = new JsonObject
						{
							["time"] = Milliseconds(record.TimeUnixNano != 0 ? record.TimeUnixNano : record.ObservedTimeUnixNano),
							["severity"] = record.SeverityText,
							["event"] = string.IsNullOrEmpty(record.EventName) ? null : record.EventName,
							["body"] = record.Body?.StringValue,
							["attributes"] = Attributes(record.Attributes),
						};
						Keep(build.Logs, entry);
						if (record.EventName == "dexc.hitch")
							Keep(build.Hitches, (JsonObject)entry.DeepClone());
					}
				}
			}

			return builds;
		}

		/// <summary>Each series of a build, with its metric, attributes, kind, unit and last point.</summary>
		public JsonArray Metrics(string build)
		{
			lock (m_Gate)
			{
				if (!m_Builds.TryGetValue(build, out var perf))
					return new JsonArray();
				return new JsonArray(perf.Metrics.Values.OrderBy(series => series.Name, StringComparer.Ordinal).Select(series => (JsonNode)new JsonObject
				{
					["name"] = series.Name,
					["metric"] = series.Metric,
					["attributes"] = series.Attributes.DeepClone(),
					["unit"] = series.Unit,
					["kind"] = series.Kind,
					["points"] = series.Points.Count,
					["last"] = series.Points.LastOrDefault()?.DeepClone(),
				}).ToArray());
			}
		}

		/// <summary>The points of one series since a time, in milliseconds since 1970.</summary>
		public JsonArray Points(string build, string name, double since)
		{
			lock (m_Gate)
			{
				if (!m_Builds.TryGetValue(build, out var perf) || !perf.Metrics.TryGetValue(name, out var series))
					return new JsonArray();
				return new JsonArray(series.Points.Where(point => point["time"].GetValue<double>() > since).Select(point => (JsonNode)point.DeepClone()).ToArray());
			}
		}

		/// <summary>
		/// The distribution of a histogram since a time, by build and series: the count, the mean, the percentiles and the
		/// share of slow and frozen frames, from the buckets of every point added up.
		/// </summary>
		public JsonArray Summary(string metric, double since)
		{
			lock (m_Gate)
			{
				var rows = new JsonArray();
				foreach (var build in m_Builds.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
				{
					foreach (var series in build.Value.Metrics.Values.Where(series => series.Metric == metric && series.Bounds != null).OrderBy(series => series.Name, StringComparer.Ordinal))
					{
						var distribution = Distribution.Of(series.Bounds, series.Points.Where(point => point["time"].GetValue<double>() > since));
						if (distribution.Count == 0)
							continue;
						rows.Add(new JsonObject
						{
							["build"] = build.Key,
							["series"] = series.Name,
							["attributes"] = series.Attributes.DeepClone(),
							["count"] = distribution.Count,
							["mean"] = distribution.Sum / distribution.Count,
							["p50"] = distribution.Quantile(0.5),
							["p95"] = distribution.Quantile(0.95),
							["p99"] = distribution.Quantile(0.99),
							["max"] = distribution.Maximum,
							["slow"] = distribution.ShareAbove(SlowFrameMilliseconds),
							["frozen"] = distribution.ShareAbove(FrozenFrameMilliseconds),
						});
					}
				}

				return rows;
			}
		}

		/// <summary>The loads of a build, newest last, with the steps that were sent with them.</summary>
		public JsonArray Loads(string build)
		{
			lock (m_Gate)
			{
				if (!m_Builds.TryGetValue(build, out var perf))
					return new JsonArray();
				var loads = new JsonArray();
				foreach (var load in perf.Loads)
				{
					var id = load["span"].GetValue<string>();
					var phases = perf.Spans.Where(span => span["parent"]?.GetValue<string>() == id)
						.OrderBy(span => span["start"].GetValue<double>())
						.Select(span => (JsonNode)new JsonObject { ["name"] = span["name"].DeepClone(), ["duration"] = span["duration"].DeepClone() })
						.ToArray();
					loads.Add(new JsonObject
					{
						["start"] = load["start"].DeepClone(),
						["duration"] = load["duration"].DeepClone(),
						["name"] = load["attributes"]?["dexc.load.name"]?.DeepClone(),
						["kind"] = load["attributes"]?["dexc.load.kind"]?.DeepClone(),
						["phases"] = new JsonArray(phases),
					});
				}

				return loads;
			}
		}

		public JsonArray Hitches(string build) => Records(build, perf => perf.Hitches);

		public JsonArray Spans(string build) => Records(build, perf => perf.Spans);

		public JsonArray Logs(string build) => Records(build, perf => perf.Logs);

		public IReadOnlyList<string> Builds()
		{
			lock (m_Gate)
				return m_Builds.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
		}

		private JsonArray Records(string build, Func<BuildPerf, Queue<JsonObject>> select)
		{
			lock (m_Gate)
			{
				return m_Builds.TryGetValue(build, out var perf)
					? new JsonArray(select(perf).Select(record => (JsonNode)record.DeepClone()).ToArray())
					: new JsonArray();
			}
		}

		private BuildPerf Of(Resource resource, List<string> builds)
		{
			var attributes = resource?.Attributes ?? new RepeatedField<KeyValue>();
			var name = attributes.FirstOrDefault(pair => pair.Key == "dexc.build")?.Value?.StringValue
				?? attributes.FirstOrDefault(pair => pair.Key == "service.name")?.Value?.StringValue
				?? "unknown";
			if (!builds.Contains(name))
				builds.Add(name);
			if (!m_Builds.TryGetValue(name, out var perf))
			{
				perf = new BuildPerf();
				m_Builds[name] = perf;
			}

			return perf;
		}

		private static void Add(BuildPerf build, Metric metric)
		{
			switch (metric.DataCase)
			{
				case Metric.DataOneofCase.Gauge:
					foreach (var point in metric.Gauge.DataPoints)
						Point(build, metric, "gauge", point.Attributes, null, Number(point.TimeUnixNano, point));
					break;
				case Metric.DataOneofCase.Sum:
					foreach (var point in metric.Sum.DataPoints)
						Point(build, metric, "sum", point.Attributes, null, Number(point.TimeUnixNano, point));
					break;
				case Metric.DataOneofCase.Histogram:
					foreach (var point in metric.Histogram.DataPoints)
					{
						Point(build, metric, "histogram", point.Attributes, point.ExplicitBounds.ToArray(), new JsonObject
						{
							["time"] = Milliseconds(point.TimeUnixNano),
							["count"] = point.Count,
							["sum"] = point.Sum,
							["min"] = point.HasMin ? point.Min : null,
							["max"] = point.HasMax ? point.Max : null,
							// What a line of one value shows of a histogram.
							["value"] = point.Count == 0 ? null : point.Sum / point.Count,
							["buckets"] = new JsonArray(point.BucketCounts.Select(count => (JsonNode)count).ToArray()),
						});
					}

					break;
			}
		}

		private static JsonObject Number(ulong time, NumberDataPoint point) => new JsonObject
		{
			["time"] = Milliseconds(time),
			["value"] = point.ValueCase == NumberDataPoint.ValueOneofCase.AsInt ? point.AsInt : point.AsDouble,
		};

		private static void Point(BuildPerf build, Metric metric, string kind, RepeatedField<KeyValue> attributes, double[] bounds, JsonObject point)
		{
			var name = attributes.Count == 0
				? metric.Name
				: metric.Name + "{" + string.Join(",", attributes.Select(pair => pair.Key + "=" + Text(pair.Value))) + "}";
			if (!build.Metrics.TryGetValue(name, out var series))
			{
				series = new Series { Name = name, Metric = metric.Name, Attributes = Attributes(attributes), Unit = metric.Unit, Kind = kind, Bounds = bounds };
				build.Metrics[name] = series;
			}

			series.Points.Enqueue(point);
			while (series.Points.Count > PointCapacity)
				series.Points.Dequeue();
		}

		private static void Keep(Queue<JsonObject> records, JsonObject record)
		{
			records.Enqueue(record);
			while (records.Count > RecordCapacity)
				records.Dequeue();
		}

		private static JsonObject Attributes(RepeatedField<KeyValue> attributes)
		{
			var result = new JsonObject();
			foreach (var pair in attributes)
			{
				result[pair.Key] = pair.Value?.ValueCase switch
				{
					AnyValue.ValueOneofCase.IntValue => JsonValue.Create(pair.Value.IntValue),
					AnyValue.ValueOneofCase.DoubleValue => JsonValue.Create(pair.Value.DoubleValue),
					AnyValue.ValueOneofCase.BoolValue => JsonValue.Create(pair.Value.BoolValue),
					_ => JsonValue.Create(Text(pair.Value)),
				};
			}

			return result;
		}

		private static string Text(AnyValue value) => value == null ? null : value.ValueCase switch
		{
			AnyValue.ValueOneofCase.StringValue => value.StringValue,
			AnyValue.ValueOneofCase.IntValue => value.IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
			AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
			AnyValue.ValueOneofCase.BoolValue => value.BoolValue ? "true" : "false",
			_ => value.ToString(),
		};

		private static string Hex(Google.Protobuf.ByteString bytes) => Convert.ToHexString(bytes.ToByteArray()).ToLowerInvariant();

		private static double Milliseconds(ulong unixNano) => unixNano / 1e6;
	}

	/// <summary>
	/// Buckets added up over points, and what they tell. A percentile falls inside a bucket, where it is taken by linear
	/// interpolation, as Prometheus does; the first and the last bucket are closed by the smallest and largest value seen.
	/// </summary>
	internal sealed class Distribution
	{
		private readonly double[] m_Bounds;
		private readonly ulong[] m_Buckets;

		private Distribution(double[] bounds)
		{
			m_Bounds = bounds;
			m_Buckets = new ulong[bounds.Length + 1];
			Minimum = double.MaxValue;
			Maximum = double.MinValue;
		}

		public ulong Count { get; private set; }
		public double Sum { get; private set; }
		public double Minimum { get; private set; }
		public double Maximum { get; private set; }

		/// <summary>The points of one series. A point whose buckets do not fit the bounds is left out.</summary>
		public static Distribution Of(double[] bounds, IEnumerable<JsonObject> points)
		{
			var distribution = new Distribution(bounds);
			foreach (var point in points)
			{
				if (!(point["buckets"] is JsonArray buckets) || buckets.Count != distribution.m_Buckets.Length)
					continue;
				for (var i = 0; i < buckets.Count; i++)
					distribution.m_Buckets[i] += buckets[i].GetValue<ulong>();
				distribution.Count += point["count"].GetValue<ulong>();
				distribution.Sum += point["sum"].GetValue<double>();
				if (point["min"] != null)
					distribution.Minimum = Math.Min(distribution.Minimum, point["min"].GetValue<double>());
				if (point["max"] != null)
					distribution.Maximum = Math.Max(distribution.Maximum, point["max"].GetValue<double>());
			}

			return distribution;
		}

		public double Quantile(double quantile)
		{
			if (Count == 0)
				return double.NaN;
			var target = quantile * Count;
			ulong below = 0;
			for (var i = 0; i < m_Buckets.Length; i++)
			{
				if (m_Buckets[i] == 0 || below + m_Buckets[i] < target)
				{
					below += m_Buckets[i];
					continue;
				}

				var lower = Math.Max(i == 0 ? Minimum : m_Bounds[i - 1], Minimum);
				var upper = Math.Min(i == m_Bounds.Length ? Maximum : m_Bounds[i], Maximum);
				if (upper < lower)
					upper = lower;
				return lower + (upper - lower) * ((target - below) / m_Buckets[i]);
			}

			return Maximum;
		}

		/// <summary>The share of values above a threshold, counted in the buckets that start at or above it.</summary>
		public double ShareAbove(double threshold)
		{
			if (Count == 0)
				return double.NaN;
			ulong above = 0;
			for (var i = 1; i < m_Buckets.Length; i++)
			{
				if (m_Bounds[i - 1] >= threshold)
					above += m_Buckets[i];
			}

			return (double)above / Count;
		}
	}
}
