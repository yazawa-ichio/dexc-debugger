#if DEXC_DEBUGGER && UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using Dexc;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcPlayerResourceLifetimeTests
	{
		private const string ResourceName = "FIX05E Runtime owned resource";
		private DexcRuntimeUISkin m_Skin;

		// The pass now lives on the root's state slot instead of a static field.
		private static DexcPlayerRenderPass Pass
		{
			get => DexcPlayerRenderer.Data.Pass;
			set => DexcPlayerRenderer.Data.Pass = value;
		}
		private static readonly MethodInfo MaterialMethod = typeof(DexcPlayerRenderPass).GetMethod(
			"GetMaterial",
			BindingFlags.Instance | BindingFlags.NonPublic
		);
		private static readonly MethodInfo MeshMethod = typeof(DexcPlayerRenderPass).GetMethod(
			"BuildMesh",
			BindingFlags.Instance | BindingFlags.NonPublic
		);
		private static readonly FieldInfo MeshField = typeof(DexcPlayerRenderPass).GetField(
			"m_Mesh",
			BindingFlags.Instance | BindingFlags.NonPublic
		);

		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.ResetForTests();
			m_Skin = ScriptableObject.CreateInstance<DexcRuntimeUISkin>();
			m_Skin.SetWorldVisualShaderForEditor(
				AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader")
			);
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			DexcPlayerRenderer.EnsureInitialized();
			DexcRuntimeUIDriver.CreateForBootstrap();
		}

		[TearDown]
		public void TearDown()
		{
			DebuggerRuntime.ResetForTests();
			Pass = null;
			foreach (var resource in OwnedResources())
			{
				Object.DestroyImmediate(resource);
			}
			Object.DestroyImmediate(m_Skin);
		}

		[UnityTest]
		public IEnumerator RepeatedSessionAndSkinInitializationReleaseNativeResources()
		{
			for (var i = 0; i < 6; i++)
			{
				var pass = Allocate(out var mesh, out var material);
				Assert.That(OwnedResources().Length, Is.EqualTo(2));
				DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = i % 2 == 0 ? m_Skin : null);
				yield return null;
				Assert.That(mesh == null, Is.True, "session end releases the old Native Mesh");
				Assert.That(material == null, Is.True, "session end releases the old Native Material");
				Assert.That(OwnedResources(), Is.Empty);
				DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = m_Skin);
				Assert.That(MaterialMethod.Invoke(pass, null), Is.Null, "a released pass must not allocate for the new session");
				MeshMethod.Invoke(pass, new object[] { EmptyFrame(), null });
				Assert.That(MeshField.GetValue(pass), Is.Null);
			}
		}

		/// <summary>Replacing the root is the Play Mode boundary: it must release the World pass and its native resources.</summary>
		[UnityTest]
		public IEnumerator RootResetReleasesNativeResources()
		{
			Allocate(out var mesh, out var material);
			DexcRoot.Reset();
			yield return null;
			Assert.That(mesh == null && material == null, Is.True, "the root boundary releases resources before dropping the pass");
			Assert.That(DexcPlayerRenderer.IsRegisteredForTests, Is.False);
			DebuggerRuntime.Initialize(options => options.RuntimeUI.Skin = m_Skin);
			DexcPlayerRenderer.EnsureInitialized();
		}

		[UnityTest]
		public IEnumerator DriverDestructionReleasesNativeResources()
		{
			Allocate(out var mesh, out var material);
			var driver = Resources.FindObjectsOfTypeAll<DexcRuntimeUIDriver>().Single();
			Object.DestroyImmediate(driver.gameObject);
			yield return null;
			Assert.That(mesh == null && material == null, Is.True, "driver destruction releases the World pass");
			Assert.That(DexcPlayerRenderer.IsRegisteredForTests, Is.False);
			Assert.That(OwnedResources(), Is.Empty);
			DexcPlayerRenderer.EnsureInitialized();
			DexcRuntimeUIDriver.CreateForBootstrap();
		}

		private static DexcPlayerRenderPass Allocate(out Mesh mesh, out Material material)
		{
			var pass = Pass ?? new DexcPlayerRenderPass();
			Pass = pass;
			material = (Material)MaterialMethod.Invoke(pass, null);
			MeshMethod.Invoke(pass, new object[] { EmptyFrame(), null });
			mesh = (Mesh)MeshField.GetValue(pass);
			Assert.That(material != null && mesh != null, Is.True, "use the production Native allocation paths");
			material.name = ResourceName;
			mesh.name = ResourceName;
			return pass;
		}

		private static DebugDisplayFrameSnapshot EmptyFrame() => new DebugDisplayFrameSnapshot(1, 0, null, null, 0);

		private static Object[] OwnedResources() =>
			Resources
				.FindObjectsOfTypeAll<Mesh>()
				.Where(value => value.name == ResourceName)
				.Cast<Object>()
				.Concat(Resources.FindObjectsOfTypeAll<Material>().Where(value => value.name == ResourceName))
				.ToArray();
	}
}
#endif
