using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dexc
{
	/// <summary>
	/// Place on any object in a development-only scene that runs ahead of the game's own first scene. It shows the boot
	/// menu for one set and starts it, which goes to the scene that set declares. The class exists in every build so the
	/// component does not become a missing script, but with Dexc disabled it does nothing: this scene is not shipped.
	/// It lives in its own file because Unity resolves scene-serialized MonoBehaviours by file name.
	/// </summary>
	[AddComponentMenu("Dexc/Debug Boot Scene")]
	public sealed class DebugBootScene : MonoBehaviour
	{
#if DEXC_DEBUGGER
		[SerializeField, Tooltip("Name of the boot set to show. Must match a [DebugBootMenu] Tag.")]
		string m_Tag = string.Empty;

		[
			SerializeField,
			Tooltip(
				"Call DexcDebugger.Initialize() before showing the menu when no session exists. Persistent DebugSetting values are restored by Initialize."
			)
		]
		bool m_InitializeDexc;

		[SerializeField, Tooltip("Seconds until the game starts automatically. 0 waits for the user.")]
		float m_AutoStartSeconds;

		[SerializeField, Tooltip("Header title.")]
		string m_Title = "Boot Menu";

		[
			SerializeField,
			Tooltip(
				"Optional Runtime UI Skin. When assigned (or Initialize Dexc is on) a session starts here so the boot menu uses the selected renderer."
			)
		]
		DexcRuntimeUISkin m_Skin;

		/// <summary>The boot set this scene shows.</summary>
		public string Tag
		{
			get { return m_Tag; }
			set { m_Tag = value; }
		}

		void Start()
		{
			if (!DexcDebugger.IsEnabled)
				return;
			if ((m_InitializeDexc || m_Skin != null) && !DexcDebugger.IsInitialized)
			{
				var skin = m_Skin;
				DexcDebugger.Initialize(options => options.RuntimeUI.Skin = skin);
			}
			if (DexcDebugger.IsInitialized)
			{
				// A renderer requested during scene load (for example from a persisted boot value) applies before the menu opens.
				DexcRuntimeUIRendererSelection.ApplyPending();
				if (DexcRuntimeUIState.InputProvider == null)
					DebugMenu.SetInputProvider(new DebugGamepadInputProvider());
			}
			DebugBootMenu.Title = string.IsNullOrEmpty(m_Title) ? "Boot Menu" : m_Title;
			DebugBootMenu.AutoStartSeconds = m_AutoStartSeconds;
			DebugBootMenu.Open(m_Tag, scene => SceneManager.LoadScene(scene, LoadSceneMode.Single));
		}

		void OnDestroy()
		{
			if (DebugBootMenu.IsOpen)
				DebugBootMenu.Close();
		}
#endif
	}
}
