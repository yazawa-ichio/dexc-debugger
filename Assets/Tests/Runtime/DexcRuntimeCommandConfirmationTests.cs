#if DEXC_DEBUGGER && UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcRuntimeCommandConfirmationTests
	{
		private DexcRuntimeUISkin m_Skin;
		private GameObject m_EventSystem;
		private int m_Renderer;

		[SetUp]
		public void SetUp()
		{
			DexcRuntimeUIState.ResetForSession();
			DebuggerRuntime.ResetForTests();
			Fix02ConfirmedCommand.s_ExecutionCount = 0;
			Fix02ConfirmedCommand.s_Reject = false;
			Fix02ConfirmedAsyncCommand.s_ExecutionCount = 0;
			m_Skin = ScriptableObject.CreateInstance<DexcRuntimeUISkin>();
			m_Skin.SetWorldVisualShaderForEditor(
				AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader")
			);
			m_Skin.SetUIToolkitPanelSettingsTemplateForEditor(
				AssetDatabase.LoadAssetAtPath<PanelSettings>(
					"Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitPanelSettings.asset"
				)
			);
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			DexcRuntimeUIState.SetSession(DebuggerRuntime.CurrentSession.SessionId);
		}

		[TearDown]
		public void TearDown()
		{
			DexcRuntimeUIState.ResetForSession();
			DebuggerRuntime.ResetForTests();
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(false);
			DexcRuntimeUIToolkitRenderer.ClearProfileOverrideForTests();
			DexcRuntimeUIToolkitRenderer.DestroyForTests();
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(false);
			DexcRuntimeUGUIRenderer.ClearProfileOverrideForTests();
#endif
			if (m_EventSystem != null)
				Object.DestroyImmediate(m_EventSystem);
			Object.DestroyImmediate(m_Skin);
		}

		[UnityTest]
		public IEnumerator IMGUIConfirmation() => VerifyRenderer(0);

#if DEXC_HAS_UGUI
		[UnityTest]
		public IEnumerator UGUIConfirmation() => VerifyRenderer(1);
#endif

		[UnityTest]
		public IEnumerator UIToolkitConfirmation() => VerifyRenderer(2);

		[Test]
		public void DirectRequestDispatchDoesNotRequireUIConfirmation()
		{
			Select("Sync");
			var creation = DebugCommand.Draft.CreateRequest(DexcRuntimeUIState.DraftId);
			Assert.That(creation.TryGetRequest(out var request), Is.True);
			request.Dispatch();
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.EqualTo(1));
			Assert.That(DexcRuntimeUIState.Confirming, Is.False);
			Assert.That(HistoryCount(), Is.EqualTo(1));
		}

		[Test]
		public void InvalidExecutionRequestPreservesValidationNotice()
		{
			DebugMenu.Open();
			Select("Sync");
			Fix02ConfirmedCommand.s_Reject = true;
			DexcRuntimeCommandExecution.Request();
			Assert.That(DexcRuntimeUIState.Notice, Is.EqualTo("Command validation failed."));
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.Zero);
			Assert.That(HistoryCount(), Is.Zero);
		}

		[Test]
		public void RepeatedSelectionReleasesOldDraftButKeepsExternalDraftAndRequest()
		{
			DebugMenu.Open();
			Select("Sync");
			var item = DexcRuntimeUIState.Command;
			Assert.That(DebugCommand.Draft.Create(item.Id).TryGetDraftId(out var external), Is.True);
			Assert.That(DebugCommand.Draft.CreateRequest(DexcRuntimeUIState.DraftId).TryGetRequest(out var request), Is.True);
			var store = DraftStore();
			for (var i = 0; i < 50; i++)
			{
				var previous = DexcRuntimeUIState.DraftId;
				Select(i % 2 == 0 ? "Async" : "Sync");
				Assert.That(DebugCommand.Draft.Validate(previous).Status, Is.EqualTo(DebugCommandDraftOperationStatus.Stale));
				Assert.That(store.Count, Is.EqualTo(2));
				Assert.That(DebugCommand.Draft.Validate(external).Status, Is.EqualTo(DebugCommandDraftOperationStatus.Succeeded));
			}
			DebugMenu.Close();
			Assert.That(store.Count, Is.EqualTo(1));
			Assert.That(request.Dispatch().Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.EqualTo(1));
			DebugCommand.Draft.Discard(external);
			Assert.That(store.Count, Is.Zero);
		}

		[Test]
		public void StaleSelectionKeepsCurrentDraftAndSessionInvalidationReleasesIt()
		{
			var stale = DebugCommand
				.Registry.GetList(new DebugListQuery("Tests/FIX02/Async", string.Empty, new DebugPageRequest(0, 8)))
				.Items.Single();
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			DexcRuntimeUIState.SetSession(DebuggerRuntime.CurrentSession.SessionId);
			DebugMenu.Open();
			Select("Sync");
			var previous = DexcRuntimeUIState.DraftId;
			var descriptor = DexcRuntimeUIState.Command;
			var field = descriptor.Fields.Single(input => input.IsInput).Id;
			DexcRuntimeUIState.CommandInputText[field] = "unfinished";
			DexcRuntimeUIRenderer.SelectCommandForTests(stale);
			Assert.That(DexcRuntimeUIState.DraftId, Is.EqualTo(previous));
			Assert.That(DexcRuntimeUIState.Command, Is.SameAs(descriptor));
			Assert.That(DexcRuntimeUIState.CommandId, Is.EqualTo(descriptor.Id));
			Assert.That(DexcRuntimeUIState.CommandInputText[field], Is.EqualTo("unfinished"));
			var store = DraftStore();
			Assert.That(store.Count, Is.EqualTo(1));
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			Assert.That(store.Count, Is.Zero);
			Assert.That(DexcRuntimeUIState.HasDraft, Is.False);
			Assert.That(DraftStore().Count, Is.Zero);
			DexcRuntimeUIState.SetSession(DebuggerRuntime.CurrentSession.SessionId);
			DebugMenu.Open();
			var provider = new DraftLifetimeInputProvider();
			DexcRuntimeUIState.SetInputProvider(provider);
			DexcRuntimeUIState.ClearInputProvider(provider);
			Select("Sync");
			store = DraftStore();
			Assert.That(store.Count, Is.EqualTo(1));
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			Assert.That(store.Count, Is.Zero, "removing a provider must not unsubscribe UI session cleanup");
			Assert.That(DexcRuntimeUIState.HasDraft, Is.False);
		}

		private sealed class DraftLifetimeInputProvider : DebugMenuInputProvider
		{
			public override DebugMenuInputFrame ReadInput() => default(DebugMenuInputFrame);
		}

		private static IDictionary DraftStore() =>
			(IDictionary)
				typeof(DexcCommandDraftStore)
					.GetField("m_Drafts", BindingFlags.Instance | BindingFlags.NonPublic)
					.GetValue(DexcCommandRuntime.Drafts);

		private void ConfigureRenderer(int renderer)
		{
			m_Renderer = renderer;
			DexcRuntimeUIRendererSelection.SetUseUIToolkitForTests(renderer == 2);
#if DEXC_HAS_UGUI
			DexcRuntimeUIRendererSelection.SetUseUGUIForTests(renderer == 1);
			if (renderer == 1)
			{
				if (Resources.FindObjectsOfTypeAll(DexcUGUIApi.EventSystemType).Length == 0)
				{
					m_EventSystem = new GameObject("FIX02 EventSystem");
					m_EventSystem.AddComponent(DexcUGUIApi.EventSystemType);
				}
				DexcRuntimeUGUIRenderer.CreateForBootstrap();
			}
#endif
			if (renderer == 2)
				DexcRuntimeUIToolkitRenderer.CreateForBootstrap();
			DebugMenu.Open();
		}

		private IEnumerator VerifyRenderer(int renderer)
		{
			ConfigureRenderer(renderer);
			Select("Sync");
			yield return Refresh();
			Primary();
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.Zero, "first control activation must not execute");
			Assert.That(DexcRuntimeUIState.Confirming, Is.True);
			DexcRuntimeUIRenderer.ExecuteCommandForTests();
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.Zero, "a second execute request cannot approve");
			yield return Refresh();
			AssertConfirmationVisible();
			Secondary();
			Assert.That(DexcRuntimeUIState.Confirming, Is.False);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail));
			Assert.That(HistoryCount(), Is.Zero, "cancel creates no result or history");
			yield return Refresh();
			Primary();
			yield return Refresh();
			Primary();
			Primary();
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.EqualTo(1), "confirm plus duplicate/reentrant activation executes once");
			Assert.That(HistoryCount(), Is.EqualTo(1));
			yield return Refresh();
			Primary();
			yield return Refresh();
			Assert.That(DexcRuntimeUIState.Confirming, Is.True, "Execute again must confirm too");
			AssertConfirmationVisible();
			if (renderer == 2)
				DexcRuntimeUIToolkitRenderer.SendKeyForTests(KeyCode.Escape);
			else
			{
				DexcRuntimeUIRenderer.QueueProviderInputForTests(new DebugMenuInputFrame(Vector2.zero, false, true));
				DexcRuntimeUIRenderer.ProcessProviderInputForTests();
			}
			Assert.That(DexcRuntimeUIState.Confirming, Is.False);
			Assert.That(DexcRuntimeUIState.Page, Is.EqualTo(DexcRuntimeUIPage.CommandDetail), "Back dismisses only the confirmation");
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.EqualTo(1));

			// Revalidation rejects new game-state failures and draft changes after the prompt.
			Select("Sync");
			Fix02ConfirmedCommand.s_Reject = true;
			yield return Refresh();
			Primary();
			Assert.That(DexcRuntimeUIState.Confirming, Is.False);
			Fix02ConfirmedCommand.s_Reject = false;
			yield return Refresh();
			Primary();
			yield return Refresh();
			Fix02ConfirmedCommand.s_Reject = true;
			Primary();
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.EqualTo(1));
			Fix02ConfirmedCommand.s_Reject = false;
			DexcRuntimeCommandExecution.CancelConfirmation();
			yield return Refresh();
			Primary();
			yield return Refresh();
			var field = DexcRuntimeUIState.Command.Fields.Single(item => item.IsInput);
			DebugCommand.Draft.UpdateInput(DexcRuntimeUIState.DraftId, field.Id, DebugValue.From(99));
			Primary();
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.EqualTo(1), "changed draft requires another prompt");
			Assert.That(DexcRuntimeUIState.Confirming, Is.False);
			Assert.That(HistoryCount(), Is.EqualTo(1));

			yield return Refresh();
			Primary();
			DexcRuntimeCommandExecution.Confirm();
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.EqualTo(1), "same-frame second activation cannot approve");
			yield return Refresh();
			DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
			DexcRuntimeCommandExecution.Confirm();
			Assert.That(DexcRuntimeUIState.Confirming, Is.False);
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.EqualTo(1), "navigation discards pending approval");
			Select("Sync");
			yield return Refresh();
			Primary();
			yield return Refresh();
			DexcRuntimeUIState.DiscardDraft();
			DexcRuntimeCommandExecution.Confirm();
			Assert.That(DexcRuntimeUIState.Confirming, Is.False);
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.EqualTo(1), "discarded draft cannot execute");

			Select("Async");
			yield return Refresh();
			Primary();
			Assert.That(Fix02ConfirmedAsyncCommand.s_ExecutionCount, Is.Zero);
			yield return Refresh();
			Primary();
			Assert.That(Fix02ConfirmedAsyncCommand.s_ExecutionCount, Is.EqualTo(1));
			DexcRuntimeCommandExecution.Request();
			DexcRuntimeCommandExecution.Confirm();
			Assert.That(Fix02ConfirmedAsyncCommand.s_ExecutionCount, Is.EqualTo(1), "Running cannot launch again");
			DexcRuntimeUIState.AsyncExecution.Cancel();
			var completion = DexcRuntimeUIState.AsyncTask;
			var deadline = Time.realtimeSinceStartup + 5f;
			while (!completion.IsCompleted && Time.realtimeSinceStartup < deadline)
			{
				yield return null;
			}
			Assert.That(completion.IsCompleted, Is.True, "cancel must finish the asynchronous command");
			DexcRuntimeUIState.ResetForSession();
			DexcRuntimeUIState.SetSession(DebuggerRuntime.CurrentSession.SessionId);
			DebugMenu.Open();
			Select("Sync");
			yield return Refresh();
			Primary();
			yield return Refresh();
			DebugMenu.Close();
			DexcRuntimeCommandExecution.Confirm();
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.EqualTo(1));
			DebugMenu.Open();
			Select("Sync");
			yield return Refresh();
			Primary();
			yield return Refresh();
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			DexcRuntimeCommandExecution.Confirm();
			Assert.That(Fix02ConfirmedCommand.s_ExecutionCount, Is.EqualTo(1), "expired session cannot execute");
		}

		[UnityTest]
		public IEnumerator IMGUIInvalidTextCannotExecuteOldValue() => VerifyInvalidText(0);

