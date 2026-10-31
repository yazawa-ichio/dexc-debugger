using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DexcDebugger.Verification
{
	public static class DexcPlayerStrippingBuildDriver
	{
		public static void BuildI17Player()
		{
			var target = NamedBuildTarget.Standalone;
			var oldBackend = PlayerSettings.GetScriptingBackend(target);
			var oldStripping = PlayerSettings.GetManagedStrippingLevel(target);
			var outputPath = Environment.GetEnvironmentVariable("DEXC_I17_BUILD_PATH");
			var reportPath = Environment.GetEnvironmentVariable("DEXC_I17_BUILD_REPORT");
			if (string.IsNullOrEmpty(outputPath))
			{
				throw new InvalidOperationException("DEXC_I17_BUILD_PATH is required.");
			}
			if (string.IsNullOrEmpty(reportPath))
			{
				throw new InvalidOperationException("DEXC_I17_BUILD_REPORT is required.");
			}
			var record = new BuildRecord { OutputPath = outputPath };
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
				PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
				PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.High);
				AssetDatabase.SaveAssets();
				var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
				var report = BuildPipeline
					.BuildPlayer(
						new BuildPlayerOptions
						{
							scenes = scenes,
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
				record.Error = exception.ToString();
			}
			finally
			{
				record.RestorationError = DexcBuildDriverCompletion.Restore(
					() => PlayerSettings.SetScriptingBackend(target, oldBackend),
					() => PlayerSettings.SetManagedStrippingLevel(target, oldStripping),
					AssetDatabase.SaveAssets,
					() =>
					{
						if (
							PlayerSettings.GetScriptingBackend(target) != oldBackend
							|| PlayerSettings.GetManagedStrippingLevel(target) != oldStripping
						)
						{
							throw new InvalidOperationException("Build settings were not restored.");
						}
					}
				);
				record.SettingsRestored = string.IsNullOrEmpty(record.RestorationError);
				record.Succeeded &= record.SettingsRestored;
			}
			DexcBuildDriverCompletion.WriteReportAndExit(reportPath, record, record.Succeeded);
		}

		[Serializable]
		private sealed class BuildRecord
		{
			public bool Succeeded;
			public bool SettingsRestored;
			public string Error;
			public string RestorationError;
			public string Result;
			public ulong TotalSize;
			public string OutputPath;
			public string Backend;
			public string Stripping;
		}
	}
}
