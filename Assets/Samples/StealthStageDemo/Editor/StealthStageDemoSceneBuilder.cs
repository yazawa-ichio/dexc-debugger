using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DexcDebugger.Samples.StealthStageDemo.Editor
{
	/// <summary>
	/// Builds the demo's scenes through Editor APIs; their YAML is not maintained by hand. Dexc and debug-side types are
	/// reached by name, so this assembly needs only the gameplay reference.
	/// </summary>
	public static class StealthStageDemoSceneBuilder
	{
		const string MenuPath = "Tools/Dexc Debugger Samples/Rebuild Stealth Stage Demo Scenes";
		const string SceneFolder = "Assets/Samples/StealthStageDemo/Scenes";
		const string DemoScenePath = SceneFolder + "/StealthStageDemo.unity";
		const string BootScenePath = SceneFolder + "/StealthBoot.unity";
		const string BattleBootScenePath = SceneFolder + "/StealthBattleBoot.unity";

		[MenuItem(MenuPath)]
		public static void RebuildScenes()
		{
			BuildDemoScene();
			BuildBootScenes();
		}

		public static void BuildDemoScene()
		{
			var skin = CreateOrUpdateSkin(SceneFolder + "/StealthDemoRuntimeUISkin.asset");
			var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			var root = new GameObject("Stealth Stage Demo");
			var controller = root.AddComponent<StealthStageDemoController>();
			var systemType = Type.GetType(
				"DexcDebugger.Samples.StealthStageDemo.StealthDebugSystem, DexcDebugger.Samples.StealthStageDemo.Debug",
				true
			);
			var system = new GameObject("Stealth Debug System").AddComponent(systemType);
			var serialized = new SerializedObject(system);
			serialized.FindProperty("m_Skin").objectReferenceValue = skin;
			serialized.FindProperty("m_Game").objectReferenceValue = controller;
			serialized.ApplyModifiedPropertiesWithoutUndo();
			Save(scene, DemoScenePath);
		}

		/// <summary>One boot scene per start configuration, since a boot scene shows the set whose Tag it names.</summary>
		public static void BuildBootScenes()
		{
			BuildBootScene(BootScenePath, "stealth", "Stealth Stage Demo");
			BuildBootScene(BattleBootScenePath, "stealth-battle", "Stealth Stage Demo - Battle Check");
			AddToBuildSettings(BootScenePath, BattleBootScenePath, DemoScenePath);
		}

		static void BuildBootScene(string scenePath, string tag, string title)
		{
			var bootSceneType = Type.GetType("Dexc.DebugBootScene, Dexc.DebugUI", true);
			var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
			var boot = new GameObject("Debug Boot Scene").AddComponent(bootSceneType);
			var serialized = new SerializedObject(boot);
			serialized.FindProperty("m_Tag").stringValue = tag;
			serialized.FindProperty("m_Title").stringValue = title;
			serialized.ApplyModifiedPropertiesWithoutUndo();
			Save(scene, scenePath);
		}

		/// <summary>A boot set starts its scene by name, which only resolves for scenes in Build Settings.</summary>
		static void AddToBuildSettings(params string[] scenePaths)
		{
			var scenes = EditorBuildSettings.scenes.ToList();
			foreach (var path in scenePaths)
			{
				if (scenes.All(scene => scene.path != path))
					scenes.Add(new EditorBuildSettingsScene(path, true));
			}
			EditorBuildSettings.scenes = scenes.ToArray();
		}

		static void Save(UnityEngine.SceneManagement.Scene scene, string scenePath)
		{
			EditorSceneManager.MarkSceneDirty(scene);
			if (!EditorSceneManager.SaveScene(scene, scenePath))
				throw new InvalidOperationException("Unity failed to save " + scenePath + ".");
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
			Debug.Log("Generated " + scenePath);
		}

		static UnityEngine.Object CreateOrUpdateSkin(string assetPath)
		{
			var skinType = Type.GetType("Dexc.DexcRuntimeUISkin, Dexc.Runtime", false);
			if (skinType == null)
				return null;
			var skin = AssetDatabase.LoadAssetAtPath(assetPath, skinType) as ScriptableObject;
			if (skin == null)
			{
				skin = ScriptableObject.CreateInstance(skinType);
				AssetDatabase.CreateAsset(skin, assetPath);
			}

			var serialized = new SerializedObject(skin);
			var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader");
			var panelSettings = AssetDatabase.LoadMainAssetAtPath(
				"Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitPanelSettings.asset"
			);
			serialized.FindProperty("m_WorldVisualShader").objectReferenceValue = shader;
			serialized.FindProperty("m_UIToolkitPanelSettingsTemplate").objectReferenceValue = panelSettings;
			serialized.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(skin);
			return skin;
		}
	}
}
