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
	/// GENERIC_MEMORY_SAVE_MANAGED names a folder that gets a copy of the assemblies as they are before any rewriting:
	/// the build removes the staging area when it ends, and tools/il2cpp-generic-memory/offline builds from the copy.
	/// </summary>
	sealed class GenericMemoryPreLink : IPostBuildPlayerScriptDLLs
	{
		public int callbackOrder => 100;

		public void OnPostBuildPlayerScriptDLLs(BuildReport report)
		{
			var save = Environment.GetEnvironmentVariable("GENERIC_MEMORY_SAVE_MANAGED");
			var variant = Environment.GetEnvironmentVariable("GENERIC_MEMORY_VARIANT");
			var rewrite = !string.IsNullOrEmpty(variant) && variant != "baseline";
			if (string.IsNullOrEmpty(save) && !rewrite)
			{
				return;
			}
			var folder = report.GetFiles().Where(file => file.role == "ManagedLibrary").Select(file => Path.GetDirectoryName(file.path)).Distinct().Single();
			if (!string.IsNullOrEmpty(save))
			{
				Save(folder, save);
			}
			if (rewrite)
			{
				var log = GenericMemoryWeaver.Run(folder, variant.Split('+'));
				Debug.Log("[generic-memory] " + variant + "\n" + log);
			}
		}

		static void Save(string folder, string destination)
		{
			if (Directory.Exists(destination))
			{
				// A saved set is what later builds are compared against, so one is never written over.
				throw new BuildFailedException("[generic-memory] " + destination + " exists already");
			}
			var count = 0;
			foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
			{
				var target = Path.Combine(destination, file.Substring(folder.Length).TrimStart('\\', '/'));
				Directory.CreateDirectory(Path.GetDirectoryName(target));
				File.Copy(file, target);
				count++;
			}
			Debug.Log("[generic-memory] saved " + count + " files of " + folder + " to " + destination);
		}
	}
}
