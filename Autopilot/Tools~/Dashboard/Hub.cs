using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Dexc.Autopilot.Compiler;

namespace Dexc.Autopilot.Dashboard
{
	/// <summary>What the dashboard knows of one build, as of its last look.</summary>
	internal sealed class BuildView
	{
		public const int HistoryCapacity = 600;

		public string Name;
		public bool Running;
		public string Error;
		public DateTime Updated;
		public JsonObject Status;
		public JsonObject Graph;
		public long GraphLoadCount = -1;
		public readonly Queue<JsonObject> History = new Queue<JsonObject>();
	}

	/// <summary>
	/// The data side of the dashboard: it looks at every build of the workspace at the interval and tells the views what
	/// changed. The views are pages that read it through the API only. A Player is asked one thing at a time: while a look
	/// at a build is under way, the next one for it waits.
	/// </summary>
	internal sealed class Hub
	{
		private readonly string m_Directory;
		private readonly ConcurrentDictionary<string, BuildView> m_Builds = new ConcurrentDictionary<string, BuildView>(StringComparer.OrdinalIgnoreCase);
		private readonly ConcurrentDictionary<string, Task> m_Looking = new ConcurrentDictionary<string, Task>(StringComparer.OrdinalIgnoreCase);
		private readonly List<Channel<string>> m_Listeners = new List<Channel<string>>();

		public Hub(string directory, TimeSpan humanTimeout)
		{
			m_Directory = directory;
			Relay = new Relay(Path.Combine(Compiler.Workspace.Open(directory).Dashboard, "journal.jsonl"), humanTimeout, name => new WorkspacePlayer(Workspace(), name), Publish);
		}

		public Relay Relay { get; }

		/// <summary>How often the reports of the Players are gathered into defects.</summary>
		public static readonly TimeSpan GatherInterval = TimeSpan.FromSeconds(5);

		private DateTime m_NextGather;
		private DateTime m_DefectsWritten;

		/// <summary>Takes the reports of the Players into defects, tells the agent of new and returning ones, and the views of any change.</summary>
		public QAChanges GatherDefects()
		{
			QAChanges changes;
			try
			{
				changes = QAStore.Gather(Workspace());
			}
			catch (Exception exception) when (exception is IOException || exception is System.Text.Json.JsonException)
			{
				Console.Error.WriteLine("Defects: " + exception.Message);
				return new QAChanges();
			}

			foreach (var defect in changes.Created)
				Relay.Defect("found", defect);
			foreach (var defect in changes.Recurred)
				Relay.Defect("recurred", defect);
			// ws qa set changes the file too, outside the dashboard.
			var path = Path.Combine(QAStore.Folder(Workspace()), "defects.json");
			var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
			if (changes.Any || written != m_DefectsWritten)
			{
				m_DefectsWritten = written;
				Publish("qa", new JsonObject());
			}

			return changes;
		}

		/// <summary>What the Players sent over OTLP.</summary>
		public Perf Perf { get; } = new Perf();

		public string Directory => m_Directory;

		public Workspace Workspace() => Compiler.Workspace.Open(m_Directory);

		public IReadOnlyList<BuildView> Builds => m_Builds.Values.OrderBy(build => build.Name, StringComparer.OrdinalIgnoreCase).ToList();

		public BuildView Build(string name) => m_Builds.TryGetValue(name, out var build) ? build : null;

		/// <summary>Looks at the builds until cancelled.</summary>
		public async Task Run(TimeSpan interval, CancellationToken cancellation)
		{
			while (!cancellation.IsCancellationRequested)
			{
				LookAll();
				Relay.Tick(DateTime.UtcNow);
				if (DateTime.UtcNow >= m_NextGather)
				{
					m_NextGather = DateTime.UtcNow + GatherInterval;
					GatherDefects();
				}
				try
				{
					await Task.Delay(interval, cancellation);
				}
				catch (TaskCanceledException)
				{
					return;
				}
			}
		}

