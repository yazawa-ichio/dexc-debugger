using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GenericMemory.Editor
{
	/// <summary>Builds the IL2CPP Player the generic-memory bench runs in, from the open Editor.</summary>
	public static class GenericMemoryBuild
	{
		const string ScenePath = "Assets/Tests/GenericMemory/Bench.unity";

		// The scenario's assembly asks for this symbol, and so does the code generate.py writes into it, which is not in Git.
		// Nothing else defines it: the Editor and every other Player are built without the scenario.
		const string ScenarioDefine = "GENERIC_MEMORY_SCENARIO";

		/// <summary>
		/// Builds one variant into Logs/UnityTests/GenericMemory/&lt;variant&gt;. The il2cpp folder is the copy that has the
		/// bench counters; the Editor's own folder is never used, so the Player always has them.
		/// </summary>
		public static string Run(string variant, string il2cpp, string stripping = "High", string label = null, string defines = null)
		{
			// label names the folder when the same variant is built more than once; defines are extra scripting defines, separated by ';'.
			var output = Path.GetFullPath("Logs/UnityTests/GenericMemory/" + (label ?? variant) + "/GenericMemory.exe");
			Directory.CreateDirectory(Path.GetDirectoryName(output));
			Environment.SetEnvironmentVariable("UNITY_IL2CPP_PATH", il2cpp);
			Environment.SetEnvironmentVariable("GENERIC_MEMORY_VARIANT", variant);

			var target = NamedBuildTarget.Standalone;
			PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
			PlayerSettings.SetApiCompatibilityLevel(target, ApiCompatibilityLevel.NET_Standard);
			PlayerSettings.SetManagedStrippingLevel(target, (ManagedStrippingLevel)Enum.Parse(typeof(ManagedStrippingLevel), stripping));
			PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSpeed);
			try
			{
				var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
				{
					scenes = new[] { ScenePath },
					locationPathName = output,
					target = BuildTarget.StandaloneWindows64,
					options = BuildOptions.None,
					extraScriptingDefines = string.IsNullOrEmpty(defines) ? new[] { ScenarioDefine } : (ScenarioDefine + ";" + defines).Split(';'),
				});
				var summary = "[generic-memory] " + (label ?? variant) + " " + report.summary.result + ", errors " + report.summary.totalErrors + ", " + report.summary.totalTime;
				Debug.Log(summary);
				return summary;
			}
			finally
			{
				// Left set, the folder would also be where the Editor looks for the tools of its own script compilation.
				Environment.SetEnvironmentVariable("UNITY_IL2CPP_PATH", null);
				Environment.SetEnvironmentVariable("GENERIC_MEMORY_VARIANT", null);
			}
		}
	}
}