#if DEXC_HAS_UGUI
		[UnityTest]
		public IEnumerator UGUIInvalidTextCannotExecuteOldValue() => VerifyInvalidText(1);
#endif

		[UnityTest]
		public IEnumerator UIToolkitInvalidTextCannotExecuteOldValue() => VerifyInvalidText(2);

		private IEnumerator VerifyInvalidText(int renderer)
		{
			ConfigureRenderer(renderer);
			var previous = CultureInfo.CurrentCulture;
			try
			{
				foreach (var culture in new[] { "ja-JP", "en-US", "de-DE" })
				{
					CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
					var command = DebugCommand
						.Registry.GetList(new DebugListQuery("Tests/ZZFIX03/Input", string.Empty, new DebugPageRequest(0, 8)))
						.Items.Single();
					DexcRuntimeUIRenderer.SelectCommandForTests(command);
					Fix03InputCommand.s_Count = 0;
					var historyBefore = HistoryCount();
					yield return Refresh();
					var amount = DexcRuntimeUIState.Command.Fields.Single(field => field.Name == "Amount");
					var count = DexcRuntimeUIState.Command.Fields.Single(field => field.Name == "Count");
					Assert.That(DexcRuntimeUIRenderer.GetCommandInputText(amount, "2.5"), Is.EqualTo("2.5"));
					foreach (var invalid in new[] { "", "-", "oops", "2,5", "1e309" })
					{
						EditInput(amount, invalid);
						Assert.That(DexcRuntimeUIState.CommandInputText[amount.Id], Is.EqualTo(invalid));
						Assert.That(DexcRuntimeCommandExecution.CanExecute(), Is.False, culture + " " + invalid);
						if (renderer == 2)
							Assert.That(DexcRuntimeUIToolkitRenderer.SafeAreaRootForTests.Q<Button>("FooterPrimary").enabledSelf, Is.False);
#if DEXC_HAS_UGUI
						if (renderer == 1)
							Assert.That(DexcRuntimeUGUIRenderer.ExecuteInteractableForTests, Is.False);
#endif
						Primary();
						DexcRuntimeUIRenderer.ExecuteCommandForTests();
						Assert.That(Fix03InputCommand.s_Count, Is.Zero);
						Assert.That(HistoryCount(), Is.EqualTo(historyBefore));
					}
					if (renderer != 0)
					{
						var offset = DexcRuntimeUIState.Command.Fields.Single(field => field.Name == "Offset");
						EditComponent("X", "1,25");
						EditComponent("Y", "2,5");
						Assert.That(DexcRuntimeCommandExecution.CanExecute(), Is.False);
						EditComponent("X", "1.25");
						EditComponent("Y", "2.5");
						Assert.That(DexcRuntimeUIState.CommandInvalidInput.Contains(offset.Id), Is.False);
					}
					EditInput(count, "2147483648");
					EditInput(amount, "2.5");
					Assert.That(DexcRuntimeCommandExecution.CanExecute(), Is.False, "another rejected field must continue to block");
					EditInput(count, "7");
					Assert.That(DexcRuntimeCommandExecution.CanExecute(), Is.True, "correction to the previous draft value must unblock");
					EditInput(count, "-2147483649");
					Assert.That(DexcRuntimeCommandExecution.CanExecute(), Is.False);
					EditInput(count, "2147483647");
					EditInput(amount, "1.");
					yield return Refresh();
					Assert.That(DexcRuntimeUIState.CommandInputText[amount.Id], Is.EqualTo("1."));
					Primary();
					Assert.That(Fix03InputCommand.s_Count, Is.EqualTo(1));
					Assert.That(Fix03InputCommand.s_Amount, Is.EqualTo(1d));
					Assert.That(Fix03InputCommand.s_Integer, Is.EqualTo(int.MaxValue));
					yield return Refresh();
					DexcRuntimeUIRenderer.SelectCommandForTests(command);
					yield return Refresh();
					EditInput(amount, "-");
					DexcRuntimeUIState.Navigate(DexcRuntimeUIPage.CommandList);
					DexcRuntimeUIRenderer.SelectCommandForTests(command);
					Assert.That(DexcRuntimeUIState.CommandInvalidInput, Is.Empty);
				}
				DexcRuntimeUIRenderer.UpdateCommandInputText(DexcRuntimeUIState.Command.Fields.First(field => field.IsInput), "-");
				DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = m_Skin);
				DexcRuntimeUIState.SetSession(DebuggerRuntime.CurrentSession.SessionId);
				Assert.That(DexcRuntimeUIState.CommandInvalidInput, Is.Empty);
			}
			finally
			{
				CultureInfo.CurrentCulture = previous;
			}
		}

		private void EditInput(DebugCommandFieldDescriptor field, string text)
		{
			if (m_Renderer == 2)
			{
				var input = DexcRuntimeUIToolkitRenderer
					.SafeAreaRootForTests.Query<TextField>()
					.ToList()
					.Single(item => item.userData != null && item.style.display.value != DisplayStyle.None && item.label == field.Label);
				input.value = text;
				Assert.That(input.value, Is.EqualTo(text));
			}
#if DEXC_HAS_UGUI
			else if (m_Renderer == 1)
			{
				var input = Enumerable
					.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
					.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
					.Where(DexcUGUIApi.InputFieldType.IsInstanceOfType)
					.Single(item =>
						(string)DexcUGUIApi.Get(item.transform.parent.Find("Label").GetComponent(DexcUGUIApi.TextType), "text")
						== field.Label
					);
				DexcUGUIApi.Set(input, "text", text);
				Assert.That(DexcUGUIApi.Get(input, "text"), Is.EqualTo(text));
			}
#endif
			else
				DexcRuntimeUIRenderer.UpdateCommandInputText(field, text);
		}

		private void EditComponent(string component, string text)
		{
			if (m_Renderer == 2)
			{
				var input = DexcRuntimeUIToolkitRenderer
					.SafeAreaRootForTests.Query<TextField>()
					.ToList()
					.Single(item => item.userData != null && item.style.display.value != DisplayStyle.None && item.label == component);
				input.value = text;
				Assert.That(input.value, Is.EqualTo(text));
			}
#if DEXC_HAS_UGUI
			else if (m_Renderer == 1)
			{
				var input = Enumerable
					.Range(0, DexcRuntimeUGUIRenderer.SelectableCount)
					.Select(DexcRuntimeUGUIRenderer.SelectableForTests)
					.Where(DexcUGUIApi.InputFieldType.IsInstanceOfType)
					.Single(item =>
						(string)DexcUGUIApi.Get(item.transform.parent.Find("ComponentLabel").GetComponent(DexcUGUIApi.TextType), "text")
						== component
					);
				DexcUGUIApi.Set(input, "text", text);
				Assert.That(DexcUGUIApi.Get(input, "text"), Is.EqualTo(text));
			}
#endif
		}

		private static void Select(string name)
		{
			var item = DebugCommand
				.Registry.GetList(new DebugListQuery("Tests/FIX02/" + name, string.Empty, new DebugPageRequest(0, 8)))
				.Items.Single();
			DexcRuntimeUIRenderer.SelectCommandForTests(item);
		}

		private static int HistoryCount() =>
			DebugCommand.Draft.GetHistory(DexcRuntimeUIState.CommandId, new DebugPageRequest(0, 20)).Entries.Count;

		private IEnumerator Refresh()
		{
			if (m_Renderer == 2)
				DexcRuntimeUIToolkitRenderer.RenderProfileForTests(390, 844, new Rect(0, 0, 390, 844));
#if DEXC_HAS_UGUI
			if (m_Renderer == 1)
				DexcRuntimeUGUIRenderer.RenderProfileForTests(390, 844, new Rect(0, 0, 390, 844));
#endif
			Canvas.ForceUpdateCanvases();
			yield return null;
			yield return null;
		}

		/// <summary>
		/// Asks the renderer to run the command. The other renderers have a footer button for it, but IMGUI drives the
		/// selected row, and the command detail page offers no Execute row while the command cannot run - the row there is
		/// History. Activating it would walk off the page instead of asking for execution, so ask for it directly.
		/// </summary>
		private void Primary()
		{
			if (m_Renderer != 0)
			{
				ClickFooter(true);
				return;
			}
			if (
				DexcRuntimeUIState.Page == DexcRuntimeUIPage.CommandDetail
				&& !DexcRuntimeUIState.Confirming
				&& DexcRuntimeUIState.AsyncExecution == null
				&& !DexcRuntimeCommandExecution.CanExecute()
			)
			{
				DexcRuntimeUIRenderer.ExecuteCommandForTests();
				return;
			}
			// The footer's actions follow the page's rows in the gamepad's order.
			DexcRuntimeUIState.SetSelectedIndex(DexcRuntimeUIRenderer.DetailRowCountForTests);
			DexcRuntimeUIRenderer.ActivateProviderSelection();
		}

		private void Secondary()
		{
			if (m_Renderer == 0)
			{
				DexcRuntimeUIState.SetSelectedIndex(DexcRuntimeUIRenderer.DetailRowCountForTests + 1);
				DexcRuntimeUIRenderer.ActivateProviderSelection();
			}
			else
				ClickFooter(false);
		}

		private void ClickFooter(bool primary)
		{
			if (m_Renderer == 2)
			{
				var button = DexcRuntimeUIToolkitRenderer.SafeAreaRootForTests.Q<Button>(primary ? "FooterPrimary" : "FooterSecondary");
				using (
					var pointer = PointerUpEvent.GetPooled(
						new Event
						{
							type = EventType.MouseUp,
							button = 0,
							mousePosition = button.worldBound.center,
						}
					)
				)
				{
					pointer.target = button;
					button.SendEvent(pointer);
				}
			}
#if DEXC_HAS_UGUI
			else
			{
				var button = DexcRuntimeUGUIRenderer
					.FooterForTests.Find(primary ? "FooterAction1" : "FooterAction0")
					.GetComponent(DexcUGUIApi.ButtonType);
				DexcUGUIApi.InvokeButton(button);
			}
#endif
		}

		private void AssertConfirmationVisible()
		{
			if (m_Renderer == 2)
			{
				var root = DexcRuntimeUIToolkitRenderer.SafeAreaRootForTests;
				Assert.That(
					root.Query<Label>()
						.ToList()
						.Any(label => label.text.Contains(Fix02ConfirmedCommand.Message) && label.worldBound.height > 0),
					Is.True
				);
				Assert.That(root.Q<Button>("FooterPrimary").text, Is.EqualTo("Confirm"));
				Assert.That(root.Q<Button>("FooterSecondary").text, Is.EqualTo("Cancel"));
				Assert.That(root.Q<Button>("FooterPrimary").worldBound.height, Is.GreaterThan(0));
			}
#if DEXC_HAS_UGUI
			else if (m_Renderer == 1)
			{
				var labels = DexcRuntimeUGUIRenderer.RootForTests.GetComponentsInChildren(DexcUGUIApi.TextType);
				Assert.That(
					labels.Any(label =>
						((string)DexcUGUIApi.Get(label, "text")).Contains(Fix02ConfirmedCommand.Message)
						&& ((RectTransform)label.transform).rect.height > 0
					),
					Is.True
				);
				Assert.That(labels.Any(label => (string)DexcUGUIApi.Get(label, "text") == "Confirm"), Is.True);
				Assert.That(labels.Any(label => (string)DexcUGUIApi.Get(label, "text") == "Cancel"), Is.True);
			}
#endif
		}
	}

	[DebugCommandAttribute("Tests/ZZFIX03/Input", Access = DebugCommandAccess.ReadOnly)]
	public sealed class Fix03InputCommand : DebugCommand
	{
		internal static int s_Count;
		internal static int s_Integer;
		internal static double s_Amount;
		public InputInt Count = new InputInt(7);
		public InputDouble Amount = new InputDouble(2.5);
		public InputVector2 Offset = new InputVector2(new Vector2(1.25f, 2.5f));

		protected override void Execute()
		{
			s_Count++;
			s_Integer = Count.Value;
			s_Amount = Amount.Value;
		}
	}

	[DebugCommandAttribute("Tests/FIX02/Sync", ConfirmationMessage = Message, Access = DebugCommandAccess.ReadOnly)]
	public sealed class Fix02ConfirmedCommand : DebugCommand
	{
		public const string Message = "Apply this debug change?";
		internal static int s_ExecutionCount;
		internal static bool s_Reject;
		public InputInt Count = new InputInt(7);

		protected override void Validate(DebugCommandValidation validation)
		{
			if (s_Reject)
				validation.Error(Count, "fix02-unavailable", "Not available now.");
		}

		protected override void Execute()
		{
			s_ExecutionCount++;
			DexcRuntimeCommandExecution.Request();
		}
	}

	[DebugCommandAttribute("Tests/FIX02/Async", ConfirmationMessage = "Start async debug change?", Access = DebugCommandAccess.ReadOnly)]
	public sealed class Fix02ConfirmedAsyncCommand : AsyncDebugCommand
	{
		internal static int s_ExecutionCount;
		protected override TimeSpan Timeout => System.Threading.Timeout.InfiniteTimeSpan;

		protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			s_ExecutionCount++;
			await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
		}
	}
}
#endif
