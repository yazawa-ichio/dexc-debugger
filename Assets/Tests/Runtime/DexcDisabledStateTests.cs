using System;
using Dexc;
using NUnit.Framework;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
#if !DEXC_DEBUGGER
	[TestFixture]
	public sealed class DexcDisabledStateTests
	{
		private int m_ProviderFactoryCalls;

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

		[Test]
		public void FeatureReadApisReturnDisabledWithoutCreatingARegistry()
		{
			var command = DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 10)));
			var setting = DebugSetting.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 10)));
			var inspector = DebugInspector.GetRegistered(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 10)));

			Assert.That(DebuggerRuntime.IsEnabled, Is.False);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
			Assert.That(command.Summary.State, Is.EqualTo(DebugCollectionState.Disabled));
			Assert.That(command.Items, Is.Empty);
			Assert.That(setting.Summary.State, Is.EqualTo(DebugCollectionState.Disabled));
			Assert.That(setting.Items, Is.Empty);
			Assert.That(inspector.Entries, Is.Empty);
			Assert.That(DebugSetting.Read(default(DebugSettingId)).Status, Is.EqualTo(DebugOperationStatus.Disabled));
			Assert.That(DebugSetting.Write(default(DebugSettingId), DebugValue.From(1)).Status, Is.EqualTo(DebugOperationStatus.Disabled));
			Assert.That(DebugCommand.Draft.Create(default(DebugCommandId)).Status, Is.EqualTo(DebugCommandDraftOperationStatus.Disabled));
			Assert.That(DebugInspector.GetComponents(default(DebugInspectorTargetHandle)).Components, Is.Empty);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
		}

		[Test]
		public void DisplayReturnsNoOpAndDoesNotCreateACompletedFrame()
		{
			var owner = new UnityEngine.GameObject("DexcDisabledDisplayOwner");
			try
			{
				var subject = DebugDisplay.CreateSubject("Disabled", owner);
				Assert.That(subject.Id.IsValid, Is.False);
				Assert.That(subject.Label, Is.Empty);
				Assert.That(DebugDisplay.TryGetCompletedFrame(out _), Is.False);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(owner);
			}
		}

		[Test]
		public void ConditionalVoidEntryRemovesArgumentEvaluation()
		{
			DebugMenu.SetInputProvider(CreateProvider());
			Assert.That(m_ProviderFactoryCalls, Is.EqualTo(0));
		}

		[Test]
		public void TypedCommandValidationAndRequestStayDisabledWithoutRuntimeSideEffects()
		{
			IncrementCommand.Executions = 0;
			var sync = new IncrementCommand();
			var syncOutput = new DisabledOutputCommand();
			var asyncCommand = new AsyncProbeCommand();
			var asyncOutput = new DisabledAsyncOutputCommand();

			AssertDisabledValidation(DebugCommand.Draft.Validate(sync));
			AssertDisabledValidation(DebugCommand.Draft.Validate(syncOutput));
			AssertDisabledValidation(DebugCommand.Draft.Validate(asyncCommand));
			AssertDisabledValidation(DebugCommand.Draft.Validate(asyncOutput));

			AssertDisabledRequest(() => DebugCommand.Draft.CreateRequest(sync));
			AssertDisabledRequest(() => DebugCommand.Draft.CreateRequest(syncOutput));
			AssertDisabledRequest(() => DebugCommand.Draft.CreateRequest(asyncCommand));
			AssertDisabledRequest(() => DebugCommand.Draft.CreateRequest(asyncOutput));

			var commandList = DebugCommand.Registry.GetList(new DebugListQuery(string.Empty, string.Empty, new DebugPageRequest(0, 10)));
			Assert.That(commandList.Summary.State, Is.EqualTo(DebugCollectionState.Disabled));
			Assert.That(commandList.Items, Is.Empty);
			Assert.That(DebugCommand.Draft.GetHistory(default(DebugCommandId), new DebugPageRequest(0, 10)).Entries, Is.Empty);
			Assert.That(IncrementCommand.Executions, Is.Zero);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
		}

		private static void AssertDisabledValidation(DebugCommandValidationResult validation)
		{
			Assert.That(validation, Is.Not.Null);
			Assert.That(validation.CanExecute, Is.False);
			Assert.That(validation.Issues, Has.Count.EqualTo(1));
			Assert.That(validation.Issues[0].IsUnavailable, Is.True);
			Assert.That(validation.Issues[0].Severity, Is.EqualTo(DebugValidationSeverity.Error));
		}

		private static void AssertDisabledRequest(Func<DebugCommandRequest> create)
		{
			var exception = Assert.Throws<DebugCommandValidationException>(() => create());
			AssertDisabledValidation(exception.Validation);
		}

		private DebugMenuInputProvider CreateProvider()
		{
			m_ProviderFactoryCalls++;
			return new DisabledInputProvider();
		}

		private sealed class DisabledInputProvider : DebugMenuInputProvider
		{
			public override DebugMenuInputFrame ReadInput()
			{
				return new DebugMenuInputFrame(UnityEngine.Vector2.zero, false, false);
			}
		}
	}
#endif
}
