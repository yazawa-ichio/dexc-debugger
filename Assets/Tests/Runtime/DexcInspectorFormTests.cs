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
	public sealed class DexcInspectorFormTests
	{
		DexcRuntimeUISkin m_Skin;
		readonly List<GameObject> m_Objects = new List<GameObject>();
		DexcUIR05Inspectable m_Target;
		DebugInspectorComponentHandle m_Handle;

		[SetUp]
		public void SetUp()
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
			Dexc.DexcDebugger.Initialize(options =>
			{
				options.RuntimeUI.Skin = m_Skin;
				options.Query.MaxPageSize = 2;
			});
			DexcRuntimeUIState.SetSession(Dexc.DexcDebugger.CurrentSession.SessionId);
			DebugMenu.Open();
			m_Target = NewObject("UIR05 target").AddComponent<DexcUIR05Inspectable>();
			OpenTarget();
		}

		GameObject NewObject(string name)
		{
			var value = new GameObject(name);
			m_Objects.Add(value);
			return value;
		}

		void OpenTarget()
		{
			DebugInspector.Register("UIR05/Target", m_Target, "UIR05 target");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("UIR05/Target", "", new DebugPageRequest(0, 2)))
				.Entries.First()
				.Target;
			DexcRuntimeUIState.InspectorTarget = target;
			m_Handle = DebugInspector
				.GetComponents(target)
				.Entries.Single(item => item.TypeName == typeof(DexcUIR05Inspectable).FullName)
				.Component;
			DexcRuntimeInspectorForm.Open(m_Handle, "UIR05 component");
		}

		[TearDown]
		public void TearDown()
		{
			DebugMenu.Close();
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(false);
			DexcRuntimeUIToolkitRenderer.DestroyForTests();
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(false);
			DexcRuntimeUGUIRenderer.DestroyForTests();
#endif
			DexcRuntimeUIState.ResetForSession();
			Dexc.DexcDebugger.ResetForTests();
			foreach (var value in m_Objects)
			{
				if (value != null)
				{
					Object.DestroyImmediate(value);
				}
			}

			m_Objects.Clear();
			Object.DestroyImmediate(m_Skin);
		}

		static DexcRuntimeInspectorForm.Field Field(string name) =>
			DexcRuntimeInspectorForm.Fields.Single(field => field.Descriptor.Name == name);

		static DexcUIRow Row(List<DexcUIRow> rows, string name, string component = null) =>
			rows.Single(row => row.Key == DexcRuntimeInspectorForm.Key(Field(name).Descriptor.Id, component));

		/// <summary>The page is checked as rows, once, without any renderer: what each renderer draws comes from these.</summary>
		[Test]
		public void FieldRowsDescribeThePageAndKeepEditsAsDraftsWithoutARenderer()
		{
			var rows = DexcRuntimeInspectorForm.FieldRows(false);
			Assert.That(rows.Any(row => row.Kind == DexcUIRowKind.Header), Is.False, "the component's name is in the header");
			Assert.That(DexcRuntimeUIRenderer.Breadcrumb(DexcRuntimeUIPage.InspectorFields).Last(), Is.EqualTo("UIR05 component"));

			var health = Row(rows, "Health");
			Assert.That(health.Kind, Is.EqualTo(DexcUIRowKind.Input));
			Assert.That(health.Text(), Is.EqualTo("100"));
			Assert.That(health.IsEnabled, Is.True);
			Assert.That(health.Auxiliary, Is.Not.Null, "every value can be opened in full");
			Assert.That(health.Confirmed, Is.Null, "inspector edits wait for Apply");
			Assert.That(Row(rows, "Locked").IsEnabled, Is.False);
			Assert.That(Row(rows, "Visible").Kind, Is.EqualTo(DexcUIRowKind.Choice));
			Assert.That(Row(rows, "Visible").Boolean, Is.True);
			var position = new[] { "x", "y", "z" }.Select(component => Row(rows, "Position", component)).ToArray();
			Assert.That(position[0].Label, Is.EqualTo(Field("Position").Label));
			Assert.That(position[2].Label, Is.Empty);

			health.Changed("150");
			Assert.That(health.Text(), Is.EqualTo("150"));
			Assert.That(Field("Health").Dirty, Is.True);
			Assert.That(m_Target.Health, Is.EqualTo(100), "nothing is written before Apply");
			DexcRuntimeInspectorForm.Apply();
			Assert.That(m_Target.Health, Is.EqualTo(150));
		}

		static void Select(string label) =>
			DexcRuntimeFormDetail.Select(DexcRuntimeFormDetail.ReadChoices().Items.Single(item => item.Label == label));

		[Test]
		public void SmallQueryLimitStillExposesAllFieldsAndPreservesIndependentInvalidText()
		{
			Assert.That(DexcRuntimeInspectorForm.Fields.Count, Is.EqualTo(12));
			DexcRuntimeInspectorForm.Change(Field("Health"), "-");
			DexcRuntimeInspectorForm.Change(Field("Speed"), "2.5");
			m_Target.Speed = 9;
			DexcRuntimeInspectorForm.Refresh();
			Assert.That(Field("Health").Raw, Is.EqualTo("-"));
			Assert.That(Field("Speed").Raw, Is.EqualTo("2.5"));
			Assert.That(DexcRuntimeInspectorForm.CanApply, Is.False);
			DexcRuntimeInspectorForm.Apply();
			Assert.That(m_Target.Health, Is.EqualTo(100));
			DexcRuntimeInspectorForm.Change(Field("Health"), "777");
			DexcRuntimeInspectorForm.Apply();
			Assert.That(m_Target.Health, Is.EqualTo(777));
			Assert.That(m_Target.Speed, Is.EqualTo(2.5f));
			Assert.That(DexcRuntimeInspectorForm.Fields.Any(field => field.Dirty), Is.False);
		}

		[Test]
		public void ExplicitEditingNeedsConfirmationAndNeverUnlocksReadOnlyOrConstants()
		{
			Assert.That(Field("Unregistered").Writable, Is.False);
			DexcRuntimeInspectorForm.Change(Field("Unregistered"), "8");
			Assert.That(Field("Unregistered").Dirty, Is.False);
			DexcRuntimeInspectorForm.RequestEditing();
			Assert.That(DexcRuntimeInspectorForm.ExplicitEditing, Is.False);
			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeInspectorForm.ExplicitEditing, Is.False);
			DexcRuntimeInspectorForm.RequestEditing();
			Select("Enable editing");
			Assert.That(Field("Unregistered").Writable, Is.True);
			foreach (var name in new[] { "Locked", "ReadOnly", "Fixed" })
			{
				Assert.That(Field(name).Writable, Is.False, name);
			}

			DexcRuntimeInspectorForm.Change(Field("Unregistered"), "8");
			DexcRuntimeInspectorForm.Apply();
			Assert.That(m_Target.Unregistered, Is.EqualTo(8));
			DexcRuntimeUIState.Back();
			OpenTarget();
			Assert.That(DexcRuntimeInspectorForm.ExplicitEditing, Is.False);
		}

		[Test]
		public void VectorsChoicesStringsAndReferencesUseTheSameDraftAndBackend()
		{
			DexcRuntimeInspectorForm.Change(Field("Position"), "-2.5", "x");
			DexcRuntimeInspectorForm.Change(Field("Position"), "3.5", "y");
			DexcRuntimeInspectorForm.Choose(Field("Visible"));
			DexcRuntimeInspectorForm.Choose(Field("Mode"));
			Select("Second");
			DexcRuntimeInspectorForm.OpenText(Field("Text"));
			DexcRuntimeFormDetail.ChangeText("Long\ntext");
			DexcRuntimeUIState.Back();
			DexcRuntimeInspectorForm.Choose(Field("Reference"));
			Select("Registered");
			Select("UIR05 target");
			Assert.That(m_Target.Reference, Is.Null);
			DexcRuntimeInspectorForm.Apply();
			Assert.That(m_Target.Position, Is.EqualTo(new Vector3(-2.5f, 3.5f, 0)));
			Assert.That(m_Target.Visible, Is.False);
			Assert.That(m_Target.Mode, Is.EqualTo(DexcUIR05Mode.Second));
			Assert.That(m_Target.Text, Is.EqualTo("Long\ntext"));
			Assert.That(m_Target.Reference, Is.EqualTo(m_Target.gameObject));
		}

		[Test]
		public void ComponentReferencesKeepExactIdentityInReadWriteAndStaleRejection()
		{
			var other = NewObject("Reference transform");
			DebugInspector.Register("UIR05/Reference", other, "Reference transform");
			var target = DebugInspector
				.GetRegistered(new DebugListQuery("UIR05/Reference", "", new DebugPageRequest(0, 2)))
				.Entries.Single()
				.Target;
			var reference = DexcInspectorRuntime.Registry.GetAssignableReferences(target, typeof(Transform).FullName).Single().Key;
			var field = Field("ComponentReference");
			DexcRuntimeInspectorForm.SelectReference(field, reference, "Reference transform");
			DexcRuntimeInspectorForm.Apply();
			Assert.That(m_Target.ComponentReference, Is.EqualTo(other.transform));
			Assert.That(field.Read.Reference, Is.EqualTo(reference));
			var frozen = field.Read;
			Object.DestroyImmediate(other);
			var replacement = NewObject("Reference transform");
			var result = m_Handle.WriteField(
				field.Descriptor.Id,
				DebugInspectorWriteValue.FromReference(reference),
				DebugInspectorWriteAccess.RegisteredOnly
			);
			Assert.That(result.Status, Is.EqualTo(DebugOperationStatus.Rejected));
			Assert.That(frozen.Reference, Is.EqualTo(reference));
			Assert.That(m_Target.ComponentReference == null, Is.True);
		}

		[Test]
		public void AnOpenedObjectThatIsDestroyedIsShownAsMissing()
		{
			var parent = NewObject("UIR05 doomed");
			NewObject("UIR05 doomed child").transform.SetParent(parent.transform);
			var scene = DebugInspector.GetScenes().Items.Single(item => item.Name == parent.scene.name);
			DexcRuntimeUIState.InspectorScene = scene.Scene;
			DexcRuntimeUIState.InspectorParent = default(DebugObjectHandle);
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorScene);
			DexcRuntimeInspectorNavigation.Row selected = null;
			for (var limit = 0; limit < 100 && selected == null; limit++)
			{
				var rows = DexcRuntimeInspectorNavigation.Rows();
				selected = rows.FirstOrDefault(row => row.Label == "› UIR05 doomed");
				if (selected == null)
					rows.First(row => row.Key == "next").Action();
			}
			selected.Action();
			Assert.That(DexcRuntimeInspectorNavigation.Rows().Any(row => row.Label == "UIR05 doomed child"), Is.True);

			Object.DestroyImmediate(parent);
			DexcRuntimeInspectorNavigation.Refresh();
			Assert.That(DexcRuntimeInspectorNavigation.Message, Does.EndWith("Missing"));
			Assert.That(DexcRuntimeInspectorNavigation.Rows().Any(row => row.Key == "components"), Is.False);
		}

		[UnityTest]
		public IEnumerator SceneHierarchyPagesBackAndSearchKeepsTheBackendBudget()
		{
			var parent = NewObject("UIR05 parent");
			for (var index = 0; index < 7; index++)
			{
				NewObject("UIR05 child " + index).transform.SetParent(parent.transform);
			}

			var scene = DebugInspector.GetScenes().Items.Single(item => item.Name == parent.scene.name);
			DexcRuntimeUIState.InspectorScene = scene.Scene;
			DexcRuntimeUIState.InspectorParent = default(DebugObjectHandle);
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorScene);
			DexcRuntimeInspectorNavigation.Row selected = null;
			for (var limit = 0; limit < 100 && selected == null; limit++)
			{
				var rows = DexcRuntimeInspectorNavigation.Rows();
				selected = rows.FirstOrDefault(row => row.Label == "› UIR05 parent");
				if (selected != null)
				{
					break;
				}

				var next = rows.FirstOrDefault(row => row.Key == "next");
				Assert.That(next, Is.Not.Null);
				next.Action();
			}

			selected.Action();
			Assert.That(DexcRuntimeInspectorNavigation.Rows().Any(row => row.Key == "components"), Is.True);
			var children = new List<string>();
			while (true)
			{
				var rows = DexcRuntimeInspectorNavigation.Rows();
				children.AddRange(rows.Where(row => row.Label.StartsWith("UIR05 child")).Select(row => row.Label));
				var next = rows.FirstOrDefault(row => row.Key == "next");
				if (next == null)
				{
					break;
				}

				next.Action();
			}

			Assert.That(children.Distinct().Count(), Is.EqualTo(7));
			DexcRuntimeUIState.Back();
			Assert.That(DexcRuntimeUIState.InspectorParent.IsValid, Is.False);
			DexcRuntimeInspectorNavigation.ChangeSearch("UIR05 child");
			for (var frame = 0; frame < 10; frame++)
			{
				yield return null;
			}

			DexcRuntimeInspectorNavigation.Refresh();
			Assert.That(DexcRuntimeInspectorNavigation.Rows().Count(row => row.Label.Contains("UIR05 child")), Is.EqualTo(2));
			var id = DexcRuntimeUIState.InspectorSearch;
			DexcRuntimeInspectorNavigation.ChangeSearch("");
			Assert.That(DexcRuntimeUIState.InspectorSearch.IsValid, Is.False);
			Assert.That(id.IsValid, Is.True);
		}

		[Test]
		public void HelperPreservesDraftButBackCloseAndStaleCallbacksCannotWrite()
		{
			var old = Field("Health");
			DexcRuntimeInspectorForm.Change(old, "-");
			DexcRuntimeFormDetail.Open(old.Error);
			DexcRuntimeUIState.Back();
			Assert.That(old.Raw, Is.EqualTo("-"));
			DexcRuntimeUIState.Back();
			DexcRuntimeInspectorForm.Change(old, "500");
			OpenTarget();
			Assert.That(Field("Health").Raw, Is.EqualTo("100"));
			DexcRuntimeInspectorForm.Change(Field("Health"), "500");
			DebugMenu.Close();
			DexcRuntimeInspectorForm.Apply();
			Assert.That(m_Target.Health, Is.EqualTo(100));
		}

		[Test]
		public void DestroyedComponentLeavesMissingReasonAndCannotWriteToSameNamedReplacement()
		{
			var old = Field("Health");
			DexcRuntimeInspectorForm.Change(old, "777");
			Object.DestroyImmediate(m_Target.gameObject);
			var replacement = NewObject("UIR05 target").AddComponent<DexcUIR05Inspectable>();
			DexcRuntimeInspectorForm.Refresh();
			DexcRuntimeInspectorForm.Change(old, "999");
			DexcRuntimeInspectorForm.Apply();
			Assert.That(DexcRuntimeUIState.Page, Is.Not.EqualTo(DexcRuntimeUIPage.InspectorFields));
			Assert.That(DexcRuntimeUIState.Notice, Does.Contain("Missing"));
			Assert.That(replacement.Health, Is.EqualTo(100));
		}

		[Test]
		public void WatchSurvivesSelectionAndTargetLossAndRemovalNeedsAnExplicitAction()
		{
			var field = Field("Health");
			DexcRuntimeInspectorForm.OpenWatchSelection();
			Select("Health");
			DexcRuntimeInspectorForm.OpenWatchSelection();
			Select("Health");
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorWatches);
			var watch = DebugInspector.GetWatches(new DebugPageRequest(0, 2)).Entries.Single();
			DexcRuntimeInspectorNavigation.Rows().Single().Action();
			Assert.That(DebugInspector.GetWatches(new DebugPageRequest(0, 2)).Entries.Count, Is.EqualTo(1));
			DexcRuntimeUIState.Back();
			Object.DestroyImmediate(m_Target.gameObject);
			DexcInspectorRuntime.Registry.Tick();
			DexcRuntimeInspectorNavigation.Refresh();
			Assert.That(DexcRuntimeInspectorNavigation.Rows().Single().Label, Does.Contain("Missing"));
			Assert.That(DexcRuntimeInspectorNavigation.Rows().Single().Label, Does.Contain("Health"));
			DexcRuntimeInspectorNavigation.Rows().Single().Action();
			Select("Remove watch");
			Assert.That(DebugInspector.GetWatches(new DebugPageRequest(0, 2)).Entries, Is.Empty);
		}

		[Test]
		public void RegisteredAndHistoryPagingReachItemsBeyondOldLeadingDefinitions()
		{
			for (var index = 0; index < 7; index++)
			{
				DebugInspector.Register("UIR05/Many/" + index, NewObject("UIR05 " + index), "entry " + index);
			}

			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorRegistered);
			DexcRuntimeInspectorNavigation.ChangeSearch("UIR05/Many");
			var labels = new List<string>();
			while (true)
			{
				var rows = DexcRuntimeInspectorNavigation.Rows();
				labels.AddRange(rows.Where(row => row.Key != "next" && row.Key != "previous").Select(row => row.Label));
				var next = rows.FirstOrDefault(row => row.Key == "next");
				if (next == null)
				{
					break;
				}

				next.Action();
			}

			Assert.That(labels.Distinct().Count(), Is.EqualTo(7));
			OpenTarget();
			for (var index = 0; index < 7; index++)
			{
				DexcRuntimeInspectorForm.Change(Field("Health"), (200 + index).ToString());
				DexcRuntimeInspectorForm.Apply();
			}

			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.History);
			DexcRuntimeHistory.Filter = "Inspector";
			DexcRuntimeInspectorNavigation.Refresh();
			var first = DexcRuntimeHistory.Read().First();
			Assert.That(first.Detail, Does.Contain("206"));
			Object.DestroyImmediate(m_Target.gameObject);
			Assert.That(DexcRuntimeHistory.Read().First().Detail, Is.EqualTo(first.Detail));
			var seen = new HashSet<string>();
			while (true)
			{
				var rows = DexcRuntimeInspectorNavigation.Rows();
				foreach (var row in rows.Where(row => row.Key != "filter" && row.Key != "next" && row.Key != "previous"))
				{
					seen.Add(row.Key);
				}

				var next = rows.FirstOrDefault(row => row.Key == "next");
				if (next == null)
				{
					break;
				}

				next.Action();
			}

			Assert.That(seen.Count, Is.EqualTo(7));
		}

		[Test]
		public void CommandHistoryDetailsAndOpeningInputsNeverExecute()
		{
			DexcUIR05Command.Executions = 0;
			var command = DebugCommand
				.Registry.GetList(new DebugListQuery("Tests/UIR05/History", "", new DebugPageRequest(0, 2)))
				.Items.Single();
			DexcRuntimeUIRenderer.SelectCommand(command);
			DexcRuntimeCommandExecution.Request();
			Assert.That(DexcUIR05Command.Executions, Is.EqualTo(1));
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.History);
			DexcRuntimeHistory.Filter = "Command";
			var entry = DexcRuntimeHistory.Read().Single();
			DexcRuntimeHistory.Open(entry);
			Assert.That(DexcUIR05Command.Executions, Is.EqualTo(1));
			Select("Open inputs");
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
			Assert.That(DexcUIR05Command.Executions, Is.EqualTo(1));
		}

		[UnityTest]
		public IEnumerator UIToolkitInputsApplyToActualInspectorFields() => NativeInputs(true);

		[UnityTest]
		public IEnumerator UGUIInputsApplyToActualInspectorFields() => NativeInputs(false);

		IEnumerator NativeInputs(bool toolkit)
		{
#if !DEXC_HAS_UGUI
			if (!toolkit)
				Assert.Ignore("uGUI not installed.");
#endif
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(toolkit);
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(!toolkit);
			if (!toolkit)
			{
				NewObject("UIR05 events").AddComponent(DexcUGUIApi.EventSystemType);
			}

#endif
			Render(toolkit ? "UIToolkit" : "UGUI");
			for (var frame = 0; frame < 8; frame++)
			{
				yield return null;
			}

			var health = Field("Health");
			var speed = Field("Speed");
			if (toolkit)
			{
				DexcRuntimeUIToolkitRenderer.InspectorInputForTests(health.Descriptor.Id).value = "-";
				DexcRuntimeUIToolkitRenderer.InspectorInputForTests(speed.Descriptor.Id).value = "3.5";
				Assert.That(DexcRuntimeUIToolkitRenderer.SafeAreaRootForTests.Q<Button>("FooterPrimary").enabledSelf, Is.False);
				DexcRuntimeUIToolkitRenderer.InspectorInputForTests(health.Descriptor.Id).value = "777";
				DexcRuntimeUIState.SetSelectedIndex(DexcRuntimeUIToolkitRenderer.PrimaryActionIndexForTests);
				DexcRuntimeUIToolkitRenderer.ActivateSelectedForTests();
			}
#if DEXC_HAS_UGUI
			else
			{
				DexcUGUIApi.Set(DexcRuntimeUGUIRenderer.InspectorInputForTests(health.Descriptor.Id), "text", "-");
				DexcUGUIApi.Set(DexcRuntimeUGUIRenderer.InspectorInputForTests(speed.Descriptor.Id), "text", "3.5");
				Assert.That(DexcRuntimeInspectorForm.CanApply, Is.False);
				DexcUGUIApi.Set(DexcRuntimeUGUIRenderer.InspectorInputForTests(health.Descriptor.Id), "text", "777");
				var button = DexcRuntimeUGUIRenderer.SelectableForTests(DexcRuntimeUGUIRenderer.PrimaryActionIndexForTests);
				var click = DexcUGUIApi.Get(button, "onClick");
				click.GetType().GetMethod("Invoke").Invoke(click, null);
			}

#endif
			Assert.That(m_Target.Health, Is.EqualTo(777));
			Assert.That(m_Target.Speed, Is.EqualTo(3.5f));
			if (toolkit)
			{
				var input = DexcRuntimeUIToolkitRenderer.InspectorInputForTests(Field("Position").Descriptor.Id, "x");
				Assert.That(input.label, Is.EqualTo("X"));
				Assert.That(input.Q<VisualElement>(className: "unity-base-text-field__input").resolvedStyle.width, Is.GreaterThan(100));
			}
		}

		[UnityTest]
		public IEnumerator UIToolkitSearchDoesNotRebuildWhileTyping() => NativeSearch(true);

		[UnityTest]
		public IEnumerator UGUISearchDoesNotRebuildWhileTyping() => NativeSearch(false);

		IEnumerator NativeSearch(bool toolkit)
		{
#if !DEXC_HAS_UGUI
			if (!toolkit)
				Assert.Ignore("uGUI not installed.");
#endif
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorRegistered);
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(toolkit);
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(!toolkit);
			if (!toolkit)
			{
				NewObject("UIR05 events").AddComponent(DexcUGUIApi.EventSystemType);
			}

