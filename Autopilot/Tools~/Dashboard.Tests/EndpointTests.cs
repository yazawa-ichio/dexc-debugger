using System;
using System.IO;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Dexc.Autopilot.Dashboard.Tests
{
	/// <summary>How the tools find the dashboard, and how they tell one that was killed.</summary>
	public sealed class EndpointTests
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

		private void Endpoint(int pid)
		{
			var workspace = Compiler.Workspace.Open(m_Workspace.Directory);
			Directory.CreateDirectory(workspace.Dashboard);
			File.WriteAllText(workspace.DashboardEndpoint, new JsonObject { ["url"] = "http://127.0.0.1:7892/", ["token"] = "t", ["pid"] = pid }.ToJsonString());
		}

		[Test]
		public void ARunningDashboardIsFound()
		{
			Endpoint(Environment.ProcessId);

			Assert.That(Compiler.Workspace.Open(m_Workspace.Directory).RunningDashboard()["token"].GetValue<string>(), Is.EqualTo("t"));
		}

		[Test]
		public void TheFileOfAKilledDashboardSaysItIsNotRunningAndGoes()
		{
			Endpoint(int.MaxValue);
			var workspace = Compiler.Workspace.Open(m_Workspace.Directory);

			Assert.That(() => workspace.RunningDashboard(), Throws.InvalidOperationException.With.Message.Contains("not running"));
			Assert.That(File.Exists(workspace.DashboardEndpoint), Is.False);
		}
	}
}
