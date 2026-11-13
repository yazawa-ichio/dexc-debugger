using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GenericMemory.Editor
{
	/// <summary>
	/// Builds the generic-memory bench as an Android Player (arm64, IL2CPP) from the open Editor, for running the checks
	/// on a device. It is <see cref="GenericMemoryBuild"/> for Android: the same scene, the same define and the same
	/// stripping level, and the il2cpp folder comes through UNITY_IL2CPP_PATH. The Player gets an application id of its
	/// own, so that it never takes the place of the project's own app on a device. Every setting that is changed for the
	/// build is put back, also one that had no entry before. Nothing is built unless Android is the active build target:
	/// this never moves the Editor to another platform.
	/// The last argument of <see cref="Run"/> is more arguments for il2cpp, which is how flags reach the C++ compiler and
	/// the linker of libil2cpp.so: "--compiler-flags=-O3 --linker-flags=-flto=thin". An option is given once for each
	/// flag (a value with a comma in it is one flag, not a list). They go through the Player settings
	/// (additionalIl2CppArgs), the only place the build reads them from, and what was there before is put back.
	/// </summary>
	public static class GenericMemoryAndroidBuild
	{
		const string ScenePath = "Assets/Tests/GenericMemory/Bench.unity";
		const string ScenarioDefine = "GENERIC_MEMORY_SCENARIO";

		/// <summary>Builds Logs/UnityTests/GenericMemory/&lt;label&gt;/GenericMemory.apk. Returns one line that says how it went.</summary>
		public static string Run(string variant, string il2cpp, string label, string applicationId, string stripping = "High", string defines = null, string il2cppArgs = null)
		{
			if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
			{
				return "[generic-memory] " + label + " not built: the active build target is " + EditorUserBuildSettings.activeBuildTarget;
			}
			var target = NamedBuildTarget.Android;
			// What the Windows bench sets and Android already has is only read, so that no entry is made for it.
			if (PlayerSettings.GetScriptingBackend(target) != ScriptingImplementation.IL2CPP
				|| PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64
				|| PlayerSettings.GetIl2CppCodeGeneration(target) != Il2CppCodeGeneration.OptimizeSpeed
				|| PlayerSettings.GetApiCompatibilityLevel(target) != ApiCompatibilityLevel.NET_Standard)
			{
				return "[generic-memory] " + label + " not built: Android is not IL2CPP, arm64 only, faster runtime and .NET Standard: "
					+ PlayerSettings.GetScriptingBackend(target) + ", " + PlayerSettings.Android.targetArchitectures + ", "
					+ PlayerSettings.GetIl2CppCodeGeneration(target) + ", " + PlayerSettings.GetApiCompatibilityLevel(target);
			}

			var output = Path.GetFullPath("Logs/UnityTests/GenericMemory/" + label + "/GenericMemory.apk");
			Directory.CreateDirectory(Path.GetDirectoryName(output));

			var settings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
			var before = EditorJsonUtility.ToJson(settings);
			var identifier = PlayerSettings.GetApplicationIdentifier(target);
			var product = PlayerSettings.productName;
			var level = PlayerSettings.GetManagedStrippingLevel(target);
			var hadLevel = Entry(settings, "managedStrippingLevel", "Android") >= 0;
			var hadBatching = Batching(settings, "Android") >= 0;
			var bundle = EditorUserBuildSettings.buildAppBundle;
			var export = EditorUserBuildSettings.exportAsGoogleAndroidProject;
			var additional = PlayerSettings.GetAdditionalIl2CppArgs();

			Environment.SetEnvironmentVariable("UNITY_IL2CPP_PATH", il2cpp);
			Environment.SetEnvironmentVariable("GENERIC_MEMORY_VARIANT", variant);
			string summary;
			try
			{
				PlayerSettings.SetApplicationIdentifier(target, applicationId);
				PlayerSettings.productName = "GenericMemory " + label;
				PlayerSettings.SetManagedStrippingLevel(target, (ManagedStrippingLevel)Enum.Parse(typeof(ManagedStrippingLevel), stripping));
				EditorUserBuildSettings.buildAppBundle = false;
				EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
				if (!string.IsNullOrEmpty(il2cppArgs))
				{
					PlayerSettings.SetAdditionalIl2CppArgs(string.IsNullOrEmpty(additional) ? il2cppArgs : additional + " " + il2cppArgs);
				}
				var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
				{
					scenes = new[] { ScenePath },
					locationPathName = output,
					target = BuildTarget.Android,
					targetGroup = BuildTargetGroup.Android,
					options = BuildOptions.None,
					extraScriptingDefines = string.IsNullOrEmpty(defines) ? new[] { ScenarioDefine } : (ScenarioDefine + ";" + defines).Split(';'),
				});
				summary = "[generic-memory] " + label + " " + report.summary.result + ", errors " + report.summary.totalErrors + ", " + report.summary.totalTime;
			}
			finally
			{
				// Left set, the folder would also be where the Editor looks for the tools of its own script compilation.
				Environment.SetEnvironmentVariable("UNITY_IL2CPP_PATH", null);
				Environment.SetEnvironmentVariable("GENERIC_MEMORY_VARIANT", null);
				EditorUserBuildSettings.buildAppBundle = bundle;
				EditorUserBuildSettings.exportAsGoogleAndroidProject = export;
				PlayerSettings.SetApplicationIdentifier(target, identifier);
				PlayerSettings.productName = product;
				if (!string.IsNullOrEmpty(il2cppArgs))
				{
					PlayerSettings.SetAdditionalIl2CppArgs(additional);
				}
				if (hadLevel)
				{
					PlayerSettings.SetManagedStrippingLevel(target, level);
				}
				else
				{
					Remove(settings, "managedStrippingLevel", "Android");
				}
				// The build itself writes the batching of the platform it builds for into the Player settings.
				if (!hadBatching)
				{
					RemoveBatching(settings, "Android");
				}
			}
			summary += EditorJsonUtility.ToJson(settings) == before ? ", settings as before" : ", SETTINGS DIFFER";
			Debug.Log(summary);
			return summary;
		}

		/// <summary>
		/// Changes the settings the way a build does and puts them back, without building. Says whether the Player
		/// settings are then what they were, which is what <see cref="Run"/> relies on.
		/// </summary>
		public static string SettingsRoundTrip(string applicationId, string stripping = "High")
		{
			var target = NamedBuildTarget.Android;
			var settings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
			var before = EditorJsonUtility.ToJson(settings);
			var identifier = PlayerSettings.GetApplicationIdentifier(target);
			var product = PlayerSettings.productName;
			var level = PlayerSettings.GetManagedStrippingLevel(target);
			var hadLevel = Entry(settings, "managedStrippingLevel", "Android") >= 0;
			PlayerSettings.SetApplicationIdentifier(target, applicationId);
			PlayerSettings.productName = "GenericMemory round trip";
			PlayerSettings.SetManagedStrippingLevel(target, (ManagedStrippingLevel)Enum.Parse(typeof(ManagedStrippingLevel), stripping));
			var changed = EditorJsonUtility.ToJson(settings) != before;
			var entryMade = Entry(settings, "managedStrippingLevel", "Android") >= 0;
			PlayerSettings.SetApplicationIdentifier(target, identifier);
			PlayerSettings.productName = product;
			if (hadLevel)
			{
				PlayerSettings.SetManagedStrippingLevel(target, level);
			}
			else
			{
				Remove(settings, "managedStrippingLevel", "Android");
			}
			return "had entry " + hadLevel + ", level " + level + ", changed " + changed + ", entry made " + entryMade
				+ ", as before " + (EditorJsonUtility.ToJson(settings) == before);
		}

		/// <summary>Where a per-platform setting of the Player settings has the entry of a platform, or -1.</summary>
		static int Entry(UnityEngine.Object settings, string map, string key)
		{
			var property = new SerializedObject(settings).FindProperty(map);
			for (var at = 0; property != null && at < property.arraySize; at++)
			{
				if (property.GetArrayElementAtIndex(at).FindPropertyRelative("first").stringValue == key)
				{
					return at;
				}
			}
			return -1;
		}

		/// <summary>Where the Player settings keep the batching of a platform, or -1. A build makes the entry of its platform.</summary>
		static int Batching(UnityEngine.Object settings, string platform)
		{
			var property = new SerializedObject(settings).FindProperty("m_BuildTargetBatching");
			for (var at = 0; property != null && at < property.arraySize; at++)
			{
				if (property.GetArrayElementAtIndex(at).FindPropertyRelative("m_BuildTarget").stringValue == platform)
				{
					return at;
				}
			}
			return -1;
		}

		/// <summary>Takes the batching entry of a platform out of the Player settings. Returns whether there was one.</summary>
		public static bool RemoveBatching(UnityEngine.Object settings, string platform)
		{
			var at = Batching(settings, platform);
			if (at < 0)
			{
				return false;
			}
			var serialized = new SerializedObject(settings);
			serialized.FindProperty("m_BuildTargetBatching").DeleteArrayElementAtIndex(at);
			serialized.ApplyModifiedPropertiesWithoutUndo();
			return true;
		}

		/// <summary>Takes the entry of a platform out of a per-platform setting: the API can set one, and cannot unset it.</summary>
		static void Remove(UnityEngine.Object settings, string map, string key)
		{
			var at = Entry(settings, map, key);
			if (at < 0)
			{
				return;
			}
			var serialized = new SerializedObject(settings);
			serialized.FindProperty(map).DeleteArrayElementAtIndex(at);
			serialized.ApplyModifiedPropertiesWithoutUndo();
		}
	}
}
