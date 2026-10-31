#if DEXC_DEBUGGER && DEXC_HAS_UGUI
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
#if UNITY_EDITOR
using UnityEditor;
#endif
using Object = UnityEngine.Object;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	[TestFixture]
	public sealed class DexcRuntimeUGUITests
	{
		GameObject m_EventSystemObject;

		[SetUp]
		public void SetUp()
		{
			RuntimeUGUIManyInputsCommand.Reject = false;
			DexcRuntimeUIState.ResetForSession();
			DebuggerRuntime.ResetForTests();
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(true);
			EnsureEventSystem();
			DebuggerRuntime.Initialize();
			// Each test owns a new runtime session, even when a previous test populated the UI list cache.
			DexcRuntimeUIState.SetSession(DebuggerRuntime.CurrentSession.SessionId);
			DexcRuntimeUGUIRenderer.CreateForBootstrap();
			DebugMenu.Open();
			DexcRuntimeUGUIRenderer.RenderProfileForTests(390, 844, new Rect(0f, 0f, 390f, 844f));
			Canvas.ForceUpdateCanvases();
		}

		[TearDown]
		public void TearDown()
		{
			if (DexcRuntimeUIState.AsyncExecution != null)
				DexcRuntimeUIState.AsyncExecution.Cancel();
			DebugMenu.Close();
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(false);
			DexcRuntimeUGUIRenderer.ClearProfileOverrideForTests();
			DexcRuntimeUIState.ResetForSession();
			DebuggerRuntime.ResetForTests();
			if (m_EventSystemObject != null)
				Object.DestroyImmediate(m_EventSystemObject);
		}

		[Test]
		public void NativeEventSystemMoveSubmitCancelWithoutProvider()
		{
			Assert.That(DexcRuntimeUIState.InputProvider, Is.Null);
			var eventSystem = DexcUGUIApi
				.EventSystemType.GetProperty("current", BindingFlags.Public | BindingFlags.Static)
				.GetValue(null, null);
			var move = Activator.CreateInstance(Resolve("UnityEngine.EventSystems.AxisEventData"), eventSystem);
			DexcUGUIApi.Set(move, "moveDir", Enum.Parse(Resolve("UnityEngine.EventSystems.MoveDirection"), "Down"));
			Assert.That(DispatchNative(DexcUGUIApi.CurrentSelected.gameObject, "IMoveHandler", "moveHandler", move), Is.True);
			DexcRuntimeUGUIRenderer.RefreshForTests();
			Assert.That(DexcRuntimeUIState.SelectedIndex, Is.EqualTo(1));
			var input = Activator.CreateInstance(Resolve("UnityEngine.EventSystems.BaseEventData"), eventSystem);
			Assert.That(DispatchNative(DexcUGUIApi.CurrentSelected.gameObject, "ISubmitHandler", "submitHandler", input), Is.True);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.SettingList));
			DexcRuntimeUGUIRenderer.RefreshForTests();
			var reached = DispatchNative(DexcUGUIApi.CurrentSelected.gameObject, "ICancelHandler", "cancelHandler", input);
			Debug.Log("UI06 F12 native EventSystem cancel reached=" + reached + ", page=" + DexcRuntimeUIState.Page);
			Assert.That(reached, Is.True, "standard EventSystem cancel must have a production receiver");
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));
		}

		[Test]
		public void NativeCancelRebindingReplacesTheOwnedListener()
		{
			var selected = DexcUGUIApi.CurrentSelected;
			var system = DexcUGUIApi.EventSystemType.GetProperty("current").GetValue(null);
			var input = Activator.CreateInstance(Resolve("UnityEngine.EventSystems.BaseEventData"), system);
			var previous = 0;
			var current = 0;
			DexcUGUIApi.SetCancelHandler(selected, _ => previous++, null);
			DexcUGUIApi.SetCancelHandler(selected, _ => current++, null);
			Assert.That(DispatchNative(selected.gameObject, "ICancelHandler", "cancelHandler", input), Is.True);
			Assert.That(previous, Is.Zero);
			Assert.That(current, Is.EqualTo(1));
		}

		static bool DispatchNative(GameObject target, string handler, string property, object data)
		{
			var execute = Resolve("UnityEngine.EventSystems.ExecuteEvents");
			var method = execute
				.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.Single(item => item.Name == "Execute" && item.IsGenericMethodDefinition);
			return (bool)
				method
					.MakeGenericMethod(Resolve("UnityEngine.EventSystems." + handler))
					.Invoke(
						null,
						new[]
						{
							target,
							data,
							execute.GetProperty(property, BindingFlags.Public | BindingFlags.Static).GetValue(null, null),
						}
					);
		}

		[UnityTest]
		public IEnumerator NativeCancelReceiverPreservesChildButtonDragToParentScrollRect()
		{
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
			DexcRuntimeUIState.Search = "Tests/";
			DexcRuntimeUGUIRenderer.RefreshForTests();
			yield return null;
			Canvas.ForceUpdateCanvases();
			var button = Enumerable
				.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
				.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
				.First(item =>
					item != null
					&& DexcUGUIApi.ButtonType.IsInstanceOfType(item)
					&& item.transform.parent == DexcRuntimeUGUIRenderer.ContentForTests
				)
				.gameObject;
			var system = DexcUGUIApi.EventSystemType.GetProperty("current").GetValue(null);
			var pointer = Activator.CreateInstance(Resolve("UnityEngine.EventSystems.PointerEventData"), system);
			var start = DexcRuntimeUGUIRenderer.ContentScreenRectForTests.center;
			DexcUGUIApi.Set(pointer, "position", start);
			DexcUGUIApi.Set(pointer, "eligibleForClick", true);
			Assert.That(DispatchNative(button, "IInitializePotentialDragHandler", "initializePotentialDrag", pointer), Is.True);
			Assert.That(DispatchNative(button, "IBeginDragHandler", "beginDragHandler", pointer), Is.True);
			// The button is the drag receiver too, so the input module would click it on release unless the drag says otherwise.
			Assert.That(DexcUGUIApi.Get(pointer, "eligibleForClick"), Is.False);
			DexcUGUIApi.Set(pointer, "position", start + Vector2.up * 100f);
			Assert.That(DispatchNative(button, "IDragHandler", "dragHandler", pointer), Is.True);
			Assert.That(DispatchNative(button, "IEndDragHandler", "endDragHandler", pointer), Is.True);
			Assert.That(DexcRuntimeUGUIRenderer.ContentForTests.anchoredPosition.y, Is.GreaterThan(20f));
		}

		[Test]
		public void ProductionHierarchyCompletesProviderKeyboardPointerHistoryAndCloseFlow()
		{
			var provider = new RecordingProvider();
			DebugMenu.SetInputProvider(provider);
			Debug.Log(
				"UI06 provider entry: state="
					+ DexcRuntimeUIState.InputState
					+ ", focus="
					+ GUI.GetNameOfFocusedControl()
					+ ", index="
					+ DexcRuntimeUIState.SelectedIndex
					+ ", selected="
					+ DexcUGUIApi.CurrentSelected
					+ ", UI session="
					+ DexcRuntimeUIState.SessionId
					+ ", runtime session="
					+ DebuggerRuntime.CurrentSession.SessionId
			);
			DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, true, false));
			DexcRuntimeUIRenderer.ProcessProviderInputForTests();
			Debug.Log(
				"UI06 provider submit: page="
					+ DexcRuntimeUIState.Page
					+ ", notice="
					+ DexcRuntimeUIState.Notice
					+ ", activeUGUI="
					+ DexcRuntimeUGUIRenderer.IsActive
					+ ", activeUITK="
					+ DexcRuntimeUIToolkitRenderer.IsActive
			);
			DexcRuntimeUGUIRenderer.RefreshForTests();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandList));
			var searchInput = DexcRuntimeUGUIRenderer.SelectableForTests(0);
			DexcUGUIApi.Set(searchInput, "text", "UI02/LongCommand");
			// A search applies when its edit ends. The field was never activated here, so its end-of-edit event is sent
			// directly rather than through DeactivateInputField, which only ends an active edit.
			var endEdit = DexcUGUIApi.Get(searchInput, "onEndEdit");
			endEdit.GetType().GetMethod("Invoke").Invoke(endEdit, new object[] { "UI02/LongCommand" });
			DexcRuntimeUGUIRenderer.RefreshForTests();
			InvokeSubmit(
				Enumerable
					.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
					.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
					.Single(item =>
						item != null
						&& DexcUGUIApi.ButtonType.IsInstanceOfType(item)
						&& Convert
							.ToString(DexcUGUIApi.Get(item.GetComponentInChildren(DexcUGUIApi.TextType), "text"))
							.StartsWith(
								DebugCommand
									.Registry.GetList(new DebugListQuery(string.Empty, "UI02/LongCommand", new DebugPageRequest(0, 8)))
									.Items.Single()
									.Path,
								StringComparison.Ordinal
							)
					)
			);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
			DexcRuntimeUGUIRenderer.RefreshForTests();
			var execute = FindSelectable("FooterAction1");
			InvokePointerClick(execute);
			Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Not.Null);
			DexcRuntimeUGUIRenderer.RefreshForTests();
			InvokeSubmit(FindSelectable("Back"));
			DexcRuntimeUGUIRenderer.RefreshForTests();
			InvokeSubmit(FindSelectable("Back"));
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));
			DexcRuntimeUGUIRenderer.RefreshForTests();
			InvokePointerClick(DexcRuntimeUGUIRenderer.SelectableForTests(3));
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.History));
			DexcRuntimeUGUIRenderer.RefreshForTests();
			InvokePointerClick(FindSelectable("Close"));
			Assert.That(DexcRuntimeUIState.IsOpen, Is.False);
			Assert.That(provider.States.Last(), Is.EqualTo(DebugMenuInputState.Closed));
		}

		[UnityTest]
		public IEnumerator ActualScrollRectDragAndWheelSurviveIdleTicksInListAndDetail()
		{
			DebugMenu.SetInputProvider(new RecordingProvider());
			foreach (var detail in new[] { false, true })
			{
				if (detail)
				{
					SelectCommand("UI02/LongCommand");
				}
				else
				{
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
					// A search lists every matching command flat, long enough to scroll.
					DexcRuntimeUIState.Search = "Tests/";
					DexcRuntimeUGUIRenderer.RefreshForTests();
				}
				yield return null;
				Canvas.ForceUpdateCanvases();
				Assert.That(DexcRuntimeUGUIRenderer.HasVerticalOverflowForTests, Is.True);
				var header = DexcRuntimeUGUIRenderer.HeaderScreenRectForTests;
				var footer = DexcRuntimeUGUIRenderer.FooterScreenRectForTests;
				var selection = DexcRuntimeUIState.SelectedIndex;
				var scroll = DexcRuntimeUGUIRenderer.ScrollRectForTests;
				var eventSystem = DexcUGUIApi.EventSystemType.GetProperty("current").GetValue(null);
				var pointer = Activator.CreateInstance(Resolve("UnityEngine.EventSystems.PointerEventData"), eventSystem);
				var start = DexcRuntimeUGUIRenderer.ContentScreenRectForTests.center;
				DexcUGUIApi.Set(pointer, "position", start);
				InvokeScrollHandler(scroll, "OnInitializePotentialDrag", pointer);
				InvokeScrollHandler(scroll, "OnBeginDrag", pointer);
				for (var step = 1; step <= 5; step++)
				{
					DexcUGUIApi.Set(pointer, "position", start + Vector2.up * (step * 35f));
					InvokeScrollHandler(scroll, "OnDrag", pointer);
					Debug.Log("UI06 F07 drag " + step + ": " + DexcRuntimeUGUIRenderer.ContentForTests.anchoredPosition);
					yield return null;
				}
				InvokeScrollHandler(scroll, "OnEndDrag", pointer);
				var release = DexcRuntimeUGUIRenderer.ContentForTests.anchoredPosition.y;
				Assert.That(release, Is.GreaterThan(20f), "actual drag must reach ScrollRect");
				for (var frame = 0; frame < 30; frame++)
				{
					DexcRuntimeUGUIRenderer.TickActive();
					yield return null;
				}
				Assert.That(
					DexcRuntimeUGUIRenderer.ContentForTests.anchoredPosition.y,
					Is.GreaterThanOrEqualTo(release - 1f),
					"idle Tick must not undo pointer scroll"
				);
				Assert.That(DexcRuntimeUIState.SelectedIndex, Is.EqualTo(selection));
				var beforeWheel = DexcRuntimeUGUIRenderer.ContentForTests.anchoredPosition.y;
				DexcUGUIApi.Set(pointer, "scrollDelta", Vector2.down * 3f);
				InvokeScrollHandler(scroll, "OnScroll", pointer);
				DexcRuntimeUGUIRenderer.TickActive();
				Assert.That(DexcRuntimeUGUIRenderer.ContentForTests.anchoredPosition.y, Is.GreaterThanOrEqualTo(beforeWheel - 1f));
				AssertScreenRect(DexcRuntimeUGUIRenderer.HeaderScreenRectForTests, header);
				AssertScreenRect(DexcRuntimeUGUIRenderer.FooterScreenRectForTests, footer);
				Assert.That(DexcRuntimeUGUIRenderer.HasHorizontalOverflowForTests, Is.False);
			}
			InvokePointerClick(FindSelectable("Close"));
			DexcRuntimeUGUIRenderer.TickActive();
			Assert.That(DexcUGUIApi.CurrentSelected, Is.Null);
			DebugMenu.Open();
			DexcRuntimeUGUIRenderer.TickActive();
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.Home));
		}

		[UnityTest]
		public IEnumerator NativeSelectableNavigationSynchronizesFocusAndOnlyScrollsIntoViewWhenNeeded()
		{
			DebugMenu.SetInputProvider(new RecordingProvider());
			SelectCommand("UI02/LongCommand");
			yield return null;
			var eventSystem = DexcUGUIApi.EventSystemType.GetProperty("current").GetValue(null);
			var move = Activator.CreateInstance(Resolve("UnityEngine.EventSystems.AxisEventData"), eventSystem);
			DexcUGUIApi.Set(move, "moveVector", Vector2.down);
			DexcUGUIApi.Set(move, "moveDir", Enum.Parse(Resolve("UnityEngine.EventSystems.MoveDirection"), "Down"));
			// Walks down to the last field: rows sit beside their labels, so the first ten can fit without scrolling.
			for (
				var step = 0;
				step < 40
					&& DexcRuntimeUIState.SelectedIndex + 1 < DexcRuntimeUGUIRenderer.SelectableCount
					&& DexcRuntimeUGUIRenderer.SelectableForTests(DexcRuntimeUIState.SelectedIndex + 1).transform.IsChildOf(DexcRuntimeUGUIRenderer.ContentForTests);
				step++
			)
			{
				var selected = DexcUGUIApi.CurrentSelected;
				selected.GetType().GetMethod("OnMove").Invoke(selected, new[] { move });
				var navigated = DexcUGUIApi.CurrentSelected;
				DexcRuntimeUGUIRenderer.TickActive();
				Assert.That(DexcUGUIApi.CurrentSelected, Is.SameAs(navigated), "empty provider frames must not override native selection");
				yield return null;
			}
			Assert.That(DexcRuntimeUGUIRenderer.VerticalNormalizedPositionForTests, Is.LessThan(1f));
			var bounds = DexcRuntimeUGUIRenderer.SelectableScreenRectForTests(DexcRuntimeUIState.SelectedIndex);
			var viewport = DexcRuntimeUGUIRenderer.ContentScreenRectForTests;
			Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(viewport.yMin - 1f));
			Assert.That(bounds.yMax, Is.LessThanOrEqualTo(viewport.yMax + 1f));
			var position = DexcRuntimeUGUIRenderer.ContentForTests.anchoredPosition;
			for (var frame = 0; frame < 5; frame++)
			{
				DexcRuntimeUGUIRenderer.TickActive();
				yield return null;
			}
			Assert.That(DexcRuntimeUGUIRenderer.ContentForTests.anchoredPosition, Is.EqualTo(position));
		}

		static void InvokeScrollHandler(Component scroll, string name, object pointer)
		{
			scroll.GetType().GetMethod(name).Invoke(scroll, new[] { pointer });
		}

		[Test]
		public void TwelveFieldDraftCorrectionRevalidatesThroughInputEventAndKeepsFocusScrollAndFooter()
		{
			var provider = new RecordingProvider();
			DebugMenu.SetInputProvider(provider);
			SelectCommand("UI02/LongCommand");
			Canvas.ForceUpdateCanvases();
			var labels = Enumerable
				.Range(0, DexcRuntimeUGUIRenderer.VisibleRowCount)
				.Select(DexcRuntimeUGUIRenderer.RowTextForTests)
				.ToArray();
			foreach (var field in DexcRuntimeUIState.Command.Fields.Where(item => item.IsInput))
			{
				Assert.That(labels, Does.Contain(field.Label));
			}

			Assert.That(DexcRuntimeUGUIRenderer.VisibleRowCount, Is.LessThanOrEqualTo(DexcRuntimeUIState.PageSize));
			Assert.That(DexcRuntimeUGUIRenderer.HasVerticalOverflowForTests, Is.True);
			Assert.That(DexcRuntimeUGUIRenderer.HasHorizontalOverflowForTests, Is.False);
			Assert.That(DexcRuntimeUGUIRenderer.ExecuteInteractableForTests, Is.True);
			var lastField = Enumerable
				.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
				.Last(index => DexcUGUIApi.InputFieldType.IsInstanceOfType(DexcRuntimeUGUIRenderer.SelectableForTests(index)));
			DexcRuntimeUGUIRenderer.SelectForTests(lastField);
			DexcRuntimeUGUIRenderer.TickActive();
			Canvas.ForceUpdateCanvases();
			Assert.That(DexcRuntimeUGUIRenderer.VerticalNormalizedPositionForTests, Is.LessThan(1f));
			var layout = DexcRuntimeUGUIRenderer.CurrentLayoutForTests;
			Assert.That(layout.Header.yMax, Is.EqualTo(layout.Panel.yMax).Within(0.01f));
			Assert.That(layout.Footer.yMin, Is.EqualTo(layout.Panel.yMin).Within(0.01f));
			var countField = Enumerable
				.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
				.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
				.First(DexcUGUIApi.InputFieldType.IsInstanceOfType);
			var countIndex = Enumerable
				.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
				.First(index => ReferenceEquals(DexcRuntimeUGUIRenderer.SelectableForTests(index), countField));
			DexcRuntimeUGUIRenderer.SelectForTests(countIndex);
			DexcRuntimeUGUIRenderer.ActivateSelectedForTests();
			DexcRuntimeUGUIRenderer.TickActive();
			var footerBefore = DexcRuntimeUGUIRenderer.FooterScreenRectForTests;
			DexcUGUIApi.Set(countField, "text", "-1");
			Canvas.ForceUpdateCanvases();
			Assert.That(DexcRuntimeUGUIRenderer.ExecuteInteractableForTests, Is.False);
			var executeButton = DexcRuntimeUGUIRenderer.FooterForTests.Find("FooterAction1").GetComponent(DexcUGUIApi.ButtonType);
			Assert.That(DexcUGUIApi.Get(executeButton, "interactable"), Is.False);
			Assert.That(FindSelectable("FooterAction1"), Is.Null);
			Assert.That(DexcUGUIApi.CurrentSelected, Is.SameAs(countField));
			Assert.That(provider.States.Last(), Is.EqualTo(DebugMenuInputState.TextInput));
			Assert.That(DexcRuntimeUGUIRenderer.VerticalNormalizedPositionForTests, Is.InRange(0f, 1f));
			AssertScreenRect(DexcRuntimeUGUIRenderer.FooterScreenRectForTests, footerBefore);
			Assert.That(DexcRuntimeUGUIRenderer.HasHorizontalOverflowForTests, Is.False);
			DexcUGUIApi.Set(countField, "text", "24");
			Canvas.ForceUpdateCanvases();
			Assert.That(DexcRuntimeUGUIRenderer.ExecuteInteractableForTests, Is.True);
			Assert.That(DexcUGUIApi.Get(executeButton, "interactable"), Is.True);
			Assert.That(FindSelectable("FooterAction1"), Is.Not.Null);
			Assert.That(DexcUGUIApi.CurrentSelected, Is.SameAs(countField));
			Assert.That(DexcRuntimeUGUIRenderer.VerticalNormalizedPositionForTests, Is.InRange(0f, 1f));
			AssertScreenRect(DexcRuntimeUGUIRenderer.FooterScreenRectForTests, footerBefore);
		}

		[UnityTest]
		public IEnumerator RunningDisablesDraftLeavesCancelSelectableAndFocusLifecycleReturnsToGame()
		{
			var provider = new RecordingProvider();
			DebugMenu.SetInputProvider(provider);
			SelectCommand("UI02/Cancellable");
			DexcRuntimeUIRenderer.ExecuteCommandForTests();
			DexcRuntimeUGUIRenderer.RefreshForTests();
			Assert.That(DexcRuntimeUIState.AsyncExecution, Is.Not.Null);
			Assert.That(DexcRuntimeUGUIRenderer.ExecuteInteractableForTests, Is.False);
			Assert.That(DexcRuntimeUGUIRenderer.CancelBoundsForTests.width, Is.GreaterThan(0f));
			var cancel = FindSelectable("FooterAction0");
			Assert.That(cancel, Is.Not.Null);
			InvokeSubmit(cancel);
			for (var index = 0; index < 30 && DexcRuntimeUIState.AsyncTask != null && !DexcRuntimeUIState.AsyncTask.IsCompleted; index++)
				yield return null;

			SelectCommand("UI02/LongCommand");
			var inputIndex = Enumerable
				.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
				.First(index => DexcUGUIApi.InputFieldType.IsInstanceOfType(DexcRuntimeUGUIRenderer.SelectableForTests(index)));
			DexcRuntimeUGUIRenderer.SelectForTests(inputIndex);
			DexcRuntimeUGUIRenderer.ActivateSelectedForTests();
			DexcRuntimeUGUIRenderer.TickActive();
			Assert.That(provider.States.Last(), Is.EqualTo(DebugMenuInputState.TextInput));
			DebugMenu.Close();
			DexcRuntimeUGUIRenderer.TickActive();
			Assert.That(provider.States.Last(), Is.EqualTo(DebugMenuInputState.Closed));
		}

		[Test]
		public void RendererRespectsExistingEventSystemDoesNotDuplicateRowsOrDrawIMGUI()
		{
			var before = Resources.FindObjectsOfTypeAll(DexcUGUIApi.EventSystemType).Length;
			var rows = DexcRuntimeUGUIRenderer.VisibleRowCount;
			DexcRuntimeUGUIRenderer.RefreshForTests();
			Assert.That(Resources.FindObjectsOfTypeAll(DexcUGUIApi.EventSystemType).Length, Is.EqualTo(before));
			Assert.That(DexcRuntimeUGUIRenderer.VisibleRowCount, Is.EqualTo(rows));
			DexcRuntimeUIRenderer.ResetRenderCountersForTests();
			DexcRuntimeUIRenderer.DrawMenuAndDisplay();
			Assert.That(DexcRuntimeUIRenderer.MenuDrawCountForTests, Is.Zero);
			Assert.That(DexcRuntimeUGUIRenderer.RootForTests.GetComponent<Canvas>().enabled, Is.True);
		}

		[UnityTest]
		public IEnumerator JapaneseCommandBreadcrumbFitsActualFontMetricsAndRendersEveryPathGlyph()
		{
#if UNITY_EDITOR
			var skin = AssetDatabase.LoadAssetAtPath<DexcRuntimeUISkin>("Assets/DexcDebugger/Font/DexcRuntimeUISkin-NotoSansJP.asset");
			Assert.That(skin, Is.Not.Null);
			DebugMenu.Close();
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = skin);
			DexcRuntimeUIState.SetSession(DebuggerRuntime.CurrentSession.SessionId);
			DebugMenu.Open();
			SelectCommand(DexcUI06JapaneseValues.Path);
			foreach (var size in new[] { new Vector2Int(1366, 768), new Vector2Int(390, 844), new Vector2Int(844, 390) })
			{
				DexcRuntimeUGUIRenderer.RenderProfileForTests(size.x, size.y, new Rect(0f, 0f, size.x, size.y));
				yield return null;
				Canvas.ForceUpdateCanvases();
				var label = CommandBreadcrumbText();
				var textRect = (RectTransform)label.transform;
				var preferred = Convert.ToSingle(DexcUGUIApi.Get(label, "preferredHeight"));
				var generator = (TextGenerator)DexcUGUIApi.Get(label, "cachedTextGenerator");
				var glyphs = GeneratedGlyphBounds(label);
				var text = Convert.ToString(DexcUGUIApi.Get(label, "text"));
				// The path is the header's place in the menu over the command's own name, all of it in the header.
				foreach (var segment in DexcUI06JapaneseValues.Path.Split('/'))
					Assert.That(text, Does.Contain(segment));
				Assert.That(DexcUGUIApi.Get(label, "font"), Is.SameAs(skin.Font));
				Assert.That(preferred, Is.LessThanOrEqualTo(textRect.rect.height + 1f));
				Assert.That(generator.lineCount, Is.GreaterThanOrEqualTo(1));
				Assert.That(glyphs.Length, Is.GreaterThan(0));
				Debug.Log("UI06 F10 header font=" + skin.Font.name + " size=" + size + " local=" + textRect.rect + " preferred=" + preferred);
				foreach (var glyph in glyphs)
				{
					Assert.That(
						ContainsGlyph(DexcRuntimeUGUIRenderer.HeaderScreenRectForTests, glyph),
						Is.True,
						"generated glyph must fit the header"
					);
				}

				Assert.That(DexcRuntimeUGUIRenderer.HasHorizontalOverflowForTests, Is.False);
				var layout = DexcRuntimeUGUIRenderer.CurrentLayoutForTests;
				Assert.That(textRect.parent.GetComponent<RectTransform>().rect.height, Is.GreaterThanOrEqualTo(preferred));
				Assert.That(layout.Header.yMax, Is.EqualTo(layout.Panel.yMax));
				Assert.That(layout.Footer.yMin, Is.EqualTo(layout.Panel.yMin));
				Debug.Log(
					"UI06 F10 font="
						+ skin.Font.name
						+ " size="
						+ size
						+ " textWidth="
						+ textRect.rect.width
						+ " preferredHeight="
						+ preferred
						+ " actualHeight="
						+ textRect.rect.height
						+ " oldHitHeight="
						+ layout.HitHeight
						+ " lines="
						+ generator.lineCount
						+ " visibleGlyphs="
						+ glyphs.Length
						+ " pathTail="
						+ glyphs.Last()
				);
			}
