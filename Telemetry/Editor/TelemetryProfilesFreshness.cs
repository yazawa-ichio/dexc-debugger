using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dexc.Telemetry.CodeGen;
using Mono.Cecil;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace Dexc.Telemetry.Editor
{
	/// <summary>
	/// Keeps the compiled assemblies in step with the presets. Changing a profile file starts no compilation by itself, so
	/// each assembly the watch processed keeps the hash of the presets it was made with, and this compares that hash with
	/// the files: when the Editor loads and whenever it comes back to the front.
	/// </summary>
	[InitializeOnLoad]
	internal static class TelemetryProfilesFreshness
	{
		static TelemetryProfilesFreshness()
		{
			EditorApplication.delayCall += Check;
			EditorApplication.focusChanged += focused =>
			{
				if (focused)
				{
					Check();
				}
			};
		}

		/// <summary>The hash of the profile files as they are now, or null when they cannot be read.</summary>
		internal static string CurrentHash(string projectFolder)
		{
			try
			{
				return TelemetryProfiles.Load(projectFolder).Hash;
			}
			catch (Exception exception) when (exception is FormatException || exception is IOException)
			{
				return null;
			}
		}

		/// <summary>The hashes the loaded assemblies were processed with.</summary>
		internal static IEnumerable<string> LoadedHashes()
		{
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (assembly.IsDynamic)
				{
					continue;
				}
				foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
				{
					if (metadata.Key == TelemetryWeaver.ProfilesMetadata)
					{
						yield return metadata.Value;
					}
				}
			}
		}

		/// <summary>Asks for a clean compilation when an assembly was made with other presets than the files hold now.</summary>
		internal static void Check()
		{
			var current = CurrentHash(Directory.GetCurrentDirectory());
			if (current == null || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
			{
				return;
			}
			if (LoadedHashes().Any(hash => hash != current))
			{
				Debug.Log("Dexc Telemetry: the presets of the watch changed, so the scripts are compiled again.");
				CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
			}
		}
	}

	/// <summary>
	/// Stops a Player build whose script assemblies were made with other presets than the files hold now. A Player build
	/// can reuse assemblies it compiled before, and a change of the files is not a reason for it to compile them again.
	/// </summary>
	internal sealed class TelemetryProfilesBuildCheck : IPostBuildPlayerScriptDLLs
	{
		public int callbackOrder => 0;

		public void OnPostBuildPlayerScriptDLLs(BuildReport report)
		{
			var folder = Directory.GetCurrentDirectory();
			var current = TelemetryProfilesFreshness.CurrentHash(folder);
			if (current == null)
			{
				return;
			}
			foreach (var path in report.GetFiles().Select(file => file.path).Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
			{
				var hash = HashOf(path);
				if (hash != null && hash != current)
				{
					throw new BuildFailedException(
						"Dexc Telemetry: " + Path.GetFileName(path) + " was compiled with other presets of the watch than the profile files hold now. Build again with Clean Build."
					);
				}
			}
		}

		/// <summary>The hash of the presets an assembly file was processed with, or null.</summary>
		internal static string HashOf(string path)
		{
			if (!File.Exists(path))
			{
				return null;
			}
			try
			{
				using (var assembly = AssemblyDefinition.ReadAssembly(path))
				{
					var metadata = assembly.CustomAttributes.FirstOrDefault(attribute =>
						attribute.AttributeType.FullName == typeof(AssemblyMetadataAttribute).FullName
						&& (string)attribute.ConstructorArguments[0].Value == TelemetryWeaver.ProfilesMetadata);
					return metadata == null ? null : (string)metadata.ConstructorArguments[1].Value;
				}
			}
			catch (BadImageFormatException)
			{
				return null;
			}
		}
	}
}
