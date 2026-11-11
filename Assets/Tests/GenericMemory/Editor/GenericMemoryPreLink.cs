using System;
using System.IO;
using System.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GenericMemory.Editor
{
	/// <summary>
	/// Rewrites the assemblies of a Player build after the scripts are compiled and before the linker reads them. At that
	/// point Temp/StagingArea/Data/Managed holds every assembly of the build, the class libraries included, and the
	/// linker and il2cpp take what is there. GENERIC_MEMORY_VARIANT names what to apply; without it, or with "baseline", nothing is.
	/// </summary>
	sealed class GenericMemoryPreLink : IPostBuildPlayerScriptDLLs
	{
		public int callbackOrder => 100;

		public void OnPostBuildPlayerScriptDLLs(BuildReport report)
		{
			var variant = Environment.GetEnvironmentVariable("GENERIC_MEMORY_VARIANT");
			if (string.IsNullOrEmpty(variant) || variant == "baseline")
			{
				return;
			}
			var folder = report.GetFiles().Where(file => file.role == "ManagedLibrary").Select(file => Path.GetDirectoryName(file.path)).Distinct().Single();
			var log = GenericMemoryWeaver.Run(folder, variant.Split('+'));
			Debug.Log("[generic-memory] " + variant + "\n" + log);
		}
	}
}
