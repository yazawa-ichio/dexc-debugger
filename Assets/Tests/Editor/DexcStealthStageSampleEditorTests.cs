using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace DexcDebugger.Tests
{
	[TestFixture]
	public sealed class DexcStealthStageSampleEditorTests
	{
		const string SampleRoot = "Assets/Samples/StealthStageDemo";

		[Test]
		public void GameplayUsesDexcDirectlyAndTheDebugSideCompilesInEveryBuild()
		{
			var gameplay = JsonUtility.FromJson<AssemblyDefinition>(
				File.ReadAllText(SampleRoot + "/Runtime/DexcDebugger.Samples.StealthStageDemo.Gameplay.asmdef")
			);
			var debug = JsonUtility.FromJson<AssemblyDefinition>(
				File.ReadAllText(SampleRoot + "/Debug/DexcDebugger.Samples.StealthStageDemo.Debug.asmdef")
			);
			Assert.That(gameplay.name, Is.EqualTo("DexcDebugger.Samples.StealthStageDemo.Gameplay"));
			Assert.That(gameplay.references, Does.Contain("Dexc.Runtime").And.Not.Contain("Dexc.DebugUI"));
			Assert.That(gameplay.references, Does.Contain("Unity.InputSystem").And.Contain("Unity.ugui"));
			Assert.That(gameplay.defineConstraints, Does.Contain("!DISABLE_DEXC_DEBUGGER"));
			// The scene holds StealthDebugSystem, so the debug side must exist in builds without DEXC_DEBUGGER too.
			Assert.That(debug.defineConstraints, Does.Contain("!DISABLE_DEXC_DEBUGGER").And.Not.Contain("DEXC_DEBUGGER"));
			Assert.That(debug.references, Does.Contain("Dexc.Runtime").And.Contain("Dexc.DebugUI"));
			var compiled = CompilationPipeline.GetAssemblies().Single(item => item.name == gameplay.name);
			Assert.That(
				compiled.assemblyReferences.Select(item => item.name),
				Does.Contain("Dexc.Runtime").And.Contain("Unity.InputSystem").And.Contain("UnityEngine.UI")
			);
		}

		[Test]
		public void SceneAndSkinImport()
		{
			Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(SampleRoot + "/Scenes/StealthStageDemo.unity"), Is.Not.Null);
			Assert.That(
				AssetDatabase.LoadAssetAtPath<Dexc.DexcRuntimeUISkin>(SampleRoot + "/Scenes/StealthDemoRuntimeUISkin.asset"),
				Is.Not.Null
			);
		}

		[Test]
		public void SampleUsesOnlyPrimitiveGameplayAndRepositoryAssets()
		{
			var files = Directory
				.GetFiles(SampleRoot, "*", SearchOption.AllDirectories)
				.Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
				.ToArray();
			Assert.That(
				files.Any(path =>
					path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
					|| path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
					|| path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
					|| path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
				),
				Is.False
			);
			var controller = File.ReadAllText(SampleRoot + "/Runtime/StealthStageDemoController.cs");
			Assert.That(controller, Does.Contain("GameObject.CreatePrimitive"));
			Assert.That(controller, Does.Contain("UnityEngine.UI.CanvasScaler"));
		}

		[Serializable]
		sealed class AssemblyDefinition
		{
			public string name;
			public string[] references;
			public string[] defineConstraints;
		}
	}
}
