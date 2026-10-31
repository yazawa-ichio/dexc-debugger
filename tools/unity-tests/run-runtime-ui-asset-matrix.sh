#!/bin/sh
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_directory/common.sh"

project_argument=${DEXC_PROJECT_PATH:-$(pwd)}
project_path=$(unity_project_path "$project_argument")
unity_cli=$(unity_cli_path)
require_editor_closed "$project_path" "$unity_cli"
editor=$(unity_editor_path "$project_path" "$unity_cli")
log_directory=$(unity_log_directory "$project_path")
stamp=$(date +%Y%m%d-%H%M%S)
project_settings="$project_path/ProjectSettings/ProjectSettings.asset"
settings_backup="$log_directory/UI03-ProjectSettings-$stamp.asset.backup"
settings_backup_directory="$log_directory/UI03-SerializedSettingsBackup-$stamp"
volume_profile="$project_path/Assets/Settings/DefaultVolumeProfile.asset"
pc_pipeline_asset="$project_path/Assets/Settings/PC_RPAsset.asset"
mobile_pipeline_asset="$project_path/Assets/Settings/Mobile_RPAsset.asset"
pipeline_global_settings="$project_path/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset"
fixture_manifest="$log_directory/UI03-RuntimeUISkin-Fixture-$stamp.json"
enabled_report="$log_directory/UI03-RuntimeUISkin-Enabled-$stamp.json"
disabled_report="$log_directory/UI03-RuntimeUISkin-Disabled-$stamp.json"
excluded_report="$log_directory/UI03-RuntimeUISkin-AssemblyExcluded-$stamp.json"
matrix_report="$log_directory/UI03-RuntimeUISkin-AssetMatrix-$stamp.json"
fixture_folder="$project_path/Assets/Tests/UI03BuildFixture"
fixture_meta="$project_path/Assets/Tests/UI03BuildFixture.meta"
assets_ui_toolkit="$project_path/Assets/UI Toolkit"
python_command=$(python_executable)
[ ! -e "$fixture_folder" ] && [ ! -e "$fixture_meta" ] || { echo "UI03 build fixture already exists; refusing to replace it." >&2; exit 1; }
[ ! -e "$assets_ui_toolkit" ] || { echo "Assets/UI Toolkit already exists; UI03 requires an absent baseline." >&2; exit 1; }
cp "$project_settings" "$settings_backup"
mkdir -p "$settings_backup_directory"
cp "$volume_profile" "$settings_backup_directory/DefaultVolumeProfile.asset"
cp "$pc_pipeline_asset" "$settings_backup_directory/PC_RPAsset.asset"
cp "$mobile_pipeline_asset" "$settings_backup_directory/Mobile_RPAsset.asset"
cp "$pipeline_global_settings" "$settings_backup_directory/UniversalRenderPipelineGlobalSettings.asset"
restored=0
fixture_created=0

run_editor()
{
	runner_label=$1
	runner_method=$2
	runner_log="$log_directory/UI03-RuntimeUISkin-$runner_label-$stamp.log"
	(cd "$project_path" && UNITY_PROJECT_PATH=. "$editor" -batchmode -quit -nographics -projectPath "$(native_path "$project_path")" -executeMethod "$runner_method" -logFile "$(native_path "$runner_log")")
}

apply_symbols()
{
	symbol_label=$1
	symbol_values=$2
	DEXC_SYMBOLS_STANDALONE="$symbol_values" DEXC_SYMBOLS_ANDROID="$symbol_values" \
		run_editor "Symbols-$symbol_label" DexcDebugger.Verification.DexcSymbolCompileDriver.ApplyRequestedSymbols
}

restore_serialized_settings()
{
	cp "$settings_backup" "$project_settings"
	cp "$settings_backup_directory/DefaultVolumeProfile.asset" "$volume_profile"
	cp "$settings_backup_directory/PC_RPAsset.asset" "$pc_pipeline_asset"
	cp "$settings_backup_directory/Mobile_RPAsset.asset" "$mobile_pipeline_asset"
	cp "$settings_backup_directory/UniversalRenderPipelineGlobalSettings.asset" "$pipeline_global_settings"
}

cleanup()
{
	cleanup_exit=$?
	trap - EXIT
	set +e
	if [ "$fixture_created" -eq 1 ]; then
		run_editor Cleanup DexcDebugger.Verification.DexcRuntimeUISkinBuildDriver.CleanupFixtureAssets
	fi
	restore_serialized_settings
	if cmp -s "$settings_backup" "$project_settings"; then restored=1; else restored=0; fi
	set -e
	if [ "$cleanup_exit" -ne 0 ]; then exit "$cleanup_exit"; fi
}

trap cleanup EXIT

apply_symbols Enabled 'DEXC_DEBUGGER;DEXC_RUNTIME_UITOOLKIT'
fixture_created=1
DEXC_UI03_FIXTURE_MANIFEST="$(native_path "$fixture_manifest")" \
	run_editor CreateFixture DexcDebugger.Verification.DexcRuntimeUISkinBuildDriver.CreateFixtureAssets

build_row()
{
	row_label=$1
	row_symbols=$2
	row_report=$3
	apply_symbols "$row_label" "$row_symbols"
	row_build_path="$log_directory/UI03-RuntimeUISkin-$row_label-$stamp/DexcUI03.exe"
	DEXC_UI03_MATRIX_ROW="$row_label" \
	DEXC_UI03_BUILD_PATH="$(native_path "$row_build_path")" \
	DEXC_UI03_ROW_REPORT="$(native_path "$row_report")" \
		run_editor "Build-$row_label" DexcDebugger.Verification.DexcRuntimeUISkinBuildDriver.BuildRow
}

build_row Enabled 'DEXC_DEBUGGER;DEXC_RUNTIME_UITOOLKIT' "$enabled_report"
build_row Disabled DEXC_RUNTIME_UITOOLKIT "$disabled_report"
build_row AssemblyExcluded 'DISABLE_DEXC_DEBUGGER;DEXC_RUNTIME_UITOOLKIT' "$excluded_report"

run_editor Cleanup DexcDebugger.Verification.DexcRuntimeUISkinBuildDriver.CleanupFixtureAssets
fixture_created=0
restore_serialized_settings
if cmp -s "$settings_backup" "$project_settings"; then restored=1; else restored=0; fi

"$python_command" "$script_directory/verify-runtime-ui-asset-matrix.py" \
	"$(native_path "$fixture_manifest")" \
	"$(native_path "$enabled_report")" \
	"$(native_path "$disabled_report")" \
	"$(native_path "$excluded_report")" \
	"$(native_path "$settings_backup")" \
	"$(native_path "$settings_backup_directory")" \
	"$(native_path "$project_path")" \
	"$(native_path "$matrix_report")"

echo "Asset matrix: $matrix_report"
echo "ProjectSettings byte-for-byte restored: $restored"
