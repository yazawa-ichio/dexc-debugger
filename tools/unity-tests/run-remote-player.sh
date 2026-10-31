#!/bin/sh
# Drives a running development Player from outside the process with the same client that drives the Editor.
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_directory/common.sh"

project_argument=${DEXC_PROJECT_PATH:-$(pwd)}
project_path=$(unity_project_path "$project_argument")
unity_cli=$(unity_cli_path)
require_editor_closed "$project_path" "$unity_cli"
editor=$(unity_editor_path "$project_path" "$unity_cli")
log_directory=$(unity_log_directory "$project_path")
stamp=${DEXC_TEST_STAMP:-$(date +%Y%m%d-%H%M%S)}
output_directory="$log_directory/I19-RemotePlayer-$stamp"
player_directory="$output_directory/Player"
player_path="$player_directory/DexcI19Remote.exe"
build_report="$output_directory/build-report.json"
summary_path="$output_directory/remote-report.json"
build_log="$log_directory/I19-RemotePlayerBuild-$stamp.log"
player_log="$log_directory/I19-RemotePlayerRun-$stamp.log"
project_settings="$project_path/ProjectSettings/ProjectSettings.asset"
player_pid=""
final_exit=0

read_symbol()
{
	name=$1
	sed -n '/^  scriptingDefineSymbols:/,/^  additionalCompilerArguments:/p' "$project_settings" |
		sed -n "s/^    $name: *//p" | head -n 1
}

original_android=$(read_symbol Android)

apply_symbols()
{
	label=$1
	standalone=$2
	android=$3
	log="$log_directory/I19-RemotePlayerSymbols-$stamp-$label.log"
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

cleanup_on_exit()
{
	final_exit=$?
	trap - 0
	if [ -n "$player_pid" ]; then
		kill "$player_pid" 2>/dev/null || true
		wait "$player_pid" 2>/dev/null || true
	fi
	if restore_project_settings "$project_path" "$unity_cli" "$settings_backup"; then
		:
	else
		if [ "$final_exit" -eq 0 ]; then final_exit=2; fi
	fi
	exit "$final_exit"
}

mkdir -p "$output_directory" "$player_directory"
settings_backup="$output_directory/ProjectSettings-before.asset"
snapshot_project_settings "$project_path" "$settings_backup"
trap cleanup_on_exit 0

apply_symbols build 'DEXC_DEBUGGER;DEXC_REMOTE_PLAYER' "$original_android"

require_editor_closed "$project_path" "$unity_cli"
DEXC_I19_BUILD_PATH="$(native_path "$player_path")" \
DEXC_I19_BUILD_REPORT="$(native_path "$build_report")" \
	"$editor" -batchmode -quit -nographics -projectPath "$(native_path "$project_path")" \
	-executeMethod DexcDebugger.Verification.DexcRemotePlayerBuildDriver.BuildRemotePlayer \
	-logFile "$(native_path "$build_log")"

trap - 0
restore_project_settings "$project_path" "$unity_cli" "$settings_backup" || exit 2
trap cleanup_on_exit 0

# The Pipeline package bakes its config into Assets during the build and deletes the asset again afterwards, but
# leaves the folders it made behind. rmdir is the guard: it refuses a folder that still holds anything.
for leftover in "$project_path/Assets/Settings/Pipeline/Resources" "$project_path/Assets/Settings/Pipeline"; do
	if [ -d "$leftover" ] && rmdir "$leftover" 2>/dev/null; then
		rm -f "$leftover.meta"
	fi
done

if [ ! -f "$player_path" ]; then
	echo "Remote verification Player was not produced: $player_path" >&2
	final_exit=1
	exit 1
fi

# No -quit: the Player has to stay up while it is driven from outside.
"$player_path" -batchmode -nographics -logFile "$(native_path "$player_log")" &
player_pid=$!

# The descriptor lands beside the executable once the server has a port. Waiting for the file rather than
# for a fixed delay keeps this honest on a slow machine.
descriptor="$player_directory/.unity-pipeline-runtime-port"
waited=0
while [ ! -f "$descriptor" ]; do
	if [ "$waited" -ge 120 ]; then
		echo "The Player never published a runtime descriptor. Log: $player_log" >&2
		final_exit=1
		exit 1
	fi
	if ! kill -0 "$player_pid" 2>/dev/null; then
		echo "The Player exited before publishing a runtime descriptor. Log: $player_log" >&2
		final_exit=1
		exit 1
	fi
	sleep 1
	waited=$((waited + 1))
done

call()
{
	name=$1
	shift
	set +e
	"$unity_cli" command --runtime-path "$(native_path "$player_directory")" "$name" "$@" 2>&1
	call_exit=$?
	set -e
	return "$call_exit"
}

record()
{
	name=$1
	shift
	echo "--- $name" >&2
	output=$(call "$name" "$@") || true
	printf '%s\n' "$output" > "$output_directory/$name.txt"
	printf '%s' "$output"
}

status_out=$(record dexc_status)
initialize_out=$(record dexc_initialize)
list_out=$(record dexc_command_list --path_prefix I19/Remote)
execute_out=$(record dexc_command_execute --path I19/Remote/Echo --inputs '{"Text":"over the wire"}')
setting_out=$(record dexc_setting_read --path I19/Remote/Volume)
registered_out=$(record dexc_inspector_registered --path_prefix I19/Remote)
diagnostics_out=$(record dexc_diagnostics_list)

python_bin=$(python_executable)
"$python_bin" - "$build_report" "$output_directory" > "$summary_path" <<'PY'
import json
import pathlib
import sys

build_path, directory = sys.argv[1:]
root = pathlib.Path(directory)
with open(build_path, encoding="utf-8") as stream:
    build = json.load(stream)


def text(name):
    path = root / (name + ".txt")
    return path.read_text(encoding="utf-8") if path.exists() else ""


checks = {
    "buildSucceeded": build.get("Succeeded") is True,
    # The Player answers at all, and says Dexc is compiled in.
    "playerAnswers": '"enabled": true' in text("dexc_status").replace("'", '"').lower()
    or '"enabled":true' in text("dexc_status").replace(" ", "").lower(),
    # A session did not exist until the outside asked for one.
    "sessionWasStartedFromOutside": "not-initialized" in text("dexc_status")
    and "not-initialized" not in text("dexc_initialize"),
    "commandListed": "I19/Remote/Echo" in text("dexc_command_list"),
    "commandRan": "over the wire" in text("dexc_command_execute"),
    "settingRead": "I19/Remote/Volume" in text("dexc_setting_read"),
    "inspectorListed": "I19/Remote/Target" in text("dexc_inspector_registered"),
    "diagnosticsAnswered": "succeeded" in text("dexc_diagnostics_list"),
}
print(json.dumps({"checks": checks, "build": build}, indent=2))
PY

cat "$summary_path"
"$python_bin" - "$summary_path" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    report = json.load(stream)
failed = [name for name, ok in report["checks"].items() if not ok]
if failed:
    print("Remote Player checks failed: " + ", ".join(failed), file=sys.stderr)
    raise SystemExit(1)
PY
