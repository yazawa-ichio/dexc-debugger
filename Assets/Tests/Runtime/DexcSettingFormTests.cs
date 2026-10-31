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
	public sealed class DexcSettingFormTests
	{
		DexcRuntimeUISkin m_Skin;
		GameObject m_Camera;
		GameObject m_Events;

		[SetUp]
		public void SetUp()
		{
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.ResetForTests();
			DexcUIR04Settings.ThrowOnWrite = false;
			DexcUIR04Settings.ThrowOnRead = false;
			DexcUIR04Settings.Writes = 0;
			DexcUIR04Settings.ResetCurrent();
			DexcUIR04PresetSettings.ResetCurrent();
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
			var group = DexcRuntimeSettingForm.ReadGroups("UIR04/Gameplay").Single();
			DexcRuntimeSettingForm.Open(group.Items[0]);
		}

		[TearDown]
		public void TearDown()
		{
			DexcUIR04Settings.ThrowOnWrite = false;
			DexcUIR04Settings.ThrowOnRead = false;
			DebugMenu.Close();
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(false);
			DexcRuntimeUIToolkitRenderer.DestroyForTests();
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(false);
			DexcRuntimeUGUIRenderer.DestroyForTests();
#endif
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.ResetForTests();
			Object.DestroyImmediate(m_Skin);
			if (m_Camera != null)
				Object.DestroyImmediate(m_Camera);
			if (m_Events != null)
				Object.DestroyImmediate(m_Events);
		}

		static DexcRuntimeSettingForm.Field Field(string name) =>
			DexcRuntimeSettingForm.Fields.Single(field => field.Item.DisplayName == name);

		static DexcUIRow Row(System.Collections.Generic.List<DexcUIRow> rows, string name, string component = null) =>
			rows.Single(row => row.Key == DexcRuntimeSettingForm.Key(Field(name).Item.Id, component));

		/// <summary>The page is checked as rows, once, without any renderer: what each renderer draws comes from these.</summary>
		[Test]
		public void DetailRowsDescribeThePageAndDriveTheDraftsWithoutARenderer()
		{
			var rows = DexcRuntimeSettingForm.DetailRows(false);
			// The group is the header's place in the menu; the page starts with the preset its values match.
			Assert.That(
				DexcRuntimeUIRenderer.Breadcrumb(DexcRuntimeUIPage.SettingDetail),
				Is.EqualTo(new[] { "Setting", "UIR04", "Gameplay" })
			);
			Assert.That(rows[0].Text(), Is.EqualTo(DexcRuntimeSettingForm.MatchingPreset));

			var health = Row(rows, "Health");
			Assert.That(health.Kind, Is.EqualTo(DexcUIRowKind.Input));
			Assert.That(health.Label, Is.EqualTo(Field("Health").Label));
			Assert.That(health.Text(), Is.EqualTo("100"));
			Assert.That(health.IsEnabled, Is.True);
			Assert.That(Row(rows, "Read only").IsEnabled, Is.False);
			Assert.That(Row(rows, "Visible").Kind, Is.EqualTo(DexcUIRowKind.Choice));
			Assert.That(Row(rows, "Visible").Boolean, Is.True);
			Assert.That(Row(rows, "Weather").Boolean, Is.False);
			Assert.That(Row(rows, "Text").Auxiliary, Is.Not.Null);

			var wind = new[] { "x", "y", "z" }.Select(component => Row(rows, "Wind", component)).ToArray();
			Assert.That(wind.Select(row => row.Component), Is.EqualTo(new[] { "x", "y", "z" }));
			Assert.That(wind[0].Label, Is.EqualTo(Field("Wind").Label));
			Assert.That(wind[1].Label, Is.Empty, "the field label sits on the first component only");
			var speed = rows.IndexOf(Row(rows, "Speed"));
			Assert.That(rows[speed + 1].Kind, Is.EqualTo(DexcUIRowKind.Slider));

			health.Changed("abc");
			Assert.That(health.Text(), Is.EqualTo("abc"), "invalid text is kept as typed");
			Assert.That(health.ErrorText, Is.Not.Null.And.Not.Empty);
			health.Changed("150");
			Assert.That(health.ErrorText, Is.Null);
			Assert.That(DexcRuntimeSettingForm.Anchor.Dirty, Is.True);
			Assert.That(health.Confirmed("150"), Is.False, "a Submit setting waits for Apply");
			Assert.That(DexcUIR04Settings.Current.Health, Is.EqualTo(100));
			DexcRuntimeSettingForm.Apply();
			Assert.That(DexcUIR04Settings.Current.Health, Is.EqualTo(150));
			Assert.That(health.Text(), Is.EqualTo("150"));
		}

		[Test]
		public void ChoiceRowsStepInPlaceAndCarryTheirModeAsANote()
		{
			var rows = DexcRuntimeSettingForm.DetailRows(false);
			Assert.That(Row(rows, "Health").Note, Is.Null, "a Submit setting needs no note");
			Assert.That(Row(rows, "Weather").Note, Is.EqualTo("Immediate"));
			Assert.That(Row(rows, "Read only").Note, Is.EqualTo("Read only"));
			Assert.That(Row(rows, "Wind", "y").Note, Is.Null, "the note sits on the first component only");
			Assert.That(Row(rows, "Health").Cycle, Is.Null);
			Assert.That(Row(rows, "Wind", "x").Cycle, Is.Null);

			var weather = Row(rows, "Weather");
			Assert.That(weather.Cycle(1), Is.True);
			Assert.That(DexcUIR04Settings.Current.Weather, Is.EqualTo(DexcUIR04Weather.Rain), "an Immediate choice is written at once");
			Assert.That(weather.Cycle(-1), Is.True);
			Assert.That(weather.Cycle(-1), Is.True);
			var names = System.Enum.GetNames(typeof(DexcUIR04Weather));
			Assert.That(weather.Text(), Is.EqualTo(names[names.Length - 1]), "stepping back from the first value wraps");

			var visible = Row(rows, "Visible");
			Assert.That(visible.Cycle(1), Is.True);
			Assert.That(DexcUIR04Settings.Current.Visible, Is.False);
			Assert.That(visible.Cycle(-1), Is.True);
			Assert.That(DexcUIR04Settings.Current.Visible, Is.True);
		}

		[Test]
		public void SearchFindsWholeClassAndDefaultStateIgnoresUnappliedText()
		{
			var group = DexcRuntimeSettingForm.ReadGroups("UIR04/Gameplay/Health").Single();
			Assert.That(group.Label, Is.EqualTo("UIR04/Gameplay"));
			Assert.That(group.Items.Length, Is.EqualTo(8));
			DexcRuntimeSettingForm.Change(Field("Health"), "2000000000");
			Assert.That(DexcRuntimeSettingForm.MatchingPreset, Is.EqualTo("Initial values"));
			Assert.That(DexcUIR04Settings.Current.Health, Is.EqualTo(100));
		}

		[Test]
		public void QueryLimitDoesNotTruncateTheClass()
		{
			DebugMenu.Close();
			Dexc.DexcDebugger.ResetForTests();
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.Initialize(options =>
			{
				options.RuntimeUI.Skin = m_Skin;
				options.Query.MaxPageSize = 2;
			});
			DebugMenu.Open();
			var group = DexcRuntimeSettingForm.ReadGroups("UIR04/Gameplay").Single();
			Assert.That(group.Items.Length, Is.EqualTo(8));
			DexcRuntimeSettingForm.Open(group.Items[0]);
			Assert.That(DexcRuntimeSettingForm.Fields.Count, Is.EqualTo(8));
			Assert.That(DexcRuntimeSettingForm.Fields.Select(field => field.Item.Id).Distinct().Count(), Is.EqualTo(8));
		}

		[Test]
		public void DegenerateSliderRangesKeepTheExactNumberFieldWithoutInvalidGeometry()
		{
			var group = DexcRuntimeSettingForm.ReadGroups("UIR04/Presets").Single();
			DexcRuntimeSettingForm.Open(group.Items[0]);
			foreach (var name in new[] { "Fixed slider", "Infinite slider" })
			{
				var field = Field(name);
				Assert.That(field.CanSlide, Is.False);
				Assert.That(DexcRuntimeSettingForm.SliderValue(field), Is.Zero);
				DexcRuntimeSettingForm.ChangeSlider(field, .75f);
				Assert.That(field.Raw, Is.EqualTo("1"));
				Assert.That(field.Dirty, Is.False);
			}
			DexcRuntimeSettingForm.Change(Field("Infinite slider"), "2.5");
			DexcRuntimeSettingForm.Confirm(Field("Infinite slider"));
			Assert.That(DexcUIR04PresetSettings.Current.Infinite, Is.EqualTo(2.5));
		}

		[Test]
		public void InvalidFieldPreventsClassApplyAndImmediateDoesNotOverwriteOtherDrafts()
		{
			DexcRuntimeSettingForm.Change(Field("Health"), "-");
			DexcRuntimeSettingForm.Change(Field("Text"), "pending");
			Assert.That(DexcRuntimeSettingForm.CanApply, Is.False);
			DexcRuntimeSettingForm.Apply();
			Assert.That(DexcUIR04Settings.Writes, Is.Zero);
			DexcRuntimeSettingForm.Change(Field("Speed"), "2.5");
			Assert.That(DexcUIR04Settings.Current.Speed, Is.EqualTo(1f));
			Assert.That(DexcRuntimeSettingForm.Confirm(Field("Speed")), Is.True);
			Assert.That(DexcRuntimeSettingForm.Confirm(Field("Speed")), Is.False);
			Assert.That(DexcUIR04Settings.Current.Speed, Is.EqualTo(2.5f));
			Assert.That(Field("Health").Raw, Is.EqualTo("-"));
			Assert.That(Field("Text").Raw, Is.EqualTo("pending"));
			DexcRuntimeSettingForm.Change(Field("Health"), "300");
			DexcRuntimeSettingForm.Apply();
			Assert.That(DexcUIR04Settings.Current.Health, Is.EqualTo(300));
			Assert.That(DexcUIR04Settings.Current.Text, Is.EqualTo("pending"));
			Assert.That(DexcRuntimeSettingForm.Fields.Any(field => field.Dirty), Is.False);
		}

		/// <summary>Two classes under one parent get labels of their own: a favorite is saved by the label.</summary>
		[Test]
		public void ClassesUnderOneParentAreToldApart()
		{
			var labels = DexcRuntimeSettingForm.ReadGroups("ZZTests/Twin").Select(group => group.Label).ToArray();
			Assert.That(labels, Is.EqualTo(new[] { "ZZTests/Twin (First +1)", "ZZTests/Twin (Second)" }));
		}

		[Test]
		public void TextBeingTypedSurvivesTheRefreshWhileTheValueStaysTheSame()
		{
			// "1." on the way to "1.5" reads as the value already applied, so it is not pending, but it is still being typed.
			DexcRuntimeSettingForm.Change(Field("Speed"), "1.");
			Assert.That(Field("Speed").Dirty, Is.False);
			DexcRuntimeSettingForm.Refresh();
			Assert.That(Field("Speed").Raw, Is.EqualTo("1."));
			DexcRuntimeSettingForm.Change(Field("Speed"), "1.5");
			Assert.That(DexcRuntimeSettingForm.Confirm(Field("Speed")), Is.True);
			Assert.That(DexcUIR04Settings.Current.Speed, Is.EqualTo(1.5f));

			// A value changed from outside the form still shows.
			DebugSetting.Write(Field("Speed").Item.Id, 3f);
			DexcRuntimeSettingForm.Refresh();
			Assert.That(Field("Speed").Raw, Is.EqualTo("3"));
		}

		[Test]
		public void FailedWritePreservesErrorAndLaterFieldsApplyWithoutRollback()
		{
			DexcRuntimeSettingForm.Change(Field("Health"), "200");
			DexcRuntimeSettingForm.Change(Field("Text"), "later");
			DexcUIR04Settings.ThrowOnWrite = true;
			DexcRuntimeSettingForm.Apply();
			Assert.That(DexcUIR04Settings.Current.Health, Is.EqualTo(200), "Setter changed before throwing; no rollback is promised.");
			Assert.That(DexcUIR04Settings.Current.Text, Is.EqualTo("later"));
			Assert.That(Field("Health").Dirty, Is.True);
			Assert.That(Field("Health").Error, Does.Contain("Failed"));
			Assert.That(Field("Text").Dirty, Is.False);
			DexcRuntimeSettingForm.Refresh();
			Assert.That(Field("Health").Error, Is.Not.Empty);
			DexcUIR04Settings.ThrowOnWrite = false;
			DexcRuntimeSettingForm.Apply();
			Assert.That(Field("Health").Dirty, Is.False);
		}

		[Test]
		public void PresetsRequireApplyAndInitialValuesAreARealReset()
		{
			var group = DexcRuntimeSettingForm.ReadGroups("UIR04/Presets").Single();
			DexcRuntimeSettingForm.Open(group.Items[0]);
			DexcRuntimeSettingForm.Change(Field("Health"), "123");
			DexcRuntimeSettingForm.OpenPresets();
			Assert.That(Field("Health").Raw, Is.EqualTo("100"), "Preset entry discards pending input.");
			Assert.That(DexcRuntimeFormDetail.CanApply, Is.False);
			DexcRuntimeFormDetail.Select(DexcRuntimeFormDetail.ReadChoices().Items.Single(item => item.Label == "Fast"));
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.FormDetail));
			Assert.That(DexcUIR04PresetSettings.Current.Speed, Is.EqualTo(1));
			DexcRuntimeFormDetail.Apply();
			Assert.That(DexcUIR04PresetSettings.Current.Speed, Is.EqualTo(3));
			Assert.That(DexcRuntimeSettingForm.MatchingPreset, Is.EqualTo("Fast"));
			DexcRuntimeSettingForm.OpenPresets();
			DexcRuntimeFormDetail.Select(DexcRuntimeFormDetail.ReadChoices().Items[0]);
			DexcRuntimeFormDetail.Apply();
			Assert.That(DexcUIR04PresetSettings.Current.Speed, Is.EqualTo(1));
			Assert.That(DexcRuntimeSettingForm.MatchingPreset, Is.EqualTo("Initial values"));
		}

		[Test]
		public void ReadonlyPresetRejectionIsNotDisplayedAsApplied()
		{
			DexcRuntimeSettingForm.OpenPresets();
			DexcRuntimeFormDetail.Select(DexcRuntimeFormDetail.ReadChoices().Items.Single(item => item.Label == "Fast"));
			DexcRuntimeFormDetail.Apply();
			Assert.That(DexcRuntimeFormDetail.Message, Does.Contain("Rejected").And.Contain("read-only"));
			Assert.That(DexcRuntimeSettingForm.MatchingPreset, Is.EqualTo("Initial values"));
			Assert.That(DexcUIR04Settings.Current.Speed, Is.EqualTo(1));
			DexcRuntimeUIState.Back();
			DexcRuntimeSettingForm.Change(Field("Health"), "400");
			DexcRuntimeSettingForm.Apply();
			DexcRuntimeSettingForm.OpenPresets();
			DexcRuntimeFormDetail.Select(DexcRuntimeFormDetail.ReadChoices().Items[0]);
			DexcRuntimeFormDetail.Apply();
			Assert.That(DexcUIR04Settings.Current.Health, Is.EqualTo(100));
			Assert.That(DexcUIR04Settings.Current.ReadOnly, Is.EqualTo(10));
		}

		[Test]
		public void HelpersPreserveRawButBackCloseAndReplacedBindingsDiscardIt()
		{
			var field = Field("Health");
			var anchor = field.Item;
			var binding = new DexcRuntimeUIRenderer.SettingInputBinding(anchor);
			binding.Change("-");
			DexcRuntimeFormDetail.Open(field.Error);
			DexcRuntimeUIState.Back();
			Assert.That(field.Raw, Is.EqualTo("-"));
			binding.Change("999");
			Assert.That(field.Raw, Is.EqualTo("-"), "Old control binding was invalidated by navigation.");
			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeSettingForm.Fields.Count, Is.Zero);
			DexcRuntimeSettingForm.Open(anchor);
			Assert.That(Field("Health").Raw, Is.EqualTo("100"));
			DexcRuntimeSettingForm.Change(field, "999");
			Assert.That(Field("Health").Raw, Is.EqualTo("100"));
			DebugMenu.Close();
			Assert.That(DexcRuntimeSettingForm.Current, Is.Null);
		}

		[Test]
		public void VectorsChoicesSliderReadonlyAndReadFailureUseActualBackend()
		{
			DexcRuntimeSettingForm.Change(Field("Wind"), "-", "x");
			DexcRuntimeSettingForm.Change(Field("Wind"), "2.5", "y");
			Assert.That(Field("Wind").Value, Is.Null);
			DexcRuntimeSettingForm.Change(Field("Wind"), "3", "x");
			DexcRuntimeSettingForm.Apply();
			Assert.That(DexcUIR04Settings.Current.Wind, Is.EqualTo(new Vector3(3, 2.5f, 0)));
			DexcRuntimeSettingForm.Choose(Field("Visible"));
			Assert.That(DexcUIR04Settings.Current.Visible, Is.False);
			DexcRuntimeSettingForm.Choose(Field("Weather"));
			DexcRuntimeFormDetail.Select(DexcRuntimeFormDetail.ReadChoices().Items.Single(item => item.Label == "Rain"));
			Assert.That(DexcUIR04Settings.Current.Weather, Is.EqualTo(DexcUIR04Weather.Rain));
			DexcRuntimeSettingForm.ChangeSlider(Field("Speed"), .5f);
			Assert.That(DexcUIR04Settings.Current.Speed, Is.EqualTo(2f));
			DexcRuntimeSettingForm.Change(Field("Read only"), "4");
			Assert.That(Field("Read only").Raw, Is.EqualTo("10"));
			DexcUIR04Settings.ThrowOnRead = true;
			DexcRuntimeSettingForm.Refresh();
			Assert.That(DexcRuntimeSettingForm.MatchingPreset, Is.EqualTo("Undetermined"));
			Assert.That(Field("Health").Error, Is.Not.Empty);
		}

		[UnityTest]
		public IEnumerator UIToolkitFieldsApplyIndependently() => NativeFieldsApplyIndependently(true);

