namespace DexcDebugger.Samples.StealthStageDemo
{
	public enum StealthStartStage
	{
		Stage1,
		Stage2,
		Stage3,
	}

	public enum StealthServerEnvironment
	{
		Local,
		Staging,
		Production,
	}

	/// <summary>
	/// What the demo runs with. This is the game's own configuration: a boot set fills it in before the stage loads, and
	/// nothing here refers to Dexc, so the demo still builds and plays with the debugger stripped out.
	/// </summary>
	public static class StealthDemoConfig
	{
		public static StealthServerEnvironment Environment = StealthServerEnvironment.Local;
		public static string Host = "localhost";
		public static StealthStartStage StartStage = StealthStartStage.Stage1;
		public static int GuardCount = 3;
		public static float TimeScale = 1f;

		/// <summary>Guards still raise the alert, but being seen no longer restarts the stage.</summary>
		public static bool Invincible;

		/// <summary>
		/// Set by the boot screen: on unless it starts the demo against production. Playing the scene without the boot
		/// screen chooses nothing, so the debugger stays off.
		/// </summary>
		public static bool DebuggerRequested;
	}
}
