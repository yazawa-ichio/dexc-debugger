#!/bin/sh
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_directory/common.sh"

mode=EditMode
project_argument=${DEXC_PROJECT_PATH:-$(pwd)}
filter=${DEXC_TEST_FILTER:-}
timeout=${DEXC_TEST_TIMEOUT:-1800}
visual_capture=0
graphics_tests=0

while [ "$#" -gt 0 ]; do
	case "$1" in
		--mode) mode=$2; shift 2 ;;
		--project) project_argument=$2; shift 2 ;;
		--filter) filter=$2; shift 2 ;;
		--timeout) timeout=$2; shift 2 ;;
		--visual-capture) visual_capture=1; shift ;;
		--graphics) graphics_tests=1; shift ;;
		*) echo "Unknown argument: $1" >&2; exit 2 ;;
	esac
done

project_path=$(unity_project_path "$project_argument")
unity_cli=$(unity_cli_path)
if [ "${DEXC_CLOSE_EDITOR:-1}" = "1" ]; then "$script_directory/close-editor.sh" || true; fi
require_editor_closed "$project_path" "$unity_cli"
version=$(unity_editor_version "$project_path")
log_directory=$(unity_log_directory "$project_path")
stamp=${DEXC_TEST_STAMP:-$(date +%Y%m%d-%H%M%S)}
artifact_name=$mode
if [ "$visual_capture" -eq 1 ]; then artifact_name=VisualCapture; fi
if [ "$graphics_tests" -eq 1 ]; then artifact_name=GraphicsTests; fi
report="$log_directory/$artifact_name-$stamp.xml"
log="$log_directory/$artifact_name-$stamp.log"
# The Editor's own log; the CLI log only carries the CLI's messages, so a hang or a crash is only visible here.
editor_log="$log_directory/$artifact_name-$stamp.editor.log"

if [ "$graphics_tests" -eq 1 ]; then
	case "$mode" in EditMode|PlayMode) ;; *) echo 'Graphics tests require EditMode or PlayMode.' >&2; exit 2 ;; esac
	[ -n "$filter" ] || { echo 'Graphics tests require an explicit --filter.' >&2; exit 2; }
	[ "$visual_capture" -eq 0 ] || { echo 'Do not combine --graphics with --visual-capture.' >&2; exit 2; }
	editor=$(unity_editor_path "$project_path" "$unity_cli")
fi

started_at=$(date +%s)
set +e
if [ "$graphics_tests" -eq 1 ]; then
	case "$(uname -s)" in
		MINGW*|MSYS*|CYGWIN*)
			powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(native_path "$script_directory/run-graphics-tests.ps1")" \
				-Editor "$(native_path "$editor")" -Project "$(native_path "$project_path")" -Mode "$mode" -Filter "$filter" \
				-Report "$(native_path "$report")" -Log "$(native_path "$log")"
			;;
		*) (cd "$project_path" && "$editor" -automated -runTests -testPlatform "$mode" -testFilter "$filter" -testResults "$report" -projectPath "$(native_path "$project_path")" -logFile "$log") ;;
	esac
elif [ "$visual_capture" -eq 1 ]; then
	(cd "$project_path" && UNITY_PROJECT_PATH=. "$unity_cli" test . --editor-version "$version" --mode "$mode" --output "$report" --report-format nunit --non-interactive --no-banner --timeout "$timeout" -- --dexc-visual-capture) >"$log" 2>&1
elif [ -n "$filter" ]; then
	(cd "$project_path" && UNITY_PROJECT_PATH=. "$unity_cli" test . --editor-version "$version" --mode "$mode" --filter "$filter" --output "$report" --report-format nunit --non-interactive --no-banner --timeout "$timeout" -- -logFile "$editor_log") >"$log" 2>&1
else
	(cd "$project_path" && UNITY_PROJECT_PATH=. "$unity_cli" test . --editor-version "$version" --mode "$mode" --output "$report" --report-format nunit --non-interactive --no-banner --timeout "$timeout" -- -logFile "$editor_log") >"$log" 2>&1
fi
cli_exit=$?
set -e
elapsed=$(( $(date +%s) - started_at ))

echo "Unity CLI exit code: $cli_exit (wall clock ${elapsed}s)"
echo "Log: $log"
echo "Editor log: $editor_log"
echo "Report: $report"

python_command=$(python_executable)
set +e
"$python_command" "$script_directory/parse-unity-test-result.py" "$(native_path "$report")" --json
parser_exit=$?
set -e

visual_capture_exit=0
if [ "$visual_capture" -eq 1 ]; then
	visual_capture_manifest=${DEXC_VISUAL_CAPTURE_MANIFEST:-}
	if [ -z "$visual_capture_manifest" ]; then
		echo 'DEXC_VISUAL_CAPTURE_MANIFEST is required for visual capture.' >&2
		visual_capture_exit=2
	else
		set +e
		"$python_command" "$script_directory/verify-visual-capture.py" "$(native_path "$visual_capture_manifest")"
		visual_capture_exit=$?
		set -e
	fi
	for pair in "DEXC_RUNTIME_UI_CAPTURE_MANIFEST:verify-runtime-ui-capture.py" "DEXC_UGUI_CAPTURE_MANIFEST:verify-runtime-ugui-capture.py" "DEXC_UITOOLKIT_CAPTURE_MANIFEST:verify-runtime-uitoolkit-capture.py"; do
		variable=${pair%%:*}
		verifier=${pair#*:}
		manifest=$(eval "printf '%s' \"\${$variable:-}\"")
		if [ "$visual_capture_exit" -eq 0 ] && [ -n "$manifest" ]; then
			set +e
			"$python_command" "$script_directory/$verifier" "$(native_path "$manifest")"
			visual_capture_exit=$?
			set -e
		fi
	done
fi

if [ "$cli_exit" -eq 0 ] && [ "$parser_exit" -eq 0 ] && [ "$visual_capture_exit" -eq 0 ]; then
	exit 0
fi
exit 1
