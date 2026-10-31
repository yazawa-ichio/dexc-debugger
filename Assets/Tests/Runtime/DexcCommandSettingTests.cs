using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcCommandSettingTests
	{
		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.Initialize();
			IncrementCommand.Executions = 0;
		}

		[TearDown]
		public void TearDown()
		{
			DebuggerRuntime.ResetForTests();
		}

#if DEXC_DEBUGGER
		[Test]
		public void CommandRegistryDraftAndRequestUseImmutableInputSnapshots()
		{
			var page = DebugCommand.Registry.GetList(new DebugListQuery("", "Tests/Increment", new DebugPageRequest(0, 20)));
			var item = page.Items[0];
			Assert.That(DebugCommand.Registry.TryGetDescriptor(item.Id, out var descriptor), Is.True);
			var create = DebugCommand.Draft.Create(item.Id);
			Assert.That(create.TryGetDraftId(out var draftId), Is.True);
			var input = descriptor.Fields[0];

			var update = DebugCommand.Draft.UpdateInput(draftId, input.Id, DebugValue.From(7));
			Assert.That(update.Status, Is.EqualTo(DebugCommandDraftOperationStatus.Succeeded));
			var creation = DebugCommand.Draft.CreateRequest(draftId);
			Assert.That(creation.TryGetRequest(out var request), Is.True);
			var result = request.Dispatch();
			Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
			Assert.That(IncrementCommand.Executions, Is.EqualTo(7));
			Assert.That(DebugCommand.Draft.GetHistory(request.CommandId, new DebugPageRequest(0, 20)).Entries, Has.Count.EqualTo(1));

			DebugCommand.Draft.UpdateInput(draftId, input.Id, DebugValue.From(3));
			Assert.That(request.Values[input.Id], Is.EqualTo(DebugValue.From(7)));
		}

		[Test]
		public void CommandValidationAndAvailabilityUseFailedWithFailureKind()
		{
			ValidationGateCommand.Reject = false;
			var request = DebugCommand.Draft.CreateRequest(new ValidationGateCommand());
			ValidationGateCommand.Reject = true;

			var result = request.Dispatch();

			Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Failed));
			Assert.That(result.FailureKind, Is.EqualTo(CommandFailureKind.Validation));
		}

		[Test]
		public void UnavailableValidationMapsToUnavailableFailureKind()
		{
			UnavailableValidationCommand.Unavailable = false;
			var request = DebugCommand.Draft.CreateRequest(new UnavailableValidationCommand());
			UnavailableValidationCommand.Unavailable = true;
			var result = request.Dispatch();

			Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Failed));
			Assert.That(result.FailureKind, Is.EqualTo(CommandFailureKind.Unavailable));
			Assert.That(result.ErrorCode, Is.EqualTo("fixture-unavailable"));
		}

		[Test]
		public void CommandAndSettingQueriesRespectPathSearchOrderAndPageLimit()
		{
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize(options => options.Query.MaxPageSize = 2);

			var pathMatches = DebugCommand.Registry.GetList(new DebugListQuery("Tests/Enemy", string.Empty, new DebugPageRequest(0, 2)));
			Assert.That(pathMatches.Items.Select(item => item.Path), Is.EqualTo(new[] { "Tests/Enemy/Damage" }));
			var tagMatches = DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, "combat", new DebugPageRequest(0, 2)));
			Assert.That(tagMatches.Items.Select(item => item.Path), Is.EqualTo(new[] { "Tests/Enemy/Damage" }));
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 3)))
			);
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				DebugSetting.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 3)))
			);
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				DebugCommand.Draft.GetHistory(
					DebugCommand
						.Registry.GetList(new DebugListQuery("Tests/Increment", string.Empty, new DebugPageRequest(0, 2)))
						.Items[0]
						.Id,
					new DebugPageRequest(0, 3)
				)
			);
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				DebugCommand.Draft.GetReferenceEntries(
					default(DebugCommandDraftId),
					default(DebugCommandFieldId),
					new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 3))
				)
			);
		}

		[Test]
		public void SettingEnumUsesStringKeySnapshotAndRejectsNumericValue()
		{
			var item = DebugSetting.GetList(new DebugListQuery(string.Empty, "Tests/Enum", new DebugPageRequest(0, 20))).Items.Single();
			Assert.That(DebugSetting.Read(item.Id).TryGetValue(out var initial), Is.True);
			Assert.That(initial, Is.EqualTo(DebugValue.From("First")));
			Assert.That(DebugSetting.Write(item.Id, DebugValue.From(1)).Status, Is.EqualTo(DebugOperationStatus.Rejected));
			Assert.That(DebugSetting.Write(item.Id, DebugValue.From("Second")).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(DebugSetting.Read(item.Id).TryGetValue(out var current), Is.True);
			Assert.That(current, Is.EqualTo(DebugValue.From("Second")));
		}

		[Test]
		public void CommandRegistryExcludesInvalidSignatureAndMultipleReferenceDefinitions()
		{
			var invalidSignature = DebugCommand.Registry.GetList(
				new DebugListQuery("", "Tests/InvalidSignature", new DebugPageRequest(0, 20))
			);
			var multipleReference = DebugCommand.Registry.GetList(
				new DebugListQuery("", "Tests/MultipleReference", new DebugPageRequest(0, 20))
			);

			Assert.That(invalidSignature.Items, Is.Empty);
			Assert.That(multipleReference.Items, Is.Empty);
			Assert.That(invalidSignature.Summary.ExcludedDefinitionCount, Is.GreaterThanOrEqualTo(2));
			Assert.That(invalidSignature.Summary.ErrorCount, Is.GreaterThanOrEqualTo(2));
		}

		[Test]
		public void TypedCommandValidationDetectsInputReplacement()
		{
			var command = new ReplacementCommand();
			Assert.That(DebugCommand.Draft.Validate(command).CanExecute, Is.True);

			command.Amount = new InputInt(9);
			var validation = DebugCommand.Draft.Validate(command);

			Assert.That(validation.CanExecute, Is.False);
			Assert.That(validation.Issues.Any(issue => issue.Code == "input-replaced"), Is.True);
		}

		[Test]
		public void TypedCommandValidationRejectsCrossFieldFailureBeforeRequestCreation()
		{
			var command = new ValidationFailureCommand();
			var validation = DebugCommand.Draft.Validate(command);

			Assert.That(validation.CanExecute, Is.False);
			Assert.That(validation.Issues.Any(issue => issue.Code == "fixture-validation"), Is.True);
			var exception = Assert.Throws<DebugCommandValidationException>(() => DebugCommand.Draft.CreateRequest(command));
			Assert.That(exception.Validation.Issues.Any(issue => issue.Code == "fixture-validation"), Is.True);
		}

		[Test]
		public void SettingReadWriteResetRangeAndHistoryUseCachedDescriptor()
		{
			var page = DebugSetting.GetList(new DebugListQuery("", "Tests/Volume", new DebugPageRequest(0, 20)));
			var item = page.Items[0];
			var read = DebugSetting.Read(item.Id);
			Assert.That(read.TryGetValue(out var initial), Is.True);
			Assert.That(initial, Is.EqualTo(DebugValue.From(2)));

			Assert.That(DebugSetting.Write(item.Id, DebugValue.From(8)).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(DebugSetting.Read(item.Id).TryGetValue(out var current), Is.True);
			Assert.That(current, Is.EqualTo(DebugValue.From(8)));
			Assert.That(DebugSetting.Write(item.Id, DebugValue.From(99)).Status, Is.EqualTo(DebugOperationStatus.Rejected));
			Assert.That(DebugSetting.Reset(item.Id).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(DebugSetting.GetHistory(item.Id, new DebugPageRequest(0, 20)).Entries.Count, Is.GreaterThanOrEqualTo(2));
		}

		[Test]
		public void SettingIntegerWritesUseExplicitAotSafeConversions()
		{
			var values = new[]
			{
				new { Path = "Tests/Integer/SByte", Value = DebugValue.From((long)-7) },
				new { Path = "Tests/Integer/Int16", Value = DebugValue.From((long)-1234) },
				new { Path = "Tests/Integer/Int32", Value = DebugValue.From((long)-123456) },
				new { Path = "Tests/Integer/Int64", Value = DebugValue.From(long.MinValue + 1) },
				new { Path = "Tests/Integer/Byte", Value = DebugValue.From((ulong)7) },
				new { Path = "Tests/Integer/UInt16", Value = DebugValue.From((ulong)1234) },
				new { Path = "Tests/Integer/UInt32", Value = DebugValue.From((ulong)123456) },
				new { Path = "Tests/Integer/UInt64", Value = DebugValue.From(ulong.MaxValue - 1) },
			};

			foreach (var item in values)
			{
				var setting = DebugSetting
					.GetList(new DebugListQuery(string.Empty, item.Path, new DebugPageRequest(0, 20)))
					.Items.Single(entry => entry.Path == item.Path);
				var write = DebugSetting.Write(setting.Id, item.Value);
				Assert.That(
					write.Status,
					Is.EqualTo(DebugOperationStatus.Succeeded),
					item.Path + ": " + string.Join("|", write.Issues.Select(issue => issue.Message))
				);
				Assert.That(DebugSetting.Read(setting.Id).TryGetValue(out var actual), Is.True, item.Path);
				Assert.That(actual, Is.EqualTo(item.Value), item.Path);
			}
		}

		[Test]
		public void SettingRegistryKeepsValidEntriesWhenSetPropertyOrPresetIsInvalid()
		{
			var valid = DebugSetting.GetList(new DebugListQuery("", "Tests/Volume", new DebugPageRequest(0, 20)));
			var invalidProperty = DebugSetting.GetList(new DebugListQuery("", "Tests/InvalidProperty", new DebugPageRequest(0, 20)));
			var presets = DebugSetting.GetPresets(valid.Items[0].SetId, new DebugPageRequest(0, 20));

			Assert.That(valid.Items, Has.Count.EqualTo(1));
			Assert.That(invalidProperty.Items, Is.Empty);
			Assert.That(presets.Items, Has.Count.EqualTo(1));
			Assert.That(valid.Summary.ExcludedDefinitionCount, Is.GreaterThanOrEqualTo(3));
			Assert.That(valid.Summary.ErrorCount, Is.GreaterThanOrEqualTo(3));
		}

		/// <summary>An async request handed to the synchronous entry point is turned away, not run into an exception of its own.</summary>
		[Test]
		public void AnAsyncRequestIsNotRunSynchronously()
		{
			DebugCommandRequest request = DebugCommand.Draft.CreateRequest(new AsyncProbeCommand());
			var result = request.Dispatch();
			Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Failed));
			Assert.That(result.FailureKind, Is.Not.EqualTo(CommandFailureKind.UnhandledException));
			Assert.That(result.ErrorCode, Is.EqualTo("request-async"));
			Assert.That(DebugCommand.Draft.GetHistory(request.CommandId, new DebugPageRequest(0, 20)).Entries, Is.Empty);
		}

		/// <summary>A choice whose key left its source before dispatch fails validation; the command does not run.</summary>
		[Test]
		public void AChoiceGoneBeforeDispatchFailsValidation()
		{
			ChoiceSourceCommand.Keys.Clear();
			ChoiceSourceCommand.Keys.AddRange(new[] { "A", "B" });
			try
			{
				var command = new ChoiceSourceCommand();
				command.Pick.Apply(DebugValue.From("A"));
				var request = DebugCommand.Draft.CreateRequest(command);
				ChoiceSourceCommand.Keys.Remove("A");

				var result = request.Dispatch();
				Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Failed));
				Assert.That(result.FailureKind, Is.EqualTo(CommandFailureKind.Validation));
			}
			finally
			{
				ChoiceSourceCommand.Keys.Clear();
			}
		}

		/// <summary>0.1f is 0.10000000149 as a double: a float at the end of its range is still inside it.</summary>
		[Test]
		public void AFloatAtTheEndOfItsRangeIsInsideIt()
		{
			var fraction = DebugSetting.GetList(new DebugListQuery("", "ZZTests/FloatSettings/Fraction", new DebugPageRequest(0, 20))).Items[0];
			Assert.That(DebugSetting.Reset(fraction.Id).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(DebugSetting.Write(fraction.Id, 0.1f).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(DebugSetting.Write(fraction.Id, 0.11f).Status, Is.EqualTo(DebugOperationStatus.Rejected));

			var result = DebugCommand.Draft.CreateRequest(new FloatRangeCommand()).Dispatch();
			Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
		}

		/// <summary>A double past the float range would be stored as Infinity, which could then be neither read nor reset.</summary>
		[Test]
		public void AFloatSettingRejectsAValueOutsideTheFloatRange()
		{
			var unbounded = DebugSetting.GetList(new DebugListQuery("", "ZZTests/FloatSettings/Unbounded", new DebugPageRequest(0, 20))).Items[0];
			var result = DebugSetting.Write(unbounded.Id, DebugValue.From(1e39));
			Assert.That(result.Status, Is.EqualTo(DebugOperationStatus.Rejected));
			Assert.That(DebugSetting.Read(unbounded.Id).TryGetValue(out var current), Is.True);
			Assert.That(current, Is.EqualTo(DebugValue.From(1f)));
		}

		[Test]
		public void SettingReadOnlyAndOnChangedFailureAreReported()
		{
			var readOnly = DebugSetting.GetList(new DebugListQuery("", "Tests/ReadOnly", new DebugPageRequest(0, 20))).Items[0];
			Assert.That(DebugSetting.Write(readOnly.Id, DebugValue.From(8)).Status, Is.EqualTo(DebugOperationStatus.ReadOnly));
			Assert.That(DebugSetting.Reset(readOnly.Id).Status, Is.EqualTo(DebugOperationStatus.ReadOnly));

			var volume = DebugSetting.GetList(new DebugListQuery("", "Tests/Volume", new DebugPageRequest(0, 20))).Items[0];
			CommandTestSettings.ThrowOnChanged = true;
			var result = DebugSetting.Write(volume.Id, DebugValue.From(8));
			CommandTestSettings.ThrowOnChanged = false;

			Assert.That(result.Status, Is.EqualTo(DebugOperationStatus.Failed));
			Assert.That(result.DiagnosticId.IsValid, Is.True);
			Assert.That(DebugSetting.Read(volume.Id).TryGetValue(out var current), Is.True);
			Assert.That(current, Is.EqualTo(DebugValue.From(8)));
		}

		[Test]
		public async Task SettingScopeResetsSceneValuesAndEndsSessionValues()
		{
			var global = DebugSetting.GetList(new DebugListQuery("", "Tests/Global", new DebugPageRequest(0, 20))).Items[0];
			var scene = DebugSetting.GetList(new DebugListQuery("", "Tests/Scene", new DebugPageRequest(0, 20))).Items[0];
			var session = DebugSetting.GetList(new DebugListQuery("", "Tests/Session", new DebugPageRequest(0, 20))).Items[0];
			Assert.That(DebugSetting.Write(global.Id, DebugValue.From(8)).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(DebugSetting.Write(scene.Id, DebugValue.From(8)).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(DebugSetting.Write(session.Id, DebugValue.From(8)).Status, Is.EqualTo(DebugOperationStatus.Succeeded));

			var fixtureScene = SceneManager.CreateScene("DexcSettingScopeFixture_" + Guid.NewGuid().ToString("N"));
			var unload = SceneManager.UnloadSceneAsync(fixtureScene);
			while (unload != null && !unload.isDone)
				await Task.Yield();

			Assert.That(DebugSetting.Read(global.Id).TryGetValue(out var globalValue), Is.True);
			Assert.That(DebugSetting.Read(scene.Id).TryGetValue(out var sceneValue), Is.True);
			Assert.That(globalValue, Is.EqualTo(DebugValue.From(8)));
			Assert.That(sceneValue, Is.EqualTo(DebugValue.From(2)));

			var current = CommandTestSettings.Current;
			DebuggerRuntime.ResetForTests();
			Assert.That(current.GlobalValue, Is.EqualTo(2));
			Assert.That(current.SceneValue, Is.EqualTo(2));
			Assert.That(current.SessionValue, Is.EqualTo(2));
		}

		[Test]
		public void ZeroHistoryCapacityDoesNotRetainSettingHistory()
		{
			DebuggerRuntime.Initialize(options => options.History.CapacityPerFeature = 0);
			var volume = DebugSetting.GetList(new DebugListQuery("", "Tests/Volume", new DebugPageRequest(0, 20))).Items[0];
			Assert.That(DebugSetting.Write(volume.Id, DebugValue.From(8)).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(DebugSetting.GetHistory(volume.Id, new DebugPageRequest(0, 20)).Entries, Is.Empty);
		}

		[Test]
		public async Task AsyncCommandDispatchCompletesThroughExecutionBoundary()
		{
			var command = new AsyncProbeCommand();
			var request = DebugCommand.Draft.CreateRequest(command);
			var result = await request.DispatchAsync();
			Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
		}

		[Test]
		public async Task PlayModeCommandSyncAndAsyncExecutionFinalizeHistory()
		{
			var syncRequest = DebugCommand.Draft.CreateRequest(new IncrementCommand());
			var syncResult = syncRequest.Dispatch();
			Assert.That(syncResult.Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
			var syncHistory = DebugCommand.Draft.GetHistory(syncRequest.CommandId, new DebugPageRequest(0, 20));
			Assert.That(syncHistory.Entries, Has.Count.EqualTo(1));
			var restored = DebugCommand.Draft.CreateFromHistory(syncHistory.Entries[0].Id);
			Assert.That(restored.TryGetDraftId(out var restoredDraft), Is.True);
			var restoredRequest = DebugCommand.Draft.CreateRequest(restoredDraft);
			Assert.That(restoredRequest.TryGetRequest(out var restoredCommand), Is.True);
			Assert.That(restoredCommand.Dispatch().Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
			Assert.That(DebugCommand.Draft.GetHistory(syncRequest.CommandId, new DebugPageRequest(0, 20)).Entries, Has.Count.EqualTo(2));

			var asyncRequest = DebugCommand.Draft.CreateRequest(new AsyncProbeCommand());
			var asyncResult = await asyncRequest.DispatchAsync();
			Assert.That(asyncResult.Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
			Assert.That(DebugCommand.Draft.GetHistory(asyncRequest.CommandId, new DebugPageRequest(0, 20)).Entries, Has.Count.EqualTo(1));
		}

		[Test]
		public async Task AsyncCompletionFromOldSessionDoesNotWriteNewSessionHistory()
		{
			ControlledAsyncCommand.Reset();
			var request = DebugCommand.Draft.CreateRequest(new ControlledAsyncCommand());
			var previous = DebuggerRuntime.CurrentSession;
			var execution = request.StartExecution();
			await Task.Yield();

			DebuggerRuntime.Initialize();
			ControlledAsyncCommand.Gate.TrySetResult(true);
			var result = await execution.WaitAsync();

			Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Cancelled));
			Assert.That(execution.State, Is.EqualTo(DebugCommandExecutionState.Cancelled));
			Assert.That(result.Output, Is.Null);
			Assert.That(DebugCommand.Draft.GetHistory(request.CommandId, new DebugPageRequest(0, 20)).Entries, Is.Empty);
			Assert.That(previous.IsCurrent, Is.False);
		}

		[Test]
		public async Task AsyncCancellationTimeoutGateAndHistoryFinalizeOnce()
		{
			var cancellationRequest = DebugCommand.Draft.CreateRequest(new CancellableAsyncCommand());
			using (var cancellation = new CancellationTokenSource())
			{
				var pending = cancellationRequest.DispatchAsync(cancellation.Token);
				await Task.Yield();
				cancellation.Cancel();
				var cancelled = await pending;
				Assert.That(cancelled.Status, Is.EqualTo(CommandExecutionStatus.Cancelled));
			}
			Assert.That(
				DebugCommand.Draft.GetHistory(cancellationRequest.CommandId, new DebugPageRequest(0, 20)).Entries.Count,
				Is.EqualTo(1)
			);

			var timeoutRequest = DebugCommand.Draft.CreateRequest(new TimeoutAsyncCommand());
			var timedOut = await timeoutRequest.DispatchAsync();
			Assert.That(timedOut.Status, Is.EqualTo(CommandExecutionStatus.TimedOut));
			Assert.That(DebugCommand.Draft.GetHistory(timeoutRequest.CommandId, new DebugPageRequest(0, 20)).Entries.Count, Is.EqualTo(1));

			ControlledAsyncCommand.Reset();
			var firstRequest = DebugCommand.Draft.CreateRequest(new ControlledAsyncCommand());
			var secondRequest = DebugCommand.Draft.CreateRequest(new ControlledAsyncCommand());
			var first = firstRequest.StartExecution();
			var locked = await secondRequest.DispatchAsync();
			Assert.That(locked.Status, Is.EqualTo(CommandExecutionStatus.Failed));
			Assert.That(locked.FailureKind, Is.EqualTo(CommandFailureKind.Unavailable));
			ControlledAsyncCommand.Gate.TrySetResult(true);
			var completed = await first.WaitAsync();
			Assert.That(completed.Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
			Assert.That(DebugCommand.Draft.GetHistory(firstRequest.CommandId, new DebugPageRequest(0, 20)).Entries.Count, Is.EqualTo(1));
		}

		[Test]
		public async Task BackgroundTimeoutFinalizesStateGateAndHistoryOnce()
		{
			TimeoutAsyncCommand.Reset();
			var mainThreadId = Thread.CurrentThread.ManagedThreadId;
			var request = DebugCommand.Draft.CreateRequest(new TimeoutAsyncCommand());
			var execution = request.StartExecution();

			var result = await execution.WaitAsync();

			Assert.That(TimeoutAsyncCommand.CompletionThreadId, Is.Not.EqualTo(mainThreadId));
			Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.TimedOut));
			Assert.That(execution.State, Is.EqualTo(DebugCommandExecutionState.TimedOut));
			Assert.That(DebugCommand.Draft.GetHistory(request.CommandId, new DebugPageRequest(0, 20)).Entries, Has.Count.EqualTo(1));
		}

		[Test]
		public async Task BackgroundExceptionBecomesADiagnosticAndAHistoryEntry()
		{
			ThrowingAsyncCommand.Reset();
			var mainThreadId = Thread.CurrentThread.ManagedThreadId;
			var request = DebugCommand.Draft.CreateRequest(new ThrowingAsyncCommand());
			var execution = request.StartExecution();

			var result = await execution.WaitAsync();

			Assert.That(ThrowingAsyncCommand.CompletionThreadId, Is.Not.EqualTo(mainThreadId));
			Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Failed));
			Assert.That(result.FailureKind, Is.EqualTo(CommandFailureKind.UnhandledException));
			Assert.That(result.DiagnosticId.IsValid, Is.True);
			Assert.That(DebugCommand.Draft.GetHistory(request.CommandId, new DebugPageRequest(0, 20)).Entries, Has.Count.EqualTo(1));
		}
#endif
	}

	[DebugCommandAttribute("Tests/Increment", Access = DebugCommandAccess.ReadOnly)]
	public sealed class IncrementCommand : DebugCommand
	{
		public static int Executions { get; set; }
		public InputInt Amount = new InputInt(1);

		protected override void Execute()
		{
			Executions += Amount.Value;
		}
	}

	[DebugCommandAttribute("Tests/Enemy/Damage", Description = "Damage an enemy", Tags = new[] { "combat" }, Order = 1)]
	public sealed class EnemyDamageCommand : DebugCommand
	{
		protected override void Execute() { }
	}

	[DebugCommandAttribute("Tests/EnemyBoss/Damage", Description = "Damage an enemy boss", Tags = new[] { "boss" }, Order = 2)]
	public sealed class EnemyBossDamageCommand : DebugCommand
	{
		protected override void Execute() { }
	}

	[DebugCommandAttribute("Tests/ValidationGate", Access = DebugCommandAccess.ReadOnly)]
	public sealed class ValidationGateCommand : DebugCommand
	{
		public static bool Reject;

		protected override void Validate(DebugCommandValidation validation)
		{
			if (Reject)
				validation.Error("gate-validation", "The validation gate is closed.");
		}

		protected override void Execute() { }
	}

	[DebugCommandAttribute("Tests/UnavailableValidation", Access = DebugCommandAccess.ReadOnly)]
	public sealed class UnavailableValidationCommand : DebugCommand
	{
		public static bool Unavailable;

		protected override void Validate(DebugCommandValidation validation)
		{
			if (Unavailable)
				validation.Unavailable("fixture-unavailable", "The fixture is unavailable.");
		}

		protected override void Execute() { }
	}

	[DebugCommandAttribute("Tests/AsyncProbe", Access = DebugCommandAccess.ReadOnly)]
	public sealed class AsyncProbeCommand : AsyncDebugCommand
	{
		protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			await Task.Yield();
			cancellationToken.ThrowIfCancellationRequested();
		}
	}

	[DebugCommandAttribute("ZZTests/ChoiceSource")]
	public sealed class ChoiceSourceCommand : DebugCommand
	{
		internal static readonly System.Collections.Generic.List<string> Keys = new System.Collections.Generic.List<string>();

		public InputChoice<string> Pick = new InputChoice<string>(() =>
			Keys.Select(key => new InputChoiceEntry<string>(key, key, key)).ToArray()
		);

		protected override void Execute() { }
	}

	[DebugCommandAttribute("ZZTests/FloatRange")]
	public sealed class FloatRangeCommand : DebugCommand<float>
	{
		[DebugRange(0, 0.1)]
		public InputFloat Amount = new InputFloat(0.1f);

		protected override float Execute() => Amount.Value;
	}

	public sealed class FloatSettings : DebugSettingSet<FloatSettings>
	{
		[DebugSetting("ZZTests/FloatSettings/Fraction")]
		[DebugRange(0, 0.1)]
		public float Fraction { get; private set; } = 0.1f;

		[DebugSetting("ZZTests/FloatSettings/Unbounded")]
		public float Unbounded { get; private set; } = 1f;
	}

	[DebugCommandAttribute("Tests/DisabledOutput", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DisabledOutputCommand : DebugCommand<int>
	{
		protected override int Execute()
		{
			return 1;
		}
	}

	[DebugCommandAttribute("Tests/DisabledAsyncOutput", Access = DebugCommandAccess.ReadOnly)]
	public sealed class DisabledAsyncOutputCommand : AsyncDebugCommand<int>
	{
		protected override async ValueTask<int> ExecuteAsync(CancellationToken cancellationToken)
		{
			await Task.Yield();
			return 1;
		}
	}

	[DebugCommandAttribute("Tests/ControlledAsync", Access = DebugCommandAccess.Lock)]
	public sealed class ControlledAsyncCommand : AsyncDebugCommand
	{
		public static TaskCompletionSource<bool> Gate { get; private set; }
		public static int Completions;

		static ControlledAsyncCommand()
		{
			Reset();
		}

		public static void Reset()
		{
			Gate = new TaskCompletionSource<bool>();
			Completions = 0;
		}

		protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			await Gate.Task.ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();
			Interlocked.Increment(ref Completions);
		}
	}

	[DebugCommandAttribute("Tests/CancellableAsync", Access = DebugCommandAccess.ReadOnly)]
	public sealed class CancellableAsyncCommand : AsyncDebugCommand
	{
		protected override TimeSpan Timeout
		{
			get { return System.Threading.Timeout.InfiniteTimeSpan; }
		}

		protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
		}
	}

	[DebugCommandAttribute("Tests/TimeoutAsync", Access = DebugCommandAccess.ReadOnly)]
	public sealed class TimeoutAsyncCommand : AsyncDebugCommand
	{
		public static int CompletionThreadId { get; private set; }
		protected override TimeSpan Timeout
		{
			get { return TimeSpan.FromMilliseconds(20); }
		}

		public static void Reset()
		{
			CompletionThreadId = 0;
		}

		protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			try
			{
				await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
			}
			finally
			{
				CompletionThreadId = Thread.CurrentThread.ManagedThreadId;
			}
		}
	}

	[DebugCommandAttribute("Tests/ThrowingAsync", Access = DebugCommandAccess.ReadOnly)]
	public sealed class ThrowingAsyncCommand : AsyncDebugCommand
	{
		public static int CompletionThreadId { get; private set; }
		protected override TimeSpan Timeout
		{
			get { return System.Threading.Timeout.InfiniteTimeSpan; }
		}

		public static void Reset()
		{
			CompletionThreadId = 0;
		}

		protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			try
			{
				await Task.Delay(1, cancellationToken).ConfigureAwait(false);
				throw new InvalidOperationException("Background fixture failure.");
			}
			finally
			{
				CompletionThreadId = Thread.CurrentThread.ManagedThreadId;
			}
		}
	}

	[DebugCommandAttribute("Tests/InvalidSignature")]
	public sealed class InvalidSignatureCommand : DebugCommand
	{
		public int RawValue = 1;

		protected override void Execute() { }
	}

	[DebugCommandAttribute("Tests/MultipleReference")]
	public sealed class MultipleReferenceCommand : DebugCommand
	{
		private static readonly InputRef<GameObject> Shared = new InputRef<GameObject>();
		public InputRef<GameObject> First = Shared;
		public InputRef<GameObject> Second = Shared;

		protected override void Execute() { }
	}

	[DebugCommandAttribute("Tests/Replacement")]
	public sealed class ReplacementCommand : DebugCommand
	{
		public InputInt Amount = new InputInt(1);

		protected override void Execute() { }
	}

	[DebugCommandAttribute("Tests/ValidationFailure")]
	public sealed class ValidationFailureCommand : DebugCommand
	{
		public InputInt Amount = new InputInt(1);

		protected override void Validate(DebugCommandValidation validation)
		{
			validation.Error("fixture-validation", "The fixture is intentionally not executable.");
		}

		protected override void Execute() { }
	}

	public sealed class CommandTestSettings : DebugSettingSet<CommandTestSettings>
	{
		public enum TestMode
		{
			First,
			Second,
		}

		public static bool ThrowOnChanged;

		[DebugSetting("Tests/Volume")]
		[DebugRange(0, 10)]
		public int Volume { get; private set; } = 2;

		[DebugSetting("Tests/Enum")]
		public TestMode Mode { get; private set; } = TestMode.First;

		[DebugSetting("Tests/Global", Scope = DebugSettingScope.Global)]
		public int GlobalValue { get; private set; } = 2;

		[DebugSetting("Tests/Scene", Scope = DebugSettingScope.Scene)]
		public int SceneValue { get; private set; } = 2;

		[DebugSetting("Tests/Session", Scope = DebugSettingScope.Session)]
		public int SessionValue { get; private set; } = 2;

		[DebugSetting("Tests/ReadOnly", Access = DebugSettingAccess.ReadOnly)]
		public int ReadOnlyValue { get; private set; } = 2;

		[DebugSetting("Tests/Integer/SByte")]
		public sbyte SByteValue { get; private set; }

		[DebugSetting("Tests/Integer/Int16")]
		public short Int16Value { get; private set; }

		[DebugSetting("Tests/Integer/Int32")]
		public int Int32Value { get; private set; }

		[DebugSetting("Tests/Integer/Int64")]
		public long Int64Value { get; private set; }

		[DebugSetting("Tests/Integer/Byte")]
		public byte ByteValue { get; private set; }

		[DebugSetting("Tests/Integer/UInt16")]
		public ushort UInt16Value { get; private set; }

		[DebugSetting("Tests/Integer/UInt32")]
		public uint UInt32Value { get; private set; }

		[DebugSetting("Tests/Integer/UInt64")]
		public ulong UInt64Value { get; private set; }

		[DebugSetting("Tests/InvalidProperty")]
		public DateTime InvalidProperty { get; private set; } = DateTime.UtcNow;

		[DebugPreset("Tests/Loud", "Loud", "Test preset")]
		public static CommandTestSettings Loud
		{
			get { return new CommandTestSettings { Volume = 8 }; }
		}

		[DebugPreset("Tests/InvalidPreset", "Invalid", "Invalid test preset")]
		public static CommandTestSettings InvalidPreset
		{
			get { throw new InvalidOperationException("Invalid preset fixture."); }
		}

		protected override void OnChanged(DebugSettingChanges changes)
		{
			if (ThrowOnChanged)
				throw new InvalidOperationException("OnChanged fixture failure.");
		}
	}

	public sealed class InvalidSettingSet : DebugSettingSet<InvalidSettingSet>
	{
		private InvalidSettingSet(int ignored) { }

		[DebugSetting("Tests/InvalidSet")]
		public int Value { get; private set; } = 1;
	}
}
