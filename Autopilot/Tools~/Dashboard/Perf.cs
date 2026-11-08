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
	/// What the Players sent over OTLP/HTTP, kept in memory by build: the points of each metric, and the latest spans and
	/// logs. The build is the dexc.build of the resource, which ws start puts there. Bounded by counts, oldest out first.
	/// </summary>
	internal sealed class Perf
	{
		public const int PointCapacity = 2000;
		public const int RecordCapacity = 500;

		private sealed class Series
		{
			public string Name;
			public string Unit;
			public string Kind;
			public readonly Queue<JsonObject> Points = new Queue<JsonObject>();
		}

		private sealed class BuildPerf
		{
			public readonly Dictionary<string, Series> Metrics = new Dictionary<string, Series>(StringComparer.Ordinal);
			public readonly Queue<JsonObject> Spans = new Queue<JsonObject>();
			public readonly Queue<JsonObject> Logs = new Queue<JsonObject>();
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
						Keep(build.Spans, new JsonObject
						{
							["name"] = span.Name,
							["start"] = Milliseconds(span.StartTimeUnixNano),
							["duration"] = (span.EndTimeUnixNano - span.StartTimeUnixNano) / 1e6,
							["trace"] = Convert.ToHexString(span.TraceId.ToByteArray()).ToLowerInvariant(),
							["span"] = Convert.ToHexString(span.SpanId.ToByteArray()).ToLowerInvariant(),
							["parent"] = span.ParentSpanId.IsEmpty ? null : Convert.ToHexString(span.ParentSpanId.ToByteArray()).ToLowerInvariant(),
							["attributes"] = Attributes(span.Attributes),
						});
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
						Keep(build.Logs, new JsonObject
						{
							["time"] = Milliseconds(record.TimeUnixNano != 0 ? record.TimeUnixNano : record.ObservedTimeUnixNano),
							["severity"] = record.SeverityText,
							["body"] = record.Body?.StringValue,
							["attributes"] = Attributes(record.Attributes),
						});
					}
				}
			}

			return builds;
		}

		/// <summary>Each metric of a build, by name, with its kind, unit and last point.</summary>
		public JsonArray Metrics(string build)
		{
			lock (m_Gate)
			{
				if (!m_Builds.TryGetValue(build, out var perf))
					return new JsonArray();
				return new JsonArray(perf.Metrics.Values.OrderBy(series => series.Name, StringComparer.Ordinal).Select(series => (JsonNode)new JsonObject
				{
					["name"] = series.Name,
					["unit"] = series.Unit,
					["kind"] = series.Kind,
					["points"] = series.Points.Count,
					["last"] = series.Points.LastOrDefault()?.DeepClone(),
				}).ToArray());
			}
		}

		/// <summary>The points of one metric since a time, in milliseconds since 1970.</summary>
		public JsonArray Points(string build, string name, double since)
		{
			lock (m_Gate)
			{
				if (!m_Builds.TryGetValue(build, out var perf) || !perf.Metrics.TryGetValue(name, out var series))
					return new JsonArray();
				return new JsonArray(series.Points.Where(point => point["time"].GetValue<double>() > since).Select(point => (JsonNode)point.DeepClone()).ToArray());
			}
		}

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
						Point(build, metric, "gauge", point.Attributes, Number(point.TimeUnixNano, point));
					break;
				case Metric.DataOneofCase.Sum:
					foreach (var point in metric.Sum.DataPoints)
						Point(build, metric, "sum", point.Attributes, Number(point.TimeUnixNano, point));
					break;
				case Metric.DataOneofCase.Histogram:
					foreach (var point in metric.Histogram.DataPoints)
					{
						Point(build, metric, "histogram", point.Attributes, new JsonObject
						{
							["time"] = Milliseconds(point.TimeUnixNano),
							["count"] = point.Count,
							["sum"] = point.Sum,
							["min"] = point.HasMin ? point.Min : null,
							["max"] = point.HasMax ? point.Max : null,
							// What a line of one value shows of a histogram.
							["value"] = point.Count == 0 ? null : point.Sum / point.Count,
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

		private static void Point(BuildPerf build, Metric metric, string kind, RepeatedField<KeyValue> attributes, JsonObject point)
		{
			var name = attributes.Count == 0
				? metric.Name
				: metric.Name + "{" + string.Join(",", attributes.Select(pair => pair.Key + "=" + Text(pair.Value))) + "}";
			if (!build.Metrics.TryGetValue(name, out var series))
			{
				series = new Series { Name = name, Unit = metric.Unit, Kind = kind };
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
				result[pair.Key] = Text(pair.Value);
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

		private static double Milliseconds(ulong unixNano) => unixNano / 1e6;
	}
}
