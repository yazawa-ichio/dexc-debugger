#if DEXC_DEBUGGER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Object = UnityEngine.Object;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	[TestFixture]
	public sealed class DexcRuntimeUIToolkitTests
	{
		const string PanelSettingsPath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitPanelSettings.asset";
		const string ThemePath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitTheme.tss";
		const string WorldVisualShaderPath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader";
		DexcRuntimeUISkin m_Skin;

		[SetUp]
		public void SetUp()
		{
			RuntimeUIToolkitManyInputsCommand.Reject = false;
			DexcRuntimeUIState.ResetForSession();
			DebuggerRuntime.ResetForTests();
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(false);
			DexcRuntimeUIToolkitRenderer.DestroyForTests();
			m_Skin = CreateVisualSkin();
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			var session = DebugCommand
				.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 1)))
				.Summary.SessionId;
			DexcRuntimeUIState.SetSession(session);
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(true);
			DexcRuntimeUIToolkitRenderer.CreateForBootstrap();
			DebugMenu.Open();
			DexcRuntimeUIToolkitRenderer.RenderProfileForTests(390, 844, new Rect(0f, 0f, 390f, 844f));
		}

		[TearDown]
		public void TearDown()
		{
			if (DexcRuntimeUIState.AsyncExecution != null)
				DexcRuntimeUIState.AsyncExecution.Cancel();
			DebugMenu.Close();
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(false);
			DexcRuntimeUIToolkitRenderer.ClearProfileOverrideForTests();
			DexcRuntimeUIToolkitRenderer.DestroyForTests();
			DexcRuntimeUIState.ResetForSession();
			DebuggerRuntime.ResetForTests();
			if (m_Skin != null)
				Object.DestroyImmediate(m_Skin);
			m_Skin = null;
		}

		[Test]
		public void RendererReportsIMGUIWhileUIToolkitFallsBack()
		{
			Assert.That(DebugMenu.Renderer, Is.EqualTo(DebugMenuRenderer.UIToolkit));
			DebugMenu.Close();
			DexcRuntimeUIToolkitRenderer.DestroyForTests();
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize();
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(true);
			DexcRuntimeUIToolkitRenderer.CreateForBootstrap();

			Assert.That(DexcRuntimeUIToolkitRenderer.IsActive, Is.False);
			Assert.That(DebugMenu.Renderer, Is.EqualTo(DebugMenuRenderer.IMGUI));
		}

		/// <summary>Home reopened looks the same to the signature, but what it lists has changed while it was closed.</summary>
		[UnityTest]
		public IEnumerator ReopeningOnHomeShowsWhatChangedWhileItWasClosed()
		{
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
			DexcRuntimeUIToolkitRenderer.TickActive();
			yield return null;
			Assert.That(HomeTexts(), Has.No.Member("Command · Tests/UIR03"));

			DebugMenu.Close();
			DexcRuntimeUIToolkitRenderer.TickActive();
			yield return null;
			DexcRuntimeFavorites.Toggle(DexcFavoriteKind.Command, "Tests/UIR03/Form");
			try
			{
				DebugMenu.Open();
				DexcRuntimeUIToolkitRenderer.TickActive();
				yield return null;
				Assert.That(HomeTexts(), Has.Member("Command · Tests/UIR03"), "the new favorite is listed");
			}
			finally
			{
				DexcRuntimeFavorites.Toggle(DexcFavoriteKind.Command, "Tests/UIR03/Form");
			}
		}

		static string[] HomeTexts() =>
			DexcRuntimeUIToolkitRenderer
				.ContentListForTests.Query<Label>()
				.ToList()
				.Where(label => label.resolvedStyle.display != DisplayStyle.None)
				.Select(label => label.text ?? string.Empty)
				.ToArray();

		[UnityTest]
		public IEnumerator NativeKeysMoveSubmitAndCancelWithoutProvider()
		{
			Assert.That(DexcRuntimeUIState.InputProvider, Is.Null);
			DebugMenu.Open();
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
			DexcRuntimeUIToolkitRenderer.RefreshForTests();
			yield return null;
			DexcRuntimeUIToolkitRenderer.TickActive();
			Debug.Log(
				"UI06 F12 UITK before: open="
					+ DexcRuntimeUIState.IsOpen
					+ ", page="
					+ DexcRuntimeUIState.Page
					+ ", count="
					+ DexcRuntimeUIToolkitRenderer.SelectionCount
					+ ", active="
					+ DexcRuntimeUIToolkitRenderer.IsActive
					+ ", field="
					+ DexcRuntimeUIToolkitRenderer.FocusedTextFieldForTests
			);
			DexcRuntimeUIToolkitRenderer.SendKeyForTests(KeyCode.DownArrow);
			Assert.That(DexcRuntimeUIState.SelectedIndex, Is.EqualTo(1));
			DexcRuntimeUIToolkitRenderer.SendKeyForTests(KeyCode.Return);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.SettingList));
			DexcRuntimeUIToolkitRenderer.SendKeyForTests(KeyCode.Escape);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));
			DexcRuntimeUIToolkitRenderer.SendKeyForTests(KeyCode.Space);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandList));
		}

		[UnityTest]
		public IEnumerator ProductionEventsCompletePointerGamepadKeyboardHistoryAndCloseFlow()
		{
			var evidence = new EventPathEvidence();
			yield return ExerciseProductionEventPaths(evidence);
			Assert.That(evidence.PointerEventPath, Is.True);
			Assert.That(evidence.ProviderNavigationPath, Is.True);
			Assert.That(evidence.ProviderSubmitPath, Is.True);
			Assert.That(evidence.ProviderCancelPath, Is.True);
			Assert.That(evidence.KeyboardEventPath, Is.True);
			Assert.That(evidence.TextFocusLifecycle, Is.True);
			Assert.That(evidence.CloseFocusReturn, Is.True);
		}

		[UnityTest]
		public IEnumerator SemanticPointerFocusAndDisabledStatesUseDistinctProductionVisuals()
		{
			var palette = DexcRuntimeUIToolkitRenderer.SemanticPaletteForTests;
			var row = FindRow("Inspector");
			SendPointerEnter(row);
			AssertColor(row.resolvedStyle.backgroundColor, palette.Selection);
			SendPointerDown(row);
			AssertColor(row.resolvedStyle.backgroundColor, palette.Accent);
			SendPointerLeave(row);
			var rowIndex = Enumerable
				.Range(0, DexcRuntimeUIToolkitRenderer.SelectableCount)
				.First(index =>
					DexcRuntimeUIToolkitRenderer.SelectableTextForTests(index).StartsWith("Inspector", StringComparison.Ordinal)
				);
			DexcRuntimeUIToolkitRenderer.SelectForTests(rowIndex);
			yield return null;
			yield return null;
			Assert.That(DexcRuntimeUIToolkitRenderer.FocusMarkerCountForTests, Is.EqualTo(1));
			Assert.That(row.resolvedStyle.borderLeftWidth, Is.EqualTo(0f).Within(0.01f));
			SelectCommand("UI03/LongCommand");
			yield return null;
			var count = FindTextField("Count");
			count.Focus();
			count.value = "-1";
			yield return null;
			Assert.That(DexcRuntimeUIToolkitRenderer.ExecuteInteractableForTests, Is.False);
			Assert.That(Require<Button>("FooterPrimary").resolvedStyle.backgroundColor, Is.EqualTo(palette.Disabled));
			Assert.That(
				Enumerable
					.Range(0, DexcRuntimeUIToolkitRenderer.SelectableCount)
					.Select(DexcRuntimeUIToolkitRenderer.SelectableTextForTests),
				Has.None.EqualTo("Execute")
			);
			RuntimeUIToolkitManyInputsCommand.Reject = true;
			DexcRuntimeUIToolkitRenderer.RefreshForTests();
			yield return null;
			// The list builds only the rows in view; the field sits below the first screen.
			DexcRuntimeUIToolkitRenderer.ScrollCommandFieldForTests(
				DexcRuntimeUIState.Command.Fields.Single(item => item.Name == "DestinationSceneSourcePath").Id
			);
			yield return null;
			var errorRow = FindRow("DestinationSceneSourcePath");
			// A field's row sits on the band a step lighter than the panel.
			AssertColor(errorRow.resolvedStyle.backgroundColor, palette.Header);
			// The message is written under the value; the "…" action keeps the row's colours.
			AssertColor(errorRow.Q<Button>("FieldError").resolvedStyle.backgroundColor, palette.Row);
			Assert.That(errorRow.Q<Label>("RowError").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
			AssertColor(errorRow.Q<Label>("RowError").resolvedStyle.color, palette.FieldErrorText);
		}

		[UnityTest]
		public IEnumerator NativeTouchCoordinatesActivateOnceAtReferenceDoubleAndActualAndroidScale()
		{
			foreach (var size in new[] { new Vector2Int(390, 844), new Vector2Int(780, 1688), new Vector2Int(1440, 2960) })
			{
				DebugMenu.Open();
				DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
				var safe = new Rect(0f, 0f, size.x, size.x == 1440 ? 2789f : size.y);
				DexcRuntimeUIToolkitRenderer.RenderProfileForTests(size.x, size.y, safe, DexcRuntimeUIProfileKind.AndroidPortrait);
				yield return null;
				yield return null;
				TapNativeTouch(FindRow("Command").Q<Label>("RowLabel"));
				yield return null;
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandList), size.ToString());
				DexcRuntimeUIState.Search = "UI03/LongCommand";
				DexcRuntimeUIToolkitRenderer.RefreshForTests();
				yield return null;
				yield return null;
				TapNativeTouch(FindRow("Tests/RuntimeUI/UI03/LongCommand").Q<Label>("RowLabel"));
				yield return null;
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
				FindTextField("Count").value = "-1";
				yield return null;
				TapNativeTouch(Require<Button>("FooterPrimary"));
				Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Null, "disabled release cannot execute");
				FindTextField("Count").value = "24";
				yield return null;
				var primary = Require<Button>("FooterPrimary");
				SendNativeTouch(primary.panel, primary.worldBound.center, TouchPhase.Began);
				SendNativeTouch(primary.panel, new Vector2(-10f, -10f), TouchPhase.Ended);
				Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Null, "release outside cannot execute");
				TapNativeTouch(primary);
				yield return null;
				Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Not.Null);
				var result = DexcRuntimeUIState.LastCommandResult;
				TapNativeTouch(Require<Button>("History"));
				yield return null;
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.FormDetail));
				Assert.That(DexcRuntimeUIState.LastCommandResult, Is.SameAs(result), "opening history must not execute");
				TapNativeTouch(Require<Button>("Back"));
				yield return null;
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
				TapNativeTouch(Require<Button>("Back"));
				yield return null;
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandList));
				TapNativeTouch(Require<Button>("Back"));
				yield return null;
				Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));
				TapNativeTouch(Require<Button>("Close"));
				yield return null;
				Assert.That(DexcRuntimeUIState.IsOpen, Is.False);
			}
		}

		[UnityTest]
		public IEnumerator NativeTouchScrollGestureDoesNotActivateReleaseRow()
		{
			yield return null;
			yield return null;
			var row = FindRow("Command");
			var panel = row.panel;
			var start = row.worldBound.center;
			SendNativeTouch(panel, start, TouchPhase.Began);
			SendNativeTouch(panel, start + Vector2.down * 90f, TouchPhase.Moved);
			SendNativeTouch(panel, start, TouchPhase.Moved);
			SendNativeTouch(panel, start, TouchPhase.Ended);
			yield return null;
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));
		}

		static void TapNativeTouch(VisualElement element)
		{
			Assert.That(element, Is.Not.Null);
			SendNativeTouch(element.panel, element.worldBound.center, TouchPhase.Began);
			SendNativeTouch(element.panel, element.worldBound.center, TouchPhase.Ended);
		}

		static void SendNativeTouch(IPanel panel, Vector2 position, TouchPhase phase)
		{
			var touch = new Touch
			{
				fingerId = 0,
				position = position,
				rawPosition = position,
				phase = phase,
				type = TouchType.Direct,
				tapCount = 1,
			};
			var target = panel.Pick(position) ?? panel.visualTree;
			if (phase == TouchPhase.Began)
			{
				using (var pointer = PointerDownEvent.GetPooled(touch, EventModifiers.None))
				{
					pointer.target = target;
					target.SendEvent(pointer);
				}
			}
			else if (phase == TouchPhase.Moved)
			{
				using (var pointer = PointerMoveEvent.GetPooled(touch, EventModifiers.None))
				{
					pointer.target = target;
					target.SendEvent(pointer);
				}
			}
			else
			{
				using (var pointer = PointerUpEvent.GetPooled(touch, EventModifiers.None))
				{
					Assert.That(pointer.originalMousePosition, Is.EqualTo(Vector2.zero), "native Touch has no mouse event coordinates");
					pointer.target = target;
					target.SendEvent(pointer);
				}
			}
		}

		[UnityTest]
		public IEnumerator TwelveFieldsAreVirtualizedAndValidationKeepsTextFocusFixedFooterAndNoHorizontalOverflow()
		{
			var provider = new RecordingProvider();
			DebugMenu.SetInputProvider(provider);
			SelectCommand("UI03/LongCommand");
			yield return null;
			yield return null;
			var rows = Enumerable
				.Range(0, DexcRuntimeUIToolkitRenderer.VisibleRowCount)
				.Select(DexcRuntimeUIToolkitRenderer.RowTextForTests)
				.ToArray();
			foreach (var field in DexcRuntimeUIState.Command.Fields.Where(field => field.IsInput))
				Assert.That(rows.Any(text => text.StartsWith(field.Label, StringComparison.Ordinal)), Is.True, field.Label);
			Assert.That(DexcRuntimeUIToolkitRenderer.VisibleRowCount, Is.LessThanOrEqualTo(DexcRuntimeUIState.PageSize));
			Assert.That(
				DexcRuntimeUIToolkitRenderer.ResolvedRowCountForTests,
				Is.LessThanOrEqualTo(DexcRuntimeUIToolkitRenderer.VisibleRowCount)
			);
			Assert.That(DexcRuntimeUIToolkitRenderer.HasVerticalOverflowForTests, Is.True);
			Assert.That(DexcRuntimeUIToolkitRenderer.HasHorizontalOverflowForTests, Is.False);
			Assert.That(DexcRuntimeUIToolkitRenderer.ExecuteInteractableForTests, Is.True);

			var count = FindTextField("Count");
			count.Focus();
			DexcRuntimeUIToolkitRenderer.TickActive();
			var footerBefore = DexcRuntimeUIToolkitRenderer.FooterScreenRectForTests;
			count.value = "-1";
			yield return null;
			DexcRuntimeUIToolkitRenderer.TickActive();
			Assert.That(DexcRuntimeUIToolkitRenderer.ExecuteInteractableForTests, Is.False);
			Assert.That(Require<Button>("FooterPrimary").enabledSelf, Is.False);
			Assert.That(DexcRuntimeUIToolkitRenderer.FocusedTextFieldForTests, Is.SameAs(count));
			Assert.That(provider.States.Last(), Is.EqualTo(DebugMenuInputState.TextInput));
			AssertScreenRect(DexcRuntimeUIToolkitRenderer.FooterScreenRectForTests, footerBefore, 1f);
			Assert.That(DexcRuntimeUIToolkitRenderer.HasHorizontalOverflowForTests, Is.False);
			// The message appears under the field while typing, and the list makes room for it: the next row moves down.
			yield return null;
			var countRow = count.parent.parent;
			var message = countRow.Q<Label>("RowError");
			Assert.That(message.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
			var next = DexcRuntimeUIToolkitRenderer
				.ContentListForTests.Query<VisualElement>(className: "dexc-uitk-row")
				.ToList()
				.Where(other => other.resolvedStyle.display != DisplayStyle.None && other.worldBound.yMin > countRow.worldBound.yMin + 1f)
				.OrderBy(other => other.worldBound.yMin)
				.First();
			Assert.That(next.worldBound.yMin, Is.GreaterThanOrEqualTo(message.worldBound.yMax - 0.5f));

			count.value = "24";
			yield return null;
			Assert.That(DexcRuntimeUIToolkitRenderer.ExecuteInteractableForTests, Is.True);
			Assert.That(Require<Button>("FooterPrimary").enabledSelf, Is.True);
			Assert.That(DexcRuntimeUIToolkitRenderer.FocusedTextFieldForTests, Is.SameAs(count));
			AssertScreenRect(DexcRuntimeUIToolkitRenderer.FooterScreenRectForTests, footerBefore, 1f);
		}

		[UnityTest]
		public IEnumerator RunningDisablesDraftExposesCancelRequestAndCloseReturnsGameplayInput()
		{
			var provider = new RecordingProvider();
			DebugMenu.SetInputProvider(provider);
			SelectCommand("UI03/Cancellable");
			DexcRuntimeUIRenderer.ExecuteCommandForTests();
			DexcRuntimeUIToolkitRenderer.RefreshForTests();
			yield return null;
			Assert.That(DexcRuntimeUIState.AsyncExecution, Is.Not.Null);
			Assert.That(DexcRuntimeUIToolkitRenderer.ExecuteInteractableForTests, Is.False);
			Assert.That(DexcRuntimeUIToolkitRenderer.CancelBoundsForTests.width, Is.GreaterThan(0f));
			var cancel = Require<Button>("FooterPrimary");
			AssertColor(cancel.resolvedStyle.backgroundColor, DexcRuntimeUIToolkitRenderer.SemanticPaletteForTests.Accent);
			Assert.That(cancel.resolvedStyle.borderLeftWidth, Is.EqualTo(1f).Within(0.01f));
			AssertColor(cancel.resolvedStyle.borderLeftColor, DexcRuntimeUIToolkitRenderer.SemanticPaletteForTests.Focus);
			SendPointerUp(cancel);
			Assert.That(DexcRuntimeUIState.AsyncExecution.State, Is.EqualTo(DebugCommandExecutionState.Cancelling));
			DebugMenu.Close();
			DexcRuntimeUIToolkitRenderer.TickActive();
			Assert.That(provider.States.Last(), Is.EqualTo(DebugMenuInputState.Closed));
		}

		[Test]
		public void RendererDoesNotUseCanvasOrIMGUIAndOnlyOneRendererIsActive()
		{
			DexcRuntimeUIRenderer.ResetRenderCountersForTests();
			DexcRuntimeUIRenderer.DrawMenuAndDisplay();
			Assert.That(DexcRuntimeUIRenderer.MenuDrawCountForTests, Is.Zero);
			Assert.That(DexcRuntimeUIToolkitRenderer.RootForTests.GetComponent<Canvas>(), Is.Null);
			Assert.That(DexcRuntimeUIToolkitRenderer.DocumentForTests, Is.Not.Null);
			Assert.That(DexcRuntimeUIToolkitRenderer.RendererCountForTests, Is.EqualTo(1));
#if DEXC_HAS_UGUI
			Assert.That(DexcRuntimeUGUIRenderer.IsActive, Is.False);
#endif
		}

		[Test]
		public void InspectorHierarchyVirtualizesOnlyCurrentParentsDirectChildren()
		{
			var parent = new GameObject("UI03HierarchyParent");
			var child = new GameObject("UI03HierarchyChild");
			var grandchild = new GameObject("UI03HierarchyGrandchild");
			child.transform.SetParent(parent.transform);
			grandchild.transform.SetParent(child.transform);
			try
			{
				var scene = DebugInspector.GetScenes().Items.Single(item => item.Name == SceneManager.GetActiveScene().name);
				var roots = DebugInspector.GetHierarchyChildren(
					new DebugHierarchyChildrenQuery(
						scene.Scene,
						default(DebugObjectHandle),
						new DebugPageRequest(0, DexcRuntimeUIState.PageSize)
					)
				);
				var parentEntry = roots.Entries.Single(item => item.Label == parent.name);
				DexcRuntimeUIState.InspectorScene = scene.Scene;
				DexcRuntimeUIState.InspectorParent = parentEntry.Target;
				DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.InspectorScene);
				DexcRuntimeUIToolkitRenderer.RefreshForTests();
				var labels = Enumerable
					.Range(0, DexcRuntimeUIToolkitRenderer.VisibleRowCount)
					.Select(DexcRuntimeUIToolkitRenderer.RowTextForTests)
					.ToArray();
				Assert.That(labels, Has.Some.Contains(child.name));
				Assert.That(labels, Has.None.Contains(grandchild.name));
				Assert.That(
					DexcRuntimeUIToolkitRenderer.ContentListForTests.virtualizationMethod,
					Is.EqualTo(CollectionVirtualizationMethod.DynamicHeight)
				);
			}
			finally
			{
				Object.DestroyImmediate(parent);
			}
		}

		[UnityTest]
		public IEnumerator MoreThanPageUsesBoundedWindowWithoutTreatingTwelveAsLimit()
		{
			SelectCommand("UI03/MoreThanPage");
			yield return null;
			Assert.That(DexcRuntimeUIToolkitRenderer.VisibleRowCount, Is.LessThanOrEqualTo(DexcRuntimeUIState.PageSize));
			// ListView makes as many rows as its shortest row would need to fill the view; with a one-line description at the
			// top that can be the whole page, so the pool is bounded by the page rather than smaller than it.
			Assert.That(
				DexcRuntimeUIToolkitRenderer.ResolvedRowCountForTests,
				Is.LessThanOrEqualTo(DexcRuntimeUIToolkitRenderer.VisibleRowCount)
			);
			var selectable = Enumerable
				.Range(0, DexcRuntimeUIToolkitRenderer.SelectableCount)
				.First(index =>
					DexcRuntimeUIToolkitRenderer.SelectableTextForTests(index).StartsWith("Next fields", StringComparison.Ordinal)
				);
			DexcRuntimeUIToolkitRenderer.SelectForTests(selectable);
			DexcRuntimeUIToolkitRenderer.ActivateSelectedForTests();
			yield return null;
			Assert.That(DexcRuntimeUIToolkitRenderer.VisibleRowCount, Is.LessThanOrEqualTo(DexcRuntimeUIState.PageSize));
			Assert.That(
				Enumerable.Range(0, DexcRuntimeUIToolkitRenderer.VisibleRowCount).Select(DexcRuntimeUIToolkitRenderer.RowTextForTests),
				Has.Some.Contains("Field26")
			);
		}

		[UnityTest]
		public IEnumerator TwelveInvalidFieldsKeepEveryFieldAndFullValidationMessageReachableAcrossPages()
		{
			SelectCommand("UI05/AllInvalid12");
			yield return null;
			yield return AssertValidationFieldPages(12, RuntimeUIToolkitAllInvalidCommand.ExpectedValidation);
		}

		[UnityTest]
		public IEnumerator MoreThanFifteenMixedValidationFieldsKeepEveryFieldAndFullMessageReachableAcrossPages()
		{
			SelectCommand("UI05/MixedValidation26");
			yield return null;
			yield return AssertValidationFieldPages(26, RuntimeUIToolkitMixedValidationCommand.ExpectedValidation);
		}

		[UnityTest]
		public IEnumerator ProfileRowsUseActualFocusableHitArea()
		{
			foreach (var size in new[] { new Vector2Int(390, 844), new Vector2Int(844, 390), new Vector2Int(1366, 768) })
			{
				DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
				DexcRuntimeUIToolkitRenderer.RenderProfileForTests(size.x, size.y, new Rect(0, 0, size.x, size.y));
				DexcRuntimeUIToolkitRenderer.RefreshForTests();
				yield return null;
				yield return null;
				Assert.That(FindRow("Command").worldBound.height, Is.GreaterThanOrEqualTo(27f));
				Assert.That(
					DexcRuntimeUIToolkitRenderer.ContentListForTests.virtualizationMethod,
					Is.EqualTo(CollectionVirtualizationMethod.DynamicHeight)
				);
			}
		}

