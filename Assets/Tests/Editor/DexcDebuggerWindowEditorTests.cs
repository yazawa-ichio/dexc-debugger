using System.Linq;
using System.Linq;
using Dexc;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DebuggerRuntime = Dexc.DexcDebugger;
using Object = UnityEngine.Object;

namespace DexcDebugger.Tests
{
	public sealed class DexcDebuggerWindowEditorTests
	{
		private GameObject m_Object;

		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.ResetForTests();
			m_Object = new GameObject("DexcEditorSelectionFixture");
		}

		[TearDown]
		public void TearDown()
		{
			Selection.activeGameObject = null;
			if (m_Object != null)
				Object.DestroyImmediate(m_Object);
			DebuggerRuntime.ResetForTests();
		}

		[Test]
		public void WindowRegistersAndUnregistersEditorCallbacks()
		{
			var before = Resources.FindObjectsOfTypeAll<DexcDebuggerWindow>().Length;
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			Assert.That(Resources.FindObjectsOfTypeAll<DexcDebuggerWindow>().Length, Is.EqualTo(before + 1));
			Object.DestroyImmediate(window);
			Assert.That(Resources.FindObjectsOfTypeAll<DexcDebuggerWindow>().Length, Is.EqualTo(before));
		}

#if DEXC_DEBUGGER
		[Test]
		public void SelectionSynchronizesToInspectorTargetInCurrentSession()
		{
			DebuggerRuntime.Initialize();
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			Selection.activeGameObject = m_Object;
			window.ProcessEditorUpdateForTests();
			Assert.That(window.SelectedSceneIsValidForTests, Is.True);
			Assert.That(window.SelectedTargetIsValidForTests, Is.True);
			Object.DestroyImmediate(window);
		}

		[Test]
		public void SelectionClearDoesNotRetainInspectorHandles()
		{
			DebuggerRuntime.Initialize();
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			Selection.activeGameObject = m_Object;
			window.ProcessEditorUpdateForTests();
			Assert.That(window.SelectedTargetIsValidForTests, Is.True);
			Selection.activeGameObject = null;
			window.NotifyUnitySelectionChangedForTests();
			window.ProcessEditorUpdateForTests();
			Assert.That(window.SelectedTargetIsValidForTests, Is.False);
			Assert.That(window.SelectedSceneIsValidForTests, Is.False);
			Object.DestroyImmediate(window);
		}

		[Test]
		public void ClosedSceneDoesNotRetainInspectorHandles()
		{
			DebuggerRuntime.Initialize();
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			var scene = EditorSceneManager.NewPreviewScene();
			Selection.activeGameObject = m_Object;
			window.ProcessEditorUpdateForTests();
			Assert.That(window.SelectedTargetIsValidForTests, Is.True);
			EditorSceneManager.ClosePreviewScene(scene);
			Selection.activeGameObject = null;
			window.NotifySceneClosedForTests(scene);
			window.ProcessEditorUpdateForTests();
			Assert.That(window.SelectedTargetIsValidForTests, Is.False);
			Assert.That(window.SelectedSceneIsValidForTests, Is.False);
			Object.DestroyImmediate(window);
		}

		[Test]
		public void PlayModeTransitionsClearInspectorHandles()
		{
			DebuggerRuntime.Initialize();
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			Selection.activeGameObject = m_Object;
			window.ProcessEditorUpdateForTests();
			Assert.That(window.SelectedTargetIsValidForTests, Is.True);
			window.NotifyPlayModeStateForTests(PlayModeStateChange.EnteredPlayMode);
			Assert.That(window.SelectedTargetIsValidForTests, Is.False);
			Assert.That(window.SelectedSceneIsValidForTests, Is.False);
			window.NotifyPlayModeStateForTests(PlayModeStateChange.EnteredEditMode);
			Assert.That(window.SelectedTargetIsValidForTests, Is.False);
			Assert.That(window.SelectedSceneIsValidForTests, Is.False);
			Object.DestroyImmediate(window);
		}

		[Test]
		public void RecreatedWindowSynchronizesCurrentUnitySelection()
		{
			DebuggerRuntime.Initialize();
			var first = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			Selection.activeGameObject = m_Object;
			first.ProcessEditorUpdateForTests();
			Assert.That(first.SelectedTargetIsValidForTests, Is.True);
			Object.DestroyImmediate(first);
			var recreated = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			recreated.ProcessEditorUpdateForTests();
			Assert.That(recreated.SelectedTargetIsValidForTests, Is.True);
			Object.DestroyImmediate(recreated);
		}

