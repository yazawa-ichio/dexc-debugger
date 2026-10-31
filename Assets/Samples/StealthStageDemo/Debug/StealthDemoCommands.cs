using Dexc;

namespace DexcDebugger.Samples.StealthStageDemo
{
	[DebugCommand(
		"Samples/Stealth/Commands/Restart Stage",
		Description = "Restart the current stage at its initial state.",
		Access = DebugCommandAccess.Lock
	)]
	public sealed class RestartStealthStageCommand : DebugCommand<string>
	{
		protected override void Validate(DebugCommandValidation validation)
		{
			if (StealthStageDemoController.Instance == null)
			{
				validation.Unavailable("stealth-not-running", "The Stealth Stage Demo is not running.");
			}
		}

		protected override string Execute()
		{
			return StealthStageDemoController.Instance.RestartStage();
		}
	}

	[DebugCommand(
		"Samples/Stealth/Commands/Next Stage",
		Description = "Advance to the next stage or finish the demo.",
		Access = DebugCommandAccess.Lock
	)]
	public sealed class NextStealthStageCommand : DebugCommand<string>
	{
		protected override void Validate(DebugCommandValidation validation)
		{
			validation.Require(
				StealthStageDemoController.Instance != null,
				"stealth-not-running",
				"The Stealth Stage Demo is not running."
			);
		}

		protected override string Execute()
		{
			return StealthStageDemoController.Instance.NextStage();
		}
	}

	[DebugCommand(
		"Samples/Stealth/Commands/Load Stage",
		Description = "Load a one-based stage number. Values outside 1-3 are rejected.",
		Access = DebugCommandAccess.Lock
	)]
	public sealed class LoadStealthStageCommand : DebugCommand<string>
	{
		public InputInt StageNumber = new InputInt(1);

		protected override void Validate(DebugCommandValidation validation)
		{
			var game = StealthStageDemoController.Instance;
			validation.Require(game != null, "stealth-not-running", "The Stealth Stage Demo is not running.");
			validation.Require(
				game != null && StageNumber.Value >= 1 && StageNumber.Value <= game.StageCount,
				StageNumber,
				"stage-out-of-range",
				"Stage Number is not one of the demo's stages."
			);
		}

		protected override string Execute()
		{
			return StealthStageDemoController.Instance.LoadStageNumber(StageNumber.Value);
		}
	}

	[DebugCommand(
		"Samples/Stealth/Commands/Warp Player",
		Description = "Move the player to the stage start or just before the Exit.",
		Access = DebugCommandAccess.Lock
	)]
	public sealed class WarpStealthPlayerCommand : DebugCommand<string>
	{
		public InputEnum<StealthWarpDestination> Destination = new InputEnum<StealthWarpDestination>(StealthWarpDestination.Exit);
		public InputFloat Offset = new InputFloat(1.2f);

		protected override void Validate(DebugCommandValidation validation)
		{
			validation.Require(
				StealthStageDemoController.Instance != null,
				"stealth-not-running",
				"The Stealth Stage Demo is not running."
			);
			validation.Require(
				Offset.Value >= 0f && Offset.Value <= 4f,
				Offset,
				"warp-offset-out-of-range",
				"Offset must be between 0 and 4 metres."
			);
		}

		protected override string Execute()
		{
			return StealthStageDemoController.Instance.WarpPlayer(Destination.Value, Offset.Value);
		}
	}

	[DebugCommand(
		"Samples/Stealth/Commands/Set Alarm",
		Description = "Force guards into or out of their alert presentation.",
		Access = DebugCommandAccess.Lock
	)]
	public sealed class SetStealthAlarmCommand : DebugCommand<string>
	{
		public InputBool Enabled = new InputBool(true);

		protected override void Validate(DebugCommandValidation validation)
		{
			validation.Require(
				StealthStageDemoController.Instance != null,
				"stealth-not-running",
				"The Stealth Stage Demo is not running."
			);
		}

		protected override string Execute()
		{
			return StealthStageDemoController.Instance.SetAlarm(Enabled.Value);
		}
	}
}
