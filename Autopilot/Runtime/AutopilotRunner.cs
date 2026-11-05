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
	}

	/// <summary>
	/// Runs the machines of the loaded bot.
	///
	/// A bot's delegates die with the VM that made them, so loading never patches what is running: it drops every machine
	/// and asks the new assembly to declare its own. A failure in a bot stops the bot and leaves the game running.
	/// </summary>
	public sealed class AutopilotRunner<TContext> : IAutopilotHost
		where TContext : AutopilotContext
	{
		private const int LogCapacity = 40;
		private const string ConfigureMethod = "Configure";

		private readonly Dictionary<string, IAutopilotBackend> m_Backends = new Dictionary<string, IAutopilotBackend>();
		private readonly Dictionary<string, int> m_Switches = new Dictionary<string, int>();
		private readonly List<string> m_Log = new List<string>();
		private readonly TContext m_Context;
		private readonly string m_EntryType;
		private IAutopilotModule m_Module;
		private AutopilotBuilder<TContext> m_Builder;
		private AutopilotMachine<TContext> m_Active;
		private float m_NextSelect;
		private int m_LoadCount;
		private int m_Steps;
		private string m_Failure;

		/// <param name="context">What each machine is handed.</param>
		/// <param name="entryType">The type in a bot that declares a static Configure method.</param>
		public AutopilotRunner(TContext context, string entryType, params IAutopilotBackend[] backends)
		{
			m_Context = context;
			m_EntryType = entryType;
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
				var builder = new AutopilotBuilder<TContext>();
				m_Module.Invoke(m_EntryType, ConfigureMethod, builder);
				m_Builder = builder;
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

				if (m_Active?.Action == null)
					return false;
				m_Active.Action(m_Context);
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
				["steps"] = m_Steps,
				["switches"] = new Dictionary<string, int>(m_Switches),
				["failure"] = m_Failure,
				["log"] = new List<string>(m_Log),
			};
			StatusRequested?.Invoke(result);
			return result;
		}

		public void Write(string text)
		{
			m_Log.Add(Time.time.ToString("0.00") + " " + text);
			if (m_Log.Count > LogCapacity)
				m_Log.RemoveAt(0);
		}

		private void Select()
		{
			foreach (var machine in m_Builder.Machines)
			{
				if (machine.Condition == null || !machine.Condition(m_Context))
					continue;
				if (machine != m_Active)
				{
					m_Active = machine;
					m_Switches.TryGetValue(machine.Name, out var count);
					m_Switches[machine.Name] = count + 1;
					Write("switch " + machine.Name);
				}

				return;
			}

			m_Active = null;
		}
	}
}