#else
			yield break;
#endif
		}

		internal static Component CommandBreadcrumbText()
		{
			return DexcRuntimeUGUIRenderer
				.RootForTests.GetComponentsInChildren(DexcUGUIApi.TextType, false)
				.Single(item => Convert.ToString(DexcUGUIApi.Get(item, "text")).EndsWith(DexcUI06JapaneseValues.Path.Split('/').Last() + "</b>"));
		}

		internal static Rect TextScreenBounds(Component text)
		{
			var corners = new Vector3[4];
			((RectTransform)text.transform).GetWorldCorners(corners);
			return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
		}

		internal static bool ContainsGlyph(Rect outer, Rect glyph)
		{
			return glyph.width > 0f
				&& glyph.height > 0f
				&& glyph.xMin >= outer.xMin - 0.5f
				&& glyph.yMin >= outer.yMin - 0.5f
				&& glyph.xMax <= outer.xMax + 0.5f
				&& glyph.yMax <= outer.yMax + 0.5f;
		}

		internal static Rect[] GeneratedGlyphBounds(Component text)
		{
			// Read the production CanvasRenderer mesh after its normal rebuild, including Text's
			// pixelsPerUnit/PixelAdjustPoint conversion. Do not populate a substitute generator.
			var mesh = text.GetComponent<CanvasRenderer>().GetMesh();
			Assert.That(mesh, Is.Not.Null);
			var vertices = mesh.vertices;
			var result = new List<Rect>();
			for (var index = 0; index + 3 < vertices.Length; index += 4)
			{
				var points = Enumerable.Range(index, 4).Select(value => text.transform.TransformPoint(vertices[value])).ToArray();
				var glyph = Rect.MinMaxRect(
					points.Min(point => point.x),
					points.Min(point => point.y),
					points.Max(point => point.x),
					points.Max(point => point.y)
				);
				if (glyph.width > 0.01f && glyph.height > 0.01f)
					result.Add(glyph);
			}
			return result.ToArray();
		}

		[Test]
		public void SemanticSelectableStatesAndFocusMarkerUseProductionNavigationState()
		{
			var selectable = DexcRuntimeUGUIRenderer.SelectableForTests(2);
			var palette = DexcRuntimeUGUIRenderer.SemanticPaletteForTests;
			var colors = DexcUGUIApi.Get(selectable, "colors");
			AssertColor((Color)DexcUGUIApi.Get(colors, "normalColor"), palette.Row);
			AssertColor((Color)DexcUGUIApi.Get(colors, "highlightedColor"), palette.Selection);
			AssertColor((Color)DexcUGUIApi.Get(colors, "pressedColor"), palette.Accent);
			AssertColor((Color)DexcUGUIApi.Get(colors, "selectedColor"), palette.Selection);
			AssertColor((Color)DexcUGUIApi.Get(colors, "disabledColor"), palette.Disabled);
			DexcRuntimeUGUIRenderer.SelectForTests(2);
			DexcRuntimeUGUIRenderer.TickActive();
			Assert.That(DexcRuntimeUGUIRenderer.FocusMarkerCountForTests, Is.EqualTo(1));
			var selectedGraphic = (Component)DexcUGUIApi.Get(selectable, "targetGraphic");
			AssertColor(selectedGraphic.GetComponent<CanvasRenderer>().GetColor(), palette.Selection);
			SelectCommand("UI02/LongCommand");
			var countField = Enumerable
				.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
				.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
				.First(DexcUGUIApi.InputFieldType.IsInstanceOfType);
			DexcUGUIApi.Set(countField, "text", "-1");
			Assert.That(DexcRuntimeUGUIRenderer.ExecuteInteractableForTests, Is.False);
			RuntimeUGUIManyInputsCommand.Reject = true;
			DexcRuntimeUGUIRenderer.RefreshForTests();
			var errorSelectable = Enumerable
				.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
				.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
				.First(item =>
					Convert
						.ToString(
							DexcUGUIApi.Get(
								(item.transform.Find("Label") ?? item.transform.parent?.Find("Label"))?.GetComponent(DexcUGUIApi.TextType),
								"text"
							)
						)
						.StartsWith("DestinationSceneSourcePath", StringComparison.Ordinal)
				);
			Assert.That(DexcUGUIApi.InputFieldType.IsInstanceOfType(errorSelectable), Is.True);
			// A field's row sits on the band a step lighter than the panel.
			AssertColor(
				(Color)DexcUGUIApi.Get(errorSelectable.transform.parent.GetComponent(DexcUGUIApi.ImageType), "color"),
				palette.Header
			);
			// The message is written under the value and the field is outlined; the "…" action keeps the row's colours.
			var errorRow = errorSelectable.transform.parent;
			var action = errorRow.Find("Error").GetComponent(DexcUGUIApi.ButtonType);
			Assert.That(action.gameObject.activeSelf, Is.True);
			AssertColor((Color)DexcUGUIApi.Get(DexcUGUIApi.Get(action, "colors"), "normalColor"), palette.Row);
			Assert.That(errorRow.Find("ErrorLine").gameObject.activeSelf, Is.True);
			Assert.That(errorRow.Find("Input/Visual/ErrorOutline").gameObject.activeSelf, Is.True);
			Assert.That(FindSelectable("FooterAction1"), Is.Null, "disabled Execute must not remain a keyboard/gamepad focus candidate");
			var disabledExecute = DexcRuntimeUGUIRenderer.FooterForTests.Find("FooterAction1").GetComponent(DexcUGUIApi.ButtonType);
			var disabledColors = DexcUGUIApi.Get(disabledExecute, "colors");
			AssertColor((Color)DexcUGUIApi.Get(disabledColors, "disabledColor"), palette.Disabled);
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
			DexcRuntimeUGUIRenderer.RefreshForTests();
			DexcRuntimeUGUIRenderer.TickActive();
			Assert.That(
				DexcRuntimeUGUIRenderer.FocusMarkerCountForTests,
				Is.EqualTo(1),
				"a stale input/footer selection must not suppress the rebuilt Home focus"
			);
			var rebuiltHome = DexcRuntimeUGUIRenderer.SelectableForTests(0);
			var rebuiltGraphic = (Component)DexcUGUIApi.Get(rebuiltHome, "targetGraphic");
			AssertColor(rebuiltGraphic.GetComponent<CanvasRenderer>().GetColor(), palette.Selection);
		}

		[Test]
		public void PooledAccentAndErrorRowsResetCompletelyWhenReusedAsHeaderAndNormalInput()
		{
			SelectCommand("UI02/LongCommand");
			var invalid = Enumerable
				.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
				.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
				.First(DexcUGUIApi.InputFieldType.IsInstanceOfType);
			DexcUGUIApi.Set(invalid, "text", "-");
			Assert.That(invalid.transform.parent.Find("ErrorLine").gameObject.activeSelf, Is.True);
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
			DexcRuntimeUGUIRenderer.RefreshForTests();
			Canvas.ForceUpdateCanvases();
			var reused = invalid.transform.parent;
			Assert.That(reused.Find("Input").gameObject.activeSelf, Is.False);
			Assert.That(reused.Find("Error").gameObject.activeSelf, Is.False);
			Assert.That(reused.Find("ErrorLine").gameObject.activeSelf, Is.False);
			Assert.That(reused.GetComponent(DexcUGUIApi.ButtonType).GetType(), Is.EqualTo(DexcUGUIApi.ButtonType));
			RectTransform row0,
				row1;
			DexcRuntimeUIState.Notice = null;
			SelectCommand("UI02/MoreThanPage");
			Canvas.ForceUpdateCanvases();
			row0 = (RectTransform)DexcRuntimeUGUIRenderer.ContentForTests.Find("Row0");
			// The page has no heading row now, so the first row is already the first, selected input.
			row1 = row0;
			var row0Button = row0.GetComponent(DexcUGUIApi.ButtonType);
			// The selected field's whole row is filled; the field inside keeps its own colour.
			AssertColor(
				(Color)DexcUGUIApi.Get(row0.GetComponent(DexcUGUIApi.ImageType), "color"),
				DexcRuntimeUGUIRenderer.SemanticPaletteForTests.Selection
			);
			Assert.That(((Behaviour)row0Button).enabled, Is.False);
			Assert.That(DexcUGUIApi.Get(row0Button, "interactable"), Is.False);
			Assert.That(DexcUGUIApi.Get(row0.GetComponent(DexcUGUIApi.ImageType), "raycastTarget"), Is.False);
			var row1Image = row1.GetComponent(DexcUGUIApi.ImageType);
			var row1Button = row1.GetComponent(DexcUGUIApi.ButtonType);
			var inputRoot = row1.Find("Input").gameObject;
			var inputVisual = inputRoot.transform.Find("Visual").gameObject;
			var input = inputRoot.GetComponent(DexcUGUIApi.InputFieldType);
			AssertColor((Color)DexcUGUIApi.Get(row1Image, "color"), DexcRuntimeUGUIRenderer.SemanticPaletteForTests.Selection);
			Assert.That(DexcUGUIApi.Get(row1Image, "raycastTarget"), Is.False);
			Assert.That(((Behaviour)row1Button).enabled, Is.False);
			Assert.That(DexcUGUIApi.Get(row1Button, "interactable"), Is.False);
			Assert.That(inputRoot.activeSelf, Is.True);
			Assert.That(((Behaviour)input).enabled, Is.True);
			Assert.That(DexcUGUIApi.Get(input, "interactable"), Is.True);
			Assert.That(DexcUGUIApi.Get(inputRoot.GetComponent(DexcUGUIApi.ImageType), "raycastTarget"), Is.True);
			AssertColor((Color)DexcUGUIApi.Get(inputRoot.GetComponent(DexcUGUIApi.ImageType), "color"), Color.clear);
			Assert.That(DexcUGUIApi.Get(inputVisual.GetComponent(DexcUGUIApi.ImageType), "raycastTarget"), Is.False);
			AssertColor((Color)DexcUGUIApi.Get(inputVisual.GetComponent(DexcUGUIApi.ImageType), "color"), Color.white);
			var inputColors = DexcUGUIApi.Get(input, "colors");
			Debug.Log(
				"UI06 reused input: index="
					+ DexcRuntimeUIState.SelectedIndex
					+ ", selected="
					+ DexcUGUIApi.CurrentSelected
					+ ", expected="
					+ input
					+ ", same="
					+ ReferenceEquals(input, DexcUGUIApi.CurrentSelected)
			);
			AssertColor((Color)DexcUGUIApi.Get(inputColors, "normalColor"), DexcRuntimeUGUIRenderer.SemanticPaletteForTests.Row);
			AssertColor((Color)DexcUGUIApi.Get(inputColors, "selectedColor"), DexcRuntimeUGUIRenderer.SemanticPaletteForTests.Row);
			AssertColor(inputVisual.GetComponent<CanvasRenderer>().GetColor(), DexcRuntimeUGUIRenderer.SemanticPaletteForTests.Row);
		}

		[Test]
		public void FieldWindowKeepsPoolBoundedWithoutTreatingTwelveAsPublicLimit()
		{
			SelectCommand("UI02/MoreThanPage");
			Assert.That(DexcRuntimeUGUIRenderer.VisibleRowCount, Is.LessThanOrEqualTo(DexcRuntimeUIState.PageSize));
			var next = Enumerable
				.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
				.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
				.First(item =>
					item != null
					&& Convert
						.ToString(DexcUGUIApi.Get(item.transform.Find("Label")?.GetComponent(DexcUGUIApi.TextType), "text"))
						.StartsWith("Next fields", StringComparison.Ordinal)
				);
			DexcUGUIApi.InvokeButton(next);
			Canvas.ForceUpdateCanvases();
			Assert.That(DexcRuntimeUGUIRenderer.VisibleRowCount, Is.LessThanOrEqualTo(DexcRuntimeUIState.PageSize));
			Assert.That(
				Enumerable.Range(0, DexcRuntimeUGUIRenderer.VisibleRowCount).Select(DexcRuntimeUGUIRenderer.RowTextForTests),
				Has.Some.Contains("Field26")
			);
		}

		[TestCase(390, 844, 28f)]
		[TestCase(844, 390, 28f)]
		[TestCase(1366, 768, 28f)]
		public void ProfileSelectableUsesActualRaycastRootAtMinimumHitHeight(int width, int height, float minimumHitHeight)
		{
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
			DexcRuntimeUGUIRenderer.RenderProfileForTests(width, height, new Rect(0f, 0f, width, height));
			DexcRuntimeUGUIRenderer.RefreshForTests();
			Canvas.ForceUpdateCanvases();
			var selectable = DexcRuntimeUGUIRenderer.SelectableForTests(0);
			Assert.That(selectable, Is.Not.Null);
			Assert.That(((RectTransform)selectable.transform).rect.height, Is.GreaterThanOrEqualTo(minimumHitHeight - 0.01f));
			var image = selectable.GetComponent(DexcUGUIApi.ImageType);
			Assert.That(image, Is.Not.Null);
			Assert.That(DexcUGUIApi.Get(image, "raycastTarget"), Is.True);
		}

