using Dexc;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DexcDebugger.Samples.StealthStageDemo
{
	public sealed class StealthDebugMenuInputProvider : DebugMenuInputProvider
	{
		readonly StealthStageDemoController m_Game;

		public StealthDebugMenuInputProvider(StealthStageDemoController game)
		{
			m_Game = game;
		}

		public override DebugMenuInputFrame ReadInput()
		{
			var gamepad = Gamepad.current;
			if (gamepad == null)
				return new DebugMenuInputFrame(Vector2.zero, false, false);
			var navigation = gamepad.dpad.ReadValue();
			var stick = gamepad.leftStick.ReadValue();
			if (stick.sqrMagnitude > navigation.sqrMagnitude)
				navigation = stick;
			return new DebugMenuInputFrame(navigation, gamepad.buttonSouth.wasPressedThisFrame, gamepad.buttonEast.wasPressedThisFrame);
		}

		public override void OnInputStateChanged(DebugMenuInputState state)
		{
			m_Game.SetGameplayInputEnabled(state == DebugMenuInputState.Closed);
		}
	}
}