		[Test]
		public void DisplaySubjectDetailReadsCompletedSnapshotWithoutEditorWorldRenderer()
		{
			DebuggerRuntime.Initialize();
			var subject = DebugDisplay.CreateSubject("EditorDisplay", m_Object);
			subject.SetMetadata("Health", 10);
			subject.DrawLine("EditorLine", Vector3.zero, Vector3.right, Color.green);
			DebuggerRuntime.CurrentSession.Display.Tick();
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			window.ProcessEditorUpdateForTests();
			var frame = window.CapturedDisplayFrameForTests;
			Assert.That(frame, Is.Not.Null);
			var snapshot = frame.Subjects.Single(item => item.Id == subject.Id);
			Assert.That(snapshot.Metadata.Single(item => item.Name == "Health").Value.TryGetSignedInteger(out var health), Is.True);
			Assert.That(health, Is.EqualTo(10));
			Assert.That(snapshot.Visuals.Select(item => item.Kind), Does.Contain(DebugDisplayPrimitiveKind.Line));
			Object.DestroyImmediate(window);
		}

		[Test]
		public void DiagnosticDetailIsAvailableForEditorInspectorReadFailure()
		{
			DebuggerRuntime.Initialize();
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			Selection.activeGameObject = m_Object;
			window.ProcessEditorUpdateForTests();
			var component = DebugInspector.GetComponents(window.SelectedTargetForTests).Components.First();
			var result = component.Component.ReadFields(new[] { default(DebugInspectorFieldId) });
			var diagnosticId = result.Value.Single().Snapshot.DiagnosticId;
			Assert.That(diagnosticId.IsValid, Is.True);
			window.RememberDiagnosticForTests(diagnosticId);
			Assert.That(window.TryGetDiagnosticDetailForTests(diagnosticId, out var detail), Is.True);
			Assert.That(detail.Summary.Code, Is.EqualTo("inspector-field-not-found"));
			Object.DestroyImmediate(window);
		}
#else
		[Test]
		public void DisabledEditorWindowDoesNotReadRuntimeSession()
		{
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			window.ProcessEditorUpdateForTests();
			Assert.That(window.SelectedTargetIsValidForTests, Is.False);
			Object.DestroyImmediate(window);
		}
#endif

#if DEXC_DEBUGGER
		[Test]
		public void SessionResetClearsEditorSelectionHandles()
		{
			DebuggerRuntime.Initialize();
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			Selection.activeGameObject = m_Object;
			window.ProcessEditorUpdateForTests();
			Assert.That(window.SelectedTargetIsValidForTests, Is.True);
			DebuggerRuntime.ResetForTests();
			window.ProcessEditorUpdateForTests();
			Assert.That(window.SelectedTargetIsValidForTests, Is.False);
			Assert.That(window.SelectedSceneIsValidForTests, Is.False);
			Object.DestroyImmediate(window);
		}

		[Test]
		public void SessionResetClearsCapturedDisplaySnapshot()
		{
			DebuggerRuntime.Initialize();
			DebugDisplay.DrawLine("EditorFrame", Vector3.zero, Vector3.right, Color.white);
			DebuggerRuntime.CurrentSession.Display.Tick();
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			window.ProcessEditorUpdateForTests();
			Assert.That(window.CapturedDisplayFrameForTests, Is.Not.Null);
			DebuggerRuntime.ResetForTests();
			window.ProcessEditorUpdateForTests();
			Assert.That(window.CapturedDisplayFrameForTests, Is.Null);
			Object.DestroyImmediate(window);
		}

