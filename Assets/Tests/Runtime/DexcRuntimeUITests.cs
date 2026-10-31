using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using DebuggerRuntime = Dexc.DexcDebugger;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DexcDebugger.Tests
{
#if DEXC_DEBUGGER
	[TestFixture]
	public sealed class DexcRuntimeUITests
	{
		[SetUp]
		public void SetUp()
		{
			RuntimeUI05DisabledExecuteCommand.Reject = false;
			RuntimeUI05DisabledExecuteCommand.ExecutionCount = 0;
			RuntimeScenarioInputEditor.RenderCount = 0;
			DexcRuntimeUIRenderer.ResetRenderCountersForTests();
			DexcRuntimeUIState.ResetForSession();
			DebuggerRuntime.ResetForTests();
			DexcRuntimeUIState.Close();
		}

		[TearDown]
		public void TearDown()
		{
			RuntimeUI05DisabledExecuteCommand.Reject = false;
			DexcRuntimeUIState.ResetForSession();
			DebuggerRuntime.ResetForTests();
		}

#if DEXC_DEBUGGER
		[Test]
		public void MenuBootstrapBuildsOnlyTheMenuAndIsIdempotent()
		{
			// Counted as a delta: earlier tests in this fixture may already have used Display or the Inspector.
			var display = Resources.FindObjectsOfTypeAll<DexcDisplayDriver>().Length;
			var inspector = Resources.FindObjectsOfTypeAll<DexcInspectorDriver>().Length;
			DexcRuntimeBootstrap.InitializeForTests();
			DexcRuntimeBootstrap.InitializeForTests();
			Assert.That(Resources.FindObjectsOfTypeAll<DexcRuntimeUIDriver>(), Has.Length.EqualTo(1), "repeat calls do not duplicate");
			// Display and Inspector build their own drivers when they are used, not when the menu is built.
			Assert.That(Resources.FindObjectsOfTypeAll<DexcDisplayDriver>(), Has.Length.EqualTo(display));
			Assert.That(Resources.FindObjectsOfTypeAll<DexcInspectorDriver>(), Has.Length.EqualTo(inspector));
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
		}

		[Test]
		public void WithoutASessionSettingAndClearingTheInputProviderDoNothing()
		{
			var provider = new TestInputProvider();
			DebugMenu.SetInputProvider(provider);
			Assert.That(DebugMenu.InputProvider, Is.Null);
			DebugMenu.ClearInputProvider(provider);
			Assert.That(provider.States, Is.Empty);
		}

		[Test]
		public void MenuOpenCloseAndToggleDoNotImplicitlyInitializeDebugger()
		{
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
			Assert.That(DebugMenu.IsOpen, Is.False);
			DebugMenu.Open();
			Assert.That(DebugMenu.IsOpen, Is.True);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
			DebugMenu.Toggle();
			Assert.That(DebugMenu.IsOpen, Is.False);
			DebugMenu.Toggle();
			Assert.That(DebugMenu.IsOpen, Is.True);
			DebugMenu.Close();
			Assert.That(DebugMenu.IsOpen, Is.False);
		}

		[Test]
		public void PlayerRendererCameraGateRejectsNonPlayerCameraTypesBeforeEnqueue()
		{
			foreach (var cameraType in Enum.GetValues(typeof(CameraType)).Cast<CameraType>())
			{
				var expected = cameraType == CameraType.Game;
				Assert.That(DexcPlayerRenderer.IsPlayerCameraTypeForTests(cameraType), Is.EqualTo(expected), cameraType.ToString());
			}
		}

		[Test]
		public void SessionChangeResetsNavigationSelectionAndDraftState()
		{
			DebuggerRuntime.Initialize();
			var first = DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 1)));
			DexcRuntimeUIState.SetSession(first.Summary.SessionId);
			DebugMenu.Open();
			DexcRuntimeUIState.Page = DexcRuntimeUIPage.SettingDetail;
			DexcRuntimeUIRenderer.SelectSettingForTests(
				DebugSetting.GetList(new DebugListQuery(string.Empty, "Tests/RuntimeUI/Quality", new DebugPageRequest(0, 4))).Items.Single()
			);
			DexcRuntimeUIRenderer.SetSettingEditTextForRenderer("9");
			DebuggerRuntime.Initialize();
			var second = DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 1)));
			DexcRuntimeUIState.SetSession(second.Summary.SessionId);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));
			Assert.That(DexcRuntimeSettingForm.Anchor, Is.Null);
			DebugMenu.Close();
		}

		[Test]
		public void MenuStateOwnsBoundedScrollPositionAndPageSize()
		{
			Assert.That(DexcRuntimeUIState.PageSize, Is.EqualTo(24));
			DexcRuntimeUIState.ScrollPosition = new UnityEngine.Vector2(3f, 27f);
			DexcRuntimeUIState.Open();
			Assert.That(DexcRuntimeUIState.ScrollPosition.y, Is.EqualTo(27f));
		}

		[Test]
		public void InputProviderIsPolledOncePerFrameAndFollowsMenuLifecycle()
		{
			DebuggerRuntime.Initialize();
			var provider = new TestInputProvider();
			DebugMenu.SetInputProvider(provider);
			DebugMenu.Open();
			Assert.That(provider.States, Is.EqualTo(new[] { DebugMenuInputState.Navigation }));
			DexcRuntimeUIState.PollInput();
			DexcRuntimeUIState.PollInput();
			Assert.That(provider.ReadCount, Is.EqualTo(1));
			DebugMenu.Close();
			Assert.That(provider.States.Last, Is.EqualTo(DebugMenuInputState.Closed));
			DebugMenu.ClearInputProvider(provider);
			Assert.That(DexcRuntimeUIState.InputProvider, Is.Null);
		}

		[Test]
		public void SessionReinitializationClosesAndUnregistersInputProvider()
		{
			DebuggerRuntime.Initialize();
			var provider = new TestInputProvider();
			DebugMenu.SetInputProvider(provider);
			DebuggerRuntime.Initialize();
			DexcRuntimeUIState.PollInput();
			Assert.That(provider.States, Does.Contain(DebugMenuInputState.Closed));
			Assert.That(DexcRuntimeUIState.InputProvider, Is.Null);
		}

		[Test]
		public void ProviderNavigationSelectsHomeFeatureAndSelectedCommandInsteadOfFirstRow()
		{
			DebuggerRuntime.Initialize();
			// No frame is drawn here, so the menu learns the session the way drawing the first page would.
			DexcRuntimeUIState.SetSession(DebuggerRuntime.CurrentSession.SessionId);
			var provider = new TestInputProvider();
			DebugMenu.SetInputProvider(provider);
			DebugMenu.Open();

			DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.down, false, false));
			DexcRuntimeUIRenderer.ProcessProviderInputForTests();
			Assert.That(DexcRuntimeUIState.SelectedIndex, Is.EqualTo(1));
			DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, true, false));
			DexcRuntimeUIRenderer.ProcessProviderInputForTests();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.SettingList));

			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
			// A search lists commands flat, in the registry's order, as the list did before folders.
			DexcRuntimeUIState.Search = "Tests/";
			var commands = DebugCommand.Registry.GetList(
				new DebugListQuery(string.Empty, "Tests/", new DebugPageRequest(0, DexcRuntimeUIState.PageSize))
			);
			Assert.That(commands.Items.Count, Is.GreaterThan(1));
			// The search field comes first; one step down is the first command.
			Assert.That(DexcRuntimeUIState.SelectedIndex, Is.Zero);
			DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.down, false, false));
			DexcRuntimeUIRenderer.ProcessProviderInputForTests();
			DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, true, false));
			DexcRuntimeUIRenderer.ProcessProviderInputForTests();
			Assert.That(DexcRuntimeUIState.Command.Path, Is.EqualTo(commands.Items[0].Path));
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
		}

		[Test]
		public void SideBySideOpensOnTheLastSectionAndTheGamepadGoesIntoTheSections()
		{
			DebugStorage.Storage = new DebugMemoryStorage();
			try
			{
				DebuggerRuntime.Initialize();
				DexcRuntimeUIState.SetSession(DebuggerRuntime.CurrentSession.SessionId);
				var provider = new TestInputProvider();
				DebugMenu.SetInputProvider(provider);
				DebugMenu.Open();
				// The renderer drawing the menu says when it is side by side.
				DexcRuntimeSplitHome.Shown = true;
				DexcRuntimeSplitHome.Apply();
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandList), "the first run opens on Command");
				Assert.That(DexcRuntimeUIRenderer.HasHeaderBack(DexcRuntimeUIPage.CommandList), Is.False, "the sections stand in for Home");

				// Cancel at a section's top goes into the sections, on that section; down and Submit open the next one.
				DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, false, true));
				DexcRuntimeUIRenderer.ProcessProviderInputForTests();
				Assert.That(DexcRuntimeSplitHome.SectionFocus, Is.Zero);
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandList));
				DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.down, false, false));
				DexcRuntimeUIRenderer.ProcessProviderInputForTests();
				Assert.That(DexcRuntimeSplitHome.SectionFocus, Is.EqualTo(1));
				DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, true, false));
				DexcRuntimeUIRenderer.ProcessProviderInputForTests();
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.SettingList));
				Assert.That(DexcRuntimeSplitHome.SectionFocus, Is.EqualTo(-1));

				// Left at a section's top goes into the sections too; Cancel there closes the menu, as it does on Home.
				DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.left, false, false));
				DexcRuntimeUIRenderer.ProcessProviderInputForTests();
				Assert.That(DexcRuntimeSplitHome.SectionFocus, Is.EqualTo(1));
				DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, false, true));
				DexcRuntimeUIRenderer.ProcessProviderInputForTests();
				Assert.That(DebugMenu.IsOpen, Is.False);

				// The section is stored for the next run and opened again; a registered home panel comes first.
				Assert.That(DebugStorage.TryLoad("UI/LastSection", out var stored), Is.True);
				Assert.That(stored, Is.EqualTo("home:SettingList"));
				DebugMenu.Open();
				DexcRuntimeSplitHome.Shown = true;
				DexcRuntimeSplitHome.Apply();
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.SettingList));
				var home = new DebugPanel("Home").Label("Start here");
				DebugPanels.Register(home);
				DebugPanels.Home = home;
				DebugMenu.Close();
				DebugMenu.Open();
				DexcRuntimeSplitHome.Apply();
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.PanelDetail));
				Assert.That(DexcRuntimePanelForm.Current, Is.SameAs(home));
				DebugMenu.ClearInputProvider(provider);
			}
			finally
			{
				DebugPanels.ResetForTests();
				DebugStorage.ResetForTests();
			}
		}

		[UnityTest]
		public IEnumerator DisabledIMGUIExecuteIsAbsentFromProductionProviderNavigationAndSubmitPath()
		{
			DebuggerRuntime.Initialize();
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(false);
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(false);
#endif
			RuntimeUI05DisabledExecuteCommand.Reject = true;
			var provider = new TestInputProvider();
			DebugMenu.SetInputProvider(provider);
			DebugMenu.Open();
			var command = DebugCommand
				.Registry.GetList(new DebugListQuery(string.Empty, "UI05/DisabledExecute", new DebugPageRequest(0, 4)))
				.Items.Single();
			DexcRuntimeUIRenderer.SelectCommandForTests(command);
			DexcRuntimeUIState.Notice = string.Empty;

			Assert.That(DebugCommand.Draft.Validate(DexcRuntimeUIState.DraftId).TryGetValidation(out var validation), Is.True);
			Assert.That(validation.CanExecute, Is.False);
			// The page's rows come first; after them the footer offers History alone, Execute being disabled, and the star comes last.
			var rows = DexcRuntimeUIRenderer.DetailRowCountForTests;
			Assert.That(DexcRuntimeUIRenderer.ProviderSelectionCount(), Is.EqualTo(rows + 2));
			Assert.That(DexcRuntimeUIRenderer.IsNavigationFocusedForTests(0), Is.True);
			// Down from the last row lands on History; the disabled Execute is skipped.
			DexcRuntimeUIState.SetSelectedIndex(rows - 1);

			provider.Enqueue(new DebugMenuInputFrame(Vector2.down, false, false));
			yield return null;
			DexcRuntimeUIState.PollInput();
			DexcRuntimeUIRenderer.ProcessProviderInputForTests();
			provider.Enqueue(new DebugMenuInputFrame(Vector2.zero, true, false));
			yield return null;
			DexcRuntimeUIState.PollInput();
			DexcRuntimeUIRenderer.ProcessProviderInputForTests();

			Assert.That(provider.NonZeroFrameCount, Is.GreaterThanOrEqualTo(2));
			Assert.That(
				DexcRuntimeUIState.Page,
				Is.EqualTo(DexcRuntimeUIPage.FormDetail),
				"The only selectable footer operation is History."
			);
			Assert.That(DexcRuntimeFormDetail.Title, Is.EqualTo("Execution history"));
			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
			Assert.That(DexcRuntimeUIRenderer.ProviderSelectionCount(), Is.EqualTo(rows + 2));
			Assert.That(DexcRuntimeUIRenderer.IsNavigationFocusedForTests(rows), Is.True);
			Assert.That(RuntimeUI05DisabledExecuteCommand.ExecutionCount, Is.Zero);
			Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Null);
			Assert.That(DexcRuntimeUIState.Notice, Is.Empty);
		}

		[Test]
		public void NavigationDiscardsSettingInputsAndCloseDiscardsCommandDraft()
		{
			DebuggerRuntime.Initialize();
			DebugMenu.Open();

			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.SettingList);
			var setting = DebugSetting
				.GetList(new DebugListQuery(string.Empty, "Tests/RuntimeUI/Quality", new DebugPageRequest(0, 4)))
				.Items.Single();
			DexcRuntimeUIRenderer.SelectSettingForTests(setting);
			DexcRuntimeUIRenderer.SetSettingEditTextForRenderer("9");
			DexcRuntimeUIState.Back();
			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));

			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
			var command = DebugCommand
				.Registry.GetList(new DebugListQuery(string.Empty, "Tests/RuntimeUI/EnemyCommand", new DebugPageRequest(0, 4)))
				.Items.Single();
			DexcRuntimeUIRenderer.SelectCommandForTests(command);
			var field = DexcRuntimeUIState.Command.Fields.Single(item => item.IsInput);
			Assert.That(
				DebugCommand.Draft.UpdateInput(DexcRuntimeUIState.DraftId, field.Id, DebugValue.From(5)).Status,
				Is.EqualTo(DebugCommandDraftOperationStatus.Succeeded)
			);
			var draft = DexcRuntimeUIState.DraftId;
			DexcRuntimeUIState.Back();
			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));
			Assert.That(DexcRuntimeUIState.HasDraft, Is.True);
			Assert.That(DebugCommand.Draft.Validate(draft).Status, Is.EqualTo(DebugCommandDraftOperationStatus.Succeeded));
			Assert.That(DexcCommandRuntime.Drafts.GetInputValue(draft, field.Id), Is.EqualTo(DebugValue.From(5)));

			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandDetail);
			DexcRuntimeUIRenderer.ExecuteCommandForTests();
			Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Not.Null);
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.History);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.History));
			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));
			Assert.That(DexcRuntimeUIState.HasDraft, Is.True);

			DebugMenu.Close();
			Assert.That(DexcRuntimeUIState.IsOpen, Is.False);
			Assert.That(DexcRuntimeUIState.HasDraft, Is.False);
			Assert.That(DebugCommand.Draft.Validate(draft).Status, Is.EqualTo(DebugCommandDraftOperationStatus.Stale));
		}

		[Test]
		public void InputProviderFocusMappingReportsTextInputAndNavigationStates()
		{
			DebuggerRuntime.Initialize();
			DebugMenu.Open();
			var provider = new TestInputProvider();
			DexcRuntimeUIState.SetInputProviderForTests(provider, "DexcSettingValue");
			Assert.That(provider.States.Last(), Is.EqualTo(DebugMenuInputState.TextInput));
			Assert.That(DexcRuntimeUIState.StateForFocusName(string.Empty), Is.EqualTo(DebugMenuInputState.Navigation));
		}

		[Test]
		public void RuntimeScenario_EnemyCommand_SelectorUpdatesDraftThroughRuntimeRenderer()
		{
			DebuggerRuntime.Initialize();
			var commands = DebugCommand.Registry.GetList(
				new DebugListQuery("", "Tests/RuntimeUI/EnemyCommand", new DebugPageRequest(0, 4))
			);
			var item = commands.Items.Single();
			DexcRuntimeUIRenderer.SelectCommandForTests(item);
			var field = DexcRuntimeUIState.Command.Fields.Single(value => value.IsInput);
			DexcRuntimeUIState.InputEditorProviderTypes = new[] { typeof(RuntimeScenarioInputEditor) };
			Assert.That(DexcRuntimeUIRenderer.DrawRuntimeInputEditorForTests(field), Is.True);
			Assert.That(DexcCommandRuntime.Drafts.GetInputValue(DexcRuntimeUIState.DraftId, field.Id), Is.EqualTo(DebugValue.From(42)));
			Assert.That(RuntimeScenarioInputEditor.RenderCount, Is.EqualTo(1));
		}

		[Test]
		public void RuntimeSettingAndInspectorEditorsFollowSchemaKinds()
		{
			DebuggerRuntime.Initialize();
			var settings = DebugSetting.GetList(new DebugListQuery("Tests/RuntimeUI/Schema", string.Empty, new DebugPageRequest(0, 16)));
			Assert.That(
				DexcRuntimeUIRenderer.GetSettingEditorKindForTests(settings.Items.Single(item => item.Path.EndsWith("Enabled"))),
				Is.EqualTo(DexcRuntimeUIRenderer.RuntimeValueEditorKind.Toggle)
			);
			Assert.That(
				DexcRuntimeUIRenderer.GetSettingEditorKindForTests(settings.Items.Single(item => item.Path.EndsWith("Mode"))),
				Is.EqualTo(DexcRuntimeUIRenderer.RuntimeValueEditorKind.Enum)
			);
			Assert.That(
				DexcRuntimeUIRenderer.GetSettingEditorKindForTests(settings.Items.Single(item => item.Path.EndsWith("Speed"))),
				Is.EqualTo(DexcRuntimeUIRenderer.RuntimeValueEditorKind.Slider)
			);
			Assert.That(
				DexcRuntimeUIRenderer.GetSettingEditorKindForTests(settings.Items.Single(item => item.Path.EndsWith("Tint"))),
				Is.EqualTo(DexcRuntimeUIRenderer.RuntimeValueEditorKind.Color)
			);
			Assert.That(
				DexcRuntimeUIRenderer.GetSettingEditorKindForTests(settings.Items.Single(item => item.Path.EndsWith("Direction"))),
				Is.EqualTo(DexcRuntimeUIRenderer.RuntimeValueEditorKind.Vector)
			);

			var gameObject = new GameObject("RuntimeUISchemaInspectable");
			try
			{
				gameObject.AddComponent<RuntimeScenarioInspectable>();
				var scene = DebugInspector.GetScenes().Items.Single(item => item.Name == SceneManager.GetActiveScene().name);
				var hierarchy = DebugInspector.GetHierarchyChildren(
					new DebugHierarchyChildrenQuery(scene.Scene, default(DebugObjectHandle), new DebugPageRequest(0, 64))
				);
				var target = hierarchy.Entries.Single(item => item.Label == gameObject.name).InspectorTarget;
				var component = DebugInspector
					.GetComponents(target)
					.Entries.Single(item => item.TypeName.Contains(nameof(RuntimeScenarioInspectable)))
					.Component;
				var fields = component.GetFields(new DebugPageRequest(0, 32));
				var values = component.ReadFields(fields.Fields.Select(item => item.Id).ToList());
				foreach (var field in fields.Fields)
				{
					var value = values.Value.Single(item => item.Field == field.Id).Snapshot.Value;
					var expected =
						field.Name == nameof(RuntimeScenarioInspectable.Enabled) ? DexcRuntimeUIRenderer.RuntimeValueEditorKind.Toggle
						: field.Name == nameof(RuntimeScenarioInspectable.Mode) ? DexcRuntimeUIRenderer.RuntimeValueEditorKind.Enum
						: field.Name == nameof(RuntimeScenarioInspectable.Tint) ? DexcRuntimeUIRenderer.RuntimeValueEditorKind.Color
						: field.Name == nameof(RuntimeScenarioInspectable.Direction) ? DexcRuntimeUIRenderer.RuntimeValueEditorKind.Vector
						: DexcRuntimeUIRenderer.RuntimeValueEditorKind.Number;
					Assert.That(DexcRuntimeUIRenderer.GetInspectorEditorKindForTests(field, value), Is.EqualTo(expected), field.Name);
				}
			}
			finally
			{
				Object.DestroyImmediate(gameObject);
			}
		}

		[UnityTest]
		public IEnumerator RuntimeScenario_SettingPreset_UsesPreviewThenExplicitApply()
		{
			DebuggerRuntime.Initialize();
			var settings = DebugSetting.GetList(new DebugListQuery("", "Tests/RuntimeUI/Quality", new DebugPageRequest(0, 4)));
			var setting = settings.Items.Single();
			DexcRuntimeUIRenderer.SelectSettingForTests(setting);
			var presets = DebugSetting.GetPresets(setting.SetId, new DebugPageRequest(0, 4));
			var preset = presets.Items.Single(item => item.DisplayName == "High");
			var preview = DebugSetting.PreviewPreset(preset.Id);
			Assert.That(
				preview.Status,
				Is.EqualTo(DebugOperationStatus.Succeeded),
				string.Join("|", preview.Issues.Select(issue => issue.Code + ":" + issue.Message))
			);
			Assert.That(preview.Value, Is.Not.Empty);
			DexcRuntimeUIRenderer.ApplyPresetForTests(preset);
			Assert.That(DebugSetting.Read(setting.Id).TryGetValue(out var value), Is.True);
			Assert.That(value, Is.EqualTo(DebugValue.From(9)));
			Assert.That(DexcRuntimeSettingForm.Anchor.Dirty, Is.False);
			DebugSetting.Reset(setting.Id);
			yield return null;
		}

		[Test]
		public void RuntimeScenario_SceneInspector_ProviderNavigationReachesComponentFields()
		{
			DebuggerRuntime.Initialize();
			DebugMenu.Open();
			var gameObject = new GameObject("RuntimeUIInspectorScenario");
			try
			{
				var component = gameObject.AddComponent<RuntimeScenarioInspectable>();
				var scene = DebugInspector.GetScenes().Items.Single(item => item.Name == SceneManager.GetActiveScene().name);
				var hierarchy = DebugInspector.GetHierarchyChildren(
					new DebugHierarchyChildrenQuery(scene.Scene, default(DebugObjectHandle), new DebugPageRequest(0, 64))
				);
				var hierarchyIndex = hierarchy.Entries.ToList().FindIndex(item => item.Label == gameObject.name);
				Assert.That(hierarchyIndex, Is.GreaterThanOrEqualTo(0), string.Join("|", hierarchy.Entries.Select(item => item.Path)));
				DexcRuntimeUIState.InspectorScene = scene.Scene;
				DexcRuntimeUIState.InspectorParent = default(DebugObjectHandle);
				DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorScene);
				DexcRuntimeUIState.SetSelectedIndex(
					DexcRuntimeInspectorNavigation.Rows().ToList().FindIndex(row => row.Label == gameObject.name)
				);
				DexcRuntimeUIState.InputState = DebugMenuInputState.Navigation;
				DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, true, false));
				DexcRuntimeUIRenderer.ProcessProviderInputForTests();
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.InspectorComponents));

				var components = DebugInspector.GetComponents(DexcRuntimeUIState.InspectorTarget);
				var componentIndex = components
					.Entries.ToList()
					.FindIndex(item => item.TypeName.Contains(nameof(RuntimeScenarioInspectable)));
				Assert.That(componentIndex, Is.GreaterThanOrEqualTo(0), string.Join("|", components.Entries.Select(item => item.TypeName)));
				DexcRuntimeUIState.SetSelectedIndex(componentIndex);
				DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, true, false));
				DexcRuntimeUIRenderer.ProcessProviderInputForTests();
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.InspectorFields));
				var fields = DexcRuntimeUIState.InspectorComponent.GetFields(new DebugPageRequest(0, 64));
				Assert.That(fields.Fields.Any(item => item.DisplayName == nameof(RuntimeScenarioInspectable.Health)), Is.True);
				Assert.That(component.Health, Is.EqualTo(7));
			}
			finally
			{
				Object.DestroyImmediate(gameObject);
			}
		}

		[Test]
		public void RuntimeScenario_SceneInspector_StaleTargetReturnsToSceneListWithReason()
		{
			DebuggerRuntime.Initialize();
			DebugMenu.Open();
			var gameObject = new GameObject("RuntimeUIStaleInspectorScenario");
			try
			{
				var scene = DebugInspector.GetScenes().Items.Single(item => item.Name == SceneManager.GetActiveScene().name);
				var hierarchy = DebugInspector.GetHierarchyChildren(
					new DebugHierarchyChildrenQuery(scene.Scene, default(DebugObjectHandle), new DebugPageRequest(0, 64))
				);
				var hierarchyIndex = hierarchy.Entries.ToList().FindIndex(item => item.Label == gameObject.name);
				Assert.That(hierarchyIndex, Is.GreaterThanOrEqualTo(0), string.Join("|", hierarchy.Entries.Select(item => item.Path)));
				DexcRuntimeUIState.InspectorScene = scene.Scene;
				DexcRuntimeUIState.InspectorParent = default(DebugObjectHandle);
				DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorScene);
				DexcRuntimeUIState.SetSelectedIndex(
					DexcRuntimeInspectorNavigation.Rows().ToList().FindIndex(row => row.Label == gameObject.name)
				);
				DexcRuntimeUIState.InputState = DebugMenuInputState.Navigation;
				DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, true, false));
				DexcRuntimeUIRenderer.ProcessProviderInputForTests();
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.InspectorComponents));

				Object.DestroyImmediate(gameObject);
				DexcRuntimeUIRenderer.RefreshInspectorDetailForTests();

				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.InspectorScene));
				Assert.That(DexcRuntimeUIState.InspectorTarget.IsValid, Is.False);
				Assert.That(DexcRuntimeUIState.InspectorParent.IsValid, Is.False);
				Assert.That(DexcRuntimeUIState.Notice, Does.Contain("no longer available"));
				var refreshed = DebugInspector.GetHierarchyChildren(
					new DebugHierarchyChildrenQuery(scene.Scene, default(DebugObjectHandle), new DebugPageRequest(0, 64))
				);
				Assert.That(refreshed.Entries.Any(item => item.Label == "RuntimeUIStaleInspectorScenario"), Is.False);
			}
			finally
			{
				if (gameObject != null)
					Object.DestroyImmediate(gameObject);
			}
		}

		[Test]
		public void RuntimeScenario_SceneInspector_ZeroFieldComponentRemainsAndStaleFieldPageReturns()
		{
			DebuggerRuntime.Initialize();
			DebugMenu.Open();
			var gameObject = new GameObject("RuntimeUIZeroFieldInspectorScenario");
			try
			{
				gameObject.AddComponent<RuntimeScenarioEmptyInspectable>();
				var scene = DebugInspector.GetScenes().Items.Single(item => item.Name == SceneManager.GetActiveScene().name);
				var hierarchy = DebugInspector.GetHierarchyChildren(
					new DebugHierarchyChildrenQuery(scene.Scene, default(DebugObjectHandle), new DebugPageRequest(0, 64))
				);
				var hierarchyIndex = hierarchy.Entries.ToList().FindIndex(item => item.Label == gameObject.name);
				Assert.That(hierarchyIndex, Is.GreaterThanOrEqualTo(0), string.Join("|", hierarchy.Entries.Select(item => item.Path)));
				DexcRuntimeUIState.InspectorScene = scene.Scene;
				DexcRuntimeUIState.InspectorParent = default(DebugObjectHandle);
				DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorScene);
				DexcRuntimeUIState.SetSelectedIndex(
					DexcRuntimeInspectorNavigation.Rows().ToList().FindIndex(row => row.Label == gameObject.name)
				);
				DexcRuntimeUIState.InputState = DebugMenuInputState.Navigation;
				DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, true, false));
				DexcRuntimeUIRenderer.ProcessProviderInputForTests();
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.InspectorComponents));

				var components = DebugInspector.GetComponents(DexcRuntimeUIState.InspectorTarget);
				var componentIndex = components
					.Entries.ToList()
					.FindIndex(item => item.TypeName.Contains(nameof(RuntimeScenarioEmptyInspectable)));
				Assert.That(componentIndex, Is.GreaterThanOrEqualTo(0), string.Join("|", components.Entries.Select(item => item.TypeName)));
				DexcRuntimeUIState.SetSelectedIndex(componentIndex);
				DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, true, false));
				DexcRuntimeUIRenderer.ProcessProviderInputForTests();
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.InspectorFields));
				Assert.That(DexcRuntimeUIState.InspectorComponent.GetFields(new DebugPageRequest(0, 64)).Fields, Is.Empty);

				DexcRuntimeUIRenderer.RefreshInspectorDetailForTests();
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.InspectorFields));

				Object.DestroyImmediate(gameObject);
				DexcRuntimeUIRenderer.RefreshInspectorDetailForTests();
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.InspectorScene));
				Assert.That(DexcRuntimeUIState.InspectorComponent.IsValid, Is.False);
				Assert.That(DexcRuntimeUIState.Notice, Does.Contain("no longer available"));
			}
			finally
			{
				if (gameObject != null)
					Object.DestroyImmediate(gameObject);
			}
		}

		[Test]
		public void RuntimeScenario_FpsAndWorldDisplay_UsesCompletedFrameForOverlayAndWorldRenderer()
		{
			DebuggerRuntime.Initialize();
			var owner = new GameObject("RuntimeUIDisplayScenario");
			try
			{
				var subject = DebugDisplay.CreateSubject("Enemy", owner);
				DebugDisplay.DrawLabel(DebugDrawOptions.Frame("FPS", Color.white), "60");
				DebugDisplay.DrawLine("Line", Vector3.zero, Vector3.right, Color.white);
				DebugDisplay.DrawRay("Ray", Vector3.zero, Vector3.up, Color.white);
				DebugDisplay.DrawPolyLine("Polyline", new[] { Vector3.zero, Vector3.right, Vector3.up }, Color.white);
				DebugDisplay.DrawWireSphere("Sphere", Vector3.zero, 1f, Color.white);
				DebugDisplay.DrawWireCube("Cube", Vector3.zero, Vector3.one, Quaternion.Euler(0f, 30f, 0f), Color.white);
				DebugDisplay.DrawWireDisc("Disc", Vector3.zero, Vector3.up, 1f, Color.white);
				subject.DrawRelation(DebugDrawOptions.Frame("Relation", Color.white), subject);
				DebuggerRuntime.CurrentSession.Display.Tick();
				var frame = DexcRuntimeUIRenderer.ReadDisplayFrameForTests();
				Assert.That(frame, Is.Not.Null);
				Assert.That(frame.Globals.Any(item => item.Kind == DebugDisplayPrimitiveKind.Label), Is.True);
				Assert.That(frame.Globals.Select(item => item.Kind), Does.Contain(DebugDisplayPrimitiveKind.WireDisc));
				DexcRuntimeUIRenderer.ResetRenderCountersForTests();
				Assert.That(DexcRuntimeUIRenderer.RenderDisplayOverlayForTests(), Is.GreaterThan(0));
				var renderedKinds = DexcRuntimeUIRenderer.RenderWorldForTests();
				Assert.That(renderedKinds, Does.Contain(DebugDisplayPrimitiveKind.Line));
				Assert.That(renderedKinds, Does.Contain(DebugDisplayPrimitiveKind.Ray));
				Assert.That(renderedKinds, Does.Contain(DebugDisplayPrimitiveKind.PolyLine));
				Assert.That(renderedKinds, Does.Contain(DebugDisplayPrimitiveKind.WireSphere));
				Assert.That(renderedKinds, Does.Contain(DebugDisplayPrimitiveKind.WireCube));
				Assert.That(renderedKinds, Does.Contain(DebugDisplayPrimitiveKind.WireDisc));
				Assert.That(renderedKinds, Does.Contain(DebugDisplayPrimitiveKind.Relation));
				Assert.That(DexcRuntimeUIRenderer.WorldVisualDrawCountForTests, Is.EqualTo(renderedKinds.Count));
			}
			finally
			{
				Object.Destroy(owner);
			}
		}

		[UnityTest]
		public IEnumerator PlayerRendererVisualCaptureWritesPlayerCameraArtifactForOptInRunner()
		{
			if (!Environment.GetCommandLineArgs().Any(item => item == "--dexc-visual-capture"))
				yield break;

			var visualSkin = ScriptableObject.CreateInstance<DexcRuntimeUISkin>();
#if UNITY_EDITOR
			visualSkin.SetWorldVisualShaderForEditor(
				AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader")
			);
#endif
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = visualSkin);
			DexcPlayerRenderer.EnsureInitialized();
			const int width = 320;
			const int height = 180;
			var background = new Color32(16, 20, 28, 255);
			var owner = new GameObject("I15PlayerVisualOwner");
			var cameraObject = new GameObject("I15PlayerVisualCamera");
			var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
			var previousActive = RenderTexture.active;
			try
			{
				var camera = cameraObject.AddComponent<Camera>();
				camera.enabled = false;
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = background;
				camera.orthographic = true;
				camera.orthographicSize = 4f;
				camera.aspect = (float)width / height;
				camera.transform.position = new Vector3(0f, 0f, -10f);
				camera.transform.rotation = Quaternion.identity;
				camera.targetTexture = target;
				camera.GetUniversalAdditionalCameraData();
				target.Create();

				DebugDisplay.CreateSubject("PlayerVisual", owner);
				DebugDisplay.DrawLine("PlayerLine", new Vector3(-1.6f, -0.7f, 0f), new Vector3(1.6f, -0.7f, 0f), Color.green);
				DebugDisplay.DrawWireSphere("PlayerWireSphere", new Vector3(0f, 0.7f, 0f), 0.65f, Color.cyan);
				DebugDisplay.DrawWireCube(
					"PlayerWireCube",
					new Vector3(0f, 0f, 0f),
					new Vector3(1.2f, 0.8f, 0.8f),
					Quaternion.Euler(0f, 25f, 0f),
					Color.yellow
				);
				DebuggerRuntime.CurrentSession.Display.Tick();
				Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
				Assert.That(frame, Is.Not.Null);
				Assert.That(frame.Globals.Select(visual => visual.Kind), Does.Contain(DebugDisplayPrimitiveKind.Line));
				Assert.That(frame.Globals.Select(visual => visual.Kind), Does.Contain(DebugDisplayPrimitiveKind.WireSphere));
				Assert.That(frame.Globals.Select(visual => visual.Kind), Does.Contain(DebugDisplayPrimitiveKind.WireCube));

				var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
				RenderPipeline.SubmitRenderRequest(camera, request);
				// The pass exists once the Player camera enqueued it, and its mesh once it ran.
				var pass = DexcPlayerRenderer.Data.Pass;
				Assert.That(pass, Is.Not.Null, "DexcPlayerRenderer did not enqueue its URP pass.");
				Assert.That(pass.m_Mesh, Is.Not.Null, "URP DexcPlayerRenderPass was not executed for the Player camera.");
				Assert.That(pass.m_Mesh.vertexCount, Is.GreaterThan(0), "URP DexcPlayerRenderPass produced no Player geometry.");
				yield return null;
				RenderTexture.active = target;
				var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
				try
				{
					texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
					texture.Apply(false, false);
					var pixels = texture.GetPixels32();
					var nonBackgroundPixels = pixels.Count(pixel =>
						Math.Abs(pixel.r - background.r) > 2 || Math.Abs(pixel.g - background.g) > 2 || Math.Abs(pixel.b - background.b) > 2
					);
					Assert.That(nonBackgroundPixels, Is.GreaterThan(0));
					var bytes = texture.EncodeToPNG();
					var manifestPath = Environment.GetEnvironmentVariable("DEXC_VISUAL_CAPTURE_MANIFEST");
					Assert.That(manifestPath, Is.Not.Null.And.Not.Empty);
					var outputDirectory = Path.GetDirectoryName(manifestPath);
					Directory.CreateDirectory(outputDirectory);
					var stamp =
						Environment.GetEnvironmentVariable("DEXC_VISUAL_CAPTURE_STAMP") ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
					var playerPath = Path.Combine(outputDirectory, "I15-Player-" + stamp + ".png");
					File.WriteAllBytes(playerPath, bytes);
					var kinds = frame
						.Globals.Concat(frame.Subjects.SelectMany(item => item.Visuals))
						.Select(item => item.Kind.ToString())
						.ToArray();
					var manifest = new PlayerVisualCaptureManifest
					{
						Frame = frame.Frame,
						PlayerFrame = frame.Frame,
						PlayerPath = Path.GetFullPath(playerPath),
						PlayerSha256 = Sha256(bytes),
						PlayerSource = "UnityEngine.Camera.targetTexture.ReadPixels after URP DexcPlayerRenderPass",
						PlayerRenderer = "DexcPlayerRenderer.EnqueuePass -> DexcPlayerRenderPass",
						PlayerCameraType = camera.cameraType.ToString(),
						PlayerWidth = width,
						PlayerHeight = height,
						PlayerNonBackgroundPixels = nonBackgroundPixels,
						PlayerKinds = kinds,
					};
					File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
					Assert.That(File.Exists(playerPath), Is.True);
					Assert.That(File.Exists(manifestPath), Is.True);
				}
				finally
				{
					Object.DestroyImmediate(texture);
				}
			}
			finally
			{
				RenderTexture.active = previousActive;
				if (cameraObject != null)
				{
					var camera = cameraObject.GetComponent<Camera>();
					if (camera != null)
						camera.targetTexture = null;
				}
				if (target != null)
				{
					target.Release();
					Object.DestroyImmediate(target);
				}
				Object.DestroyImmediate(cameraObject);
				Object.DestroyImmediate(owner);
				Object.DestroyImmediate(visualSkin);
			}
		}

		[UnityTest]
		public IEnumerator RuntimeIMGUIVisualCaptureWritesThreeResponsiveProfilesForOptInRunner()
		{
			if (!Environment.GetCommandLineArgs().Any(item => item == "--dexc-visual-capture"))
				yield break;

			var manifestPath = Environment.GetEnvironmentVariable("DEXC_RUNTIME_UI_CAPTURE_MANIFEST");
			Assert.That(manifestPath, Is.Not.Null.And.Not.Empty);
			var outputDirectory = Path.GetDirectoryName(manifestPath);
			Directory.CreateDirectory(outputDirectory);
			var stamp = Environment.GetEnvironmentVariable("DEXC_VISUAL_CAPTURE_STAMP") ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
			var profiles = new[]
			{
				new RuntimeUICaptureProfile("MobilePortrait", 390, 844),
				new RuntimeUICaptureProfile("MobileLandscape", 844, 390),
				new RuntimeUICaptureProfile("WindowsPlayer", 1366, 768),
			};
			var captures = new List<RuntimeUICaptureEntry>();
			DebuggerRuntime.Initialize();
			DexcRuntimeBootstrap.InitializeForTests();
			DebugMenu.Open();
			var cameraObject = new GameObject("UI01RuntimeIMGUIVisualCaptureCamera");
			var camera = cameraObject.AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.015f, 0.02f, 0.03f, 1f);
			camera.cullingMask = 0;

			foreach (var profile in profiles)
			{
				ResetRuntimeUICaptureState();
				DexcRuntimeUIRenderer.SetProfileOverrideForTests(
					DexcRuntimeUIReferenceResolver.LegacyProfileForTests(profile.Width, profile.Height)
				);
#if UNITY_EDITOR
				var restoreResolution = SetRuntimeGameViewSize(profile.Width, profile.Height, profile.Name);
#else
				Screen.SetResolution(profile.Width, profile.Height, FullScreenMode.Windowed);
				Action restoreResolution = () => { };
#endif
				try
				{
					var attempts = 0;
					while ((Screen.width != profile.Width || Screen.height != profile.Height) && attempts++ < 120)
						yield return null;
					Assert.That(Screen.width, Is.EqualTo(profile.Width), profile.Name + " width");
					Assert.That(Screen.height, Is.EqualTo(profile.Height), profile.Name + " height");

					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
					yield return CaptureRuntimeUI(profile, "Home", stamp, outputDirectory, captures);

					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
					DexcRuntimeUIState.Search = "UI02/LongCommand";
					yield return CaptureRuntimeUI(profile, "CommandList", stamp, outputDirectory, captures);

					SelectRuntimeCaptureCommand("UI02/LongCommand");
					yield return CaptureRuntimeUI(profile, "CommandDetailDraft", stamp, outputDirectory, captures);

					DexcRuntimeUIRenderer.ExecuteCommandForTests();
					Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Not.Null);
					yield return CaptureRuntimeUI(profile, "ResultState", stamp, outputDirectory, captures);

					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.History);
					yield return CaptureRuntimeUI(profile, "History", stamp, outputDirectory, captures);
					DexcRuntimeUIState.DiscardDraft();

					SelectRuntimeCaptureCommand("UI02/LongCommand");
					DexcRuntimeUIState.ScrollPosition = new Vector2(0f, 240f);
					yield return CaptureRuntimeUI(profile, "MultiInput12", stamp, outputDirectory, captures);

					RuntimeUGUIManyInputsCommand.Reject = true;
					DexcRuntimeUIRenderer.ExecuteCommandForTests();
					Assert.That(DexcRuntimeUIState.Notice, Is.EqualTo("Command validation failed."));
					yield return CaptureRuntimeUI(profile, "Validation", stamp, outputDirectory, captures);

					RuntimeUGUIManyInputsCommand.Reject = false;
					DexcRuntimeUIState.DiscardDraft();
					SelectRuntimeCaptureCommand("UI02/Cancellable");
					DexcRuntimeUIState.Notice = "Source Path: Scene/Streaming/Regions/West/VeryLongDestinationReference";
					yield return CaptureRuntimeUI(profile, "LongTextReference", stamp, outputDirectory, captures);
					DexcRuntimeUIState.Notice = string.Empty;
					DexcRuntimeUIRenderer.ExecuteCommandForTests();
					Assert.That(DexcRuntimeUIState.AsyncExecution, Is.Not.Null);
					yield return CaptureRuntimeUI(profile, "Running", stamp, outputDirectory, captures);
					ResetRuntimeUICaptureState();
				}
				finally
				{
					DexcRuntimeUIRenderer.ClearProfileOverrideForTests();
					restoreResolution();
				}
			}

			DebugMenu.Close();
			Object.DestroyImmediate(cameraObject);
			var manifest = new RuntimeUICaptureManifest
			{
				Version = 3,
				Renderer = "Runtime IMGUI",
				Source =
					"DexcRuntimeUIDriver.OnGUI -> DexcRuntimeUIRenderer.DrawMenuAndDisplay -> ScreenCapture.CaptureScreenshotAsTexture",
				RowHeight = 38f,
				Captures = captures.ToArray(),
			};
			File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
			Assert.That(File.Exists(manifestPath), Is.True);
			Assert.That(captures, Has.Count.EqualTo(27));
			Assert.That(captures.All(item => item.ContentVerified), Is.True);
		}

		[UnityTest]
		public IEnumerator UI06F11AndroidPortraitCommandListCaptureRecordsFifteenRowsAndScrollBounds()
		{
			var manifestPath = Environment.GetEnvironmentVariable("DEXC_UI06_F11_CAPTURE_MANIFEST");
			if (string.IsNullOrEmpty(manifestPath))
				yield break;

#if !UNITY_EDITOR
			Assert.Fail("UI06-F11 capture requires the Editor GameView runner.");
			yield break;
#else
			const int width = 1440;
			const int height = 2960;
			const string search = "UI06/Manual";
			var outputDirectory = Path.GetDirectoryName(manifestPath);
			Directory.CreateDirectory(outputDirectory);
			var stamp = Environment.GetEnvironmentVariable("DEXC_TEST_STAMP") ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
			var skin = AssetDatabase.LoadAssetAtPath<DexcRuntimeUISkin>("Assets/DexcDebugger/Font/DexcRuntimeUISkin-NotoSansJP.asset");
			Assert.That(skin, Is.Not.Null);
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = skin);
			DexcRuntimeBootstrap.InitializeForTests();
			DebugMenu.Open();
			var cameraObject = new GameObject("UI06F11AndroidPortraitCaptureCamera");
			var camera = cameraObject.AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.015f, 0.02f, 0.03f, 1f);
			camera.cullingMask = 0;
			DexcRuntimeUIRenderer.SetProfileOverrideForTests(DexcRuntimeUIProfileKind.AndroidPortrait);
			var restoreResolution = SetRuntimeGameViewSize(width, height, "UI06-F11-AndroidPortrait");
			var captures = new List<UI06F11CommandListCaptureEntry>();
			try
			{
				var attempts = 0;
				while ((Screen.width != width || Screen.height != height) && attempts++ < 120)
					yield return null;
				Assert.That(Screen.width, Is.EqualTo(width));
				Assert.That(Screen.height, Is.EqualTo(height));
				var session = DebugCommand
					.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 1)))
					.Summary.SessionId;
				DexcRuntimeUIState.SetSession(session);
				DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
				DexcRuntimeUIState.Search = search;
				var rows = DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, search, new DebugPageRequest(0, 24))).Items;
				Assert.That(rows, Has.Count.EqualTo(15));
				Assert.That(rows.Any(item => item.Path == "UI06/Manual/日本語入力確認"), Is.True);

				DexcRuntimeUIState.ListScrollPosition = Vector2.zero;
				yield return CaptureUI06F11CommandList("Top", stamp, manifestPath, rows, captures);
				DexcRuntimeUIState.SetSelectedIndex(rows.Count - 1);
				DexcRuntimeUIState.ListSelectionVisibilityRequested = true;
				yield return CaptureUI06F11CommandList("Bottom", stamp, manifestPath, rows, captures);
			}
			finally
			{
				DexcRuntimeUIRenderer.ClearProfileOverrideForTests();
				restoreResolution();
				DebugMenu.Close();
				Object.DestroyImmediate(cameraObject);
			}

			File.WriteAllText(
				manifestPath,
				JsonUtility.ToJson(
					new UI06F11CommandListCaptureManifest
					{
						Version = 1,
						Renderer = "Runtime IMGUI",
						Profile = "AndroidPortrait",
						Search = search,
						RowCount = 15,
						RowHeight = 38f,
						RequestedViewportHeight = 440f,
						ContentHeight = captures.Last().ContentHeight,
						MaximumScrollY = captures.Last().MaximumScrollY,
						Captures = captures.ToArray(),
					},
					true
				)
			);
			Assert.That(captures, Has.Count.EqualTo(2));
