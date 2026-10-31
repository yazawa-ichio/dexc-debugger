namespace DexcBootCheck
{
	public enum BootCheckRenderer
	{
		IMGUI,
		UGUI,
		UIToolkit,
	}

	/// <summary>
	/// What the Boot Check scene runs with. This is the game's own configuration: the boot menu writes into it before the
	/// scene loads, and nothing here refers to Dexc, so the scene still builds and runs with the debugger stripped out.
	/// </summary>
	public static class BootCheckConfig
	{
		public static BootCheckRenderer Renderer = BootCheckRenderer.IMGUI;
		public static bool ShowFps = true;
		public static bool OpenDebugMenuOnStart;
		public static string PlayerName = "Tester";
		public static float TimeScale = 1f;
	}
}
