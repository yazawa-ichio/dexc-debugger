using System;
using System.Collections.Generic;
using Dexc.Autopilot;
using UnityEngine;

namespace DexcDebugger.Samples.StealthStageDemo.Autopilot
{
	/// <summary>What a bot sees and does each step. Everything it touches in the game it reaches through typed code.</summary>
	public sealed class StealthContext : AutopilotContext
	{
		internal Vector2 MoveValue;
		internal bool RetryValue;

		public StealthStageDemoController Game => StealthStageDemoController.Instance;

		public void Move(Vector2 value) => MoveValue = value;

		public void Stop() => MoveValue = Vector2.zero;

		public void PressRetry() => RetryValue = true;
	}

	/// <summary>Puts the autopilot into the game: its virtual pad, its bots and the door tools reach them through.</summary>
	public sealed class StealthAutopilot : MonoBehaviour
	{
		private const string BotEntry = "StealthBot.Entry";

		private readonly StealthContext m_Context = new StealthContext();
		private StealthAutopilotGamepad m_Gamepad;
		private AutopilotRunner<StealthContext> m_Runner;
		private AutopilotServer m_Server;
		private StealthStageState m_LastState;
		private int m_StagesCleared;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Boot()
		{
			var host = new GameObject("StealthAutopilot");
			DontDestroyOnLoad(host);
			host.AddComponent<StealthAutopilot>();
		}

		private void Awake()
		{
			m_Gamepad = StealthAutopilotGamepad.Create();
			m_Runner = new AutopilotRunner<StealthContext>(m_Context, BotEntry, CreateBackends());
			m_Runner.StatusRequested += AddGameStatus;
			m_Server = AutopilotServer.TryStart(m_Runner);
		}

		private void OnDestroy()
		{
			m_Server?.Dispose();
			m_Runner.Unload();
		}

		private static IAutopilotBackend[] CreateBackends()
		{
			var backends = new List<IAutopilotBackend>
			{
				new ILRuntimeBackend(delegates =>
				{
					delegates.RegisterFunctionDelegate<StealthContext, bool>();
					delegates.RegisterMethodDelegate<StealthContext>();
				}),
			};
#if UNITY_EDITOR || ENABLE_RUNTIME_PIPELINE_DEV || ENABLE_RUNTIME_PIPELINE
			backends.Add(new PipelineBackend(OpenTypes));
#endif
			return backends.ToArray();
		}

		/// <summary>The game's own types, whose private members interpreted code may reach.</summary>
		private static IEnumerable<Type> OpenTypes()
		{
			foreach (var type in typeof(StealthStageDemoController).Assembly.GetTypes())
			{
				if (!type.IsGenericTypeDefinition && type.Name.IndexOf('<') < 0)
					yield return type;
			}
		}

		private void Update()
		{
			m_Server?.Pump();
			var game = StealthStageDemoController.Instance;
			if (game == null)
				return;
			if (game.State != m_LastState)
			{
				if (game.State == StealthStageState.StageCleared || game.State == StealthStageState.AllStagesComplete)
					m_StagesCleared++;
				m_Runner.Write("game " + game.State + " stage " + game.CurrentStageNumber);
				m_LastState = game.State;
			}

			m_Context.RetryValue = false;
			if (m_Runner.Tick())
				m_Gamepad.Write(m_Context.MoveValue, m_Context.RetryValue);
			else
				m_Gamepad.Write(Vector2.zero, false);
		}

		private void AddGameStatus(IDictionary<string, object> status)
		{
			var game = StealthStageDemoController.Instance;
			status["stagesCleared"] = m_StagesCleared;
			if (game == null)
				return;
			status["stage"] = game.CurrentStageNumber;
			status["state"] = game.State.ToString();
			status["detections"] = game.DetectionCount;
			status["player"] = game.PlayerPosition.ToString();
			status["distanceToExit"] = game.DistanceToExit;
		}
	}
}
