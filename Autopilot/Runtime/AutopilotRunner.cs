using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dexc.Autopilot
{
	/// <summary>What the server asks of whatever is running the bots.</summary>
	public interface IAutopilotHost
	{
		IAutopilotBackend FindBackend(string name);
		IDictionary<string, object> Load(byte[] assembly, string backend);
		IDictionary<string, object> Status();

		/// <summary>The machines of the loaded bot, their states and where each Goto leads.</summary>
		IDictionary<string, object> Graph();

		/// <summary>Answers a decision a bot is waiting on.</summary>
		/// <param name="by">"human" or "agent".</param>
		IDictionary<string, object> Answer(string id, string answer, string by);
	}

	/// <summary>
	/// Runs the machines of the loaded bot.
	///
	/// A bot's delegates die with the VM that made them, so loading never patches what is running: it drops every machine
	/// and asks the new assembly to declare its own. What the bot knew survives that: the blackboards stay here, and each
	/// machine picks up in the state it was in. A failure in a bot stops the bot and leaves the game running.
	/// </summary>
	public sealed class AutopilotRunner<TContext> : IAutopilotHost
		where TContext : AutopilotContext
	{
		private const int LogCapacity = 40;
		private const string ConfigureMethod = "Configure";

		private readonly Dictionary<string, IAutopilotBackend> m_Backends = new Dictionary<string, IAutopilotBackend>();
		private readonly Dictionary<string, int> m_Switches = new Dictionary<string, int>();
		private readonly List<string> m_Log = new List<string>();
		private readonly Dictionary<string, AutopilotBlackboard> m_MachineBoards = new Dictionary<string, AutopilotBlackboard>();
		private readonly Dictionary<string, AutopilotBlackboard> m_StateBoards = new Dictionary<string, AutopilotBlackboard>();
		private readonly Dictionary<string, string> m_Resume = new Dictionary<string, string>();
		private readonly AutopilotBlackboard m_Shared = new AutopilotBlackboard();
		private readonly List<AutopilotDecision> m_Decisions = new List<AutopilotDecision>();
		private readonly TContext m_Context;
		private readonly string m_EntryType;
		private IAutopilotModule m_Module;
		private AutopilotBuilder<TContext> m_Builder;
		private AutopilotMachine<TContext> m_Active;
		private float m_NextSelect;
		private float m_MachineStarted;
		private float m_StateStarted;
		private int m_LoadCount;
		private int m_Steps;
		private int m_Asked;
		private string m_Failure;
		private IReadOnlyList<string> m_Missing = new string[0];

		/// <param name="context">What each machine is handed.</param>
		/// <param name="entryType">The type in a bot that declares a static Configure method.</param>
		public AutopilotRunner(TContext context, string entryType, params IAutopilotBackend[] backends)
		{
			m_Context = context;
			m_EntryType = entryType;
			m_Context.Shared = m_Shared;
			m_Context.AskHandler = Ask;
			foreach (var backend in backends)
				m_Backends[backend.Name] = backend;
		}

		public event Action<IDictionary<string, object>> StatusRequested;

		public IAutopilotBackend FindBackend(string name) => m_Backends.TryGetValue(name, out var backend) ? backend : null;

		public IDictionary<string, object> Load(byte[] assembly, string backend)
		{
			Unload();
			var result = new Dictionary<string, object>();
			try
			{
				m_Module = FindBackend(backend).Load(assembly);
				m_Missing = m_Module.UnboundMembers;
				if (m_Missing.Count > 0)
				{
					result["missing"] = m_Missing;
					Write("missing " + m_Missing.Count + " members");
				}

				var builder = new AutopilotBuilder<TContext>();
				m_Module.Invoke(m_EntryType, ConfigureMethod, builder);
				m_Builder = builder;
				Resume();
				m_LoadCount++;
				m_NextSelect = 0f;
				m_Failure = null;
				Write("loaded " + builder.Machines.Count + " machines on " + backend);
				result["loaded"] = true;
				result["machines"] = builder.Machines.Count;
			}
			catch (Exception exception)
			{
				m_Failure = exception.ToString();
				result["loaded"] = false;
				result["error"] = m_Failure;
				Unload();
			}

			return result;
		}

		public void Unload()
		{
			Withdraw(null, null);
			m_Active = null;
			m_Builder = null;
			m_Module?.Dispose();
			m_Module = null;
		}

		/// <summary>Decides which machine runs and runs one step of it. Returns whether a machine ran.</summary>
		public bool Tick()
		{
			if (m_Builder == null)
				return false;
			try
			{
				if (Time.time >= m_NextSelect)
				{
					m_NextSelect = Time.time + m_Builder.SelectInterval;
					Select();
				}

				if (m_Active?.Current == null)
					return false;
				m_Context.StateTime = Time.time - m_StateStarted;
				Transition();
				m_Active.Current.StepAction?.Invoke(m_Context);
				m_Steps++;
				return true;
			}
			catch (Exception exception)
			{
				m_Failure = exception.ToString();
				Write("failed: " + exception.Message);
				Unload();
				return false;
			}
		}

		public IDictionary<string, object> Status()
		{
			var result = new Dictionary<string, object>
			{
				["loadCount"] = m_LoadCount,
				["active"] = m_Active?.Name,
				["activeState"] = m_Active?.Current?.Name,
				["steps"] = m_Steps,
				["switches"] = new Dictionary<string, int>(m_Switches),
				["failure"] = m_Failure,
				["missing"] = m_Missing,
				["log"] = new List<string>(m_Log),
				["decisions"] = Decisions(),
			};
			StatusRequested?.Invoke(result);
			return result;
		}

		public IDictionary<string, object> Graph()
		{
			var machines = new List<object>();
			if (m_Builder != null)
			{
				foreach (var machine in m_Builder.Machines)
				{
					var states = new List<object>();
					foreach (var state in machine.States.Values)
					{
						var targets = new List<string>();
						foreach (var transition in state.Transitions)
							targets.Add(transition.Value);
						states.Add(new Dictionary<string, object> { ["name"] = state.Name, ["goto"] = targets });
					}

					machines.Add(new Dictionary<string, object>
					{
						["name"] = machine.Name,
						["when"] = machine.Condition != null,
						["dwell"] = machine.MinimumTime,
						["restartOnEnter"] = machine.Restarts,
						["first"] = machine.First?.Name,
						// An interrupted machine keeps its state and goes on from there.
						["current"] = machine.Current?.Name,
						["states"] = states,
					});
				}
			}

			return new Dictionary<string, object>
			{
				["loadCount"] = m_LoadCount,
				["active"] = m_Active?.Name,
				["selectInterval"] = m_Builder?.SelectInterval ?? 0f,
				["machines"] = machines,
			};
		}

		public IDictionary<string, object> Answer(string id, string answer, string by)
		{
			var decision = m_Decisions.Find(pending => pending.Id == id);
			if (decision == null)
				return new Dictionary<string, object> { ["ok"] = false, ["error"] = "No decision " + id + " is waiting." };
			if (answer == null || (decision.Choices.Count > 0 && !Contains(decision.Choices, answer)))
				return new Dictionary<string, object> { ["ok"] = false, ["error"] = "Not one of the choices: " + answer };
			decision.Settle(answer, by);
			m_Decisions.Remove(decision);
			Write("answer " + id + " by " + by + ": " + answer);
			return new Dictionary<string, object> { ["ok"] = true };
		}

		public void Write(string text)
		{
			m_Log.Add(Time.time.ToString("0.00") + " " + text);
			if (m_Log.Count > LogCapacity)
				m_Log.RemoveAt(0);
		}

		private AutopilotDecision Ask(string question, string[] choices)
		{
			m_Asked++;
			var decision = new AutopilotDecision("d" + m_Asked, question, choices, m_Active?.Name, m_Active?.Current?.Name);
			m_Decisions.Add(decision);
			Write("ask " + decision.Id + ": " + question);
			return decision;
		}

		/// <summary>Takes back what the state asked; null for both takes back everything.</summary>
		private void Withdraw(string machine, string state)
		{
			for (var index = m_Decisions.Count - 1; index >= 0; index--)
			{
				var decision = m_Decisions[index];
				if (machine != null && (decision.Machine != machine || decision.State != state))
					continue;
				decision.IsWithdrawn = true;
				m_Decisions.RemoveAt(index);
				Write("withdraw " + decision.Id);
			}
		}

		private List<object> Decisions()
		{
			var decisions = new List<object>();
			foreach (var decision in m_Decisions)
			{
				decisions.Add(new Dictionary<string, object>
				{
					["id"] = decision.Id,
					["question"] = decision.Question,
					["choices"] = decision.Choices,
					["machine"] = decision.Machine,
					["state"] = decision.State,
					["elapsed"] = decision.Elapsed,
				});
			}

			return decisions;
		}

		private static bool Contains(IReadOnlyList<string> choices, string answer)
		{
			foreach (var choice in choices)
			{
				if (choice == answer)
					return true;
			}

			return false;
		}

		private void ExitState(AutopilotMachine<TContext> machine)
		{
			machine.Current.ExitAction?.Invoke(m_Context);
			Withdraw(machine.Name, machine.Current.Name);
		}

		private void Select()
		{
			if (m_Active != null && Time.time - m_MachineStarted < m_Active.MinimumTime)
				return;
			foreach (var machine in m_Builder.Machines)
			{
				if (machine.Condition == null || !machine.Condition(m_Context))
					continue;
				if (machine != m_Active)
					Switch(machine);
				return;
			}

			Switch(null);
		}

		private void Switch(AutopilotMachine<TContext> machine)
		{
			if (m_Active == machine)
				return;
			// The state it was in is kept: an interrupted machine goes on from there.
			if (m_Active?.Current != null)
				ExitState(m_Active);
			m_Active = machine;
			if (machine == null)
				return;
			m_MachineStarted = Time.time;
			m_Switches.TryGetValue(machine.Name, out var count);
			m_Switches[machine.Name] = count + 1;
			Write("switch " + machine.Name);
			m_Context.Machine = Board(m_MachineBoards, machine.Name, m_Shared);
			if (machine.Restarts || machine.Current == null)
			{
				m_Context.Machine.Clear();
				Board(m_StateBoards, machine.Name, m_Context.Machine).Clear();
				EnterState(machine, machine.First);
				return;
			}

			EnterState(machine, machine.Current);
		}

		private void Transition()
		{
			foreach (var transition in m_Active.Current.Transitions)
			{
				if (!transition.Key(m_Context))
					continue;
				var next = m_Active.State(transition.Value);
				ExitState(m_Active);
				m_Context.State.Clear();
				EnterState(m_Active, next);
				return;
			}
		}

		private void EnterState(AutopilotMachine<TContext> machine, AutopilotState<TContext> state)
		{
			machine.Current = state;
			if (state == null)
				return;
			m_Resume[machine.Name] = state.Name;
			m_StateStarted = Time.time;
			m_Context.StateTime = 0f;
			m_Context.State = Board(m_StateBoards, machine.Name, m_Context.Machine);
			Write(machine.Name + " > " + state.Name);
			state.EnterAction?.Invoke(m_Context);
		}

		/// <summary>Puts each machine of a new bot back in the state its last version was in, if it still has that state.</summary>
		private void Resume()
		{
			foreach (var machine in m_Builder.Machines)
			{
				if (m_Resume.TryGetValue(machine.Name, out var state) && machine.States.TryGetValue(state, out var known))
					machine.Current = known;
			}
		}

		private static AutopilotBlackboard Board(Dictionary<string, AutopilotBlackboard> boards, string key, AutopilotBlackboard parent)
		{
			if (!boards.TryGetValue(key, out var board))
			{
				board = new AutopilotBlackboard(parent);
				boards[key] = board;
			}

			return board;
		}
	}
}
