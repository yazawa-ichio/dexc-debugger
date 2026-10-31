#if DEXC_DEBUGGER
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DexcDebugger.Tests
{
	/// <summary>
	/// Diagnostic screenshots of the boot scene through every renderer at several Game view sizes. Skipped unless
	/// DEXC_BOOT_CAPTURE=1; run with the graphics runner:
	/// sh tools/unity-tests/run-playmode-tests.sh --graphics --filter DexcBootSceneCaptureTests
	/// </summary>
	public sealed class DexcBootSceneCaptureTests
	{
		const string Output = "Logs/UnityTests/BootScene";

		[UnityTest]
		public IEnumerator CaptureBootSceneThroughEveryRenderer()
		{
			if (Environment.GetEnvironmentVariable("DEXC_BOOT_CAPTURE") != "1")
				Assert.Ignore("Set DEXC_BOOT_CAPTURE=1 and use the graphics runner to capture the boot scene.");
			Directory.CreateDirectory(Output);
			foreach (var lowResolution in new[] { false, true })
			foreach (
				var size in new[]
				{
					new Vector2Int(1366, 768),
					new Vector2Int(390, 844),
					new Vector2Int(844, 390),
					new Vector2Int(640, 480),
				}
			)
			{
#if UNITY_EDITOR
				var restore = SetGameViewSize(size.x, size.y, lowResolution);
#else
				Action restore = () => { };
#endif
				try
				{
					for (var attempt = 0; (Screen.width != size.x || Screen.height != size.y) && attempt < 120; attempt++)
						yield return null;
					yield return CaptureScene(size.x + "x" + size.y + (lowResolution ? "-lowres" : string.Empty));
				}
				finally
				{
					restore();
				}
			}
		}

		static IEnumerator CaptureScene(string prefix)
		{
			DebugStorage.Storage = new DebugMemoryStorage();
			DebugPanels.ResetForTests();
			Dexc.DexcDebugger.ResetForTests();
			DebugBootSelection.Open("check").Reset();
			SceneManager.LoadScene("DebugBootScene", LoadSceneMode.Single);
			yield return null;
			yield return null;
			yield return null;
			Debug.Log(
				"BootCapture "
					+ prefix
					+ " screen="
					+ Screen.width
					+ "x"
					+ Screen.height
					+ " dpi="
					+ Screen.dpi
					+ " renderer="
					+ DebugMenu.Renderer
					+ " safe="
					+ Screen.safeArea
			);
			foreach (var renderer in new[] { DebugMenuRenderer.IMGUI, DebugMenuRenderer.UGUI, DebugMenuRenderer.UIToolkit })
			{
				DebugMenu.SetRenderer(renderer);
				yield return null;
				yield return null;
				yield return null;
				yield return Capture(prefix + "-" + renderer + "-list");
				DexcRuntimePanelForm.OpenAt(0);
				yield return null;
				yield return null;
				DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(new Vector2(0f, -1f), false, false));
				DexcRuntimeUIRenderer.ProcessProviderInputForTests();
				yield return null;
				yield return null;
				yield return Capture(prefix + "-" + renderer + "-detail");
				DexcRuntimeUIState.Back();
				yield return null;
			}
			DebugMenu.SetRenderer(DebugMenuRenderer.IMGUI);
			DebugBootMenu.Start();
			for (var frame = 0; frame < 10 && SceneManager.GetActiveScene().name != "BootCheck"; frame++)
				yield return null;
			yield return null;
			yield return null;
			yield return Capture(prefix + "-bootcheck");
			var cleanup = SceneManager.CreateScene("BootCapture-Cleanup-" + prefix);
			SceneManager.SetActiveScene(cleanup);
			foreach (
				var scene in Enumerable
					.Range(0, SceneManager.sceneCount)
					.Select(SceneManager.GetSceneAt)
					.Where(scene => scene.isLoaded && scene != cleanup)
					.ToList()
			)
				yield return SceneManager.UnloadSceneAsync(scene);
			Time.timeScale = 1f;
			DebugPanels.ResetForTests();
			DebugStorage.ResetForTests();
			Dexc.DexcDebugger.ResetForTests();
		}

		static IEnumerator Capture(string name)
		{
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();
			var texture = ScreenCapture.CaptureScreenshotAsTexture();
			try
			{
				File.WriteAllBytes(Path.Combine(Output, name + ".png"), texture.EncodeToPNG());
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(texture);
			}
		}

#if UNITY_EDITOR
		static Action SetGameViewSize(int width, int height, bool lowResolution)
		{
			var editorAssembly = typeof(Editor).Assembly;
			var gameViewType = editorAssembly.GetType("UnityEditor.GameView", true);
			var gameViewSizesType = editorAssembly.GetType("UnityEditor.GameViewSizes", true);
			var gameViewSizeType = editorAssembly.GetType("UnityEditor.GameViewSize", true);
			var gameViewSizeTypeEnum = editorAssembly.GetType("UnityEditor.GameViewSizeType", true);
			var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(gameViewSizesType);
			var singleton = singletonType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
			var groupType = singleton
				.GetType()
				.GetProperty("currentGroupType", BindingFlags.Public | BindingFlags.Instance)
				.GetValue(singleton, null);
			var group = singleton
				.GetType()
				.GetMethod("GetGroup", BindingFlags.Public | BindingFlags.Instance)
				.Invoke(singleton, new[] { groupType });
			var fixedResolution = Enum.Parse(gameViewSizeTypeEnum, "FixedResolution");
			var constructor = gameViewSizeType.GetConstructor(
				BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
				null,
				new[] { gameViewSizeTypeEnum, typeof(int), typeof(int), typeof(string) },
				null
			);
			var size = constructor.Invoke(new object[] { fixedResolution, width, height, "Dexc BootCapture " + width + "x" + height });
			group.GetType().GetMethod("AddCustomSize", BindingFlags.Public | BindingFlags.Instance).Invoke(group, new[] { size });
			var index =
				(int)group.GetType().GetMethod("GetTotalCount", BindingFlags.Public | BindingFlags.Instance).Invoke(group, null) - 1;
			var gameView = EditorWindow.GetWindow(gameViewType);
			var selected = gameViewType.GetProperty(
				"selectedSizeIndex",
				BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
			);
			var previous = (int)selected.GetValue(gameView, null);
			var lowResolutionProperty = gameViewType.GetProperty(
				"lowResolutionForAspectRatios",
				BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
			);
			var lowResolutionField = gameViewType.GetField(
				"m_LowResolutionForAspectRatios",
				BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
			);
			var previousLowResolution =
				lowResolutionProperty != null
					? (bool)lowResolutionProperty.GetValue(gameView, null)
					: lowResolutionField != null && (bool)lowResolutionField.GetValue(gameView);
			selected.SetValue(gameView, index, null);
			if (lowResolutionProperty != null)
				lowResolutionProperty.SetValue(gameView, lowResolution, null);
			else
				lowResolutionField?.SetValue(gameView, lowResolution);
			gameView.Repaint();
			EditorApplication.QueuePlayerLoopUpdate();
			return () =>
			{
				selected.SetValue(gameView, previous, null);
				if (lowResolutionProperty != null)
					lowResolutionProperty.SetValue(gameView, previousLowResolution, null);
				else
					lowResolutionField?.SetValue(gameView, previousLowResolution);
				group
					.GetType()
					.GetMethod("RemoveCustomSize", BindingFlags.Public | BindingFlags.Instance)
					.Invoke(group, new object[] { index });
				gameView.Repaint();
			};
		}
#endif
	}
}
#endif