#if UNITY_EDITOR
		[UnityTest]
		public IEnumerator ProductionCanvasMatchesApprovedScreenSpaceShellAndSelectableBoundsInAllProfiles()
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
					SelectCommand("UI02/LongCommand");
					DexcRuntimeUGUIRenderer.RenderProfileForTests(
						profile.Width,
						profile.Height,
						new Rect(0f, 0f, profile.Width, profile.Height)
					);
					Canvas.ForceUpdateCanvases();
					yield return null;
					yield return null;
					Canvas.ForceUpdateCanvases();
					Assert.That(DexcRuntimeUGUIRenderer.RendererCountForTests, Is.EqualTo(1));
					AssertScreenRect(DexcRuntimeUGUIRenderer.PanelScreenRectForTests, profile.Panel);
					AssertScreenRect(
						DexcRuntimeUGUIRenderer.HeaderScreenRectForTests,
						new Rect(profile.Panel.x, profile.Panel.yMax - Header(profile), profile.Panel.width, Header(profile))
					);
					AssertScreenRect(
						DexcRuntimeUGUIRenderer.ContentScreenRectForTests,
						new Rect(profile.Panel.x, profile.Panel.y + 40f, profile.Panel.width, profile.Panel.height - 40f - Header(profile))
					);
					AssertScreenRect(
						DexcRuntimeUGUIRenderer.FooterScreenRectForTests,
						new Rect(profile.Panel.x, profile.Panel.y, profile.Panel.width, 40f)
					);
					var inputIndex = Enumerable
						.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
						.First(index => DexcUGUIApi.InputFieldType.IsInstanceOfType(DexcRuntimeUGUIRenderer.SelectableForTests(index)));
					var hit = DexcRuntimeUGUIRenderer.SelectableScreenRectForTests(inputIndex);
					Assert.That(hit.height, Is.GreaterThanOrEqualTo(profile.MinimumHitHeight - 0.01f));
					Assert.That(hit.xMin, Is.GreaterThanOrEqualTo(profile.Panel.xMin));
					Assert.That(hit.xMax, Is.LessThanOrEqualTo(profile.Panel.xMax));
				}
				finally
				{
					restore();
				}

				yield return null;
			}
		}
