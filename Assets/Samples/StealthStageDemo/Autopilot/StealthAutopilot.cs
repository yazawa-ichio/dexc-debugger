using System;
using System.Collections.Generic;
using Dexc.Autopilot.Bridge;
using UnityEngine;

namespace DexcDebugger.Samples.StealthStageDemo.Autopilot
{
	/// <summary>What a bot sees and does each step. Everything it touches in the game it reaches through typed code.</summary>
	public sealed class AutopilotContext
	{
		internal Vector2 MoveValue;
		internal bool RetryValue;

		public StealthStageDemoController Game => StealthStageDemoController.Instance;
		public float Time => UnityEngine.Time.time;

		public void Move(Vector2 value) => MoveValue = value;

		public void Stop() => MoveValue = Vector2.zero;

		public void PressRetry() => RetryValue = true;
	}

	public sealed class AutopilotMachine
	{
		internal AutopilotMachine(string name) => Name = name;

		public string Name { get; }
		internal Func<AutopilotContext, bool> Condition;
		internal Action<AutopilotContext> Action;

		public AutopilotMachine When(Func<AutopilotContext, bool> condition)
		{
			Condition = condition;
			return this;
		}

		public AutopilotMachine Step(Action<AutopilotContext> action)
		{
			Action = action;
			return this;
		}
	}

	/// <summary>What a loaded bot declares. Earlier machines win when several want to run.</summary>
	public sealed class AutopilotBuilder
	{
		internal readonly List<AutopilotMachine> Machines = new List<AutopilotMachine>();
		internal readonly Dictionary<string, object> Reports = new Dictionary<string, object>();

		/// <summary>Seconds between decisions about which machine runs.</summary>
		public float SelectInterval { get; set; } = 1f;

		public AutopilotMachine Machine(string name)
		{
			var machine = new AutopilotMachine(name);
			Machines.Add(machine);
			return machine;
		}

		public void Report(string key, string value) => Reports[key] = value;
	}

	/// <summary>
	/// Runs the machines of the loaded bot.
	///
	/// A bot's delegates die with the interpreter that made them, so loading never patches what is running: it drops
	/// every machine and asks the new assembly to declare its own. The interpreter has no exception handling, so this
	/// is also where a bot's failure is caught.
	/// </summary>
	public sealed class StealthAutopilot : MonoBehaviour
	{
		private const int LogCapacity = 40;
		private const string BotEntry = "StealthBot.Entry";

		private readonly AutopilotContext m_Context = new AutopilotContext();
		private readonly List<string> m_Log = new List<string>();
		private StealthAutopilotGamepad m_Gamepad;
		private StealthAutopilotServer m_Server;
		private DexcIlModule m_Module;
		private Dexc.Autopilot.DexcILRuntimeModule m_ILRuntime;
		private AutopilotBuilder m_Builder;
		private AutopilotMachine m_Active;
		private float m_NextSelect;
		private Dictionary<string, int> m_Switches = new Dictionary<string, int>();
		private int m_LoadCount;
		private int m_Steps;
		private int m_StagesCleared;
		private StealthStageState m_LastState;
		private float m_MaxMove;
		private string m_Failure;

		public static StealthAutopilot Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Boot()
		{
			var host = new GameObject("StealthAutopilot");
			DontDestroyOnLoad(host);
			Instance = host.AddComponent<StealthAutopilot>();
		}

		private void Awake()
		{
			m_Gamepad = StealthAutopilotGamepad.Create();
			m_Server = StealthAutopilotServer.TryStart(this);
		}

		private void OnDestroy() => m_Server?.Dispose();

		/// <summary>The game's own types, whose private members interpreted code may reach.</summary>
		internal static List<Type> OpenTypes()
		{
			var open = new List<Type>();
			foreach (var type in typeof(StealthStageDemoController).Assembly.GetTypes())
			{
				if (!type.IsGenericTypeDefinition && type.Name.IndexOf('<') < 0)
					open.Add(type);
			}

			return open;
		}