#endif
			Render(toolkit ? "UIToolkit" : "UGUI");
			for (var frame = 0; frame < 8; frame++)
			{
				yield return null;
			}

			if (toolkit)
			{
				var input = DexcRuntimeUIToolkitRenderer
					.SafeAreaRootForTests.Query<TextField>()
					.ToList()
					.Single(item => item.label == "Search");
				input.Focus();
				yield return null;
				input.value = "UIR05";
				DebugInspector.Register("UIR05/Other", NewObject("Other search target"), "Other");
				DexcRuntimeInspectorNavigation.Refresh();
				for (var frame = 0; frame < 4; frame++)
				{
					yield return null;
				}

				Assert.That(input.panel, Is.Not.Null);
				Assert.That(input.value, Is.EqualTo("UIR05"));
				Assert.That(DexcRuntimeInspectorNavigation.Search, Is.Empty);
				input.value = "UIR05/Target";
				input.Blur();
			}
#if DEXC_HAS_UGUI
			else
			{
				var input = DexcRuntimeUGUIRenderer.RootForTests.GetComponentsInChildren(DexcUGUIApi.InputFieldType).Single();
				DexcUGUIApi.SetSelected(input.gameObject);
				input.GetType().GetMethod("ActivateInputField").Invoke(input, null);
				for (var frame = 0; frame < 4; frame++)
				{
					yield return null;
				}

				DexcUGUIApi.Set(input, "text", "UIR05");
				DebugInspector.Register("UIR05/Other", NewObject("Other search target"), "Other");
				DexcRuntimeInspectorNavigation.Refresh();
				for (var frame = 0; frame < 4; frame++)
				{
					yield return null;
				}

				Assert.That((string)DexcUGUIApi.Get(input, "text"), Is.EqualTo("UIR05"));
				Assert.That(DexcRuntimeInspectorNavigation.Search, Is.Empty);
				DexcUGUIApi.Set(input, "text", "UIR05/Target");
				input.GetType().GetMethod("DeactivateInputField").Invoke(input, null);
				DexcUGUIApi.SetSelected(null);
			}

