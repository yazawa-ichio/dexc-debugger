using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace DexcDebugger.Tests.SymbolMatrix
{
	public sealed class DexcSymbolMatrixEditorTests
	{
		[Test]
		public void CompiledAssembliesMatchTheCurrentSymbolBoundary()
		{
			var names = CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(assembly => assembly.name).ToArray();
#if DISABLE_DEXC_DEBUGGER
			foreach (
				var name in new[]
				{
					"Dexc.Runtime",
					"Dexc.DebugUI",
					"Dexc.Editor",
					"DexcDebugger.Tests.Runtime",
					"DexcDebugger.Tests.Editor",
					"DexcDebugger.Tests.Symbols.Enabled",
					"DexcDebugger.Tests.Symbols.Disabled",
					"DexcDebugger.Tests.Symbols.AssemblyExcluded",
				}
			)
			{
				Assert.That(names, Does.Not.Contain(name), "DISABLE_DEXC_DEBUGGER must win, including when DEXC_DEBUGGER is also defined.");
			}
#else
			Assert.That(names, Does.Contain("Dexc.Runtime").And.Contain("Dexc.DebugUI").And.Contain("Dexc.Editor"));
#if DEXC_DEBUGGER
			Assert.That(names, Does.Contain("DexcDebugger.Tests.Symbols.Enabled").And.Not.Contain("DexcDebugger.Tests.Symbols.Disabled"));
#else
			Assert.That(names, Does.Contain("DexcDebugger.Tests.Symbols.Disabled").And.Not.Contain("DexcDebugger.Tests.Symbols.Enabled"));
#endif
#endif
		}

		[Test]
		public void VerificationDriverSurvivesAllStatesWithoutEnteringPlayerAssemblies()
		{
			Assert.That(
				CompilationPipeline.GetAssemblyNameFromScriptPath("Assets/Tests/Verification/Editor/DexcSymbolCompileDriver.cs"),
				Is.EqualTo("DexcDebugger.Verification.Editor.dll")
			);
			Assert.That(
				CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(assembly => assembly.name),
				Does.Contain("DexcDebugger.Verification.Editor")
			);
			Assert.That(
				CompilationPipeline.GetAssemblies(AssembliesType.Player).Select(assembly => assembly.name),
				Does.Not.Contain("DexcDebugger.Verification.Editor")
			);
		}
	}
}
