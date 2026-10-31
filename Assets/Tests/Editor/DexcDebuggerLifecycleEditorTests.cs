using Dexc;
using NUnit.Framework;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcDebuggerLifecycleEditorTests
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
		public void SubsystemResetSimulatesDomainReloadDisabledPlaySessionBoundary()
		{
			DebuggerRuntime.Initialize();
			var previous = DebuggerRuntime.CurrentSession;

			DebuggerRuntime.ResetForTests();

			Assert.That(previous.IsCurrent, Is.False);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
		}
#else
		[Test]
		public void DisabledAssemblyRemainsUninitializedAcrossReset()
		{
			DebuggerRuntime.ResetForTests();

			Assert.That(DebuggerRuntime.IsEnabled, Is.False);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
		}
#endif
	}
}
