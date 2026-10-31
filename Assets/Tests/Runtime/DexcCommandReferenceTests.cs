#if DEXC_DEBUGGER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcCommandReferenceTests
	{
		internal static GameObject s_First;
		internal static GameObject s_Second;
		internal static bool s_SourceThrows;
		internal static bool s_NotifyThrows;
		internal static int s_ExecutedFirst;
		internal static int s_ExecutedSecond;

		[SetUp]
		public void SetUp()
		{
			s_SourceThrows = false;
			s_NotifyThrows = false;
			s_ExecutedFirst = 0;
			s_ExecutedSecond = 0;
			s_First = new GameObject("FIX05 first");
			s_Second = new GameObject("FIX05 second");
			DebuggerRuntime.Initialize();
		}

		[TearDown]
		public void TearDown()
		{
			s_SourceThrows = false;
			s_NotifyThrows = false;
			DebuggerRuntime.ResetForTests();
			Object.DestroyImmediate(s_First);
			Object.DestroyImmediate(s_Second);
			s_First = null;
			s_Second = null;
		}

		internal static IEnumerable<InputChoiceEntry<GameObject>> Source()
		{
			yield return new InputChoiceEntry<GameObject>("same", s_First, "First", "Scene");
			if (s_SourceThrows)
			{
				throw new InvalidOperationException("source-fault");
			}
			yield return new InputChoiceEntry<GameObject>("second", s_Second, "Second", "Scene");
		}

		[Test]
		public void RepeatedPagesReuseHandlesAndDoNotRegisterFilteredCandidates()
		{
			var draft = Draft("Sync", out var descriptor);
			var field = Field(descriptor, "First");
			var page = Page(draft, field, "First", 0, 1);
			var handle = page.Entries.Single().Handle;
			for (var i = 0; i < 100; i++)
			{
				Assert.That(Page(draft, field, "First", 0, 1).Entries.Single().Handle, Is.EqualTo(handle));
			}
			Assert.That(ReferenceCount(), Is.EqualTo(1));
			Assert.That(Page(draft, field, "missing", 0, 1).Entries, Is.Empty);
			Assert.That(Page(draft, field, "", 10, 1).Entries, Is.Empty);
			Assert.That(ReferenceCount(), Is.EqualTo(1));
			Assert.That(Page(draft, field, "", 1, 1).Entries.Single().Handle, Is.Not.EqualTo(handle));
			Assert.That(ReferenceCount(), Is.EqualTo(2));
			DebugCommand.Draft.Discard(draft);
			draft = Draft("Sync", out descriptor);
			Assert.That(Page(draft, Field(descriptor, "First"), "First", 0, 1).Entries.Single().Handle, Is.EqualTo(handle));
		}

		[Test]
		public void OptionalReferenceRunsWithNothingSelected()
		{
			var draft = Draft("Optional", out _);
			Assert.That(DebugCommand.Draft.CreateRequest(draft).TryGetRequest(out var request), Is.True);
			Assert.That(request.Dispatch().Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
			Assert.That(s_ExecutedFirst, Is.EqualTo(-1), "the command saw no selection");
		}

		[Test]
		public void KeyedReferenceListsTheInspectorSourcesWithThatKey()
		{
			var draft = Draft("Keyed", out var descriptor);
			var field = Field(descriptor, "Target");
			Assert.That(Page(draft, field).Entries.Select(entry => entry.Label), Is.EquivalentTo(new[] { "First", "Second" }));
			Assert.That(Page(draft, field).Entries.Select(entry => entry.Path).Distinct(), Is.EqualTo(new[] { "Tests/ZZFIX05Ref/Keyed/Scene" }));
			Assert.That(DebugCommand.Draft.CreateRequest(draft).TryGetRequest(out _), Is.False, "a required reference needs a selection");
			Select(draft, field, "Second");
			Assert.That(DebugCommand.Draft.CreateRequest(draft).TryGetRequest(out var request), Is.True);
			Assert.That(request.Dispatch().Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
			Assert.That(s_ExecutedFirst, Is.EqualTo(s_Second.GetInstanceID()));
		}

		[DebugInspectorSource("Tests/ZZFIX05Ref/Keyed", Key = "zzfix05-keyed")]
		internal static IEnumerable<DebugSourceEntry<GameObject>> KeyedSource()
		{
			if (s_First == null)
				yield break;
			yield return new DebugSourceEntry<GameObject>(s_First, "First", "Scene");
			yield return new DebugSourceEntry<GameObject>(s_Second, "Second", "Scene");
		}

		[Test]
		public void FailedSourceDoesNotAccumulatePartialHandlesAndCanRecover()
		{
			var draft = Draft("Sync", out var descriptor);
			s_SourceThrows = true;
			for (var i = 0; i < 5; i++)
			{
				Assert.That(Page(draft, Field(descriptor, "First")).Status, Is.EqualTo(DebugOperationStatus.Failed));
			}
			Assert.That(ReferenceCount(), Is.Zero);
			s_SourceThrows = false;
			Assert.That(Page(draft, Field(descriptor, "First")).Entries.Count, Is.EqualTo(2));
		}

		[Test]
		public void DraftRequestPreservesBothObjectsAfterReselectDiscardAndSourceSwap()
		{
			var draft = SelectedDraft(out var descriptor);
			var firstId = s_First.GetInstanceID();
			var secondId = s_Second.GetInstanceID();
			Assert.That(DebugCommand.Draft.CreateRequest(draft).TryGetRequest(out var request), Is.True);
			Select(draft, Field(descriptor, "First"), "Second");
			DebugCommand.Draft.ClearReference(draft, Field(descriptor, "Second"));
			DebugCommand.Draft.Discard(draft);
			SwapSource();
			s_SourceThrows = true;
			Assert.That(request.Dispatch().Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
			Assert.That(s_ExecutedFirst, Is.EqualTo(firstId));
			Assert.That(s_ExecutedSecond, Is.EqualTo(secondId));
		}

		[Test]
		public void DestroyedSelectionCannotFallBackToReplacementWithSameKey()
		{
			var draft = SelectedDraft(out var descriptor);
			var handle = Page(draft, Field(descriptor, "First"), "First").Entries.Single().Handle;
			Assert.That(DebugCommand.Draft.CreateRequest(draft).TryGetRequest(out var request), Is.True);
			ReplaceFirst();
			var replacement = Page(draft, Field(descriptor, "First"), "First").Entries.Single().Handle;
			Assert.That(replacement, Is.Not.EqualTo(handle));
			Assert.That(ReferenceCount(), Is.EqualTo(2), "destroyed object registration is removed");
			Assert.That(
				DebugCommand.Draft.SelectReference(draft, Field(descriptor, "First"), handle).Status,
				Is.EqualTo(DebugCommandDraftOperationStatus.Rejected)
			);
			Assert.That(DebugCommand.Draft.CreateRequest(draft).Status, Is.EqualTo(DebugCommandRequestCreationStatus.ValidationFailed));
			var result = request.Dispatch();
			Assert.That(result.FailureKind, Is.EqualTo(CommandFailureKind.MissingReference));
			Assert.That(s_ExecutedFirst, Is.Zero);
			Select(draft, Field(descriptor, "First"), "First");
			Assert.That(DebugCommand.Draft.CreateRequest(draft).Status, Is.EqualTo(DebugCommandRequestCreationStatus.Created));
		}

		[Test]
		public void TypedSyncRequestKeepsObjectAndSuppressesOutputWhenDestroyed()
		{
			var command = new Fix05ReferenceOutputCommand();
			command.Target.Key = "same";
			var request = DebugCommand.Draft.CreateRequest(command);
			var expected = s_First.GetInstanceID();
			SwapSource();
			Assert.That(request.Dispatch().TryGetOutput(out var output), Is.True);
			Assert.That(output, Is.EqualTo(expected));
			Object.DestroyImmediate(s_Second);
			var failed = request.Dispatch();
			Assert.That(failed.FailureKind, Is.EqualTo(CommandFailureKind.MissingReference));
			Assert.That(failed.TryGetOutput(out output), Is.False);
			Assert.That(output, Is.Zero);
		}

		[UnityTest]
		public IEnumerator TypedAndUntypedAsyncRequestsKeepObjectsAndRejectDestroyedTargets()
		{
			var typed = new Fix05ReferenceAsyncOutputCommand();
			typed.Target.Key = "same";
			var typedRequest = DebugCommand.Draft.CreateRequest(typed);
			var draft = Draft("Async", out var descriptor);
			Select(draft, Field(descriptor, "Target"), "First");
			Assert.That(DebugCommand.Draft.CreateRequest(draft).TryGetRequest(out var untypedRequest), Is.True);
			var expected = s_First.GetInstanceID();
			DebugCommand.Draft.Discard(draft);
			SwapSource();
			var typedTask = typedRequest.DispatchAsync().AsTask();
			while (!typedTask.IsCompleted)
				yield return null;
			Assert.That(typedTask.Result.TryGetOutput(out var output), Is.True);
			Assert.That(output, Is.EqualTo(expected));
			var task = ((AsyncDebugCommandRequest)untypedRequest).DispatchAsync().AsTask();
			while (!task.IsCompleted)
				yield return null;
			Assert.That(task.Result.Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
			Assert.That(s_ExecutedFirst, Is.EqualTo(expected));
			Object.DestroyImmediate(s_Second);
			typedTask = typedRequest.DispatchAsync().AsTask();
			while (!typedTask.IsCompleted)
				yield return null;
			Assert.That(typedTask.Result.FailureKind, Is.EqualTo(CommandFailureKind.MissingReference));
			Assert.That(typedTask.Result.TryGetOutput(out output), Is.False);
			task = ((AsyncDebugCommandRequest)untypedRequest).DispatchAsync().AsTask();
			while (!task.IsCompleted)
				yield return null;
			Assert.That(task.Result.FailureKind, Is.EqualTo(CommandFailureKind.MissingReference));
		}

		[Test]
		public void HistoryRestoresSameObjectsAndKeepsMissingSelectionAfterDestruction()
		{
			var draft = SelectedDraft(out var descriptor);
			Assert.That(DebugCommand.Draft.CreateRequest(draft).TryGetRequest(out var request), Is.True);
			request.Dispatch();
			var history = DebugCommand.Draft.GetHistory(descriptor.Id, new DebugPageRequest(0, 8)).Entries.Single().Id;
			var first = s_First.GetInstanceID();
			SwapSource();
			Assert.That(DebugCommand.Draft.CreateFromHistory(history).TryGetDraftId(out var restored), Is.True);
			Assert.That(DebugCommand.Draft.CreateRequest(restored).TryGetRequest(out var restoredRequest), Is.True);
			restoredRequest.Dispatch();
			Assert.That(s_ExecutedFirst, Is.EqualTo(first));
			Object.DestroyImmediate(s_Second);
			var missing = DebugCommand.Draft.CreateFromHistory(history);
			Assert.That(missing.TryGetDraftId(out restored), Is.True);
			Assert.That(missing.Validation.CanExecute, Is.False);
			Assert.That(DexcCommandRuntime.Drafts.GetInputValue(restored, Field(descriptor, "First")), Is.EqualTo(DebugValue.From("same")));
			Assert.That(DebugCommand.Draft.CreateRequest(restored).Status, Is.EqualTo(DebugCommandRequestCreationStatus.ValidationFailed));
		}

		[Test]
		public void RejectedNotificationRestoresReferenceIdentityAndDraftVersion()
		{
			var draft = SelectedDraft(out var descriptor);
			Assert.That(DebugCommand.Draft.CreateRequest(draft).TryGetRequest(out var before), Is.True);
			var first = s_First.GetInstanceID();
			SwapSource();
			s_NotifyThrows = true;
			var update = DebugCommand.Draft.UpdateInput(draft, Field(descriptor, "Amount"), DebugValue.From(4));
			s_NotifyThrows = false;
			Assert.That(update.Status, Is.EqualTo(DebugCommandDraftOperationStatus.Rejected));
			Assert.That(DebugCommand.Draft.CreateRequest(draft).TryGetRequest(out var after), Is.True);
			Assert.That(after.DraftVersion, Is.EqualTo(before.DraftVersion));
			after.Dispatch();
			Assert.That(s_ExecutedFirst, Is.EqualTo(first));
			var command = new Fix05ReferenceCommand();
			command.First.Key = "same";
			command.Second.Key = "second";
			Assert.That(command.First.TryGetValue(out var target), Is.True);
			SwapSource();
			s_NotifyThrows = true;
			Assert.Throws<TargetInvocationException>(() => DebugCommand.Draft.NotifyInputsChanged(command, command.Amount));
			s_NotifyThrows = false;
			Assert.That(command.First.TryGetValue(out var restored), Is.True);
			Assert.That(restored, Is.SameAs(target));
		}

		[Test]
		public void IncompatibleHandleIsRejectedWithoutChangingTheSelectedObject()
		{
			var draft = Draft("Transform", out var descriptor);
			var field = Field(descriptor, "Target");
			Select(draft, field, "Transform");
			var otherDraft = Draft("Sync", out var other);
			var incompatible = Page(otherDraft, Field(other, "First"), "First").Entries.Single().Handle;
			Assert.That(
				DebugCommand.Draft.SelectReference(draft, field, incompatible).Status,
				Is.EqualTo(DebugCommandDraftOperationStatus.Rejected)
			);
			Assert.That(DebugCommand.Draft.CreateRequest(draft).TryGetRequest(out var request), Is.True);
			Assert.That(request.Dispatch().Output, Is.EqualTo(DebugValue.From(s_First.transform.GetInstanceID())));
		}

		[Test]
		public void SessionEndClearsStoresAndInvalidatesRequestsAndHandles()
		{
			var draft = SelectedDraft(out var descriptor);
			Assert.That(DebugCommand.Draft.CreateRequest(draft).TryGetRequest(out var request), Is.True);
			var store = DexcCommandRuntime.References;
			var drafts = DexcCommandRuntime.Drafts;
			var handle = Page(draft, Field(descriptor, "First"), "First").Entries.Single().Handle;
			DebuggerRuntime.Initialize();
			Assert.That(Entries(store).Count, Is.Zero);
			Assert.That(
				(
					(IDictionary)
						typeof(DexcCommandDraftStore).GetField("m_Drafts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(drafts)
				).Count,
				Is.Zero
			);
			Assert.That(request.Dispatch().ErrorCode, Is.EqualTo("stale-request"));
			draft = Draft("Sync", out descriptor);
			Assert.That(
				DebugCommand.Draft.SelectReference(draft, Field(descriptor, "First"), handle).Status,
				Is.EqualTo(DebugCommandDraftOperationStatus.Rejected)
			);
		}

		private static DebugCommandDraftId Draft(string name, out DebugCommandDescriptor descriptor)
		{
			var item = DebugCommand
				.Registry.GetList(new DebugListQuery("Tests/ZZFIX05Ref/" + name, "", new DebugPageRequest(0, 8)))
				.Items.Single();
			DebugCommand.Registry.TryGetDescriptor(item.Id, out descriptor);
			Assert.That(DebugCommand.Draft.Create(item.Id).TryGetDraftId(out var draft), Is.True);
			return draft;
		}

		private static DebugCommandDraftId SelectedDraft(out DebugCommandDescriptor descriptor)
		{
			var draft = Draft("Sync", out descriptor);
			Select(draft, Field(descriptor, "First"), "First");
			Select(draft, Field(descriptor, "Second"), "Second");
			return draft;
		}

		private static DebugCommandFieldId Field(DebugCommandDescriptor descriptor, string name) =>
			descriptor.Fields.Single(field => field.Name == name).Id;

		private static DebugCommandReferenceListPage Page(
			DebugCommandDraftId draft,
			DebugCommandFieldId field,
			string search = "",
			int offset = 0,
			int count = 8
		) => DebugCommand.Draft.GetReferenceEntries(draft, field, new DebugListQuery("", search, new DebugPageRequest(offset, count)));

		private static void Select(DebugCommandDraftId draft, DebugCommandFieldId field, string label) =>
			Assert.That(
				DebugCommand.Draft.SelectReference(draft, field, Page(draft, field, label).Entries.Single().Handle).Status,
				Is.EqualTo(DebugCommandDraftOperationStatus.Succeeded)
			);

		private static IDictionary Entries(DexcCommandReferenceStore store) =>
			(IDictionary)
				typeof(DexcCommandReferenceStore).GetField("m_Entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(store);

		private static int ReferenceCount() => Entries(DexcCommandRuntime.References).Count;

		private static void SwapSource()
		{
			var previous = s_First;
			s_First = s_Second;
			s_Second = previous;
		}

		private static void ReplaceFirst()
		{
			Object.DestroyImmediate(s_First);
			s_First = new GameObject("FIX05 replacement");
		}
	}

	[DebugCommandAttribute("Tests/ZZFIX05Ref/Transform", Access = DebugCommandAccess.ReadOnly)]
	public sealed class Fix05ReferenceTransformCommand : DebugCommand<int>
	{
		public InputRef<Transform> Target = InputRef.Create(() =>
			new[] { new InputChoiceEntry<Transform>("transform", DexcCommandReferenceTests.s_First.transform, "Transform", "Scene") }
		);

		protected override int Execute()
		{
			Target.TryGetValue(out var target);
			return target.GetInstanceID();
		}
	}

	[DebugCommandAttribute("Tests/ZZFIX05Ref/Optional", Access = DebugCommandAccess.ReadOnly)]
	public sealed class Fix05OptionalReferenceCommand : DebugCommand
	{
		public InputRef<GameObject> Target = InputRef.Create(DexcCommandReferenceTests.Source, optional: true);

		protected override void Execute()
		{
			DexcCommandReferenceTests.s_ExecutedFirst = Target.TryGetValue(out var target) ? target.GetInstanceID() : -1;
		}
	}

	[DebugCommandAttribute("Tests/ZZFIX05Ref/Keyed", Access = DebugCommandAccess.ReadOnly)]
	public sealed class Fix05KeyedReferenceCommand : DebugCommand
	{
		public InputRef<GameObject> Target = new InputRef<GameObject>("zzfix05-keyed");

		protected override void Execute()
		{
			Target.TryGetValue(out var target);
			DexcCommandReferenceTests.s_ExecutedFirst = target.GetInstanceID();
		}
	}

	[DebugCommandAttribute("Tests/ZZFIX05Ref/Sync", Access = DebugCommandAccess.ReadOnly)]
	public sealed class Fix05ReferenceCommand : DebugCommand
	{
		public InputRef<GameObject> First = InputRef.Create(DexcCommandReferenceTests.Source);
		public InputRef<GameObject> Second = InputRef.Create(DexcCommandReferenceTests.Source);
		public InputInt Amount = new InputInt();

		protected override void OnInputsChanged(DebugInputChanges changes)
		{
			if (DexcCommandReferenceTests.s_NotifyThrows)
			{
				First.Key = "";
				Second.Key = "";
				throw new InvalidOperationException("notify-fault");
			}
		}

		protected override void Execute()
		{
			First.TryGetValue(out var first);
			Second.TryGetValue(out var second);
			DexcCommandReferenceTests.s_ExecutedFirst = first.GetInstanceID();
			DexcCommandReferenceTests.s_ExecutedSecond = second.GetInstanceID();
		}
	}

	[DebugCommandAttribute("Tests/ZZFIX05Ref/Output", Access = DebugCommandAccess.ReadOnly)]
	public sealed class Fix05ReferenceOutputCommand : DebugCommand<int>
	{
		public InputRef<GameObject> Target = InputRef.Create(DexcCommandReferenceTests.Source);

		protected override int Execute()
		{
			Target.TryGetValue(out var target);
			return target.GetInstanceID();
		}
	}

	[DebugCommandAttribute("Tests/ZZFIX05Ref/Async", Access = DebugCommandAccess.ReadOnly)]
	public sealed class Fix05ReferenceAsyncCommand : AsyncDebugCommand
	{
		public InputRef<GameObject> Target = InputRef.Create(DexcCommandReferenceTests.Source);

		protected override ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			Target.TryGetValue(out var target);
			DexcCommandReferenceTests.s_ExecutedFirst = target.GetInstanceID();
			return default(ValueTask);
		}
	}

	[DebugCommandAttribute("Tests/ZZFIX05Ref/AsyncOutput", Access = DebugCommandAccess.ReadOnly)]
	public sealed class Fix05ReferenceAsyncOutputCommand : AsyncDebugCommand<int>
	{
		public InputRef<GameObject> Target = InputRef.Create(DexcCommandReferenceTests.Source);

		protected override ValueTask<int> ExecuteAsync(CancellationToken cancellationToken)
		{
			Target.TryGetValue(out var target);
			return new ValueTask<int>(target.GetInstanceID());
		}
	}
}
#endif
