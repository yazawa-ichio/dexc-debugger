using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if DEXC_DEBUGGER && !DISABLE_DEXC_DEBUGGER
using Dexc;
#endif

namespace DexcDebugger.Verification
{
	public static class DexcRuntimeUISkinBuildDriver
	{
		const string FixtureFolder = "Assets/Tests/UI03BuildFixture";
		const string ScenePath = FixtureFolder + "/RuntimeUISkinBuild.unity";
		const string SkinPath = FixtureFolder + "/RuntimeUISkin.asset";
		const string TexturePath = FixtureFolder + "/RuntimeUITexture.asset";
		const string MaterialPath = FixtureFolder + "/RuntimeUIMaterial.mat";
		const string ShaderPath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader";
		const string PanelSettingsPath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitPanelSettings.asset";
		const string ThemePath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitTheme.tss";
		const string NotoSansJPPresetPath = "Assets/DexcDebugger/Font/DexcRuntimeUISkin-NotoSansJP.asset";
		const string NotoSansJPFontPath = "Assets/DexcDebugger/Font/NotoSansJP-Regular.ttf";
		const string BIZUDPGothicPath = "Assets/Tests/Font/BIZ_UDPGothic/BIZUDPGothic-Regular.ttf";
		const string NotoSansPath = "Assets/Tests/Font/Noto_Sans/NotoSans-Regular.ttf";
		const string DeletedUxmlPath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkit.uxml";
		const string DeletedUssPath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkit.uss";
		const string DeletedUxmlGuid = "e57db1f797935be458c73d9563f0eeb8";
		const string DeletedUssGuid = "a9d0ab5b0f365e6489bd2c6d97905fe5";

		public static void CreateFixtureAssets()
		{
#if DEXC_DEBUGGER && !DISABLE_DEXC_DEBUGGER
			try
			{
				AssetDatabase.DeleteAsset(FixtureFolder);
				EnsureFolder(FixtureFolder);
				var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
				if (shader == null)
					throw new InvalidOperationException("World Visual Shader is missing at " + ShaderPath + ".");
				var panelSettings = AssetDatabase.LoadMainAssetAtPath(PanelSettingsPath);
				var theme = AssetDatabase.LoadMainAssetAtPath(ThemePath);
				if (panelSettings == null)
					throw new InvalidOperationException("UI Toolkit PanelSettings is missing at " + PanelSettingsPath + ".");
				if (theme == null)
					throw new InvalidOperationException("UI Toolkit Theme is missing at " + ThemePath + ".");
				var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "UI02 Runtime UI Texture" };
				texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
				texture.Apply(false, false);
				AssetDatabase.CreateAsset(texture, TexturePath);
				var material = new Material(shader) { name = "UI02 Runtime UI Material" };
				material.mainTexture = texture;
				AssetDatabase.CreateAsset(material, MaterialPath);
				var skin = ScriptableObject.CreateInstance<DexcRuntimeUISkin>();
				var serialized = new SerializedObject(skin);
				serialized.FindProperty("m_Texture").objectReferenceValue = texture;
				serialized.FindProperty("m_Material").objectReferenceValue = material;
				serialized.FindProperty("m_WorldVisualShader").objectReferenceValue = shader;
				serialized.FindProperty("m_UIToolkitPanelSettingsTemplate").objectReferenceValue = panelSettings;
				serialized.ApplyModifiedPropertiesWithoutUndo();
				AssetDatabase.CreateAsset(skin, SkinPath);
				AssetDatabase.SaveAssets();
				var manifest = new FixtureManifest
				{
					Version = 3,
					Skin = Entry("Skin", SkinPath),
					Texture = Entry("Texture", TexturePath),
					Material = Entry("Material", MaterialPath),
					Shader = Entry("Shader", ShaderPath),
					PanelSettings = Entry("PanelSettings", PanelSettingsPath),
					Theme = Entry("Theme", ThemePath),
					NotoSansJPPreset = Entry("NotoSansJPPreset", NotoSansJPPresetPath),
					NotoSansJPFont = Entry("NotoSansJPFont", NotoSansJPFontPath),
					BIZUDPGothicTestOnly = Entry("BIZUDPGothicTestOnly", BIZUDPGothicPath),
					NotoSansTestOnly = Entry("NotoSansTestOnly", NotoSansPath),
					DeletedUxml = KnownEntry("DeletedUxml", DeletedUxmlPath, DeletedUxmlGuid),
					DeletedUss = KnownEntry("DeletedUss", DeletedUssPath, DeletedUssGuid),
					SkinDependencyGuids = DependencyGuids(SkinPath, true),
					SkinDirectDependencyGuids = DependencyGuids(SkinPath, false),
					PanelSettingsDependencyGuids = DependencyGuids(PanelSettingsPath, true),
					PanelSettingsDirectDependencyGuids = DependencyGuids(PanelSettingsPath, false),
					NotoSansJPPresetDependencyGuids = DependencyGuids(NotoSansJPPresetPath, true),
					NotoSansJPPresetDirectDependencyGuids = DependencyGuids(NotoSansJPPresetPath, false),
				};
				WriteJson(Required("DEXC_UI03_FIXTURE_MANIFEST"), manifest);
				EditorApplication.Exit(0);
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
				EditorApplication.Exit(1);
			}
#else
			Debug.LogError("CreateFixtureAssets requires DEXC_DEBUGGER without DISABLE_DEXC_DEBUGGER.");
			EditorApplication.Exit(1);
#endif
		}

