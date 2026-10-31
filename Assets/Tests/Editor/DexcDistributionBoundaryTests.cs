using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dexc;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;

namespace DexcDebugger.Tests
{
	public sealed class DexcDistributionBoundaryTests
	{
		[Test]
		public void PackageHasNoRequiredDependenciesAndGatesUrpBehindAVersionDefine()
		{
			var package = PackageInfo.FindForAssetPath("Packages/jp.yazawa-ichio.dexc-debugger/package.json");
			Assert.That(package, Is.Not.Null);
			Assert.That(
				package.dependencies,
				Is.Empty,
				"URP, Input System and uGUI are optional; nothing is force-installed into consumers"
			);
			// Display owns all three of its layers, so the URP gate sits on the Runtime assembly.
			var runtime = System.IO.File.ReadAllText("Packages/jp.yazawa-ichio.dexc-debugger/Runtime/Dexc.Runtime.asmdef");
			Assert.That(runtime, Does.Contain("\"define\": \"DEXC_HAS_URP\""));
			Assert.That(runtime, Does.Contain("com.unity.render-pipelines.universal"));
			var debugUI = System.IO.File.ReadAllText("Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Dexc.DebugUI.asmdef");
			Assert.That(debugUI, Does.Not.Contain("DEXC_HAS_URP"), "the menu assembly no longer renders World visuals");
		}

		[Test]
		public void VerificationScriptsHaveTheirOwnRuntimeAndEditorAssemblies()
		{
			Assert.That(
				CompilationPipeline.GetAssemblyNameFromScriptPath("Assets/Tests/Verification/DexcRuntimeUISkinBuildFixture.cs"),
				Is.EqualTo("DexcDebugger.Verification.Runtime.dll")
			);
			Assert.That(
				CompilationPipeline.GetAssemblyNameFromScriptPath("Assets/Tests/Verification/Editor/DexcSymbolCompileDriver.cs"),
				Is.EqualTo("DexcDebugger.Verification.Editor.dll")
			);
			Assert.That(
				CompilationPipeline.GetAssemblies(AssembliesType.Player).Any(item => item.name == "DexcDebugger.Verification.Editor"),
				Is.False
			);
		}

		/// <summary>
		/// The remote layers are kept apart by what each assembly is allowed to reference, not by anyone remembering to.
		/// The operations answer with plain objects and must not know which carrier is asking; the frame types must not
		/// be able to touch the engine, so that the protocol can be tested without Unity in the way.
		/// </summary>
		[Test]
		public void RemoteLayersCannotReachPastTheirOwnAssembly()
		{
			var protocol = System.IO.File.ReadAllText("Packages/jp.yazawa-ichio.dexc-debugger/Remote/Dexc.Remote.Protocol.asmdef");
			Assert.That(protocol, Does.Contain("\"noEngineReferences\": true"), "the frame types are testable without Unity");
			Assert.That(protocol, Does.Contain("\"references\": []"), "the frame types depend on nothing");

			var operations = System.IO.File.ReadAllText("Packages/jp.yazawa-ichio.dexc-debugger/Remote/Host/Dexc.Remote.asmdef");
			Assert.That(
				operations,
				Does.Not.Contain("Unity.Pipeline"),
				"the operations must not be able to see the Unity CLI, or a carrier would leak into what it carries"
			);

			var carrier = System.IO.File.ReadAllText("Packages/jp.yazawa-ichio.dexc-debugger/Remote/Pipeline/Dexc.Remote.Pipeline.asmdef");
			Assert.That(carrier, Does.Contain("Unity.Pipeline"));
			Assert.That(carrier, Does.Contain("\"define\": \"DEXC_HAS_PIPELINE\""), "no package, no carrier");
			// The package's own assembly only exists under its define constraints, and the version define only says the
			// package is installed. Without the same constraints here, a Player compiles the carrier against an assembly it
			// does not have. They are read from the package, so a package update that moves them fails here first.
			var pipeline = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName("Unity.Pipeline");
			Assert.That(pipeline, Is.Not.Null, "the Pipeline package is installed in this project");
			var expected = JsonUtility.FromJson<DefineConstraints>(System.IO.File.ReadAllText(pipeline)).defineConstraints;
			var actual = JsonUtility.FromJson<DefineConstraints>(carrier).defineConstraints;
			Assert.That(expected, Is.Not.Empty);
			Assert.That(actual, Is.SupersetOf(expected), "no Pipeline assembly in the build, no carrier");
		}

		/// <summary>Each remote layer is its own assembly, so a file cannot drift into the wrong one unnoticed.</summary>
		[Test]
		public void EachRemoteLayerIsItsOwnAssembly()
		{
			var expected = new[]
			{
				("Packages/jp.yazawa-ichio.dexc-debugger/Remote/DexcRemoteMessage.cs", "Dexc.Remote.Protocol.dll"),
				("Packages/jp.yazawa-ichio.dexc-debugger/Remote/Host/DexcRemoteOperations.cs", "Dexc.Remote.dll"),
				("Packages/jp.yazawa-ichio.dexc-debugger/Remote/Pipeline/DexcRemotePipelineCommands.cs", "Dexc.Remote.Pipeline.dll"),
			};
			foreach (var (path, assembly) in expected)
				Assert.That(CompilationPipeline.GetAssemblyNameFromScriptPath(path), Is.EqualTo(assembly), path);
		}

		[Test]
		public void PackageInternalsAreNotExposedToConsumerDefaultAssembly()
		{
			foreach (var assembly in new[] { typeof(Dexc.DexcDebugger).Assembly, typeof(DebugMenu).Assembly })
			{
				var friends = assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(item => item.AssemblyName).ToArray();
				Assert.That(friends, Does.Not.Contain("Assembly-CSharp"));
				Assert.That(friends, Does.Contain("DexcDebugger.Verification.Runtime"));
				Assert.That(friends, Does.Contain("DexcDebugger.Verification.Editor"));
			}
		}

		[System.Serializable]
		sealed class DefineConstraints
		{
			public string[] defineConstraints;
		}
	}
}
