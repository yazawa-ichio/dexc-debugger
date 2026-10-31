#!/usr/bin/env python3
"""Verify UI03 Standalone BuildReport asset GUID inclusion and restoration."""

import hashlib
import json
import sys
from pathlib import Path


def fail(reason):
    print(json.dumps({"success": False, "reason": reason}))
    return 1


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def main():
    if len(sys.argv) != 9:
        return fail("arguments-required")
    fixture_path, enabled_path, disabled_path, excluded_path, settings_backup, serialized_backup, project, output_path = sys.argv[1:]
    try:
        fixture = load(fixture_path)
        rows = {
            "Enabled": load(enabled_path),
            "Disabled": load(disabled_path),
            "AssemblyExcluded": load(excluded_path),
        }
    except (OSError, json.JSONDecodeError) as error:
        return fail(f"artifact-invalid:{error}")
    if fixture.get("Version") != 3:
        return fail("fixture-version-invalid")
    assets = {name: fixture.get(name) or {} for name in ("Skin", "Texture", "Material", "Shader", "PanelSettings", "Theme", "NotoSansJPPreset", "NotoSansJPFont")}
    test_only = {name: fixture.get(name) or {} for name in ("BIZUDPGothicTestOnly", "NotoSansTestOnly")}
    deleted = {name: fixture.get(name) or {} for name in ("DeletedUxml", "DeletedUss")}
    expected_guids = {entry.get("Guid", "").lower() for entry in assets.values()}
    deleted_guids = {entry.get("Guid", "").lower() for entry in deleted.values()}
    test_only_guids = {entry.get("Guid", "").lower() for entry in test_only.values()}
    if len(expected_guids) != 8 or "" in expected_guids or len(test_only_guids) != 2 or "" in test_only_guids or len(deleted_guids) != 2 or "" in deleted_guids:
        return fail("fixture-guids-invalid")
    skin_direct = {value.lower() for value in fixture.get("SkinDirectDependencyGuids") or []}
    skin_dependencies = {value.lower() for value in fixture.get("SkinDependencyGuids") or []}
    panel_direct = {value.lower() for value in fixture.get("PanelSettingsDirectDependencyGuids") or []}
    panel_dependencies = {value.lower() for value in fixture.get("PanelSettingsDependencyGuids") or []}
    preset_direct = {value.lower() for value in fixture.get("NotoSansJPPresetDirectDependencyGuids") or []}
    preset_dependencies = {value.lower() for value in fixture.get("NotoSansJPPresetDependencyGuids") or []}
    panel_guid = assets["PanelSettings"]["Guid"].lower()
    theme_guid = assets["Theme"]["Guid"].lower()
    font_guid = assets["NotoSansJPFont"]["Guid"].lower()
    preset_panel_guid = assets["PanelSettings"]["Guid"].lower()
    shader_guid = assets["Shader"]["Guid"].lower()
    if (panel_guid not in skin_direct or theme_guid not in panel_direct or theme_guid not in skin_dependencies or theme_guid not in panel_dependencies
            or font_guid not in preset_direct or preset_panel_guid not in preset_direct or shader_guid not in preset_direct
            or font_guid not in preset_dependencies or theme_guid not in preset_dependencies):
        return fail("skin-panel-theme-dependency-chain-invalid")
    matrix = []
    for name, row in rows.items():
        if row.get("Version") != 3 or row.get("Row") != name or row.get("Succeeded") is not True:
            return fail(f"build-row-invalid:{name}")
        if row.get("AssetsUIToolkitGenerated") is not False:
            return fail(f"assets-ui-toolkit-generated:{name}")
        packed = {value.lower() for value in row.get("SourceAssetGuids") or []}
        included = {asset: entry.get("Guid", "").lower() in packed for asset, entry in assets.items()}
        deleted_included = {asset: entry.get("Guid", "").lower() in packed for asset, entry in deleted.items()}
        test_only_included = {asset: entry.get("Guid", "").lower() in packed for asset, entry in test_only.items()}
        expected = name == "Enabled"
        if any(value != expected for value in included.values()):
            return fail(f"asset-inclusion-invalid:{name}:{included}")
        if any(deleted_included.values()):
            return fail(f"deleted-asset-included:{name}:{deleted_included}")
        if any(test_only_included.values()):
            return fail(f"test-only-font-included:{name}:{test_only_included}")
        matrix.append({
            "Row": name,
            "ExpectedIncluded": expected,
            "Included": included,
            "DeletedAssetIncluded": deleted_included,
            "TestOnlyFontIncluded": test_only_included,
            "AssetsUIToolkitGenerated": False,
            "BuildReport": str(Path({"Enabled": enabled_path, "Disabled": disabled_path, "AssemblyExcluded": excluded_path}[name]).resolve()),
        })
    project_path = Path(project)
    restoration_pairs = {
        "ProjectSettings": (Path(settings_backup), project_path / "ProjectSettings/ProjectSettings.asset"),
        "DefaultVolumeProfile": (Path(serialized_backup) / "DefaultVolumeProfile.asset", project_path / "Assets/Settings/DefaultVolumeProfile.asset"),
        "PC_RPAsset": (Path(serialized_backup) / "PC_RPAsset.asset", project_path / "Assets/Settings/PC_RPAsset.asset"),
        "Mobile_RPAsset": (Path(serialized_backup) / "Mobile_RPAsset.asset", project_path / "Assets/Settings/Mobile_RPAsset.asset"),
        "UniversalRenderPipelineGlobalSettings": (Path(serialized_backup) / "UniversalRenderPipelineGlobalSettings.asset", project_path / "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset"),
    }
    restored_files = {}
    for name, (before_path, after_path) in restoration_pairs.items():
        if not before_path.is_file() or not after_path.is_file():
            return fail(f"restoration-file-missing:{name}")
        before_hash = hashlib.sha256(before_path.read_bytes()).hexdigest()
        after_hash = hashlib.sha256(after_path.read_bytes()).hexdigest()
        restored_files[name] = {"BeforeSha256": before_hash, "AfterSha256": after_hash, "ByteForByteRestored": before_hash == after_hash}
        if before_hash != after_hash:
            return fail(f"serialized-setting-not-restored:{name}:{after_hash}")
    fixture_absent_after = not (project_path / "Assets/Tests/UI03BuildFixture").exists() and not (project_path / "Assets/Tests/UI03BuildFixture.meta").exists()
    assets_ui_toolkit_absent_after = not (project_path / "Assets/UI Toolkit").exists()
    if not fixture_absent_after:
        return fail("fixture-not-restored")
    if not assets_ui_toolkit_absent_after:
        return fail("assets-ui-toolkit-not-restored")
    record = {
        "Version": 3,
        "Success": True,
        "Project": str(project_path.resolve()),
        "FixtureManifest": str(Path(fixture_path).resolve()),
        "Assets": assets,
        "DeletedAssets": deleted,
        "TestOnlyFonts": test_only,
        "DependencyChain": {
            "SkinToPanelSettingsDirect": True,
            "PanelSettingsToThemeDirect": True,
            "SkinGuid": assets["Skin"]["Guid"],
            "PanelSettingsGuid": assets["PanelSettings"]["Guid"],
            "ThemeGuid": assets["Theme"]["Guid"],
            "NotoSansJPPresetGuid": assets["NotoSansJPPreset"]["Guid"],
            "NotoSansJPFontGuid": assets["NotoSansJPFont"]["Guid"],
            "SkinDirectDependencyGuids": sorted(skin_direct),
            "PanelSettingsDirectDependencyGuids": sorted(panel_direct),
            "NotoSansJPPresetDirectDependencyGuids": sorted(preset_direct),
        },
        "Rows": matrix,
        "RestoredFiles": restored_files,
        "ProjectSettingsByteForByteRestored": restored_files["ProjectSettings"]["ByteForByteRestored"],
        "SerializedSettingsByteForByteRestored": all(value["ByteForByteRestored"] for value in restored_files.values()),
        "FixtureStateBefore": "Absent",
        "FixtureStateAfter": "Absent" if fixture_absent_after else "Present",
        "FixtureByteForByteRestored": fixture_absent_after,
        "AssetsUIToolkitStateBefore": "Absent",
        "AssetsUIToolkitStateAfter": "Absent" if assets_ui_toolkit_absent_after else "Present",
        "AssetsUIToolkitGenerated": False,
    }
    output = Path(output_path)
    output.write_text(json.dumps(record, indent=2), encoding="utf-8")
    print(json.dumps(record, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
