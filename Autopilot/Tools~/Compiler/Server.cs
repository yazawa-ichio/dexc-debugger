using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Dexc.Autopilot.Compiler
{
	/// <summary>
	/// Takes one JSON line per connection and answers with one. Loopback only. A compile holds the lock, so requests that
	/// arrive together run one after another; each is short, and that keeps the kept compilations simple.
	/// Left alone it exits, so a server a client started does not outlive the work. A watching server stays: saves are its
	/// requests, and it could not say it had gone.
	/// </summary>
	internal sealed class Server
	{
		private const int WatchDelayMilliseconds = 150;
		private const int IdleMilliseconds = 30 * 60 * 1000;

		private static readonly string[] s_DefaultUsings =
		{
			"DexcDebugger.Samples.StealthStageDemo",
			"DexcDebugger.Samples.StealthStageDemo.Autopilot",
			"UnityEngine",
		};

		private readonly object m_Gate = new object();
		private readonly ManualResetEventSlim m_Stopped = new ManualResetEventSlim();
		private readonly Dictionary<string, BuildEngine> m_Engines = new Dictionary<string, BuildEngine>(StringComparer.OrdinalIgnoreCase);
		private readonly string m_ResponseFile;
		private readonly PlayerLink m_Player;
		private readonly int m_Port;
		private readonly string m_WatchVm;
		private readonly string m_WatchDirectory;
		private Timer m_IdleTimer;
		private Timer m_WatchTimer;
		private FileSystemWatcher m_Watcher;

		public Server(string root, int port, string watchVm, string watchDirectory)
		{
			var work = Path.Combine(root, "Logs", "UnityTests", "Autopilot");
			m_ResponseFile = Path.Combine(work, "refs.rsp");
			m_Player = new PlayerLink(Path.Combine(work, "token"), null);
			m_Port = port;
			m_WatchVm = watchVm;
			m_WatchDirectory = watchDirectory;
		}

		public void Run()
		{
			var listener = new TcpListener(IPAddress.Loopback, m_Port);
			listener.Start();
			Console.WriteLine("Compiler on port " + m_Port);
			if (m_WatchDirectory != null)
				Watch();
			else
				m_IdleTimer = new Timer(_ => m_Stopped.Set(), null, IdleMilliseconds, Timeout.Infinite);
			Task.Run(() => Accept(listener));
			Task.Run(WarmUp);
			m_Stopped.Wait();
			listener.Stop();
			lock (m_Gate)
			{
				foreach (var engine in m_Engines.Values)
					engine.Dispose();
			}
		}

		/// <summary>The references a request compiles against, kept for as long as the server runs. Call with the lock held.</summary>
		private BuildEngine Engine(JsonObject request)
		{
			var responseFile = (string)request["rsp"] ?? m_ResponseFile;
			if (!m_Engines.TryGetValue(responseFile, out var engine))
				m_Engines[responseFile] = engine = new BuildEngine(responseFile, Path.Combine(Path.GetDirectoryName(responseFile), "refs"));
			return engine;
		}

		private PlayerLink Player(JsonObject request) =>
			request["port"] == null ? m_Player : new PlayerLink((string)request["token"], int.Parse((string)request["port"]));

		/// <summary>The first compile loads the references and compiles Roslyn itself. A request should not be what pays for that.</summary>
		private void WarmUp()
		{
			try
			{
				lock (m_Gate)
				{
					var engine = Engine(new JsonObject());
					engine.CompileSnippet("0", "ilruntime", s_DefaultUsings);
					engine.CompileSnippet("0", "pipeline", s_DefaultUsings);
					if (m_WatchDirectory != null)
						engine.CompileProject(new[] { m_WatchDirectory }, Path.GetFileName(m_WatchDirectory), m_WatchVm);
				}
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception.Message);
			}
		}

		private void Accept(TcpListener listener)
		{
			while (true)
			{
				TcpClient client;
				try
				{
					client = listener.AcceptTcpClient();
				}
				catch (SocketException)
				{
					return;
				}

				Task.Run(() => Serve(client));
			}
		}

		private void Serve(TcpClient client)
		{
			m_IdleTimer?.Change(IdleMilliseconds, Timeout.Infinite);
			using (client)
			using (var stream = client.GetStream())
			using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
			using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
			{
				JsonObject answer;
				try
				{
					answer = Handle(JsonNode.Parse(reader.ReadLine() ?? "{}").AsObject());
				}
				catch (Exception exception)
				{
					answer = new JsonObject { ["ok"] = false, ["error"] = exception.ToString() };
				}

				writer.WriteLine(answer.ToJsonString());
				writer.Flush();
			}
		}

		private JsonObject Handle(JsonObject request)
		{
			switch ((string)request["op"])
			{
				case "eval":
					return Eval(request);
				case "bot":
					return Bot(request);
				case "status":
					lock (m_Gate)
						return new JsonObject { ["ok"] = true, ["references"] = Engine(request).ReferenceCount, ["engines"] = m_Engines.Count, ["watching"] = m_WatchDirectory };
				case "stop":
					m_Stopped.Set();
					return new JsonObject { ["ok"] = true };
				default:
					throw new ArgumentException("Unknown operation: " + request["op"]);
			}
		}

		private JsonObject Eval(JsonObject request)
		{
			var clock = Stopwatch.StartNew();
			var vm = (string)request["vm"] ?? "ilruntime";
			var named = (string)request["usings"];
			var usings = named == null ? s_DefaultUsings : named.Split(';', StringSplitOptions.RemoveEmptyEntries);
			BuildResult result;
			lock (m_Gate)
				result = Engine(request).CompileSnippet((string)request["snippet"], vm, usings);
			if (!result.Ok)
				return Refused(result);
			if (request["send"] == null)
				return Compiled(result, true);
			var verb = request["keep"] != null ? "eval-keep:" : "eval:";
			var answer = Player(request).Send(verb + vm, result.Assembly);
			answer["compileMs"] = result.Milliseconds;
			answer["totalMs"] = clock.ElapsedMilliseconds;
			AddWarnings(answer, result);
			return answer;
		}

		private JsonObject Bot(JsonObject request)
		{
			var directories = ((string)request["dirs"]).Split(';', StringSplitOptions.RemoveEmptyEntries);
			var name = (string)request["name"] ?? Path.GetFileName(directories[0].TrimEnd('/', '\\'));
			var vm = (string)request["vm"] ?? "ilruntime";
			BuildResult result;
			lock (m_Gate)
				result = Engine(request).CompileProject(directories, name, vm);
			if (!result.Ok)
				return Refused(result);
			var path = (string)request["out"];
			if (!string.IsNullOrEmpty(path))
				File.WriteAllBytes(path, result.Assembly);
			var answer = Compiled(result, string.IsNullOrEmpty(path) && request["load"] == null);
			if (request["load"] != null)
				answer["player"] = Player(request).Send("load:" + vm, result.Assembly);
			return answer;
		}

		private static JsonObject Refused(BuildResult result)
		{
			var answer = new JsonObject { ["ok"] = false, ["compile"] = ToArray(result.Errors), ["compileMs"] = result.Milliseconds };
			AddWarnings(answer, result);
			return answer;
		}

		private static JsonObject Compiled(BuildResult result, bool withImage)
		{
			var answer = new JsonObject { ["ok"] = true, ["compileMs"] = result.Milliseconds, ["size"] = result.Assembly.Length };
			if (withImage)
				answer["assembly"] = Convert.ToBase64String(result.Assembly);
			AddWarnings(answer, result);
			return answer;
		}

		private static void AddWarnings(JsonObject answer, BuildResult result)
		{
			if (result.Warnings.Count > 0)
				answer["warnings"] = ToArray(result.Warnings);
		}

		private static JsonArray ToArray(List<string> items)
		{
			var array = new JsonArray();
			foreach (var item in items)
				array.Add(item);
			return array;
		}

		private void Watch()
		{
			m_WatchTimer = new Timer(_ => LoadWatched());
			m_Watcher = new FileSystemWatcher(m_WatchDirectory, "*.cs") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
			FileSystemEventHandler changed = (_, change) =>
			{
				if (!change.FullPath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
					m_WatchTimer.Change(WatchDelayMilliseconds, Timeout.Infinite);
			};
			m_Watcher.Changed += changed;
			m_Watcher.Created += changed;
			m_Watcher.Deleted += changed;
			m_Watcher.Renamed += (_, change) => changed(null, change);
			m_Watcher.EnableRaisingEvents = true;
			Console.WriteLine("Watching " + m_WatchDirectory);
		}

		private void LoadWatched()
		{
			try
			{
				BuildResult result;
				lock (m_Gate)
					result = Engine(new JsonObject()).CompileProject(new[] { m_WatchDirectory }, Path.GetFileName(m_WatchDirectory), m_WatchVm);
				if (!result.Ok)
				{
					Console.WriteLine(string.Join(Environment.NewLine, result.Errors));
					return;
				}

				var answer = m_Player.Send("load:" + m_WatchVm, result.Assembly);
				Console.WriteLine("Loaded in " + result.Milliseconds + " ms: " + answer.ToJsonString());
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception.Message);
			}
		}
	}

	/// <summary>The Player's server takes one request per connection, so each send opens its own.</summary>
	internal sealed class PlayerLink
	{
		private const int TimeoutMilliseconds = 30000;

		private readonly string m_TokenFile;
		private readonly int? m_Port;

		/// <param name="port">Null for DEXC_AUTOPILOT_PORT, read when a request goes out.</param>
		public PlayerLink(string tokenFile, int? port)
		{
			m_TokenFile = tokenFile;
			m_Port = port;
		}

		public JsonObject Send(string operation, byte[] payload)
		{
			var named = Environment.GetEnvironmentVariable("DEXC_AUTOPILOT_PORT");
			var port = m_Port ?? (string.IsNullOrEmpty(named) ? 7890 : int.Parse(named));
			using var client = new TcpClient { NoDelay = true, ReceiveTimeout = TimeoutMilliseconds, SendTimeout = TimeoutMilliseconds };
			client.Connect(IPAddress.Loopback, port);
			using var stream = client.GetStream();
			using var writer = new StreamWriter(stream, new UTF8Encoding(false));
			using var reader = new StreamReader(stream, new UTF8Encoding(false));
			var line = File.ReadAllText(m_TokenFile).Trim() + " " + operation;
			writer.WriteLine(payload == null ? line : line + " " + Convert.ToBase64String(payload));
			writer.Flush();
			return JsonNode.Parse(reader.ReadLine()).AsObject();
		}
	}
}
