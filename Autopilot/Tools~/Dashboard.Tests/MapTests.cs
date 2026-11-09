using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Dexc.Autopilot.Compiler;
using NUnit.Framework;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace Dexc.Autopilot.Dashboard.Tests
{
	public sealed class MapTests
	{
		private FakeWorkspace m_Workspace;

		[SetUp]
		public void SetUp()
		{
			m_Workspace = new FakeWorkspace("a");
			var maps = Maps.Folder(m_Workspace.Directory);
			Directory.CreateDirectory(maps);
			// x across from -10 to 10, z down from 10 to -10: north up, as a top view.
			File.WriteAllText(Path.Combine(maps, "yard.json"), "{\"image\":\"yard.png\",\"horizontal\":\"x\",\"vertical\":\"z\",\"left\":-10,\"right\":10,\"top\":10,\"bottom\":-10}");
		}

		[TearDown]
		public void TearDown()
		{
			m_Workspace.Dispose();
		}

		private static KeyValue Pair(string key, AnyValue value) => new KeyValue { Key = key, Value = value };

		[Test]
		public void AWorldPositionFallsOnTheImageByItsAxesAndEdges()
		{
			var layout = Maps.Layout(m_Workspace.Directory, "yard");

			Assert.That(layout.Project(-10, 99, 10), Is.EqualTo((0.0, 0.0)), "the top left; y is not an axis of this map");
			Assert.That(layout.Project(5, 0, -5), Is.EqualTo((0.75, 0.75)));
			Assert.That(layout.Project(20, 0, 0).U, Is.GreaterThan(1), "outside stays outside");
			Assert.That(Maps.Layout(m_Workspace.Directory, "..\\yard"), Is.Null);
		}

		[Test]
		public void PositionsHitchesAndDefectsOfTheMapAreProjected()
		{
			var hub = new Hub(m_Workspace.Directory, TimeSpan.FromSeconds(60));
			var request = new ExportLogsServiceRequest();
			var resource = new ResourceLogs { Resource = new Resource() };
			resource.Resource.Attributes.Add(Pair("dexc.build", new AnyValue { StringValue = "a" }));
			var scope = new ScopeLogs();
			foreach (var (name, map) in new[] { ("dexc.position", "yard"), ("dexc.hitch", "yard"), ("dexc.position", "elsewhere") })
			{
				var record = new LogRecord { TimeUnixNano = 1, EventName = name };
				record.Attributes.Add(Pair("dexc.map", new AnyValue { StringValue = map }));
				record.Attributes.Add(Pair("dexc.position.x", new AnyValue { DoubleValue = 0 }));
				record.Attributes.Add(Pair("dexc.position.y", new AnyValue { DoubleValue = 0 }));
				record.Attributes.Add(Pair("dexc.position.z", new AnyValue { DoubleValue = 0 }));
				scope.LogRecords.Add(record);
			}

			resource.ScopeLogs.Add(scope);
			request.ResourceLogs.Add(resource);
			hub.Perf.Add(request);
			var folder = Path.Combine(QAStore.Reports(hub.Workspace(), "a"), "r1");
			Directory.CreateDirectory(folder);
			File.WriteAllText(Path.Combine(folder, "report.json"), "{\"location\":{\"map\":\"yard\",\"x\":-10,\"y\":0,\"z\":-10}}");
			QAStore.Change(hub.Workspace(), data =>
			{
				data.Defects.Add(new QADefect { Id = "D0001", Signature = "s", Title = "Stuck", Reports = { "a/r1" } });
				return true;
			});

			var points = Maps.Points(hub, "yard");

			Assert.That(points["positions"].AsArray().Count, Is.EqualTo(1), "only this map");
			Assert.That(points["positions"][0]["u"].GetValue<double>(), Is.EqualTo(0.5));
			Assert.That(points["hitches"].AsArray().Count, Is.EqualTo(1));
			Assert.That(points["defects"][0]["v"].GetValue<double>(), Is.EqualTo(1.0));
			Assert.That(points["image"].GetValue<string>(), Is.EqualTo("/project/maps/yard.png"));
			Assert.That(hub.Perf.Logs("a").Single()["event"].GetValue<string>(), Is.EqualTo("dexc.hitch"), "positions do not push the logs out");
		}
	}
}
