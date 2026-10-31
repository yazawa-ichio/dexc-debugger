#if DEXC_DEBUGGER
using System.Collections;
using System.Linq;
using Dexc;
using DexcDebugger.Samples.StealthStageDemo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DexcDebugger.Tests
{
	/// <summary>
	/// Starts the Stealth Stage Demo through its own boot scenes, so the boot values have to reach the game and the
	/// debug integration has to attach to a scene that is not the first one.
	/// </summary>
	public sealed class DexcStealthSampleBootTests
	{
		Scene m_Cleanup;

		[SetUp]
		public void SetUp()
		{
			DebugStorage.Storage = new DebugMemoryStorage();
			DebugPanels.ResetForTests();
			Dexc.DexcDebugger.ResetForTests();
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			DebugMenu.Close();
			DebugBootMenu.Close();
			yield return null;
			// Only the sample scenes are unloaded; the test runner lives in its own scene and must stay.
			var sampleScenes = Enumerable
				.Range(0, SceneManager.sceneCount)
				.Select(SceneManager.GetSceneAt)
				.Where(scene => scene.isLoaded && scene.path.StartsWith("Assets/Samples/StealthStageDemo/"))
				.ToList();
			if (sampleScenes.Count > 0)
			{
				m_Cleanup = SceneManager.CreateScene("DexcStealthSampleBootTests-Cleanup");
				SceneManager.SetActiveScene(m_Cleanup);
				foreach (var scene in sampleScenes)
					yield return SceneManager.UnloadSceneAsync(scene);
			}
			Time.timeScale = 1f;
			StealthDemoConfig.Environment = StealthServerEnvironment.Local;
			StealthDemoConfig.Host = "localhost";
			StealthDemoConfig.StartStage = StealthStartStage.Stage1;
			StealthDemoConfig.GuardCount = 3;
			StealthDemoConfig.TimeScale = 1f;
			StealthDemoConfig.Invincible = false;
			StealthDemoConfig.DebuggerRequested = true;
			DebugPanels.ResetForTests();
			DebugStorage.ResetForTests();
			Dexc.DexcDebugger.ResetForTests();
		}

		static IEnumerator Boot(string bootScene)
		{
			SceneManager.LoadScene(bootScene, LoadSceneMode.Single);
			yield return null;
			yield return null;
			Assert.That(DebugBootMenu.IsOpen, Is.True, bootScene + " opens its boot set");
		}

		static IEnumerator StartDemo()
		{
			DebugBootMenu.Start();
			for (var frame = 0; frame < 10 && SceneManager.GetActiveScene().name != "StealthStageDemo"; frame++)
				yield return null;
			// One more frame so the integration added on scene load has run its Start.
			yield return null;
			Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("StealthStageDemo"));
		}

		[UnityTest]
		public IEnumerator BattleCheckStartsTheChosenStageWithTheChosenGuards()
		{
			yield return Boot("StealthBattleBoot");
			var battle = DebugBootMenu.Panels.Single(panel => panel.Title == "Battle");
			((DebugChoiceWidget)battle.Widgets[0]).Index = (int)StealthStartStage.Stage2;
			((DebugNumberWidget)battle.Widgets[1]).Value = 1;
			yield return StartDemo();

			var game = StealthStageDemoController.Instance;
			Assert.That(game.CurrentStageNumber, Is.EqualTo(2));
			Assert.That(game.Guards, Has.Count.EqualTo(1));
			Assert.That(Dexc.DexcDebugger.IsInitialized, Is.True, "StealthDebugSystem brings the debugger up");
		}

		[UnityTest]
		public IEnumerator ResetReturnsTheServerToItsDefault()
		{
			// With a session the boot set is shown through the Runtime menu, whose footer carries Reset and Start.
			Dexc.DexcDebugger.Initialize();
			yield return Boot("StealthBoot");
			Assert.That(DebugBootMenu.UsesRuntimeMenu, Is.True);
			var server = (DebugChoiceWidget)DebugBootMenu.Panels.Single(panel => panel.Title == "Server").Widgets[0];
			server.Index = (int)StealthServerEnvironment.Production;

			// Reset sits after the panel rows, before Start.
			DexcRuntimeUIState.SetSelectedIndex(DebugBootMenu.Panels.Count);
			DexcRuntimeUIRenderer.ActivateProviderSelection();

			Assert.That(server.Index, Is.EqualTo((int)StealthServerEnvironment.Local));
			Assert.That(DebugBootMenu.IsOpen, Is.True, "Reset does not start the game");
		}

		[UnityTest]
		public IEnumerator ProductionStartsTheDemoWithTheDebuggerOff()
		{
			yield return Boot("StealthBoot");
			var server = DebugBootMenu.Panels.Single(panel => panel.Title == "Server");
			((DebugChoiceWidget)server.Widgets[0]).Index = (int)StealthServerEnvironment.Production;
			yield return StartDemo();

			var game = StealthStageDemoController.Instance;
			Assert.That(game.CurrentStageNumber, Is.EqualTo(1));
			Assert.That(((Behaviour)GameObject.Find("Stealth Debug System").GetComponent("StealthDebugSystem")).enabled, Is.False);
			Assert.That(Dexc.DexcDebugger.IsInitialized, Is.False);
		}
	}
}
#endif
