using UnityEditor;
using UnityEngine;

namespace Dexc
{
	internal static class DexcRuntimeUISkinAsset
	{
		private const string WorldVisualShaderPath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader";
		private const string UIToolkitPanelSettingsPath =
			"Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitPanelSettings.asset";
		private const string NotoSansJPFontPath = "Assets/DexcDebugger/Font/NotoSansJP-Regular.ttf";
		private const string NotoSansJPPresetPath = "Assets/DexcDebugger/Font/DexcRuntimeUISkin-NotoSansJP.asset";

		[MenuItem("Assets/Create/Dexc/Runtime UI Skin", false, 201)]
		private static void Create()
		{
			var skin = CreateConfiguredSkin(null);
			var selectedPath = Selection.activeObject == null ? string.Empty : AssetDatabase.GetAssetPath(Selection.activeObject);
			var directory = AssetDatabase.IsValidFolder(selectedPath) ? selectedPath : "Assets";
			var path = AssetDatabase.GenerateUniqueAssetPath(directory + "/DexcRuntimeUISkin.asset");
			AssetDatabase.CreateAsset(skin, path);
			AssetDatabase.SaveAssets();
			Selection.activeObject = skin;
			EditorGUIUtility.PingObject(skin);
		}

		public static void GenerateNotoSansJPPreset()
		{
			var font = AssetDatabase.LoadAssetAtPath<Font>(NotoSansJPFontPath);
			if (font == null)
			{
				throw new System.InvalidOperationException("Noto Sans JP Regular is missing at " + NotoSansJPFontPath + ".");
			}
			if (AssetDatabase.LoadMainAssetAtPath(NotoSansJPPresetPath) != null)
			{
				throw new System.InvalidOperationException(
					"Refusing to replace the existing Noto Sans JP preset at " + NotoSansJPPresetPath + "."
				);
			}

			var skin = CreateConfiguredSkin(font);
			AssetDatabase.CreateAsset(skin, NotoSansJPPresetPath);
			AssetDatabase.SaveAssets();
		}

		private static DexcRuntimeUISkin CreateConfiguredSkin(Font font)
		{
			var shader = AssetDatabase.LoadAssetAtPath<Shader>(WorldVisualShaderPath);
			var panelSettingsTemplate = AssetDatabase.LoadMainAssetAtPath(UIToolkitPanelSettingsPath);
			if (shader == null)
			{
				throw new System.InvalidOperationException("World Visual Shader is missing at " + WorldVisualShaderPath + ".");
			}
			if (panelSettingsTemplate == null)
			{
				throw new System.InvalidOperationException("UI Toolkit PanelSettings is missing at " + UIToolkitPanelSettingsPath + ".");
			}

			var skin = ScriptableObject.CreateInstance<DexcRuntimeUISkin>();
			skin.MigrateSerializedPalette();
			skin.SetWorldVisualShaderForEditor(shader);
			skin.SetUIToolkitPanelSettingsTemplateForEditor(panelSettingsTemplate);
			if (font != null)
			{
				var serialized = new SerializedObject(skin);
				serialized.FindProperty("m_Font").objectReferenceValue = font;
				serialized.ApplyModifiedPropertiesWithoutUndo();
			}
			return skin;
		}
	}
}
