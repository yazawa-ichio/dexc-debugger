using Dexc.Autopilot;
using DexcDebugger.Samples.StealthStageDemo;
using DexcDebugger.Samples.StealthStageDemo.Autopilot;
using UnityEngine;

namespace StealthBot
{
	public static class Entry
	{
		public static void Configure(AutopilotBuilder<StealthContext> builder)
		{
			var brain = new Brain();
			builder.SelectInterval = 0.25f;
			builder.Machine("Paused").When(brain.IsPaused).Step(brain.Hold);
			builder.Machine("Evade").When(brain.IsExposed).Step(brain.Evade);
			builder.Machine("Advance").When(context => true).Step(brain.Advance);
		}
	}

	/// <summary>The walkable floor of the current stage, read from the stage's own private layout.</summary>
	sealed class Grid
	{
		public const float Cell = 0.5f;
		public const int Width = 35;
		public const int Height = 27;
		const float MinX = -8.5f;
		const float MinZ = -6.5f;
		const float Clearance = 0.6f;

		readonly bool[] m_Blocked = new bool[Width * Height];
		bool[] m_Avoided = new bool[Width * Height];

		public Grid(StealthStageDemoController game)
		{
			var stage = StealthStageDemoController.s_Stages[game.m_CurrentStageIndex];
			for (var z = 0; z < Height; z++)
			{
				for (var x = 0; x < Width; x++)
				{
					var position = Position(x + z * Width);
					for (var index = 0; index < stage.Obstacles.Length; index++)
					{
						var center = stage.Obstacles[index];
						var size = stage.ObstacleSizes[index];
						if (
							Mathf.Abs(position.x - center.x) < size.x * 0.5f + Clearance
							&& Mathf.Abs(position.z - center.z) < size.z * 0.5f + Clearance
						)
							m_Blocked[x + z * Width] = true;
					}
				}
			}
		}

		/// <summary>Keeps paths this far from every guard's patrol, as long as a path remains.</summary>
		public void AvoidPatrols(StealthStageDemoController game, float radius)
		{
			m_Avoided = new bool[Width * Height];
			var reach = Mathf.CeilToInt(radius / Cell);
			var guards = game.m_Guards;
			for (var index = 0; index < guards.Count; index++)
			{
				var route = guards[index].PatrolRoute;
				for (var point = 0; point < route.Length; point++)
				{
					var from = route[point];
					var to = route[(point + 1) % route.Length];
					for (var step = 0; step <= 8; step++)
					{
						var x = from.x + (to.x - from.x) * step / 8f;
						var z = from.z + (to.z - from.z) * step / 8f;
						var center = CellAt(x, z);
						var cx = center % Width;
						var cz = center / Width;
						for (var dz = -reach; dz <= reach; dz++)
						{
							for (var dx = -reach; dx <= reach; dx++)
							{
								var nx = cx + dx;
								var nz = cz + dz;
								if (nx >= 0 && nz >= 0 && nx < Width && nz < Height && dx * dx + dz * dz <= reach * reach)
									m_Avoided[nx + nz * Width] = true;
							}
						}
					}
				}
			}
		}

		public Vector3 Position(int cell) =>
			new Vector3(MinX + cell % Width * Cell, 1f, MinZ + cell / Width * Cell);

		public int CellAt(float worldX, float worldZ)
		{
			var x = Mathf.Clamp(Mathf.RoundToInt((worldX - MinX) / Cell), 0, Width - 1);
			var z = Mathf.Clamp(Mathf.RoundToInt((worldZ - MinZ) / Cell), 0, Height - 1);
			return x + z * Width;
		}

		/// <summary>For every cell, the next cell towards <paramref name="goal"/>.</summary>
		public int[] Flow(int goal)
		{
			var next = new int[Width * Height];
			for (var index = 0; index < next.Length; index++)
				next[index] = -1;
			var queue = new int[Width * Height];
			var head = 0;
			var tail = 0;
			queue[tail++] = goal;
			next[goal] = goal;
			while (head < tail)
			{
				var cell = queue[head++];
				var x = cell % Width;
				var z = cell / Width;
				for (var side = 0; side < 4; side++)
				{
					var nx = x + (side == 0 ? 1 : side == 1 ? -1 : 0);
					var nz = z + (side == 2 ? 1 : side == 3 ? -1 : 0);
					if (nx < 0 || nz < 0 || nx >= Width || nz >= Height)
						continue;
					var other = nx + nz * Width;
					if (m_Blocked[other] || m_Avoided[other] || next[other] >= 0)
						continue;
					next[other] = cell;
					queue[tail++] = other;
				}
			}

			return next;
		}
	}

