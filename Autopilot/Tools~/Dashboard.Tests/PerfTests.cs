using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Google.Protobuf;
using NUnit.Framework;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace Dexc.Autopilot.Dashboard.Tests
{
	public sealed class PerfTests
	{
		private FakeWorkspace m_Workspace;

		[SetUp]
		public void SetUp()
		{
			m_Workspace = new FakeWorkspace("a");
		}

		[TearDown]
		public void TearDown()
		{
			m_Workspace.Dispose();
		}

		private static byte[] Metrics(string build)
		{
			var time = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000;
			var request = new ExportMetricsServiceRequest();
			var resource = new ResourceMetrics { Resource = new Resource() };
			resource.Resource.Attributes.Add(new KeyValue { Key = "dexc.build", Value = new AnyValue { StringValue = build } });
			var scope = new ScopeMetrics();
			var memory = new Metric { Name = "unity.memory.used", Unit = "By", Gauge = new Gauge() };
			memory.Gauge.DataPoints.Add(new NumberDataPoint { TimeUnixNano = time, AsInt = 100 });
			var frame = new Metric { Name = "unity.frame.time", Unit = "ms", Histogram = new Histogram() };
			frame.Histogram.DataPoints.Add(new HistogramDataPoint { TimeUnixNano = time, Count = 2, Sum = 33.4, Max = 20 });
			scope.Metrics.Add(memory);
			scope.Metrics.Add(frame);
			resource.ScopeMetrics.Add(scope);
			request.ResourceMetrics.Add(resource);
			using var output = new MemoryStream();
			using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
				request.WriteTo(gzip);
			return output.ToArray();
		}

		[Test]
		public async Task WhatAPlayerPushesIsKeptByBuildAndForwarded()
		{
			var backend = new Hub(m_Workspace.Directory, TimeSpan.FromSeconds(60));
			await using var forwarded = new DashboardServer(backend, 0, "other", TestContext.CurrentContext.TestDirectory);
			await forwarded.Start();
			var hub = new Hub(m_Workspace.Directory, TimeSpan.FromSeconds(60));
			await using var server = new DashboardServer(hub, 0, "secret", TestContext.CurrentContext.TestDirectory, forwarded.Url);
			await server.Start();
			using var client = new HttpClient { BaseAddress = new Uri(server.Url) };

			var content = new ByteArrayContent(Metrics("a"));
			content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
			content.Headers.ContentEncoding.Add("gzip");
			var response = await client.PostAsync("v1/metrics", content);

			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "no token: a Player keeps sending across a restart");
			var metrics = JsonNode.Parse(await client.GetStringAsync("api/perf/metrics?build=a")).AsArray();
			Assert.That(metrics.Select(metric => metric["name"].GetValue<string>()), Is.EqualTo(new[] { "unity.frame.time", "unity.memory.used" }));
			var frame = JsonNode.Parse(await client.GetStringAsync("api/perf/points?build=a&name=unity.frame.time")).AsArray().Single();
			Assert.That(frame["value"].GetValue<double>(), Is.EqualTo(16.7).Within(1e-9));
			Assert.That(frame["max"].GetValue<double>(), Is.EqualTo(20));

			for (var attempt = 0; attempt < 50 && backend.Perf.Builds().Count == 0; attempt++)
				await Task.Delay(100);
			Assert.That(backend.Perf.Builds(), Is.EqualTo(new[] { "a" }), "sent on as it came");
		}
	}
}