#endif
		}

		[UnityTest]
		public IEnumerator WrappedCommandRowsFitAndNavigationPreservesTouchScrollAcrossProfiles()
		{
#if !UNITY_EDITOR
			yield break;
#else
			if (Application.isBatchMode)
			{
				Assert.Ignore("Use the automatic --graphics runner for IMGUI layout.");
			}
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(false);
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(false);
#endif
			var skin = AssetDatabase.LoadAssetAtPath<DexcRuntimeUISkin>("Assets/DexcDebugger/Font/DexcRuntimeUISkin-NotoSansJP.asset");
			Assert.That(skin, Is.Not.Null);
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = skin);
			DexcRuntimeBootstrap.InitializeForTests();
			DebugMenu.Open();
			var profiles = new[]
			{
				new RuntimeUICaptureProfile("AndroidPortrait", 1440, 2960),
				new RuntimeUICaptureProfile("AndroidLandscape", 2960, 1440),
				new RuntimeUICaptureProfile("Windows", 1366, 768),
			};
			var profileKinds = new[]
			{
				DexcRuntimeUIProfileKind.AndroidPortrait,
				DexcRuntimeUIProfileKind.AndroidLandscape,
				DexcRuntimeUIProfileKind.Windows,
			};
			for (var profileIndex = 0; profileIndex < profiles.Length; profileIndex++)
			{
				var profile = profiles[profileIndex];
				DexcRuntimeUIRenderer.SetProfileOverrideForTests(profileKinds[profileIndex]);
				var restoreResolution = SetRuntimeGameViewSize(profile.Width, profile.Height, "UI06-F11-" + profile.Name);
				try
				{
					var attempts = 0;
					while ((Screen.width != profile.Width || Screen.height != profile.Height) && attempts++ < 120)
						yield return null;
					var session = DebugCommand
						.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 1)))
						.Summary.SessionId;
					DexcRuntimeUIState.SetSession(session);
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
					DexcRuntimeUIState.Search = "Tests/UIR07/Scrolling";
					DexcRuntimeUIState.SetSelectedIndex(14);
					DexcRuntimeUIState.ListSelectionVisibilityRequested = true;
					yield return null;
					yield return null;
					yield return null;
					Assert.That(DexcRuntimeUIRenderer.ListRowRectsForTests, Has.Length.EqualTo(15), profile.Name);
					Assert.That(DexcRuntimeUIRenderer.ListRowsFullyVisibleForTests[14], Is.True, profile.Name + " selected last row");
					Assert.That(
						DexcRuntimeUIRenderer.ListRowRectsForTests[14].height,
						Is.GreaterThan(28f),
						profile.Name + " wrapped Japanese row"
					);
					Assert.That(
						DexcRuntimeUIRenderer.ListRowRectsForTests.All(rect =>
							rect.xMin >= 0f && rect.xMax <= DexcRuntimeUIRenderer.ListViewportRect.width + 0.1f
						),
						Is.True,
						profile.Name + " horizontal bounds"
					);
					var physical = DexcRuntimeUIRenderer.CalculateLayoutForProfileTests(
						Screen.width,
						Screen.height,
						Screen.safeArea,
						DexcRuntimeUIPage.CommandList,
						profileKinds[profileIndex]
					);
					var lastScreenRow = DexcRuntimeUIRenderer.ListRowScreenRectsForTests[14];
					Assert.That(
						lastScreenRow.yMin,
						Is.GreaterThanOrEqualTo(Screen.height - physical.ContentViewport.yMax + 7f * physical.Scale),
						profile.Name + " last row must stay above the outer area clip"
					);
					Assert.That(
						lastScreenRow.xMax,
						Is.LessThanOrEqualTo(physical.ContentViewport.xMax - 7f * physical.Scale),
						profile.Name + " row must stay inside the outer area clip"
					);
					yield return new WaitForEndOfFrame();
					var capture = ScreenCapture.CaptureScreenshotAsTexture();
					Assert.That(capture, Is.Not.Null);
					try
					{
						Directory.CreateDirectory("Logs/UnityTests/UIR07/CommandList");
						File.WriteAllBytes("Logs/UnityTests/UIR07/CommandList/" + profile.Name + ".png", capture.EncodeToPNG());
					}
					finally
					{
						Object.DestroyImmediate(capture);
					}
					var maximumScroll = Mathf.Max(
						0f,
						DexcRuntimeUIRenderer.ListRowRectsForTests.Last().yMax - DexcRuntimeUIRenderer.ListViewportRect.height
					);
					Assert.That(
						DexcRuntimeUIState.ScrollPosition.y,
						Is.GreaterThanOrEqualTo(maximumScroll - 0.1f),
						profile.Name + " last row scroll bound"
					);
					var touchScroll = Mathf.Min(42f, maximumScroll);
					DexcRuntimeUIState.ScrollPosition = new Vector2(0f, touchScroll);
					DexcRuntimeUIState.ListSelectionVisibilityRequested = false;
					yield return null;
					yield return null;
					Assert.That(
						DexcRuntimeUIState.ScrollPosition.y,
						Is.EqualTo(touchScroll).Within(0.1f),
						profile.Name + " idle touch scroll"
					);
				}
				finally
				{
					DexcRuntimeUIRenderer.ClearProfileOverrideForTests();
					restoreResolution();
				}
			}
			DebugMenu.Close();
