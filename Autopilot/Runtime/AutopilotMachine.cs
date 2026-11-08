using System;
using System.Collections.Generic;

namespace Dexc.Autopilot
{
	/// <summary>What a bot sees each step. A game derives from this to hand its bots typed access.</summary>
	public class AutopilotContext
	{
		public float Time => UnityEngine.Time.time;

		/// <summary>Lives as long as the runner, through every machine and every reload of the bot.</summary>
		public AutopilotBlackboard Shared { get; internal set; }

		/// <summary>Belongs to the running machine, and is still there when it comes back from an interruption.</summary>
		public AutopilotBlackboard Machine { get; internal set; }

		/// <summary>Belongs to the running state, and is gone when the state exits.</summary>
		public AutopilotBlackboard State { get; internal set; }

		/// <summary>Seconds the running state has been running.</summary>
		public float StateTime { get; internal set; }
	}

	public sealed class AutopilotState<TContext>
		where TContext : AutopilotContext
	{
		internal readonly List<KeyValuePair<Func<TContext, bool>, string>> Transitions =
			new List<KeyValuePair<Func<TContext, bool>, string>>();
		internal Action<TContext> EnterAction;
		internal Action<TContext> StepAction;
		internal Action<TContext> ExitAction;

		internal AutopilotState(string name) => Name = name;

		public string Name { get; }

		public AutopilotState<TContext> Enter(Action<TContext> action)
		{
			EnterAction = action;
			return this;
		}

		public AutopilotState<TContext> Step(Action<TContext> action)
		{
			StepAction = action;
			return this;
		}

		public AutopilotState<TContext> Exit(Action<TContext> action)
		{
			ExitAction = action;
			return this;
		}

		/// <summary>Moves to another state of the same machine when the condition holds. The first that holds wins.</summary>
		public AutopilotState<TContext> Goto(string state, Func<TContext, bool> condition)
		{
			Transitions.Add(new KeyValuePair<Func<TContext, bool>, string>(condition, state));
			return this;
		}
	}

	/// <summary>
	/// One goal of a bot, as states. Machines take over from one another by condition, so an interruption is just another
	/// machine that wants to run: the one it replaced stays in its state and goes on from there when it comes back.
	/// </summary>
	public sealed class AutopilotMachine<TContext>
		where TContext : AutopilotContext
	{
		private const string DefaultState = "Main";

		internal readonly Dictionary<string, AutopilotState<TContext>> States = new Dictionary<string, AutopilotState<TContext>>();
		internal Func<TContext, bool> Condition;
		internal AutopilotState<TContext> Current;
		internal AutopilotState<TContext> First;
		internal float MinimumTime;
		internal bool Restarts;

		internal AutopilotMachine(string name) => Name = name;

		public string Name { get; }

		public AutopilotMachine<TContext> When(Func<TContext, bool> condition)
		{
			Condition = condition;
			return this;
		}

		/// <summary>Keeps this machine running at least this long once it starts, so two conditions cannot fight every frame.</summary>
		public AutopilotMachine<TContext> Dwell(float seconds)
		{
			MinimumTime = seconds;
			return this;
		}

		/// <summary>Starts over from the first state each time it takes over, rather than going on from where it was.</summary>
		public AutopilotMachine<TContext> RestartOnEnter()
		{
			Restarts = true;
			return this;
		}

		public AutopilotState<TContext> State(string name)
		{
			if (States.TryGetValue(name, out var state))
				return state;
			state = new AutopilotState<TContext>(name);
			States[name] = state;
			First = First ?? state;
			return state;
		}

		/// <summary>A machine with a single state.</summary>
		public AutopilotMachine<TContext> Step(Action<TContext> action)
		{
			State(DefaultState).Step(action);
			return this;
		}
	}

	/// <summary>What a loaded bot declares. Earlier machines win when several want to run.</summary>
	public sealed class AutopilotBuilder<TContext>
		where TContext : AutopilotContext
	{
		internal readonly List<AutopilotMachine<TContext>> Machines = new List<AutopilotMachine<TContext>>();

		/// <summary>Seconds between decisions about which machine runs. Zero decides every frame.</summary>
		public float SelectInterval { get; set; }

		public AutopilotMachine<TContext> Machine(string name)
		{
			var machine = new AutopilotMachine<TContext>(name);
			Machines.Add(machine);
			return machine;
		}
	}
}
