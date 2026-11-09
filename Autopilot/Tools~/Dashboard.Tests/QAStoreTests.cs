using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dexc.Autopilot.Compiler;
using NUnit.Framework;

namespace Dexc.Autopilot.Dashboard.Tests
{
	/// <summary>Gathering the reports Dexc QA writes into the defects of a workspace.</summary>
	public sealed class QAStoreTests
	{
		private FakeWorkspace m_Workspace;
		private string m_Player;

		[SetUp]
		public void SetUp()
		{
			m_Workspace = new FakeWorkspace("a");
			m_Player = Path.Combine(m_Workspace.Directory, ".state", "a", "qa");
			Directory.CreateDirectory(m_Player);
		}

		[TearDown]
		public void TearDown()
		{
			m_Workspace.Dispose();
		}

		private Workspace Workspace => Compiler.Workspace.Open(m_Workspace.Directory);

		private void Report(string id, string screenshot)
		{
			var folder = Path.Combine(m_Player, id);
			Directory.CreateDirectory(folder);
			File.WriteAllText(Path.Combine(folder, "report.json"), new JsonObject { ["id"] = id, ["screenshot"] = screenshot }.ToJsonString());
		}

		private void Counts(string run, int count, string last, params string[] reports)
		{
			var signatures = new JsonArray(new JsonObject
			{
				["signature"] = "exception:NullReferenceException@Game.Open ()",
				["kind"] = "exception",
				["title"] = "NullReferenceException: Object reference",
				["count"] = count,
				["first"] = "2026-11-09T00:00:00.0000000Z",
				["last"] = last,
				["reports"] = new JsonArray(reports.Select(report => (JsonNode)report).ToArray()),
			});
			File.WriteAllText(Path.Combine(m_Player, "signatures-" + run + ".json"), new JsonObject { ["run"] = run, ["signatures"] = signatures }.ToJsonString());
		}

		[Test]
		public void WholeReportsAreTakenAndEachRunCountsOnce()
		{
			Report("r1", null);
			Report("r2", "pending");
			Counts("run1", 5, "2026-11-09T00:00:05.0000000Z", "r1", "r2");

			var first = QAStore.Gather(Workspace);
			Counts("run1", 7, "2026-11-09T00:00:07.0000000Z", "r1", "r2");
			Counts("run2", 1, "2026-11-09T00:01:00.0000000Z");
			var second = QAStore.Gather(Workspace);

			Assert.That(first.Created.Single().Id, Is.EqualTo("D0001"));
			Assert.That(first.Reports, Is.EqualTo(1), "a screenshot still to come keeps the report with the running Player");
			Assert.That(Directory.Exists(Path.Combine(m_Player, "r2")), Is.True);
			Assert.That(second.Created, Is.Empty);
			var defect = QAStore.Read(Workspace).Defects.Single();
			Assert.That(defect.Count, Is.EqualTo(8), "the counts of a run replace, the runs add up");
			Assert.That(defect.Reports, Is.EqualTo(new[] { "a/r1" }));
		}

		[Test]
		public void AFixedDefectThatComesBackIsOpenAgainAndTheAgentHearsOfBoth()
		{
			var hub = new Hub(m_Workspace.Directory, TimeSpan.FromSeconds(60));
			Counts("run1", 1, "2026-11-09T00:00:01.0000000Z");
			hub.GatherDefects();
			var found = hub.Relay.Next(TimeSpan.Zero, CancellationToken.None).Result;

			QAStore.SetState(Workspace, "D0001", QADefect.Fixed, "agent", "null check added");
			Counts("run2", 2, QAStore.Now());
			var changes = hub.GatherDefects();
			var recurred = hub.Relay.Next(TimeSpan.Zero, CancellationToken.None).Result;

			Assert.That(found["kind"].GetValue<string>(), Is.EqualTo("defect"));
			Assert.That(found["defect"].GetValue<string>(), Is.EqualTo("found"));
			Assert.That(changes.Recurred.Single().Id, Is.EqualTo("D0001"));
			Assert.That(recurred["defect"].GetValue<string>(), Is.EqualTo("recurred"));
			var defect = QAStore.Read(Workspace).Defects.Single();
			Assert.That(defect.State, Is.EqualTo(QADefect.Open));
			Assert.That(defect.Notes.Select(note => note.Text), Is.EqualTo(new[] { "open -> fixed: null check added", "fixed -> open: occurred again in a" }));
		}

		[Test]
		public async Task TheFilesOfAReportAreServedButNothingBesideThem()
		{
			Report("r1", null);
			Counts("run1", 1, "2026-11-09T00:00:01.0000000Z", "r1");
			var hub = new Hub(m_Workspace.Directory, TimeSpan.FromSeconds(60));
			hub.GatherDefects();
			await using var server = new DashboardServer(hub, 0, "secret", TestContext.CurrentContext.TestDirectory);
			await server.Start();
			using var client = new HttpClient { BaseAddress = new Uri(server.Url) };

			var report = JsonNode.Parse(await client.GetStringAsync("api/qa/report?build=a&id=r1"));
			var escape = await client.GetAsync("api/qa/file?build=a&id=r1&name=" + Uri.EscapeDataString("..\\..\\..\\workspace.json"));
			var defects = JsonNode.Parse(await client.GetStringAsync("api/qa/defects?state=open")).AsArray();

			Assert.That(report["id"].GetValue<string>(), Is.EqualTo("r1"));
			Assert.That(escape.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(defects.Single()["signature"].GetValue<string>(), Does.StartWith("exception:"));
		}
	}
}
