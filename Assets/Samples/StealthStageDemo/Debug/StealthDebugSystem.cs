using Dexc;
using UnityEngine;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Samples.StealthStageDemo
{
	/// <summary>
	/// Brings Dexc up for the demo: the session, the debug menu's input and key, and the HUD widget. It sits in the demo
	/// scene beside the game. What the game shows in the Inspector and Display, its own components register.
	/// </summary>
	[DefaultExecutionOrder(-1000)]
	public sealed class StealthDebugSystem : MonoBehaviour
	{
		[SerializeField]
		DexcRuntimeUISkin m_Skin;

		[SerializeField]
		StealthStageDemoController m_Game;

		StealthDebugMenuInputProvider m_InputProvider;
		DebugDisplayWidget m_DetectionsWidget;

		/// <summary>Runs before the game's Awake, so the game finds the session when it builds its first stage.</summary>
		void Awake()
		{
			// The debugger comes up only when the boot screen asked for it; playing the scene directly leaves it off.
			if (StealthDemoConfig.DebuggerRequested && !DebuggerRuntime.IsInitialized)
			{
				var skin = m_Skin;
				DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = skin);
			}
			if (!StealthDemoConfig.DebuggerRequested || !DebuggerRuntime.IsInitialized)
			{
				enabled = false;
				return;
			}
			m_InputProvider = new StealthDebugMenuInputProvider(m_Game);
			DebugMenu.SetInputProvider(m_InputProvider);
			// Dexc already shows FPS; the widget shows a number the game's own panel does not.
			m_DetectionsWidget = new DebugDisplayWidget("Samples/Stealth/HUD/Detections", () => "Detections  " + m_Game.DetectionCount);
			DebugDisplay.RegisterWidget(m_DetectionsWidget);
		}

		void Start()
		{
			StealthDemoSettings.ApplyTo(m_Game);
		}

		void Update()
		{
			if (StealthStageInput.WasDebugMenuPressed())
				DebugMenu.Toggle();
		}

		/// <summary>
		/// A DEBUG button at the bottom right edge, so a mouse or a tap opens the menu as well. Dexc opens the menu only
		/// when the game asks; this button and the key above are the demo's ways of asking.
		/// </summary>
		void OnGUI()
		{
			if (DebugMenu.IsOpen)
				return;
			var size = new Vector2(Screen.height * 0.12f, Screen.height * 0.06f);
			var rect = new Rect(Screen.width - size.x - 8f, Screen.height - size.y - 8f, size.x, size.y);
			if (GUI.Button(rect, "DEBUG"))
				DebugMenu.Open();
		}

		void OnDestroy()
		{
			if (m_InputProvider == null)
				return;
			DebugMenu.ClearInputProvider(m_InputProvider);
			DebugDisplay.UnregisterWidget(m_DetectionsWidget);
		}
	}
}