	sealed class Brain
	{
		const float Margin = 1.5f;
		const float AngleMargin = 20f;

		int m_Stage = -1;
		Grid m_Grid;
		int[] m_Flow;
		int[] m_Home;
		Vector3 m_Start;

		public bool IsPaused(StealthContext context) => context.Game.State != StealthStageState.Running;

		public void Hold(StealthContext context) => context.Stop();

		public bool IsExposed(StealthContext context)
		{
			var position = context.Game.PlayerPosition;
			return Seen(context.Game, position.x, position.z);
		}

		public void Evade(StealthContext context)
		{
			Prepare(context.Game);
			Walk(context, m_Home, true);
		}

		public void Advance(StealthContext context)
		{
			Prepare(context.Game);
			Walk(context, m_Flow, false);
		}

		void Prepare(StealthStageDemoController game)
		{
			if (game.m_CurrentStageIndex == m_Stage && m_Grid != null)
				return;
			m_Stage = game.m_CurrentStageIndex;
			m_Grid = new Grid(game);
			var exit = game.ExitPosition;
			m_Start = StealthStageDemoController.s_Stages[m_Stage].PlayerStart;
			var goal = m_Grid.CellAt(exit.x, exit.z);
			var start = m_Grid.CellAt(m_Start.x, m_Start.z);
			for (var radius = 4f; radius >= 0f; radius -= 1f)
			{
				m_Grid.AvoidPatrols(game, radius);
				m_Flow = m_Grid.Flow(goal);
				if (m_Flow[start] >= 0)
					break;
			}

			m_Home = m_Grid.Flow(start);
		}

		void Walk(StealthContext context, int[] flow, bool retreating)
		{
			var game = context.Game;
			var position = game.PlayerPosition;
			var cell = m_Grid.CellAt(position.x, position.z);
			var next = flow[cell];
			if (next < 0)
				next = cell;
			var ahead = flow[next] >= 0 ? flow[next] : next;
			var target = m_Grid.Position(ahead);
			if (!retreating && Seen(game, target.x, target.z))
			{
				context.Stop();
				return;
			}

			var offset = target - position;
			offset.y = 0f;
			if (offset.sqrMagnitude < 0.0001f)
			{
				context.Stop();
				return;
			}

			offset.Normalize();
			context.Move(new Vector2(offset.x, offset.z));
		}

		/// <summary>
		/// Physics.Linecast is not in the build: the game never calls it. The stage's boxes are all there is to hit.
		/// Coordinates travel as floats because a Vector3 parameter of an interpreted method arrives as null.
		/// </summary>
		static bool LineIsClear(StealthStageDemoController game, float fromX, float fromZ, float toX, float toZ)
		{
			var stage = StealthStageDemoController.s_Stages[game.m_CurrentStageIndex];
			var dx = toX - fromX;
			var dz = toZ - fromZ;
			var steps = Mathf.CeilToInt(Mathf.Sqrt(dx * dx + dz * dz) / 0.25f);
			for (var step = 1; step < steps; step++)
			{
				var x = fromX + dx * step / steps;
				var z = fromZ + dz * step / steps;
				for (var index = 0; index < stage.Obstacles.Length; index++)
				{
					var center = stage.Obstacles[index];
					var size = stage.ObstacleSizes[index];
					if (Mathf.Abs(x - center.x) < size.x * 0.5f && Mathf.Abs(z - center.z) < size.z * 0.5f)
						return false;
				}
			}

			return true;
		}

		/// <summary>The game's own test, widened so the bot keeps some distance from the edge of a guard's view.</summary>
		static bool Seen(StealthStageDemoController game, float x, float z)
		{
			var guards = game.m_Guards;
			for (var index = 0; index < guards.Count; index++)
			{
				var guard = guards[index];
				var origin = guard.transform.position;
				var dx = x - origin.x;
				var dz = z - origin.z;
				var reach = game.DetectionDistance + Margin;
				if (dx * dx + dz * dz > reach * reach)
					continue;
				if (Vector3.Angle(guard.transform.forward, new Vector3(dx, 0f, dz)) > game.FieldOfView * 0.5f + AngleMargin)
					continue;
				if (LineIsClear(game, origin.x, origin.z, x, z))
					return true;
			}

			return false;
		}
	}
}
