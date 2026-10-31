#!/bin/sh
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_directory/common.sh"

project_argument=${DEXC_PROJECT_PATH:-$(pwd)}
project_path=$(unity_project_path "$project_argument")
unity_cli=$(unity_cli_path)
if [ "${DEXC_CLOSE_EDITOR:-1}" = "1" ]; then "$script_directory/close-editor.sh" || true; fi
require_editor_closed "$project_path" "$unity_cli"
editor=$(unity_editor_path "$project_path" "$unity_cli")
version=$(unity_editor_version "$project_path")
log_directory=$(unity_log_directory "$project_path")
stamp=${DEXC_TEST_STAMP:-$(date +%Y%m%d-%H%M%S)}
log="$log_directory/Compile-$stamp.log"

if [ ! -x "$editor" ] && [ ! -f "$editor" ]; then
	echo "Unity Editor was not found: $editor" >&2
	exit 2
fi

native_log=$(native_path "$log")
set +e
# Absolute on purpose: another process reading this command line resolves a relative -projectPath against its
# own working directory, and then reports this Editor as holding whatever project that happens to be.
(cd "$project_path" && "$editor" -batchmode -quit -nographics -projectPath "$(native_path "$project_path")" -logFile "$native_log")
editor_exit=$?
set -e

compiler_errors=0
if [ -f "$log" ]; then
	compiler_errors=$(grep -Eic 'error CS[0-9]+|Scripts have compiler errors|Compilation failed' "$log" || true)
else
	echo "Unity did not create a compiler log: $log" >&2
	compiler_errors=1
fi

python_command=$(python_executable)
"$python_command" - "$editor_exit" "$compiler_errors" "$log" "$version" <<'PY'
import json
import sys

editor_exit = int(sys.argv[1])
compiler_errors = int(sys.argv[2])
print(json.dumps({
    "success": editor_exit == 0 and compiler_errors == 0,
    "editorExitCode": editor_exit,
    "compilerErrorCount": compiler_errors,
    "log": sys.argv[3],
    "editorVersion": sys.argv[4],
}, indent=2))
sys.exit(0 if editor_exit == 0 and compiler_errors == 0 else 1)
PY
