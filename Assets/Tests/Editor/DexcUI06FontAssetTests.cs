#if DEXC_DEBUGGER
using System;
using System.IO;
using System.Linq;
using Dexc;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace DexcDebugger.Tests
{
	[TestFixture]
	public sealed class DexcUI06FontAssetTests
	{
		const string PresetPath = "Assets/DexcDebugger/Font/DexcRuntimeUISkin-NotoSansJP.asset";
		const string NotoSansJPPath = "Assets/DexcDebugger/Font/NotoSansJP-Regular.ttf";
		const string BIZUDPGothicPath = "Assets/Tests/Font/BIZ_UDPGothic/BIZUDPGothic-Regular.ttf";
		const string NotoSansPath = "Assets/Tests/Font/Noto_Sans/NotoSans-Regular.ttf";
		const string ShaderPath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/Shaders/WorldVisual.shader";
		const string PanelSettingsPath = "Packages/jp.yazawa-ichio.dexc-debugger/DebugUI/DexcRuntimeUIToolkitPanelSettings.asset";
		const string Canonical =
			"表示/日本語連続文字列確認空白を含まない日本語の説明文がパネル幅で自然に折り返され最後まで表示されることを確認します保存先シーン参照パスシーン/配信地域/日本語ローカライズ確認用の非常に長い参照先選択した参照先は使用できません有効な参照先を選択してから実行してください";

		[Test]
		public void StaticRegularFontsImportWithSelectedFamiliesAndNoVariableTables()
		{
			AssertFont(NotoSansJPPath, "Noto Sans JP", "3681e3f570564744198d78ab85e1f3c3");
			AssertFont(BIZUDPGothicPath, "BIZ UDPGothic", "f8d1c230ca34ebb4492753db01aa8b78");
			AssertFont(NotoSansPath, "Noto Sans", "0580f4735c4bb7542a0a879bfbcf9846");

			var selected = AssetDatabase
				.FindAssets("t:Font", new[] { "Assets/DexcDebugger/Font", "Assets/Tests/Font" })
				.Select(AssetDatabase.GUIDToAssetPath)
				.Where(path => path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
				.OrderBy(path => path, StringComparer.Ordinal)
				.ToArray();
			Assert.That(
				selected,
				Is.EqualTo(new[] { NotoSansJPPath, BIZUDPGothicPath, NotoSansPath }.OrderBy(path => path, StringComparer.Ordinal))
			);
			Assert.That(selected, Has.All.Not.Contains("Variable"));
			Assert.That(selected, Has.All.Not.Contains("Italic"));
			Assert.That(selected, Has.All.Not.Contains("Bold"));
			Assert.That(selected, Has.All.Not.Contains("Condensed"));
		}

		[Test]
		public void PositiveFontsCoverCanonicalJapaneseAndNotoSansRemainsNegativeEvidence()
		{
			var characters = Canonical.Where(character => !char.IsWhiteSpace(character)).Distinct().ToArray();
			var notoSansJP = AssetDatabase.LoadAssetAtPath<Font>(NotoSansJPPath);
			var bizUDPGothic = AssetDatabase.LoadAssetAtPath<Font>(BIZUDPGothicPath);
			var notoSans = AssetDatabase.LoadAssetAtPath<Font>(NotoSansPath);
			Assert.That(SourceCovers(notoSansJP, characters), Is.True, "Noto Sans JP must cover every canonical character.");
			Assert.That(SourceCovers(bizUDPGothic, characters), Is.True, "BIZ UDPGothic must cover every canonical character.");
			Assert.That(
				SourceContains(notoSans, '日'),
				Is.False,
				"Noto Sans must remain negative evidence even if another fallback renders Japanese."
			);
			Assert.That(
				SourceCovers(notoSans, characters),
				Is.False,
				"Noto Sans itself must not be reported as the Japanese glyph source."
			);
		}

		[Test]
		public void NotoSansJPPresetUsesDefaultContractAndDirectPackageReferencesOnly()
		{
			var preset = AssetDatabase.LoadAssetAtPath<DexcRuntimeUISkin>(PresetPath);
			Assert.That(preset, Is.Not.Null);
			Assert.That(preset.Font, Is.SameAs(AssetDatabase.LoadAssetAtPath<Font>(NotoSansJPPath)));
			Assert.That(preset.WorldVisualShader, Is.SameAs(AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath)));
			Assert.That(preset.SnapshotUIToolkitPanelSettingsTemplate(), Is.SameAs(AssetDatabase.LoadMainAssetAtPath(PanelSettingsPath)));
			AssertProfile(preset.AndroidPortrait, new Vector2(366f, 640f), 12f, DexcRuntimeUIAlignment.Bottom, 48f, 48f);
			AssertProfile(preset.AndroidLandscape, new Vector2(480f, 366f), 12f, DexcRuntimeUIAlignment.LeftTop, 44f, 48f);
			AssertProfile(preset.Windows, new Vector2(480f, 640f), 12f, DexcRuntimeUIAlignment.LeftCenter, 40f, 40f);
			Assert.That(preset.PanelColor, Is.EqualTo(new Color(0.04f, 0.06f, 0.10f, 0.98f)));
			Assert.That(preset.PrimaryTextColor, Is.EqualTo(new Color(0.97f, 0.98f, 0.99f, 1f)));
			Assert.That(preset.SecondaryTextColor, Is.EqualTo(new Color(0.69f, 0.74f, 0.82f, 1f)));
			Assert.That(preset.SelectionColor, Is.EqualTo(new Color(0.12f, 0.25f, 0.43f, 1f)));
			Assert.That(preset.FocusColor, Is.EqualTo(new Color(0.38f, 0.65f, 0.98f, 1f)));
			Assert.That(preset.SuccessColor, Is.EqualTo(new Color(0.29f, 0.87f, 0.50f, 1f)));
			Assert.That(preset.WarningColor, Is.EqualTo(new Color(0.98f, 0.75f, 0.14f, 1f)));
			Assert.That(preset.DisabledColor, Is.EqualTo(new Color(0.16f, 0.19f, 0.24f, 1f)));

			var direct = AssetDatabase.GetDependencies(PresetPath, false);
			Assert.That(direct, Does.Contain(NotoSansJPPath));
			Assert.That(direct, Does.Contain(ShaderPath));
			Assert.That(direct, Does.Contain(PanelSettingsPath));
			Assert.That(PresetPath, Does.Not.Contain("/Resources/"));
			Assert.That(PlayerSettings.GetPreloadedAssets(), Has.None.SameAs(preset));
			// The boot and Boot Check scenes are debug entry points that reference the preset on purpose (their Skin
			// fields exist only when DEXC_DEBUGGER is defined, so disabled builds drop the reference).
			var debugScenes = new[] { "Assets/Scenes/DebugBootScene.unity", "Assets/Scenes/BootCheck.unity" };
			foreach (var scene in EditorBuildSettings.scenes.Where(scene => scene.enabled && !debugScenes.Contains(scene.path)))
			{
				Assert.That(AssetDatabase.GetDependencies(scene.path, true), Does.Not.Contain(PresetPath), scene.path);
			}
		}

		private static void AssertFont(string path, string family, string expectedGuid)
		{
			var font = AssetDatabase.LoadAssetAtPath<Font>(path);
			Assert.That(font, Is.Not.Null, path);
			Assert.That(font.fontNames, Does.Contain(family), path);
			Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(expectedGuid));
			Assert.That(HasTrueTypeTable(path, "fvar"), Is.False, path + " must be a static font.");
		}

		private static bool HasTrueTypeTable(string path, string expected)
		{
			using (var stream = File.OpenRead(path))
			using (var reader = new BinaryReader(stream))
			{
				stream.Position = 4;
				var count = ReadUInt16BigEndian(reader);
				stream.Position = 12;
				for (var index = 0; index < count; index++)
				{
					var tag = new string(reader.ReadChars(4));
					stream.Position += 12;
					if (tag == expected)
						return true;
				}
			}
			return false;
		}

		private static ushort ReadUInt16BigEndian(BinaryReader reader)
		{
			var bytes = reader.ReadBytes(2);
			return (ushort)((bytes[0] << 8) | bytes[1]);
		}

		private static bool SourceCovers(Font font, char[] characters)
		{
			return characters.All(character => SourceContains(font, character));
		}

		private static bool SourceContains(Font font, char character)
		{
			Assert.That(FontEngine.LoadFontFace(font), Is.EqualTo(FontEngineError.Success), font.name);
			return FontEngine.TryGetGlyphWithUnicodeValue(character, GlyphLoadFlags.LOAD_DEFAULT, out _);
		}

		private static void AssertProfile(
			DexcRuntimeUISkin.LayoutProfile profile,
			Vector2 size,
			float margin,
			DexcRuntimeUIAlignment alignment,
			float row,
			float hit
		)
		{
			Assert.That(profile.PanelSize, Is.EqualTo(size));
			Assert.That(profile.SafeAreaMargin, Is.EqualTo(margin));
			Assert.That(profile.Alignment, Is.EqualTo(alignment));
			Assert.That(profile.RowHeight, Is.EqualTo(row));
			Assert.That(profile.MinimumHitHeight, Is.EqualTo(hit));
		}
	}
}
#endif
