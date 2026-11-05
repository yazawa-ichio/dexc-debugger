using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Scripting;

namespace DexcDebugger.Samples.StealthStageDemo.Autopilot
{
	/// <summary>
	/// The hands of the autopilot: a gamepad that exists only in the Input System.
	///
	/// It is a Gamepad so the game needs no change at all. <see cref="StealthStageInput"/> reads Gamepad.current, and the
	/// state queued here arrives through the same events a real pad's would, so everything a player's input passes
	/// through runs for the autopilot too.
	/// </summary>
	[Preserve]
	[InputControlLayout(stateType = typeof(GamepadState), displayName = "Stealth Autopilot Gamepad")]
	public sealed class StealthAutopilotGamepad : Gamepad
	{
		public static StealthAutopilotGamepad Create()
		{
			// By default a Player that loses focus drops device input, which would stop a run the moment someone clicks away.
			InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
			InputSystem.RegisterLayout<StealthAutopilotGamepad>();
			var device = InputSystem.AddDevice<StealthAutopilotGamepad>();
			device.MakeCurrent();
			return device;
		}

		public void Write(Vector2 move, bool retry)
		{
			var state = new GamepadState { leftStick = move };
			if (retry)
				state = state.WithButton(GamepadButton.Select);
			InputSystem.QueueStateEvent(this, state);
		}
	}
}
