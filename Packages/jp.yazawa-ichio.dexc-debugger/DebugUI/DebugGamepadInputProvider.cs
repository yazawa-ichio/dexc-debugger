namespace Dexc
{
	/// <summary>
	/// Ready-made <see cref="DebugMenuInputProvider"/> that reads the current Input System gamepad
	/// (d-pad for navigation, South/Start for submit, East for cancel) without a static
	/// package reference. Returns an empty frame when the Input System package or a gamepad is absent.
	/// Pass an instance to <see cref="DebugMenu.SetInputProvider"/>.
	/// </summary>
	public class DebugGamepadInputProvider : DebugMenuInputProvider
	{
		public override DebugMenuInputFrame ReadInput()
		{
#if DEXC_DEBUGGER
			return DexcBootMenuInput.ReadGamepad();
#else
			return default(DebugMenuInputFrame);
#endif
		}
	}
}
