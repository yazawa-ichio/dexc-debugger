using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DexcDebugger.Verification
{
	/// <summary>
	/// Builds the IL2CPP Player the autopilot verification loads a bot into. The stage scene is built as it is, so the game
	/// that runs is the game a player gets. Whatever the project had is put back afterwards.
	///
	/// With both VMs, only the Pipeline package's interpreter goes in. Its symbol has to be defined for that assembly to compile
	/// at all, and it brings the rest of the package with it, so <see cref="DexcAutopilotAssemblyFilter"/> takes that out again.
	/// </summary>
	public static class DexcAutopilotPlayerBuildDriver
	{
		public const string WithoutAutopilot = "none";
		public const string WithBothVms = "both";
		const string WithILRuntime = "ilruntime";

		/// <summary>none is the game alone, to measure against. ilruntime is the normal build.</summary>
		public static string Configuration
		{
			get
			{
				var value = Environment.GetEnvironmentVariable("DEXC_AUTOPILOT_CONFIGURATION");
				return string.IsNullOrEmpty(value) ? WithILRuntime : value;
			}
		}

		static string Symbols
		{
			get
			{
				if (Configuration == WithoutAutopilot)
					return string.Empty;
				return Configuration == WithBothVms ? "ENABLE_RUNTIME_PIPELINE;DEXC_AUTOPILOT" : "DEXC_AUTOPILOT";
			}
		}

		const string Scene = "Assets/Samples/StealthStageDemo/Scenes/StealthStageDemo.unity";
		const string ShaderCarrier = "Assets/Resources/DexcAutopilotShaderCarrier.mat";

		public static void BuildAutopilotPlayer()
		{
			var target = NamedBuildTarget.Standalone;
			var defines = PlayerSettings.GetScriptingDefineSymbols(target);
			var wasDevelopment = EditorUserBuildSettings.development;
			var backend = PlayerSettings.GetScriptingBackend(target);
			var stripping = PlayerSettings.GetManagedStrippingLevel(target);
			var outputPath = Environment.GetEnvironmentVariable("DEXC_AUTOPILOT_BUILD_PATH");
			var reportPath = Environment.GetEnvironmentVariable("DEXC_AUTOPILOT_BUILD_REPORT");
			if (string.IsNullOrEmpty(outputPath))
				throw new InvalidOperationException("DEXC_AUTOPILOT_BUILD_PATH is required.");
			if (string.IsNullOrEmpty(reportPath))
				throw new InvalidOperationException("DEXC_AUTOPILOT_BUILD_REPORT is required.");
			var level = Environment.GetEnvironmentVariable("DEXC_AUTOPILOT_STRIPPING");
			var requested = string.IsNullOrEmpty(level)
				? ManagedStrippingLevel.High
				: (ManagedStrippingLevel)Enum.Parse(typeof(ManagedStrippingLevel), level);

			var record = new BuildRecord { OutputPath = outputPath };
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
				PlayerSettings.SetScriptingDefineSymbols(target, defines + ";" + Symbols);
				EditorUserBuildSettings.development = false;
				PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
				PlayerSettings.SetManagedStrippingLevel(target, requested);
				// The stage makes its materials at runtime with Shader.Find, and no asset in the scene names the shader, so
				// a Player would not contain it. A material in Resources brings it along for this build only.
				Directory.CreateDirectory("Assets/Resources");
				AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Lit")), ShaderCarrier);
				AssetDatabase.SaveAssets();
				var report = BuildPipeline
					.BuildPlayer(
						new BuildPlayerOptions
						{
							scenes = new[] { Scene },
							locationPathName = outputPath,
							target = BuildTarget.StandaloneWindows64,
							options = BuildOptions.None,
						}
					)
					.summary;
				record = new BuildRecord
				{
					Succeeded = report.result == BuildResult.Succeeded,
					Result = report.result.ToString(),
					TotalSize = report.totalSize,
					OutputPath = outputPath,
					Backend = PlayerSettings.GetScriptingBackend(target).ToString(),
					Stripping = PlayerSettings.GetManagedStrippingLevel(target).ToString(),
				};
			}
			catch (Exception exception)
			{
				record.Succeeded = false;
				record.Result = exception.GetType().Name;
				record.Message = exception.Message;
				Debug.LogException(exception);
			}
			finally
			{
				AssetDatabase.DeleteAsset(ShaderCarrier);
				EditorUserBuildSettings.development = wasDevelopment;
				PlayerSettings.SetScriptingBackend(target, backend);
				PlayerSettings.SetManagedStrippingLevel(target, stripping);
				PlayerSettings.SetScriptingDefineSymbols(target, defines);
				File.WriteAllText(reportPath, JsonUtility.ToJson(record, true));
			}
			EditorApplication.Exit(record.Succeeded ? 0 : 1);
		}

		[Serializable]
		private struct BuildRecord
		{
			public bool Succeeded;
			public string Result;
			public ulong TotalSize;
			public string OutputPath;
			public string Backend;
			public string Stripping;
			public string Message;
		}
	}
}
