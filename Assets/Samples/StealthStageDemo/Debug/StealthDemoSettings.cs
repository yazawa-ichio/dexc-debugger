using Dexc;

namespace DexcDebugger.Samples.StealthStageDemo
{
	public sealed class StealthDemoSettings : DebugSettingSet<StealthDemoSettings>
	{
		[DebugSetting("Samples/Stealth/Settings/Player Speed", ApplyMode = DebugSettingApplyMode.Immediate)]
		[DebugRange(1, 12)]
		[DebugSlider]
		public float PlayerSpeed { get; private set; } = 5f;

		[DebugSetting("Samples/Stealth/Settings/Guard Speed", ApplyMode = DebugSettingApplyMode.Submit)]
		[DebugRange(0.5, 8)]
		[DebugSlider]
		public float GuardSpeed { get; private set; } = 2.2f;

		[DebugSetting("Samples/Stealth/Settings/Field Of View", ApplyMode = DebugSettingApplyMode.Submit)]
		[DebugRange(20, 160)]
		[DebugSlider]
		public float FieldOfView { get; private set; } = 70f;

		[DebugSetting("Samples/Stealth/Settings/Detection Distance", ApplyMode = DebugSettingApplyMode.Submit)]
		[DebugRange(2, 14)]
		[DebugSlider]
		public float DetectionDistance { get; private set; } = 6.5f;

		[DebugSetting("Samples/Stealth/Settings/Show World Visuals", ApplyMode = DebugSettingApplyMode.Immediate)]
		public bool ShowWorldVisuals { get; private set; } = true;

		[DebugPreset("stealth-easy", "Easy", "Faster player with shorter, narrower guard vision.")]
		public static StealthDemoSettings Easy =>
			new StealthDemoSettings
			{
				PlayerSpeed = 7f,
				GuardSpeed = 1.6f,
				FieldOfView = 52f,
				DetectionDistance = 4.5f,
				ShowWorldVisuals = true,
			};

		[DebugPreset("stealth-challenge", "Challenge", "Faster guards with a broad, long view cone.")]
		public static StealthDemoSettings Challenge =>
			new StealthDemoSettings
			{
				PlayerSpeed = 4.5f,
				GuardSpeed = 3.5f,
				FieldOfView = 105f,
				DetectionDistance = 9f,
				ShowWorldVisuals = true,
			};

		/// <summary>Hands the current values to the game. StealthDebugSystem calls it once when the demo starts.</summary>
		public static void ApplyTo(StealthStageDemoController game)
		{
			var settings = Current;
			game.ApplyTuning(
				settings.PlayerSpeed,
				settings.GuardSpeed,
				settings.FieldOfView,
				settings.DetectionDistance,
				settings.ShowWorldVisuals
			);
		}

		/// <summary>Every later write, reset or preset lands here, so the game only hears about values that changed.</summary>
		protected override void OnChanged(DebugSettingChanges changes)
		{
			if (StealthStageDemoController.Instance != null)
				ApplyTo(StealthStageDemoController.Instance);
		}
	}
}