#endif
			for (var frame = 0; frame < 4; frame++)
			{
				yield return null;
			}

			Assert.That(DexcRuntimeInspectorNavigation.Search, Is.EqualTo("UIR05/Target"));
			Assert.That(DexcRuntimeInspectorNavigation.Rows().Single().Label, Is.EqualTo("UIR05 target"));
		}

		void Render(string renderer)
		{
			var safe = new Rect(0, 0, Screen.width, Screen.height);
			if (renderer == "UIToolkit")
			{
				DexcRuntimeUIToolkitRenderer.RenderProfileForTests(Screen.width, Screen.height, safe, DexcRuntimeUIProfileKind.Windows);
			}
#if DEXC_HAS_UGUI
			else if (renderer == "UGUI")
			{
				DexcRuntimeUGUIRenderer.RenderProfileForTests(Screen.width, Screen.height, safe, DexcRuntimeUIProfileKind.Windows);
			}
#endif
			else
			{
				DexcRuntimeUIRenderer.SetProfileOverrideForTests(DexcRuntimeUIProfileKind.Windows);
			}
		}

		[UnityTest]
		public IEnumerator RemainingRuntimeCaptures()
		{
			if (Application.isBatchMode)
			{
				Assert.Ignore("Requires automatic --graphics runner.");
			}

			foreach (var renderer in new[] { "IMGUI", "UIToolkit", "UGUI" })
			{
#if !DEXC_HAS_UGUI
				if (renderer == "UGUI")
					continue;
#endif
				TearDown();
				DexcRuntimeBootstrap.InitializeForTests();
				SetUp();
				var camera = NewObject("UIR05 camera").AddComponent<Camera>();
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = Color.black;
				camera.cullingMask = 0;
				DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(renderer == "UIToolkit");
#if DEXC_HAS_UGUI
				DexcRuntimeUIRendererSelection.SetUseUGUIForTests(renderer == "UGUI");
#endif
				foreach (
					var state in new[]
					{
						"fields",
						"collection",
						"collection-detail",
						"error",
						"watch",
						"history",
						"home",
						"display-setting",
					}
				)
				{
					if (state == "collection")
					{
						var collection = NewObject("UIR05 collection").AddComponent<DexcCollectionFixture>();
						collection.Values = Enumerable.Range(0, 129).ToList();
						DebugInspector.Register("UIR05/Collection", collection, "Collection");
						var target = DebugInspector
							.GetRegistered(new DebugListQuery("UIR05/Collection", "", new DebugPageRequest(0, 2)))
							.Entries.Single()
							.Target;
						var component = DebugInspector
							.GetComponents(target)
							.Components.Single(item => item.TypeName == typeof(DexcCollectionFixture).FullName)
							.Component;
						DexcRuntimeUIState.InspectorTarget = target;
						DexcRuntimeInspectorForm.Open(component);
						var field = DexcRuntimeInspectorForm.Fields.Single();
						Assert.That(field.Writable, Is.False);
						Assert.That(field.Rows, Is.EqualTo(1));
						Assert.That(field.Raw, Is.EqualTo("129 items (first 128)"));
					}

					if (state == "collection-detail")
					{
						DexcRuntimeInspectorForm.OpenText(DexcRuntimeInspectorForm.Fields.Single());
						Assert.That(DexcRuntimeFormDetail.Message, Does.Contain("[127] Value · 127"));
						Assert.That(DexcRuntimeFormDetail.Message, Does.Not.Contain("[128]"));
					}

					if (state == "error")
					{
						OpenTarget();
						DexcRuntimeInspectorForm.Change(Field("Health"), "-");
					}

					if (state == "watch")
					{
						DexcRuntimeInspectorForm.OpenWatchSelection();
						Select("Health");
						DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorWatches);
					}

					if (state == "history")
					{
						OpenTarget();
						DexcRuntimeInspectorForm.Change(Field("Health"), "200");
						DexcRuntimeInspectorForm.Apply();
						DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.History);
					}

					if (state == "home")
					{
						DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
					}

					if (state == "display-setting")
					{
						var setting = DebugSetting.GetList(new DebugListQuery("Display/HUD", "", new DebugPageRequest(0, 2))).Items.First();
						DexcRuntimeSettingForm.Open(setting);
						Assert.That(DexcRuntimeSettingForm.Fields.Count, Is.EqualTo(3));
						var hud = DexcRuntimeSettingForm.Fields.Single(field => field.Item.Path == "Display/HUD");
						DexcRuntimeSettingForm.Choose(hud);
						Assert.That(DexcDisplayVisibility.Capture().Layer("HUD"), Is.False);
						Assert.That(DexcDisplayVisibility.Capture().Layer("Overlay"), Is.True);
					}

					Render(renderer);
					for (var frame = 0; frame < 8; frame++)
					{
						yield return null;
					}

					yield return new WaitForEndOfFrame();
					var texture = ScreenCapture.CaptureScreenshotAsTexture();
					try
					{
						var safe = new Rect(0, 0, Screen.width, Screen.height);
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
						Directory.CreateDirectory("Logs/UnityTests/UIR05");
						File.WriteAllBytes("Logs/UnityTests/UIR05/" + renderer + "-" + state + ".png", texture.EncodeToPNG());
					}
					finally
					{
						Object.DestroyImmediate(texture);
					}
				}
			}
		}
	}

	public enum DexcUIR05Mode
	{
		First,
		Second,
	}

	public sealed class DexcUIR05Inspectable : MonoBehaviour
	{
		[DebugField]
		public int Health = 100;

		[DebugField]
		public float Speed = 1;

		[DebugField]
		public Vector3 Position;

		[DebugField]
		public bool Visible = true;

		[DebugField]
		public DexcUIR05Mode Mode;

		[DebugField]
		public string Text = "Sample";

		[DebugField]
		public GameObject Reference;

		[DebugField]
		public Transform ComponentReference;

		[DebugField(Access = DebugFieldAccess.Locked)]
		public int Locked = 10;

		[DebugField(Access = DebugFieldAccess.ReadOnly)]
		public int ReadOnly = 20;
		public int Unregistered = 1;
		public readonly int Fixed = 3;
	}

	[DebugCommandAttribute("Tests/UIR05/History", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DexcUIR05Command : DebugCommand
	{
		internal static int Executions;
		public InputInt Amount = new InputInt(7);

		protected override void Execute()
		{
			Executions++;
		}
	}
}
#endif
