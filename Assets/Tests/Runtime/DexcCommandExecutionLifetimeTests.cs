#if DEXC_DEBUGGER
using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcCommandExecutionLifetimeTests
	{
		[SetUp]
		public void SetUp()
		{
			DexcRuntimeUIState.ResetForSession();
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize();
			LifetimeAsyncCommand.s_Completion = new TaskCompletionSource<bool>();
			LifetimeAsyncCommand.s_Reinitialize = false;
		}

		[TearDown]
		public void TearDown()
		{
			LifetimeAsyncCommand.s_Completion.TrySetResult(true);
			DexcRuntimeUIState.ResetForSession();
			DebuggerRuntime.ResetForTests();
		}

		[TestCase(false, false)]
		[TestCase(false, true)]
		[TestCase(true, false)]
		[TestCase(true, true)]
		public void CompletionDetachesBeforeCallbacksAndReportsEveryFailure(bool fault, bool disposalFault)
		{
			var expected = Result();
			Task<CommandExecutionResult> task = fault
				? Task.FromException<CommandExecutionResult>(new InvalidOperationException("task-fault"))
				: Task.FromResult(expected);
			DebugCommandExecution execution = null;
			var callbacks = 0;
			var source = new CancellationTokenSource();
			source.Token.Register(() =>
			{
				callbacks++;
				Assert.That(execution, Is.Null);
				Assert.That(task, Is.Null);
				if (disposalFault)
				{
					throw new InvalidOperationException("dispose-fault");
				}
			});
			var owned = new LifetimeExecution(source, task);
			execution = owned;
			Assert.That(DexcCommandExecutionObserver.TryComplete(ref execution, ref task, out var result, out var error), Is.True);
			Assert.That(result, fault ? Is.Null : Is.SameAs(expected));
			Assert.That(error != null, Is.EqualTo(fault || disposalFault));
			if (fault)
			{
				Assert.That(error.ToString(), Does.Contain("task-fault"));
			}
			if (disposalFault)
			{
				Assert.That(error.ToString(), Does.Contain("dispose-fault"));
			}
			Assert.That(callbacks, Is.EqualTo(1));
			Assert.Throws<ObjectDisposedException>(() =>
			{
				var token = source.Token;
			});
			Assert.DoesNotThrow(owned.Dispose);
			Assert.That(DexcCommandExecutionObserver.TryComplete(ref execution, ref task, out _, out _), Is.False);
		}

		[Test]
		public void CancelledTaskAndPendingReleaseLeaveNoOwnedExecution()
		{
			Task<CommandExecutionResult> task = Task.FromCanceled<CommandExecutionResult>(new CancellationToken(true));
			DebugCommandExecution execution = new LifetimeExecution(new CancellationTokenSource(), task);
			Assert.That(DexcCommandExecutionObserver.TryComplete(ref execution, ref task, out var result, out var error), Is.True);
			Assert.That(result, Is.Null);
			Assert.That(error, Is.InstanceOf<OperationCanceledException>());
			var completion = new TaskCompletionSource<CommandExecutionResult>();
			task = completion.Task;
			execution = new LifetimeExecution(new CancellationTokenSource(), task);
			Assert.That(DexcCommandExecutionObserver.TryComplete(ref execution, ref task, out _, out _), Is.False);
			Assert.That(DexcCommandExecutionObserver.Release(ref execution, ref task), Is.Null);
			Assert.That(execution, Is.Null);
			Assert.That(task, Is.Null);
			completion.SetException(new InvalidOperationException("late-fault"));
			Assert.That(DexcCommandExecutionObserver.Release(ref execution, ref task), Is.Null);
		}

		[UnityTest]
		public IEnumerator DriverCompletesFaultsWhileHiddenAndAllowsNewExecution()
		{
			DebugMenu.Close();
			DexcRuntimeUIDriver.CreateForBootstrap();
			var source = new CancellationTokenSource();
			source.Token.Register(() =>
			{
				throw new InvalidOperationException("dispose-fault");
			});
			var failed = Task.FromException<CommandExecutionResult>(new InvalidOperationException("task-fault"));
			DexcRuntimeUIState.AsyncExecution = new LifetimeExecution(source, failed);
			DexcRuntimeUIState.AsyncTask = failed;
			yield return null;
			yield return null;
			Assert.That(DexcRuntimeUIState.AsyncExecution, Is.Null);
			Assert.That(DexcRuntimeUIState.AsyncTask, Is.Null);
			Assert.That(DexcRuntimeUIState.Notice, Does.Contain("task-fault").And.Contain("dispose-fault"));
			DebugMenu.Open();
			SelectAsync();
			DexcRuntimeCommandExecution.Request();
			Assert.That(DexcRuntimeUIState.AsyncExecution, Is.Not.Null);
			LifetimeAsyncCommand.s_Completion.SetException(new InvalidOperationException("user-command-fault"));
			var deadline = Time.realtimeSinceStartup + 5f;
			while (DexcRuntimeUIState.AsyncExecution != null && Time.realtimeSinceStartup < deadline)
			{
				yield return null;
			}
			Assert.That(DexcRuntimeUIState.AsyncExecution, Is.Null);
			Assert.That(DexcRuntimeUIState.LastCommandResult.Status, Is.EqualTo(CommandExecutionStatus.Failed));
			Assert.That(DexcRuntimeUIState.LastCommandResult.FailureKind, Is.EqualTo(CommandFailureKind.UnhandledException));
			Assert.That(DexcRuntimeUIState.Notice, Does.Not.Contain("completion failed"), "ordinary Command failure remains a result");
		}

		[UnityTest]
		public IEnumerator ReinitializeDetachesWithoutProviderAndIgnoresLateResult()
		{
			DebugMenu.Open();
			SelectAsync();
			DexcRuntimeCommandExecution.Request();
			var oldTask = DexcRuntimeUIState.AsyncTask;
			Assert.That(DexcRuntimeUIState.InputProvider, Is.Null);
			DebuggerRuntime.Initialize();
			Assert.That(DexcRuntimeUIState.AsyncExecution, Is.Null);
			Assert.That(DexcRuntimeUIState.AsyncTask, Is.Null);
			LifetimeAsyncCommand.s_Completion.TrySetResult(true);
			yield return null;
			yield return null;
			DexcRuntimeCommandExecution.Poll();
			Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Null);
			Assert.That(oldTask, Is.Not.Null);
		}

		[Test]
		public void ResetFinishesAfterThrowingCancellationCallback()
		{
			DebugMenu.Open();
			SelectAsync();
			var source = new CancellationTokenSource();
			source.Token.Register(() =>
			{
				throw new InvalidOperationException("cancel-fault");
			});
			var completion = new TaskCompletionSource<CommandExecutionResult>();
			DexcRuntimeUIState.AsyncTask = completion.Task;
			DexcRuntimeUIState.AsyncExecution = new LifetimeExecution(source, completion.Task);
			Assert.DoesNotThrow(DexcRuntimeCommandExecution.CancelExecution);
			Assert.That(DexcRuntimeUIState.AsyncExecution, Is.Not.Null, "cancellation does not claim completion");
			Assert.DoesNotThrow(DexcRuntimeUIState.ResetForSession);
			Assert.That(DexcRuntimeUIState.HasDraft, Is.False);
			Assert.That(DexcRuntimeUIState.Command, Is.Null);
			Assert.That(DexcRuntimeUIState.AsyncTask, Is.Null);
			completion.SetException(new InvalidOperationException("late-cancel-fault"));
		}

		[Test]
		public void CompletionCallbackCannotOverwriteNewExecutionOrSession()
		{
			var next = Task.FromResult(Result());
			var nextExecution = new LifetimeExecution(new CancellationTokenSource(), next);
			var source = new CancellationTokenSource();
			source.Token.Register(() =>
			{
				DexcRuntimeUIState.AsyncExecution = nextExecution;
				DexcRuntimeUIState.AsyncTask = next;
			});
			var first = Task.FromResult(Result());
			DexcRuntimeUIState.AsyncTask = first;
			DexcRuntimeUIState.AsyncExecution = new LifetimeExecution(source, first);
			DexcRuntimeCommandExecution.Poll();
			Assert.That(DexcRuntimeUIState.AsyncExecution, Is.SameAs(nextExecution));
			Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Null);
			DexcRuntimeCommandExecution.Poll();
			Assert.That(DexcRuntimeUIState.AsyncExecution, Is.Null);
			Assert.That(DexcRuntimeUIState.AsyncTask, Is.Null);
			Assert.That(
				DexcRuntimeUIState.LastCommandResult,
				Is.Null,
				"An injected execution without an originating draft must not attach a UI result."
			);
			DexcRuntimeUIState.LastCommandResult = null;
			var resetSource = new CancellationTokenSource();
			resetSource.Token.Register(() => DebuggerRuntime.Initialize());
			DexcRuntimeUIState.AsyncTask = first;
			DexcRuntimeUIState.AsyncExecution = new LifetimeExecution(resetSource, first);
			DexcRuntimeCommandExecution.Poll();
			Assert.That(DexcRuntimeUIState.LastCommandResult, Is.Null);
		}

		[Test]
		public void ReinitializeInsideCommandStartCannotAttachOldExecution()
		{
			DebugMenu.Open();
			SelectAsync();
			LifetimeAsyncCommand.s_Reinitialize = true;
			DexcRuntimeCommandExecution.Request();
			Assert.That(DexcRuntimeUIState.AsyncExecution, Is.Null);
			Assert.That(DexcRuntimeUIState.AsyncTask, Is.Null);
		}

		private static void SelectAsync()
		{
			var page = DebugCommand.Registry.GetList(new DebugListQuery("ZZTests/FIX05A", string.Empty, new DebugPageRequest(0, 10)));
			DexcRuntimeUIRenderer.SelectCommandForTests(page.Items.Single());
		}

		private static CommandExecutionResult Result()
		{
			return new CommandExecutionResult(
				"test",
				"test",
				CommandExecutionStatus.Succeeded,
				CommandFailureKind.None,
				string.Empty,
				string.Empty,
				default(DebugDiagnosticId),
				DateTimeOffset.UtcNow,
				TimeSpan.Zero,
				null
			);
		}

		private sealed class LifetimeExecution : DebugCommandExecution
		{
			private readonly Task<CommandExecutionResult> m_Task;

			internal LifetimeExecution(CancellationTokenSource source, Task<CommandExecutionResult> task)
				: base("lifetime-test", source)
			{
				m_Task = task;
			}

			public override DebugCommandExecutionState State => DebugCommandExecutionState.Running;

			public override ValueTask<CommandExecutionResult> WaitAsync() => new ValueTask<CommandExecutionResult>(m_Task);
		}
	}

	[DebugCommandAttribute("ZZTests/FIX05A/Async")]
	public sealed class LifetimeAsyncCommand : AsyncDebugCommand
	{
		internal static TaskCompletionSource<bool> s_Completion = new TaskCompletionSource<bool>();
		internal static bool s_Reinitialize;

		protected override async ValueTask ExecuteAsync(CancellationToken cancellationToken)
		{
			if (s_Reinitialize)
			{
				DebuggerRuntime.Initialize();
			}
			await s_Completion.Task;
		}
	}
}
#endif