		public IDictionary<string, object> Load(byte[] assembly, bool ilruntime = false)
		{
			Unload();
			var result = new Dictionary<string, object>();
			try
			{
				var builder = new AutopilotBuilder();
				if (ilruntime)
				{
					m_ILRuntime = Dexc.Autopilot.DexcILRuntimeModule.Load(
						assembly,
						delegates =>
						{
							delegates.RegisterFunctionDelegate<AutopilotContext, bool>();
							delegates.RegisterMethodDelegate<AutopilotContext>();
						}
					);
					m_ILRuntime.Invoke(BotEntry, "Configure", builder);
				}
				else
				{
					m_Module = DexcIlModule.Load(assembly, OpenTypes());
					// Refused here, not at whatever frame first reaches the missing member.
					if (m_Module.UnboundMembers.Count > 0)
						throw new InvalidOperationException("Not available in this build: " + string.Join(", ", m_Module.UnboundMembers));
					m_Module.Invoke("Configure", builder);
				}

				m_Builder = builder;
				m_LoadCount++;
				m_NextSelect = 0f;
				m_Failure = null;
				Write("loaded " + builder.Machines.Count + " machines");
				result["loaded"] = true;
				result["machines"] = builder.Machines.Count;
				result["reports"] = builder.Reports;
				if (m_Module != null)
				{
					result["unbound"] = new List<string>(m_Module.UnboundMembers);
					result["bindFailures"] = new List<string>(m_Module.BindFailures);
				}
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
			m_ILRuntime?.Dispose();
			m_ILRuntime = null;
			m_Gamepad.Write(Vector2.zero, false);
		}

		public IDictionary<string, object> Status()
		{
			var game = StealthStageDemoController.Instance;
			var result = new Dictionary<string, object>
			{
				["loadCount"] = m_LoadCount,
				["active"] = m_Active != null ? m_Active.Name : null,
				["steps"] = m_Steps,
				["switches"] = new Dictionary<string, int>(m_Switches),
				["gamepadCurrent"] = UnityEngine.InputSystem.Gamepad.current?.GetType().Name,
				["stickNow"] = m_Gamepad.leftStick.ReadValue().ToString(),
				["maxMove"] = m_MaxMove,
				["focused"] = Application.isFocused,
				["stagesCleared"] = m_StagesCleared,
				["failure"] = m_Failure,
				["log"] = new List<string>(m_Log),
			};
			if (game != null)
			{
				result["stage"] = game.CurrentStageNumber;
				result["state"] = game.State.ToString();
				result["detections"] = game.DetectionCount;
				result["player"] = game.PlayerPosition.ToString();
				result["distanceToExit"] = game.DistanceToExit;
			}

			return result;
		}

		private void Update()
		{
			m_Server?.Pump();
			var game = StealthStageDemoController.Instance;
			if (game != null)
			{
				if (game.State != m_LastState)
				{
					if (game.State == StealthStageState.StageCleared || game.State == StealthStageState.AllStagesComplete)
						m_StagesCleared++;
					Write("game " + game.State + " stage " + game.CurrentStageNumber);
					m_LastState = game.State;
				}
			}

			if (m_Builder == null || game == null)
				return;

			try
			{
				if (UnityEngine.Time.time >= m_NextSelect)
				{
					m_NextSelect = UnityEngine.Time.time + m_Builder.SelectInterval;
					Select();
				}

				if (m_Active?.Action == null)
					return;
				m_Context.RetryValue = false;
				m_Active.Action(m_Context);
				m_Steps++;
				m_MaxMove = Mathf.Max(m_MaxMove, m_Context.MoveValue.magnitude);
				m_Gamepad.Write(m_Context.MoveValue, m_Context.RetryValue);
			}
			catch (Exception exception)
			{
				m_Failure = exception.ToString();
				Write("failed: " + exception.Message);
				Unload();
			}
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

		private void Write(string text)
		{
			m_Log.Add(UnityEngine.Time.time.ToString("0.00") + " " + text);
			if (m_Log.Count > LogCapacity)
				m_Log.RemoveAt(0);
		}
	}
}
