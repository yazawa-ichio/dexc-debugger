#if STEALTH_HAS_TELEMETRY || STEALTH_HAS_QA
using UnityEngine;
#if STEALTH_HAS_TELEMETRY
using Dexc.Telemetry;
#endif
#if STEALTH_HAS_QA
using Dexc.QA;
#endif

namespace DexcDebugger.Samples.StealthStageDemo.Measure
{
	/// <summary>
	/// What only this game can tell the measures, as an example of the layer a game adds: the stage as the annotation of
	/// every frame, the building of a stage as a load, where the player stands on which stage, and the stage, the state
	/// and the detections in each defect report. The packages decide nothing of this. Without them nothing is compiled.
	/// </summary>
	static class StealthMeasure
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void Attach()
		{
			var game = StealthStageDemoController.Instance;
			if (game != null && game.GetComponent<StealthMeasureHook>() == null)
			{
				game.gameObject.AddComponent<StealthMeasureHook>();
			}
		}
	}

	sealed class StealthMeasureHook : MonoBehaviour
#if STEALTH_HAS_TELEMETRY
		, ITelemetryLocation
#endif
#if STEALTH_HAS_QA
		, IDexcQAContributor
#endif
	{
		StealthStageDemoController m_Game;
#if STEALTH_HAS_TELEMETRY
		TelemetryLoad m_Load;
		TelemetrySpan m_Building;
		bool m_EndsNextFrame;
#endif

		void Awake()
		{
			m_Game = GetComponent<StealthStageDemoController>();
			m_Game.StageBuilding += OnStageBuilding;
			m_Game.StageBuilt += OnStageBuilt;
#if STEALTH_HAS_TELEMETRY
			DexcTelemetry.SetLocation(this);
			DexcTelemetry.Annotate("stage", m_Game.CurrentStageNumber.ToString());
#endif
#if STEALTH_HAS_QA
			DexcQA.AddContributor(this);
#endif
		}

		void OnDestroy()
		{
			m_Game.StageBuilding -= OnStageBuilding;
			m_Game.StageBuilt -= OnStageBuilt;
#if STEALTH_HAS_TELEMETRY
			DexcTelemetry.SetLocation(null);
#endif
#if STEALTH_HAS_QA
			DexcQA.RemoveContributor(this);
#endif
		}

		void OnStageBuilding(int stage)
		{
#if STEALTH_HAS_TELEMETRY
			// From the moment the game asks for the stage to the first frame that takes input.
			m_Load?.End();
			m_Load = DexcTelemetry.BeginLoad("stage" + stage, TelemetryLoadKind.Level);
			m_Building = m_Load.Phase("build");
#endif
		}

		void OnStageBuilt(int stage)
		{
#if STEALTH_HAS_TELEMETRY
			m_Building.Dispose();
			DexcTelemetry.Annotate("stage", stage.ToString());
			m_EndsNextFrame = true;
#endif
		}

#if STEALTH_HAS_TELEMETRY
		void Update()
		{
			if (m_EndsNextFrame)
			{
				m_EndsNextFrame = false;
				m_Load?.End();
				m_Load = null;
			}
		}

		/// <summary>Each stage is its own map, named as Dashboard/maps of the workspace names it.</summary>
		public bool TryGetLocation(out string map, out float x, out float y, out float z)
		{
			var position = m_Game.PlayerPosition;
			map = "stage" + m_Game.CurrentStageNumber;
			x = position.x;
			y = position.y;
			z = position.z;
			return true;
		}
#endif

#if STEALTH_HAS_QA
		public void Contribute(DexcQAReport report)
		{
			report.Set("stage", m_Game.CurrentStageNumber.ToString());
			report.Set("state", m_Game.State.ToString());
			report.Set("detections", m_Game.DetectionCount.ToString());
			report.Set("distanceToExit", m_Game.DistanceToExit.ToString("0.00"));
		}
#endif
	}
}
#endif
