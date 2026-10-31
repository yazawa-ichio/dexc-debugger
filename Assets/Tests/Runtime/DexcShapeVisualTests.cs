#if DEXC_DEBUGGER && UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Dexc;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcShapeVisualTests
	{
		[UnityTest]
		public IEnumerator PlayerCameraRendersAllShapesInAllStyles()
		{
			DebuggerRuntime.ResetForTests();
			var skin = ScriptableObject.CreateInstance<DexcRuntimeUISkin>();
			skin.SetWorldVisualShaderForEditor(
				AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader")
			);
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = skin);
			DexcPlayerRenderer.EnsureInitialized();
			var cameraObject = new GameObject("VIS01CaptureCamera");
			var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
			var previous = RenderTexture.active;
			var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
			GameObject occluder = null;
			Material occluderMaterial = null;
			try
			{
				var camera = cameraObject.AddComponent<Camera>();
				camera.enabled = false;
				camera.orthographic = true;
				camera.orthographicSize = 3f;
				camera.aspect = 1f;
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = Color.black;
				camera.transform.position = new Vector3(0f, 0f, -10f);
				camera.targetTexture = target;
				camera.GetUniversalAdditionalCameraData();
				target.Create();
				var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/UnityTests/VIS01"));
				Directory.CreateDirectory(directory);
				foreach (var shape in new[] { "Sphere", "Cube", "Disc", "Capsule" })
				{
					foreach (DebugDisplayDrawStyle style in Enum.GetValues(typeof(DebugDisplayDrawStyle)))
					{
						var options = DebugDrawOptions.Frame("capture", Color.green).WithStyle(style, Color.red);
						switch (shape)
						{
							case "Sphere":
								DebugDisplay.DrawSphere(options, Vector3.zero, 1.4f);
								break;
							case "Cube":
								DebugDisplay.DrawCube(options, Vector3.zero, Vector3.one * 2f, Quaternion.Euler(20f, 30f, 0f));
								break;
							case "Disc":
								DebugDisplay.DrawDisc(options, Vector3.zero, new Vector3(0.3f, 0.4f, -1f), 1.4f);
								break;
							case "Capsule":
								DebugDisplay.DrawCapsule(options, Vector3.zero, 0.8f, 4f, Quaternion.Euler(0f, 0f, 20f));
								break;
						}
						DebuggerRuntime.CurrentSession.Display.Tick();
						RenderPipeline.SubmitRenderRequest(
							camera,
							new UniversalRenderPipeline.SingleCameraRequest { destination = target }
						);
						Assert.That(DexcPlayerRenderer.Data.Pass?.m_Mesh, Is.Not.Null);
						yield return null;
						RenderTexture.active = target;
						texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
						texture.Apply(false, false);
						var pixels = texture.GetPixels32();
						var red = pixels.Count(p => p.r > 80 && p.r > p.g * 2);
						var green = pixels.Count(p => p.g > 80 && p.g > p.r * 2);
						File.WriteAllBytes(Path.Combine(directory, shape + "-" + style + ".png"), texture.EncodeToPNG());
						Assert.That(red > 100, Is.EqualTo(style != DebugDisplayDrawStyle.Wire), shape + "/" + style + " fill coverage");
						Assert.That(green > 10, Is.EqualTo(style != DebugDisplayDrawStyle.Solid), shape + "/" + style + " wire coverage");
					}
				}
				var opaque = DebugDrawOptions.Infinity("capture", Color.red).WithStyle(DebugDisplayDrawStyle.Solid, Color.red);
				DebugDisplay.DrawDisc(opaque, Vector3.zero, Vector3.forward, 1.4f);
				DebuggerRuntime.CurrentSession.Display.Tick();
				var opaquePixel = CenterPixel(camera, target, texture);
				DebugDisplay.DrawDisc(
					DebugDrawOptions.Infinity("capture", Color.red).WithStyle(DebugDisplayDrawStyle.Solid),
					Vector3.zero,
					Vector3.forward,
					1.4f
				);
				DebuggerRuntime.CurrentSession.Display.Tick();
				var transparentPixel = CenterPixel(camera, target, texture);
				Assert.That(transparentPixel.r, Is.GreaterThan(0.02f).And.LessThan(opaquePixel.r - 0.1f));
				File.WriteAllBytes(Path.Combine(directory, "Disc-default-alpha.png"), texture.EncodeToPNG());
				occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
				occluder.transform.position = new Vector3(0f, 0f, -2f);
				occluder.transform.localScale = new Vector3(6f, 6f, 1f);
				var shader = Shader.Find("Universal Render Pipeline/Unlit");
				Assert.That(shader, Is.Not.Null);
				occluderMaterial = new Material(shader);
				occluderMaterial.SetColor("_BaseColor", Color.blue);
				occluder.GetComponent<Renderer>().sharedMaterial = occluderMaterial;
				yield return null;
				var hiddenPixel = CenterPixel(camera, target, texture);
				Assert.That(hiddenPixel.b, Is.GreaterThan(.5f));
				Assert.That(hiddenPixel.r, Is.LessThan(.05f));
				File.WriteAllBytes(Path.Combine(directory, "Disc-opaque-occlusion.png"), texture.EncodeToPNG());
			}
			finally
			{
				RenderTexture.active = previous;
				DebuggerRuntime.ResetForTests();
				Object.DestroyImmediate(texture);
				target.Release();
				Object.DestroyImmediate(target);
				Object.DestroyImmediate(cameraObject);
				Object.DestroyImmediate(skin);
				if (occluder != null)
					Object.DestroyImmediate(occluder);
				if (occluderMaterial != null)
					Object.DestroyImmediate(occluderMaterial);
			}
		}

		private static Color CenterPixel(Camera camera, RenderTexture target, Texture2D texture)
		{
			RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
			RenderTexture.active = target;
			texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
			texture.Apply();
			return texture.GetPixel(128, 128);
		}
	}
}
#endif
