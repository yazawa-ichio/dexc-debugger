using System;
using System.IO;
using Unity.Pipeline.Config;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DexcDebugger.Verification
{
	/// <summary>
	/// Builds the Player that the remote verification drives from outside.
	///
	/// A development build with ENABLE_RUNTIME_PIPELINE_DEV, because the Pipeline package compiles its runtime server only
	/// into a build that defines that symbol, and the symbol only into one that defines ENABLE_PROFILER - which a
	/// development build does. Mono rather than IL2CPP: what is being checked here is that a tool outside the process can
	/// reach the game, and the IL2CPP and stripping question already has its own verification.
	/// </summary>
	public static class DexcRemotePlayerBuildDriver
	{
		const string PipelineSymbol = "ENABLE_RUNTIME_PIPELINE_DEV";

		public static void BuildRemotePlayer()
		{
			var target = NamedBuildTarget.Standalone;
			// The Pipeline package boots its own driver in a Player from its runtime settings and the symbol. Whatever the
			// project had is put back afterwards: a build for a check should not leave a game with a port open on it.
			var settingsBackup = RuntimePipelineConfig.BackUpSettingsFileForTesting();
			var defines = PlayerSettings.GetScriptingDefineSymbols(target);
			var wasDevelopment = EditorUserBuildSettings.development;
			const string temporaryScene = "Assets/DexcRemotePlayerTemporary.unity";
			var outputPath = Environment.GetEnvironmentVariable("DEXC_I19_BUILD_PATH");
			var reportPath = Environment.GetEnvironmentVariable("DEXC_I19_BUILD_REPORT");
			if (string.IsNullOrEmpty(outputPath))
				throw new InvalidOperationException("DEXC_I19_BUILD_PATH is required.");
			if (string.IsNullOrEmpty(reportPath))
				throw new InvalidOperationException("DEXC_I19_BUILD_REPORT is required.");

			var record = new BuildRecord { OutputPath = outputPath };
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
				var config = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
				config.autoStart = true;
				config.port = 0;
				config.Save();
				PlayerSettings.SetScriptingDefineSymbols(target, defines + ";" + PipelineSymbol);
				EditorUserBuildSettings.development = true;
				PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
				PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Disabled);
				AssetDatabase.SaveAssets();
				// One empty scene of its own, rather than whatever Build Settings holds. A Player needs at least one scene,
				// and the project's own scenes start a Dexc session - which is the one thing this build has to arrive
				// without, since what it checks is that a session can be started from outside. The fixture puts itself in
				// through RuntimeInitializeOnLoadMethod and needs nothing from the scene.
				var empty = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
				EditorSceneManager.SaveScene(empty, temporaryScene);
				var scenes = new[] { temporaryScene };
				var report = BuildPipeline
					.BuildPlayer(
						new BuildPlayerOptions
						{
							scenes = scenes,
							locationPathName = outputPath,
							target = BuildTarget.StandaloneWindows64,
							options = BuildOptions.Development,
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
					SceneCount = scenes.Length,
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
				AssetDatabase.DeleteAsset(temporaryScene);
				EditorUserBuildSettings.development = wasDevelopment;
				PlayerSettings.SetScriptingDefineSymbols(target, defines);
				RuntimePipelineConfig.RestoreSettingsFileForTesting(settingsBackup);
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
			public int SceneCount;
			public string Message;
		}
	}
}
