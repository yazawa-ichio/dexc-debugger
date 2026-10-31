#if DEXC_DEBUGGER && UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dexc;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DexcDebugger.Tests
{
	public sealed class DexcUIIntegrationTests
	{
		private DexcRuntimeUISkin m_Skin;
		private Font m_Font;
		private GameObject m_Camera;
		private GameObject m_Events;
		private readonly Dictionary<string, Rect> m_ScreenControls = new Dictionary<string, Rect>();
		private readonly Dictionary<string, Rect> m_Controls = new Dictionary<string, Rect>();
		private static readonly Vector3[] s_Cases =
		{
			new Vector3(260, 260, 28),
			new Vector3(320, 520, 36),
			new Vector3(400, 260, 44),
			new Vector3(320, 520, 48),
			new Vector3(260, 520, 48),
			new Vector3(400, 260, 28),
		};

		private void Initialize(Vector3 dimensions)
		{
			DexcRuntimeBootstrap.InitializeForTests();
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.ResetForTests();
			DexcUIR07Settings.ResetCurrent();
			m_Font = AssetDatabase.LoadAssetAtPath<Font>("Assets/DexcDebugger/Font/NotoSansJP-Regular.ttf");
			Assert.That(m_Font, Is.Not.Null);
			m_Font.RequestCharactersInTexture("Ag09あ国gjpq最大体力!", 13, FontStyle.Normal);
			foreach (var character in "Ag09あ国gjpq最大体力!")
			{
				Assert.That(m_Font.GetCharacterInfo(character, out _, 13), Is.True, character.ToString());
			}

			m_Skin = ScriptableObject.CreateInstance<DexcRuntimeUISkin>();
			m_Skin.SetWorldVisualShaderForEditor(
				AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader")
			);
			m_Skin.SetUIToolkitPanelSettingsTemplateForEditor(
				AssetDatabase.LoadAssetAtPath<PanelSettings>(
					"Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitPanelSettings.asset"
				)
			);
			var serialized = new SerializedObject(m_Skin);
			serialized.FindProperty("m_Font").objectReferenceValue = m_Font;
			foreach (var profile in new[] { "m_Windows", "m_AndroidPortrait", "m_AndroidLandscape" })
			{
				serialized.FindProperty(profile + ".m_PanelSize").vector2Value = new Vector2(dimensions.x, dimensions.y);
				serialized.FindProperty(profile + ".m_RowHeight").floatValue = dimensions.z;
				serialized.FindProperty(profile + ".m_MinimumHitHeight").floatValue = 28;
			}

			serialized.ApplyModifiedPropertiesWithoutUndo();
			Dexc.DexcDebugger.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			DexcRuntimeUIState.SetSession(Dexc.DexcDebugger.CurrentSession.SessionId);
			DebugMenu.Open();
			DexcRuntimeSettingForm.Open(DexcRuntimeSettingForm.ReadGroups("ZZUIR07/Form").Single().Items[0]);
			m_Camera = new GameObject("UIR07 capture camera");
			var camera = m_Camera.AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = Color.black;
			camera.cullingMask = 0;
#if DEXC_HAS_UGUI
			if (Resources.FindObjectsOfTypeAll(DexcUGUIApi.EventSystemType).Length == 0)
			{
				m_Events = new GameObject("UIR07 EventSystem", DexcUGUIApi.EventSystemType);
			}

#endif
			DexcRuntimeUIRenderer.ValueControlDrawnForTests = (key, rect) => m_Controls[key] = rect;
			DexcRuntimeUIRenderer.ValueControlScreenRectForTests = (key, rect) => m_ScreenControls[key] = rect;
		}

		[TearDown]
		public void TearDown()
		{
			DexcRuntimeUIRenderer.ValueControlDrawnForTests = null;
			DexcRuntimeUIRenderer.ValueControlScreenRectForTests = null;
			m_ScreenControls.Clear();
			m_Controls.Clear();
			DebugMenu.Close();
			DexcRuntimeUIRenderer.ClearProfileOverrideForTests();
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(false);
			DexcRuntimeUIToolkitRenderer.DestroyForTests();
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(false);
			DexcRuntimeUGUIRenderer.DestroyForTests();
#endif
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.ResetForTests();
			if (m_Skin != null)
			{
				Object.DestroyImmediate(m_Skin);
			}

			if (m_Camera != null)
			{
				Object.DestroyImmediate(m_Camera);
			}

			if (m_Events != null)
			{
				Object.DestroyImmediate(m_Events);
			}
		}

		private static void Render(string renderer, DexcRuntimeUIProfileKind profile)
		{
			var safe = new Rect(0, 0, Screen.width, Screen.height);
			if (renderer == "UIToolkit")
			{
				DexcRuntimeUIToolkitRenderer.RenderProfileForTests(Screen.width, Screen.height, safe, profile);
			}
#if DEXC_HAS_UGUI
			else if (renderer == "UGUI")
			{
				DexcRuntimeUGUIRenderer.RenderProfileForTests(Screen.width, Screen.height, safe, profile);
			}
#endif
			else
			{
				DexcRuntimeUIRenderer.SetProfileOverrideForTests(profile);
			}
		}

		private static IEnumerator Settle()
		{
			for (var frame = 0; frame < 10; frame++)
			{
				yield return null;
			}

			// Without a Game view the batch Editor never renders a frame, so WaitForEndOfFrame would never resume. A fixture
			// that ran earlier may have opened one, which is why this only shows when the fixture runs on its own.
			if (!Application.isBatchMode)
				yield return new WaitForEndOfFrame();
		}

		[UnityTest]
		public IEnumerator FormsKeepTextGeometryAndReachLastFieldAcrossProfiles()
		{
			if (Application.isBatchMode)
			{
				Assert.Ignore("Pixel/geometry capture runs in the automatic --graphics suite.");
			}
			foreach (var renderer in new[] { "IMGUI", "UIToolkit", "UGUI" })
			{
#if !DEXC_HAS_UGUI
				if (renderer == "UGUI")
					continue;
#endif
				for (var index = 0; index < s_Cases.Length; index++)
				{
					var dimensions = s_Cases[index];
					var profile = new[]
					{
						DexcRuntimeUIProfileKind.Windows,
						DexcRuntimeUIProfileKind.AndroidPortrait,
						DexcRuntimeUIProfileKind.AndroidLandscape,
					}[index % 3];
					Initialize(dimensions);
					Render(renderer, profile);
					yield return Settle();
					var first = DexcRuntimeSettingForm.Fields.First();
					Assert.That(first.Label, Does.Contain("最大体力"));
					var before = InputRect(renderer, first.Item.Id);
					Assert.That(before.height, Is.EqualTo(dimensions.z).Within(1), renderer + " row height");
					ValidateText(renderer);
#if DEXC_HAS_UGUI
					if (renderer == "UGUI")
					{
						var input = DexcRuntimeUGUIRenderer.SettingInputForTests(first.Item.Id);
						DexcUGUIApi.SetSelected(input.gameObject);
						input.GetType().GetMethod("ActivateInputField").Invoke(input, null);
						yield return Settle();
						DexcUGUIApi.Set(input, "selectionAnchorPosition", 0);
						DexcUGUIApi.Set(input, "selectionFocusPosition", 3);
						yield return Settle();
						AssertSelectionMeshInsideInput(input);
						var marker = (RectTransform)input.transform.parent.Find("FocusMarker");
						var labelBounds = (RectTransform)input.transform.parent.Find("Label");
						var markerCorners = new Vector3[4];
						var labelCorners = new Vector3[4];
						marker.GetWorldCorners(markerCorners);
						labelBounds.GetWorldCorners(labelCorners);
						Assert.That(markerCorners[2].x, Is.LessThanOrEqualTo(labelCorners[0].x), "Focus marker must not cover the label.");
						var close = DexcRuntimeUGUIRenderer.RootForTests.transform.Find("SafeAreaRoot/Panel/Header/Close/Label");
						var closeMesh = close.GetComponent<CanvasRenderer>().GetMesh();
						Assert.That(closeMesh, Is.Not.Null);
						Assert.That(
							closeMesh.vertexCount,
							Is.GreaterThanOrEqualTo(4),
							"Close symbol must actually render at the minimum row height."
						);
					}

#endif
					Capture(renderer, profile, dimensions, "top");
					DexcRuntimeSettingForm.Change(first, "-");
					yield return Settle();
					Assert.That(InputRect(renderer, first.Item.Id), Is.EqualTo(before), renderer + " error must not resize input");
					Assert.That(DexcRuntimeSettingForm.CanApply, Is.False);
					Capture(renderer, profile, dimensions, "error");
					DexcRuntimeSettingForm.Change(first, "100");
					var last = DexcRuntimeSettingForm.Fields.Last();
					Assert.That(last.Label, Does.Contain("最終"));
					if (renderer == "UIToolkit")
					{
						DexcRuntimeUIToolkitRenderer.ContentListForTests.ScrollToItem(-1);
					}
#if DEXC_HAS_UGUI
					else if (renderer == "UGUI")
					{
						DexcUGUIApi.Set(DexcRuntimeUGUIRenderer.ScrollRectForTests, "verticalNormalizedPosition", 0f);
					}
#endif
					else
					{
						DexcRuntimeUIState.ScrollPosition = new Vector2(0, 100000);
					}

					yield return Settle();
					AssertLastFieldVisible(renderer, last.Item.Id, profile);
					Capture(renderer, profile, dimensions, "bottom");
					TearDown();
					yield return null;
				}
			}
		}

		private Rect InputRect(string renderer, DebugSettingId id)
		{
			if (renderer == "UIToolkit")
			{
				var field = DexcRuntimeUIToolkitRenderer.SettingInputForTests(id);
				Assert.That(field, Is.Not.Null);
				return field.Q<VisualElement>(className: "unity-base-text-field__input").layout;
			}

#if DEXC_HAS_UGUI
			if (renderer == "UGUI")
			{
				return ((RectTransform)DexcRuntimeUGUIRenderer.SettingInputForTests(id).transform).rect;
			}

#endif
			return m_Controls.Single(pair => pair.Key == "setting:" + id).Value;
		}

		private void ValidateText(string renderer)
		{
			if (renderer == "UIToolkit")
			{
				Assert.That(DexcRuntimeUIToolkitRenderer.HasHorizontalOverflowForTests, Is.False);
				var inputs = DexcRuntimeUIToolkitRenderer.ContentListForTests.Query<TextField>().ToList().Where(IsDisplayed).ToArray();
				Assert.That(inputs.Length, Is.GreaterThan(0));
				foreach (var input in inputs)
				{
					var value = input.Q<VisualElement>(className: "unity-base-text-field__input");
					Assert.That(value.resolvedStyle.unityTextAlign, Is.EqualTo(TextAnchor.MiddleLeft));
					var glyphs = value.Q<TextElement>();
					var line = glyphs.MeasureTextSize(
						"Ag09あ国gjpq",
						0,
						VisualElement.MeasureMode.Undefined,
						0,
						VisualElement.MeasureMode.Undefined
					);
					Assert.That(
						line.y,
						Is.LessThanOrEqualTo(value.contentRect.height + 1),
						input.label + " input=" + input.worldBound + " value=" + value.worldBound
					);
					Assert.That(value.resolvedStyle.paddingLeft, Is.EqualTo(6).Within(1));
				}
			}
#if DEXC_HAS_UGUI
			else if (renderer == "UGUI")
			{
				Assert.That(DexcRuntimeUGUIRenderer.HasHorizontalOverflowForTests, Is.False);
				foreach (var field in DexcRuntimeSettingForm.Fields)
				{
					var input = DexcRuntimeUGUIRenderer.SettingInputForTests(field.Item.Id);
					if (input == null)
					{
						continue;
					}

					var text = (Component)DexcUGUIApi.Get(input, "textComponent");
					Assert.That(DexcUGUIApi.Get(text, "alignment"), Is.EqualTo(TextAnchor.MiddleLeft));
					Assert.That(DexcUGUIApi.Get(text, "font"), Is.SameAs(m_Font));
					Assert.That(DexcUGUIApi.Get(text, "fontStyle"), Is.EqualTo(FontStyle.Normal));
					Assert.That(
						input.transform.parent.GetComponent<CanvasRenderer>().GetColor(),
						Is.EqualTo(Color.white),
						"A reused input must stop the old Button tint tween."
					);
					var label = input.transform.parent.Find("Label").GetComponent(DexcUGUIApi.TextType);
					Assert.That(
						((RectTransform)label.transform).rect.height + 1,
						Is.GreaterThanOrEqualTo(Convert.ToSingle(DexcUGUIApi.Get(label, "preferredHeight")))
					);
					Assert.That(
						((RectTransform)text.transform).rect.height + 1,
						Is.GreaterThanOrEqualTo(Convert.ToSingle(DexcUGUIApi.Get(text, "preferredHeight")))
					);
				}
			}
#endif
			else
			{
				Assert.That(DexcRuntimeUIRenderer.EnsureStyleCache().TextField.alignment, Is.EqualTo(TextAnchor.MiddleLeft));
				Assert.That(m_Controls.Count, Is.GreaterThan(5));
			}
		}

#if DEXC_HAS_UGUI
		private static void AssertSelectionMeshInsideInput(Component input)
		{
			var caret = (RectTransform)
				input
					.GetType()
					.GetField("caretRectTrans", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
					.GetValue(input);
			Assert.That(caret, Is.Not.Null);
			var mesh = caret.GetComponent<CanvasRenderer>().GetMesh();
			Assert.That(mesh, Is.Not.Null);
			Assert.That(mesh.vertexCount, Is.GreaterThanOrEqualTo(4));
			var body = (RectTransform)input.transform;
			var text = (Component)DexcUGUIApi.Get(input, "textComponent");
			var textMesh = text.GetComponent<CanvasRenderer>().GetMesh();
			Assert.That(textMesh, Is.Not.Null);
			foreach (var vertex in textMesh.vertices)
			{
				var point = body.InverseTransformPoint(text.transform.TransformPoint(vertex));
				Assert.That(
					point.x,
					Is.InRange(body.rect.xMin - 1, body.rect.xMax + 1),
					"Rendered text must stay inside the input horizontally."
				);
				Assert.That(
					point.y,
					Is.InRange(body.rect.yMin - 1, body.rect.yMax + 1),
					"Rendered text must stay inside the input vertically."
				);
			}

			foreach (var vertex in mesh.vertices)
			{
				var point = body.InverseTransformPoint(caret.TransformPoint(vertex));
				Assert.That(
					point.x,
					Is.InRange(body.rect.xMin - 1, body.rect.xMax + 1),
					"Native selection must stay inside the input horizontally."
				);
				Assert.That(
					point.y,
					Is.InRange(body.rect.yMin - 1, body.rect.yMax + 1),
					"Native selection must stay inside the input vertically."
				);
			}
		}
#endif

		private static bool IsDisplayed(VisualElement element)
		{
			for (var parent = element; parent != null; parent = parent.parent)
			{
				if (parent.resolvedStyle.display == DisplayStyle.None)
				{
					return false;
				}
			}

			return true;
		}

		private void AssertLastFieldVisible(string renderer, DebugSettingId id, DexcRuntimeUIProfileKind profile)
		{
			if (renderer == "UIToolkit")
			{
				var field = DexcRuntimeUIToolkitRenderer.SettingInputForTests(id);
				Assert.That(field, Is.Not.Null);
				var viewport = DexcRuntimeUIToolkitRenderer.ContentListForTests.Q<ScrollView>().contentViewport.worldBound;
				Assert.That(field.worldBound.yMax, Is.LessThanOrEqualTo(viewport.yMax + 1));
				Assert.That(field.worldBound.yMin, Is.GreaterThanOrEqualTo(viewport.yMin - 1));
			}
#if DEXC_HAS_UGUI
			else if (renderer == "UGUI")
			{
				var field = DexcRuntimeUGUIRenderer.SettingInputForTests(id);
				var viewport = DexcRuntimeUGUIRenderer.ViewportForTests;
				var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, field.transform);
				Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(viewport.rect.yMin - 1));
				Assert.That(bounds.max.y, Is.LessThanOrEqualTo(viewport.rect.yMax + 1));
			}
#endif
			else
			{
				Assert.That(DexcRuntimeUIState.ScrollPosition.y, Is.InRange(1, 99999));
				var viewport = DexcRuntimeUIRenderer
					.CalculateLayoutForProfileTests(
						Screen.width,
						Screen.height,
						new Rect(0, 0, Screen.width, Screen.height),
						DexcRuntimeUIPage.SettingDetail,
						profile
					)
					.ContentViewport;
				viewport.y = Screen.height - viewport.yMax;
				var rect = m_ScreenControls["setting:" + id];
				// Renderer geometry uses bottom-left screen coordinates, like the resolved viewport.
				Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(viewport.yMin - 1));
				Assert.That(rect.yMax, Is.LessThanOrEqualTo(viewport.yMax + 1));
			}
		}

		private static void Capture(string renderer, DexcRuntimeUIProfileKind profile, Vector3 dimensions, string state)
		{
			var texture = ScreenCapture.CaptureScreenshotAsTexture();
			try
			{
				var panel = DexcRuntimeUIReferenceResolver
					.Calculate(Screen.width, Screen.height, new Rect(0, 0, Screen.width, Screen.height), profile)
					.Panel;
				var pixels = texture.GetPixels(
					Mathf.CeilToInt(panel.xMin),
					Mathf.CeilToInt(panel.yMin),
					Mathf.FloorToInt(panel.width) - 1,
					Mathf.FloorToInt(panel.height) - 1
				);
				Assert.That(
					pixels.Count(pixel => pixel.r > .5f && pixel.g > .5f && pixel.b > .5f),
					Is.GreaterThan(30),
					"Expected text inside the menu."
				);
				var folder = "Logs/UnityTests/UIR07/Runtime";
				Directory.CreateDirectory(folder);
				File.WriteAllBytes(
					folder
						+ "/"
						+ renderer
						+ "-"
						+ profile
						+ "-"
						+ dimensions.x
						+ "x"
						+ dimensions.y
						+ "-row"
						+ dimensions.z
						+ "-"
						+ state
						+ ".png",
					texture.EncodeToPNG()
				);
				File.WriteAllText(
					folder
						+ "/"
						+ renderer
						+ "-"
						+ profile
						+ "-"
						+ dimensions.x
						+ "x"
						+ dimensions.y
						+ "-row"
						+ dimensions.z
						+ "-"
						+ state
						+ ".json",
					string.Format(
						System.Globalization.CultureInfo.InvariantCulture,
						"{{\"x\":{0},\"y\":{1},\"width\":{2},\"height\":{3},\"screenWidth\":{4},\"screenHeight\":{5}}}",
						panel.x,
						Screen.height - panel.yMax,
						panel.width,
						panel.height,
						texture.width,
						texture.height
					)
				);
			}
			finally
			{
				Object.DestroyImmediate(texture);
			}
		}

		[UnityTest]
		public IEnumerator DiagnosticRowsOpenRealDetailsAcrossRenderers()
		{
			foreach (var renderer in new[] { "IMGUI", "UIToolkit", "UGUI" })
			{
#if !DEXC_HAS_UGUI
				if (renderer == "UGUI")
					continue;
#endif
				Initialize(new Vector3(320, 260, 28));
				var record = Dexc.DexcDebugger.CurrentSession.Diagnostics.AddOperation(
					DebugDiagnosticSeverity.Error,
					DebugFeature.Setting,
					"write",
					"uir07-detail",
					"詳細の本文がここに表示されます",
					"UIR07",
					"test"
				);
				DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Diagnostic);
				Render(renderer, DexcRuntimeUIProfileKind.Windows);
				yield return Settle();
				var rows = DexcRuntimeInspectorNavigation.Rows();
				var row = rows.Single(item => item.Key == record.DiagnosticId.ToString());
				if (renderer == "UIToolkit")
				{
					var index = Enumerable
						.Range(0, DexcRuntimeUIToolkitRenderer.SelectableCount)
						.Single(candidate => DexcRuntimeUIToolkitRenderer.SelectableTextForTests(candidate) == row.Label);
					DexcRuntimeUIToolkitRenderer.SendClickForTests(index);
				}
#if DEXC_HAS_UGUI
				else if (renderer == "UGUI")
				{
					var button = DexcRuntimeUGUIRenderer
						.RootForTests.GetComponentsInChildren(DexcUGUIApi.ButtonType)
						.First(candidate =>
							candidate
								.GetComponentsInChildren(DexcUGUIApi.TextType)
								.Any(text => Equals(DexcUGUIApi.Get(text, "text"), row.Label))
						);
					DexcUGUIApi.InvokeButton(button);
				}
#endif
				else
				{
					DexcRuntimeUIState.SetSelectedIndex(rows.ToList().IndexOf(row));
					DexcRuntimeUIRenderer.ActivateProviderSelection();
				}

				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.FormDetail));
				Assert.That(DexcRuntimeFormDetail.Message, Does.Contain("詳細の本文"));
				TearDown();
				yield return null;
			}
		}
	}

	public sealed class DexcUIR07Settings : DebugSettingSet<DexcUIR07Settings>
	{
		[DebugSetting(
			"ZZUIR07/Form/A 最大体力装備パッシブスキル一時的なバフによる補正をすべて含む",
			ApplyMode = DebugSettingApplyMode.Submit
		)]
		public int Health { get; private set; } = 100;

		[DebugSetting("ZZUIR07/Form/B 表示の有効化")]
		public bool Visible { get; private set; } = true;

		[DebugSetting("ZZUIR07/Form/C 速度"), DebugRange(0, 4), DebugSlider]
		public float Speed { get; private set; } = 1;

		[DebugSetting("ZZUIR07/Form/D readonly", Access = DebugSettingAccess.ReadOnly)]
		public long ReadOnly { get; private set; } = long.MaxValue;

		[DebugSetting("ZZUIR07/Form/E 位置", ApplyMode = DebugSettingApplyMode.Submit)]
		public Vector3 Position { get; private set; } = new Vector3(.125f, 2, 3);

		[DebugSetting("ZZUIR07/Form/F 文字列", ApplyMode = DebugSettingApplyMode.Submit)]
		public string Text { get; private set; } = "Ag09あ国gjpq";

		[DebugSetting("ZZUIR07/Form/Z 最終値", ApplyMode = DebugSettingApplyMode.Submit)]
		public double Tail { get; private set; } = .125;
	}
}
#endif
