using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;

namespace Dexc.Autopilot.Dashboard.Tests
{
	/// <summary>
	/// A workspace in a temporary folder whose builds are answered by fake Players on this machine. The process of a build
	/// is this test process, so the workspace takes it for running.
	/// </summary>
	internal sealed class FakeWorkspace : IDisposable
	{
		private readonly List<FakePlayer> m_Players = new List<FakePlayer>();

		public FakeWorkspace(params string[] builds)
		{
			Directory = Path.Combine(Path.GetTempPath(), "DexcDashboard-" + Guid.NewGuid().ToString("N"));
			System.IO.Directory.CreateDirectory(Directory);
			var list = new JsonObject();
			foreach (var name in builds)
				list[name] = new JsonObject { ["player"] = Environment.ProcessPath };
			File.WriteAllText(Path.Combine(Directory, "workspace.json"), new JsonObject { ["builds"] = list }.ToJsonString());
			foreach (var name in builds)
			{
				var player = new FakePlayer();
				m_Players.Add(player);
				var state = Path.Combine(Directory, ".state", name);
				System.IO.Directory.CreateDirectory(state);
				File.WriteAllText(Path.Combine(state, "token"), player.Token);
				File.WriteAllText(Path.Combine(state, "port"), player.Port.ToString());
				File.WriteAllText(Path.Combine(state, "player.pid"), Environment.ProcessId.ToString());
			}
		}

		public string Directory { get; }

		public FakePlayer this[int index] => m_Players[index];

		public void Dispose()
		{
			foreach (var player in m_Players)
				player.Dispose();
			System.IO.Directory.Delete(Directory, true);
		}
	}

	/// <summary>Answers status, graph and answer the way AutopilotServer does, from what the test sets.</summary>
	internal sealed class FakePlayer : IDisposable
	{
		private readonly TcpListener m_Listener = new TcpListener(IPAddress.Loopback, 0);
		private readonly Thread m_Thread;

		public FakePlayer()
		{
			m_Listener.Start();
			Port = ((IPEndPoint)m_Listener.LocalEndpoint).Port;
			m_Thread = new Thread(Serve) { IsBackground = true };
			m_Thread.Start();
		}

		public string Token { get; } = Guid.NewGuid().ToString("N");

		public int Port { get; }

		public JsonObject Status = new JsonObject { ["loadCount"] = 1, ["active"] = "Quest", ["activeState"] = "Walk", ["steps"] = 3, ["log"] = new JsonArray(), ["decisions"] = new JsonArray() };

		public JsonObject Graph = new JsonObject { ["loadCount"] = 1, ["active"] = "Quest", ["machines"] = new JsonArray() };

		/// <summary>Every operation asked, in order, with its payload as text.</summary>
		public readonly ConcurrentQueue<(string Operation, string Payload)> Requests = new ConcurrentQueue<(string, string)>();

		public void Dispose()
		{
			m_Listener.Stop();
			m_Thread.Join(TimeSpan.FromSeconds(5));
		}

		private void Serve()
		{
			while (true)
			{
				TcpClient client;
				try
				{
					client = m_Listener.AcceptTcpClient();
				}
				catch (SocketException)
				{
					return;
				}

				using (client)
				using (var stream = client.GetStream())
				using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
				using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
				{
					var parts = (reader.ReadLine() ?? string.Empty).Split(' ');
					if (parts.Length < 2 || parts[0] != Token)
						continue;
					var payload = parts.Length > 2 ? Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])) : null;
					Requests.Enqueue((parts[1], payload));
					JsonNode reply;
					lock (this)
					{
						reply = parts[1] == "status" ? Status.DeepClone()
							: parts[1] == "graph" ? Graph.DeepClone()
							: parts[1].StartsWith("answer:", StringComparison.Ordinal) ? new JsonObject { ["ok"] = true }
							: new JsonObject { ["ok"] = false, ["error"] = "Unknown operation: " + parts[1] };
					}

					writer.WriteLine(reply.ToJsonString());
					writer.Flush();
				}
			}
		}
	}
}