#endif
		}

#if UNITY_EDITOR
		private static IEnumerator CaptureUI06F11CommandList(
			string position,
			string stamp,
			string manifestPath,
			IReadOnlyList<DebugCommandListItem> rows,
			ICollection<UI06F11CommandListCaptureEntry> captures
		)
		{
			yield return null;
			yield return new WaitForEndOfFrame();
			yield return null;
			yield return new WaitForEndOfFrame();
			var texture = ScreenCapture.CaptureScreenshotAsTexture();
			Assert.That(texture, Is.Not.Null, position);
			try
			{
				var bytes = texture.EncodeToPNG();
				var artifactPath = Path.Combine(
					Path.GetDirectoryName(manifestPath),
					"UI06-F11-AndroidPortrait-CommandList-" + position + "-" + stamp + ".png"
				);
				File.WriteAllBytes(artifactPath, bytes);
				var layout = DexcRuntimeUIRenderer.CalculateLayoutForProfileTests(
					Screen.width,
					Screen.height,
					Screen.safeArea,
					DexcRuntimeUIPage.CommandList,
					DexcRuntimeUIProfileKind.AndroidPortrait
				);
				var rowRects = DexcRuntimeUIRenderer.ListRowRectsForTests;
				var mainRects = DexcRuntimeUIRenderer.ListMainRectsForTests;
				var detailRects = DexcRuntimeUIRenderer.ListDetailRectsForTests;
				var rowScreenRects = DexcRuntimeUIRenderer.ListRowScreenRectsForTests;
				var mainScreenRects = DexcRuntimeUIRenderer.ListMainScreenRectsForTests;
				var detailScreenRects = DexcRuntimeUIRenderer.ListDetailScreenRectsForTests;
				var fullyVisible = DexcRuntimeUIRenderer.ListRowsFullyVisibleForTests;
				Assert.That(rowRects, Has.Length.EqualTo(rows.Count));
				Assert.That(mainRects, Has.Length.EqualTo(rows.Count));
				Assert.That(detailRects, Has.Length.EqualTo(rows.Count));
				Assert.That(rowScreenRects, Has.Length.EqualTo(rows.Count));
				Assert.That(mainScreenRects, Has.Length.EqualTo(rows.Count));
				Assert.That(detailScreenRects, Has.Length.EqualTo(rows.Count));
				Assert.That(fullyVisible, Has.Length.EqualTo(rows.Count));
				var evidence = Enumerable
					.Range(0, rows.Count)
					.Select(index => new UI06F11CommandListRowEntry
					{
						Index = index,
						Path = rows[index].Path,
						Description = rows[index].Description ?? string.Empty,
						Row = new RuntimeUICaptureRect(rowRects[index]),
						Main = new RuntimeUICaptureRect(mainRects[index]),
						Detail = new RuntimeUICaptureRect(detailRects[index]),
						RowScreen = new RuntimeUICaptureRect(rowScreenRects[index]),
						MainScreen = new RuntimeUICaptureRect(mainScreenRects[index]),
						DetailScreen = new RuntimeUICaptureRect(detailScreenRects[index]),
						FullyVisible = fullyVisible[index],
					})
					.ToArray();
				var contentHeight = rowRects.Last().yMax;
				captures.Add(
					new UI06F11CommandListCaptureEntry
					{
						Position = position,
						ArtifactPath = Path.GetFullPath(artifactPath),
						Sha256 = Sha256(bytes),
						Bytes = bytes.Length,
						Screen = new RuntimeUICaptureRect(new Rect(0f, 0f, Screen.width, Screen.height)),
						SafeArea = new RuntimeUICaptureRect(Screen.safeArea),
						Panel = new RuntimeUICaptureRect(layout.Panel),
						ContentViewport = new RuntimeUICaptureRect(layout.ContentViewport),
						ListViewport = new RuntimeUICaptureRect(DexcRuntimeUIRenderer.ListViewportRect),
						ListViewportScreen = new RuntimeUICaptureRect(DexcRuntimeUIRenderer.ListViewportScreenRectForTests),
						Scale = layout.Scale,
						ListScrollY = DexcRuntimeUIState.ListScrollPosition.y,
						SelectedIndex = DexcRuntimeUIState.SelectedIndex,
						ContentHeight = contentHeight,
						MaximumScrollY = Mathf.Max(0f, contentHeight - DexcRuntimeUIRenderer.ListViewportRect.height),
						LastFullyVisibleIndex = evidence
							.Where(item => item.FullyVisible)
							.Select(item => item.Index)
							.DefaultIfEmpty(-1)
							.Max(),
						Paths = rows.Select(item => item.Path).ToArray(),
						Descriptions = rows.Select(item => item.Description ?? string.Empty).ToArray(),
						Rows = evidence,
					}
				);
			}
			finally
			{
				Object.DestroyImmediate(texture);
			}
		}
