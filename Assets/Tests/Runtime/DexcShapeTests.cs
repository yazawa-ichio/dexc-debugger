#if DEXC_DEBUGGER
using System;
using System.Linq;
using System.Reflection;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcShapeTests
	{
		private GameObject m_CameraObject;
		private Camera m_Camera;
		private DexcPlayerRenderPass m_Pass;

		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize();
			m_CameraObject = new GameObject("ShapeTestCamera");
			m_Camera = m_CameraObject.AddComponent<Camera>();
			m_Pass = new DexcPlayerRenderPass();
		}

		[TearDown]
		public void TearDown()
		{
			m_Pass.ReleaseResources(true);
			Object.DestroyImmediate(m_CameraObject);
			DebuggerRuntime.ResetForTests();
		}

		private DebugDisplayFrameSnapshot Frame()
		{
			DebuggerRuntime.CurrentSession.Display.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
			return frame;
		}

		private Mesh Build(DebugDisplayFrameSnapshot frame)
		{
			typeof(DexcPlayerRenderPass)
				.GetMethod("BuildMesh", BindingFlags.NonPublic | BindingFlags.Instance)
				.Invoke(m_Pass, new object[] { frame, m_Camera });
			return (Mesh)typeof(DexcPlayerRenderPass).GetField("m_Mesh", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(m_Pass);
		}

		private static void Draw(int shape, DebugDrawOptions options)
		{
			switch (shape)
			{
				case 0:
					DebugDisplay.DrawSphere(options, Vector3.zero, 1f);
					break;
				case 1:
					DebugDisplay.DrawCube(options, Vector3.zero, new Vector3(2f, 3f, 4f), Quaternion.identity);
					break;
				case 2:
					DebugDisplay.DrawDisc(options, Vector3.zero, Vector3.forward, 1f);
					break;
				case 3:
					DebugDisplay.DrawCapsule(options, Vector3.zero, 1f, 4f, Quaternion.identity);
					break;
			}
		}

		[Test]
		public void OptionsAreImmutableAndRejectInvalidStylesAndColors()
		{
			var original = DebugDrawOptions.Infinity("shape", Color.green);
			var changed = original.WithStyle(DebugDisplayDrawStyle.WireAndSolid, Color.red);
			Assert.That(original.DrawStyle, Is.EqualTo(DebugDisplayDrawStyle.Wire));
			Assert.That(changed.FillColor, Is.EqualTo(Color.red));
			Assert.That(changed.TimeScope, Is.EqualTo(DebugDisplayTimeScope.Infinity));
			Assert.That(original.FillColor.a, Is.EqualTo(0.25f));
			Assert.Throws<ArgumentOutOfRangeException>(() => original.WithStyle((DebugDisplayDrawStyle)99));
			Assert.Throws<ArgumentOutOfRangeException>(() => original.WithStyle(DebugDisplayDrawStyle.Solid, new Color(float.NaN, 0f, 0f)));
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(2)]
		[TestCase(3)]
		public void EveryShapeHasIndependentFillAndWireGeometry(int shape)
		{
			foreach (DebugDisplayDrawStyle style in Enum.GetValues(typeof(DebugDisplayDrawStyle)))
			{
				Draw(shape, DebugDrawOptions.Frame("shape", Color.green).WithStyle(style, Color.red));
				var frame = Frame();
				Assert.That(frame.Globals.Single().DrawStyle, Is.EqualTo(style));
				var mesh = Build(frame);
				Assert.That(mesh.subMeshCount, Is.EqualTo(2));
				Assert.That(mesh.GetIndexCount(0) > 0, Is.EqualTo(style != DebugDisplayDrawStyle.Wire));
				Assert.That(mesh.GetIndexCount(1) > 0, Is.EqualTo(style != DebugDisplayDrawStyle.Solid));
				Assert.That(
					mesh.vertices.All(v =>
						!float.IsNaN(v.x)
						&& !float.IsInfinity(v.x)
						&& !float.IsNaN(v.y)
						&& !float.IsInfinity(v.y)
						&& !float.IsNaN(v.z)
						&& !float.IsInfinity(v.z)
					),
					Is.True
				);
				var colors = mesh.colors32;
				Assert.That(mesh.GetIndices(0).All(i => colors[i].r == 255 && colors[i].g == 0), Is.True);
				Assert.That(mesh.GetIndices(1).All(i => colors[i].g == 255 && colors[i].r == 0), Is.True);
			}
		}

		[Test]
		public void LegacyWireApisIgnoreFillStyle()
		{
			var options = DebugDrawOptions.Frame("sphere", Color.white).WithStyle(DebugDisplayDrawStyle.Solid);
			DebugDisplay.DrawWireSphere(options, Vector3.zero, 1f);
			var frame = Frame();
			Assert.That(frame.Globals.Single().Kind, Is.EqualTo(DebugDisplayPrimitiveKind.WireSphere));
			Assert.That(frame.Globals.Single().DrawStyle, Is.EqualTo(DebugDisplayDrawStyle.Wire));
			Assert.That(Build(frame).GetIndexCount(0), Is.Zero);
		}

		[Test]
		public void ObliqueDiscVerticesStayInRequestedPlane()
		{
			var normal = new Vector3(1f, 2f, 3f).normalized;
			var center = new Vector3(5f, 1f, -2f);
			DebugDisplay.DrawDisc("disc", center, normal, 2f, Color.white, DebugDisplayDrawStyle.Solid);
			var vertices = Build(Frame()).vertices;
			Assert.That(vertices.All(v => Mathf.Abs(Vector3.Dot(v - center, normal)) < 0.0001f), Is.True);
			Assert.That(vertices.Max(v => (v - center).magnitude), Is.EqualTo(2f).Within(0.0001f));
			DebugDisplay.DrawWireDisc("disc", center, normal, 2f, Color.white);
			vertices = Build(Frame()).vertices;
			// Each segment quad is symmetric around its mathematical endpoints.
			for (var i = 0; i < vertices.Length; i += 4)
				Assert.That(Mathf.Abs(Vector3.Dot((vertices[i] + vertices[i + 1]) * 0.5f - center, normal)), Is.LessThan(0.0001f));
		}

		[TestCase(4f, 4f)]
		[TestCase(1f, 2f)]
		[TestCase(0f, 2f)]
		public void CapsuleHeightIncludesCapsAndRotatesToX(float height, float expected)
		{
			DebugDisplay.DrawCapsule(
				"capsule",
				Vector3.zero,
				1f,
				height,
				Quaternion.FromToRotation(Vector3.up, Vector3.right),
				Color.white,
				DebugDisplayDrawStyle.Solid
			);
			var mesh = Build(Frame());
			Assert.That(mesh.bounds.size.x, Is.EqualTo(expected).Within(0.0001f));
			Assert.That(mesh.bounds.size.y, Is.EqualTo(2f).Within(0.0001f));
			Assert.That(mesh.bounds.size.z, Is.EqualTo(2f).Within(0.0001f));
		}

		[Test]
		public void CapsuleSurfaceMatchesDistanceToAxisSegment()
		{
			DebugDisplay.DrawCapsule("capsule", Vector3.zero, 1f, 4f, Quaternion.identity, Color.white, DebugDisplayDrawStyle.Solid);
			foreach (var v in Build(Frame()).vertices)
			{
				var nearest = new Vector3(0f, Mathf.Clamp(v.y, -1f, 1f), 0f);
				Assert.That((v - nearest).magnitude, Is.EqualTo(1f).Within(0.0001f));
			}
		}

		[Test]
		public void SubjectLocalCapsulePreservesSnapshotAndLifetime()
		{
			m_CameraObject.transform.position = new Vector3(3f, 4f, 5f);
			m_CameraObject.transform.localScale = new Vector3(2f, 3f, 2f);
			var subject = DebugDisplay.CreateSubject("collider", m_CameraObject);
			var options = DebugDrawOptions.Infinity("capsule", Color.green).WithStyle(DebugDisplayDrawStyle.Solid, Color.red);
			subject.DrawCapsule(options, Vector3.zero, 1f, 4f, Quaternion.identity, DebugDisplaySpace.Local, m_CameraObject.transform);
			var first = Frame();
			var snapshot = first.Subjects.Single().Visuals.Single();
			Assert.That(snapshot.Height, Is.EqualTo(4f));
			Assert.That(snapshot.FillColor, Is.EqualTo(Color.red));
			Assert.That(snapshot.WithMissingTarget().DrawStyle, Is.EqualTo(DebugDisplayDrawStyle.Solid));
			var mesh = Build(first);
			Assert.That(mesh.bounds.center, Is.EqualTo(new Vector3(3f, 4f, 5f)));
			Assert.That(mesh.bounds.size.y, Is.EqualTo(12f).Within(0.0001f));
			Assert.That(mesh.bounds.size.x, Is.EqualTo(4f).Within(0.0001f));
			Assert.That(Frame().Subjects.Single().Visuals.Single().DrawStyle, Is.EqualTo(DebugDisplayDrawStyle.Solid));
			subject.DrawCapsule(options.WithStyle(DebugDisplayDrawStyle.Wire), Vector3.zero, 1f, 2f, Quaternion.identity);
			Assert.That(Frame().Subjects.Single().Visuals, Has.Count.EqualTo(1));
			Assert.That(snapshot.DrawStyle, Is.EqualTo(DebugDisplayDrawStyle.Solid));
		}

		[TestCase(-1f, 2f)]
		[TestCase(1f, -1f)]
		[TestCase(float.NaN, 2f)]
		[TestCase(1f, float.PositiveInfinity)]
		public void InvalidCapsuleIsDropped(float radius, float height)
		{
			DebugDisplay.DrawCapsule("bad", Vector3.zero, radius, height, Quaternion.identity, Color.white);
			var frame = Frame();
			Assert.That(frame.Globals, Is.Empty);
			Assert.That(frame.DroppedCount, Is.EqualTo(1));
		}

		[Test]
		public void LargeShapeMeshUses32BitIndices()
		{
			for (var i = 0; i < 24; i++)
				Draw(3, DebugDrawOptions.Frame("shape/" + i, Color.white).WithStyle(DebugDisplayDrawStyle.Solid));
			var mesh = Build(Frame());
			Assert.That(mesh.vertexCount, Is.GreaterThan(65535));
			Assert.That(mesh.indexFormat, Is.EqualTo(UnityEngine.Rendering.IndexFormat.UInt32));
			Assert.That(mesh.GetIndices(0).Max(), Is.LessThan(mesh.vertexCount));
		}

		[TestCase(1e-30f)]
		[TestCase(1e30f)]
		public void CapsuleNormalizesFiniteNonzeroRotationInSnapshot(float magnitude)
		{
			DebugDisplay.DrawCapsule(
				"normalized",
				Vector3.zero,
				1f,
				4f,
				new Quaternion(0f, 0f, magnitude, magnitude),
				Color.white,
				DebugDisplayDrawStyle.Solid
			);
			var frame = Frame();
			Assert.That(Quaternion.Dot(frame.Globals.Single().Rotation, frame.Globals.Single().Rotation), Is.EqualTo(1f).Within(.00001f));
			Assert.That(Build(frame).bounds.size.x, Is.EqualTo(4f).Within(.0001f));
		}

		[Test]
		public void LocalCapsuleClampsFullHeightBeforeAxisAndRadialScaling()
		{
			m_CameraObject.transform.localScale = new Vector3(-2f, 3f, 1f);
			DebugDisplay.DrawCapsule(
				"scaled",
				Vector3.zero,
				1f,
				1f,
				Quaternion.identity,
				Color.white,
				DebugDisplayDrawStyle.Solid,
				DebugDisplaySpace.Local,
				m_CameraObject.transform
			);
			var bounds = Build(Frame()).bounds;
			Assert.That(bounds.size.y, Is.EqualTo(6f).Within(.0001f));
			Assert.That(bounds.size.x, Is.EqualTo(4f).Within(.0001f));
			Assert.That(bounds.size.z, Is.EqualTo(4f).Within(.0001f));
		}
	}
}
#endif
