using System;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcDebuggerInitializationTests
	{
		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.ResetForTests();
		}

		[TearDown]
		public void TearDown()
		{
			DebuggerRuntime.ResetForTests();
		}

#if DEXC_DEBUGGER
		[Test]
		public void InitializeCopiesOptionsAndStartsSession()
		{
			DexcDebuggerOptions captured = null;
			DebuggerRuntime.Initialize(options =>
			{
				captured = options;
				options.Command.DefaultAccess = DebugCommandAccess.ReadOnly;
				options.History.CapacityPerFeature = 7;
			});

			Assert.That(DebuggerRuntime.IsEnabled, Is.True);
			Assert.That(DebuggerRuntime.IsInitialized, Is.True);
			Assert.That(captured, Is.Not.Null);
			Assert.That(DebuggerRuntime.CurrentSession.Options.DefaultAccess, Is.EqualTo(DebugCommandAccess.ReadOnly));
			Assert.That(DebuggerRuntime.CurrentSession.Options.HistoryCapacityPerFeature, Is.EqualTo(7));

			captured.Command.DefaultAccess = DebugCommandAccess.Lock;
			captured.History.CapacityPerFeature = 99;

			Assert.That(DebuggerRuntime.CurrentSession.Options.DefaultAccess, Is.EqualTo(DebugCommandAccess.ReadOnly));
			Assert.That(DebuggerRuntime.CurrentSession.Options.HistoryCapacityPerFeature, Is.EqualTo(7));
		}

		[Test]
		public void ReinitializeInvalidatesPreviousGenerationBeforeStartingNewSession()
		{
			DebuggerRuntime.Initialize();
			var previous = DebuggerRuntime.CurrentSession;

			DebuggerRuntime.Initialize();
			var current = DebuggerRuntime.CurrentSession;

			Assert.That(previous.IsCurrent, Is.False);
			Assert.That(current.IsCurrent, Is.True);
			Assert.That(current.Generation, Is.GreaterThan(previous.Generation));
			Assert.That(DebuggerRuntime.IsInitialized, Is.True);
		}

		[Test]
		public void CallbackExceptionDoesNotRestorePreviousSession()
		{
			DebuggerRuntime.Initialize();
			var previous = DebuggerRuntime.CurrentSession;
			var expected = new InvalidOperationException("callback failure");

			var actual = Assert.Throws<InvalidOperationException>(() => DebuggerRuntime.Initialize(_ => throw expected));

			Assert.That(actual, Is.SameAs(expected));
			Assert.That(previous.IsCurrent, Is.False);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
			Assert.That(DebuggerRuntime.CurrentSession, Is.Null);
		}

		[Test]
		public void InvalidOptionsLeaveSessionUninitialized()
		{
			var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
				DebuggerRuntime.Initialize(options => options.Query.MaxPageSize = 0)
			);

			Assert.That(exception.ParamName, Is.EqualTo("MaxPageSize"));
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
		}

		[Test]
		public void NullCallbackIsRejectedAndKeepsTheCurrentSession()
		{
			Action<DexcDebuggerOptions> callback = null;

			Assert.Throws<ArgumentNullException>(() => DebuggerRuntime.Initialize(callback));
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);

			DebuggerRuntime.Initialize();
			Assert.Throws<ArgumentNullException>(() => DebuggerRuntime.Initialize(callback));
			Assert.That(DebuggerRuntime.IsInitialized, Is.True);
		}

		[Test]
		public void RecursiveInitializeIsRejectedBeforeInvalidatingSession()
		{
			DebuggerRuntime.Initialize();
			var previous = DebuggerRuntime.CurrentSession;

			Assert.Throws<InvalidOperationException>(() => DebuggerRuntime.Initialize(_ => DebuggerRuntime.Initialize()));

			Assert.That(previous.IsCurrent, Is.False);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
		}

		[Test]
		public void SubsystemResetClearsSession()
		{
			DebuggerRuntime.Initialize();
			var previous = DebuggerRuntime.CurrentSession;

			DebuggerRuntime.ResetForTests();

			Assert.That(previous.IsCurrent, Is.False);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
			Assert.That(DebuggerRuntime.CurrentSession, Is.Null);
		}
#else
		[Test]
		public void DisabledAssemblyReportsDisabledAndUninitialized()
		{
			Assert.That(DebuggerRuntime.IsEnabled, Is.False);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
		}
#endif
	}
}