#endif

		private static void ResetRuntimeUICaptureState()
		{
			RuntimeUGUIManyInputsCommand.Reject = false;
			DexcRuntimeUIState.Notice = string.Empty;
			DexcRuntimeUIState.Search = string.Empty;
			DexcRuntimeUIState.ScrollPosition = Vector2.zero;
			DexcRuntimeUIState.ListScrollPosition = Vector2.zero;
			DexcRuntimeUIState.ResetForSession();
			var session = DebugCommand
				.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 1)))
				.Summary.SessionId;
			DexcRuntimeUIState.SetSession(session);
			DebugMenu.Open();
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
		}

		private static void SelectRuntimeCaptureCommand(string search)
		{
			var command = DebugCommand
				.Registry.GetList(new DebugListQuery(string.Empty, search, new DebugPageRequest(0, 8)))
				.Items.Single();
			DexcRuntimeUIRenderer.SelectCommandForTests(command);
		}

		private static bool CanExecuteRuntimeCaptureDraft()
		{
			if (!DexcRuntimeUIState.HasDraft || DexcRuntimeUIState.AsyncExecution != null)
				return false;
			var operation = DebugCommand.Draft.Validate(DexcRuntimeUIState.DraftId);
			return operation.TryGetValidation(out var validation) && validation.CanExecute;
		}

		private static IEnumerator CaptureRuntimeUI(
			RuntimeUICaptureProfile profile,
			string scenario,
			string stamp,
			string outputDirectory,
			ICollection<RuntimeUICaptureEntry> captures
		)
		{
			const int settledFrames = 2;
			yield return null;
			yield return new WaitForEndOfFrame();
			yield return null;
			yield return new WaitForEndOfFrame();
			var texture = ScreenCapture.CaptureScreenshotAsTexture();
			Assert.That(texture, Is.Not.Null, profile.Name + "/" + scenario);
			try
			{
				var bytes = texture.EncodeToPNG();
				var artifactPath = Path.Combine(outputDirectory, "UI01-" + profile.Name + "-" + scenario + "-" + stamp + ".png");
				File.WriteAllBytes(artifactPath, bytes);
				var layout = DexcRuntimeUIRenderer.CalculateLayoutForTests(
					Screen.width,
					Screen.height,
					Screen.safeArea,
					DexcRuntimeUIState.Page
				);
				var pixels = texture.GetPixels32();
				var background = pixels[0];
				var nonBackgroundPixels = pixels.Count(pixel =>
					Math.Abs(pixel.r - background.r) > 3 || Math.Abs(pixel.g - background.g) > 3 || Math.Abs(pixel.b - background.b) > 3
				);
				var panelColorBuckets = CountPanelColorBuckets(pixels, texture.width, texture.height, layout.Panel);
				var shell = MeasureShellPixels(pixels, texture.width, texture.height, layout, scenario, background);
				var footerFocusMarker = DexcRuntimeUIRenderer.FooterFocusMarkerRectForTests;
				footerFocusMarker.position += layout.Footer.position;
				var contentVerified = nonBackgroundPixels > 100 && panelColorBuckets > 8 && shell.Verified && bytes.Length > 512;
				Assert.That(
					shell.HorizontalScrollbarRows,
					Is.LessThan(2),
					profile.Name + "/" + scenario + " rendered an unintended horizontal scrollbar."
				);
				Assert.That(contentVerified, Is.True, profile.Name + "/" + scenario + " did not contain verified Runtime IMGUI content.");
				captures.Add(
					new RuntimeUICaptureEntry
					{
						Profile = profile.Name,
						Scenario = scenario,
						Page = DexcRuntimeUIState.Page.ToString(),
						ArtifactPath = Path.GetFullPath(artifactPath),
						Sha256 = Sha256(bytes),
						Screen = new RuntimeUICaptureRect(new Rect(0f, 0f, Screen.width, Screen.height)),
						SafeArea = new RuntimeUICaptureRect(Screen.safeArea),
						Panel = new RuntimeUICaptureRect(layout.Panel),
						Header = new RuntimeUICaptureRect(layout.Header),
						ContentViewport = new RuntimeUICaptureRect(layout.ContentViewport),
						Footer = new RuntimeUICaptureRect(layout.Footer),
						FooterFocusMarker = new RuntimeUICaptureRect(footerFocusMarker),
						FooterFocusMarkerVisible = DexcRuntimeUIRenderer.FooterFocusMarkerVisibleForTests,
						RowHeight = layout.RowHeight,
						VerticalOverflow = DexcRuntimeUIState.ScrollPosition.y > 0.01f,
						HorizontalOverflow = shell.HorizontalScrollbarRows >= 2,
						HorizontalScrollbarMaxRun = shell.HorizontalScrollbarMaxRun,
						HorizontalScrollbarRows = shell.HorizontalScrollbarRows,
						HeaderFixed = Math.Abs(layout.Header.yMin - layout.Panel.yMin) < 0.01f,
						FooterFixed = layout.Footer.height <= 0f || Math.Abs(layout.Footer.yMax - layout.Panel.yMax) < 0.01f,
						ExecuteEnabled = DexcRuntimeUIState.Page == DexcRuntimeUIPage.CommandDetail && CanExecuteRuntimeCaptureDraft(),
						CancelVisible = DexcRuntimeUIState.AsyncExecution != null,
						Notice = DexcRuntimeUIState.Notice ?? string.Empty,
						AsyncRunning = DexcRuntimeUIState.AsyncExecution != null,
						SelectedIndex = DexcRuntimeUIState.SelectedIndex,
						ScrollY = DexcRuntimeUIState.ScrollPosition.y,
						SettledFrames = settledFrames,
						NonBackgroundPixels = nonBackgroundPixels,
						PanelColorBuckets = panelColorBuckets,
						HeaderBackVisiblePixels = shell.HeaderBackVisiblePixels,
						HeaderTitleVisiblePixels = shell.HeaderTitleVisiblePixels,
						HeaderCloseVisiblePixels = shell.HeaderCloseVisiblePixels,
						ContentOutsideInkPixels = shell.ContentOutsideInkPixels,
						ContentLeftBoundaryInkPixels = shell.ContentLeftBoundaryInkPixels,
						FooterVisibleXMin = shell.FooterVisibleXMin,
						FooterVisibleXMax = shell.FooterVisibleXMax,
						ShellVerified = shell.Verified,
						ContentVerified = contentVerified,
					}
				);
			}
			finally
			{
				Object.DestroyImmediate(texture);
			}
		}

		private static int CountPanelColorBuckets(IReadOnlyList<Color32> pixels, int width, int height, Rect panel)
		{
			var buckets = new HashSet<int>();
			var xMin = Mathf.Clamp(Mathf.FloorToInt(panel.xMin), 0, width - 1);
			var xMax = Mathf.Clamp(Mathf.CeilToInt(panel.xMax), 0, width);
			var yMin = Mathf.Clamp(Mathf.FloorToInt(panel.yMin), 0, height - 1);
			var yMax = Mathf.Clamp(Mathf.CeilToInt(panel.yMax), 0, height);
			for (var guiY = yMin; guiY < yMax; guiY += 2)
			{
				var textureY = height - 1 - guiY;
				for (var x = xMin; x < xMax; x += 2)
				{
					var pixel = pixels[textureY * width + x];
					buckets.Add((pixel.r >> 4) << 8 | (pixel.g >> 4) << 4 | pixel.b >> 4);
				}
			}
			return buckets.Count;
		}

		private static RuntimeUIShellMetrics MeasureShellPixels(
			IReadOnlyList<Color32> pixels,
			int width,
			int height,
			DexcRuntimeUILayout layout,
			string page,
			Color32 background
		)
		{
			var panel = layout.Panel;
			var header = layout.Header;
			var hasBack = page != "Home";
			var back = CountVisiblePixels(
				pixels,
				width,
				height,
				new Rect(header.x, header.y, Mathf.Min(40f, header.width), header.height),
				background
			);
			var titleX = header.x + (hasBack ? 40f : 0f);
			var titleWidth = Mathf.Min(160f, Mathf.Max(0f, header.xMax - 40f - titleX));
			var title = CountVisiblePixels(pixels, width, height, new Rect(titleX, header.y, titleWidth, header.height), background);
			var close = CountVisiblePixels(
				pixels,
				width,
				height,
				new Rect(Mathf.Max(header.x, header.xMax - 40f), header.y, Mathf.Min(40f, header.width), header.height),
				background
			);
			var outside =
				CountInkPixels(pixels, width, height, new Rect(0f, layout.ContentViewport.y, panel.x, layout.ContentViewport.height))
				+ CountInkPixels(
					pixels,
					width,
					height,
					new Rect(panel.xMax, layout.ContentViewport.y, Mathf.Max(0f, width - panel.xMax), layout.ContentViewport.height)
				);
			var leftBoundary = CountInkPixels(
				pixels,
				width,
				height,
				new Rect(
					layout.ContentViewport.x,
					layout.ContentViewport.y,
					Mathf.Min(2f, layout.ContentViewport.width),
					layout.ContentViewport.height
				)
			);
			var footerXMin = -1;
			var footerXMax = -1;
			if (layout.Footer.height > 0f)
				FindVisibleHorizontalBounds(pixels, width, height, layout.Footer, background, out footerXMin, out footerXMax);
			var footerWidth = footerXMin < 0 || footerXMax < footerXMin ? 0f : footerXMax - footerXMin + 1f;
			var footerVerified =
				layout.Footer.height <= 0f
				|| footerXMin >= layout.Footer.x
					&& footerXMax < layout.Footer.xMax
					&& footerXMin - layout.Footer.x <= 6f
					&& layout.Footer.xMax - 1f - footerXMax <= 6f
					&& footerWidth >= layout.Footer.width - 12f;
			MeasureHorizontalScrollbar(
				pixels,
				width,
				height,
				layout.ContentViewport,
				out var horizontalScrollbarMaxRun,
				out var horizontalScrollbarRows
			);
			return new RuntimeUIShellMetrics
			{
				HeaderBackVisiblePixels = back,
				HeaderTitleVisiblePixels = title,
				HeaderCloseVisiblePixels = close,
				ContentOutsideInkPixels = outside,
				ContentLeftBoundaryInkPixels = leftBoundary,
				FooterVisibleXMin = footerXMin,
				FooterVisibleXMax = footerXMax,
				HorizontalScrollbarMaxRun = horizontalScrollbarMaxRun,
				HorizontalScrollbarRows = horizontalScrollbarRows,
				Verified =
					(!hasBack || back > 40)
					&& title > 80
					&& close > 40
					&& outside == 0
					&& leftBoundary == 0
					&& footerVerified
					&& horizontalScrollbarRows < 2,
			};
		}

		private static void MeasureHorizontalScrollbar(
			IReadOnlyList<Color32> pixels,
			int width,
			int height,
			Rect content,
			out int maximumRun,
			out int matchingRows
		)
		{
			maximumRun = 0;
			matchingRows = 0;
			var xMin = Mathf.Clamp(Mathf.FloorToInt(content.xMin), 0, width);
			var xMax = Mathf.Clamp(Mathf.CeilToInt(content.xMax), 0, width);
			var yMax = Mathf.Clamp(Mathf.CeilToInt(content.yMax), 0, height);
			var yMin = Mathf.Max(Mathf.Clamp(Mathf.FloorToInt(content.yMin), 0, height), yMax - 24);
			var minimumRun = Mathf.CeilToInt(content.width * 0.45f);
			for (var guiY = yMin; guiY < yMax; guiY++)
			{
				var textureY = height - 1 - guiY;
				var run = 0;
				var rowMaximum = 0;
				for (var x = xMin; x < xMax; x++)
				{
					var pixel = pixels[textureY * width + x];
					var high = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
					var low = Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b));
					var scrollbarPixel = high >= 30 && high <= 190 && high - low <= 12;
					run = scrollbarPixel ? run + 1 : 0;
					rowMaximum = Mathf.Max(rowMaximum, run);
				}
				maximumRun = Mathf.Max(maximumRun, rowMaximum);
				if (rowMaximum >= minimumRun)
					matchingRows++;
			}
		}

		private static int CountVisiblePixels(IReadOnlyList<Color32> pixels, int width, int height, Rect rect, Color32 background)
		{
			var count = 0;
			VisitGuiRectPixels(
				pixels,
				width,
				height,
				rect,
				pixel =>
				{
					if (
						Math.Abs(pixel.r - background.r) > 8
						|| Math.Abs(pixel.g - background.g) > 8
						|| Math.Abs(pixel.b - background.b) > 8
					)
						count++;
				}
			);
			return count;
		}

		private static int CountInkPixels(IReadOnlyList<Color32> pixels, int width, int height, Rect rect)
		{
			var count = 0;
			VisitGuiRectPixels(
				pixels,
				width,
				height,
				rect,
				pixel =>
				{
					if (Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) > 64)
						count++;
				}
			);
			return count;
		}

		private static void FindVisibleHorizontalBounds(
			IReadOnlyList<Color32> pixels,
			int width,
			int height,
			Rect rect,
			Color32 background,
			out int xMin,
			out int xMax
		)
		{
			xMin = int.MaxValue;
			xMax = int.MinValue;
			var rectXMin = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, width);
			var rectXMax = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), 0, width);
			var rectYMin = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, height);
			var rectYMax = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), 0, height);
			for (var guiY = rectYMin; guiY < rectYMax; guiY++)
			{
				var textureY = height - 1 - guiY;
				for (var x = rectXMin; x < rectXMax; x++)
				{
					var pixel = pixels[textureY * width + x];
					if (
						Math.Abs(pixel.r - background.r) <= 8
						&& Math.Abs(pixel.g - background.g) <= 8
						&& Math.Abs(pixel.b - background.b) <= 8
					)
						continue;
					xMin = Mathf.Min(xMin, x);
					xMax = Mathf.Max(xMax, x);
				}
			}
			if (xMin == int.MaxValue)
				xMin = -1;
			if (xMax == int.MinValue)
				xMax = -1;
		}

		private static void VisitGuiRectPixels(IReadOnlyList<Color32> pixels, int width, int height, Rect rect, Action<Color32> visit)
		{
			var xMin = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, width);
			var xMax = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), 0, width);
			var yMin = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, height);
			var yMax = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), 0, height);
			for (var guiY = yMin; guiY < yMax; guiY++)
			{
				var textureY = height - 1 - guiY;
				for (var x = xMin; x < xMax; x++)
					visit(pixels[textureY * width + x]);
			}
		}