		public static void BuildRow()
		{
			var row = Required("DEXC_UI03_MATRIX_ROW");
			var outputPath = Required("DEXC_UI03_BUILD_PATH");
			var reportPath = Required("DEXC_UI03_ROW_REPORT");
			var record = new RowRecord
			{
				Version = 3,
				Row = row,
				OutputPath = outputPath,
			};
			try
			{
				CreateFixtureScene();
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
				var report = BuildPipeline.BuildPlayer(
					new BuildPlayerOptions
					{
						scenes = new[] { ScenePath },
						locationPathName = outputPath,
						target = BuildTarget.StandaloneWindows64,
						options = BuildOptions.None,
					}
				);
				record.Result = report.summary.result.ToString();
				record.Succeeded = report.summary.result == BuildResult.Succeeded;
				record.TotalSize = report.summary.totalSize;
				record.SourceAssetGuids = report
					.packedAssets.SelectMany(packed => packed.contents)
					.Select(content => content.sourceAssetGUID.ToString())
					.Where(guid => !string.IsNullOrEmpty(guid))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.OrderBy(guid => guid, StringComparer.OrdinalIgnoreCase)
					.ToArray();
				record.AssetsUIToolkitGenerated = AssetDatabase.IsValidFolder("Assets/UI Toolkit");
			}
			catch (Exception exception)
			{
				record.Error = exception.GetType().FullName + ": " + exception.Message;
				record.Succeeded = false;
				Debug.LogException(exception);
			}
			WriteJson(reportPath, record);
			EditorApplication.Exit(record.Succeeded ? 0 : 1);
		}

		public static void CleanupFixtureAssets()
		{
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			AssetDatabase.DeleteAsset(FixtureFolder);
			if (AssetDatabase.IsValidFolder("Assets/UI Toolkit"))
				AssetDatabase.DeleteAsset("Assets/UI Toolkit");
			AssetDatabase.SaveAssets();
			EditorApplication.Exit(0);
		}

		static void CreateFixtureScene()
		{
			EnsureFolder(FixtureFolder);
			var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			var host = new GameObject("DexcRuntimeUISkinBuildFixture");
			var fixture = host.AddComponent<DexcRuntimeUISkinBuildFixture>();
#if DEXC_DEBUGGER && !DISABLE_DEXC_DEBUGGER
			var skin = AssetDatabase.LoadAssetAtPath<DexcRuntimeUISkin>(SkinPath);
			var preset = AssetDatabase.LoadAssetAtPath<DexcRuntimeUISkin>(NotoSansJPPresetPath);
			if (skin == null)
				throw new InvalidOperationException("Enabled row could not load the Runtime UI Skin fixture.");
			if (preset == null)
				throw new InvalidOperationException("Enabled row could not load the Noto Sans JP preset.");
			var serialized = new SerializedObject(fixture);
			serialized.FindProperty("m_Skin").objectReferenceValue = skin;
			serialized.FindProperty("m_NotoSansJPPreset").objectReferenceValue = preset;
			serialized.ApplyModifiedPropertiesWithoutUndo();
#endif
			if (!EditorSceneManager.SaveScene(scene, ScenePath))
				throw new InvalidOperationException("Could not save " + ScenePath + ".");
			AssetDatabase.SaveAssets();
		}

		static FixtureEntry Entry(string name, string path)
		{
			return new FixtureEntry
			{
				Name = name,
				Path = path,
				Guid = AssetDatabase.AssetPathToGUID(path),
			};
		}

		static FixtureEntry KnownEntry(string name, string path, string guid)
		{
			return new FixtureEntry
			{
				Name = name,
				Path = path,
				Guid = guid,
			};
		}

		static string[] DependencyGuids(string path, bool recursive)
		{
			return AssetDatabase
				.GetDependencies(path, recursive)
				.Select(AssetDatabase.AssetPathToGUID)
				.Where(guid => !string.IsNullOrEmpty(guid))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(guid => guid, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}

		static void EnsureFolder(string path)
		{
			var parts = path.Split('/');
			var current = parts[0];
			for (var index = 1; index < parts.Length; index++)
			{
				var next = current + "/" + parts[index];
				if (!AssetDatabase.IsValidFolder(next))
					AssetDatabase.CreateFolder(current, parts[index]);
				current = next;
			}
		}

		static string Required(string name)
		{
			var value = Environment.GetEnvironmentVariable(name);
			if (string.IsNullOrEmpty(value))
				throw new InvalidOperationException(name + " is required.");
			return value;
		}

		static void WriteJson(string path, object value)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
			File.WriteAllText(path, JsonUtility.ToJson(value, true));
		}

		[Serializable]
		sealed class FixtureManifest
		{
			public int Version;
			public FixtureEntry Skin;
			public FixtureEntry Texture;
			public FixtureEntry Material;
			public FixtureEntry Shader;
			public FixtureEntry PanelSettings;
			public FixtureEntry Theme;
			public FixtureEntry NotoSansJPPreset;
			public FixtureEntry NotoSansJPFont;
			public FixtureEntry BIZUDPGothicTestOnly;
			public FixtureEntry NotoSansTestOnly;
			public FixtureEntry DeletedUxml;
			public FixtureEntry DeletedUss;
			public string[] SkinDependencyGuids;
			public string[] SkinDirectDependencyGuids;
			public string[] PanelSettingsDependencyGuids;
			public string[] PanelSettingsDirectDependencyGuids;
			public string[] NotoSansJPPresetDependencyGuids;
			public string[] NotoSansJPPresetDirectDependencyGuids;
		}

		[Serializable]
		sealed class FixtureEntry
		{
			public string Name;
			public string Path;
			public string Guid;
		}

		[Serializable]
		sealed class RowRecord
		{
			public int Version;
			public string Row;
			public bool Succeeded;
			public string Result;
			public ulong TotalSize;
			public string OutputPath;
			public string[] SourceAssetGuids;
			public bool AssetsUIToolkitGenerated;
			public string Error;
		}
	}
}