#if UNITY_EDITOR
		[UnityTest]
		public IEnumerator ProductionUIDocumentMatchesApprovedActualScreenSpaceBoundsInAllProfiles()
		{
			var profiles = new[]
			{
				new ScreenSpaceProfile(390, 844, new Rect(12f, 12f, 320f, 520f), 28f),
				new ScreenSpaceProfile(844, 390, new Rect(12f, 12f, 480f, 366f), 28f),
				new ScreenSpaceProfile(1366, 768, new Rect(12f, 124f, 360f, 520f), 28f),
			};
			foreach (var profile in profiles)
			{
				var restore = SetRuntimeGameViewSize(profile.Width, profile.Height, "ScreenSpaceAssertion");
				try
				{
					for (var attempt = 0; (Screen.width != profile.Width || Screen.height != profile.Height) && attempt < 120; attempt++)
					{
						yield return null;
					}

					Assert.That(Screen.width, Is.EqualTo(profile.Width));
					Assert.That(Screen.height, Is.EqualTo(profile.Height));
					SelectCommand("UI03/LongCommand");
					DexcRuntimeUIToolkitRenderer.RenderProfileForTests(
						profile.Width,
						profile.Height,
						new Rect(0f, 0f, profile.Width, profile.Height)
					);
					yield return null;
					yield return null;
					AssertScreenRect(DexcRuntimeUIToolkitRenderer.PanelScreenRectForTests, profile.Panel, 1f);
					AssertScreenRect(
						DexcRuntimeUIToolkitRenderer.HeaderScreenRectForTests,
						new Rect(profile.Panel.x, profile.Panel.yMax - Header(profile), profile.Panel.width, Header(profile)),
						1f
					);
					AssertScreenRect(
						DexcRuntimeUIToolkitRenderer.ContentScreenRectForTests,
						new Rect(profile.Panel.x, profile.Panel.y + 40f, profile.Panel.width, profile.Panel.height - 40f - Header(profile)),
						1f
					);
					AssertScreenRect(
						DexcRuntimeUIToolkitRenderer.FooterScreenRectForTests,
						new Rect(profile.Panel.x, profile.Panel.y, profile.Panel.width, 40f),
						1f
					);
					Assert.That(
						DexcRuntimeUIToolkitRenderer.ContentListForTests.virtualizationMethod,
						Is.EqualTo(CollectionVirtualizationMethod.DynamicHeight)
					);
				}
				finally
				{
					restore();
				}

				yield return null;
			}
		}

		[UnityTest]
		public IEnumerator ValidationMessageRetainsFullSeverityAndTailWithinBoundedContentInAllProfiles()
		{
			const string expectedMessage = "Error: The selected Source Path is stale. Choose a valid destination before Execute.";
			const string expectedTail = "valid destination before Execute.";
			var profiles = new[]
			{
				new ScreenSpaceProfile(390, 844, new Rect(12f, 12f, 320f, 520f), 28f),
				new ScreenSpaceProfile(844, 390, new Rect(12f, 12f, 480f, 366f), 28f),
				new ScreenSpaceProfile(1366, 768, new Rect(12f, 124f, 360f, 520f), 28f),
			};
			foreach (var profile in profiles)
			{
				var restore = SetRuntimeGameViewSize(profile.Width, profile.Height, "ValidationMessageAssertion");
				try
				{
					for (var attempt = 0; (Screen.width != profile.Width || Screen.height != profile.Height) && attempt < 120; attempt++)
					{
						yield return null;
					}

					SelectCommand("UI03/LongCommand");
					RuntimeUIToolkitManyInputsCommand.Reject = true;
					DexcRuntimeUIToolkitRenderer.RenderProfileForTests(
						profile.Width,
						profile.Height,
						new Rect(0f, 0f, profile.Width, profile.Height)
					);
					DexcRuntimeUIToolkitRenderer.RefreshForTests();
					var field = DexcRuntimeUIState.Command.Fields.Single(item => item.Name == "DestinationSceneSourcePath");
					yield return AssertFieldErrorWritten(field.Id, field.Name, expectedMessage);
					Assert.That(expectedMessage, Does.EndWith(expectedTail));
				}
				finally
				{
					RuntimeUIToolkitManyInputsCommand.Reject = false;
					restore();
				}

				yield return null;
			}
		}
