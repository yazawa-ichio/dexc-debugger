using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Dexc.Autopilot.Dashboard.Tests
{
	public sealed class RelayTests
	{
		private static readonly TimeSpan s_Human = TimeSpan.FromSeconds(60);

		private FakeWorkspace m_Workspace;
		private Hub m_Hub;

		[SetUp]
		public void SetUp()
		{
			m_Workspace = new FakeWorkspace("a");
			Ask("d1", "Which way?");
			m_Hub = new Hub(m_Workspace.Directory, s_Human);
		}

		[TearDown]
		public void TearDown()
		{
			m_Workspace.Dispose();
		}

		private void Ask(params string[] decisions)
		{
			var waiting = new JsonArray();
			for (var index = 0; index + 1 < decisions.Length; index += 2)
				waiting.Add(new JsonObject { ["id"] = decisions[index], ["question"] = decisions[index + 1], ["choices"] = new JsonArray("left", "right"), ["machine"] = "Quest", ["state"] = "Fork", ["elapsed"] = 0 });
			lock (m_Workspace[0])
				m_Workspace[0].Status["decisions"] = waiting;
		}

		private async Task Look() => await Task.WhenAll(m_Hub.LookAll());

		private string[] Journal() => m_Hub.Relay.Journal().Select(entry => entry["event"].GetValue<string>()).ToArray();

		[Test]
		public async Task AQuestionNobodyAnsweredGoesToTheAgentWhoseAnswerReachesThePlayer()
		{
			await Look();
			Assert.That(m_Hub.Relay.Decisions().Single()["stage"].GetValue<string>(), Is.EqualTo("human"));
			m_Hub.Relay.Tick(DateTime.UtcNow + s_Human - TimeSpan.FromSeconds(5));
			Assert.That(await m_Hub.Relay.Next(TimeSpan.Zero, CancellationToken.None), Is.Null, "still the people's");

			m_Hub.Relay.Tick(DateTime.UtcNow + s_Human + TimeSpan.FromSeconds(1));
			var next = await m_Hub.Relay.Next(TimeSpan.Zero, CancellationToken.None);
			Assert.That(next["question"].GetValue<string>(), Is.EqualTo("Which way?"));
			Assert.That(m_Hub.Relay.Answer("a", "d1", "left", "agent")["ok"].GetValue<bool>(), Is.True);

			Assert.That(m_Workspace[0].Requests, Does.Contain(("answer:d1:agent", "left")));
			Assert.That(m_Hub.Relay.Decisions(), Is.Empty);
			Assert.That(Journal(), Is.EqualTo(new[] { "asked", "escalated", "delivered", "answered" }));
		}

		[Test]
		public async Task AQuestionThePeopleAnsweredOrTheBotWithdrewNeverReachesTheAgent()
		{
			Ask("d1", "Which way?", "d2", "How fast?");
			await Look();
			m_Hub.Relay.Answer("a", "d1", "right", "human");
			Ask("d1", "Which way?");
			await Look();

			m_Hub.Relay.Tick(DateTime.UtcNow + s_Human + TimeSpan.FromSeconds(1));

			Assert.That(await m_Hub.Relay.Next(TimeSpan.Zero, CancellationToken.None), Is.Null);
			Assert.That(Journal(), Is.EqualTo(new[] { "asked", "asked", "answered", "withdrawn" }));
			Assert.That(new Hub(m_Workspace.Directory, s_Human).Relay.Journal().Count, Is.EqualTo(4), "the journal is kept in the workspace");
		}

		[Test]
		public async Task TheAgentWaitsForWhatThePeopleTellIt()
		{
			await using var server = new DashboardServer(m_Hub, 0, "secret", TestContext.CurrentContext.TestDirectory);
			await server.Start();
			using var client = new HttpClient { BaseAddress = new Uri(server.Url) };
			HttpRequestMessage Post(string path, string body, bool token = true)
			{
				var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
				if (token)
					request.Headers.Add(DashboardServer.TokenHeader, "secret");
				return request;
			}

			Assert.That((await client.SendAsync(Post("api/agent/next?timeout=0", "{}", false))).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That((await client.SendAsync(Post("api/agent/next?timeout=0", "{}"))).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

			var waiting = client.SendAsync(Post("api/agent/next?timeout=10", "{}"));
			await Task.Delay(200);
			Assert.That(waiting.IsCompleted, Is.False, "it waits");
			await client.SendAsync(Post("api/instructions", "{\"text\":\"Go to stage 2\",\"builds\":[\"a\"]}"));
			var instruction = JsonNode.Parse(await (await waiting).Content.ReadAsStringAsync());

			Assert.That(instruction["kind"].GetValue<string>(), Is.EqualTo("instruction"));
			Assert.That(instruction["text"].GetValue<string>(), Is.EqualTo("Go to stage 2"));
			Assert.That(instruction["builds"][0].GetValue<string>(), Is.EqualTo("a"));
		}
	}
}