		/// <summary>Starts a look at each build that is not being looked at already. The workspace is read again each time.</summary>
		public IReadOnlyList<Task> LookAll()
		{
			var workspace = Workspace();
			foreach (var gone in m_Builds.Keys.Where(name => !workspace.File.Builds.ContainsKey(name)).ToList())
				m_Builds.TryRemove(gone, out _);
			var looks = new List<Task>();
			foreach (var name in workspace.File.Builds.Keys)
			{
				if (m_Looking.TryGetValue(name, out var running) && !running.IsCompleted)
					continue;
				var look = Task.Run(() => Look(workspace, name));
				m_Looking[name] = look;
				looks.Add(look);
			}

			return looks;
		}

		private void Look(Workspace workspace, string name)
		{
			var build = m_Builds.GetOrAdd(name, key => new BuildView { Name = key });
			var player = new WorkspacePlayer(workspace, name);
			var before = Summary(build).ToJsonString();
			try
			{
				build.Running = player.IsRunning;
				if (!build.Running)
				{
					build.Status = null;
					build.Error = null;
					Relay.Observe(name, null, DateTime.UtcNow);
				}
				else
				{
					var status = player.Send("status", null);
					build.Status = status;
					build.Error = null;
					Remember(build, status);
					Relay.Observe(name, status["decisions"] as JsonArray ?? new JsonArray(), DateTime.UtcNow);
					var loadCount = status["loadCount"]?.GetValue<long>() ?? 0;
					if (loadCount != build.GraphLoadCount)
					{
						build.Graph = player.Send("graph", null);
						build.GraphLoadCount = loadCount;
						Publish("graph", new JsonObject { ["name"] = name, ["graph"] = build.Graph.DeepClone() });
					}
				}
			}
			catch (Exception exception)
			{
				build.Error = exception.Message;
			}

			build.Updated = DateTime.UtcNow;
			var after = Summary(build);
			if (after.ToJsonString() != before || build.Status != null)
				Publish("build", Full(build));
		}

		private static void Remember(BuildView build, JsonObject status)
		{
			lock (build.History)
			{
				build.History.Enqueue(new JsonObject
				{
					["time"] = DateTime.UtcNow.ToString("O"),
					["active"] = status["active"]?.DeepClone(),
					["activeState"] = status["activeState"]?.DeepClone(),
					["steps"] = status["steps"]?.DeepClone(),
				});
				while (build.History.Count > BuildView.HistoryCapacity)
					build.History.Dequeue();
			}
		}

		/// <summary>The few things a list of builds shows.</summary>
		public static JsonObject Summary(BuildView build) => new JsonObject
		{
			["name"] = build.Name,
			["running"] = build.Running,
			["error"] = build.Error,
			["active"] = build.Status?["active"]?.DeepClone(),
			["activeState"] = build.Status?["activeState"]?.DeepClone(),
			["steps"] = build.Status?["steps"]?.DeepClone(),
			["failure"] = build.Status?["failure"]?.DeepClone(),
			["decisions"] = build.Status?["decisions"] is JsonArray decisions ? decisions.Count : 0,
		};

		public static JsonObject Full(BuildView build) => new JsonObject
		{
			["name"] = build.Name,
			["running"] = build.Running,
			["error"] = build.Error,
			["updated"] = build.Updated.ToString("O"),
			["status"] = build.Status?.DeepClone(),
		};

		public static JsonArray History(BuildView build)
		{
			lock (build.History)
				return new JsonArray(build.History.Select(entry => (JsonNode)entry.DeepClone()).ToArray());
		}

		/// <summary>A view that listens gets every event from now on, one "event: name" and "data: json" per event.</summary>
		public ChannelReader<string> Listen()
		{
			var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
			lock (m_Listeners)
				m_Listeners.Add(channel);
			return channel.Reader;
		}

		public void Forget(ChannelReader<string> reader)
		{
			lock (m_Listeners)
				m_Listeners.RemoveAll(channel => channel.Reader == reader);
		}

		public void Publish(string name, JsonNode data)
		{
			var text = "event: " + name + "\ndata: " + data.ToJsonString() + "\n\n";
			lock (m_Listeners)
			{
				foreach (var channel in m_Listeners)
					channel.Writer.TryWrite(text);
			}
		}
	}
}
