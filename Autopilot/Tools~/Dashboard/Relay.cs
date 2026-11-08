using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dexc.Autopilot.Compiler;

namespace Dexc.Autopilot.Dashboard
{
	/// <summary>A question a bot asked, as the dashboard follows it.</summary>
	internal sealed class RelayDecision
	{
		public const string Human = "human";
		public const string Agent = "agent";
		public const string Answered = "answered";
		public const string Withdrawn = "withdrawn";

		public string Build;
		public string Id;
		public string Question;
		public JsonArray Choices;
		public string Machine;
		public string State;
		public DateTime Asked;
		public DateTime AgentFrom;
		public string Stage = Human;
		public string Answer;
		public string AnsweredBy;

		public JsonObject ToJson() => new JsonObject
		{
			["kind"] = "decision",
			["build"] = Build,
			["id"] = Id,
			["question"] = Question,
			["choices"] = Choices.DeepClone(),
			["machine"] = Machine,
			["state"] = State,
			["asked"] = Asked.ToString("O"),
			["agentFrom"] = AgentFrom.ToString("O"),
			["stage"] = Stage,
			["answer"] = Answer,
			["answeredBy"] = AnsweredBy,
		};
	}

	/// <summary>
	/// Carries the questions of the bots to the people at the dashboard and, when nobody answers in time, to the agent
	/// driving the workspace; and carries what the people tell the agent. The agent takes what is for it one at a time,
	/// each once, by waiting on <see cref="Next"/>. Everything that happens is written to a journal in the workspace, so
	/// that a dashboard started again still shows how things went.
	/// </summary>
	internal sealed class Relay
	{
		private const int JournalShown = 200;

		private readonly object m_Gate = new object();
		private readonly Func<string, WorkspacePlayer> m_Player;
		private readonly Action<string, JsonNode> m_Publish;
		private readonly string m_JournalPath;
		private readonly Dictionary<(string Build, string Id), RelayDecision> m_Decisions = new Dictionary<(string, string), RelayDecision>();
		private readonly Queue<JsonObject> m_ForAgent = new Queue<JsonObject>();
		private readonly List<JsonObject> m_Journal = new List<JsonObject>();
		private readonly SemaphoreSlim m_Arrived = new SemaphoreSlim(0);
		private int m_Instructions;

		/// <param name="player">The Player of a build, to send the answers to.</param>
		/// <param name="publish">Tells the views.</param>
		public Relay(string journalPath, TimeSpan humanTimeout, Func<string, WorkspacePlayer> player, Action<string, JsonNode> publish)
		{
			m_JournalPath = journalPath;
			HumanTimeout = humanTimeout;
			m_Player = player;
			m_Publish = publish;
			if (File.Exists(journalPath))
			{
				foreach (var line in File.ReadLines(journalPath).TakeLast(JournalShown))
				{
					if (JsonNode.Parse(line) is JsonObject entry)
						m_Journal.Add(entry);
				}
			}
		}

		public TimeSpan HumanTimeout { get; }

		/// <summary>What a look at a build found waiting, or null when it is stopped.</summary>
		public void Observe(string build, JsonArray waiting, DateTime now)
		{
			var seen = new HashSet<string>();
			lock (m_Gate)
			{
				foreach (var item in waiting ?? new JsonArray())
				{
					var id = item["id"]?.GetValue<string>();
					var question = item["question"]?.GetValue<string>();
					if (id == null)
						continue;
					seen.Add(id);
					// A Player started again counts from d1: the same id with another question is another decision.
					if (m_Decisions.TryGetValue((build, id), out var known) && known.Question == question)
						continue;
					var elapsed = item["elapsed"]?.GetValue<double>() ?? 0;
					var decision = new RelayDecision
					{
						Build = build,
						Id = id,
						Question = question,
						Choices = item["choices"] is JsonArray choices ? (JsonArray)choices.DeepClone() : new JsonArray(),
						Machine = item["machine"]?.GetValue<string>(),
						State = item["state"]?.GetValue<string>(),
						Asked = now - TimeSpan.FromSeconds(elapsed),
					};
					decision.AgentFrom = decision.Asked + HumanTimeout;
					m_Decisions[(build, id)] = decision;
					Record("asked", decision.ToJson());
				}

				// Gone from the Player without an answer from here: the state that asked has exited.
				foreach (var decision in m_Decisions.Values.Where(decision => decision.Build == build && !seen.Contains(decision.Id) && IsOpen(decision)).ToList())
				{
					decision.Stage = RelayDecision.Withdrawn;
					Record("withdrawn", decision.ToJson());
				}

				if (waiting == null)
				{
					foreach (var key in m_Decisions.Keys.Where(key => key.Build == build).ToList())
						m_Decisions.Remove(key);
				}
			}
		}

