#!/bin/sh
# Plays Assets/Samples/StealthStageDemo in a real Play Mode session, captures gameplay and the Dexc menu, and checks
# the capture manifest. Compilation with Dexc disabled or excluded is covered by run-symbol-matrix.sh, since the
# sample lives in this project's Assets.
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_directory/common.sh"

project_path=$(unity_project_path "${DEXC_PROJECT_PATH:-$(pwd)}")
unity_cli=$(unity_cli_path)
require_editor_closed "$project_path" "$unity_cli"
editor=$(unity_editor_path "$project_path" "$unity_cli")
log_directory=$(unity_log_directory "$project_path")
stamp=${DEXC_TEST_STAMP:-$(date +%Y%m%d-%H%M%S)}
python_bin=$(python_executable)
manifest="$log_directory/DEXC-SAMPLE01-Capture-$stamp.json"

DEXC_STEALTH_SAMPLE_CAPTURE_MANIFEST="$(native_path "$manifest")" "$editor" -batchmode -projectPath "$(native_path "$project_path")" 	-executeMethod DexcDebugger.Tests.DexcStealthStageSamplePlayDriver.Run 	-logFile "$(native_path "$log_directory/DEXC-SAMPLE01-PlayMode-$stamp.log")"
"$python_bin" - "$(native_path "$manifest")" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as stream:
    result = json.load(stream)
if not isinstance(result, dict) or result.get("success") is not True:
    raise SystemExit("Stealth sample PlayMode validation did not succeed.")
PY
echo "Stealth sample capture manifest: $manifest"
