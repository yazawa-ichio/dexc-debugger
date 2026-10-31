#if DEXC_DEBUGGER && UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dexc;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DexcDebugger.Tests
{
	/// <summary>
	/// Captures the representative pages of every renderer from a small phone to a 4K monitor, so a layout that only
	/// breaks at one end of the range shows up. Run with the graphics runner; the pictures land in Logs/UnityTests/Resolution.
	/// </summary>
	public sealed class DexcResolutionCaptureTests
	{
		const string Output = "Logs/UnityTests/Resolution";

		readonly struct Size
		{
			internal Size(int width, int height, DexcRuntimeUIProfileKind profile)
			{
				Width = width;
				Height = height;
				Profile = profile;
			}

			internal readonly int Width;
			internal readonly int Height;
			internal readonly DexcRuntimeUIProfileKind Profile;
			internal string Name => Width + "x" + Height;
		}

		static readonly Size[] Sizes =
		{
			new Size(390, 844, DexcRuntimeUIProfileKind.AndroidPortrait),
			new Size(844, 390, DexcRuntimeUIProfileKind.AndroidLandscape),
			new Size(1440, 3200, DexcRuntimeUIProfileKind.AndroidPortrait),
			new Size(800, 600, DexcRuntimeUIProfileKind.Windows),
			new Size(1366, 768, DexcRuntimeUIProfileKind.Windows),
			new Size(2560, 1440, DexcRuntimeUIProfileKind.Windows),
			new Size(3840, 2160, DexcRuntimeUIProfileKind.Windows),
		};

		static readonly string[] States =
		{
			"home",
			"command-list",
			"command-detail",
			"command-error",
			"setting-list",
			"setting-detail",
			"inspector-fields",
			"collection",
			"history",
			"diagnostics",
			"split-command",
			"split-setting",
		};

		/// <summary>
		/// Narrows a run to some sizes, renderers or pages, such as "1366x768;UIToolkit;history,diagnostics". Each part is a
		/// comma list; an empty part keeps everything.
		/// </summary>
		static readonly string[] Only = (Environment.GetEnvironmentVariable("DEXC_RESOLUTION_ONLY") ?? string.Empty).Split(';');

		static bool Included(int part, string name)
		{
			var list = part < Only.Length ? Only[part].Trim() : string.Empty;
			return list.Length == 0 || list.Split(',').Any(item => item.Trim() == name);
		}

		DexcRuntimeUISkin m_Skin;
		readonly List<GameObject> m_Objects = new List<GameObject>();

		void SetUp()
		{
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.ResetForTests();
			m_Skin = ScriptableObject.CreateInstance<DexcRuntimeUISkin>();
			m_Skin.SetWorldVisualShaderForEditor(
				AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader")
			);
			m_Skin.SetUIToolkitPanelSettingsTemplateForEditor(
				AssetDatabase.LoadAssetAtPath<PanelSettings>(
					"Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitPanelSettings.asset"
				)
			);
			Dexc.DexcDebugger.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			DexcRuntimeUIState.SetSession(Dexc.DexcDebugger.CurrentSession.SessionId);
			DebugMenu.Open();
		}

		[TearDown]
		public void TearDown()
		{
			DebugMenu.Close();
			DexcRuntimeUIRenderer.ClearProfileOverrideForTests();
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(false);
			DexcRuntimeUIToolkitRenderer.DestroyForTests();
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(false);
			DexcRuntimeUGUIRenderer.DestroyForTests();
#endif
			DexcRuntimeUIState.LayoutOverride = null;
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.ResetForTests();
			foreach (var value in m_Objects)
			{
				if (value != null)
					Object.DestroyImmediate(value);
			}
			m_Objects.Clear();
			if (m_Skin != null)
				Object.DestroyImmediate(m_Skin);
		}

		GameObject NewObject(string name)
		{
			var value = new GameObject(name);
			m_Objects.Add(value);
			return value;
		}

		[UnityTest]
		public IEnumerator EveryPageStaysInsideThePanelFromPhoneToFourK()
		{
			if (Application.isBatchMode)
				Assert.Ignore("Requires the automatic --graphics runner.");
			var overflows = new List<string>();
			foreach (var size in Sizes.Where(size => Included(0, size.Name)))
			{
				var restore = SetGameViewSize(size.Width, size.Height);
				try
				{
					for (var attempt = 0; (Screen.width != size.Width || Screen.height != size.Height) && attempt < 120; attempt++)
						yield return null;
					Assert.That(Screen.width, Is.EqualTo(size.Width), size.Name + " width");
					Assert.That(Screen.height, Is.EqualTo(size.Height), size.Name + " height");
					var directory = Path.Combine(Output, size.Name);
					Directory.CreateDirectory(directory);
					foreach (var renderer in new[] { "IMGUI", "UIToolkit", "UGUI" })
					{
#if !DEXC_HAS_UGUI
						if (renderer == "UGUI")
							continue;
#endif
						if (!Included(1, renderer))
							continue;
						TearDown();
						DexcRuntimeBootstrap.InitializeForTests();
						SetUp();
						var camera = NewObject("Resolution capture camera").AddComponent<Camera>();
						camera.clearFlags = CameraClearFlags.SolidColor;
						camera.backgroundColor = Color.black;
						camera.cullingMask = 0;
						DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(renderer == "UIToolkit");
#if DEXC_HAS_UGUI
						DexcRuntimeUIRendererSelection.SetUseUGUIForTests(renderer == "UGUI");
#endif
						foreach (var state in States.Where(state => Included(2, state)))
						{
							var safe = new Rect(0, 0, Screen.width, Screen.height);
							DexcRuntimeUIState.LayoutOverride = null;
							if (state.StartsWith("split-"))
							{
								DexcRuntimeUIState.LayoutOverride = DexcRuntimeUILayoutMode.Split;
								if (!DexcRuntimeUIReferenceResolver.Calculate(Screen.width, Screen.height, safe, size.Profile).Split)
									continue;
							}
							Prepare(state);
							Render(renderer, size.Profile);
							for (var frame = 0; frame < 8; frame++)
								yield return null;
							yield return new WaitForEndOfFrame();
							var texture = ScreenCapture.CaptureScreenshotAsTexture();
							try
							{
								var panel = DexcRuntimeUIReferenceResolver.Calculate(Screen.width, Screen.height, safe, size.Profile).Panel;
								var outside = BrightPixelsOutside(texture, panel);
								if (outside > 0)
									overflows.Add(size.Name + " " + renderer + " " + state + ": " + outside + " px outside the panel");
								File.WriteAllBytes(Path.Combine(directory, renderer + "-" + state + ".png"), texture.EncodeToPNG());
							}
							finally
							{
								Object.DestroyImmediate(texture);
							}
						}
					}
				}
				finally
				{
					restore();
				}
			}
			Assert.That(overflows, Is.Empty, string.Join("\n", overflows));
		}

		void Prepare(string state)
		{
			switch (state)
			{
				case "home":
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
					break;
				case "command-list":
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
					break;
				case "command-detail":
				case "split-command":
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
					SelectCommand("UI02/LongCommand");
					break;
				case "command-error":
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
					SelectCommand("UI02/LongCommand");
					DexcRuntimeCommandForm.DetailRows().First(row => row.Kind == DexcUIRowKind.Input && row.Changed != null).Changed("x");
					break;
				case "setting-list":
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.SettingList);
					break;
				case "setting-detail":
				case "split-setting":
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.SettingList);
					DexcRuntimeSettingForm.Open(
						DebugSetting.GetList(new DebugListQuery("Display/HUD", "", new DebugPageRequest(0, 2))).Items.First()
					);
					break;
				case "inspector-fields":
					OpenInspectable();
					break;
				case "collection":
					var collection = NewObject("Resolution collection").AddComponent<DexcCollectionFixture>();
					collection.Values = Enumerable.Range(0, 40).ToList();
					DebugInspector.Register("Resolution/Collection", collection, "Collection");
					var target = DebugInspector
						.GetRegistered(new DebugListQuery("Resolution/Collection", "", new DebugPageRequest(0, 2)))
						.Entries.Single()
						.Target;
					var component = DebugInspector
						.GetComponents(target)
						.Components.Single(item => item.TypeName == typeof(DexcCollectionFixture).FullName)
						.Component;
					DexcRuntimeUIState.InspectorTarget = target;
					DexcRuntimeInspectorForm.Open(component);
					DexcRuntimeInspectorForm.OpenText(DexcRuntimeInspectorForm.Fields.Single());
					break;
				case "history":
					OpenInspectable();
					DexcRuntimeInspectorForm.Change(DexcRuntimeInspectorForm.Fields.Single(field => field.Descriptor.Name == "Health"), "200");
					DexcRuntimeInspectorForm.Apply();
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.History);
					break;
				case "diagnostics":
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Diagnostic);
					break;
			}
		}

		void OpenInspectable()
		{
			var inspectable = NewObject("Resolution target").AddComponent<DexcUIR05Inspectable>();
			DebugInspector.Register("Resolution/Target", inspectable, "Resolution target");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("Resolution/Target", "", new DebugPageRequest(0, 2)))
				.Entries.First()
				.Target;
			DexcRuntimeUIState.InspectorTarget = target;
			var handle = DebugInspector
				.GetComponents(target)
				.Entries.Single(item => item.TypeName == typeof(DexcUIR05Inspectable).FullName)
				.Component;
			DexcRuntimeInspectorForm.Open(handle, "Resolution component");
		}

		static void SelectCommand(string search)
		{
			DexcRuntimeUIRenderer.SelectCommandForTests(
				DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, search, new DebugPageRequest(0, 8))).Items.First()
			);
		}

		static void Render(string renderer, DexcRuntimeUIProfileKind profile)
		{
			var safe = new Rect(0, 0, Screen.width, Screen.height);
			// The display layer finds the open panel through this profile, so every renderer sets it to keep the text clear.
			DexcRuntimeUIRenderer.SetProfileOverrideForTests(profile);
			if (renderer == "UIToolkit")
				DexcRuntimeUIToolkitRenderer.RenderProfileForTests(Screen.width, Screen.height, safe, profile);
#if DEXC_HAS_UGUI
			else if (renderer == "UGUI")
				DexcRuntimeUGUIRenderer.RenderProfileForTests(Screen.width, Screen.height, safe, profile);
#endif
		}

		/// <summary>
		/// The camera clears to black, so anything lit in a band around the panel was drawn past its edge. The band stays
		/// clear of the display layer's frame counter in the screen's corner.
		/// </summary>
		static int BrightPixelsOutside(Texture2D texture, Rect panel)
		{
			const int band = 48;
			var pixels = texture.GetPixels32();
			var count = 0;
			// The panel rect and the captured rows both start at the bottom of the screen.
			var bottom = Mathf.FloorToInt(panel.yMin) - 1;
			var top = Mathf.CeilToInt(panel.yMax);
			var left = Mathf.FloorToInt(panel.xMin) - 1;
			var right = Mathf.CeilToInt(panel.xMax);
			for (var y = Mathf.Max(0, bottom - band); y < Mathf.Min(texture.height, top + band); y++)
			{
				for (var x = Mathf.Max(0, left - band); x < Mathf.Min(texture.width, right + band); x++)
				{
					if (y >= bottom && y <= top && x >= left && x <= right)
						continue;
					var pixel = pixels[y * texture.width + x];
					if (pixel.r + pixel.g + pixel.b > 60)
						count++;
				}
			}
			return count;
		}

		static Action SetGameViewSize(int width, int height)
		{
			var editorAssembly = typeof(Editor).Assembly;
			var gameViewType = editorAssembly.GetType("UnityEditor.GameView", true);
			var gameViewSizesType = editorAssembly.GetType("UnityEditor.GameViewSizes", true);
			var gameViewSizeType = editorAssembly.GetType("UnityEditor.GameViewSize", true);
			var gameViewSizeTypeEnum = editorAssembly.GetType("UnityEditor.GameViewSizeType", true);
			var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(gameViewSizesType);
			var singleton = singletonType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
			var currentGroupType = singleton
				.GetType()
				.GetProperty("currentGroupType", BindingFlags.Public | BindingFlags.Instance)
				.GetValue(singleton, null);
			var group = singleton
				.GetType()
				.GetMethod("GetGroup", BindingFlags.Public | BindingFlags.Instance)
				.Invoke(singleton, new[] { currentGroupType });
			var fixedResolution = Enum.Parse(gameViewSizeTypeEnum, "FixedResolution");
			var constructor = gameViewSizeType.GetConstructor(
				BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
				null,
				new[] { gameViewSizeTypeEnum, typeof(int), typeof(int), typeof(string) },
				null
			);
			var size = constructor.Invoke(new object[] { fixedResolution, width, height, "Dexc Resolution " + width + "x" + height });
			group.GetType().GetMethod("AddCustomSize", BindingFlags.Public | BindingFlags.Instance).Invoke(group, new[] { size });
			var index =
				(int)group.GetType().GetMethod("GetTotalCount", BindingFlags.Public | BindingFlags.Instance).Invoke(group, null) - 1;
			var gameView = EditorWindow.GetWindow(gameViewType);
			var selectedSizeIndex = gameViewType.GetProperty(
				"selectedSizeIndex",
				BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
			);
			var previousIndex = (int)selectedSizeIndex.GetValue(gameView, null);
			selectedSizeIndex.SetValue(gameView, index, null);
			gameView.Repaint();
			EditorApplication.QueuePlayerLoopUpdate();
			return () =>
			{
				selectedSizeIndex.SetValue(gameView, previousIndex, null);
				group
					.GetType()
					.GetMethod("RemoveCustomSize", BindingFlags.Public | BindingFlags.Instance)
					.Invoke(group, new object[] { index });
				gameView.Repaint();
			};
		}
	}
}
#endif
