using System;
using System.Diagnostics;
using Dexc;
using UnityEngine;

namespace DexcDebugger.Samples.StealthStageDemo
{
	public sealed class StealthGuardController : MonoBehaviour
	{
		const string InspectorPath = "Samples/Stealth/Inspector/Guards";

		StealthStageDemoController m_Game;
		Vector3[] m_PatrolRoute;
		Renderer m_Renderer;

		[DebugField(Access = DebugFieldAccess.ReadOnly)]
		int m_PatrolIndex;

		[DebugField(Access = DebugFieldAccess.ReadOnly)]
		bool m_IsAlert;

		[DebugField(Access = DebugFieldAccess.ReadOnly)]
		bool m_CanSeePlayer;

		DebugDisplaySubject m_Subject;
		string m_ViewPath;
		string m_RoutePath;
		string m_RelationPath;

		public int PatrolIndex => m_PatrolIndex;
		public bool IsAlert => m_IsAlert;
		public Vector3[] PatrolRoute => m_PatrolRoute;

		public void Initialize(StealthStageDemoController game, Vector3[] patrolRoute)
		{
			m_Game = game;
			m_PatrolRoute = patrolRoute;
			m_PatrolIndex = m_PatrolRoute.Length > 1 ? 1 : 0;
			m_Renderer = GetComponent<Renderer>();
			FaceNextWaypoint();
		}

		void OnEnable()
		{
			DebugInspector.Register(InspectorPath, this, name);
			m_Subject = DebugDisplay.CreateSubject("Samples/Stealth/Guard", this);
			var prefix = "Samples/Stealth/World/" + name + "/";
			m_ViewPath = prefix + "View";
			m_RoutePath = prefix + "Patrol Route";
			m_RelationPath = prefix + "Player Relation";
		}

		void OnDisable()
		{
			DebugInspector.Unregister(InspectorPath, this);
			m_Subject.Dispose();
		}

		public bool EvaluateDetectionNow()
		{
			var playerPosition = m_Game.PlayerTransform.position;
			var toPlayer = playerPosition - transform.position;
			toPlayer.y = 0f;
			if (toPlayer.sqrMagnitude > m_Game.DetectionDistance * m_Game.DetectionDistance)
				return false;
			if (toPlayer.sqrMagnitude < 0.0001f)
				return true;
			if (Vector3.Angle(transform.forward, toPlayer) > m_Game.FieldOfView * 0.5f)
				return false;

			var origin = transform.position + Vector3.up * 0.45f;
			var target = playerPosition + Vector3.up * 0.45f;
			var direction = target - origin;
			// The ray starts inside this guard's own collider, which a raycast does not report, so the first hit is
			// whatever stands between the guard and the player - or the player.
			if (!Physics.Raycast(origin, direction.normalized, out var hit, direction.magnitude, ~0, QueryTriggerInteraction.Ignore))
				return true;
			return hit.transform == m_Game.PlayerTransform;
		}

		public void SetAlert(bool value)
		{
			m_IsAlert = value;
			m_Renderer.sharedMaterial = value ? m_Game.AlertMaterial : m_Game.GuardMaterial;
		}

		void Update()
		{
			if (m_Game.State != StealthStageState.Running)
				return;
			SetAlert(m_Game.IsAlarmForced);
			MovePatrol(Time.deltaTime);
			m_CanSeePlayer = EvaluateDetectionNow();
			if (m_CanSeePlayer)
			{
				SetAlert(true);
				m_Game.NotifyDetected(this);
			}
		}

		void LateUpdate()
		{
			DrawDebug();
		}

		/// <summary>
		/// Building the route below is ordinary code that the compiler would keep, so the whole drawing sits in one method
		/// that a build without DEXC_DEBUGGER drops along with its call.
		/// </summary>
		[Conditional("DEXC_DEBUGGER")]
		void DrawDebug()
		{
			m_Subject.SetMetadata("PatrolIndex", m_PatrolIndex);
			m_Subject.SetMetadata("Alert", m_IsAlert);
			if (!m_Game.ShowDebugWorldVisuals)
				return;
			var color = m_IsAlert ? Color.red : new Color(1f, 0.72f, 0.12f);
			// The area the guard can see: the same distance and angle the detection check uses.
			DebugDisplay.DrawWireSector(
				DebugDrawOptions.Frame(m_ViewPath, color),
				transform.position + Vector3.up * 0.3f,
				Vector3.up,
				transform.forward,
				m_Game.FieldOfView,
				m_Game.DetectionDistance
			);
			// The route closed back on its first point, built on the stack.
			Span<Vector3> route = stackalloc Vector3[m_PatrolRoute.Length + 1];
			for (var index = 0; index < m_PatrolRoute.Length; index++)
				route[index] = m_PatrolRoute[index] + Vector3.up * 0.25f;
			route[m_PatrolRoute.Length] = route[0];
			DebugDisplay.DrawPolyLine(DebugDrawOptions.Frame(m_RoutePath, Color.yellow), route);
			// Drawn from this guard's Subject, so the line starts at the guard instead of the world origin.
			m_Subject.DrawRelation(DebugDrawOptions.Frame(m_RelationPath, color), m_Game.PlayerSubject);
		}

		void MovePatrol(float deltaTime)
		{
			var target = m_PatrolRoute[m_PatrolIndex];
			var offset = target - transform.position;
			offset.y = 0f;
			if (offset.sqrMagnitude < 0.04f)
			{
				m_PatrolIndex = (m_PatrolIndex + 1) % m_PatrolRoute.Length;
				FaceNextWaypoint();
				return;
			}

			var direction = offset.normalized;
			transform.position += direction * Mathf.Min(offset.magnitude, m_Game.GuardMoveSpeed * deltaTime);
			transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
		}

		void FaceNextWaypoint()
		{
			var direction = m_PatrolRoute[m_PatrolIndex] - transform.position;
			direction.y = 0f;
			if (direction.sqrMagnitude > 0.0001f)
				transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
		}
	}
}