#if UNITY_EDITOR
		private static Action SetRuntimeGameViewSize(int width, int height, string profile)
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
			var size = constructor.Invoke(new object[] { fixedResolution, width, height, "Dexc UI01 " + profile });
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
#endif

		private static string Sha256(byte[] bytes)
		{
			using (var sha = SHA256.Create())
				return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
		}

		[Serializable]
		private sealed class PlayerVisualCaptureManifest
		{
			public long Frame;
			public long PlayerFrame;
			public string PlayerPath;
			public string PlayerSha256;
			public string PlayerSource;
			public string PlayerRenderer;
			public string PlayerCameraType;
			public int PlayerWidth;
			public int PlayerHeight;
			public int PlayerNonBackgroundPixels;
			public string[] PlayerKinds;
		}

		private readonly struct RuntimeUICaptureProfile
		{
			internal RuntimeUICaptureProfile(string name, int width, int height)
			{
				Name = name;
				Width = width;
				Height = height;
			}

			internal string Name { get; }
			internal int Width { get; }
			internal int Height { get; }
		}

		[Serializable]
		private sealed class RuntimeUICaptureManifest
		{
			public int Version;
			public string Renderer;
			public string Source;
			public float RowHeight;
			public RuntimeUICaptureEntry[] Captures;
		}

		[Serializable]
		private sealed class UI06F11CommandListCaptureManifest
		{
			public int Version;
			public string Renderer;
			public string Profile;
			public string Search;
			public int RowCount;
			public float RowHeight;
			public float ContentHeight;
			public float RequestedViewportHeight;
			public float MaximumScrollY;
			public UI06F11CommandListCaptureEntry[] Captures;
		}

		[Serializable]
		private sealed class UI06F11CommandListCaptureEntry
		{
			public string Position;
			public string ArtifactPath;
			public string Sha256;
			public int Bytes;
			public RuntimeUICaptureRect Screen;
			public RuntimeUICaptureRect SafeArea;
			public RuntimeUICaptureRect Panel;
			public RuntimeUICaptureRect ContentViewport;
			public RuntimeUICaptureRect ListViewport;
			public RuntimeUICaptureRect ListViewportScreen;
			public float Scale;
			public float ListScrollY;
			public int SelectedIndex;
			public float ContentHeight;
			public float MaximumScrollY;
			public int LastFullyVisibleIndex;
			public string[] Paths;
			public string[] Descriptions;
			public UI06F11CommandListRowEntry[] Rows;
		}

		[Serializable]
		private sealed class UI06F11CommandListRowEntry
		{
			public int Index;
			public string Path;
			public string Description;
			public RuntimeUICaptureRect Row;
			public RuntimeUICaptureRect Main;
			public RuntimeUICaptureRect Detail;
			public RuntimeUICaptureRect RowScreen;
			public RuntimeUICaptureRect MainScreen;
			public RuntimeUICaptureRect DetailScreen;
			public bool FullyVisible;
		}

		[Serializable]
		private sealed class RuntimeUICaptureEntry
		{
			public string Profile;
			public string Scenario;
			public string Page;
			public string ArtifactPath;
			public string Sha256;
			public RuntimeUICaptureRect Screen;
			public RuntimeUICaptureRect SafeArea;
			public RuntimeUICaptureRect Panel;
			public RuntimeUICaptureRect Header;
			public RuntimeUICaptureRect ContentViewport;
			public RuntimeUICaptureRect Footer;
			public RuntimeUICaptureRect FooterFocusMarker;
			public bool FooterFocusMarkerVisible;
			public float RowHeight;
			public bool VerticalOverflow;
			public bool HorizontalOverflow;
			public int HorizontalScrollbarMaxRun;
			public int HorizontalScrollbarRows;
			public bool HeaderFixed;
			public bool FooterFixed;
			public bool ExecuteEnabled;
			public bool CancelVisible;
			public string Notice;
			public bool AsyncRunning;
			public int SelectedIndex;
			public float ScrollY;
			public int SettledFrames;
			public int NonBackgroundPixels;
			public int PanelColorBuckets;
			public int HeaderBackVisiblePixels;
			public int HeaderTitleVisiblePixels;
			public int HeaderCloseVisiblePixels;
			public int ContentOutsideInkPixels;
			public int ContentLeftBoundaryInkPixels;
			public int FooterVisibleXMin;
			public int FooterVisibleXMax;
			public bool ShellVerified;
			public bool ContentVerified;
		}

		private struct RuntimeUIShellMetrics
		{
			internal int HeaderBackVisiblePixels;
			internal int HeaderTitleVisiblePixels;
			internal int HeaderCloseVisiblePixels;
			internal int ContentOutsideInkPixels;
			internal int ContentLeftBoundaryInkPixels;
			internal int FooterVisibleXMin;
			internal int FooterVisibleXMax;
			internal int HorizontalScrollbarMaxRun;
			internal int HorizontalScrollbarRows;
			internal bool Verified;
		}

		[Serializable]
		private struct RuntimeUICaptureRect
		{
			public RuntimeUICaptureRect(Rect rect)
			{
				X = rect.x;
				Y = rect.y;
				Width = rect.width;
				Height = rect.height;
			}

			public float X;
			public float Y;
			public float Width;
			public float Height;
		}

		private sealed class TestInputProvider : DebugMenuInputProvider
		{
			readonly Queue<DebugMenuInputFrame> m_Frames = new Queue<DebugMenuInputFrame>();
			internal int ReadCount;
			internal int NonZeroFrameCount;
			internal readonly List<DebugMenuInputState> States = new List<DebugMenuInputState>();

			internal void Enqueue(DebugMenuInputFrame frame)
			{
				m_Frames.Enqueue(frame);
			}

			public override DebugMenuInputFrame ReadInput()
			{
				ReadCount++;
				var frame = m_Frames.Count == 0 ? new DebugMenuInputFrame(Vector2.zero, false, false) : m_Frames.Dequeue();
				if (frame.Navigation != Vector2.zero || frame.SubmitPressed || frame.CancelPressed)
					NonZeroFrameCount++;
				return frame;
			}

			public override void OnInputStateChanged(DebugMenuInputState state)
			{
				States.Add(state);
			}
		}

		[DebugInputEditor(100, "Runtime")]
		public sealed class RuntimeScenarioInputEditor : DebugInputEditor
		{
			public static int RenderCount;

			public override bool CanEdit(DebugInputEditorDescriptor descriptor)
			{
				return descriptor.InputType == typeof(RuntimeScenarioInt);
			}

			public override void OnGUI(DebugInputEditorContext context, DebugInputEdit edit)
			{
				RenderCount++;
				edit.SetValue(DebugValue.From(42));
			}
		}

		public sealed class RuntimeScenarioInt : DebugInput<int>
		{
			public RuntimeScenarioInt()
				: base(1, DebugInputSchema.SignedInteger()) { }

			protected override DebugValue WriteValue(int value) => DebugValue.From(value);

			protected override DebugInputReadResult<int> ReadValue(DebugValue value)
			{
				return value.TryGetSignedInteger(out var number) && number >= int.MinValue && number <= int.MaxValue
					? DebugInputReadResult<int>.Success((int)number)
					: DebugInputReadResult<int>.Failure("An Int32 is required.");
			}
		}

		[DebugCommandAttribute("Tests/RuntimeUI/EnemyCommand", Access = DebugCommandAccess.ReadOnly)]
		public sealed class RuntimeScenarioEnemyCommand : DebugCommand
		{
			public RuntimeScenarioInt Damage = new RuntimeScenarioInt();

			protected override void Execute() { }
		}

		[DebugCommandAttribute("Tests/RuntimeUI/UI05/DisabledExecute", Access = DebugCommandAccess.ReadOnly)]
		public sealed class RuntimeUI05DisabledExecuteCommand : DebugCommand
		{
			public static bool Reject;
			public static int ExecutionCount;
			public InputString SourcePath = new InputString("Scenes/Validation.unity");

			protected override void Validate(DebugCommandValidation validation)
			{
				if (Reject)
					validation.Error(SourcePath, "ui05-disabled-execute", "The selected Source Path is stale.");
			}

			protected override void Execute()
			{
				ExecutionCount++;
			}
		}

		[DebugCommandAttribute(
			"UI06/Manual/日本語入力確認",
			Description = "物理入力でDetailを開き空白なし日本語を入力してvalidation更新と末尾表示を確認します"
		)]
		public sealed class RuntimeUI06F11JapaneseCommand : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/00")]
		public sealed class RuntimeUI06F11ScrollCommand00 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/01")]
		public sealed class RuntimeUI06F11ScrollCommand01 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/02")]
		public sealed class RuntimeUI06F11ScrollCommand02 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/03")]
		public sealed class RuntimeUI06F11ScrollCommand03 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/04")]
		public sealed class RuntimeUI06F11ScrollCommand04 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/05")]
		public sealed class RuntimeUI06F11ScrollCommand05 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/06")]
		public sealed class RuntimeUI06F11ScrollCommand06 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/07")]
		public sealed class RuntimeUI06F11ScrollCommand07 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/08")]
		public sealed class RuntimeUI06F11ScrollCommand08 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/09")]
		public sealed class RuntimeUI06F11ScrollCommand09 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/10")]
		public sealed class RuntimeUI06F11ScrollCommand10 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/11")]
		public sealed class RuntimeUI06F11ScrollCommand11 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/12")]
		public sealed class RuntimeUI06F11ScrollCommand12 : DebugCommand
		{
			protected override void Execute() { }
		}

		[DebugCommandAttribute("UI06/Manual/Scroll/13", Description = "Short / long / numeric native focus regression")]
		public sealed class RuntimeUI06F11ScrollCommand13 : DebugCommand
		{
			protected override void Execute() { }
		}

		public sealed class RuntimeScenarioInspectable : MonoBehaviour
		{
			public int Health = 7;
			public bool Enabled = true;
			public RuntimeScenarioMode Mode = RuntimeScenarioMode.Combat;
			public Color Tint = Color.cyan;
			public Vector3 Direction = Vector3.forward;
		}

		public enum RuntimeScenarioMode
		{
			Combat,
			Passive,
		}

		public sealed class RuntimeScenarioEmptyInspectable : MonoBehaviour { }

		public sealed class RuntimeScenarioSettings : DebugSettingSet<RuntimeScenarioSettings>
		{
			[DebugSetting("Tests/RuntimeUI/Quality")]
			public int Quality { get; private set; } = 1;

			[DebugSetting("Tests/RuntimeUI/Schema/Enabled")]
			public bool Enabled { get; private set; } = true;

			[DebugSetting("Tests/RuntimeUI/Schema/Mode")]
			public RuntimeScenarioMode Mode { get; private set; } = RuntimeScenarioMode.Combat;

			[DebugSetting("Tests/RuntimeUI/Schema/Speed")]
			[DebugRange(0d, 10d)]
			[DebugSlider]
			public float Speed { get; private set; } = 3f;

			[DebugSetting("Tests/RuntimeUI/Schema/Tint")]
			public Color Tint { get; private set; } = Color.white;

			[DebugSetting("Tests/RuntimeUI/Schema/Direction")]
			public Vector3 Direction { get; private set; } = Vector3.forward;

			[DebugPreset("Tests/RuntimeUI/High", "High", "Runtime scenario preset")]
			public static RuntimeScenarioSettings High
			{
				get { return new RuntimeScenarioSettings { Quality = 9 }; }
			}
		}
#endif
	}
#endif
}
