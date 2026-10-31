using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcCommandOutputTests
	{
		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.Initialize();
			ProjectionProbe.Next = null;
			ProjectionProbe.ToStringCalls = 0;
		}

		[TearDown]
		public void TearDown()
		{
			ProjectionProbe.Next = null;
			DebuggerRuntime.ResetForTests();
		}

#if DEXC_DEBUGGER
		[Test]
		public void BuiltInOutputsPreserveKindsValuesAndDeclaredType()
		{
			var cases = new[]
			{
				new { Raw = (object)true, Expected = DebugValue.From(true) },
				new { Raw = (object)sbyte.MinValue, Expected = DebugValue.From(sbyte.MinValue) },
				new { Raw = (object)byte.MaxValue, Expected = DebugValue.From(byte.MaxValue) },
				new { Raw = (object)short.MinValue, Expected = DebugValue.From(short.MinValue) },
				new { Raw = (object)ushort.MaxValue, Expected = DebugValue.From(ushort.MaxValue) },
				new { Raw = (object)int.MinValue, Expected = DebugValue.From(int.MinValue) },
				new { Raw = (object)uint.MaxValue, Expected = DebugValue.From(uint.MaxValue) },
				new { Raw = (object)long.MinValue, Expected = DebugValue.From(long.MinValue) },
				new { Raw = (object)long.MaxValue, Expected = DebugValue.From(long.MaxValue) },
				new { Raw = (object)ulong.MaxValue, Expected = DebugValue.From(ulong.MaxValue) },
				new { Raw = (object)float.MaxValue, Expected = DebugValue.From(float.MaxValue) },
				new { Raw = (object)double.MinValue, Expected = DebugValue.From(double.MinValue) },
				new { Raw = (object)"結果\"\n日本語", Expected = DebugValue.From("結果\"\n日本語") },
				new { Raw = (object)ProjectionMode.Ready, Expected = DebugValue.From("Ready") },
			};
			foreach (var item in cases)
			{
				ProjectionProbe.Next = item.Raw;
				var request = DebugCommand.Draft.CreateRequest(new ProjectionOutputCommand());
				var typed = request.Dispatch();
				Assert.That(typed.Status, Is.EqualTo(CommandExecutionStatus.Succeeded), typed.Message);
				Assert.That(typed.Output, Is.EqualTo(item.Expected));
				Assert.That(typed.OutputTypeName, Is.EqualTo(typeof(object).FullName));
				Assert.That(typed.TryGetOutput(out var raw), Is.True);
				Assert.That(raw, Is.SameAs(item.Raw));
				var untyped = ((DebugCommandRequest)request).Dispatch();
				Assert.That(untyped, Is.TypeOf<CommandExecutionResult>());
				Assert.That(untyped.Output, Is.EqualTo(item.Expected));
			}
		}

		[Test]
		public void UnityValueOutputsPreserveMemberNamesAndKinds()
		{
			var values = new object[]
			{
				new Vector2(1, 2),
				new Vector3(1, 2, 3),
				new Vector4(1, 2, 3, 4),
				new Color(1, 2, 3, 4),
				new Vector2Int(int.MinValue, int.MaxValue),
				new Vector3Int(int.MinValue, int.MaxValue, -1),
			};
			foreach (var value in values)
			{
				ProjectionProbe.Next = value;
				var result = DebugCommand.Draft.CreateRequest(new ProjectionOutputCommand()).Dispatch();
				Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Succeeded), result.Message);
				Assert.That(result.Output.TryGetObject(out var members), Is.True);
				var integer = value is Vector2Int || value is Vector3Int;
				var names = value is Color ? new[] { "r", "g", "b", "a" } : new[] { "x", "y", "z", "w" };
				var count =
					value is Vector2 || value is Vector2Int ? 2
					: value is Vector3 || value is Vector3Int ? 3
					: 4;
				Assert.That(members.Select(member => member.Name), Is.EqualTo(names.Take(count)));
				for (var i = 0; i < count; i++)
				{
					var expected = integer
						? DebugValue.From(
							i == 0 ? int.MinValue
							: i == 1 ? int.MaxValue
							: -1
						)
						: DebugValue.From((float)(i + 1));
					Assert.That(members[i].Value, Is.EqualTo(expected));
				}
			}
		}

		[Test]
		public async Task JsonOutputsAreSnapshotsAndArraysDoNotUseInputCapacityLimits()
		{
			var data = new ProjectionData { Number = 7, Text = "before" };
			ProjectionProbe.Next = data;
			var typed = DebugCommand.Draft.CreateRequest(new ProjectionOutputCommand()).Dispatch();
			Assert.That(typed.Output.TryGetString(out var json), Is.True);
			Assert.That(JsonUtility.FromJson<ProjectionData>(json).Number, Is.EqualTo(7));
			data.Text = "after";
			Assert.That(JsonUtility.FromJson<ProjectionData>(json).Text, Is.EqualTo("before"));
			Assert.That(typed.TryGetOutput(out var raw), Is.True);
			Assert.That(raw, Is.SameAs(data));

			ProjectionProbe.Next = new ProjectionStruct { Number = 9 };
			var structured = DebugCommand.Draft.CreateRequest(new ProjectionOutputCommand()).Dispatch();
			Assert.That(structured.Output.TryGetString(out json), Is.True);
			Assert.That(JsonUtility.FromJson<ProjectionStruct>(json).Number, Is.EqualTo(9));

			var large = Enumerable.Range(0, DebugValue.MaxArrayLength + 17).ToArray();
			ProjectionProbe.Next = large;
			var request = DebugCommand.Draft.CreateRequest(new ProjectionArrayCommand());
			var array = request.Dispatch();
			Assert.That(array.Status, Is.EqualTo(CommandExecutionStatus.Succeeded), array.Message);
			Assert.That(array.OutputTypeName, Is.EqualTo(typeof(int[]).FullName));
			Assert.That(array.Output.TryGetString(out json), Is.True);
			Assert.That(JsonUtility.FromJson<ProjectionArrayJson>(json).Items, Is.EqualTo(large));
			Assert.That(array.TryGetOutput(out var arrayRaw), Is.True);
			Assert.That(arrayRaw, Is.SameAs(large));
			var history = DebugCommand.Draft.GetHistory(request.CommandId, new DebugPageRequest(0, 20)).Entries.Single();
			Assert.That(history.OutputSummary, Does.Contain(typeof(int[]).FullName));
			Assert.That(history.OutputSummary.Length, Is.LessThan(256));
			Assert.That(history.OutputSummary, Does.Not.Contain(json));

			ProjectionProbe.Next = new[] { data };
			var objects = DebugCommand.Draft.CreateRequest(new ProjectionOutputCommand()).Dispatch();
			Assert.That(objects.Output.TryGetString(out json), Is.True);
			Assert.That(JsonUtility.FromJson<ProjectionObjectArrayJson>(json).Items[0].Text, Is.EqualTo("after"));
			ProjectionProbe.Next = new List<int> { 2, 4 };
			var list = DebugCommand.Draft.CreateRequest(new ProjectionOutputCommand()).Dispatch();
			Assert.That(list.Output.TryGetString(out json), Is.True);
			Assert.That(JsonUtility.FromJson<ProjectionArrayJson>(json).Items, Is.EqualTo(new[] { 2, 4 }));
			ProjectionProbe.Next = data;
			var asyncTyped = await DebugCommand.Draft.CreateRequest(new ProjectionAsyncCommand()).DispatchAsync();
			Assert.That(asyncTyped.Status, Is.EqualTo(CommandExecutionStatus.Succeeded), asyncTyped.Message);
			Assert.That(asyncTyped.TryGetOutput(out raw), Is.True);
			Assert.That(raw, Is.SameAs(data));
			Assert.That(asyncTyped.Output.TryGetString(out json), Is.True);
			data.Number = 99;
			Assert.That(JsonUtility.FromJson<ProjectionData>(json).Number, Is.EqualTo(7));
			Assert.That(ProjectionProbe.ToStringCalls, Is.Zero);
		}

		[Test]
		public async Task NullOutputIsDistinctFromVoidForSyncAndAsync()
		{
			var typed = DebugCommand.Draft.CreateRequest(new ProjectionOutputCommand()).Dispatch();
			Assert.That(typed.TryGetOutput(out var raw), Is.True);
			Assert.That(raw, Is.Null);
			Assert.That(typed.Output, Is.SameAs(DebugValue.Null));
			var asyncTyped = await DebugCommand.Draft.CreateRequest(new ProjectionAsyncCommand()).DispatchAsync();
			Assert.That(asyncTyped.TryGetOutput(out raw), Is.True);
			Assert.That(raw, Is.Null);
			Assert.That(asyncTyped.Output, Is.SameAs(DebugValue.Null));
			Assert.That(asyncTyped.OutputTypeName, Is.EqualTo(typeof(object).FullName));
			var syncVoid = DebugCommand.Draft.CreateRequest(new OverloadedSyncCommand()).Dispatch();
			var asyncVoid = await DebugCommand.Draft.CreateRequest(new OverloadedAsyncCommand()).DispatchAsync();
			Assert.That(syncVoid.Output, Is.Null);
			Assert.That(asyncVoid.Output, Is.Null);
			Assert.That(syncVoid.OutputTypeName, Is.Empty);
			Assert.That(asyncVoid.OutputTypeName, Is.Empty);
		}

		[Test]
		public async Task ProjectionFailuresAreInternalAndDoNotPublishTypedOutput()
		{
			var owner = new GameObject("Projection failure fixture");
			try
			{
				var invalid = new object[]
				{
					float.NaN,
					double.PositiveInfinity,
					new Color(float.NaN, 0, 0),
					new Vector3(0, float.NegativeInfinity, 0),
					owner,
				};
				foreach (var value in invalid)
				{
					ProjectionProbe.Next = value;
					var request = DebugCommand.Draft.CreateRequest(new ProjectionOutputCommand());
					var typed = request.Dispatch();
					AssertProjectionFailure(typed);
					Assert.That(typed.TryGetOutput(out var raw), Is.False);
					Assert.That(raw, Is.Null);
					AssertProjectionFailure(((DebugCommandRequest)request).Dispatch());
					var history = DebugCommand.Draft.GetHistory(request.CommandId, new DebugPageRequest(0, 20)).Entries[0];
					Assert.That(history.Status, Is.EqualTo(CommandExecutionStatus.Failed));
					Assert.That(history.ErrorCode, Is.EqualTo(typed.ErrorCode));
					Assert.That(history.OutputSummary, Does.Contain("unavailable"));
				}

				var mainThread = Thread.CurrentThread.ManagedThreadId;
				var asyncRequest = DebugCommand.Draft.CreateRequest(new ProjectionAsyncCommand());
				using (var execution = asyncRequest.StartExecution())
				{
					var result = await execution.WaitTypedAsync();
					AssertProjectionFailure(result);
					Assert.That(result.TryGetOutput(out var raw), Is.False);
					Assert.That(raw, Is.Null);
					Assert.That(execution.State, Is.EqualTo(DebugCommandExecutionState.Failed));
					Assert.That(ProjectionProbe.CompletionThread, Is.Not.EqualTo(mainThread));
				}
				AssertProjectionFailure(await ((AsyncDebugCommandRequest)asyncRequest).DispatchAsync());
				Assert.That(
					DebugCommand.Draft.GetHistory(asyncRequest.CommandId, new DebugPageRequest(0, 20)).Entries,
					Has.Count.EqualTo(2)
				);
				ProjectionProbe.Next = 42;
				var recovered = await asyncRequest.DispatchAsync();
				Assert.That(
					recovered.Status,
					Is.EqualTo(CommandExecutionStatus.Succeeded),
					"Projection failure must release the execution gate."
				);
				Assert.That(recovered.Output, Is.EqualTo(DebugValue.From(42)));
			}
			finally
			{
				ProjectionProbe.Next = null;
				UnityEngine.Object.DestroyImmediate(owner);
			}
		}

		[Test]
		public async Task UntypedAsyncResultAndExecutionDoNotKeepRawOutputAlive()
		{
			var weak = CreateNextData();
			var request = (AsyncDebugCommandRequest)DebugCommand.Draft.CreateRequest(new ProjectionAsyncCommand());
			using (var execution = request.StartExecution())
			{
				var result = await execution.WaitAsync();
				ProjectionProbe.Next = null;
				Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Succeeded));
				Assert.That(result.Output.TryGetString(out var json), Is.True);
				Assert.That(JsonUtility.FromJson<ProjectionData>(json).Number, Is.EqualTo(37));
				// Right after the result arrives, Mono's conservative scan still finds the raw output in stack slots the
				// delivery left behind. One hop later it is collectable; a heap reference would survive the hop.
				await Task.Yield();
				for (var attempt = 0; attempt < 5 && weak.IsAlive; attempt++)
				{
					ScrubStack();
					GC.Collect();
					GC.WaitForPendingFinalizers();
					GC.Collect();
				}
				Assert.That(weak.IsAlive, Is.False, "Untyped UI execution must keep only the projection.");
				GC.KeepAlive(execution);
				GC.KeepAlive(result);
			}
		}

		[Test]
		public async Task CancelledAndTimedOutOutputsRemainUnavailable()
		{
			ProjectionProbe.Next = new ProjectionData();
			var cancelled = await DebugCommand.Draft.CreateRequest(new ProjectionAsyncCommand()).DispatchAsync(new CancellationToken(true));
			Assert.That(cancelled.Status, Is.EqualTo(CommandExecutionStatus.Cancelled));
			Assert.That(cancelled.TryGetOutput(out var raw), Is.False);
			Assert.That(raw, Is.Null);
			Assert.That(cancelled.Output, Is.Null);
			Assert.That(cancelled.OutputTypeName, Is.EqualTo(typeof(object).FullName));
			var timedOut = await DebugCommand.Draft.CreateRequest(new ProjectionTimeoutCommand()).DispatchAsync();
			Assert.That(timedOut.Status, Is.EqualTo(CommandExecutionStatus.TimedOut));
			Assert.That(timedOut.TryGetOutput(out raw), Is.False);
			Assert.That(raw, Is.Null);
			Assert.That(timedOut.Output, Is.Null);
			Assert.That(timedOut.OutputTypeName, Is.EqualTo(typeof(object).FullName));
		}

		private static void AssertProjectionFailure(CommandExecutionResult result)
		{
			Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Failed), result.Message);
			Assert.That(result.FailureKind, Is.EqualTo(CommandFailureKind.Internal));
			Assert.That(result.ErrorCode, Is.EqualTo("command-output-projection-failed"));
			Assert.That(result.OutputTypeName, Is.EqualTo(typeof(object).FullName));
			Assert.That(result.DiagnosticId.IsValid, Is.True);
			Assert.That(result.Output, Is.Null);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static WeakReference CreateNextData()
		{
			ProjectionProbe.Next = new ProjectionData { Number = 37, Text = "snapshot" };
			return new WeakReference(ProjectionProbe.Next);
		}