#endif

		[UnityTest]
		public IEnumerator RuntimeUIToolkitVisualCaptureWritesApprovedExpandedMatrix()
		{
			if (!Environment.GetCommandLineArgs().Any(item => item == "--dexc-visual-capture"))
				yield break;
			var manifestPath = Environment.GetEnvironmentVariable("DEXC_UITOOLKIT_CAPTURE_MANIFEST");
			Assert.That(manifestPath, Is.Not.Null.And.Not.Empty);
			var outputDirectory = Path.GetDirectoryName(manifestPath);
			Directory.CreateDirectory(outputDirectory);
			var stamp = Environment.GetEnvironmentVariable("DEXC_VISUAL_CAPTURE_STAMP") ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
			var profiles = new[]
			{
				new CaptureProfile("MobilePortrait", 390, 844),
				new CaptureProfile("MobileLandscape", 844, 390),
				new CaptureProfile("WindowsPlayer", 1366, 768),
			};
			var captures = new List<CaptureEntry>();
			var skin = CreateVisualSkin();
			var cameraObject = new GameObject("UI03CaptureClearCamera");
			var camera = cameraObject.AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = Color.black;
			camera.cullingMask = 0;
			camera.depth = 10000f;
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = skin);
			DexcRuntimeUIState.ResetForSession();
			var session = DebugCommand
				.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 1)))
				.Summary.SessionId;
			DexcRuntimeUIState.SetSession(session);
			DebugMenu.Open();
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(true);
			var eventEvidence = new EventPathEvidence();
			var eventPathsExercised = false;

			foreach (var profile in profiles)
			{
				ResetVisualScenarioState();
#if UNITY_EDITOR
				var restore = SetRuntimeGameViewSize(profile.Width, profile.Height, profile.Name);
#else
				Screen.SetResolution(profile.Width, profile.Height, FullScreenMode.Windowed);
				Action restore = () => { };
#endif
				try
				{
					for (var attempt = 0; (Screen.width != profile.Width || Screen.height != profile.Height) && attempt < 120; attempt++)
						yield return null;
					Assert.That(Screen.width, Is.EqualTo(profile.Width));
					Assert.That(Screen.height, Is.EqualTo(profile.Height));
					DexcRuntimeUIToolkitRenderer.RenderProfileForTests(profile.Width, profile.Height, Screen.safeArea);
					if (!eventPathsExercised)
					{
						yield return ExerciseProductionEventPaths(eventEvidence);
						Assert.That(eventEvidence.IsComplete, Is.True);
						eventPathsExercised = true;
						ResetVisualScenarioState();
						DexcRuntimeUIToolkitRenderer.RenderProfileForTests(profile.Width, profile.Height, Screen.safeArea);
					}
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
					DexcRuntimeUIToolkitRenderer.RefreshForTests();
					yield return Capture(profile, "Home", stamp, outputDirectory, captures);

					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
					DexcRuntimeUIState.Search = "UI03/LongCommand";
					DexcRuntimeUIToolkitRenderer.RefreshForTests();
					yield return Capture(profile, "CommandList", stamp, outputDirectory, captures);
					SelectCommand("UI03/LongCommand");
					yield return Capture(profile, "CommandDetailDraft", stamp, outputDirectory, captures);
					DexcRuntimeUIRenderer.ExecuteCommandForTests();
					DexcRuntimeUIToolkitRenderer.RefreshForTests();
					yield return Capture(profile, "ResultState", stamp, outputDirectory, captures);
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.History);
					DexcRuntimeUIToolkitRenderer.RefreshForTests();
					yield return Capture(profile, "History", stamp, outputDirectory, captures);

					SelectCommand("UI03/LongCommand");
					yield return Capture(profile, "MultiInput12", stamp, outputDirectory, captures);
					RuntimeUIToolkitManyInputsCommand.Reject = true;
					DexcRuntimeUIToolkitRenderer.RefreshForTests();
					yield return Capture(profile, "Validation", stamp, outputDirectory, captures);
					RuntimeUIToolkitManyInputsCommand.Reject = false;
					DexcRuntimeUIState.Notice = string.Empty;
					SelectCommand("UI03/Cancellable");
					DexcRuntimeUIState.Notice = "Source Path: Scene/Streaming/Regions/West/VeryLongDestinationReference";
					DexcRuntimeUIToolkitRenderer.RefreshForTests();
					yield return Capture(profile, "LongTextReference", stamp, outputDirectory, captures);
					DexcRuntimeUIState.Notice = string.Empty;
					DexcRuntimeUIToolkitRenderer.RefreshForTests();
					DexcRuntimeUIRenderer.ExecuteCommandForTests();
					DexcRuntimeUIToolkitRenderer.RefreshForTests();
					yield return Capture(profile, "Running", stamp, outputDirectory, captures);
					ResetVisualScenarioState();
				}
				finally
				{
					restore();
				}
			}

			var template = DexcRuntimeUIToolkitRenderer.PanelSettingsTemplateForTests;
			var clonedSettings = DexcRuntimeUIToolkitRenderer.PanelSettingsForTests;
