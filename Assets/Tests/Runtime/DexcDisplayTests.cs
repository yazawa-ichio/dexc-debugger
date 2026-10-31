using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	[TestFixture]
	public sealed class DexcDisplayTests
	{
		private GameObject m_Owner;

		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize();
			m_Owner = new GameObject("DexcDisplayOwner");
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Owner != null)
				UnityEngine.Object.DestroyImmediate(m_Owner);
			DebuggerRuntime.ResetForTests();
		}

#if DEXC_DEBUGGER
		[Test]
		public void SubjectStoresTypedMetadataAndUpdatesExistingSlot()
		{
			var subject = DebugDisplay.CreateSubject("Enemy", m_Owner);
			Assert.That(subject.Id.IsValid, Is.True);
			Assert.That(subject.Label, Is.EqualTo("DexcDisplayOwner"));

			subject.SetMetadata("HitPoints", 10);
			subject.SetMetadata("HitPoints", 7);
			subject.SetMetadata("Velocity", new Vector3(1f, 2f, 3f));
			var snapshots = DebuggerRuntime.CurrentSession.Display.CaptureSubjects();
			var snapshot = snapshots.Single(item => item.Id == subject.Id);
			Assert.That(snapshot.Category, Is.EqualTo("Enemy"));
			Assert.That(snapshot.Metadata, Has.Count.EqualTo(2));
			Assert.That(snapshot.Metadata.Single(item => item.Name == "HitPoints").Value, Is.EqualTo(DebugValue.From(7)));
			Assert.That(snapshot.Metadata.Single(item => item.Name == "Velocity").Value.Kind, Is.EqualTo(DebugValueKind.Object));

			subject.SetMetadata("HitPoints", "wrong-type");
			snapshots = DebuggerRuntime.CurrentSession.Display.CaptureSubjects();
			Assert.That(
				snapshots.Single(item => item.Id == subject.Id).Metadata.Single(item => item.Name == "HitPoints").Value,
				Is.EqualTo(DebugValue.From(7))
			);
		}

		[Test]
		public void SubjectLabelAndMetadataRemovalAreExplicitAndDisposeIsIdempotent()
		{
			var subject = DebugDisplay.CreateSubject("Enemy", m_Owner);
			subject.SetLabel("Elite");
			subject.SetMetadata("State", "Alert");
			subject.RemoveMetadata("State");
			Assert.That(subject.Label, Is.EqualTo("Elite"));
			Assert.That(DebuggerRuntime.CurrentSession.Display.CaptureSubjects().Single().Metadata, Is.Empty);

			subject.Dispose();
			subject.Dispose();
			Assert.That(DebuggerRuntime.CurrentSession.Display.CaptureSubjects(), Is.Empty);
		}

		[Test]
		public void DestroyedOwnerIsRemovedWithoutStrongOwnerRetention()
		{
			var subject = DebugDisplay.CreateSubject("Enemy", m_Owner);
			Assert.That(DebuggerRuntime.CurrentSession.Display.CaptureSubjects(), Has.Count.EqualTo(1));
			UnityEngine.Object.DestroyImmediate(m_Owner);
			m_Owner = null;
			DebuggerRuntime.CurrentSession.Display.Tick();
			Assert.That(DebuggerRuntime.CurrentSession.Display.CaptureSubjects(), Is.Empty);
			subject.Dispose();
		}

		[Test]
		public void SubjectAndMetadataCapacityReturnSharedNoOpOrRejectNewSlot()
		{
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize(options =>
			{
				options.Display.MaxSubjectCount = 1;
				options.Display.MaxMetadataPerSubject = 1;
			});
			var first = DebugDisplay.CreateSubject("First", m_Owner);
			var other = new GameObject("DexcDisplayOther");
			var second = DebugDisplay.CreateSubject("Second", other);
			Assert.That(second, Is.SameAs(DebugDisplay.CreateSubject("Third", m_Owner)));
			first.SetMetadata("First", 1);
			first.SetMetadata("Second", 2);
			var snapshot = DebuggerRuntime.CurrentSession.Display.CaptureSubjects().Single();
			Assert.That(snapshot.Metadata, Has.Count.EqualTo(1));
			second.Dispose();
			UnityEngine.Object.DestroyImmediate(other);
		}

		[Test]
		public void ReinitializeInvalidatesSubjectOperationsAndClearsRegistry()
		{
			var subject = DebugDisplay.CreateSubject("Enemy", m_Owner);
			DebuggerRuntime.Initialize();
			DebugDisplay.TryGetCompletedFrame(out _);
			Assert.That(DebuggerRuntime.CurrentSession.Display.CaptureSubjects(), Is.Empty);
			Assert.Throws<InvalidOperationException>(() => subject.SetLabel("stale"));
			subject.Dispose();
		}

		[Test]
		public void FrameSnapshotIsImmutableAndFrameVisualsDoNotLeakToNextFrame()
		{
			var options = DebugDrawOptions.Frame("Physics/Line", Color.red);
			DebugDisplay.DrawLine(options, Vector3.zero, Vector3.right);
			Assert.That(DebugDisplay.TryGetCompletedFrame(out _), Is.False);
			DebuggerRuntime.CurrentSession.Display.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var first), Is.True);
			Assert.That(first.Globals, Has.Count.EqualTo(1));
			Assert.That(first.Globals[0].Start, Is.EqualTo(Vector3.zero));

			DebugDisplay.DrawLine(options, Vector3.up, Vector3.forward);
			DebuggerRuntime.CurrentSession.Display.Tick();
			Assert.That(first.Globals[0].Start, Is.EqualTo(Vector3.zero));
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var second), Is.True);
			Assert.That(second.Frame, Is.GreaterThan(first.Frame));
			Assert.That(second.Globals[0].Start, Is.EqualTo(Vector3.up));
		}

		[Test]
		public void SubjectVisualsUseStructuredSnapshotAndInfinityClearIsExplicit()
		{
			var subject = DebugDisplay.CreateSubject("Enemy", m_Owner);
			subject.DrawWireSphere(DebugDrawOptions.Infinity("Perception", Color.cyan), Vector3.one, 2f);
			DebuggerRuntime.CurrentSession.Display.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var first), Is.True);
			Assert.That(first.Subjects.Single().Visuals, Has.Count.EqualTo(1));
			Assert.That(first.Subjects.Single().Visuals[0].Kind, Is.EqualTo(DebugDisplayPrimitiveKind.WireSphere));
			Assert.That(first.Subjects.Single().Visuals[0].Radius, Is.EqualTo(2f));

			DebuggerRuntime.CurrentSession.Display.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var retained), Is.True);
			Assert.That(retained.Subjects.Single().Visuals, Has.Count.EqualTo(1));
			subject.Clear("Perception");
			DebuggerRuntime.CurrentSession.Display.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var cleared), Is.True);
			Assert.That(cleared.Subjects.Single().Visuals, Is.Empty);
		}

		[Test]
		public void WireSectorIsAClosedPathWhoseEdgesOpenAroundForward()
		{
			DebugDisplay.DrawWireSector(
				DebugDrawOptions.Frame("Guard/View", Color.yellow),
				Vector3.zero,
				Vector3.up,
				Vector3.forward,
				90f,
				2f
			);
			DebuggerRuntime.CurrentSession.Display.Tick();

			Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
			var sector = frame.Globals.Single();
			Assert.That(sector.Kind, Is.EqualTo(DebugDisplayPrimitiveKind.PolyLine));
			var points = sector.Points;
			Assert.That(points[0], Is.EqualTo(Vector3.zero));
			Assert.That(points[points.Count - 1], Is.EqualTo(Vector3.zero), "the outline closes at the centre");
			var edges = new[] { points[1], points[points.Count - 2] };
			foreach (var edge in edges)
			{
				Assert.That(edge.magnitude, Is.EqualTo(2f).Within(0.001f));
				Assert.That(Vector3.Angle(Vector3.forward, edge), Is.EqualTo(45f).Within(0.01f));
				Assert.That(edge.y, Is.EqualTo(0f).Within(0.001f), "the sector lies flat around the normal");
			}
			Assert.That(edges[0].x * edges[1].x, Is.LessThan(0f), "one edge on each side of forward");
		}

		[Test]
		public void InvalidGeometryPolylineLimitAndPrimitiveKindChangeAreDroppedAtomically()
		{
			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize(options =>
			{
				options.Display.MaxPolylinePointCount = 2;
				options.Display.MaxVisualsPerFrame = 8;
			});
			var line = DebugDrawOptions.Frame("Geometry/One", Color.white);
			DebugDisplay.DrawLine(line, Vector3.zero, Vector3.right);
			DebugDisplay.DrawWireSphere(line, Vector3.zero, 1f);
			DebugDisplay.DrawPolyLine(
				DebugDrawOptions.Frame("Geometry/TooLong", Color.white),
				new[] { Vector3.zero, Vector3.right, Vector3.up }
			);
			DebugDisplay.DrawWireSphere(DebugDrawOptions.Frame("Geometry/NaN", Color.white), Vector3.zero, float.NaN);
			DebuggerRuntime.CurrentSession.Display.Tick();

			Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
			Assert.That(frame.Globals, Has.Count.EqualTo(1));
			Assert.That(frame.Globals[0].Kind, Is.EqualTo(DebugDisplayPrimitiveKind.Line));
			Assert.That(frame.DroppedCount, Is.EqualTo(3));
		}

		[Test]
		public void WithoutASessionDrawingAndRegisteringDoNothing()
		{
			DebuggerRuntime.ResetForTests();

			var subject = DebugDisplay.CreateSubject("Enemy", m_Owner);
			subject.SetMetadata("HitPoints", 10);
			subject.DrawLabel("Name", "Enemy", Color.white);
			subject.Dispose();
			DebugDisplay.DrawLine("Line", Vector3.zero, Vector3.right, Color.white);
			DebugDisplay.Clear("Line");
			DebugInspector.Register("Enemies", m_Owner, "Enemy");
			DebugInspector.Unregister("Enemies", m_Owner);

			Assert.That(subject.Id.IsValid, Is.False);
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.False);
			Assert.That(frame, Is.Null);
		}

		[Test]
		public void PolyLineTakesASpanAsWellAsAnArrayOrAList()
		{
			Span<Vector3> stack = stackalloc Vector3[] { Vector3.zero, Vector3.right, Vector3.up };
			DebugDisplay.DrawPolyLine("Span", stack, Color.white);
			DebugDisplay.DrawPolyLine("Array", new[] { Vector3.zero, Vector3.right }, Color.white);
			DebugDisplay.DrawPolyLine("List", new List<Vector3> { Vector3.zero, Vector3.up }, Color.white);
			DebuggerRuntime.CurrentSession.Display.Tick();

			Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
			Assert.That(frame.Globals.Single(item => item.Path == "Span").Points, Is.EqualTo(new[] { Vector3.zero, Vector3.right, Vector3.up }));
			Assert.That(frame.Globals.Single(item => item.Path == "Array").Points, Has.Count.EqualTo(2));
			Assert.That(frame.Globals.Single(item => item.Path == "List").Points, Has.Count.EqualTo(2));
		}

		[Test]
		public void OptionsRejectInvalidLifetimeAndLocalGeometryRequiresBasis()
		{
			Assert.Throws<ArgumentException>(() => DebugDrawOptions.Frame(string.Empty, Color.white));
			Assert.Throws<ArgumentOutOfRangeException>(() => DebugDrawOptions.Realtime("x", Color.white, 0f));
			Assert.Throws<ArgumentException>(() => DebugDrawOptions.Create("x", Color.white, DebugDisplayTimeScope.Infinity, 1f));
			Assert.Throws<ArgumentOutOfRangeException>(() => DebugDrawOptions.Frame("x", new Color(float.NaN, 0f, 0f, 1f)));

			DebugDisplay.DrawWireSphere(DebugDrawOptions.Frame("Local/Invalid", Color.white), Vector3.zero, 1f, DebugDisplaySpace.Local);
			DebuggerRuntime.CurrentSession.Display.Tick();
			Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
			Assert.That(frame.Globals, Is.Empty);
			Assert.That(frame.DroppedCount, Is.EqualTo(1));
		}

		[Test]
		public void SnapshotPreservesInitialWorldPrimitivesRotationSpaceAndBasis()
		{
			m_Owner.transform.position = new Vector3(3f, 4f, 5f);
			m_Owner.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
			m_Owner.transform.localScale = new Vector3(2f, 3f, 4f);
			var subject = DebugDisplay.CreateSubject("Enemy", m_Owner);
			DebugDisplay.DrawLine("Line", Vector3.zero, Vector3.right, Color.white);
			DebugDisplay.DrawRay("Ray", Vector3.zero, Vector3.up, Color.white);
			DebugDisplay.DrawPolyLine("Polyline", new[] { Vector3.zero, Vector3.right, Vector3.up }, Color.white);
			DebugDisplay.DrawWireSphere("Sphere", Vector3.zero, 1f, Color.white);
			DebugDisplay.DrawWireCube("Cube", Vector3.zero, Vector3.one, Quaternion.Euler(0f, 30f, 0f), Color.white);
			DebugDisplay.DrawWireDisc("Disc", Vector3.zero, Vector3.up, 1f, Color.white);
			DebugDisplay.DrawRelation("Relation", subject, Color.white);
			DebugDisplay.DrawWireSphere("LocalSphere", Vector3.right, 1f, Color.white, DebugDisplaySpace.Local, m_Owner.transform);
			DebuggerRuntime.CurrentSession.Display.Tick();

			Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
			Assert.That(frame.Globals.Select(item => item.Kind), Does.Contain(DebugDisplayPrimitiveKind.WireDisc));
			Assert.That(frame.Globals.Select(item => item.Kind), Does.Contain(DebugDisplayPrimitiveKind.Relation));
			var cube = frame.Globals.Single(item => item.Path == "Cube");
			Assert.That(cube.Rotation, Is.EqualTo(Quaternion.Euler(0f, 30f, 0f)));
			var local = frame.Globals.Single(item => item.Path == "LocalSphere");
			Assert.That(local.Space, Is.EqualTo(DebugDisplaySpace.Local));
			Assert.That(local.Basis.Position, Is.EqualTo(m_Owner.transform.position));
			Assert.That(local.Basis.Rotation, Is.EqualTo(m_Owner.transform.rotation));
			Assert.That(local.Basis.Scale, Is.EqualTo(m_Owner.transform.lossyScale));
		}

		[Test]
		public async Task RealtimeAndGameTimeUseSeparateClocks()
		{
			var previousScale = Time.timeScale;
			try
			{
				Time.timeScale = 0f;
				DebugDisplay.DrawLabelFor("Time/Realtime", "real", 0.01f, DebugDisplayTimeScope.Realtime);
				DebugDisplay.DrawLabelFor("Time/Game", "game", 0.01f, DebugDisplayTimeScope.GameTime);
				DebuggerRuntime.CurrentSession.Display.Tick();
				await Task.Delay(100);
				DebuggerRuntime.CurrentSession.Display.Tick();
				Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
				Assert.That(frame.Globals, Has.Count.EqualTo(1));
				Assert.That(frame.Globals[0].Path, Is.EqualTo("Time/Game"));
			}
			finally
			{
				Time.timeScale = previousScale;
			}
		}

		[Test]
		public void DisposedRelationTargetBecomesMissingInLaterSnapshot()
		{
			var source = DebugDisplay.CreateSubject("Source", m_Owner);
			var targetObject = new GameObject("DexcDisplayTarget");
			var target = DebugDisplay.CreateSubject("Target", targetObject);
			source.DrawRelation(DebugDrawOptions.Infinity("Target", Color.red), target);
			DebuggerRuntime.CurrentSession.Display.Tick();
			target.Dispose();
			DebuggerRuntime.CurrentSession.Display.Tick();
			var sourceSnapshot = DebugDisplay.TryGetCompletedFrame(out var frame)
				? frame.Subjects.Single(item => item.Id == source.Id)
				: null;
			Assert.That(sourceSnapshot, Is.Not.Null);
			Assert.That(sourceSnapshot.Visuals.Single().Target.IsValid, Is.False);
			Assert.That(sourceSnapshot.Visuals.Single().TargetMissing, Is.True);
			UnityEngine.Object.DestroyImmediate(targetObject);
		}
#endif
	}
}