#endif

		[TestCase(CommandExecutionStatus.Failed)]
		[TestCase(CommandExecutionStatus.Cancelled)]
		[TestCase(CommandExecutionStatus.TimedOut)]
		public void UnsuccessfulTypedResultsDoNotExposeOrRetainOutput(CommandExecutionStatus status)
		{
			var result = CreateResult(status, true, out var weak);
			Assert.That(result.TryGetOutput(out var output), Is.False);
			Assert.That(output, Is.Null);
			for (var attempt = 0; attempt < 5 && weak.IsAlive; attempt++)
			{
				ScrubStack();
				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();
			}
			Assert.That(weak.IsAlive, Is.False, "Keeping the failed result must not keep its raw output alive.");
			GC.KeepAlive(result);
		}

		[Test]
		public void SuccessfulTypedResultsExposeOutputOnlyWhenPresent()
		{
			var result = CreateResult(CommandExecutionStatus.Succeeded, true, out var weak);
			Assert.That(result.TryGetOutput(out var output), Is.True);
			Assert.That(output, Is.SameAs(weak.Target));
			var absent = CreateResult(CommandExecutionStatus.Succeeded, false, out _);
			Assert.That(absent.TryGetOutput(out output), Is.False);
			Assert.That(output, Is.Null);
		}

		/// <summary>Overwrites stale stack slots so Mono's conservative scan cannot keep a dead reference alive.</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void ScrubStack()
		{
			Span<byte> buffer = stackalloc byte[16 * 1024];
			buffer.Clear();
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static CommandExecutionResult<object> CreateResult(CommandExecutionStatus status, bool hasOutput, out WeakReference weak)
		{
			var raw = new object();
			weak = new WeakReference(raw);
			var result = new CommandExecutionResult(
				"fixture",
				"ZZTests/FIX04/Output",
				status,
				CommandFailureKind.None,
				"",
				"",
				default(DebugDiagnosticId),
				DateTimeOffset.UtcNow,
				TimeSpan.Zero,
				null
			);
			return new CommandExecutionResult<object>(result, raw, hasOutput);
		}
	}

	internal static class ProjectionProbe
	{
		internal static object Next;
		internal static int ToStringCalls;
		internal static int CompletionThread;
	}

	public enum ProjectionMode
	{
		Ready,
	}

	[Serializable]
	public sealed class ProjectionData
	{
		public int Number;
		public string Text;

		public override string ToString()
		{
			ProjectionProbe.ToStringCalls++;
			throw new InvalidOperationException("Raw ToString must not run.");
		}
	}

	[Serializable]
	public struct ProjectionStruct
	{
		public int Number;
	}

	[Serializable]
	public sealed class ProjectionArrayJson
	{
		public int[] Items;
	}

	[Serializable]
	public sealed class ProjectionObjectArrayJson
	{
		public ProjectionData[] Items;
	}

	[DebugCommandAttribute("ZZTests/FIX04B/Output")]
	public sealed class ProjectionOutputCommand : DebugCommand<object>
	{
		protected override object Execute()
		{
			return ProjectionProbe.Next;
		}
	}

	[DebugCommandAttribute("ZZTests/FIX04B/Array")]
	public sealed class ProjectionArrayCommand : DebugCommand<int[]>
	{
		protected override int[] Execute()
		{
			return (int[])ProjectionProbe.Next;
		}
	}

	[DebugCommandAttribute("ZZTests/FIX04B/AsyncOutput")]
	public sealed class ProjectionAsyncCommand : AsyncDebugCommand<object>
	{
		protected override async ValueTask<object> ExecuteAsync(CancellationToken token)
		{
			return await Task.Run(() =>
				{
					ProjectionProbe.CompletionThread = Thread.CurrentThread.ManagedThreadId;
					return ProjectionProbe.Next;
				})
				.ConfigureAwait(false);
		}
	}

	[DebugCommandAttribute("ZZTests/FIX04B/TimeoutOutput")]
	public sealed class ProjectionTimeoutCommand : AsyncDebugCommand<object>
	{
		protected override TimeSpan Timeout
		{
			get { return TimeSpan.FromMilliseconds(1); }
		}

		protected override async ValueTask<object> ExecuteAsync(CancellationToken token)
		{
			await Task.Delay(50).ConfigureAwait(false);
			return ProjectionProbe.Next;
		}
	}
}
