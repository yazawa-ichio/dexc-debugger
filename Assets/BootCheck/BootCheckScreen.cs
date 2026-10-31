#if DEXC_DEBUGGER && !DISABLE_DEXC_DEBUGGER
using System;
using Dexc;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DexcBootCheck
{
	/// <summary>
	/// Verification screen reached from the boot scene. It reads its own <see cref="BootCheckConfig"/>, which the boot set
	/// filled in before this scene loaded, and shows an overlay to toggle the debug menu, switch the renderer and return
	/// to the boot scene.
	/// </summary>
	public sealed class BootCheckScreen : MonoBehaviour
	{
		[SerializeField]
		DexcRuntimeUISkin m_Skin;

		DebugPanel m_Panel;
		DebugDisplayWidget m_Fps;
		DebugGamepadInputProvider m_Input;
		int m_Clicks;
		string m_Status = string.Empty;

		void Start()
		{
			// No-op when the boot scene already started a session; the skin is used on a direct launch of this scene.
			Dexc.DexcDebugger.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			if (DebugMenu.InputProvider == null)
			{
				m_Input = new DebugGamepadInputProvider();
				DebugMenu.SetInputProvider(m_Input);
			}
			m_Fps = new DebugDisplayWidget(
				"BootCheck/HUD/FPS",
				() => (1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)).ToString("0") + " fps"
			);
			m_Panel = new DebugPanel("Boot Check")
				.Label(() => "Player: " + BootCheckConfig.PlayerName + "  Renderer: " + DebugMenu.Renderer)
				.Toggle("FPS widget", () => m_Fps.Enabled, value => m_Fps.Enabled = value)
				.Number("Time scale", () => Time.timeScale, value => Time.timeScale = (float)value, 0.25, 3, 0.25)
				.Number("Clicks", () => m_Clicks, value => m_Clicks = (int)value, 0, 999, 1, true)
				.Button("Count", () => m_Clicks++);
			DebugPanels.Register(m_Panel);
			ApplyBootSettings();
		}

		void OnDestroy()
		{
			if (m_Panel != null)
				DebugPanels.Unregister(m_Panel);
			if (m_Fps != null)
				DebugDisplay.UnregisterWidget(m_Fps);
			if (m_Input != null)
				DebugMenu.ClearInputProvider(m_Input);
		}

		/// <summary>Applies what the boot menu chose: time scale, renderer, FPS widget and the optional Debug Menu.</summary>
		void ApplyBootSettings()
		{
			Time.timeScale = BootCheckConfig.TimeScale;
			ApplyRenderer(BootCheckConfig.Renderer);
			DebugDisplay.UnregisterWidget(m_Fps);
			if (BootCheckConfig.ShowFps)
				DebugDisplay.RegisterWidget(m_Fps);
			if (BootCheckConfig.OpenDebugMenuOnStart)
				DebugMenu.Open();
		}

		void ApplyRenderer(BootCheckRenderer renderer)
		{
			var target = BootCheckSettings.ToMenuRenderer(renderer);
			m_Status = DebugMenu.SetRenderer(target)
				? "Renderer: " + DebugMenu.RendererName(target)
				: DebugMenu.RendererName(target) + " is unavailable, using IMGUI";
		}

		void OnGUI()
		{
			var scale = Mathf.Max(1f, Screen.dpi > 0f ? Screen.dpi / 160f : 1f);
			GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
			GUILayout.BeginArea(new Rect(12f, 12f, 360f, 300f), GUI.skin.box);
			GUILayout.Label("Boot Check");
			GUILayout.Label("Player: " + BootCheckConfig.PlayerName + "   TimeScale: " + Time.timeScale.ToString("0.00"));
			GUILayout.Label("Boot renderer: " + BootCheckConfig.Renderer + "   Active: " + DebugMenu.Renderer);
			GUILayout.Label(m_Status);
			GUILayout.Space(6f);
			if (GUILayout.Button(DebugMenu.Renderer + "  ›  next renderer", GUILayout.Height(32f)))
				CycleRenderer();
			if (GUILayout.Button("Debug Menu (open/close)", GUILayout.Height(32f)))
				DebugMenu.Toggle();
			// Returning to the boot scene unloads this one, so its HUD widgets go with it. The boot menu is never
			// shown over a running game, which is why the library needs no rule for hiding the HUD while it is open.
			if (GUILayout.Button("Back to Boot Scene", GUILayout.Height(32f)))
				SceneManager.LoadScene(0, LoadSceneMode.Single);
			GUILayout.EndArea();
			GUI.matrix = Matrix4x4.identity;
		}

		void CycleRenderer()
		{
			var next = (BootCheckRenderer)(((int)BootCheckConfig.Renderer + 1) % Enum.GetValues(typeof(BootCheckRenderer)).Length);
			BootCheckConfig.Renderer = next;
			ApplyRenderer(next);
		}
	}
}
#endif