#endif

		[UnityTest]
		public IEnumerator RuntimeUGUIVisualCaptureWritesApprovedAndExpandedProfileMatrix()
		{
			if (!Environment.GetCommandLineArgs().Any(item => item == "--dexc-visual-capture"))
				yield break;
			var manifestPath = Environment.GetEnvironmentVariable("DEXC_UGUI_CAPTURE_MANIFEST");
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
			var captureCameraObject = new GameObject("UI02CaptureClearCamera");
			var captureCamera = captureCameraObject.AddComponent<Camera>();
			captureCamera.clearFlags = CameraClearFlags.SolidColor;
			captureCamera.backgroundColor = Color.black;
			captureCamera.cullingMask = 0;
			captureCamera.depth = 10000f;
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = skin);
			DexcRuntimeUIState.ResetForSession();
			var visualSession = DebugCommand
				.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 1)))
				.Summary.SessionId;
			DexcRuntimeUIState.SetSession(visualSession);
			DebugMenu.Open();
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(true);

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
					DexcRuntimeUGUIRenderer.RenderProfileForTests(profile.Width, profile.Height, Screen.safeArea);
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
					DexcRuntimeUGUIRenderer.RefreshForTests();
					yield return Capture(profile, "Home", stamp, outputDirectory, captures);

					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
					DexcRuntimeUIState.Search = "UI02/LongCommand";
					DexcRuntimeUGUIRenderer.RefreshForTests();
					yield return Capture(profile, "CommandList", stamp, outputDirectory, captures);
					SelectCommand("UI02/LongCommand");
					yield return Capture(profile, "CommandDetailDraft", stamp, outputDirectory, captures);
					DexcRuntimeUIRenderer.ExecuteCommandForTests();
					DexcRuntimeUGUIRenderer.RefreshForTests();
					yield return Capture(profile, "ResultState", stamp, outputDirectory, captures);
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.History);
					DexcRuntimeUGUIRenderer.RefreshForTests();
					yield return Capture(profile, "History", stamp, outputDirectory, captures);

					SelectCommand("UI02/LongCommand");
					DexcRuntimeUGUIRenderer.SelectForTests(Mathf.Max(0, DexcRuntimeUGUIRenderer.SelectableCount - 5));
					DexcRuntimeUGUIRenderer.TickActive();
					yield return Capture(profile, "MultiInput12", stamp, outputDirectory, captures);
					RuntimeUGUIManyInputsCommand.Reject = true;
					DexcRuntimeUGUIRenderer.RefreshForTests();
					var semanticField = FindInputSelectable("Mode");
					DexcRuntimeUGUIRenderer.SelectForTests(semanticField);
					DexcRuntimeUGUIRenderer.TickActive();
					ScrollSelectableIntoContent(semanticField);
					var invalidFieldBounds = DexcRuntimeUGUIRenderer.SelectableScreenRectForTests(semanticField);
					var validationContent = DexcRuntimeUGUIRenderer.ContentScreenRectForTests;
					Assert.That(invalidFieldBounds.yMin, Is.GreaterThanOrEqualTo(validationContent.yMin - 0.01f));
					Assert.That(invalidFieldBounds.yMax, Is.LessThanOrEqualTo(validationContent.yMax + 0.01f));
					yield return Capture(profile, "Validation", stamp, outputDirectory, captures);
					RuntimeUGUIManyInputsCommand.Reject = false;
					DexcRuntimeUIState.Notice = string.Empty;
					DexcRuntimeUGUIRenderer.RefreshForTests();
					SelectCommand("UI02/Cancellable");
					DexcRuntimeUIState.Notice = "Source Path: Scene/Streaming/Regions/West/VeryLongDestinationReference";
					DexcRuntimeUGUIRenderer.RefreshForTests();
					yield return Capture(profile, "LongTextReference", stamp, outputDirectory, captures);
					DexcRuntimeUIState.Notice = string.Empty;
					DexcRuntimeUGUIRenderer.RefreshForTests();
					DexcRuntimeUIRenderer.ExecuteCommandForTests();
					DexcRuntimeUGUIRenderer.RefreshForTests();
					yield return Capture(profile, "Running", stamp, outputDirectory, captures);
					ResetVisualScenarioState();
				}
				finally
				{
					restore();
				}
			}

			File.WriteAllText(
				manifestPath,
				JsonUtility.ToJson(
					new CaptureManifest
					{
						Version = 2,
						Renderer = "Runtime uGUI",
						Source = "DexcRuntimeUGUIRenderer production Canvas -> ScreenCapture.CaptureScreenshotAsTexture",
						Captures = captures.ToArray(),
					},
					true
				)
			);
			Object.DestroyImmediate(captureCameraObject);
			Object.DestroyImmediate(skin);
			Assert.That(captures, Has.Count.EqualTo(27));
		}

		private static IEnumerator Capture(
			CaptureProfile profile,
			string scenario,
			string stamp,
			string outputDirectory,
			ICollection<CaptureEntry> captures
		)
		{
			DexcRuntimeUGUIRenderer.SetCanvasVisibleForCaptureTests(false);
			Canvas.ForceUpdateCanvases();
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();
			DexcRuntimeUGUIRenderer.SetCanvasVisibleForCaptureTests(true);
			DexcRuntimeUGUIRenderer.TickActive();
			ApplyCaptureScenarioViewport(scenario);
			Canvas.ForceUpdateCanvases();
			yield return null;
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();
			ApplyCaptureScenarioViewport(scenario);
			Canvas.ForceUpdateCanvases();
			var texture = ScreenCapture.CaptureScreenshotAsTexture();
			try
			{
				var bytes = texture.EncodeToPNG();
				var path = Path.Combine(outputDirectory, "UI02-" + profile.Name + "-" + scenario + "-" + stamp + ".png");
				File.WriteAllBytes(path, bytes);
				var pixels = texture.GetPixels32();
				var background = pixels[0];
				var nonBackground = pixels.Count(pixel =>
					Math.Abs(pixel.r - background.r) > 3 || Math.Abs(pixel.g - background.g) > 3 || Math.Abs(pixel.b - background.b) > 3
				);
				var layout = DexcRuntimeUGUIRenderer.CurrentLayoutForTests;
				var panel = DexcRuntimeUGUIRenderer.PanelScreenRectForTests;
				var header = DexcRuntimeUGUIRenderer.HeaderScreenRectForTests;
				var content = DexcRuntimeUGUIRenderer.ContentScreenRectForTests;
				var footer = DexcRuntimeUGUIRenderer.FooterScreenRectForTests;
				AssertScreenRect(panel, layout.Panel);
				AssertScreenRect(header, layout.Header);
				AssertScreenRect(content, layout.Content);
				if (layout.Footer.height > 0f)
					AssertScreenRect(footer, layout.Footer);
				captures.Add(
					new CaptureEntry
					{
						Profile = profile.Name,
						Scenario = scenario,
						ArtifactPath = Path.GetFullPath(path),
						Sha256 = Sha256(bytes),
						Width = texture.width,
						Height = texture.height,
						Panel = new CaptureRect(panel),
						Header = new CaptureRect(header),
						Content = new CaptureRect(content),
						Footer = new CaptureRect(footer),
						RowHeight = layout.RowHeight,
						HitHeight = layout.HitHeight,
						VisibleRows = DexcRuntimeUGUIRenderer.VisibleRowCount,
						VerticalOverflow = DexcRuntimeUGUIRenderer.HasVerticalOverflowForTests,
						HorizontalOverflow = DexcRuntimeUGUIRenderer.HasHorizontalOverflowForTests,
						HeaderFixed = Math.Abs(layout.Header.yMax - layout.Panel.yMax) < 0.01f,
						FooterFixed = layout.Footer.height <= 0f || Math.Abs(layout.Footer.yMin - layout.Panel.yMin) < 0.01f,
						ExecuteEnabled = DexcRuntimeUGUIRenderer.ExecuteInteractableForTests,
						CancelBounds = new CaptureRect(DexcRuntimeUGUIRenderer.CancelBoundsForTests),
						Page = DexcRuntimeUIState.Page.ToString(),
						Notice = DexcRuntimeUIState.Notice ?? string.Empty,
						AsyncRunning = DexcRuntimeUIState.AsyncExecution != null,
						SelectedIndex = DexcRuntimeUIState.SelectedIndex,
						VerticalNormalizedPosition = DexcRuntimeUGUIRenderer.VerticalNormalizedPositionForTests,
						NonBackgroundPixels = nonBackground,
						RendererCount = DexcRuntimeUGUIRenderer.RendererCountForTests,
						SettledFrames = 2,
					}
				);
			}
			finally
			{
				Object.DestroyImmediate(texture);
			}
		}

		private static void ApplyCaptureScenarioViewport(string scenario)
		{
			if (scenario == "Validation")
				ScrollSelectableIntoContent(FindInputSelectable("Mode"));
		}

		private static int FindInputSelectable(string label)
		{
			return Enumerable
				.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
				.First(index =>
				{
					var selectable = DexcRuntimeUGUIRenderer.SelectableForTests(index);
					// A field is an input or a choice; the choice's button sits inside the input's area.
					if (
						selectable == null
						|| !DexcUGUIApi.InputFieldType.IsInstanceOfType(selectable) && selectable.name != "ChoiceButton"
					)
						return false;
					var row = selectable.transform;
					while (row != null && row.Find("Label") == null)
						row = row.parent;
					var labelTransform = row == null ? null : row.Find("Label");
					var text =
						labelTransform == null
							? string.Empty
							: Convert.ToString(DexcUGUIApi.Get(labelTransform.GetComponent(DexcUGUIApi.TextType), "text"));
					return text.IndexOf(label, StringComparison.Ordinal) >= 0;
				});
		}

		private static void ScrollSelectableIntoContent(int index)
		{
			for (var attempt = 0; attempt < 4; attempt++)
			{
				Canvas.ForceUpdateCanvases();
				var selectable = DexcRuntimeUGUIRenderer.SelectableScreenRectForTests(index);
				var viewport = DexcRuntimeUGUIRenderer.ContentScreenRectForTests;
				var scrollable = Mathf.Max(
					1f,
					DexcRuntimeUGUIRenderer.ContentForTests.rect.height - DexcRuntimeUGUIRenderer.ViewportForTests.rect.height
				);
				var normalized = DexcRuntimeUGUIRenderer.VerticalNormalizedPositionForTests;
				if (selectable.yMin < viewport.yMin)
					normalized -= (viewport.yMin - selectable.yMin) / scrollable;
				else if (selectable.yMax > viewport.yMax)
					normalized += (selectable.yMax - viewport.yMax) / scrollable;
				else
					return;
				DexcUGUIApi.Set(DexcRuntimeUGUIRenderer.ScrollRectForTests, "verticalNormalizedPosition", Mathf.Clamp01(normalized));
			}
			Canvas.ForceUpdateCanvases();
		}

		private static void ResetVisualScenarioState()
		{
			RuntimeUGUIManyInputsCommand.Reject = false;
			DexcRuntimeUIState.Notice = string.Empty;
			DexcRuntimeUIState.Search = string.Empty;
			DexcRuntimeUIState.ScrollPosition = Vector2.zero;
			DexcRuntimeUIState.ListScrollPosition = Vector2.zero;
			DexcRuntimeUIState.ResetForSession();
			DebugMenu.Open();
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.Home);
		}

		private void EnsureEventSystem()
		{
			if (Resources.FindObjectsOfTypeAll(DexcUGUIApi.EventSystemType).Length != 0)
				return;
			m_EventSystemObject = new GameObject("UI02EventSystem");
			m_EventSystemObject.AddComponent(DexcUGUIApi.EventSystemType);
		}

		private static void SelectCommand(string search)
		{
			var item = DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, search, new DebugPageRequest(0, 8))).Items.Single();
			DexcRuntimeUIRenderer.SelectCommandForTests(item);
			DexcRuntimeUGUIRenderer.RefreshForTests();
			Canvas.ForceUpdateCanvases();
		}

		private static Component FindSelectable(string name)
		{
			return Enumerable
				.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
				.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
				.FirstOrDefault(item => item != null && item.gameObject.name == name);
		}

		private static void InvokePointerClick(Component selectable)
		{
			Assert.That(selectable, Is.Not.Null);
			var eventSystem = DexcUGUIApi
				.EventSystemType.GetProperty("current", BindingFlags.Public | BindingFlags.Static)
				.GetValue(null, null);
			var eventData = Activator.CreateInstance(Resolve("UnityEngine.EventSystems.PointerEventData"), eventSystem);
			selectable.GetType().GetMethod("OnPointerClick").Invoke(selectable, new[] { eventData });
		}

		private static void InvokeSubmit(Component selectable)
		{
			Assert.That(selectable, Is.Not.Null);
			var eventSystem = DexcUGUIApi
				.EventSystemType.GetProperty("current", BindingFlags.Public | BindingFlags.Static)
				.GetValue(null, null);
			var eventData = Activator.CreateInstance(Resolve("UnityEngine.EventSystems.BaseEventData"), eventSystem);
			selectable.GetType().GetMethod("OnSubmit").Invoke(selectable, new[] { eventData });
		}

		private static Type Resolve(string name)
		{
			return AppDomain
				.CurrentDomain.GetAssemblies()
				.Select(assembly => assembly.GetType(name, false))
				.FirstOrDefault(type => type != null);
		}

		private static DexcRuntimeUISkin CreateVisualSkin()
		{
			var skin = ScriptableObject.CreateInstance<DexcRuntimeUISkin>();
#if UNITY_EDITOR
			skin.SetWorldVisualShaderForEditor(
				AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader")
			);
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
			var size = constructor.Invoke(new object[] { fixedResolution, width, height, "Dexc UI02 " + profile });
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

		private static void AssertScreenRect(Rect actual, Rect expected)
		{
			Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.01f));
			Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.01f));
			Assert.That(actual.width, Is.EqualTo(expected.width).Within(0.01f));
			Assert.That(actual.height, Is.EqualTo(expected.height).Within(0.01f));
		}

		private static void AssertColor(Color actual, Color expected)
		{
			Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f));
			Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f));
			Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f));
			Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f));
		}

		private sealed class RecordingProvider : DebugMenuInputProvider
		{
			internal readonly List<DebugMenuInputState> States = new List<DebugMenuInputState>();

			public override DebugMenuInputFrame ReadInput()
			{
				return new DebugMenuInputFrame(Vector2.zero, false, false);
			}

			public override void OnInputStateChanged(DebugMenuInputState state)
			{
				States.Add(state);
			}
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
			public CaptureRect Panel;
			public CaptureRect Header;
			public CaptureRect Content;
			public CaptureRect Footer;
			public float RowHeight;
			public float HitHeight;
			public int VisibleRows;
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
			public float VerticalNormalizedPosition;
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
		"Tests/RuntimeUI/UI02/LongCommandNameThatMustWrapWithoutHorizontalOverflow",
		Description = "Twelve parameters with a very long Source Path and string payload",
		Access = DebugCommandAccess.ReadOnly
	)]
	public sealed class RuntimeUGUIManyInputsCommand : DebugCommand
	{
		public static bool Reject;
		public InputInt Count = new InputInt(24);
		public InputFloat Radius = new InputFloat(12.5f);
		public InputBool IncludeInactive = new InputBool(true);
		public InputVector3 CenterPosition = new InputVector3(new Vector3(128f, 4.5f, -92f));
		public InputString DestinationSceneSourcePath = new InputString("Scenes/Streaming/Regions/West/VeryLongDestinationSceneName.unity");
		public InputEnum<RuntimeUGUIMode> Mode = new InputEnum<RuntimeUGUIMode>(RuntimeUGUIMode.Combat);
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
					"ui02-invalid-source",
					"The selected Source Path is stale. Choose a valid destination before Execute."
				);
			if (Count.Value < 0)
				validation.Error(Count, "ui02-invalid-count", "Count must be zero or greater before Execute.");
			validation.Warning(Mode, "ui02-mode-warning", "Review the immediate mode before execution.");
		}

		protected override void Execute() { }
	}

	[DebugCommandAttribute("Tests/RuntimeUI/UI02/Cancellable", Access = DebugCommandAccess.ReadOnly)]
	public sealed class RuntimeUGUICancellableCommand : AsyncDebugCommand
	{
		protected override TimeSpan Timeout => System.Threading.Timeout.InfiniteTimeSpan;

		protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
		}
	}

	[DebugCommandAttribute("Tests/RuntimeUI/UI02/MoreThanPage", Access = DebugCommandAccess.ReadOnly)]
	public sealed class RuntimeUGUIMoreThanPageCommand : DebugCommand
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

	public enum RuntimeUGUIMode
	{
		Combat,
		Passive,
	}
}
#endif
