#if DEXC_DEBUGGER
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DexcDebugger.Tests
{
	/// <summary>Loads the real boot scene and the Boot Check scene, so scene wiring and renderer switching are exercised end to end.</summary>
	public sealed class DexcBootSceneTests
	{
		Scene m_Cleanup;

		[SetUp]
		public void SetUp()
		{
			DebugStorage.Storage = new DebugMemoryStorage();
			DebugPanels.ResetForTests();
			Dexc.DexcDebugger.ResetForTests();
			// Clear what earlier runs stored through real PlayerPrefs so it cannot leak into this one.
			DebugBootSelection.Open("check").Reset();
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			DebugMenu.Close();
			DebugBootMenu.Close();
			DebugBootMenu.AutoStartSeconds = 0f;
			DebugMenu.SetRenderer(DebugMenuRenderer.IMGUI);
			yield return null;
			// Only the debug scenes are unloaded; the test runner lives in its own scene and must stay.
			var debugScenes = Enumerable
				.Range(0, SceneManager.sceneCount)
				.Select(SceneManager.GetSceneAt)
				.Where(scene => scene.isLoaded && (scene.name == "DebugBootScene" || scene.name == "BootCheck"))
				.ToList();
			if (debugScenes.Count > 0)
			{
				m_Cleanup = SceneManager.CreateScene("DexcBootSceneTests-Cleanup");
				SceneManager.SetActiveScene(m_Cleanup);
				foreach (var scene in debugScenes)
					yield return SceneManager.UnloadSceneAsync(scene);
			}
			Time.timeScale = 1f;
			DebugPanels.ResetForTests();
			DebugStorage.ResetForTests();
			Dexc.DexcDebugger.ResetForTests();
		}

		static IEnumerator LoadBootScene()
		{
			SceneManager.LoadScene("DebugBootScene", LoadSceneMode.Single);
			yield return null;
			yield return null;
			Assert.That(DebugBootMenu.IsOpen, Is.True, "DebugBootScene component must open the boot menu");
			Assert.That(DebugBootMenu.UsesRuntimeMenu, Is.True);
		}

		/// <summary>The boot screen draws no close button, and a hidden one would start the game: no renderer lets the gamepad reach it.</summary>
		[UnityTest]
		public IEnumerator EveryRendererLetsTheGamepadReachOnlyTheBootButtonsItDraws()
		{
			yield return LoadBootScene();
			foreach (var renderer in new[] { DebugMenuRenderer.UGUI, DebugMenuRenderer.UIToolkit, DebugMenuRenderer.IMGUI })
			{
				Assert.That(DebugMenu.SetRenderer(renderer), Is.True, renderer.ToString());
				yield return null;
				yield return null;
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.PanelList), renderer.ToString());
				Assert.That(
					DexcRuntimeUIRenderer.ProviderSelectionCount(),
					Is.EqualTo(DebugBootMenu.Panels.Count + 2),
					renderer + ": boot panels plus Reset and Start"
				);
			}
		}

		[UnityTest]
		public IEnumerator BootSceneOpensTheMenuAndStartLoadsTheBootCheckScene()
		{
			yield return LoadBootScene();
			Assert.That(DebugBootMenu.Panels.Any(panel => panel.Title == "Boot Check"), Is.True, "the Boot Check set is shown");
			Assert.That(
				Dexc.DexcDebugger.IsInitialized,
				Is.True,
				"the boot scene starts a session so the Runtime menu renders the boot menu"
			);
			Assert.That(DexcRuntimeUIState.IsOpen, Is.True);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.PanelList));
			Assert.That(
				DexcRuntimeUIRenderer.ProviderSelectionCount(),
				Is.EqualTo(DebugBootMenu.Panels.Count + 2),
				"boot panels plus Reset and Start"
			);
			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeUIState.IsOpen, Is.True, "Back at the boot root does not close the boot menu");

			// Start is the gamepad's last item, after the panel rows and Reset.
			DexcRuntimeUIState.SetSelectedIndex(DebugBootMenu.Panels.Count + 1);
			DexcRuntimeUIRenderer.ActivateProviderSelection();
			for (var frame = 0; frame < 10 && SceneManager.GetActiveScene().name != "BootCheck"; frame++)
				yield return null;
			yield return null;
			Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("BootCheck"));
			Assert.That(DebugBootMenu.IsOpen, Is.False);
			Assert.That(Dexc.DexcDebugger.IsInitialized, Is.True, "BootCheckScreen initializes Dexc");
			Assert.That(DebugPanels.All.Any(panel => panel.Title == "Boot Check"), Is.True, "the screen registers its panel");
			Assert.That(DebugMenu.InputProvider, Is.Not.Null, "the boot scene provider is kept by the Boot Check screen");

			foreach (var renderer in new[] { DebugMenuRenderer.UGUI, DebugMenuRenderer.UIToolkit, DebugMenuRenderer.IMGUI })
			{
				Assert.That(DebugMenu.SetRenderer(renderer), Is.True, renderer.ToString());
				yield return null;
				Assert.That(DebugMenu.Renderer, Is.EqualTo(renderer));
				DebugMenu.Open();
				yield return null;
				DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.PanelList);
				yield return null;
				Assert.That(DexcRuntimeUIRenderer.ProviderSelectionCount(), Is.GreaterThan(0), renderer + " lists panels");
				DebugMenu.Close();
				yield return null;
			}
			LogAssert.NoUnexpectedReceived();
		}

		/// <summary>Gamepad flow on a text row: landing does not edit, Submit edits, Back leaves the field without leaving the page.</summary>
		[UnityTest]
		public IEnumerator TextRowsEnterEditingOnSubmitAndLeaveOnBackInEveryRenderer()
		{
			yield return LoadBootScene();
			var provider = new QueuedProvider();
			DebugMenu.ClearInputProvider(DebugMenu.InputProvider);
			DebugMenu.SetInputProvider(provider);
			var bootCheck = DebugBootMenu.Panels.First(panel => panel.Title == "Boot Check");
			// Focusable rows: Renderer, Show FPS, Open Debug Menu, Player Name (text), Time Scale (number), Start with Debug Menu.
			const int textRow = 3;
			foreach (var renderer in new[] { DebugMenuRenderer.UGUI, DebugMenuRenderer.UIToolkit, DebugMenuRenderer.IMGUI })
			{
				Assert.That(DebugMenu.SetRenderer(renderer), Is.True, renderer.ToString());
				yield return null;
				yield return null;
				Assert.That(DebugMenu.Renderer, Is.EqualTo(renderer));
				Assert.That(
					DebugBootMenu.UsesRuntimeMenu && DexcRuntimeUIState.IsOpen,
					Is.True,
					renderer + " keeps the boot menu open across the switch"
				);
				Assert.That(DebugMenu.InputProvider, Is.SameAs(provider), renderer + " keeps the provider");
				DexcRuntimePanelForm.Open(bootCheck);
				yield return null;
				yield return null;
				if (renderer == DebugMenuRenderer.IMGUI)
				{
					// IMGUI handles provider frames inside OnGUI, which does not run in batch mode: feed HandleProviderInput directly.
					for (var step = 0; step < textRow; step++)
					{
						DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(new Vector2(0f, -1f), false, false));
						DexcRuntimeUIRenderer.ProcessProviderInputForTests();
						// Release between presses; a held direction only auto-repeats after the repeat delay.
						DexcRuntimeUIRenderer.QueueProviderInputForTests(default(DebugMenuInputFrame));
						DexcRuntimeUIRenderer.ProcessProviderInputForTests();
					}
					Assert.That(DexcRuntimeUIState.SelectedIndex, Is.EqualTo(textRow), "IMGUI moved to Player Name");
					var textWidget = DexcRuntimePanelForm.FocusedWidget;
					Assert.That(textWidget, Is.InstanceOf<DebugTextWidget>());
					DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, true, false));
					DexcRuntimeUIRenderer.ProcessProviderInputForTests();
					Assert.That(
						DexcRuntimeUIRenderer.PendingPanelTextFocus,
						Is.EqualTo(DexcRuntimeUIRenderer.PanelTextKey(textWidget)),
						"IMGUI Submit focuses the text field on the next repaint"
					);
					DexcRuntimeUIRenderer.PendingPanelTextFocus = null;
					continue;
				}
				for (var step = 0; step < textRow; step++)
					yield return Drive(provider, new DebugMenuInputFrame(new Vector2(0f, -1f), false, false));
				Assert.That(DexcRuntimeUIState.SelectedIndex, Is.EqualTo(textRow), renderer + " moved to Player Name");
				Assert.That(DexcRuntimeUIRenderer.IsTextFocused(), Is.False, renderer + " landing on a text row does not start editing");
				yield return Drive(provider, new DebugMenuInputFrame(Vector2.zero, true, false));
				Assert.That(DexcRuntimeUIRenderer.IsTextFocused(), Is.True, renderer + " Submit starts editing");
				Assert.That(DexcRuntimeUIState.InputState, Is.EqualTo(DebugMenuInputState.TextInput), renderer + " reports TextInput");
				yield return Drive(provider, new DebugMenuInputFrame(new Vector2(0f, -1f), false, false));
				Assert.That(
					DexcRuntimeUIState.SelectedIndex,
					Is.EqualTo(textRow),
					renderer + " navigation stays with the field while editing"
				);
				yield return Drive(provider, new DebugMenuInputFrame(Vector2.zero, false, true));
				Assert.That(DexcRuntimeUIRenderer.IsTextFocused(), Is.False, renderer + " Back leaves the field");
				Assert.That(
					DexcRuntimeUIState.Page,
					Is.EqualTo(DexcRuntimeUIPage.PanelDetail),
					renderer + " Back while editing stays on the page"
				);
				Assert.That(
					DexcRuntimeUIState.InputState,
					Is.EqualTo(DebugMenuInputState.Navigation),
					renderer + " reports Navigation again"
				);
				yield return Drive(provider, new DebugMenuInputFrame(new Vector2(0f, -1f), false, false));
				Assert.That(
					DexcRuntimeUIState.SelectedIndex,
					Is.EqualTo(textRow + 1),
					renderer + " navigation works after leaving the field"
				);
				yield return Drive(provider, new DebugMenuInputFrame(Vector2.zero, false, true));
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.PanelList), renderer + " Back returns to the list");
			}
			LogAssert.NoUnexpectedReceived();
		}

