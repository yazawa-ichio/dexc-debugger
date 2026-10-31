#if DEXC_DEBUGGER && UNITY_EDITOR
using System;
using System.Collections;
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
	public sealed class DexcFormControlTests
	{
		DexcRuntimeUISkin m_Skin;
		GameObject m_EventSystem;
		GameObject m_Camera;

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
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.ResetForTests();
			if (m_Skin != null)
				Object.DestroyImmediate(m_Skin);
			m_Skin = null;
			if (m_EventSystem != null)
				Object.DestroyImmediate(m_EventSystem);
			m_EventSystem = null;
			if (m_Camera != null)
				Object.DestroyImmediate(m_Camera);
			m_Camera = null;
		}

		void Initialize(float height, string commandPath = "Tests/UIR02/Form")
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
			var serialized = new SerializedObject(m_Skin);
			serialized.FindProperty("m_Windows.m_RowHeight").floatValue = height;
			serialized.FindProperty("m_Font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(
				"Assets/DexcDebugger/Font/NotoSansJP-Regular.ttf"
			);
			serialized.ApplyModifiedPropertiesWithoutUndo();
			Dexc.DexcDebugger.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			var list = DebugCommand.Registry.GetList(
				new DebugListQuery(commandPath, string.Empty, DexcRuntimeUIState.CreatePageRequest(0, 1))
			);
			DexcRuntimeUIState.SetSession(list.Summary.SessionId);
			DebugMenu.Open();
			DexcRuntimeUIRenderer.SelectCommandForTests(list.Items.Single());
		}

		[UnityTest]
		public IEnumerator ProductionFormCapturesAtCompactAndExpandedHeights()
		{
			if (Application.isBatchMode)
				Assert.Ignore("Requires the automatic PlayMode runner --graphics mode.");
			var output = Path.GetFullPath("Logs/UnityTests/UIR02");
			Directory.CreateDirectory(output);
			foreach (var height in new[] { 28f, 48f })
			foreach (var renderer in new[] { "IMGUI", "UIToolkit", "UGUI" })
			{
#if !DEXC_HAS_UGUI
				if (renderer == "UGUI")
					continue;
#endif
				DexcRuntimeBootstrap.InitializeForTests();
				Initialize(height);
				m_Camera = new GameObject("UIR02 capture camera");
				var camera = m_Camera.AddComponent<Camera>();
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = Color.black;
				camera.cullingMask = 0;
				var safe = new Rect(0f, 0f, Screen.width, Screen.height);
				if (renderer == "UIToolkit")
				{
					DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(true);
					DexcRuntimeUIToolkitRenderer.RenderProfileForTests(Screen.width, Screen.height, safe, DexcRuntimeUIProfileKind.Windows);
				}
#if DEXC_HAS_UGUI
				else if (renderer == "UGUI")
				{
					DexcRuntimeUIRendererSelection.SetUseUGUIForTests(true);
					DexcRuntimeUGUIRenderer.RenderProfileForTests(Screen.width, Screen.height, safe, DexcRuntimeUIProfileKind.Windows);
				}
#endif
				else
					DexcRuntimeUIRenderer.SetProfileOverrideForTests(DexcRuntimeUIProfileKind.Windows);
				for (var i = 0; i < 8; i++)
					yield return null;
				yield return new WaitForEndOfFrame();
				Assert.That(DexcRuntimeUIState.IsOpen && DexcRuntimeUIState.HasDraft, Is.True);
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
				var texture = ScreenCapture.CaptureScreenshotAsTexture();
				try
				{
					Assert.That(texture.width, Is.EqualTo(Screen.width));
					Assert.That(texture.height, Is.EqualTo(Screen.height));
					var panel = DexcRuntimeUIReferenceResolver
						.Calculate(Screen.width, Screen.height, safe, DexcRuntimeUIProfileKind.Windows)
						.Panel;
					var panelPixels = texture.GetPixels(
						Mathf.CeilToInt(panel.xMin),
						Mathf.CeilToInt(panel.yMin),
						Mathf.FloorToInt(panel.width) - 1,
						Mathf.FloorToInt(panel.height) - 1
					);
					Assert.That(
						panelPixels.Count(pixel => pixel.r + pixel.g + pixel.b > 0.08f),
						Is.GreaterThan(panelPixels.Length / 2),
						"The menu region must be rendered, not only unrelated scene content."
					);
					Assert.That(
						panelPixels.Count(pixel => pixel.r > 0.5f && pixel.g > 0.5f && pixel.b > 0.5f),
						Is.GreaterThan(30),
						"The menu region must include text glyphs."
					);
					File.WriteAllBytes(Path.Combine(output, renderer + "-" + height + ".png"), texture.EncodeToPNG());
				}
				finally
				{
					Object.DestroyImmediate(texture);
				}
				TearDown();
				yield return null;
			}
		}

		static void SendPointerUp(VisualElement element)
		{
			var systemEvent = new UnityEngine.Event
			{
				type = EventType.MouseUp,
				button = 0,
				mousePosition = element.worldBound.center,
			};
			using (var pointer = PointerUpEvent.GetPooled(systemEvent))
			{
				pointer.target = element;
				element.SendEvent(pointer);
			}
		}

		[UnityTest]
		public IEnumerator ToolkitInputHeightLabelAndErrorGeometryRemainStable()
		{
			foreach (var height in new[] { 28f, 36f, 44f, 48f })
			{
				Initialize(height);
				DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(true);
				DexcRuntimeUIToolkitRenderer.CreateForBootstrap();
				DexcRuntimeUIToolkitRenderer.RenderProfileForTests(1366, 768, new Rect(0f, 0f, 1366f, 768f));
				for (var i = 0; i < 8; i++)
					yield return null;
				var input = DexcRuntimeUIToolkitRenderer
					.ContentListForTests.Query<TextField>()
					.ToList()
					.First(field => field.label.Contains("最大体力"));
				var value = input.Q<VisualElement>(className: "unity-base-text-field__input");
				var label = input.Q<Label>();
				Assert.That(value.worldBound.height, Is.EqualTo(height).Within(1f));
				// The label sits beside the value, or above it when it wraps too far for the label column.
				Assert.That(
					label.worldBound.yMax + 4f <= value.worldBound.yMin || label.worldBound.xMax <= value.worldBound.xMin + 0.5f,
					Is.True,
					"label " + label.worldBound + " overlaps value " + value.worldBound
				);
				Assert.That(label.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
				Assert.That(label.text, Does.Contain("含む"));
				Assert.That(value.resolvedStyle.unityTextAlign, Is.EqualTo(TextAnchor.MiddleLeft));
				var textElement = value.Q<TextElement>();
				Assert.That(textElement, Is.Not.Null);
				var line = textElement.MeasureTextSize(
					"Ag09あ国gjpq",
					0f,
					VisualElement.MeasureMode.Undefined,
					0f,
					VisualElement.MeasureMode.Undefined
				);
				Assert.That(
					line.y,
					Is.LessThanOrEqualTo(value.contentRect.height + 1f),
					"The configured font line must fit inside the padded input."
				);
				var before = value.worldBound;
				var footer = DexcRuntimeUIToolkitRenderer.FooterForTests.worldBound;
				input.value = "1e-";
				for (var i = 0; i < 3; i++)
					yield return null;
				Assert.That(input.value, Is.EqualTo("1e-"));
				Assert.That(value.worldBound, Is.EqualTo(before));
				Assert.That(DexcRuntimeUIToolkitRenderer.FooterForTests.worldBound, Is.EqualTo(footer));
				// The message is written under the value, which keeps its place.
				var row = input.parent.parent;
				Assert.That(row.Q<Label>("RowError").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
				Assert.That(row.Q<Button>("FieldError").resolvedStyle.visibility, Is.EqualTo(Visibility.Hidden));
				Assert.That(DexcRuntimeUIToolkitRenderer.ExecuteInteractableForTests, Is.False);
				input.value = "100";
				yield return null;
				Assert.That(row.Q<Label>("RowError").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
				TearDown();
				yield return null;
			}
		}

		[UnityTest]
		public IEnumerator ToolkitInvalidInputWritesItsMessageUnderTheValue()
		{
			Initialize(28f);
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(true);
			DexcRuntimeUIToolkitRenderer.CreateForBootstrap();
			DexcRuntimeUIToolkitRenderer.RenderProfileForTests(1366, 768, new Rect(0f, 0f, 1366f, 768f));
			for (var i = 0; i < 8; i++)
				yield return null;
			var input = DexcRuntimeUIToolkitRenderer
				.ContentListForTests.Query<TextField>()
				.ToList()
				.First(field => field.label.Contains("最大体力"));
			input.value = "1e-";
			for (var i = 0; i < 3; i++)
				yield return null;
			// As in IMGUI, the whole message is under the value: there is no button to open it on another page.
			var row = input.parent.parent;
			Assert.That(row.Q<Button>("FieldError").resolvedStyle.visibility, Is.EqualTo(Visibility.Hidden));
			var message = row.Q<Label>("RowError");
			Assert.That(message.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
			Assert.That(message.text, Is.Not.Empty);
			Assert.That(message.worldBound.yMin, Is.GreaterThanOrEqualTo(input.worldBound.yMax - 0.5f));
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
			Assert.That(input.value, Is.EqualTo("1e-"));
		}

		[UnityTest]
		public IEnumerator ToolkitVectorUsesVerticalComponentsAndKeepsIncompleteInputAcrossOtherEdits()
		{
			Initialize(28f, "Tests/UIR02/Vector");
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(true);
			DexcRuntimeUIToolkitRenderer.CreateForBootstrap();
			DexcRuntimeUIToolkitRenderer.RenderProfileForTests(1366, 768, new Rect(0f, 0f, 1366f, 768f));
			for (var i = 0; i < 8; i++)
				yield return null;
			var fields = DexcRuntimeUIToolkitRenderer
				.ContentListForTests.Query<TextField>()
				.ToList()
				.Where(field => field.resolvedStyle.display != DisplayStyle.None)
				.ToArray();
			Assert.That(fields.Select(field => field.label), Is.EquivalentTo(new[] { "X", "Y", "Z" }));
			var x = fields.Single(field => field.label == "X");
			var y = fields.Single(field => field.label == "Y");
			var z = fields.Single(field => field.label == "Z");
			Assert.That(x.worldBound.yMax, Is.LessThanOrEqualTo(y.worldBound.yMin));
			Assert.That(y.worldBound.yMax, Is.LessThanOrEqualTo(z.worldBound.yMin));
			x.value = "2147483647";
			y.value = "-";
			z.value = "7";
			yield return null;
			Assert.That(y.value, Is.EqualTo("-"));
			Assert.That(DexcRuntimeUIToolkitRenderer.ExecuteInteractableForTests, Is.False);
			y.value = "8";
			yield return null;
			Assert.That(DexcRuntimeUIToolkitRenderer.ExecuteInteractableForTests, Is.True);
			var descriptor = DexcRuntimeUIState.Command.Fields.Single(field => field.IsInput);
			var value = DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, descriptor.Id);
			Assert.That(value.TryGetObject(out var members), Is.True);
			Assert.That(members.Single(member => member.Name == "x").Value.TryGetSignedInteger(out var integer), Is.True);
			Assert.That(integer, Is.EqualTo(2147483647L));
			Assert.That(members.Single(member => member.Name == "z").Value.TryGetSignedInteger(out integer), Is.True);
			Assert.That(integer, Is.EqualTo(7L));
		}

#if DEXC_HAS_UGUI
		[UnityTest]
		public IEnumerator UguiVectorErrorIsWrittenUnderItsComponentWithoutLosingTheOthers()
		{
			Initialize(28f, "Tests/UIR02/Vector");
			if (Resources.FindObjectsOfTypeAll(DexcUGUIApi.EventSystemType).Length == 0)
			{
				m_EventSystem = new GameObject("UIR02 EventSystem");
				m_EventSystem.AddComponent(DexcUGUIApi.EventSystemType);
			}
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(true);
			DexcRuntimeUGUIRenderer.CreateForBootstrap();
			DexcRuntimeUGUIRenderer.RenderProfileForTests(1366, 768, new Rect(0f, 0f, 1366f, 768f));
			for (var i = 0; i < 4; i++)
				yield return null;
			var fields = DexcRuntimeUGUIRenderer
				.RootForTests.GetComponentsInChildren(DexcUGUIApi.InputFieldType)
				.Where(field => field.gameObject.activeInHierarchy)
				.ToArray();
			Assert.That(fields.Length, Is.EqualTo(3));
			Assert.That(fields[0].transform.position.y, Is.GreaterThan(fields[1].transform.position.y));
			Assert.That(fields[1].transform.position.y, Is.GreaterThan(fields[2].transform.position.y));
			DexcUGUIApi.Set(fields[0], "text", "2147483647");
			DexcUGUIApi.Set(fields[1], "text", "-");
			DexcUGUIApi.Set(fields[2], "text", "7");
			yield return null;
			// As in IMGUI, the message is under the component, and the gamepad has no button to stop at for it.
			var row = fields[1].transform.parent;
			var action = row.Find("Error").GetComponent(DexcUGUIApi.ButtonType);
			Assert.That(
				Enumerable.Range(0, DexcRuntimeUGUIRenderer.SelectableCount).Select(DexcRuntimeUGUIRenderer.SelectableForTests),
				Has.No.Member(action)
			);
			var message = row.Find("ErrorLine");
			Assert.That(message.gameObject.activeSelf, Is.True);
			Assert.That(Convert.ToString(DexcUGUIApi.Get(message.GetComponent(DexcUGUIApi.TextType), "text")), Is.Not.Empty);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
			Assert.That(fields.Select(field => (string)DexcUGUIApi.Get(field, "text")), Is.EqualTo(new[] { "2147483647", "-", "7" }));
		}

		[UnityTest]
		public IEnumerator UguiInputHeightFullLabelAndErrorGeometryRemainStable()
		{
			foreach (var height in new[] { 28f, 36f, 44f, 48f })
			{
				Initialize(height);
				DexcRuntimeUIRendererSelection.SetUseUGUIForTests(true);
				DexcRuntimeUGUIRenderer.CreateForBootstrap();
				DexcRuntimeUGUIRenderer.RenderProfileForTests(1366, 768, new Rect(0f, 0f, 1366f, 768f));
				for (var i = 0; i < 3; i++)
					yield return null;
				Canvas.ForceUpdateCanvases();
				var input = DexcRuntimeUGUIRenderer
					.RootForTests.GetComponentsInChildren(DexcUGUIApi.InputFieldType)
					.First(field => field.gameObject.activeInHierarchy);
				var value = (RectTransform)input.transform;
				var label = input.transform.parent.Find("Label").GetComponent(DexcUGUIApi.TextType);
				Assert.That(value.rect.height, Is.EqualTo(height).Within(1f));
				Assert.That(Convert.ToString(DexcUGUIApi.Get(label, "text")), Does.Contain("含む"));
				Assert.That(
					((RectTransform)label.transform).rect.height,
					Is.GreaterThanOrEqualTo(Convert.ToSingle(DexcUGUIApi.Get(label, "preferredHeight")) - 1f)
				);
				Assert.That(
					DexcUGUIApi.Get((Component)DexcUGUIApi.Get(input, "textComponent"), "alignment"),
					Is.EqualTo(TextAnchor.MiddleLeft)
				);
				var before = value.rect;
				var position = value.position;
				var footer = DexcRuntimeUGUIRenderer.FooterScreenRectForTests;
				DexcUGUIApi.Set(input, "text", "1e-");
				yield return null;
				Assert.That(DexcUGUIApi.Get(input, "text"), Is.EqualTo("1e-"));
				Assert.That(value.rect, Is.EqualTo(before));
				Assert.That(value.position, Is.EqualTo(position));
				Assert.That(DexcRuntimeUGUIRenderer.FooterScreenRectForTests, Is.EqualTo(footer));
				// The message is written under the value, which keeps its place.
				Assert.That(input.transform.parent.Find("ErrorLine").gameObject.activeSelf, Is.True);
				Assert.That(input.transform.parent.Find("Error").gameObject.activeSelf, Is.False);
				Assert.That(DexcRuntimeUGUIRenderer.ExecuteInteractableForTests, Is.False);
				DexcUGUIApi.Set(input, "text", "100");
				yield return null;
				Assert.That(input.transform.parent.Find("ErrorLine").gameObject.activeSelf, Is.False);
				TearDown();
				yield return null;
			}
		}
#endif
	}

	[DebugCommandAttribute("Tests/UIR02/Vector", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcFormVectorCommand : DebugCommand
	{
		public InputVector3Int Position = new InputVector3Int(new Vector3Int(1, 2, 3));

		protected override void Execute() { }
	}

	[DebugCommandAttribute("Tests/UIR02/Form", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcFormControlCommand : DebugCommand
	{
		public InputInt 最大体力装備パッシブスキル一時的なバフによる補正をすべて含む = new InputInt(100);

		protected override void Execute() { }
	}
}
#endif