#if DEXC_HAS_UGUI
		[UnityTest]
		public IEnumerator UGUIFieldsApplyIndependently() => NativeFieldsApplyIndependently(false);
#endif

		IEnumerator NativeFieldsApplyIndependently(bool toolkit)
		{
#if !DEXC_HAS_UGUI
			if (!toolkit)
				Assert.Ignore("uGUI is unavailable.");
#endif
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(toolkit);
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(!toolkit);
			if (!toolkit)
				m_Events = new GameObject("UIR04 events", DexcUGUIApi.EventSystemType);
#endif
			var rect = new Rect(0, 0, Screen.width, Screen.height);
			if (toolkit)
				DexcRuntimeUIToolkitRenderer.RenderProfileForTests(Screen.width, Screen.height, rect, DexcRuntimeUIProfileKind.Windows);
#if DEXC_HAS_UGUI
			else
				DexcRuntimeUGUIRenderer.RenderProfileForTests(Screen.width, Screen.height, rect, DexcRuntimeUIProfileKind.Windows);
#endif
			for (var frame = 0; frame < 8; frame++)
				yield return null;
			if (toolkit)
			{
				var root = DexcRuntimeUIToolkitRenderer.SafeAreaRootForTests;
				var inputs = root.Query<TextField>().ToList();
				inputs.Single(input => input.label == "Health").value = "777";
				inputs.Single(input => input.label == "Text").value = "native";
				// The note sits under the label in the same text.
				var speed = inputs.Single(input => input.label.StartsWith("Speed\n", StringComparison.Ordinal));
				var focused = false;
				speed.RegisterCallback<FocusInEvent>(_ => focused = true);
				speed.RegisterCallback<FocusOutEvent>(_ => focused = false);
				speed.Focus();
				for (var frame = 0; frame < 3; frame++)
					yield return null;
				Assert.That(focused, Is.True, "Production field must receive FocusIn before editing.");
				speed.value = "2.5";
				yield return null;
				Assert.That(DexcUIR04Settings.Current.Speed, Is.EqualTo(1));
				speed.Blur();
				yield return null;
				Assert.That(DexcUIR04Settings.Current.Speed, Is.EqualTo(2.5f));
				Assert.That(Field("Health").Raw, Is.EqualTo("777"));
				root.Query<Slider>()
					.ToList()
					.Single(slider => slider.name == "RowSlider" && slider.resolvedStyle.display != DisplayStyle.None)
					.value = .75f;
				Assert.That(DexcUIR04Settings.Current.Speed, Is.EqualTo(3));
				DexcRuntimeUIState.SetSelectedIndex(DexcRuntimeUIToolkitRenderer.PrimaryActionIndexForTests);
				DexcRuntimeUIToolkitRenderer.ActivateSelectedForTests();
				Assert.That(root.Q<Button>("FooterSecondary").text, Is.EqualTo("Presets"));
			}
#if DEXC_HAS_UGUI
			else
			{
				var inputs = DexcRuntimeUGUIRenderer.RootForTests.GetComponentsInChildren(DexcUGUIApi.InputFieldType);
				foreach (var input in inputs)
				{
					var label = (string)DexcUGUIApi.Get(input.transform.parent.Find("Label").GetComponent(DexcUGUIApi.TextType), "text");
					if (label == "Health")
						DexcUGUIApi.Set(input, "text", "777");
					if (label == "Text")
						DexcUGUIApi.Set(input, "text", "native");
				}
				var speed = inputs.Single(input =>
					(string)DexcUGUIApi.Get(input.transform.parent.Find("Label").GetComponent(DexcUGUIApi.TextType), "text")
					== "Speed"
				);
				DexcUGUIApi.SetSelected(speed.gameObject);
				speed.GetType().GetMethod("ActivateInputField").Invoke(speed, null);
				for (var frame = 0; frame < 5; frame++)
					yield return null;
				DexcUGUIApi.Set(speed, "text", "2.5");
				Assert.That(DexcUIR04Settings.Current.Speed, Is.EqualTo(1));
				speed.GetType().GetMethod("DeactivateInputField").Invoke(speed, null);
				Assert.That(DexcUIR04Settings.Current.Speed, Is.EqualTo(2.5f));
				Assert.That(Field("Health").Raw, Is.EqualTo("777"));
				var slider = DexcRuntimeUGUIRenderer.RootForTests.GetComponentsInChildren(DexcUGUIApi.SliderType).Single();
				DexcUGUIApi.Set(slider, "value", .75f);
				Assert.That(DexcUIR04Settings.Current.Speed, Is.EqualTo(3));
				var apply = DexcRuntimeUGUIRenderer.SelectableForTests(DexcRuntimeUGUIRenderer.PrimaryActionIndexForTests);
				DexcUGUIApi.Get(apply, "onClick").GetType().GetMethod("Invoke").Invoke(DexcUGUIApi.Get(apply, "onClick"), null);
			}
#endif
			Assert.That(DexcUIR04Settings.Current.Health, Is.EqualTo(777));
			Assert.That(DexcUIR04Settings.Current.Text, Is.EqualTo("native"));
			Assert.That(DexcRuntimeSettingForm.Fields.Any(field => field.Dirty), Is.False);
		}

		[UnityTest]
		public IEnumerator SettingFlowCaptures()
		{
			if (Application.isBatchMode)
				Assert.Ignore("Requires automatic --graphics runner.");
			foreach (var renderer in new[] { "IMGUI", "UIToolkit", "UGUI" })
			{
#if !DEXC_HAS_UGUI
				if (renderer == "UGUI")
					continue;
#endif
				TearDown();
				DexcRuntimeBootstrap.InitializeForTests();
				SetUp();
				m_Camera = new GameObject("UIR04 camera");
				var camera = m_Camera.AddComponent<Camera>();
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = Color.black;
				camera.cullingMask = 0;
				DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(renderer == "UIToolkit");
#if DEXC_HAS_UGUI
				DexcRuntimeUIRendererSelection.SetUseUGUIForTests(renderer == "UGUI");
#endif
				foreach (var state in new[] { "edit", "error", "presets" })
				{
					if (state == "error")
						DexcRuntimeSettingForm.Change(Field("Health"), "-");
					if (state == "presets")
						DexcRuntimeSettingForm.OpenPresets();
					var safe = new Rect(0, 0, Screen.width, Screen.height);
					if (renderer == "UIToolkit")
						DexcRuntimeUIToolkitRenderer.RenderProfileForTests(
							Screen.width,
							Screen.height,
							safe,
							DexcRuntimeUIProfileKind.Windows
						);
#if DEXC_HAS_UGUI
					else if (renderer == "UGUI")
						DexcRuntimeUGUIRenderer.RenderProfileForTests(Screen.width, Screen.height, safe, DexcRuntimeUIProfileKind.Windows);
#endif
					else
						DexcRuntimeUIRenderer.SetProfileOverrideForTests(DexcRuntimeUIProfileKind.Windows);
					for (var frame = 0; frame < 8; frame++)
						yield return null;
					yield return new WaitForEndOfFrame();
					Assert.That(DexcRuntimeUIState.IsOpen && DexcRuntimeSettingForm.Current != null, Is.True);
					var texture = ScreenCapture.CaptureScreenshotAsTexture();
					try
					{
						var panel = DexcRuntimeUIReferenceResolver
							.Calculate(Screen.width, Screen.height, safe, DexcRuntimeUIProfileKind.Windows)
							.Panel;
						var pixels = texture.GetPixels(
							Mathf.CeilToInt(panel.xMin),
							Mathf.CeilToInt(panel.yMin),
							Mathf.FloorToInt(panel.width) - 1,
							Mathf.FloorToInt(panel.height) - 1
						);
						Assert.That(pixels.Count(pixel => pixel.r > .5f && pixel.g > .5f && pixel.b > .5f), Is.GreaterThan(50));
						Directory.CreateDirectory("Logs/UnityTests/UIR04");
						File.WriteAllBytes("Logs/UnityTests/UIR04/" + renderer + "-" + state + ".png", texture.EncodeToPNG());
					}
					finally
					{
						Object.DestroyImmediate(texture);
					}
				}
			}
		}
	}

	public sealed class DexcUIR04PresetSettings : DebugSettingSet<DexcUIR04PresetSettings>
	{
		[DebugSetting("UIR04/Presets/Health", ApplyMode = DebugSettingApplyMode.Submit)]
		public int Health { get; private set; } = 100;

		[DebugSetting("UIR04/Presets/Speed")]
		public float Speed { get; private set; } = 1;

		[DebugSetting("UIR04/Presets/Fixed slider"), DebugRange(1, 1), DebugSlider]
		public double Fixed { get; private set; } = 1;

		[DebugSetting("UIR04/Presets/Infinite slider"), DebugRange(double.NegativeInfinity, double.PositiveInfinity), DebugSlider]
		public double Infinite { get; private set; } = 1;

		[DebugPreset("uir04-valid-fast", "Fast")]
		public static DexcUIR04PresetSettings Fast => new DexcUIR04PresetSettings { Speed = 3 };
	}

	public enum DexcUIR04Weather
	{
		Clear,
		Rain,
	}

	public sealed class DexcTwinFirstSettings : DebugSettingSet<DexcTwinFirstSettings>
	{
		[DebugSetting("ZZTests/Twin/First")]
		public bool First { get; private set; }

		[DebugSetting("ZZTests/Twin/Third")]
		public bool Third { get; private set; }
	}

	public sealed class DexcTwinSecondSettings : DebugSettingSet<DexcTwinSecondSettings>
	{
		[DebugSetting("ZZTests/Twin/Second")]
		public bool Second { get; private set; }
	}

	public sealed class DexcUIR04Settings : DebugSettingSet<DexcUIR04Settings>
	{
		internal static bool ThrowOnWrite;
		internal static bool ThrowOnRead;
		internal static int Writes;
		int m_Health = 100;

		[DebugSetting("UIR04/Gameplay/Health", ApplyMode = DebugSettingApplyMode.Submit)]
		public int Health
		{
			get
			{
				if (ThrowOnRead)
					throw new InvalidOperationException("read failure");
				return m_Health;
			}
			private set
			{
				m_Health = value;
				if (ThrowOnWrite)
					throw new InvalidOperationException("write failure after mutation");
			}
		}

		[DebugSetting("UIR04/Gameplay/Speed"), DebugRange(0, 4), DebugSlider]
		public float Speed { get; private set; } = 1;

		[DebugSetting("UIR04/Gameplay/Text", ApplyMode = DebugSettingApplyMode.Submit)]
		public string Text { get; private set; } = "Sample";

		[DebugSetting("UIR04/Gameplay/Wind", ApplyMode = DebugSettingApplyMode.Submit)]
		public Vector3 Wind { get; private set; }

		[DebugSetting("UIR04/Gameplay/Visible")]
		public bool Visible { get; private set; } = true;

		[DebugSetting("UIR04/Gameplay/Weather")]
		public DexcUIR04Weather Weather { get; private set; }

		[DebugSetting("UIR04/Gameplay/Read only", Access = DebugSettingAccess.ReadOnly)]
		public int ReadOnly { get; private set; } = 10;

		[DebugSetting("UIR04/Gameplay/Maximum value", ApplyMode = DebugSettingApplyMode.Submit)]
		public long MaximumValue { get; private set; } = long.MaxValue;

		[DebugPreset("uir04-fast", "Fast")]
		public static DexcUIR04Settings Fast => new DexcUIR04Settings { Speed = 3 };

		protected override void OnChanged(DebugSettingChanges changes) => Writes++;
	}
}
#endif
