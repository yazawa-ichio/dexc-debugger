#if DEXC_DEBUGGER
using System.Linq;
using System.Reflection;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DexcDebugger.Tests
{
	public sealed class DexcWireDiscTests
	{
		[TestCase(1f, 1f, 1f, false)]
		[TestCase(0f, 1f, 0f, false)]
		[TestCase(1f, 0f, 0f, false)]
		[TestCase(-2f, 7f, 3f, false)]
		[TestCase(1f, 1f, 1f, true)]
		public void WireCenterlineStaysOnRequestedPlaneAndRadius(float x, float y, float z, bool local)
		{
			Dexc.DexcDebugger.ResetForTests();
			Dexc.DexcDebugger.Initialize();
			var cameraObject = new GameObject("WireDisc camera");
			var basisObject = new GameObject("WireDisc basis");
			var pass = new DexcPlayerRenderPass();
			try
			{
				var camera = cameraObject.AddComponent<Camera>();
				camera.transform.rotation = Quaternion.Euler(23f, 37f, 11f);
				basisObject.transform.SetPositionAndRotation(new Vector3(-3f, 5f, 7f), Quaternion.Euler(35f, 67f, 19f));
				basisObject.transform.localScale = Vector3.one * 2f;
				var center = new Vector3(2f, 3f, -1f);
				var normal = new Vector3(x, y, z);
				const float radius = 2.5f;
				DebugDisplay.DrawWireDisc(
					"FIX07",
					center,
					normal,
					radius,
					Color.green,
					local ? DebugDisplaySpace.Local : DebugDisplaySpace.World,
					local ? basisObject.transform : null
				);
				Dexc.DexcDebugger.CurrentSession.Display.Tick();
				Assert.That(DebugDisplay.TryGetCompletedFrame(out var frame), Is.True);
				Assert.That(frame.Globals.Single().Kind, Is.EqualTo(DebugDisplayPrimitiveKind.WireDisc));
				typeof(DexcPlayerRenderPass)
					.GetMethod("BuildMesh", BindingFlags.Instance | BindingFlags.NonPublic)
					.Invoke(pass, new object[] { frame, camera });
				var mesh = (Mesh)
					typeof(DexcPlayerRenderPass).GetField("m_Mesh", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pass);
				var vertices = mesh.vertices;
				Assert.That(vertices.Length, Is.EqualTo(32 * 4));
				var worldCenter = local ? basisObject.transform.TransformPoint(center) : center;
				var worldNormal = (local ? basisObject.transform.rotation * normal : normal).normalized;
				var worldRadius = local ? radius * 2f : radius;
				var first = (vertices[0] + vertices[1]) * 0.5f;
				var previous = first;
				for (var index = 0; index < vertices.Length; index += 4)
				{
					var start = (vertices[index] + vertices[index + 1]) * 0.5f;
					var end = (vertices[index + 2] + vertices[index + 3]) * 0.5f;
					Assert.That(Vector3.Distance(start, previous), Is.LessThan(0.0001f));
					foreach (var point in new[] { start, end })
					{
						Assert.That(Vector3.Dot(point - worldCenter, worldNormal), Is.EqualTo(0f).Within(0.0001f), "Disc plane");
						Assert.That(Vector3.Distance(point, worldCenter), Is.EqualTo(worldRadius).Within(0.0001f), "Disc radius");
					}
					previous = end;
				}
				Assert.That(Vector3.Distance(previous, first), Is.LessThan(0.0001f), "Closed circle");
			}
			finally
			{
				pass.ReleaseResources(true);
				Object.DestroyImmediate(cameraObject);
				Object.DestroyImmediate(basisObject);
				Dexc.DexcDebugger.ResetForTests();
			}
		}
	}
}
#endif
