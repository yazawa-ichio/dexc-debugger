using System;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Dexc.Telemetry
{
	/// <summary>
	/// Puts the frame of telemetry at the end of Initialization. By then the frame before has ended: what the game recorded
	/// in it and what Unity measured of it are both complete. The work itself falls in the profiler frame of the next one.
	/// </summary>
	internal static class TelemetryPlayerLoop
	{
		/// <summary>The type that marks the system of this package in the player loop.</summary>
		internal sealed class Frame
		{
		}

		internal static void Install(PlayerLoopSystem.UpdateFunction update)
		{
			PlayerLoop.SetPlayerLoop(With(PlayerLoop.GetCurrentPlayerLoop(), update));
		}

		internal static void Uninstall()
		{
			PlayerLoop.SetPlayerLoop(Without(PlayerLoop.GetCurrentPlayerLoop()));
		}

		/// <summary>The loop with the system of this package at the end of Initialization, once.</summary>
		/// <exception cref="InvalidOperationException">The loop has no Initialization.</exception>
		internal static PlayerLoopSystem With(PlayerLoopSystem loop, PlayerLoopSystem.UpdateFunction update)
		{
			loop = Without(loop);
			var phases = loop.subSystemList;
			for (var i = 0; i < phases.Length; i++)
			{
				if (phases[i].type != typeof(Initialization))
				{
					continue;
				}
				var systems = phases[i].subSystemList ?? Array.Empty<PlayerLoopSystem>();
				var extended = new PlayerLoopSystem[systems.Length + 1];
				Array.Copy(systems, extended, systems.Length);
				extended[systems.Length] = new PlayerLoopSystem { type = typeof(Frame), updateDelegate = update };
				phases[i].subSystemList = extended;
				return loop;
			}
			throw new InvalidOperationException("The player loop has no Initialization phase to run telemetry in.");
		}

		/// <summary>The loop without the system of this package.</summary>
		internal static PlayerLoopSystem Without(PlayerLoopSystem loop)
		{
			var phases = loop.subSystemList;
			if (phases == null)
			{
				return loop;
			}
			for (var i = 0; i < phases.Length; i++)
			{
				var systems = phases[i].subSystemList;
				if (systems != null && Array.Exists(systems, system => system.type == typeof(Frame)))
				{
					phases[i].subSystemList = Array.FindAll(systems, system => system.type != typeof(Frame));
				}
			}
			return loop;
		}
	}
}
