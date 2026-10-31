using UnityEngine;
using UnityEngine.InputSystem;

namespace DexcDebugger.Samples.StealthStageDemo
{
	public static class StealthStageInput
	{
		public static Vector2 ReadMove()
		{
			var move = Vector2.zero;
			var keyboard = Keyboard.current;
			if (keyboard != null)
			{
				if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
					move.x -= 1f;
				if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
					move.x += 1f;
				if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
					move.y -= 1f;
				if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
					move.y += 1f;
			}

			var gamepad = Gamepad.current;
			if (gamepad != null)
			{
				var stick = gamepad.leftStick.ReadValue();
				if (stick.sqrMagnitude > move.sqrMagnitude)
					move = stick;
			}

			return Vector2.ClampMagnitude(move, 1f);
		}

		public static bool WasRetryPressed()
		{
			return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame
				|| Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame;
		}

		public static bool WasDebugMenuPressed()
		{
			return Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame
				|| Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
		}
	}
}
