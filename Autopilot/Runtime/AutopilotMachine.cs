using System;
using System.Collections.Generic;

namespace Dexc.Autopilot
{
	/// <summary>What a bot sees each step. A game derives from this to hand its bots typed access.</summary>
	public class AutopilotContext
	{
		public float Time => UnityEngine.Time.time;
	}

	public sealed class AutopilotMachine<TContext>
		where TContext : AutopilotContext
	{
		internal AutopilotMachine(string name) => Name = name;

		public string Name { get; }
		internal Func<TContext, bool> Condition;
		internal Action<TContext> Action;

		public AutopilotMachine<TContext> When(Func<TContext, bool> condition)
		{
			Condition = condition;
			return this;
		}

		public AutopilotMachine<TContext> Step(Action<TContext> action)
		{
			Action = action;
			return this;
		}
	}

	/// <summary>What a loaded bot declares. Earlier machines win when several want to run.</summary>
	public sealed class AutopilotBuilder<TContext>
		where TContext : AutopilotContext
	{
		internal readonly List<AutopilotMachine<TContext>> Machines = new List<AutopilotMachine<TContext>>();

		/// <summary>Seconds between decisions about which machine runs.</summary>
		public float SelectInterval { get; set; } = 1f;

		public AutopilotMachine<TContext> Machine(string name)
		{
			var machine = new AutopilotMachine<TContext>(name);
			Machines.Add(machine);
			return machine;
		}
	}
}
