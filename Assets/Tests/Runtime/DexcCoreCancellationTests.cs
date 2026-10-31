#if DEXC_DEBUGGER
using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcCoreCancellationTests
	{
		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.ResetForTests();
			CoreCancellationProbe.Current = new CoreCancellationProbe();
			DebuggerRuntime.Initialize();
		}

		[TearDown]
		public void TearDown()
		{
			CoreCancellationProbe.Current.Gate.TrySetResult(1);
			DebuggerRuntime.ResetForTests();
		}

		[Test]
		public async Task DisposeBeforeTimeoutKeepsTokenAndGateUntilRealCompletion()
		{
			var probe = CoreCancellationProbe.Current;
			var request = DebugCommand.Draft.CreateRequest(new CoreLifetimeCommand());
			var execution = request.StartExecution();
			var task = execution.WaitTypedAsync().AsTask();
			execution.Dispose();
			Assert.That(probe.Token.IsCancellationRequested, Is.True);
			Assert.DoesNotThrow(() =>
			{
				var handle = probe.Token.WaitHandle;
			});
			await Task.Delay(250);
			Assert.That(task.IsCompleted, Is.False);
			Assert.That(execution.State, Is.EqualTo(DebugCommandExecutionState.Cancelling));
			AssertLocked();
			probe.Gate.SetResult(42);
			var result = await task;
			Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Cancelled));
			Assert.That(result.TryGetOutput(out var value), Is.False);
			Assert.That(value, Is.Zero);
			Assert.That(result.Output, Is.Null);
			AssertReleased(probe, execution, request.CommandId);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task TimeoutWinsLaterCancelAndCallbackFailureCannotReleaseGate(bool throwCallback)
		{
			var probe = CoreCancellationProbe.Current;
			probe.OnCancel = () =>
			{
				if (throwCallback)
					throw new InvalidOperationException("callback-fault");
			};
			var request = DebugCommand.Draft.CreateRequest(new CoreLifetimeTimeoutCommand());
			using (var execution = request.StartExecution())
			{
				var task = execution.WaitTypedAsync().AsTask();
				await WaitUntil(() => execution.State == DebugCommandExecutionState.TimingOut);
				execution.Cancel();
				Assert.That(task.IsCompleted, Is.False);
				AssertLocked();
				probe.Gate.SetResult(42);
				var result = await task;
				Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.TimedOut));
				Assert.That(result.TryGetOutput(out _), Is.False);
				AssertReleased(probe, execution, request.CommandId);
			}
		}

		[TestCase(false, false)]
		[TestCase(false, true)]
		[TestCase(true, false)]
		[TestCase(true, true)]
		public async Task OnlyRequestedExecutionTokenProducesCancelledResult(bool cancel, bool sameToken)
		{
			var probe = CoreCancellationProbe.Current;
			using (var execution = DebugCommand.Draft.CreateRequest(new CoreLifetimeCommand()).StartExecution())
			{
				if (cancel)
					execution.Cancel();
				probe.Gate.SetException(new OperationCanceledException(sameToken ? probe.Token : new CancellationToken(true)));
				var result = await execution.WaitTypedAsync();
				Assert.That(
					result.Status,
					Is.EqualTo(cancel && sameToken ? CommandExecutionStatus.Cancelled : CommandExecutionStatus.Failed)
				);
				Assert.That(
					result.FailureKind,
					Is.EqualTo(cancel && sameToken ? CommandFailureKind.None : CommandFailureKind.UnhandledException)
				);
				Assert.That(result.TryGetOutput(out _), Is.False);
			}
		}

		[Test]
		public async Task CallerTokenCancellationSurfacesCallbackFailureWithoutCompletingCommand()
		{
			var probe = CoreCancellationProbe.Current;
			probe.OnCancel = () =>
			{
				throw new InvalidOperationException("external-callback-fault");
			};
			using (var source = new CancellationTokenSource())
			using (var execution = DebugCommand.Draft.CreateRequest(new CoreLifetimeCommand()).StartExecution(source.Token))
			{
				var pending = execution.WaitAsync().AsTask();
				// The failing callback was registered by the Command on its own token; the caller's Cancel is what runs it.
				Assert.Throws<AggregateException>(() => source.Cancel());
				Assert.That(execution.State, Is.EqualTo(DebugCommandExecutionState.Cancelling));
				Assert.That(pending.IsCompleted, Is.False);
				AssertLocked();
				probe.Gate.SetException(new OperationCanceledException(probe.Token));
				Assert.That((await pending).Status, Is.EqualTo(CommandExecutionStatus.Cancelled));
			}
		}

		[Test]
		public async Task SessionEndCancelsTypedAndUntypedDirectDispatchAndSuppressesOldHistory()
		{
			var first = CoreCancellationProbe.Current;
			first.OnCancel = () => DebuggerRuntime.Initialize();
			var firstRequest = DebugCommand.Draft.CreateRequest(new CoreLifetimeReadOnlyCommand());
			var typed = firstRequest.DispatchAsync().AsTask();
			var second = new CoreCancellationProbe();
			CoreCancellationProbe.Current = second;
			var secondRequest = DebugCommand.Draft.CreateRequest(new CoreLifetimeReadOnlyCommand());
			var untyped = ((AsyncDebugCommandRequest)secondRequest).DispatchAsync().AsTask();
			var oldSession = DebuggerRuntime.CurrentSession;
			DebuggerRuntime.Initialize();
			Assert.That(first.Token.IsCancellationRequested, Is.True);
			Assert.That(second.Token.IsCancellationRequested, Is.True, "a throwing/reentrant callback must not skip other executions");
			Assert.That(typed.IsCompleted || untyped.IsCompleted, Is.False);
			first.Gate.SetResult(11);
			second.Gate.SetResult(22);
			Assert.That((await typed).Status, Is.EqualTo(CommandExecutionStatus.Cancelled));
			Assert.That((await untyped).Status, Is.EqualTo(CommandExecutionStatus.Cancelled));
			Assert.That(oldSession.CommandHistory, Is.Null);
			Assert.That(DebuggerRuntime.CurrentSession.CommandHistory, Is.Null);
			Assert.That(TrackedCount(oldSession), Is.Zero);
			Assert.That(TrackedCount(DebuggerRuntime.CurrentSession), Is.Zero);
			Assert.Throws<ObjectDisposedException>(() =>
			{
				var handle = first.Token.WaitHandle;
			});
			Assert.Throws<ObjectDisposedException>(() =>
			{
				var handle = second.Token.WaitHandle;
			});
		}

		[Test]
		public async Task SuccessfulCompletionDisposesResourcesAndIgnoresLateCancellation()
		{
			var probe = CoreCancellationProbe.Current;
			using (var source = new CancellationTokenSource())
			using (var execution = DebugCommand.Draft.CreateRequest(new CoreLifetimeCommand()).StartExecution(source.Token))
			{
				probe.Gate.SetResult(17);
				var result = await execution.WaitTypedAsync();
				Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
				Assert.That(result.TryGetOutput(out var value), Is.True);
				Assert.That(value, Is.EqualTo(17));
				source.Cancel();
				execution.Cancel();
				execution.Dispose();
				Assert.That(probe.Cancellations, Is.Zero);
				Assert.That(execution.State, Is.EqualTo(DebugCommandExecutionState.Succeeded));
				Assert.That(TrackedCount(DebuggerRuntime.CurrentSession), Is.Zero);
				Assert.Throws<ObjectDisposedException>(() =>
				{
					var handle = probe.Token.WaitHandle;
				});
			}
		}

		[Test]
		public async Task ControllerUnregistersExternalTokenAndTimerAfterCompletion()
		{
			using (var external = new CancellationTokenSource())
			{
				var weak = FinishController(external.Token);
				for (var index = 0; index < 3; index++)
				{
					GC.Collect();
					GC.WaitForPendingFinalizers();
					await Task.Delay(10);
				}
				Assert.That(weak.IsAlive, Is.False, "a live external token or timer must not retain a completed execution");
				external.Cancel();
			}
		}

		[Test]
		public async Task SynchronousCancellationThrowUsesExecutionTokenAndRecordsHistory()
		{
			using (var external = new CancellationTokenSource())
			{
				CoreCancellationProbe.Current.OnStart = () => external.Cancel();
				var request = DebugCommand.Draft.CreateRequest(new CoreLifetimeImmediateCommand());
				var result = await request.DispatchAsync(external.Token);
				Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Cancelled));
				Assert.That(DebugCommand.Draft.GetHistory(request.CommandId, new DebugPageRequest(0, 10)).Entries.Count, Is.EqualTo(1));
				Assert.That(TrackedCount(DebuggerRuntime.CurrentSession), Is.Zero);
			}
		}

		[Test]
		public async Task UnhandledFailureAfterCancelRemainsACommandFailure()
		{
			using (var execution = DebugCommand.Draft.CreateRequest(new CoreLifetimeCommand()).StartExecution())
			{
				execution.Cancel();
				CoreCancellationProbe.Current.Gate.SetException(new InvalidOperationException("after-cancel-fault"));
				var result = await execution.WaitTypedAsync();
				Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Failed));
				Assert.That(result.FailureKind, Is.EqualTo(CommandFailureKind.UnhandledException));
				Assert.That(result.TryGetOutput(out _), Is.False);
			}
		}

		[Test]
		public void CompletedTaskWinsCancellationBeforeQueuedContinuation()
		{
			var controller = new DexcCommandCancellation(CancellationToken.None, _ => Assert.Fail("callback failure"));
			var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			controller.Track(completion.Task);
			completion.SetResult(true);
			controller.Request(DexcCommandCancellationCause.Cancelled);
			Assert.That(controller.Cause, Is.EqualTo(DexcCommandCancellationCause.None));
			Assert.That(controller.Token.IsCancellationRequested, Is.False);
			controller.Finish();
		}

		[Test]
		public async Task PreCancelledStaleAndNullRequestsReleasePreflightOwnership()
		{
			var request = DebugCommand.Draft.CreateRequest(new CoreLifetimeCommand());
			using (var execution = request.StartExecution(new CancellationToken(true)))
			{
				Assert.That((await execution.WaitAsync()).Status, Is.EqualTo(CommandExecutionStatus.Cancelled));
				Assert.That(CoreCancellationProbe.Current.Token.CanBeCanceled, Is.False);
				Assert.That(TrackedCount(DebuggerRuntime.CurrentSession), Is.Zero);
			}
			DebuggerRuntime.Initialize();
			using (var execution = request.StartExecution())
			{
				Assert.That((await execution.WaitAsync()).ErrorCode, Is.EqualTo("stale-request"));
				Assert.That(TrackedCount(DebuggerRuntime.CurrentSession), Is.Zero);
			}
			using (var execution = ((AsyncDebugCommandRequest)null).StartExecution())
			{
				Assert.That((await execution.WaitAsync()).ErrorCode, Is.EqualTo("request-null"));
				Assert.That(TrackedCount(DebuggerRuntime.CurrentSession), Is.Zero);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static WeakReference FinishController(CancellationToken external)
		{
			var controller = new DexcCommandCancellation(external, _ => { });
			var completion = new TaskCompletionSource<bool>();
			controller.Track(completion.Task);
			controller.StartTimeout(TimeSpan.FromHours(1));
			completion.SetResult(true);
			controller.Finish();
			return new WeakReference(controller);
		}

		private static void AssertLocked()
		{
			var result = DebugCommand.Draft.CreateRequest(new CoreLifetimeGateCommand()).Dispatch();
			Assert.That(result.ErrorCode, Is.EqualTo("command-locked"));
		}

		private static void AssertReleased(CoreCancellationProbe probe, DebugCommandExecution execution, DebugCommandId id)
		{
			Assert.Throws<ObjectDisposedException>(() =>
			{
				var handle = probe.Token.WaitHandle;
			});
			Assert.DoesNotThrow(execution.Dispose);
			Assert.That(DebugCommand.Draft.GetHistory(id, new DebugPageRequest(0, 10)).Entries.Count, Is.EqualTo(1));
			Assert.That(TrackedCount(DebuggerRuntime.CurrentSession), Is.Zero);
			Assert.That(
				DebugCommand.Draft.CreateRequest(new CoreLifetimeGateCommand()).Dispatch().Status,
				Is.EqualTo(CommandExecutionStatus.Succeeded)
			);
		}

		private static int TrackedCount(DexcRuntimeSession session)
		{
			var collection = typeof(DexcRuntimeSession)
				.GetField("m_RunningCommands", BindingFlags.Instance | BindingFlags.NonPublic)
				.GetValue(session);
			return (int)collection.GetType().GetProperty("Count").GetValue(collection);
		}

		private static async Task WaitUntil(Func<bool> condition)
		{
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (!condition() && DateTime.UtcNow < deadline)
				await Task.Delay(5);
			Assert.That(condition(), Is.True, "timed out waiting for state transition");
		}
	}

	internal sealed class CoreCancellationProbe
	{
		internal static CoreCancellationProbe Current = new CoreCancellationProbe();
		internal readonly TaskCompletionSource<int> Gate = new TaskCompletionSource<int>(
			TaskCreationOptions.RunContinuationsAsynchronously
		);
		internal CancellationToken Token;
		internal Action OnCancel;
		internal Action OnStart;
		internal int Cancellations;

		internal async ValueTask<int> Run(CancellationToken token)
		{
			Token = token;
			token.Register(() =>
			{
				Interlocked.Increment(ref Cancellations);
				OnCancel?.Invoke();
			});
			return await Gate.Task.ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Waits on the probe and, for these tests, never times out on its own. A timeout is part of a command's definition
	/// and is read once, when definitions are collected, so a test that wants a different one declares its own type
	/// rather than moving a value the collected definition cannot see change.
	/// </summary>
	[DebugCommandAttribute("ZZTests/FIX05B/Lock")]
	public sealed class CoreLifetimeCommand : AsyncDebugCommand<int>
	{
		protected override TimeSpan Timeout => TimeSpan.FromHours(1);

		protected override ValueTask<int> ExecuteAsync(CancellationToken token) => CoreCancellationProbe.Current.Run(token);
	}

	/// <summary>The same wait, but short enough that the timeout is what finishes it.</summary>
	[DebugCommandAttribute("ZZTests/FIX05B/LockTimeout")]
	public sealed class CoreLifetimeTimeoutCommand : AsyncDebugCommand<int>
	{
		protected override TimeSpan Timeout => TimeSpan.FromMilliseconds(30);

		protected override ValueTask<int> ExecuteAsync(CancellationToken token) => CoreCancellationProbe.Current.Run(token);
	}

	[DebugCommandAttribute("ZZTests/FIX05B/ReadOnly", Access = DebugCommandAccess.ReadOnly)]
	public sealed class CoreLifetimeReadOnlyCommand : AsyncDebugCommand<int>
	{
		protected override TimeSpan Timeout => System.Threading.Timeout.InfiniteTimeSpan;

		protected override ValueTask<int> ExecuteAsync(CancellationToken token) => CoreCancellationProbe.Current.Run(token);
	}

	[DebugCommandAttribute("ZZTests/FIX05B/Gate")]
	public sealed class CoreLifetimeGateCommand : DebugCommand
	{
		protected override void Execute() { }
	}

	[DebugCommandAttribute("ZZTests/FIX05B/Immediate")]
	public sealed class CoreLifetimeImmediateCommand : AsyncDebugCommand
	{
		protected override ValueTask ExecuteAsync(CancellationToken token)
		{
			CoreCancellationProbe.Current.OnStart?.Invoke();
			token.ThrowIfCancellationRequested();
			return default(ValueTask);
		}
	}
}
#endif