		/// <summary>Hands to the agent what the people let wait too long.</summary>
		public void Tick(DateTime now)
		{
			lock (m_Gate)
			{
				foreach (var decision in m_Decisions.Values.Where(decision => decision.Stage == RelayDecision.Human && now >= decision.AgentFrom))
				{
					decision.Stage = RelayDecision.Agent;
					var entry = decision.ToJson();
					Record("escalated", entry);
					ForAgent(entry);
				}
			}
		}

		public JsonArray Decisions()
		{
			lock (m_Gate)
				return new JsonArray(m_Decisions.Values.Where(IsOpen).OrderBy(decision => decision.Asked).Select(decision => (JsonNode)decision.ToJson()).ToArray());
		}

		/// <summary>Sends the answer to the Player. It counts once the Player took it.</summary>
		public JsonObject Answer(string build, string id, string answer, string by)
		{
			RelayDecision decision;
			lock (m_Gate)
			{
				if (!m_Decisions.TryGetValue((build, id), out decision) || !IsOpen(decision))
					return new JsonObject { ["ok"] = false, ["error"] = "No decision " + id + " of " + build + " is waiting." };
			}

			var reply = m_Player(build).Send("answer:" + id + ":" + by, Encoding.UTF8.GetBytes(answer ?? string.Empty));
			if (reply["ok"]?.GetValue<bool>() != true)
				return reply;
			lock (m_Gate)
			{
				decision.Stage = RelayDecision.Answered;
				decision.Answer = answer;
				decision.AnsweredBy = by;
				Record("answered", decision.ToJson());
			}

			return reply;
		}

		/// <summary>What a person asks the agent to do, for some builds or for all when none are named.</summary>
		public JsonObject Instruct(string text, JsonArray builds)
		{
			lock (m_Gate)
			{
				m_Instructions++;
				var entry = new JsonObject
				{
					["kind"] = "instruction",
					["id"] = "i" + m_Instructions + "-" + DateTime.UtcNow.ToString("HHmmss"),
					["text"] = text,
					["builds"] = builds?.DeepClone() ?? new JsonArray(),
				};
				Record("instructed", entry);
				ForAgent(entry);
				return entry;
			}
		}

		/// <summary>What the agent tells the people: what it does, what it found.</summary>
		public void Say(string text)
		{
			lock (m_Gate)
				Record("said", new JsonObject { ["kind"] = "say", ["text"] = text });
		}

		/// <summary>Waits for the next thing for the agent, which it gets once. Null when nothing came in time.</summary>
		public async Task<JsonObject> Next(TimeSpan timeout, CancellationToken cancellation)
		{
			var deadline = DateTime.UtcNow + timeout;
			while (true)
			{
				var left = deadline - DateTime.UtcNow;
				if (!await m_Arrived.WaitAsync(left > TimeSpan.Zero ? left : TimeSpan.Zero, cancellation))
					return null;
				lock (m_Gate)
				{
					var entry = m_ForAgent.Dequeue();
					// A decision someone answered while it waited, or that was withdrawn, is no longer the agent's.
					if (entry["kind"]?.GetValue<string>() == "decision"
						&& (!m_Decisions.TryGetValue((entry["build"].GetValue<string>(), entry["id"].GetValue<string>()), out var decision) || !IsOpen(decision)))
					{
						continue;
					}

					Record("delivered", new JsonObject { ["kind"] = entry["kind"]?.DeepClone(), ["id"] = entry["id"]?.DeepClone(), ["build"] = entry["build"]?.DeepClone() });
					return entry;
				}
			}
		}

		public JsonArray Journal()
		{
			lock (m_Gate)
				return new JsonArray(m_Journal.Select(entry => (JsonNode)entry.DeepClone()).ToArray());
		}

		private static bool IsOpen(RelayDecision decision) => decision.Stage == RelayDecision.Human || decision.Stage == RelayDecision.Agent;

		private void ForAgent(JsonObject entry)
		{
			m_ForAgent.Enqueue((JsonObject)entry.DeepClone());
			m_Arrived.Release();
		}

		/// <summary>Into the journal, the file and the views. Under the gate.</summary>
		private void Record(string what, JsonObject detail)
		{
			var entry = new JsonObject { ["time"] = DateTime.UtcNow.ToString("O"), ["event"] = what, ["detail"] = detail.DeepClone() };
			m_Journal.Add(entry);
			if (m_Journal.Count > JournalShown)
				m_Journal.RemoveAt(0);
			Directory.CreateDirectory(Path.GetDirectoryName(m_JournalPath));
			File.AppendAllText(m_JournalPath, entry.ToJsonString() + "\n");
			m_Publish("journal", entry);
		}
	}
}
