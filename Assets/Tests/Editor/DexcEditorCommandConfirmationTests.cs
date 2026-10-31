#if DEXC_DEBUGGER
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcEditorCommandConfirmationTests
	{
		private DexcDebuggerWindow m_Window;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			yield return new EnterPlayMode();
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize();
			EditorConfirmedCommand.s_Count = 0;
			EditorConfirmedCommand.s_Value = 0;
			EditorConfirmedCommand.s_Reject = false;
			EditorConfirmedCommand.s_Reenter = null;
			EditorConfirmedAsyncCommand.s_Count = 0;
			EditorImmediateCommand.s_Count = 0;
			m_Window = CreateWindow();
			Select("Sync");
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			EditorConfirmedCommand.s_Reenter = null;
			if (m_Window != null)
			{
				m_Window.Close();
			}
			DebuggerRuntime.ResetForTests();
			if (EditorApplication.isPlaying)
			{
				yield return new ExitPlayMode();
			}
		}

		[UnityTest]
		public IEnumerator ProductionButtonsCancelConfirmAndExecuteAgainWithoutDuplicateDispatch()
		{
			SetValue(7);
			yield return Repaint();
			Click(m_Window.CommandPrimaryRectForTests);
			Assert.That(EditorConfirmedCommand.s_Count, Is.Zero, "first Execute must wait for approval");
			Assert.That(m_Window.CommandConfirmingForTests, Is.True);
			m_Window.ExecuteSelectedCommandForTests();
			m_Window.ConfirmCommandForTests();
			Assert.That(EditorConfirmedCommand.s_Count, Is.Zero, "same frame cannot approve");
			yield return Repaint();
			Assert.That(m_Window.CommandConfirmingForTests, Is.True, "ordinary repaint must retain approval");
			Click(m_Window.CommandCancelRectForTests);
			Assert.That(m_Window.CommandConfirmingForTests, Is.False);
			Assert.That(HistoryCount(), Is.Zero);
			yield return Repaint();
			Click(m_Window.CommandPrimaryRectForTests);
			yield return Repaint();
			EditorConfirmedCommand.s_Reenter = () => m_Window.ExecuteSelectedCommandForTests();
			Click(m_Window.CommandPrimaryRectForTests);
			m_Window.ConfirmCommandForTests();
			m_Window.ExecuteSelectedCommandForTests();
			Assert.That(EditorConfirmedCommand.s_Count, Is.EqualTo(1));
			Assert.That(EditorConfirmedCommand.s_Value, Is.EqualTo(7));
			Assert.That(HistoryCount(), Is.EqualTo(1));
			yield return Repaint();
			Click(m_Window.CommandPrimaryRectForTests);
			yield return Repaint();
			Assert.That(m_Window.CommandConfirmingForTests, Is.True, "Execute again also requires approval");
			m_Window.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
			Assert.That(m_Window.CommandConfirmingForTests, Is.False);
			Assert.That(HistoryCount(), Is.EqualTo(1));
		}

		[UnityTest]
		public IEnumerator InvalidStateAndChangedOrDiscardedDraftCannotApprove()
		{
			EditorConfirmedCommand.s_Reject = true;
			m_Window.ExecuteSelectedCommandForTests();
			Assert.That(m_Window.CommandConfirmingForTests, Is.False);
			EditorConfirmedCommand.s_Reject = false;
			m_Window.ExecuteSelectedCommandForTests();
			yield return null;
			EditorConfirmedCommand.s_Reject = true;
			m_Window.ConfirmCommandForTests();
			Assert.That(m_Window.CommandConfirmingForTests, Is.False);
			EditorConfirmedCommand.s_Reject = false;
			m_Window.ExecuteSelectedCommandForTests();
			yield return null;
			var input = Input();
			DebugCommand.Draft.UpdateInput(m_Window.SelectedDraftForTests, input.Id, DebugValue.From(9));
			m_Window.ConfirmCommandForTests();
			Assert.That(m_Window.CommandNoticeForTests, Does.Contain("changed"));
			m_Window.ExecuteSelectedCommandForTests();
			yield return null;
			SetValue(11);
			Assert.That(m_Window.CommandConfirmingForTests, Is.False);
			m_Window.ConfirmCommandForTests();
			m_Window.ExecuteSelectedCommandForTests();
			yield return null;
			DebugCommand.Draft.Discard(m_Window.SelectedDraftForTests);
			m_Window.ConfirmCommandForTests();
			Assert.That(EditorConfirmedCommand.s_Count, Is.Zero);
			Assert.That(HistoryCount(), Is.Zero);
		}

		[UnityTest]
		public IEnumerator NavigationSelectionSessionPlayModeAndCloseDiscardApproval()
		{
			foreach (
				var invalidate in new Action[]
				{
					() => m_Window.SelectHistoryPageForTests(),
					() => Select("Immediate"),
					() =>
						m_Window.SelectSettingForTests(
							DebugSetting
								.GetList(new DebugListQuery("Tests/Volume", string.Empty, new DebugPageRequest(0, 8)))
								.Items.Single()
								.Id
						),
					() => DebuggerRuntime.Initialize(),
					() => m_Window.NotifyPlayModeStateForTests(PlayModeStateChange.ExitingPlayMode),
				}
			)
			{
				Select("Sync");
				m_Window.ExecuteSelectedCommandForTests();
				Assert.That(m_Window.CommandConfirmingForTests, Is.True);
				yield return null;
				invalidate();
				Assert.That(m_Window.CommandConfirmingForTests, Is.False);
				m_Window.ConfirmCommandForTests();
			}
			Select("Sync");
			m_Window.ExecuteSelectedCommandForTests();
			yield return null;
			var closedWindow = m_Window;
			m_Window.Close();
			Assert.That(closedWindow.CommandConfirmingForTests, Is.False);
			closedWindow.ConfirmCommandForTests();
			Assert.That(EditorConfirmedCommand.s_Count, Is.Zero);
		}

		[UnityTest]
		public IEnumerator AsyncCommandRequiresApprovalAndRunningCancelRemainsUsable()
		{
			Select("Async");
			m_Window.ExecuteSelectedCommandForTests();
			Assert.That(EditorConfirmedAsyncCommand.s_Count, Is.Zero);
			yield return null;
			m_Window.ConfirmCommandForTests();
			Assert.That(EditorConfirmedAsyncCommand.s_Count, Is.EqualTo(1));
			m_Window.ExecuteSelectedCommandForTests();
			Assert.That(m_Window.CommandExecutionEnabledForTests, Is.False);
			yield return Repaint();
			Click(m_Window.CommandCancelRectForTests);
			var deadline = Time.realtimeSinceStartup + 5f;
			while (m_Window.LastCommandExecutionForTests == null && Time.realtimeSinceStartup < deadline)
			{
				m_Window.ProcessEditorUpdateForTests();
				yield return null;
			}
			Assert.That(m_Window.LastCommandExecutionForTests, Is.Not.Null);
			Assert.That(EditorConfirmedAsyncCommand.s_Count, Is.EqualTo(1));
			Assert.That(HistoryCount(), Is.EqualTo(1));
		}

		[UnityTest]
		public IEnumerator WindowsKeepIndependentApprovalAndNoMessageExecutesImmediately()
		{
			var other = CreateWindow();
			try
			{
				Assert.That(other.SelectCommandForTests(Command("Sync").Id), Is.True);
				m_Window.ExecuteSelectedCommandForTests();
				other.ExecuteSelectedCommandForTests();
				yield return null;
				m_Window.CancelConfirmationForTests();
				Assert.That(other.CommandConfirmingForTests, Is.True);
				other.ConfirmCommandForTests();
				Assert.That(EditorConfirmedCommand.s_Count, Is.EqualTo(1));
			}
			finally
			{
				other.Close();
			}
			Select("Immediate");
			m_Window.ExecuteSelectedCommandForTests();
			m_Window.ExecuteSelectedCommandForTests();
			Assert.That(m_Window.CommandConfirmingForTests, Is.False);
			Assert.That(EditorImmediateCommand.s_Count, Is.EqualTo(1));
		}

		[UnityTest]
		public IEnumerator RepeatedSelectionAndHistoryReuseKeepOnlyOwnedDrafts()
		{
			m_Window.RefreshCommandPageForTests();
			var store = DraftStore();
			Assert.That(store.Count, Is.EqualTo(1));
			SetValue(7);
			Assert.That(DebugCommand.Draft.CreateRequest(m_Window.SelectedDraftForTests).TryGetRequest(out var request), Is.True);
			Assert.That(request.Dispatch().Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
			m_Window.RefreshHistoryForTests();
			var history = m_Window.CommandHistoryForTests.Entries.Single().Id;
			for (var i = 0; i < 30; i++)
			{
				var previous = m_Window.SelectedDraftForTests;
				Select(i % 2 == 0 ? "Immediate" : "Sync");
				Assert.That(DebugCommand.Draft.Validate(previous).Status, Is.EqualTo(DebugCommandDraftOperationStatus.Stale));
				Assert.That(store.Count, Is.EqualTo(1), "command selection must release the previous draft");
				previous = m_Window.SelectedDraftForTests;
				Assert.That(m_Window.ReuseCommandHistoryForTests(history), Is.True);
				Assert.That(DebugCommand.Draft.Validate(previous).Status, Is.EqualTo(DebugCommandDraftOperationStatus.Stale));
				Assert.That(store.Count, Is.EqualTo(1), "history reuse must release the previous draft");
				Assert.That(m_Window.CommandValueForTests(Input().Id), Is.EqualTo(DebugValue.From(7)));
			}
			m_Window.Close();
			m_Window = null;
			Assert.That(store.Count, Is.Zero);
			Assert.That(
				request.Dispatch().Status,
				Is.EqualTo(CommandExecutionStatus.Succeeded),
				"value request remains usable after its draft is discarded"
			);
			Assert.That(EditorConfirmedCommand.s_Value, Is.EqualTo(7));
			yield return null;
		}

		[UnityTest]
		public IEnumerator FailedHistoryPreparationReleasesTemporaryDraftAndKeepsCurrentInput()
		{
			SetValue(7);
			Assert.That(DebugCommand.Draft.CreateRequest(m_Window.SelectedDraftForTests).TryGetRequest(out var request), Is.True);
			request.Dispatch();
			m_Window.RefreshHistoryForTests();
			var history = m_Window.CommandHistoryForTests.Entries.Single().Id;
			SetValue(11);
			var previous = m_Window.SelectedDraftForTests;
			var store = DraftStore();
			EditorConfirmedCommand.s_Reject = true;
			try
			{
				for (var i = 0; i < 20; i++)
				{
					Assert.That(m_Window.ReuseCommandHistoryForTests(history), Is.False);
					Assert.That(m_Window.SelectedDraftForTests, Is.EqualTo(previous));
					Assert.That(m_Window.CommandValueForTests(Input().Id), Is.EqualTo(DebugValue.From(11)));
					Assert.That(store.Count, Is.EqualTo(1));
				}
			}
			finally
			{
				EditorConfirmedCommand.s_Reject = false;
			}
			Assert.That(m_Window.SelectCommandForTests(default(DebugCommandId)), Is.False);
			Assert.That(m_Window.SelectedDraftForTests, Is.EqualTo(previous));
			Select("Immediate");
			previous = m_Window.SelectedDraftForTests;
			typeof(DexcDebuggerWindow).GetField("m_Commands", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(m_Window, null);
			Assert.That(
				m_Window.ReuseCommandHistoryForTests(history),
				Is.True,
				"History identity resolves independently of the current page."
			);
			Assert.That(m_Window.SelectedCommandPathForTests, Is.EqualTo("Tests/FIX02E/Sync"));
			Assert.That(m_Window.SelectedDraftForTests, Is.Not.EqualTo(previous));
			Assert.That(m_Window.CommandValueForTests(Input().Id), Is.EqualTo(DebugValue.From(7)));
			Assert.That(store.Count, Is.EqualTo(1));
			yield return null;
		}

		[UnityTest]
		public IEnumerator WindowAndSessionCleanupDoNotDiscardOtherOwnersDrafts()
		{
			var store = DraftStore();
			Assert.That(DebugCommand.Draft.Create(Command("Sync").Id).TryGetDraftId(out var external), Is.True);
			var other = CreateWindow();
			try
			{
				Assert.That(other.SelectCommandForTests(Command("Immediate").Id), Is.True);
				var otherDraft = other.SelectedDraftForTests;
				Assert.That(store.Count, Is.EqualTo(3));
				m_Window.NotifyPlayModeStateForTests(PlayModeStateChange.ExitingPlayMode);
				Assert.That(store.Count, Is.EqualTo(2));
				Assert.That(DebugCommand.Draft.Validate(otherDraft).Status, Is.EqualTo(DebugCommandDraftOperationStatus.Succeeded));
				Assert.That(DebugCommand.Draft.Validate(external).Status, Is.EqualTo(DebugCommandDraftOperationStatus.Succeeded));
				Select("Sync");
				m_Window.Close();
				m_Window = null;
				Assert.That(store.Count, Is.EqualTo(2));
				DebuggerRuntime.Initialize();
				Assert.That(store.Contains(otherDraft), Is.False, "session notification releases the window-owned draft in the old store");
				Assert.That(other.SelectedDraftIsValidForTests, Is.False);
				Assert.That(DebugCommand.Draft.Validate(external).Status, Is.EqualTo(DebugCommandDraftOperationStatus.Stale));
				Assert.That(DraftStore().Count, Is.Zero);
			}
			finally
			{
				other.Close();
			}
			yield return null;
		}

		private static IDictionary DraftStore() =>
			(IDictionary)
				typeof(DexcCommandDraftStore)
					.GetField("m_Drafts", BindingFlags.Instance | BindingFlags.NonPublic)
					.GetValue(DexcCommandRuntime.Drafts);

		private static DexcDebuggerWindow CreateWindow()
		{
			var window = ScriptableObject.CreateInstance<DexcDebuggerWindow>();
			window.position = new Rect(0, 0, 1100, 700);
			window.ShowUtility();
			window.ProcessEditorUpdateForTests();
			return window;
		}

		private static DebugCommandListItem Command(string name) =>
			DebugCommand
				.Registry.GetList(new DebugListQuery("Tests/FIX02E/" + name, string.Empty, new DebugPageRequest(0, 8)))
				.Items.Single();

		private void Select(string name) => Assert.That(m_Window.SelectCommandForTests(Command(name).Id), Is.True);

		private DebugCommandFieldDescriptor Input()
		{
			DebugCommand.Registry.TryGetDescriptor(Command("Sync").Id, out var descriptor);
			return descriptor.Fields.Single(field => field.IsInput);
		}

		private void SetValue(int value) => Assert.That(m_Window.UpdateCommandInputForTests(Input().Id, DebugValue.From(value)), Is.True);

		private int HistoryCount()
		{
			m_Window.RefreshHistoryForTests();
			return m_Window.CommandHistoryForTests.Entries.Count;
		}

		private IEnumerator Repaint()
		{
			m_Window.Repaint();
			yield return null;
			Assert.That(m_Window.CommandPrimaryRectForTests.width, Is.GreaterThan(0));
			Assert.That(m_Window.CommandPrimaryRectForTests.height, Is.GreaterThan(0));
		}

		private void Click(Rect rect)
		{
			Assert.That(rect.width, Is.GreaterThan(0));
			Assert.That(rect.height, Is.GreaterThan(0));
			m_Window.SendEvent(
				new Event
				{
					type = EventType.MouseDown,
					button = 0,
					mousePosition = rect.center,
				}
			);
			m_Window.SendEvent(
				new Event
				{
					type = EventType.MouseUp,
					button = 0,
					mousePosition = rect.center,
				}
			);
		}
	}

	[DebugCommandAttribute(
		"Tests/FIX02E/Sync",
		ConfirmationMessage = "Apply the Editor debug change?",
		Access = DebugCommandAccess.ReadOnly
	)]
	public sealed class EditorConfirmedCommand : DebugCommand
	{
		internal static int s_Count;
		internal static int s_Value;
		internal static bool s_Reject;
		internal static Action s_Reenter;
		public InputInt Value = new InputInt(3);

		protected override void Validate(DebugCommandValidation validation)
		{
			if (s_Reject)
			{
				validation.Error(Value, "unavailable", "Cannot execute now.");
			}
		}

		protected override void Execute()
		{
			s_Count++;
			s_Value = Value.Value;
			s_Reenter?.Invoke();
		}
	}

	[DebugCommandAttribute(
		"Tests/FIX02E/Async",
		ConfirmationMessage = "Start the Editor debug operation?",
		Access = DebugCommandAccess.ReadOnly
	)]
	public sealed class EditorConfirmedAsyncCommand : AsyncDebugCommand
	{
		internal static int s_Count;
		protected override TimeSpan Timeout => System.Threading.Timeout.InfiniteTimeSpan;

		protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			s_Count++;
			await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
		}
	}

	[DebugCommandAttribute("Tests/FIX02E/Immediate", Access = DebugCommandAccess.ReadOnly)]
	public sealed class EditorImmediateCommand : DebugCommand
	{
		internal static int s_Count;

		protected override void Execute()
		{
			s_Count++;
		}
	}
}
#endif
