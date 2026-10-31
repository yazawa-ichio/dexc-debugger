#!/bin/sh
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_directory/common.sh"

project_argument=${DEXC_PROJECT_PATH:-$(pwd)}
project_path=$(unity_project_path "$project_argument")
unity_cli=$(unity_cli_path)
require_editor_closed "$project_path" "$unity_cli"
editor=$(unity_editor_path "$project_path" "$unity_cli")
version=$(unity_editor_version "$project_path")
log_directory=$(unity_log_directory "$project_path")
stamp=${DEXC_TEST_STAMP:-$(date +%Y%m%d-%H%M%S)}
output_directory="$log_directory/I17-StrippingPlayer-$stamp"
player_path="$output_directory/DexcI17Stripping.exe"
build_report="$output_directory/build-report.json"
player_result="$output_directory/player-result.json"
driver_log="$log_directory/I17-StrippingPlayerDriver-$stamp.log"
build_log="$log_directory/I17-StrippingPlayerBuild-$stamp.log"
player_log="$log_directory/I17-StrippingPlayerRun-$stamp.log"
project_settings="$project_path/ProjectSettings/ProjectSettings.asset"

read_symbol()
{
	name=$1
	sed -n '/^  scriptingDefineSymbols:/,/^  additionalCompilerArguments:/p' "$project_settings" |
		sed -n "s/^    $name: *//p" | head -n 1
}

original_android=$(read_symbol Android)
final_exit=0

apply_symbols()
{
	label=$1
	standalone=$2
	android=$3
	log="$log_directory/I17-StrippingPlayerSymbols-$stamp-$label.log"
	require_editor_closed "$project_path" "$unity_cli" || return 2
	set +e
	DEXC_SYMBOLS_STANDALONE="$standalone" \
	DEXC_SYMBOLS_ANDROID="$android" \
		"$editor" -batchmode -quit -nographics -projectPath "$(native_path "$project_path")" \
		-executeMethod DexcDebugger.Verification.DexcSymbolCompileDriver.ApplyRequestedSymbols \
		-logFile "$(native_path "$log")"
	exit_code=$?
	set -e
	if [ "$exit_code" -ne 0 ]; then
		echo "Symbol apply failed for $label (exit $exit_code). Log: $log" >&2
		return "$exit_code"
	fi
}

restore_settings_on_exit()
{
	final_exit=$?
	trap - 0
	if restore_project_settings "$project_path" "$unity_cli" "$settings_backup"; then
		:
	else
		if [ "$final_exit" -eq 0 ]; then final_exit=2; fi
	fi
	exit "$final_exit"
}

mkdir -p "$output_directory"
settings_backup="$output_directory/ProjectSettings-before.asset"
snapshot_project_settings "$project_path" "$settings_backup"
trap restore_settings_on_exit 0

apply_symbols build 'DEXC_DEBUGGER;DEXC_I17_STRIPPING_PLAYER' "$original_android"

require_editor_closed "$project_path" "$unity_cli"
DEXC_I17_BUILD_PATH="$(native_path "$player_path")" \
DEXC_I17_BUILD_REPORT="$(native_path "$build_report")" \
	"$editor" -batchmode -quit -nographics -projectPath "$(native_path "$project_path")" \
	-executeMethod DexcDebugger.Verification.DexcPlayerStrippingBuildDriver.BuildI17Player \
	-logFile "$(native_path "$build_log")"

trap - 0
restore_project_settings "$project_path" "$unity_cli" "$settings_backup" || exit 2

if [ ! -f "$player_path" ]; then
	echo "IL2CPP stripping Player was not produced: $player_path" >&2
	final_exit=1
	exit 1
fi

set +e
DEXC_I17_STRIPPING_RESULT="$(native_path "$player_result")" \
	"$player_path" -batchmode -nographics -quit -logFile "$(native_path "$player_log")"
player_exit=$?
set -e
if [ "$player_exit" -ne 0 ]; then
	echo "IL2CPP stripping Player exited with $player_exit. Log: $player_log" >&2
	final_exit=$player_exit
	exit "$player_exit"
fi

python_bin=$(python_executable)
"$python_bin" - "$build_report" "$player_result" <<'PY'
import json
import sys

build_path, result_path = sys.argv[1:]
with open(build_path, encoding="utf-8") as stream:
    build = json.load(stream)
with open(result_path, encoding="utf-8") as stream:
    result = json.load(stream)
checks = {
    "buildSucceeded": build.get("Succeeded") is True,
    "buildBackend": build.get("Backend") == "IL2CPP",
    "buildStripping": build.get("Stripping") == "High",
    "playerPass": result.get("Pass") is True,
    "playerInitialized": result.get("Initialized") is True,
    "commandFixture": result.get("CommandCount", 0) > 0,
    "settingFixture": result.get("SettingCount", 0) > 0,
    "inspectorSourceFixture": result.get("InspectorSourceCount", 0) > 0,
    "inputEditorFixture": result.get("InputEditorCount", 0) > 0,
    "uguiReflection": result.get("UGUIReflection") is True,
}
print(json.dumps({"checks": checks, "build": build, "player": result}, indent=2))
if not all(checks.values()):
    raise SystemExit(1)
PY

echo "I17 stripping Player verification passed: $output_directory"
final_exit=0
exit 0
