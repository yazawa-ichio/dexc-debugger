using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace DexcDebugger.Verification
{
	/// <summary>
	/// Keeps the Pipeline package's server, commands and Roslyn out of the autopilot Player. The interpreter assembly
	/// depends on none of them, and with them gone nothing in the Player opens a port or starts a process on its own.
	/// </summary>
	public sealed class DexcAutopilotAssemblyFilter : IFilterBuildAssemblies
	{
		public int callbackOrder => 0;

		public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
		{
			if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEXC_AUTOPILOT_BUILD_PATH")))
				return assemblies;
			return assemblies.Where(path => !Excluded(Path.GetFileName(path))).ToArray();
		}

		private static bool Excluded(string file) =>
			file == "Unity.Pipeline.dll"
			|| file == "Dexc.Remote.Pipeline.dll"
			|| file.StartsWith("UnityPipeline.Microsoft.CodeAnalysis", StringComparison.Ordinal);
	}
}