		[Test]
		public void EditorWindowCommandSettingAndHistoryOperationsUseRuntimeFacades()
		{
			DebuggerRuntime.Initialize();
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();

			window.RefreshCommandPageForTests();
			var command = window.CommandItemsForTests.Single(item => item.Path == "Tests/Increment");
			Assert.That(window.SelectCommandForTests(command.Id), Is.True);
			Assert.That(window.SelectedCommandPathForTests, Is.EqualTo("Tests/Increment"));
			Assert.That(DebugCommand.Registry.TryGetDescriptor(command.Id, out var descriptor), Is.True);
			var input = descriptor.Fields.Single(field => field.IsInput);
			Assert.That(window.UpdateCommandInputForTests(input.Id, DebugValue.From(3)), Is.True);
			// Commands run outside Play Mode; the window no longer refuses them, and the run lands in history.
			Assert.That(window.CommandExecutionEnabledForTests, Is.True);
			Assert.That(window.ExecuteSelectedCommandForTests(), Is.Not.Null);
			window.RefreshHistoryForTests();
			Assert.That(window.CommandHistoryForTests, Is.Not.Null);
			Assert.That(window.CommandHistoryForTests.Entries, Has.Count.EqualTo(1));

			window.RefreshSettingPageForTests();
			var setting = DebugSetting.GetList(new DebugListQuery(string.Empty, "Tests/Volume", new DebugPageRequest(0, 8))).Items.Single();
			Assert.That(window.SelectSettingForTests(setting.Id), Is.True);
			Assert.That(window.SelectedSettingPathForTests, Is.EqualTo("Tests/Volume"));
			var write = window.WriteSelectedSettingForTests(DebugValue.From(5));
			Assert.That(write.Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			window.RefreshHistoryForTests();
			Assert.That(window.SettingHistoryForTests, Is.Not.Null);
			Assert.That(window.SettingHistoryForTests.Entries, Has.Count.EqualTo(1));

			Object.DestroyImmediate(window);
		}

		[Test]
		public void EditorWindowHistoryReuseRestoresDetailAndRunsOutsidePlayMode()
		{
			DebuggerRuntime.Initialize();
			var commands = DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, "Tests/Increment", new DebugPageRequest(0, 8)));
			var command = commands.Items.Single();
			Assert.That(DebugCommand.Registry.TryGetDescriptor(command.Id, out var descriptor), Is.True);
			var draft = DebugCommand.Draft.Create(command.Id);
			Assert.That(draft.TryGetDraftId(out var draftId), Is.True);
			var input = descriptor.Fields.Single(field => field.IsInput);
			Assert.That(
				DebugCommand.Draft.UpdateInput(draftId, input.Id, DebugValue.From(7)).Status,
				Is.EqualTo(DebugCommandDraftOperationStatus.Succeeded)
			);
			Assert.That(DebugCommand.Draft.CreateRequest(draftId).TryGetRequest(out var request), Is.True);
			Assert.That(request.Dispatch().Status, Is.EqualTo(CommandExecutionStatus.Succeeded));

			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			window.RefreshCommandPageForTests();
			Assert.That(window.SelectCommandForTests(command.Id), Is.True);
			window.RefreshHistoryForTests();
			var entry = window.CommandHistoryForTests.Entries.Single();
			Assert.That(window.ReuseCommandHistoryForTests(entry.Id), Is.True);
			Assert.That(window.SelectedDraftIsValidForTests, Is.True);
			Assert.That(window.SelectedCommandPathForTests, Is.EqualTo("Tests/Increment"));
			Assert.That(window.CommandValueForTests(input.Id), Is.EqualTo(DebugValue.From(7)));
			Assert.That(window.CommandNoticeForTests, Does.Contain("restored"));
			Assert.That(window.ExecuteSelectedCommandForTests(), Is.Not.Null, "a restored draft runs outside Play Mode");
			Object.DestroyImmediate(window);
		}

		[Test]
		public void EditorWindowSettingComparisonAndPresetPreviewUseRuntimeFacades()
		{
			DebuggerRuntime.Initialize();
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			window.RefreshSettingPageForTests();
			var setting = DebugSetting.GetList(new DebugListQuery(string.Empty, "Tests/Volume", new DebugPageRequest(0, 8))).Items.Single();
			Assert.That(window.SelectSettingForTests(setting.Id), Is.True);
			var presets = DebugSetting.GetPresets(setting.SetId, new DebugPageRequest(0, 8));
			Assert.That(presets.Items, Has.Count.EqualTo(1));
			Assert.That(window.SelectPresetForTests(presets.Items[0].Id), Is.True);
			Assert.That(window.PresetPreviewForTests, Is.Not.Null);
			Assert.That(window.PresetPreviewForTests.Value.Any(difference => difference.Path == "Tests/Volume"), Is.True);
			Assert.That(window.ApplyPresetForTests().Status, Is.EqualTo(window.PresetPreviewForTests.Status));
			Assert.That(DebugSetting.Reset(setting.Id).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Object.DestroyImmediate(window);
		}
#endif
	}
}
