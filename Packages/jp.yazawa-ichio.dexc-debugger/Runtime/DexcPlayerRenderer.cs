#if DEXC_HAS_URP
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Dexc
{
	/// <summary>Enqueues the Dexc World pass into each URP Player camera.</summary>
	internal static class DexcPlayerRenderer
	{
		/// <summary>State of this class. Owned by <see cref="DexcUIRoot"/>; nothing here survives a root reset.</summary>
		internal sealed class State
		{
			internal DexcPlayerRenderPass Pass;
			internal bool Registered;
		}

		internal static State Data => DexcRoot.Current.PlayerRenderer;
		private static DexcPlayerRenderPass Pass
		{
			get => Data.Pass;
			set => Data.Pass = value;
		}
		private static bool Registered
		{
			get => Data.Registered;
			set => Data.Registered = value;
		}

		internal static void Shutdown()
		{
			Shutdown(false);
		}

		private static void Shutdown(bool immediate)
		{
#if DEXC_DEBUGGER
			RenderPipelineManager.beginCameraRendering -= EnqueuePass;
			DexcDebugger.SessionInvalidated -= ReleasePass;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ShutdownForEditor;
#endif
#endif
			Registered = false;
			ReleasePass(immediate);
		}

		private static void ReleasePass()
		{
			ReleasePass(false);
		}

		private static void ReleasePass(bool immediate)
		{
			var pass = Pass;
			Pass = null;
			pass?.ReleaseResources(immediate);
		}

#if DEXC_DEBUGGER && UNITY_EDITOR
		private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
		{
			if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
				ShutdownForEditor();
		}

		private static void ShutdownForEditor()
		{
			// Release before the domain loses its owner; a later frame is not guaranteed.
			Shutdown(true);
		}
#endif

		internal static void EnsureInitialized()
		{
#if DEXC_DEBUGGER
			if (Registered)
				return;
			Registered = true;
			RenderPipelineManager.beginCameraRendering += EnqueuePass;
			DexcDebugger.SessionInvalidated += ReleasePass;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ShutdownForEditor;
#endif
#endif
		}

#if UNITY_INCLUDE_TESTS
		internal static bool IsRegisteredForTests
		{
			get { return Registered; }
		}

		internal static IReadOnlyList<DebugDisplayPrimitiveKind> GetWorldPrimitiveKindsForTests()
		{
			if (!DebugDisplay.TryGetCompletedFrame(out var frame))
				return Array.Empty<DebugDisplayPrimitiveKind>();
			return frame.Globals.Concat(frame.Subjects.SelectMany(subject => subject.Visuals)).Select(visual => visual.Kind).ToList();
		}
#endif

		private static void EnqueuePass(ScriptableRenderContext context, Camera camera)
		{
			if (!IsPlayerCamera(camera) || !DexcDebugger.IsEnabled || !DexcDebugger.IsInitialized)
				return;
			var cameraData = camera.GetUniversalAdditionalCameraData();
			if (cameraData == null || cameraData.scriptableRenderer == null)
				return;
			if (Pass == null)
				Pass = new DexcPlayerRenderPass();
			cameraData.scriptableRenderer.EnqueuePass(Pass);
		}

		private static bool IsPlayerCamera(Camera camera)
		{
			return camera != null && IsPlayerCameraType(camera.cameraType);
		}

		private static bool IsPlayerCameraType(CameraType cameraType)
		{
			return cameraType == CameraType.Game;
		}

#if UNITY_INCLUDE_TESTS
		internal static bool IsPlayerCameraTypeForTests(CameraType cameraType)
		{
			return IsPlayerCameraType(cameraType);
		}
#endif
	}

	internal sealed class DexcPlayerRenderPass : ScriptableRenderPass
	{
		private const float LineWidth = 0.018f;
		private const int CircleSegments = 32;
		private readonly List<Vector3> m_Vertices = new List<Vector3>(512);
		private readonly List<Color32> m_Colors = new List<Color32>(512);
		private readonly List<int> m_Indices = new List<int>(768);
		private readonly List<int> m_FillIndices = new List<int>(768);

		// Read by the renderer tests as the proof that the pass built geometry.
		internal Mesh m_Mesh;
		private Material m_Material;
		private long m_MaterialGeneration = -1;
		private bool m_IsReleased;

		internal DexcPlayerRenderPass()
		{
			renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
		}

		internal void ReleaseResources(bool immediate)
		{
			if (m_IsReleased)
			{
				return;
			}
			m_IsReleased = true;
			var mesh = m_Mesh;
			var material = m_Material;
			m_Mesh = null;
			m_Material = null;
			m_MaterialGeneration = -1;
			m_Vertices.Clear();
			m_Colors.Clear();
			m_Indices.Clear();
			m_FillIndices.Clear();
			DestroyResource(mesh, immediate);
			DestroyResource(material, immediate);
		}

		private static void DestroyResource(UnityEngine.Object resource, bool immediate)
		{
			if (immediate)
			{
				UnityEngine.Object.DestroyImmediate(resource);
				return;
			}
			CoreUtils.Destroy(resource);
		}

		// URP 17.6 took this method off ScriptableRenderPass along with Compatibility Mode, so there is nothing left to
		// override. The condition is the URP version and not the Unity one because the method belongs to URP; a project
		// that pins a different URP than its Editor ships with gets the right answer either way. If the define is ever
		// absent the override stays, which is what every URP up to 17.5 wants.
#if !DEXC_URP_COMPATIBILITY_REMOVED
		[Obsolete("URP compatibility render pass API is retained for non-RenderGraph renderers.", false)]
		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			if (m_IsReleased)
				return;
			if (!DebugDisplay.TryGetCompletedFrame(out var frame))
				return;
			if (GetMaterial() == null)
				return;
			var camera = renderingData.cameraData.camera;
			if (camera == null)
				return;
			BuildMesh(frame, camera);
			if (m_Mesh == null || m_Mesh.vertexCount == 0)
				return;

			var command = CommandBufferPool.Get("Dexc Player World Visual");
			command.DrawMesh(m_Mesh, Matrix4x4.identity, m_Material, 0, 0);
			command.DrawMesh(m_Mesh, Matrix4x4.identity, m_Material, 1, 0);
			context.ExecuteCommandBuffer(command);
			CommandBufferPool.Release(command);
		}
#endif

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			if (m_IsReleased)
				return;
			if (!DebugDisplay.TryGetCompletedFrame(out var frame))
				return;
			if (GetMaterial() == null)
				return;
			var cameraData = frameData.Get<UniversalCameraData>();
			BuildMesh(frame, cameraData.camera);
			if (m_Mesh == null || m_Mesh.vertexCount == 0)
				return;
			var resources = frameData.Get<UniversalResourceData>();
			using (var builder = renderGraph.AddRasterRenderPass<PlayerRenderGraphData>("Dexc Player World Visual", out var passData))
			{
				passData.Mesh = m_Mesh;
				passData.Material = m_Material;
				builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
				if (resources.activeDepthTexture.IsValid())
					builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
				builder.SetRenderFunc(
					(PlayerRenderGraphData data, RasterGraphContext context) =>
					{
						context.cmd.DrawMesh(data.Mesh, Matrix4x4.identity, data.Material, 0, 0);
						context.cmd.DrawMesh(data.Mesh, Matrix4x4.identity, data.Material, 1, 0);
					}
				);
			}
		}

		private sealed class PlayerRenderGraphData
		{
			internal Mesh Mesh;
			internal Material Material;
		}

		private Material GetMaterial()
		{
			if (m_IsReleased)
				return null;
			var session = DexcDebugger.CurrentSession;
			if (session == null || session.Options.RuntimeUISkin == null)
			{
				if (m_Material != null)
					CoreUtils.Destroy(m_Material);
				m_Material = null;
				m_MaterialGeneration = -1;
				return null;
			}
			if (m_Material != null && m_MaterialGeneration == session.Generation)
				return m_Material;
			if (m_Material != null)
			{
				CoreUtils.Destroy(m_Material);
				m_Material = null;
			}
			m_MaterialGeneration = session.Generation;
			var shader = session.Options.RuntimeUISkin.WorldVisualShader;
			m_Material = shader == null ? null : CoreUtils.CreateEngineMaterial(shader);
			return m_Material;
		}

		private void BuildMesh(DebugDisplayFrameSnapshot frame, Camera camera)
		{
			if (m_IsReleased)
				return;
			m_Vertices.Clear();
			m_Colors.Clear();
			m_Indices.Clear();
			m_FillIndices.Clear();
			var subjects = frame.Subjects.ToDictionary(subject => subject.Id, subject => subject);
			var visibility = DexcDisplayVisibility.Capture();
			foreach (var visual in frame.Globals)
			{
				if (visibility.IsVisible(visual, false))
					AddVisual(visual, null, subjects, camera);
			}
			foreach (var subject in frame.Subjects)
			{
				foreach (var visual in subject.Visuals)
				{
					if (visibility.IsVisible(visual, true))
						AddVisual(visual, subject, subjects, camera);
				}
			}
			if (m_Mesh == null)
			{
				m_Mesh = new Mesh { name = "Dexc Player World Visual Mesh" };
				m_Mesh.MarkDynamic();
			}
			m_Mesh.Clear();
			if (m_Vertices.Count == 0)
				return;
			m_Mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			m_Mesh.SetVertices(m_Vertices);
			m_Mesh.SetColors(m_Colors);
			m_Mesh.subMeshCount = 2;
			m_Mesh.SetIndices(m_FillIndices, MeshTopology.Triangles, 0, false);
			m_Mesh.SetIndices(m_Indices, MeshTopology.Triangles, 1, false);
			m_Mesh.RecalculateBounds();
		}

		private void AddVisual(
			DebugDisplayVisualSnapshot visual,
			DebugDisplaySubjectSnapshot source,
			IReadOnlyDictionary<DebugDisplaySubjectId, DebugDisplaySubjectSnapshot> subjects,
			Camera camera
		)
		{
			var color = (Color32)visual.Color;
			var center = WorldPoint(visual, visual.Center);
			if (
				visual.Kind == DebugDisplayPrimitiveKind.Sphere
				|| visual.Kind == DebugDisplayPrimitiveKind.Cube
				|| visual.Kind == DebugDisplayPrimitiveKind.Disc
				|| visual.Kind == DebugDisplayPrimitiveKind.Capsule
			)
			{
				AddShape(visual, camera);
				return;
			}
			switch (visual.Kind)
			{
				case DebugDisplayPrimitiveKind.Line:
				case DebugDisplayPrimitiveKind.Ray:
					AddSegment(WorldPoint(visual, visual.Start), WorldPoint(visual, visual.End), color, camera);
					break;
				case DebugDisplayPrimitiveKind.PolyLine:
					for (var i = 1; i < visual.Points.Count; i++)
						AddSegment(WorldPoint(visual, visual.Points[i - 1]), WorldPoint(visual, visual.Points[i]), color, camera);
					break;
				case DebugDisplayPrimitiveKind.WireSphere:
					AddCircle(center, WorldScale(visual) * visual.Radius, Vector3.right, Vector3.up, color, camera);
					AddCircle(center, WorldScale(visual) * visual.Radius, Vector3.right, Vector3.forward, color, camera);
					AddCircle(center, WorldScale(visual) * visual.Radius, Vector3.up, Vector3.forward, color, camera);
					break;
				case DebugDisplayPrimitiveKind.WireCube:
					AddWireCube(center, WorldRotation(visual), WorldSize(visual), color, camera);
					break;
				case DebugDisplayPrimitiveKind.WireDisc:
					AddWireDisc(center, WorldDirection(visual, visual.Normal), WorldScale(visual) * visual.Radius, color, camera);
					break;
				case DebugDisplayPrimitiveKind.Relation:
					AddRelation(visual, source, subjects, color, camera);
					break;
			}
		}

		private void AddShape(DebugDisplayVisualSnapshot visual, Camera camera)
		{
			var wire = visual.DrawStyle != DebugDisplayDrawStyle.Solid;
			var solid = visual.DrawStyle != DebugDisplayDrawStyle.Wire;
			var center = WorldPoint(visual, visual.Center);
			var radius = WorldScale(visual) * visual.Radius;
			var color = (Color32)visual.Color;
			var fill = (Color32)visual.FillColor;
			switch (visual.Kind)
			{
				case DebugDisplayPrimitiveKind.Sphere:
					if (solid)
						AddCapsuleSurface(center, Quaternion.identity, radius, 0f, fill);
					if (wire)
					{
						AddCircle(center, radius, Vector3.right, Vector3.up, color, camera);
						AddCircle(center, radius, Vector3.right, Vector3.forward, color, camera);
						AddCircle(center, radius, Vector3.up, Vector3.forward, color, camera);
					}
					break;
				case DebugDisplayPrimitiveKind.Cube:
					if (solid)
						AddSolidCube(center, WorldRotation(visual), WorldSize(visual), fill);
					if (wire)
						AddWireCube(center, WorldRotation(visual), WorldSize(visual), color, camera);
					break;
				case DebugDisplayPrimitiveKind.Disc:
					var normal = WorldDirection(visual, visual.Normal).normalized;
					var tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
					var bitangent = Vector3.Cross(normal, tangent).normalized;
					if (solid)
					{
						for (var i = 0; i < CircleSegments; i++)
							AddTriangle(
								center,
								CirclePoint(center, radius, tangent, bitangent, i),
								CirclePoint(center, radius, tangent, bitangent, i + 1),
								fill
							);
					}
					if (wire)
						AddCircle(center, radius, tangent, bitangent, color, camera);
					break;
				case DebugDisplayPrimitiveKind.Capsule:
					var localRotation = visual.Rotation.normalized;
					var axis = ShapeVector(visual, localRotation * Vector3.up);
					var radialScale = Mathf.Max(
						ShapeVector(visual, localRotation * Vector3.right).magnitude,
						ShapeVector(visual, localRotation * Vector3.forward).magnitude
					);
					radius = visual.Radius * radialScale;
					var halfSegment = Mathf.Max(0f, Mathf.Max(visual.Height, visual.Radius * 2f) * axis.magnitude * 0.5f - radius);
					var rotation = axis.sqrMagnitude > 0f ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : Quaternion.identity;
					if (solid)
						AddCapsuleSurface(center, rotation, radius, halfSegment, fill);
					if (wire)
						AddWireCapsule(center, rotation, radius, halfSegment, color, camera);
					break;
			}
		}

		private static Vector3 ShapeVector(DebugDisplayVisualSnapshot visual, Vector3 direction)
		{
			return visual.Space == DebugDisplaySpace.Local && visual.Basis != null
				? visual.Basis.Rotation * Vector3.Scale(visual.Basis.Scale, direction)
				: direction;
		}

		private static Vector3 CirclePoint(Vector3 center, float radius, Vector3 axisA, Vector3 axisB, int index)
		{
			var angle = index * Mathf.PI * 2f / CircleSegments;
			return center + radius * (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle));
		}

		private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color32 color)
		{
			var first = m_Vertices.Count;
			m_Vertices.Add(a);
			m_Vertices.Add(b);
			m_Vertices.Add(c);
			m_Colors.Add(color);
			m_Colors.Add(color);
			m_Colors.Add(color);
			m_FillIndices.Add(first);
			m_FillIndices.Add(first + 1);
			m_FillIndices.Add(first + 2);
		}

		private void AddSolidCube(Vector3 center, Quaternion rotation, Vector3 size, Color32 color)
		{
			var corners = new Vector3[8];
			for (var i = 0; i < 8; i++)
				corners[i] =
					center
					+ rotation
						* new Vector3(
							(i & 1) == 0 ? -size.x * 0.5f : size.x * 0.5f,
							(i & 2) == 0 ? -size.y * 0.5f : size.y * 0.5f,
							(i & 4) == 0 ? -size.z * 0.5f : size.z * 0.5f
						);
			var faces = new[] { 0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3 };
			for (var i = 0; i < faces.Length; i += 4)
			{
				AddTriangle(corners[faces[i]], corners[faces[i + 1]], corners[faces[i + 2]], color);
				AddTriangle(corners[faces[i]], corners[faces[i + 2]], corners[faces[i + 3]], color);
			}
		}

		private const int HemisphereSegments = 8;

		private static Vector3 CapsulePoint(Vector3 center, Quaternion rotation, float radius, float halfSegment, int ring, int segment)
		{
			var upper = ring > HemisphereSegments;
			var latitude = upper
				? (ring - HemisphereSegments - 1) * Mathf.PI * 0.5f / HemisphereSegments
				: -Mathf.PI * 0.5f + ring * Mathf.PI * 0.5f / HemisphereSegments;
			var angle = segment * Mathf.PI * 2f / CircleSegments;
			var radial = radius * Mathf.Cos(latitude);
			return center
				+ rotation
					* new Vector3(
						radial * Mathf.Cos(angle),
						(upper ? halfSegment : -halfSegment) + radius * Mathf.Sin(latitude),
						radial * Mathf.Sin(angle)
					);
		}

		private void AddCapsuleSurface(Vector3 center, Quaternion rotation, float radius, float halfSegment, Color32 color)
		{
			for (var ring = 0; ring < HemisphereSegments * 2 + 1; ring++)
			{
				for (var segment = 0; segment < CircleSegments; segment++)
				{
					var a = CapsulePoint(center, rotation, radius, halfSegment, ring, segment);
					var b = CapsulePoint(center, rotation, radius, halfSegment, ring, segment + 1);
					var c = CapsulePoint(center, rotation, radius, halfSegment, ring + 1, segment + 1);
					var d = CapsulePoint(center, rotation, radius, halfSegment, ring + 1, segment);
					AddTriangle(a, b, c, color);
					AddTriangle(a, c, d, color);
				}
			}
		}

		private void AddWireCapsule(Vector3 center, Quaternion rotation, float radius, float halfSegment, Color32 color, Camera camera)
		{
			var up = rotation * Vector3.up;
			AddCircle(center - up * halfSegment, radius, rotation * Vector3.right, rotation * Vector3.forward, color, camera);
			if (halfSegment > 0f)
				AddCircle(center + up * halfSegment, radius, rotation * Vector3.right, rotation * Vector3.forward, color, camera);
			for (var side = 0; side < 4; side++)
			{
				var direction = rotation * Quaternion.AngleAxis(side * 90f, Vector3.up) * Vector3.right;
				AddSegment(center - up * halfSegment + direction * radius, center + up * halfSegment + direction * radius, color, camera);
				for (var i = 0; i < HemisphereSegments; i++)
				{
					var a = i * Mathf.PI * 0.5f / HemisphereSegments;
					var b = (i + 1) * Mathf.PI * 0.5f / HemisphereSegments;
					for (var sign = -1; sign <= 1; sign += 2)
					{
						AddSegment(
							center + up * sign * (halfSegment + radius * Mathf.Sin(a)) + direction * radius * Mathf.Cos(a),
							center + up * sign * (halfSegment + radius * Mathf.Sin(b)) + direction * radius * Mathf.Cos(b),
							color,
							camera
						);
					}
				}
			}
		}

		private void AddRelation(
			DebugDisplayVisualSnapshot visual,
			DebugDisplaySubjectSnapshot source,
			IReadOnlyDictionary<DebugDisplaySubjectId, DebugDisplaySubjectSnapshot> subjects,
			Color32 color,
			Camera camera
		)
		{
			if (visual.TargetMissing)
				return;
			var sourcePosition = source != null && source.Owner != null ? source.Owner.Position : WorldPoint(visual, visual.Center);
			if (subjects.TryGetValue(visual.Target, out var target) && target.Owner != null)
			{
				AddSegment(sourcePosition, target.Owner.Position, color, camera);
				return;
			}
			if (visual.TargetOwner != null)
				AddSegment(sourcePosition, visual.TargetOwner.Position, color, camera);
		}

		private void AddWireCube(Vector3 center, Quaternion rotation, Vector3 size, Color32 color, Camera camera)
		{
			var half = size * 0.5f;
			var corners = new Vector3[8];
			for (var i = 0; i < corners.Length; i++)
			{
				var local = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
				corners[i] = center + rotation * local;
			}
			var edges = new[] { 0, 1, 1, 3, 3, 2, 2, 0, 4, 5, 5, 7, 7, 6, 6, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
			for (var i = 0; i < edges.Length; i += 2)
				AddSegment(corners[edges[i]], corners[edges[i + 1]], color, camera);
		}

		private void AddWireDisc(Vector3 center, Vector3 normal, float radius, Color32 color, Camera camera)
		{
			normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
			var axis = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
			var tangent = Vector3.Cross(normal, axis).normalized;
			AddCircle(center, radius, tangent, Vector3.Cross(normal, tangent).normalized, color, camera);
		}

		private void AddCircle(Vector3 center, float radius, Vector3 axisA, Vector3 axisB, Color32 color, Camera camera)
		{
			var previous = center + axisA.normalized * radius;
			for (var i = 1; i <= CircleSegments; i++)
			{
				var angle = i * Mathf.PI * 2f / CircleSegments;
				var next = center + (axisA.normalized * Mathf.Cos(angle) + axisB.normalized * Mathf.Sin(angle)) * radius;
				AddSegment(previous, next, color, camera);
				previous = next;
			}
		}

		private void AddSegment(Vector3 start, Vector3 end, Color32 color, Camera camera)
		{
			var direction = end - start;
			if (direction.sqrMagnitude < 0.000001f)
				return;
			var side = Vector3.Cross(direction.normalized, camera.transform.forward);
			if (side.sqrMagnitude < 0.000001f)
				side = camera.transform.right;
			side = side.normalized * LineWidth;
			var first = m_Vertices.Count;
			m_Vertices.Add(start - side);
			m_Vertices.Add(start + side);
			m_Vertices.Add(end + side);
			m_Vertices.Add(end - side);
			m_Colors.Add(color);
			m_Colors.Add(color);
			m_Colors.Add(color);
			m_Colors.Add(color);
			m_Indices.Add(first);
			m_Indices.Add(first + 1);
			m_Indices.Add(first + 2);
			m_Indices.Add(first);
			m_Indices.Add(first + 2);
			m_Indices.Add(first + 3);
		}

		private static Vector3 WorldPoint(DebugDisplayVisualSnapshot visual, Vector3 value)
		{
			return visual.Space == DebugDisplaySpace.Local && visual.Basis != null
				? Matrix4x4.TRS(visual.Basis.Position, visual.Basis.Rotation, visual.Basis.Scale).MultiplyPoint3x4(value)
				: value;
		}

		private static Vector3 WorldDirection(DebugDisplayVisualSnapshot visual, Vector3 value)
		{
			return visual.Space == DebugDisplaySpace.Local && visual.Basis != null ? visual.Basis.Rotation * value : value;
		}

		private static Quaternion WorldRotation(DebugDisplayVisualSnapshot visual)
		{
			return visual.Space == DebugDisplaySpace.Local && visual.Basis != null
				? visual.Basis.Rotation * visual.Rotation
				: visual.Rotation;
		}

		private static Vector3 WorldSize(DebugDisplayVisualSnapshot visual)
		{
			return visual.Space == DebugDisplaySpace.Local && visual.Basis != null
				? Vector3.Scale(visual.Size, visual.Basis.Scale)
				: visual.Size;
		}

		private static float WorldScale(DebugDisplayVisualSnapshot visual)
		{
			if (visual.Space != DebugDisplaySpace.Local || visual.Basis == null)
				return 1f;
			return Mathf.Max(Mathf.Abs(visual.Basis.Scale.x), Mathf.Abs(visual.Basis.Scale.y), Mathf.Abs(visual.Basis.Scale.z));
		}
	}
}
#else
using System;
using System.Collections.Generic;

namespace Dexc
{
	/// <summary>URP is not installed: World Visual rendering is unavailable. The menu, HUD and other features keep working.</summary>
	internal static class DexcPlayerRenderer
	{
		internal static void EnsureInitialized() { }

		internal static void Shutdown() { }

#if UNITY_INCLUDE_TESTS
		internal static IReadOnlyList<DebugDisplayPrimitiveKind> GetWorldPrimitiveKindsForTests()
		{
			return Array.Empty<DebugDisplayPrimitiveKind>();
		}
#endif
	}
}
#endif
