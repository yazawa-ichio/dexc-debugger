using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcCoreContractTests
	{
		private int m_ArgumentCalls;

		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize();
			CoreContractProbe.Clear();
			IntegerVectorSettings.Changes = 0;
			IntegerVectorSettings.Writes = 0;
			m_ArgumentCalls = 0;
		}

		[TearDown]
		public void TearDown()
		{
			CoreContractProbe.Clear();
			DebuggerRuntime.ResetForTests();
		}

#if DEXC_DEBUGGER
		[TestCase(2)]
		[TestCase(3)]
		public void IntegerVectorsRoundTripResetAndApplyPreset(int dimensions)
		{
			var item = FindVector(dimensions);
			Assert.That(DebugSetting.Read(item.Id).TryGetValue(out var initial), Is.True);
			Assert.That(DebugSetting.Write(item.Id, initial).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(DebugSetting.Read(item.Id).TryGetValue(out var same), Is.True);
			Assert.That(same, Is.EqualTo(initial));

			var limits = VectorValue(dimensions, DebugValue.From(int.MinValue), DebugValue.From(int.MaxValue), DebugValue.From(-1));
			Assert.That(DebugSetting.Write(item.Id, limits).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(DebugSetting.Read(item.Id).TryGetValue(out var actual), Is.True);
			Assert.That(actual, Is.EqualTo(limits));
			Assert.That(DebugSetting.Reset(item.Id).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(DebugSetting.Read(item.Id).TryGetValue(out actual), Is.True);
			Assert.That(actual, Is.EqualTo(initial));

			var preset = DebugSetting.GetPresets(item.SetId, new DebugPageRequest(0, 20)).Items.Single();
			Assert.That(DebugSetting.PreviewPreset(preset.Id).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			IntegerVectorSettings.Changes = 0;
			Assert.That(DebugSetting.ApplyPreset(preset.Id).Status, Is.EqualTo(DebugOperationStatus.Succeeded));
			Assert.That(
				IntegerVectorSettings.Changes,
				Is.EqualTo(1),
				"The base virtual OnChanged contract must be used despite its overload."
			);
			Assert.That(DebugSetting.Read(item.Id).TryGetValue(out actual), Is.True);
			Assert.That(actual, Is.EqualTo(limits));
			Assert.That(DebugSetting.GetHistory(item.Id, new DebugPageRequest(0, 20)).Entries, Has.Count.EqualTo(4));
		}

		[TestCase(2)]
		[TestCase(3)]
		public void InvalidIntegerVectorComponentsNeverWriteNotifyOrAddHistory(int dimensions)
		{
			var item = FindVector(dimensions);
			Assert.That(DebugSetting.Read(item.Id).TryGetValue(out var before), Is.True);
			var invalid = new[]
			{
				DebugValue.From(2.0),
				DebugValue.From(2.5),
				DebugValue.From((uint)2),
				DebugValue.From((long)int.MinValue - 1),
				DebugValue.From((long)int.MaxValue + 1),
				DebugValue.From(long.MinValue),
				DebugValue.From(long.MaxValue),
				DebugValue.From("2"),
				DebugValue.Null,
			};
			IntegerVectorSettings.Writes = 0;
			IntegerVectorSettings.Changes = 0;
			for (var component = 0; component < dimensions; component++)
			{
				foreach (var bad in invalid)
				{
					var values = new[] { DebugValue.From(1), DebugValue.From(2), DebugValue.From(3) };
					values[component] = bad;
					var value = VectorValue(dimensions, values[0], values[1], values[2]);
					Assert.That(DebugSetting.Write(item.Id, value).Status, Is.EqualTo(DebugOperationStatus.Rejected));
				}
				var names = new[] { "x", "y", "z" };
				var missing = DebugValue.FromObject(
					names
						.Take(dimensions)
						.Where((name, index) => index != component)
						.Select(name => new DebugValueMember(name, DebugValue.From(1)))
				);
				Assert.That(DebugSetting.Write(item.Id, missing).Status, Is.EqualTo(DebugOperationStatus.Rejected));
			}
			Assert.That(DebugSetting.Read(item.Id).TryGetValue(out var after), Is.True);
			Assert.That(after, Is.EqualTo(before));
			Assert.That(IntegerVectorSettings.Writes, Is.Zero);
			Assert.That(IntegerVectorSettings.Changes, Is.Zero);
			Assert.That(DebugSetting.GetHistory(item.Id, new DebugPageRequest(0, 20)).Entries, Is.Empty);
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(2)]
		[TestCase(3)]
		public async Task CommandOverridesAndNotificationsIgnoreSameNameOverloads(int kind)
		{
			var command = CreateCommand(kind);
			var input = (InputInt)command.GetType().GetField("Amount").GetValue(command);
			Assert.That(Validate(command).CanExecute, Is.True);
			CoreContractProbe.Clear();
			DexcNotifyEnabledConsumer.Notify(command, input);
			Assert.That(CoreContractProbe.Notifications, Is.EqualTo(1));
			Assert.That(input.Value, Is.EqualTo(2));
			Assert.Throws<ArgumentException>(() => DexcNotifyEnabledConsumer.Notify(command, new InputInt(9)));
			Assert.Throws<ArgumentException>(() => DexcNotifyEnabledConsumer.Notify(command, input, input));
			Assert.That(CoreContractProbe.Notifications, Is.EqualTo(1));

			CoreContractProbe.ThrowOnChange = true;
			Assert.Throws<TargetInvocationException>(() => DexcNotifyEnabledConsumer.Notify(command, input));
			Assert.That(input.Value, Is.EqualTo(2), "Notification failures must restore the input snapshot.");
			CoreContractProbe.ThrowOnChange = false;
			CoreContractProbe.Reject = true;
			Assert.That(Validate(command).CanExecute, Is.False);
			CoreContractProbe.Reject = false;

			CommandExecutionResult result;
			if (command is DebugCommand sync)
			{
				result = DebugCommand.Draft.CreateRequest(sync).Dispatch();
			}
			else if (command is DebugCommand<int> typed)
			{
				var typedResult = DebugCommand.Draft.CreateRequest(typed).Dispatch();
				Assert.That(typedResult.TryGetOutput(out var value), Is.True);
				Assert.That(value, Is.EqualTo(2));
				result = typedResult;
			}
			else if (command is AsyncDebugCommand asyncCommand)
			{
				result = await DebugCommand.Draft.CreateRequest(asyncCommand).DispatchAsync();
			}
			else
			{
				var typedResult = await DebugCommand.Draft.CreateRequest((AsyncDebugCommand<int>)command).DispatchAsync();
				Assert.That(typedResult.TryGetOutput(out var value), Is.True);
				Assert.That(value, Is.EqualTo(2));
				result = typedResult;
			}
			Assert.That(result.Status, Is.EqualTo(CommandExecutionStatus.Succeeded), result.Message);
			Assert.That(CoreContractProbe.Executions, Is.EqualTo(1));
			Assert.That(CoreContractProbe.LastValue, Is.EqualTo(2));
			Assert.That(CoreContractProbe.Validations, Is.EqualTo(3));
		}

		private static DebugSettingListItem FindVector(int dimensions)
		{
			return DebugSetting
				.GetList(new DebugListQuery("ZZTests/FIX04/Vector" + dimensions, string.Empty, new DebugPageRequest(0, 20)))
				.Items.Single();
		}

		private static DebugValue VectorValue(int dimensions, DebugValue x, DebugValue y, DebugValue z)
		{
			var values = new[] { new DebugValueMember("x", x), new DebugValueMember("y", y), new DebugValueMember("z", z) };
			return DebugValue.FromObject(values.Take(dimensions));
		}

		private static DebugCommandValidationResult Validate(object command)
		{
			if (command is DebugCommand sync)
			{
				return DebugCommand.Draft.Validate(sync);
			}
			if (command is DebugCommand<int> typed)
			{
				return DebugCommand.Draft.Validate(typed);
			}
			if (command is AsyncDebugCommand asyncCommand)
			{
				return DebugCommand.Draft.Validate(asyncCommand);
			}
			return DebugCommand.Draft.Validate((AsyncDebugCommand<int>)command);
		}
#else
		[Test]
		public void DisabledNotifyEliminatesAllFourCallSitesIncludingArgumentEvaluation()
		{
			DebugCommand.Draft.NotifyInputsChanged((DebugCommand)CountArgument(0), new InputInt(1));
			DebugCommand.Draft.NotifyInputsChanged((DebugCommand<int>)CountArgument(1), new InputInt(1));
			DebugCommand.Draft.NotifyInputsChanged((AsyncDebugCommand)CountArgument(2), new InputInt(1));
			DebugCommand.Draft.NotifyInputsChanged((AsyncDebugCommand<int>)CountArgument(3), new InputInt(1));
			Assert.That(m_ArgumentCalls, Is.Zero);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
		}

		[Test]
		public void DisabledNotifyBodiesAreNoOpEvenForAnEnabledConsumerAndInvalidArguments()
		{
			for (var kind = 0; kind < 4; kind++)
			{
				var command = CreateCommand(kind);
				var input = (InputInt)command.GetType().GetField("Amount").GetValue(command);
				Assert.DoesNotThrow(() => DexcNotifyEnabledConsumer.Notify(command, input));
				Assert.That(input.Value, Is.EqualTo(1));
				Assert.DoesNotThrow(() => DexcNotifyEnabledConsumer.Notify(command, null));
			}
			foreach (var definition in typeof(DebugCommand.Draft).GetMethods().Where(method => method.Name == "NotifyInputsChanged"))
			{
				var method = definition.IsGenericMethodDefinition ? definition.MakeGenericMethod(typeof(int)) : definition;
				Assert.DoesNotThrow(() => method.Invoke(null, new object[] { null, null }));
			}
			Assert.That(CoreContractProbe.Notifications, Is.Zero);
			Assert.That(CoreContractProbe.Validations, Is.Zero);
			Assert.That(CoreContractProbe.Executions, Is.Zero);
			Assert.That(DebuggerRuntime.IsInitialized, Is.False);
			Assert.That(
				DebugCommand.Registry.GetList(new DebugListQuery("", "", new DebugPageRequest(0, 20))).Summary.State,
				Is.EqualTo(DebugCollectionState.Disabled)
			);
		}
#endif

		private object CountArgument(int kind)
		{
			m_ArgumentCalls++;
			return CreateCommand(kind);
		}

		private static object CreateCommand(int kind)
		{
			switch (kind)
			{
				case 0:
					return new OverloadedSyncCommand();
				case 1:
					return new OverloadedOutputCommand();
				case 2:
					return new OverloadedAsyncCommand();
				default:
					return new OverloadedAsyncOutputCommand();
			}
		}
	}

	internal static class CoreContractProbe
	{
		internal static int Notifications;
		internal static int Validations;
		internal static int Executions;
		internal static int LastValue;
		internal static bool Reject;
		internal static bool ThrowOnChange;

		internal static void Clear()
		{
			Notifications = Validations = Executions = LastValue = 0;
			Reject = ThrowOnChange = false;
		}

		internal static void Changed(InputInt input, DebugInputChanges changes)
		{
			Assert.That(changes.Contains(input), Is.True);
			Notifications++;
			input.Apply(DebugValue.From(input.Value + 1));
			if (ThrowOnChange)
			{
				throw new InvalidOperationException("Notification rollback fixture.");
			}
		}

		internal static void Validate(DebugCommandValidation validation)
		{
			Validations++;
			if (Reject)
			{
				validation.Error("fixture-reject", "Rejected by the contract override.");
			}
		}

		internal static int Execute(InputInt input)
		{
			Executions++;
			LastValue = input.Value;
			return input.Value;
		}
	}

	public sealed class IntegerVectorSettings : DebugSettingSet<IntegerVectorSettings>
	{
		public static int Changes;
		public static int Writes;
		private Vector2Int m_Two = new Vector2Int(3, -4);
		private Vector3Int m_Three = new Vector3Int(5, -6, 7);

		[DebugSetting("ZZTests/FIX04/Vector2")]
		public Vector2Int Two
		{
			get { return m_Two; }
			private set
			{
				Writes++;
				m_Two = value;
			}
		}

		[DebugSetting("ZZTests/FIX04/Vector3")]
		public Vector3Int Three
		{
			get { return m_Three; }
			private set
			{
				Writes++;
				m_Three = value;
			}
		}

		[DebugPreset("ZZTests/FIX04/Limits", "Limits", "Integer vector boundaries")]
		public static IntegerVectorSettings Limits
		{
			get
			{
				return new IntegerVectorSettings
				{
					Two = new Vector2Int(int.MinValue, int.MaxValue),
					Three = new Vector3Int(int.MinValue, int.MaxValue, -1),
				};
			}
		}

		protected override void OnChanged(DebugSettingChanges changes)
		{
			Changes++;
		}

		private void OnChanged(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}
	}

	[DebugCommandAttribute("ZZTests/FIX04/OverloadedSyncCommand", Access = DebugCommandAccess.ReadOnly)]
	public sealed class OverloadedSyncCommand : DebugCommand
	{
		public InputInt Amount = new InputInt(1);

		protected override void OnInputsChanged(DebugInputChanges changes)
		{
			CoreContractProbe.Changed(Amount, changes);
		}

		protected override void Validate(DebugCommandValidation validation)
		{
			CoreContractProbe.Validate(validation);
		}

		private void OnInputsChanged(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}

		private void Validate(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}

		protected override void Execute()
		{
			CoreContractProbe.Execute(Amount);
		}

		private void Execute(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}

		private void Execute<T>()
		{
			throw new InvalidOperationException("Wrong generic overload.");
		}
	}

	[DebugCommandAttribute("ZZTests/FIX04/OverloadedOutputCommand", Access = DebugCommandAccess.ReadOnly)]
	public sealed class OverloadedOutputCommand : DebugCommand<int>
	{
		public InputInt Amount = new InputInt(1);

		protected override void OnInputsChanged(DebugInputChanges changes)
		{
			CoreContractProbe.Changed(Amount, changes);
		}

		protected override void Validate(DebugCommandValidation validation)
		{
			CoreContractProbe.Validate(validation);
		}

		private void OnInputsChanged(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}

		private void Validate(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}

		protected override int Execute()
		{
			return CoreContractProbe.Execute(Amount);
		}

		private void Execute(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}

		private void Execute<T>()
		{
			throw new InvalidOperationException("Wrong generic overload.");
		}
	}

	[DebugCommandAttribute("ZZTests/FIX04/OverloadedAsyncCommand", Access = DebugCommandAccess.ReadOnly)]
	public sealed class OverloadedAsyncCommand : AsyncDebugCommand
	{
		public InputInt Amount = new InputInt(1);

		protected override void OnInputsChanged(DebugInputChanges changes)
		{
			CoreContractProbe.Changed(Amount, changes);
		}

		protected override void Validate(DebugCommandValidation validation)
		{
			CoreContractProbe.Validate(validation);
		}

		private void OnInputsChanged(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}

		private void Validate(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}

		protected override ValueTask ExecuteAsync(CancellationToken token)
		{
			CoreContractProbe.Execute(Amount);
			return default(ValueTask);
		}

		private void ExecuteAsync(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}

		private void ExecuteAsync<T>(CancellationToken token)
		{
			throw new InvalidOperationException("Wrong generic overload.");
		}
	}

	[DebugCommandAttribute("ZZTests/FIX04/OverloadedAsyncOutputCommand", Access = DebugCommandAccess.ReadOnly)]
	public sealed class OverloadedAsyncOutputCommand : AsyncDebugCommand<int>
	{
		public InputInt Amount = new InputInt(1);

		protected override void OnInputsChanged(DebugInputChanges changes)
		{
			CoreContractProbe.Changed(Amount, changes);
		}

		protected override void Validate(DebugCommandValidation validation)
		{
			CoreContractProbe.Validate(validation);
		}

		private void OnInputsChanged(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}

		private void Validate(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}

		protected override ValueTask<int> ExecuteAsync(CancellationToken token)
		{
			return new ValueTask<int>(CoreContractProbe.Execute(Amount));
		}

		private void ExecuteAsync(int ignored)
		{
			throw new InvalidOperationException("Wrong overload.");
		}

		private void ExecuteAsync<T>(CancellationToken token)
		{
			throw new InvalidOperationException("Wrong generic overload.");
		}
	}
}