#if DEXC_HAS_UGUI
		[UnityTest]
		public IEnumerator UGUIBootMenuCreatesAPointerEventSystemWhileOpenWhenTheSceneHasNone()
		{
			yield return LoadBootScene();
			Assert.That(CurrentEventSystem(), Is.Null, "the debug scenes have no EventSystem of their own");
			Assert.That(DebugMenu.SetRenderer(DebugMenuRenderer.UGUI), Is.True);
			yield return null;
			yield return null;
			Assert.That(DexcUGUIApi.HasFallbackEventSystem, Is.True);
			Assert.That(CurrentEventSystem(), Is.Not.Null, "uGUI needs an EventSystem for pointer input");
			Assert.That(DebugMenu.SetRenderer(DebugMenuRenderer.IMGUI), Is.True);
			yield return null;
			yield return null;
			Assert.That(DexcUGUIApi.HasFallbackEventSystem, Is.False, "the fallback is released when uGUI is no longer drawing");
			LogAssert.NoUnexpectedReceived();
		}

		static object CurrentEventSystem()
		{
			return DexcUGUIApi.EventSystemType.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
		}
#endif

		[UnityTest]
		public IEnumerator AutoStartRunsThroughTheRuntimeMenuAndInteractionCancelsIt()
		{
			Dexc.DexcDebugger.Initialize();
			var started = 0;
			DebugBootMenu.AutoStartSeconds = 5f;
			DebugBootMenu.Open("check", _ => started++);
			Assert.That(DebugBootMenu.UsesRuntimeMenu, Is.True);
			Assert.That(DebugBootMenu.AutoStartRemainingSeconds, Is.EqualTo(5));
			Assert.That(DexcRuntimeUIRenderer.Title(DexcRuntimeUIPage.PanelList), Does.EndWith("5s"));
			DexcRuntimeUIState.Back();
			Assert.That(DebugBootMenu.AutoStartRemainingSeconds, Is.EqualTo(0), "any interaction cancels the countdown");
			DexcBootMenuState.AutoStartAt = DueNow;
			yield return null;
			yield return null;
			Assert.That(started, Is.EqualTo(0));
			Assert.That(DebugBootMenu.IsOpen, Is.True);

			DebugBootMenu.Close();
			DebugBootMenu.Open("check", _ => started++);
			DexcBootMenuState.AutoStartAt = DueNow;
			yield return null;
			yield return null;
			Assert.That(started, Is.EqualTo(1), "the countdown starts the game while the Runtime menu shows the boot menu");
			Assert.That(DebugBootMenu.IsOpen, Is.False);
			Assert.That(DexcRuntimeUIState.IsOpen, Is.False);
		}

		/// <summary>A countdown that is already due. Must stay positive: realtimeSinceStartup is near zero when Play Mode starts in batch mode.</summary>
		const float DueNow = 0.0001f;

		static IEnumerator Drive(QueuedProvider provider, DebugMenuInputFrame frame)
		{
			provider.Enqueue(frame);
			yield return null;
			yield return null;
		}

		sealed class QueuedProvider : DebugMenuInputProvider
		{
			readonly Queue<DebugMenuInputFrame> m_Frames = new Queue<DebugMenuInputFrame>();

			public void Enqueue(DebugMenuInputFrame frame) => m_Frames.Enqueue(frame);

			public override DebugMenuInputFrame ReadInput() => m_Frames.Count > 0 ? m_Frames.Dequeue() : default(DebugMenuInputFrame);
		}
	}
}
#endif
