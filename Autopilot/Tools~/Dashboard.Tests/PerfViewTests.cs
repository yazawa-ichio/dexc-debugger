using System;
using System.Linq;
using System.Text.Json.Nodes;
using Google.Protobuf;
using NUnit.Framework;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Dexc.Autopilot.Dashboard.Tests
{
	/// <summary>What the performance views read: distributions, loads and hitches.</summary>
	public sealed class PerfViewTests
	{
		private static readonly ulong s_Now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000;

		private static KeyValue Text(string key, string value) => new KeyValue { Key = key, Value = new AnyValue { StringValue = value } };

		private static Resource Build(string name)
		{
			var resource = new Resource();
			resource.Attributes.Add(Text("dexc.build", name));
			return resource;
		}

		[Test]
		public void PercentilesComeFromTheBucketsAddedUp()
		{
			var points = new[]
			{
				new JsonObject { ["count"] = 4UL, ["sum"] = 60.0, ["min"] = 5.0, ["max"] = 18.0, ["buckets"] = new JsonArray(2UL, 2UL, 0UL) },
				new JsonObject { ["count"] = 6UL, ["sum"] = 120.0, ["min"] = 12.0, ["max"] = 30.0, ["buckets"] = new JsonArray(0UL, 4UL, 2UL) },
			};

			var distribution = Distribution.Of(new double[] { 10, 20 }, points);

			Assert.That(distribution.Count, Is.EqualTo(10UL));
			Assert.That(distribution.Quantile(0.5), Is.EqualTo(15).Within(1e-9), "inside 10 to 20, three of six in");
			Assert.That(distribution.Quantile(0.95), Is.EqualTo(27.5).Within(1e-9), "the last bucket is closed by the max");
			Assert.That(distribution.Quantile(0.1), Is.EqualTo(7.5).Within(1e-9), "the first bucket is opened by the min");
			Assert.That(distribution.ShareAbove(20), Is.EqualTo(0.2));
			Assert.That(distribution.ShareAbove(10), Is.EqualTo(0.8));
		}

		[Test]
		public void TheSummaryIsBySeriesAndBuild()
		{
			var perf = new Perf();
			foreach (var (build, slow) in new[] { ("a", 1UL), ("b", 5UL) })
			{
				var request = new ExportMetricsServiceRequest();
				var resource = new ResourceMetrics { Resource = Build(build) };
				var scope = new ScopeMetrics();
				var metric = new Metric { Name = "unity.frame.time", Unit = "ms", Histogram = new Histogram() };
				var point = new HistogramDataPoint { TimeUnixNano = s_Now, Count = 10, Sum = 200, Min = 10, Max = 40 };
				point.Attributes.Add(Text("stage", "1"));
				point.ExplicitBounds.Add(new[] { 16.7, 700 });
				point.BucketCounts.Add(new[] { 10 - slow, slow, 0UL });
				metric.Histogram.DataPoints.Add(point);
				scope.Metrics.Add(metric);
				resource.ScopeMetrics.Add(scope);
				request.ResourceMetrics.Add(resource);
				perf.Add(request);
			}

			var rows = perf.Summary("unity.frame.time", 0);

			Assert.That(rows.Select(row => row["build"].GetValue<string>()), Is.EqualTo(new[] { "a", "b" }));
			Assert.That(rows.Select(row => row["attributes"]["stage"].GetValue<string>()), Is.All.EqualTo("1"));
			Assert.That(rows.Select(row => row["slow"].GetValue<double>()), Is.EqualTo(new[] { 0.1, 0.5 }));
			Assert.That(rows.Select(row => row["frozen"].GetValue<double>()), Is.All.EqualTo(0.0));
		}

		[Test]
		public void ALoadComesWithItsStepsAndAHitchIsKeptApart()
		{
			var perf = new Perf();
			var traces = new ExportTraceServiceRequest();
			var spans = new ResourceSpans { Resource = Build("a") };
			var scope = new ScopeSpans();
			var load = new Span { Name = "dexc.load", SpanId = ByteString.CopyFrom(1, 1, 1, 1, 1, 1, 1, 1), StartTimeUnixNano = s_Now, EndTimeUnixNano = s_Now + 900000000 };
			load.Attributes.Add(Text("dexc.load.name", "stage1"));
			load.Attributes.Add(Text("dexc.load.kind", "level"));
			var step = new Span { Name = "assets", SpanId = ByteString.CopyFrom(2, 2, 2, 2, 2, 2, 2, 2), ParentSpanId = load.SpanId, StartTimeUnixNano = s_Now, EndTimeUnixNano = s_Now + 600000000 };
			scope.Spans.Add(step);
			scope.Spans.Add(load);
			spans.ScopeSpans.Add(scope);
			traces.ResourceSpans.Add(spans);
			perf.Add(traces);

			var logs = new ExportLogsServiceRequest();
			var records = new ResourceLogs { Resource = Build("a") };
			var scopeLogs = new ScopeLogs();
			var hitch = new LogRecord { TimeUnixNano = s_Now, EventName = "dexc.hitch", Body = new AnyValue { StringValue = "hitch" } };
			hitch.Attributes.Add(new KeyValue { Key = "unity.frame.time", Value = new AnyValue { DoubleValue = 250 } });
			scopeLogs.LogRecords.Add(hitch);
			scopeLogs.LogRecords.Add(new LogRecord { TimeUnixNano = s_Now, Body = new AnyValue { StringValue = "plain" } });
			records.ScopeLogs.Add(scopeLogs);
			logs.ResourceLogs.Add(records);
			perf.Add(logs);

			var loaded = perf.Loads("a").Single();
			Assert.That(loaded["name"].GetValue<string>(), Is.EqualTo("stage1"));
			Assert.That(loaded["duration"].GetValue<double>(), Is.EqualTo(900).Within(1e-6));
			Assert.That(loaded["phases"][0]["name"].GetValue<string>(), Is.EqualTo("assets"));
			Assert.That(perf.Hitches("a").Single()["attributes"]["unity.frame.time"].GetValue<double>(), Is.EqualTo(250));
			Assert.That(perf.Logs("a").Count, Is.EqualTo(2));
		}
	}
}
