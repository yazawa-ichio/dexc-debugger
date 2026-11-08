using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Dexc.Autopilot.Dashboard.Tests
{
	public sealed class HubTests
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

		[Test]
		public async Task ALookTakesTheStatusAndTheGraphOnlyWhenTheBotWasLoadedAgain()
		{
			var hub = new Hub(m_Workspace.Directory, System.TimeSpan.FromSeconds(60));

			await Task.WhenAll(hub.LookAll());
			await Task.WhenAll(hub.LookAll());
			var build = hub.Build("a");
			Assert.That(build.Running, Is.True, build.Error);
			Assert.That(build.Status["activeState"].GetValue<string>(), Is.EqualTo("Walk"));
			Assert.That(m_Workspace[0].Requests.Count(request => request.Operation == "graph"), Is.EqualTo(1));

			lock (m_Workspace[0])
				m_Workspace[0].Status["loadCount"] = 2;
			await Task.WhenAll(hub.LookAll());

			Assert.That(m_Workspace[0].Requests.Count(request => request.Operation == "graph"), Is.EqualTo(2));
			Assert.That(Hub.History(build).Count, Is.EqualTo(3));
		}

		[Test]
		public async Task OnlyThisMachineIsServedAndChangesNeedTheToken()
		{
			var hub = new Hub(m_Workspace.Directory, System.TimeSpan.FromSeconds(60));
			await Task.WhenAll(hub.LookAll());
			await using var server = new DashboardServer(hub, 0, "secret", TestContext.CurrentContext.TestDirectory);
			await server.Start();
			using var client = new HttpClient { BaseAddress = new System.Uri(server.Url) };

			var status = JsonNode.Parse(await client.GetStringAsync("api/status?build=a"));
			Assert.That(status["status"]["active"].GetValue<string>(), Is.EqualTo("Quest"));

			var renamed = new HttpRequestMessage(HttpMethod.Get, "api/builds");
			renamed.Headers.Host = "attacker.example";
			Assert.That((await client.SendAsync(renamed)).StatusCode, Is.EqualTo(HttpStatusCode.MisdirectedRequest));

			Assert.That((await client.PostAsync("api/anything", new StringContent("{}"))).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
		}
	}
}
