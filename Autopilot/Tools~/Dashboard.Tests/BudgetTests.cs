using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Dexc.Autopilot.Compiler;
using NUnit.Framework;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace Dexc.Autopilot.Dashboard.Tests
{
	/// <summary>Budgets judged on what the Players sent, and what a verification reads.</summary>
	public sealed class BudgetTests
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

		private Workspace Workspace => Compiler.Workspace.Open(m_Workspace.Directory);

		private void Budget(string stage, double max)
		{
			var workspace = Workspace;
			workspace.File.Budgets.Add(new WorkspaceBudget { Metric = "unity.frame.time", Stat = "p95", Max = max, Where = new Dictionary<string, string> { ["stage"] = stage } });
			workspace.Save();
		}

		/// <summary>200 frames at stage 1, half of them over 16.7 ms.</summary>
		private static void Frames(Perf perf)
		{
			var request = new ExportMetricsServiceRequest();
			var resource = new ResourceMetrics { Resource = new Resource() };
			resource.Resource.Attributes.Add(new KeyValue { Key = "dexc.build", Value = new AnyValue { StringValue = "a" } });
			var metric = new Metric { Name = "unity.frame.time", Unit = "ms", Histogram = new Histogram() };
			var point = new HistogramDataPoint { TimeUnixNano = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000, Count = 200, Sum = 4000, Min = 10, Max = 40 };
			point.Attributes.Add(new KeyValue { Key = "stage", Value = new AnyValue { StringValue = "1" } });
			point.ExplicitBounds.Add(new[] { 16.7, 33.4 });
			point.BucketCounts.Add(new ulong[] { 100, 100, 0 });
			metric.Histogram.DataPoints.Add(point);
			var scope = new ScopeMetrics();
			scope.Metrics.Add(metric);
			resource.ScopeMetrics.Add(scope);
			request.ResourceMetrics.Add(resource);
			perf.Add(request);
		}

		[Test]
		public void ABudgetGoneOverIsAPerformanceDefectCountedPerCheck()
		{
			Budget("1", 25);
			Budget("2", 1);
			var hub = new Hub(m_Workspace.Directory, TimeSpan.FromSeconds(60));
			Frames(hub.Perf);

			var first = hub.CheckBudgets();
			hub.CheckBudgets();

			var defect = QAStore.Read(Workspace).Defects.Single();
			Assert.That(first.Created.Single().Kind, Is.EqualTo("perf"));
			Assert.That(defect.Signature, Is.EqualTo("perf:p95:unity.frame.time{stage=1}"));
			Assert.That(defect.Title, Does.StartWith("unity.frame.time p95 3"));
			Assert.That(defect.Count, Is.EqualTo(2), "two checks found it over");
			Assert.That(hub.Relay.Journal().Select(entry => entry["event"].GetValue<string>()), Is.EqualTo(new[] { "found" }));
		}

		[Test]
		public void AVerificationRunsOnTheSeedOfAReportAndLooksAfterItsStart()
		{
			var folder = Path.Combine(QAStore.Reports(Workspace, "a"), "r1");
			Directory.CreateDirectory(folder);
			File.WriteAllText(Path.Combine(folder, "report.json"), new JsonObject { ["resource"] = new JsonObject { ["dexc.seed"] = "42" } }.ToJsonString());
			var data = new QAData();
			var defect = new QADefect { Id = "D0001", Signature = "s", Reports = { "a/r1" } };
			defect.Builds["a"] = new QAOccurrence { Last = "2026-11-09T00:00:10.0000000Z" };
			data.Defects.Add(defect);

			Assert.That(QAStore.SeedOf(Workspace, defect), Is.EqualTo("42"));
			Assert.That(QAStore.OccurredSince(data, "D0001", "a", "2026-11-09T00:00:05.0000000Z"), Is.True);
			Assert.That(QAStore.OccurredSince(data, "D0001", "a", "2026-11-09T00:00:20.0000000Z"), Is.False);
			Assert.That(QAStore.OccurredSince(data, "D0001", "b", "2026-11-09T00:00:05.0000000Z"), Is.False);
		}
	}
}