#if UNITY_EDITOR
			var panelSettingsGuid = AssetDatabase.AssetPathToGUID(PanelSettingsPath);
			var themeGuid = AssetDatabase.AssetPathToGUID(ThemePath);
#else
			var panelSettingsGuid = string.Empty;
			var themeGuid = string.Empty;
#endif
			File.WriteAllText(
				manifestPath,
				JsonUtility.ToJson(
					new CaptureManifest
					{
						Version = 2,
						Renderer = "Runtime UI Toolkit",
						Source =
							"DexcRuntimeUIToolkitRenderer UIDocument/code-generated VisualElements/session-cloned PanelSettings -> ScreenCapture.CaptureScreenshotAsTexture",
						HierarchySource = "CodeGeneratedVisualElements",
						PanelSettingsAssetPath = PanelSettingsPath,
						PanelSettingsGuid = panelSettingsGuid,
						ThemeAssetPath = ThemePath,
						ThemeGuid = themeGuid,
						SkinSnapshotPanelSettingsReference =
							DebuggerRuntime.CurrentSession != null
							&& ReferenceEquals(
								DebuggerRuntime.CurrentSession.Options.RuntimeUISkin.UIToolkitPanelSettingsTemplate,
								template
							),
						PanelSettingsCloned = template != null && clonedSettings != null && !ReferenceEquals(template, clonedSettings),
						ThemeDirectReference =
							template != null
							&& clonedSettings != null
							&& template.themeStyleSheet != null
							&& ReferenceEquals(template.themeStyleSheet, clonedSettings.themeStyleSheet),
						Hierarchy = "UIDocument/SafeAreaRoot/Panel/(Header,ListView,Footer)",
						VirtualizedControl = "ListView/FixedHeight",
						PageSize = DexcRuntimeUIState.PageSize,
						DirectChildrenLazyHierarchy = true,
						PointerEventPath = eventEvidence.PointerEventPath,
						ProviderNavigationPath = eventEvidence.ProviderNavigationPath,
						ProviderSubmitPath = eventEvidence.ProviderSubmitPath,
						ProviderCancelPath = eventEvidence.ProviderCancelPath,
						ProviderReadCount = eventEvidence.ProviderReadCount,
						KeyboardEventPath = eventEvidence.KeyboardEventPath,
						TextFocusLifecycle = eventEvidence.TextFocusLifecycle,
						CloseFocusReturn = eventEvidence.CloseFocusReturn,
						Captures = captures.ToArray(),
					},
					true
				)
			);
			Object.DestroyImmediate(cameraObject);
			Object.DestroyImmediate(skin);
			Assert.That(captures, Has.Count.EqualTo(27));
		}

		private static IEnumerator ExerciseProductionEventPaths(EventPathEvidence evidence)
		{
			var provider = new RecordingProvider();
			DebugMenu.Open();
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
			DexcRuntimeUIToolkitRenderer.RefreshForTests();
			yield return null;
			DexcRuntimeUIToolkitRenderer.TickActive();
			Assert.That(DexcRuntimeUIToolkitRenderer.IsActive, Is.True);
			SendPointerUp(FindRow("Command"));
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandList));
			evidence.PointerEventPath = DexcRuntimeUIState.Page == DexcRuntimeUIPage.CommandList;
			DexcRuntimeUIState.Search = "UI03/LongCommand";
			SelectCommand("UI03/LongCommand");
			yield return null;
			SendPointerUp(Require<Button>("FooterPrimary"));
			Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Not.Null);
			DexcRuntimeUIToolkitRenderer.RefreshForTests();
			yield return null;
			DexcRuntimeUIToolkitRenderer.SendKeyForTests(KeyCode.Escape);
			yield return null;
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandList));
			Assert.That(
				DexcRuntimeUIToolkitRenderer.FocusedTextFieldForTests,
				Is.Null,
				"Returning to the list marks the search row without editing it."
			);
			DexcRuntimeUIToolkitRenderer.SendKeyForTests(KeyCode.Return);
			yield return null;
			Assert.That(DexcRuntimeUIToolkitRenderer.FocusedTextFieldForTests, Is.Not.Null, "Enter on the search row starts editing.");
			DexcRuntimeUIToolkitRenderer.SendKeyForTests(KeyCode.Escape);
			yield return null;
			Assert.That(
				DexcRuntimeUIState.Page,
				Is.EqualTo(DexcRuntimeUIPage.CommandList),
				"Escape first leaves the focused search input."
			);
			Assert.That(DexcRuntimeUIToolkitRenderer.FocusedTextFieldForTests, Is.Null);
			Assert.That(
				DexcRuntimeUIToolkitRenderer.DocumentForTests.rootVisualElement.panel.focusController.focusedElement,
				Is.SameAs(DexcRuntimeUIToolkitRenderer.DocumentForTests.rootVisualElement),
				"Keyboard navigation retains a target after leaving search."
			);
			DexcRuntimeUIToolkitRenderer.SendKeyForTests(KeyCode.Escape);
			yield return null;
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));
			evidence.KeyboardEventPath = true;
			DexcRuntimeUIToolkitRenderer.RefreshForTests();
			yield return null;
			SendPointerUp(FindRow("History"));
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.History));
			SendPointerUp(Require<Button>("Back"));
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));
			DebugMenu.SetInputProvider(provider);
			DexcRuntimeUIToolkitRenderer.TickActive();
			Assert.That(provider.States.Last(), Is.EqualTo(DebugMenuInputState.Navigation));
			var navigationReads = provider.ReadCount;
			yield return DriveProviderFrame(provider, new DebugMenuInputFrame(new Vector2(0f, -1f), false, false));
			evidence.ProviderNavigationPath =
				provider.ReadCount > navigationReads && provider.NonZeroFrameCount >= 1 && DexcRuntimeUIState.SelectedIndex == 1;
			var submitReads = provider.ReadCount;
			yield return DriveProviderFrame(provider, new DebugMenuInputFrame(Vector2.zero, true, false));
			evidence.ProviderSubmitPath =
				provider.ReadCount > submitReads
				&& provider.NonZeroFrameCount >= 2
				&& DexcRuntimeUIState.Page == DexcRuntimeUIPage.SettingList;
			var cancelReads = provider.ReadCount;
			yield return DriveProviderFrame(provider, new DebugMenuInputFrame(Vector2.zero, false, true));
			evidence.ProviderCancelPath =
				provider.ReadCount > cancelReads && provider.NonZeroFrameCount >= 3 && DexcRuntimeUIState.Page == DexcRuntimeUIPage.Home;
			SelectCommand("UI03/LongCommand");
			yield return null;
			yield return null;
			var count = FindTextField("Count");
			count.Focus();
			DexcRuntimeUIToolkitRenderer.TickActive();
			var enteredTextInput =
				ReferenceEquals(DexcRuntimeUIToolkitRenderer.FocusedTextFieldForTests, count)
				&& provider.States.Last() == DebugMenuInputState.TextInput;
			DexcRuntimeUIToolkitRenderer.SendKeyForTests(KeyCode.Return);
			yield return null;
			DexcRuntimeUIToolkitRenderer.TickActive();
			evidence.TextFocusLifecycle =
				enteredTextInput
				&& DexcRuntimeUIToolkitRenderer.FocusedTextFieldForTests == null
				&& provider.States.Last() == DebugMenuInputState.Navigation;
			evidence.KeyboardEventPath = evidence.KeyboardEventPath && evidence.TextFocusLifecycle;
			count = FindTextField("Count");
			count.Focus();
			DexcRuntimeUIToolkitRenderer.TickActive();
			SendPointerUp(Require<Button>("Close"));
			evidence.PointerEventPath = evidence.PointerEventPath && !DexcRuntimeUIState.IsOpen;
			evidence.CloseFocusReturn =
				!DexcRuntimeUIState.IsOpen
				&& DexcRuntimeUIToolkitRenderer.FocusedTextFieldForTests == null
				&& provider.States.Last() == DebugMenuInputState.Closed;
			evidence.ProviderReadCount = provider.ReadCount;
			DebugMenu.ClearInputProvider(provider);
		}

		private static IEnumerator DriveProviderFrame(RecordingProvider provider, DebugMenuInputFrame frame)
		{
			provider.Enqueue(frame);
			yield return null;
			DexcRuntimeUIState.PollInput();
			DexcRuntimeUIToolkitRenderer.TickActive();
		}

		private static IEnumerator Capture(
			CaptureProfile profile,
			string scenario,
			string stamp,
			string outputDirectory,
			ICollection<CaptureEntry> captures
		)
		{
			DexcRuntimeUIToolkitRenderer.SetDocumentVisibleForCaptureTests(false);
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();
			DexcRuntimeUIToolkitRenderer.SetDocumentVisibleForCaptureTests(true);
			DexcRuntimeUIToolkitRenderer.TickActive();
			if (scenario == "MultiInput12")
				DexcRuntimeUIToolkitRenderer.ContentListForTests.ScrollToItem(9);
			else if (scenario == "Validation")
			{
				var messageIndex = DexcRuntimeUIToolkitRenderer.ValidationMessageItemIndexForTests;
				Assert.That(messageIndex, Is.GreaterThanOrEqualTo(0));
				DexcRuntimeUIToolkitRenderer.ScrollValidationMessageIntoViewForTests();
			}
			// Rows of different heights settle over a few layouts, so the scroll is repeated until the row stays put.
			for (var settle = 0; settle < 3 && scenario == "Validation"; settle++)
			{
				yield return null;
				DexcRuntimeUIToolkitRenderer.ScrollValidationMessageIntoViewForTests();
			}
			if (scenario == "Validation")
			{
				// ListView places a row of estimated height at the top edge; the last few points are scrolled by hand.
				yield return null;
				var marker = DexcRuntimeUIToolkitRenderer.ValidationMessageScreenRectForTests;
				var list = DexcRuntimeUIToolkitRenderer.ContentScreenRectForTests;
				var scroll = DexcRuntimeUIToolkitRenderer.ContentListForTests.Q<ScrollView>();
				if (marker.yMax > list.yMax)
					scroll.scrollOffset -= new Vector2(0f, marker.yMax - list.yMax + 4f);
				else if (marker.yMin < list.yMin)
					scroll.scrollOffset += new Vector2(0f, list.yMin - marker.yMin + 4f);
				yield return null;
			}
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();
			var texture = ScreenCapture.CaptureScreenshotAsTexture();
			try
			{
				var bytes = texture.EncodeToPNG();
				var path = Path.Combine(outputDirectory, "UI03-" + profile.Name + "-" + scenario + "-" + stamp + ".png");
				File.WriteAllBytes(path, bytes);
				var pixels = texture.GetPixels32();
				var background = pixels[0];
				var nonBackground = pixels.Count(pixel =>
					Math.Abs(pixel.r - background.r) > 3 || Math.Abs(pixel.g - background.g) > 3 || Math.Abs(pixel.b - background.b) > 3
				);
				var layout = DexcRuntimeUIToolkitRenderer.CurrentLayoutForTests;
				captures.Add(
					new CaptureEntry
					{
						Profile = profile.Name,
						Scenario = scenario,
						ArtifactPath = Path.GetFullPath(path),
						Sha256 = Sha256(bytes),
						Width = texture.width,
						Height = texture.height,
						Screen = new CaptureRect(layout.Screen),
						SafeArea = new CaptureRect(layout.SafeArea),
						Panel = new CaptureRect(DexcRuntimeUIToolkitRenderer.PanelScreenRectForTests),
						Header = new CaptureRect(DexcRuntimeUIToolkitRenderer.HeaderScreenRectForTests),
						Content = new CaptureRect(DexcRuntimeUIToolkitRenderer.ContentScreenRectForTests),
						Footer = new CaptureRect(DexcRuntimeUIToolkitRenderer.FooterScreenRectForTests),
						ValidationMessage = DexcRuntimeUIToolkitRenderer.ValidationMessageForTests,
						ValidationMessageTail = DexcRuntimeUIToolkitRenderer.ValidationMessageTailForTests,
						ValidationMessageBounds = new CaptureRect(DexcRuntimeUIToolkitRenderer.ValidationMessageScreenRectForTests),
						ValidationMessageTailBounds = new CaptureRect(DexcRuntimeUIToolkitRenderer.ValidationMessageTailScreenRectForTests),
						ValidationMessageFullyVisible = DexcRuntimeUIToolkitRenderer.ValidationMessageFullyVisibleForTests,
						RowHeight = layout.RowHeight,
						HitHeight = layout.HitHeight,
						ItemCount = DexcRuntimeUIToolkitRenderer.VisibleRowCount,
						ResolvedItemCount = DexcRuntimeUIToolkitRenderer.ResolvedRowCountForTests,
						Virtualization = DexcRuntimeUIToolkitRenderer.ContentListForTests.virtualizationMethod.ToString(),
						VerticalOverflow = DexcRuntimeUIToolkitRenderer.HasVerticalOverflowForTests,
						HorizontalOverflow = DexcRuntimeUIToolkitRenderer.HasHorizontalOverflowForTests,
						HeaderFixed = Math.Abs(layout.Header.yMax - layout.Panel.yMax) < 0.01f,
						FooterFixed = layout.Footer.height <= 0f || Math.Abs(layout.Footer.yMin - layout.Panel.yMin) < 0.01f,
						ExecuteEnabled = DexcRuntimeUIToolkitRenderer.ExecuteInteractableForTests,
						CancelBounds = new CaptureRect(DexcRuntimeUIToolkitRenderer.CancelBoundsForTests),
						Page = DexcRuntimeUIState.Page.ToString(),
						Notice = DexcRuntimeUIState.Notice ?? string.Empty,
						AsyncRunning = DexcRuntimeUIState.AsyncExecution != null,
						SelectedIndex = DexcRuntimeUIState.SelectedIndex,
						ScrollOffset = DexcRuntimeUIToolkitRenderer.VerticalScrollOffsetForTests,
						NonBackgroundPixels = nonBackground,
						RendererCount = DexcRuntimeUIToolkitRenderer.RendererCountForTests,
						SettledFrames = 2,
					}
				);
			}
			finally
			{
				Object.DestroyImmediate(texture);
			}
		}

		private static void ResetVisualScenarioState()
		{
			RuntimeUIToolkitManyInputsCommand.Reject = false;
			DexcRuntimeUIState.Notice = string.Empty;
			DexcRuntimeUIState.Search = string.Empty;
			DexcRuntimeUIState.ScrollPosition = Vector2.zero;
			DexcRuntimeUIState.ListScrollPosition = Vector2.zero;
			DexcRuntimeUIState.ResetForSession();
			DebugMenu.Open();
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
		}

		private static void SelectCommand(string search)
		{
			var item = DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, search, new DebugPageRequest(0, 8))).Items.Single();
			DexcRuntimeUIRenderer.SelectCommandForTests(item);
			DexcRuntimeUIToolkitRenderer.RefreshForTests();
		}

		private static IEnumerator AssertValidationFieldPages(int expectedFieldCount, Func<string, string> expectedValidation)
		{
			var fields = DexcRuntimeUIState.Command.Fields.Where(field => field.IsInput).ToArray();
			Assert.That(fields, Has.Length.EqualTo(expectedFieldCount));
			var fieldsById = fields.ToDictionary(field => field.Id);
			var reachedFields = new HashSet<DebugCommandFieldId>();
			var footer = DexcRuntimeUIToolkitRenderer.FooterScreenRectForTests;
			var sawNext = false;
			var pageCount = 0;
			while (true)
			{
				pageCount++;
				Assert.That(pageCount, Is.LessThanOrEqualTo(expectedFieldCount), "field page loop");
				yield return null;
				yield return null;
				var visibleFields = DexcRuntimeUIToolkitRenderer.VisibleCommandFieldIdsForTests;
				var previousIndex = FindSelectableIndex("Previous fields");
				var nextIndex = FindSelectableIndex("Next fields");
				var navigationRows = (previousIndex < 0 ? 0 : 1) + (nextIndex < 0 ? 0 : 1);
				Assert.That(visibleFields, Is.Not.Empty);
				// The command's path is in the header, so the page holds only its fields and paging.
				Assert.That(DexcRuntimeUIToolkitRenderer.VisibleRowCount, Is.EqualTo(visibleFields.Length + navigationRows));
				Assert.That(DexcRuntimeUIToolkitRenderer.VisibleRowCount, Is.LessThanOrEqualTo(DexcRuntimeUIState.PageSize));
				AssertScreenRect(DexcRuntimeUIToolkitRenderer.FooterScreenRectForTests, footer, 1f);
				Assert.That(DexcRuntimeUIToolkitRenderer.HasHorizontalOverflowForTests, Is.False);
				foreach (var fieldId in visibleFields)
				{
					Assert.That(reachedFields.Add(fieldId), Is.True, "field repeated across forward pages");
					Assert.That(fieldsById.TryGetValue(fieldId, out var field), Is.True, "unknown field ID");
					yield return AssertFieldErrorWritten(fieldId, field.Name, expectedValidation(field.Name));
					AssertScreenRect(DexcRuntimeUIToolkitRenderer.FooterScreenRectForTests, footer, 1f);
				}

				if (nextIndex < 0)
				{
					break;
				}

				sawNext = true;
				DexcRuntimeUIToolkitRenderer.SelectForTests(nextIndex);
				DexcRuntimeUIToolkitRenderer.ActivateSelectedForTests();
			}

			Assert.That(sawNext, Is.EqualTo(expectedFieldCount > DexcRuntimeUIState.PageSize));
			Assert.That(reachedFields.SetEquals(fields.Select(field => field.Id)), Is.True, "all field IDs must be reachable");
			if (!sawNext)
			{
				yield break;
			}

			var previous = FindSelectableIndex("Previous fields");
			Assert.That(previous, Is.GreaterThanOrEqualTo(0), "Previous fields must be enabled on the last page");
			var lastPageFields = DexcRuntimeUIToolkitRenderer.VisibleCommandFieldIdsForTests;
			DexcRuntimeUIToolkitRenderer.SelectForTests(previous);
			DexcRuntimeUIToolkitRenderer.ActivateSelectedForTests();
			yield return null;
			yield return null;
			Assert.That(DexcRuntimeUIToolkitRenderer.VisibleCommandFieldIdsForTests, Is.Not.EqualTo(lastPageFields));
			Assert.That(
				FindSelectableIndex("Next fields"),
				Is.GreaterThanOrEqualTo(0),
				"Next fields must be enabled after moving backward"
			);
			Assert.That(DexcRuntimeUIToolkitRenderer.HasHorizontalOverflowForTests, Is.False);
			AssertScreenRect(DexcRuntimeUIToolkitRenderer.FooterScreenRectForTests, footer, 1f);
		}

		/// <summary>As in IMGUI, a field's whole message is written under its value, wrapped inside the list.</summary>
		private static IEnumerator AssertFieldErrorWritten(DebugCommandFieldId fieldId, string fieldName, string expected)
		{
			DexcRuntimeUIToolkitRenderer.ScrollCommandFieldForTests(fieldId);
			yield return null;
			yield return null;
			DexcRuntimeUIToolkitRenderer.ScrollCommandFieldForTests(fieldId);
			yield return null;
			yield return null;
			var input = FindTextField(fieldName);
			var draft = input.value;
			var row = input.parent.parent;
			// The button at the end is only ever a field's "…" action, never a "!" that opens the message elsewhere.
			Assert.That(row.Q<Button>("FieldError").text, Is.EqualTo("…"));
			var text = row.Q<Label>("RowError");
			Assert.That(text.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
			Assert.That(text.text, Is.EqualTo(expected));
			Assert.That(text.worldBound.yMin, Is.GreaterThanOrEqualTo(input.worldBound.yMax - 0.5f));
			Assert.That(text.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
			var measured = text.MeasureTextSize(
				expected,
				text.contentRect.width,
				VisualElement.MeasureMode.AtMost,
				0,
				VisualElement.MeasureMode.Undefined
			);
			Assert.That(text.contentRect.height + 1, Is.GreaterThanOrEqualTo(measured.y));
			Assert.That(text.worldBound.xMax, Is.LessThanOrEqualTo(row.worldBound.xMax + 1));
			Assert.That(DexcRuntimeUIToolkitRenderer.HasHorizontalOverflowForTests, Is.False);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
			Assert.That(FindTextField(fieldName).value, Is.EqualTo(draft));
		}

		private static int FindSelectableIndex(string text)
		{
			for (var index = 0; index < DexcRuntimeUIToolkitRenderer.SelectableCount; index++)
			{
				if (string.Equals(DexcRuntimeUIToolkitRenderer.SelectableTextForTests(index), text, StringComparison.Ordinal))
					return index;
			}
			return -1;
		}

		private static T Require<T>(string name)
			where T : VisualElement
		{
			var element = DexcRuntimeUIToolkitRenderer.DocumentForTests.rootVisualElement.Q<T>(name);
			Assert.That(element, Is.Not.Null, name);
			return element;
		}

		private static TextField FindTextField(string label)
		{
			var field = DexcRuntimeUIToolkitRenderer
				.ContentListForTests.Query<TextField>(className: "dexc-uitk-input")
				.ToList()
				.FirstOrDefault(item =>
					item.resolvedStyle.display != DisplayStyle.None
					// A long label is broken between its words to fit its column.
					&& item.label.Replace("\n", string.Empty).IndexOf(label, StringComparison.Ordinal) >= 0
				);
			Assert.That(field, Is.Not.Null, label);
			return field;
		}

		private static VisualElement FindRow(string text)
		{
			var row = DexcRuntimeUIToolkitRenderer
				.ContentListForTests.Query<VisualElement>(className: "dexc-uitk-row")
				.ToList()
				.FirstOrDefault(item =>
					item.userData != null
					&& item.Q<Label>("RowLabel") != null
					&& item.Q<Label>("RowLabel").text.Replace("\n", string.Empty).StartsWith(text, StringComparison.Ordinal)
				);
			Assert.That(row, Is.Not.Null, text);
			return row;
		}

		private static void SendPointerUp(VisualElement element)
		{
			Assert.That(element, Is.Not.Null);
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

		private static void SendPointerEnter(VisualElement element)
		{
			var systemEvent = PointerEvent(element, EventType.MouseMove);
			using (var pointer = PointerEnterEvent.GetPooled(systemEvent))
			{
				pointer.target = element;
				element.SendEvent(pointer);
			}
		}

		private static void SendPointerDown(VisualElement element)
		{
			var systemEvent = PointerEvent(element, EventType.MouseDown);
			using (var pointer = PointerDownEvent.GetPooled(systemEvent))
			{
				pointer.target = element;
				element.SendEvent(pointer);
			}
		}

		private static void SendPointerLeave(VisualElement element)
		{
			var systemEvent = PointerEvent(element, EventType.MouseMove);
			using (var pointer = PointerLeaveEvent.GetPooled(systemEvent))
			{
				pointer.target = element;
				element.SendEvent(pointer);
			}
		}

		private static UnityEngine.Event PointerEvent(VisualElement element, EventType type)
		{
			Assert.That(element, Is.Not.Null);
			return new UnityEngine.Event
			{
				type = type,
				button = 0,
				mousePosition = element.worldBound.center,
			};
		}

		private static DexcRuntimeUISkin CreateVisualSkin()
		{
			var skin = ScriptableObject.CreateInstance<DexcRuntimeUISkin>();
#if UNITY_EDITOR
			skin.SetWorldVisualShaderForEditor(AssetDatabase.LoadAssetAtPath<Shader>(WorldVisualShaderPath));
			skin.SetUIToolkitPanelSettingsTemplateForEditor(AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath));
#endif
			return skin;
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
			var size = constructor.Invoke(new object[] { fixedResolution, width, height, "Dexc UI03 " + profile });
			group.GetType().GetMethod("AddCustomSize", BindingFlags.Public | BindingFlags.Instance).Invoke(group, new[] { size });
			var index =
				(int)group.GetType().GetMethod("GetTotalCount", BindingFlags.Public | BindingFlags.Instance).Invoke(group, null) - 1;
			var gameView = EditorWindow.GetWindow(gameViewType);
			var selectedSizeIndex = gameViewType.GetProperty(
				"selectedSizeIndex",
				BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
			);
			var previous = (int)selectedSizeIndex.GetValue(gameView, null);
			selectedSizeIndex.SetValue(gameView, index, null);
			gameView.Repaint();
			EditorApplication.QueuePlayerLoopUpdate();
			return () =>
			{
				selectedSizeIndex.SetValue(gameView, previous, null);
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

		private static void AssertScreenRect(Rect actual, Rect expected, float tolerance)
		{
			Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
			Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
			Assert.That(actual.width, Is.EqualTo(expected.width).Within(tolerance));
			Assert.That(actual.height, Is.EqualTo(expected.height).Within(tolerance));
		}

		private static void AssertContained(Rect inner, Rect outer, float tolerance)
		{
			Assert.That(inner.width, Is.GreaterThan(0f));
			Assert.That(inner.height, Is.GreaterThan(0f));
			Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - tolerance));
			Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + tolerance));
			Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - tolerance));
			Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + tolerance));
		}

		private static void AssertColor(Color actual, Color expected)
		{
			Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.01f));
			Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.01f));
			Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.01f));
			Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.01f));
		}

		private sealed class RecordingProvider : DebugMenuInputProvider
		{
			readonly Queue<DebugMenuInputFrame> m_Frames = new Queue<DebugMenuInputFrame>();
			internal readonly List<DebugMenuInputState> States = new List<DebugMenuInputState>();
			internal int ReadCount { get; private set; }
			internal int NonZeroFrameCount { get; private set; }

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

		[Serializable]
		private sealed class EventPathEvidence
		{
			public bool PointerEventPath;
			public bool ProviderNavigationPath;
			public bool ProviderSubmitPath;
			public bool ProviderCancelPath;
			public int ProviderReadCount;
			public bool KeyboardEventPath;
			public bool TextFocusLifecycle;
			public bool CloseFocusReturn;
			internal bool IsComplete =>
				PointerEventPath
				&& ProviderNavigationPath
				&& ProviderSubmitPath
				&& ProviderCancelPath
				&& ProviderReadCount > 0
				&& KeyboardEventPath
				&& TextFocusLifecycle
				&& CloseFocusReturn;
		}

		private readonly struct CaptureProfile
		{
			internal CaptureProfile(string name, int width, int height)
			{
				Name = name;
				Width = width;
				Height = height;
			}

			internal string Name { get; }
			internal int Width { get; }
			internal int Height { get; }
		}

		private static float Header(ScreenSpaceProfile profile) => DexcRuntimeFormLayout.HeaderHeight(profile.MinimumHitHeight);

		private readonly struct ScreenSpaceProfile
		{
			internal ScreenSpaceProfile(int width, int height, Rect panel, float minimumHitHeight)
			{
				Width = width;
				Height = height;
				Panel = panel;
				MinimumHitHeight = minimumHitHeight;
			}

			internal int Width { get; }
			internal int Height { get; }
			internal Rect Panel { get; }
			internal float MinimumHitHeight { get; }
		}

		[Serializable]
		private sealed class CaptureManifest
		{
			public int Version;
			public string Renderer;
			public string Source;
			public string HierarchySource;
			public string PanelSettingsAssetPath;
			public string PanelSettingsGuid;
			public string ThemeAssetPath;
			public string ThemeGuid;
			public bool SkinSnapshotPanelSettingsReference;
			public bool PanelSettingsCloned;
			public bool ThemeDirectReference;
			public string Hierarchy;
			public string VirtualizedControl;
			public int PageSize;
			public bool DirectChildrenLazyHierarchy;
			public bool PointerEventPath;
			public bool ProviderNavigationPath;
			public bool ProviderSubmitPath;
			public bool ProviderCancelPath;
			public int ProviderReadCount;
			public bool KeyboardEventPath;
			public bool TextFocusLifecycle;
			public bool CloseFocusReturn;
			public CaptureEntry[] Captures;
		}

		[Serializable]
		private sealed class CaptureEntry
		{
			public string Profile;
			public string Scenario;
			public string ArtifactPath;
			public string Sha256;
			public int Width;
			public int Height;
			public CaptureRect Screen;
			public CaptureRect SafeArea;
			public CaptureRect Panel;
			public CaptureRect Header;
			public CaptureRect Content;
			public CaptureRect Footer;
			public string ValidationMessage;
			public string ValidationMessageTail;
			public CaptureRect ValidationMessageBounds;
			public CaptureRect ValidationMessageTailBounds;
			public bool ValidationMessageFullyVisible;
			public float RowHeight;
			public float HitHeight;
			public int ItemCount;
			public int ResolvedItemCount;
			public string Virtualization;
			public bool VerticalOverflow;
			public bool HorizontalOverflow;
			public bool HeaderFixed;
			public bool FooterFixed;
			public bool ExecuteEnabled;
			public CaptureRect CancelBounds;
			public string Page;
			public string Notice;
			public bool AsyncRunning;
			public int SelectedIndex;
			public float ScrollOffset;
			public int NonBackgroundPixels;
			public int RendererCount;
			public int SettledFrames;
		}

		[Serializable]
		private struct CaptureRect
		{
			internal CaptureRect(Rect value)
			{
				X = value.x;
				Y = value.y;
				Width = value.width;
				Height = value.height;
			}

			public float X;
			public float Y;
			public float Width;
			public float Height;
		}
	}

	[DebugCommandAttribute(
		"Tests/RuntimeUI/UI03/LongCommandNameThatMustWrapWithoutHorizontalOverflow",
		Description = "Twelve parameters with a very long Source Path and string payload",
		Access = DebugCommandAccess.ReadOnly
	)]
	public sealed class RuntimeUIToolkitManyInputsCommand : DebugCommand
	{
		public static bool Reject;
		public InputInt Count = new InputInt(24);
		public InputFloat Radius = new InputFloat(12.5f);
		public InputBool IncludeInactive = new InputBool(true);
		public InputVector3 CenterPosition = new InputVector3(new Vector3(128f, 4.5f, -92f));
		public InputString DestinationSceneSourcePath = new InputString("Scenes/Streaming/Regions/West/VeryLongDestinationSceneName.unity");
		public InputEnum<RuntimeUIToolkitMode> Mode = new InputEnum<RuntimeUIToolkitMode>(RuntimeUIToolkitMode.Combat);
		public InputLong Seed = new InputLong(987654321L);
		public InputDouble DelaySeconds = new InputDouble(2.4d);
		public InputVector2 Area = new InputVector2(new Vector2(1920f, 1080f));
		public InputVector3 FallbackPosition = new InputVector3(new Vector3(1024.5f, -12.25f, 4096.75f));
		public InputString Notes = new InputString(
			"This intentionally long value wraps inside the bounded content viewport without horizontal overflow."
		);
		public InputBool ConfirmDangerousOperation = new InputBool(true);

		protected override void Validate(DebugCommandValidation validation)
		{
			if (Reject)
				validation.Error(
					DestinationSceneSourcePath,
					"ui03-invalid-source",
					"The selected Source Path is stale. Choose a valid destination before Execute."
				);
			if (Count.Value < 0)
				validation.Error(Count, "ui03-invalid-count", "Count must be zero or greater before Execute.");
			validation.Warning(Mode, "ui03-mode-warning", "Review the immediate mode before execution.");
		}

		protected override void Execute() { }
	}

	[DebugCommandAttribute("Tests/RuntimeUI/UI03/Cancellable", Access = DebugCommandAccess.ReadOnly)]
	public sealed class RuntimeUIToolkitCancellableCommand : AsyncDebugCommand
	{
		protected override TimeSpan Timeout => System.Threading.Timeout.InfiniteTimeSpan;

		protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
		}
	}

	[DebugCommandAttribute("Tests/RuntimeUI/UI03/MoreThanPage", Access = DebugCommandAccess.ReadOnly)]
	public sealed class RuntimeUIToolkitMoreThanPageCommand : DebugCommand
	{
		public InputInt Field01 = new InputInt(1);
		public InputInt Field02 = new InputInt(2);
		public InputInt Field03 = new InputInt(3);
		public InputInt Field04 = new InputInt(4);
		public InputInt Field05 = new InputInt(5);
		public InputInt Field06 = new InputInt(6);
		public InputInt Field07 = new InputInt(7);
		public InputInt Field08 = new InputInt(8);
		public InputInt Field09 = new InputInt(9);
		public InputInt Field10 = new InputInt(10);
		public InputInt Field11 = new InputInt(11);
		public InputInt Field12 = new InputInt(12);
		public InputInt Field13 = new InputInt(13);
		public InputInt Field14 = new InputInt(14);
		public InputInt Field15 = new InputInt(15);
		public InputInt Field16 = new InputInt(16);
		public InputInt Field17 = new InputInt(17);
		public InputInt Field18 = new InputInt(18);
		public InputInt Field19 = new InputInt(19);
		public InputInt Field20 = new InputInt(20);
		public InputInt Field21 = new InputInt(21);
		public InputInt Field22 = new InputInt(22);
		public InputInt Field23 = new InputInt(23);
		public InputInt Field24 = new InputInt(24);
		public InputInt Field25 = new InputInt(25);
		public InputInt Field26 = new InputInt(26);

		protected override void Execute() { }
	}

	[DebugCommandAttribute("Tests/RuntimeUI/UI05/AllInvalid12", Access = DebugCommandAccess.ReadOnly)]
	public sealed class RuntimeUIToolkitAllInvalidCommand : DebugCommand
	{
		public InputInt Field01 = new InputInt(1);
		public InputInt Field02 = new InputInt(2);
		public InputInt Field03 = new InputInt(3);
		public InputInt Field04 = new InputInt(4);
		public InputInt Field05 = new InputInt(5);
		public InputInt Field06 = new InputInt(6);
		public InputInt Field07 = new InputInt(7);
		public InputInt Field08 = new InputInt(8);
		public InputInt Field09 = new InputInt(9);
		public InputInt Field10 = new InputInt(10);
		public InputInt Field11 = new InputInt(11);
		public InputInt Field12 = new InputInt(12);

		public static string ExpectedValidation(string fieldName)
		{
			return "Error: " + Message(fieldName);
		}

		protected override void Validate(DebugCommandValidation validation)
		{
			validation.Error(Field01, "ui05-all-invalid-01", Message(nameof(Field01)));
			validation.Error(Field02, "ui05-all-invalid-02", Message(nameof(Field02)));
			validation.Error(Field03, "ui05-all-invalid-03", Message(nameof(Field03)));
			validation.Error(Field04, "ui05-all-invalid-04", Message(nameof(Field04)));
			validation.Error(Field05, "ui05-all-invalid-05", Message(nameof(Field05)));
			validation.Error(Field06, "ui05-all-invalid-06", Message(nameof(Field06)));
			validation.Error(Field07, "ui05-all-invalid-07", Message(nameof(Field07)));
			validation.Error(Field08, "ui05-all-invalid-08", Message(nameof(Field08)));
			validation.Error(Field09, "ui05-all-invalid-09", Message(nameof(Field09)));
			validation.Error(Field10, "ui05-all-invalid-10", Message(nameof(Field10)));
			validation.Error(Field11, "ui05-all-invalid-11", Message(nameof(Field11)));
			validation.Error(Field12, "ui05-all-invalid-12", Message(nameof(Field12)));
		}

		protected override void Execute() { }

		private static string Message(string fieldName)
		{
			return fieldName + " validation reaches sentinel " + fieldName + "-END.";
		}
	}

	[DebugCommandAttribute("Tests/RuntimeUI/UI05/MixedValidation26", Access = DebugCommandAccess.ReadOnly)]
	public sealed class RuntimeUIToolkitMixedValidationCommand : DebugCommand
	{
		public InputInt Field01 = new InputInt(1);
		public InputInt Field02 = new InputInt(2);
		public InputInt Field03 = new InputInt(3);
		public InputInt Field04 = new InputInt(4);
		public InputInt Field05 = new InputInt(5);
		public InputInt Field06 = new InputInt(6);
		public InputInt Field07 = new InputInt(7);
		public InputInt Field08 = new InputInt(8);
		public InputInt Field09 = new InputInt(9);
		public InputInt Field10 = new InputInt(10);
		public InputInt Field11 = new InputInt(11);
		public InputInt Field12 = new InputInt(12);
		public InputInt Field13 = new InputInt(13);
		public InputInt Field14 = new InputInt(14);
		public InputInt Field15 = new InputInt(15);
		public InputInt Field16 = new InputInt(16);
		public InputInt Field17 = new InputInt(17);
		public InputInt Field18 = new InputInt(18);
		public InputInt Field19 = new InputInt(19);
		public InputInt Field20 = new InputInt(20);
		public InputInt Field21 = new InputInt(21);
		public InputInt Field22 = new InputInt(22);
		public InputInt Field23 = new InputInt(23);
		public InputInt Field24 = new InputInt(24);
		public InputInt Field25 = new InputInt(25);
		public InputInt Field26 = new InputInt(26);

		public static string ExpectedValidation(string fieldName)
		{
			return IsWarning(fieldName) ? "Warning: " + Message(fieldName) : "Error: " + Message(fieldName);
		}

		protected override void Validate(DebugCommandValidation validation)
		{
			AddIssue(validation, Field01, nameof(Field01), "ui05-mixed-01");
			AddIssue(validation, Field02, nameof(Field02), "ui05-mixed-02");
			AddIssue(validation, Field03, nameof(Field03), "ui05-mixed-03");
			AddIssue(validation, Field04, nameof(Field04), "ui05-mixed-04");
			AddIssue(validation, Field05, nameof(Field05), "ui05-mixed-05");
			AddIssue(validation, Field06, nameof(Field06), "ui05-mixed-06");
			AddIssue(validation, Field07, nameof(Field07), "ui05-mixed-07");
			AddIssue(validation, Field08, nameof(Field08), "ui05-mixed-08");
			AddIssue(validation, Field09, nameof(Field09), "ui05-mixed-09");
			AddIssue(validation, Field10, nameof(Field10), "ui05-mixed-10");
			AddIssue(validation, Field11, nameof(Field11), "ui05-mixed-11");
			AddIssue(validation, Field12, nameof(Field12), "ui05-mixed-12");
			AddIssue(validation, Field13, nameof(Field13), "ui05-mixed-13");
			AddIssue(validation, Field14, nameof(Field14), "ui05-mixed-14");
			AddIssue(validation, Field15, nameof(Field15), "ui05-mixed-15");
			AddIssue(validation, Field16, nameof(Field16), "ui05-mixed-16");
			AddIssue(validation, Field17, nameof(Field17), "ui05-mixed-17");
			AddIssue(validation, Field18, nameof(Field18), "ui05-mixed-18");
			AddIssue(validation, Field19, nameof(Field19), "ui05-mixed-19");
			AddIssue(validation, Field20, nameof(Field20), "ui05-mixed-20");
			AddIssue(validation, Field21, nameof(Field21), "ui05-mixed-21");
			AddIssue(validation, Field22, nameof(Field22), "ui05-mixed-22");
			AddIssue(validation, Field23, nameof(Field23), "ui05-mixed-23");
			AddIssue(validation, Field24, nameof(Field24), "ui05-mixed-24");
			AddIssue(validation, Field25, nameof(Field25), "ui05-mixed-25");
			AddIssue(validation, Field26, nameof(Field26), "ui05-mixed-26");
		}

		protected override void Execute() { }

		private static void AddIssue(DebugCommandValidation validation, IDebugInput field, string fieldName, string code)
		{
			if (IsWarning(fieldName))
				validation.Warning(field, code, Message(fieldName));
			else
				validation.Error(field, code, Message(fieldName));
		}

		private static bool IsWarning(string fieldName)
		{
			return int.Parse(fieldName.Substring("Field".Length)) % 2 == 0;
		}

		private static string Message(string fieldName)
		{
			return fieldName + " mixed validation reaches sentinel " + fieldName + "-END.";
		}
	}

	public enum RuntimeUIToolkitMode
	{
		Combat,
		Passive,
	}
}
#endif
