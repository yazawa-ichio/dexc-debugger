#!/bin/sh
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_directory/common.sh"

boundary_only=0
case "${1:-}" in
    --boundary) [ "$#" -eq 1 ] || exit 2; boundary_only=1 ;;
    '') [ "$#" -eq 0 ] || exit 2 ;;
    *) echo 'Usage: run-symbol-matrix.sh [--boundary]' >&2; exit 2 ;;
esac

project_argument=${DEXC_PROJECT_PATH:-$(pwd)}
project_path=$(unity_project_path "$project_argument")
unity_cli=$(unity_cli_path)
require_editor_closed "$project_path" "$unity_cli"
version=$(unity_editor_version "$project_path")
log_directory=$(unity_log_directory "$project_path")
stamp=$(date +%Y%m%d-%H%M%S)
matrix_log="$log_directory/SymbolMatrix-$stamp.log"
project_settings="$project_path/ProjectSettings/ProjectSettings.asset"

read_symbol()
{
	name=$1
	if [ ! -r "$project_settings" ] || ! grep -q '^  scriptingDefineSymbols:' "$project_settings"; then
		echo "Cannot read scripting define settings: $project_settings" >&2
		return 2
	fi
	sed -n '/^  scriptingDefineSymbols:/,/^  additionalCompilerArguments:/p' "$project_settings" |
		sed -n "s/^    $name: *//p" | head -n 1
}

original_standalone=$(read_symbol Standalone)
original_android=$(read_symbol Android)

# Writes the two define lines in place. The Editor is deliberately not involved: it refuses to start when scripts do
# not compile, which is exactly the situation a failed row leaves behind and exactly when the project must be put back.
apply_symbols()
{
	label=$1
	standalone=$2
	android=$3
	require_editor_closed "$project_path" "$unity_cli" || return 2
	if [ ! -r "$project_settings" ] || ! grep -q '^  scriptingDefineSymbols:' "$project_settings"; then
		echo "Cannot write scripting define settings for $label: $project_settings" >&2
		return 2
	fi
	written="$project_settings.dexc-symbols"
	if ! awk -v standalone="$standalone" -v android="$android" '
		/^  scriptingDefineSymbols:/ { inside = 1; print; next }
		inside && /^  [A-Za-z]/ { inside = 0 }
		inside && /^    Standalone:/ { print "    Standalone: " standalone; next }
		inside && /^    Android:/ { print "    Android: " android; next }
		{ print }
	' "$project_settings" > "$written"; then
		rm -f "$written"
		echo "Symbol write failed for $label." >&2
		return 2
	fi
	mv "$written" "$project_settings"
	# awk only replaces lines that are already there. Settings without per-platform define lines (written as {}) come
	# through untouched, and every row would then run with the same symbols and pass - so read the values back.
	if [ "$(read_symbol Standalone)" != "$standalone" ] || [ "$(read_symbol Android)" != "$android" ]; then
		echo "Symbols for $label did not take: $project_settings" >&2
		return 2
	fi
}

restore_symbols()
{
	original_exit=$?
	trap - 0
	restore_exit=0
	if apply_symbols restore "$original_standalone" "$original_android" >> "$matrix_log" 2>&1; then
		if restored_standalone=$(read_symbol Standalone) && restored_android=$(read_symbol Android); then
			if [ "$restored_standalone" != "$original_standalone" ] || [ "$restored_android" != "$original_android" ]; then
				echo 'Restored symbols do not match the pre-run settings.' >> "$matrix_log"
				restore_exit=2
			fi
		else
			restore_exit=2
		fi
	else
		restore_exit=$?
	fi
	echo "Restore exit code: $restore_exit" >> "$matrix_log"
	if [ "$restore_exit" -ne 0 ]; then
		echo "Symbol restoration failed. Inspect $matrix_log before another run." >&2
		if [ "$original_exit" -eq 0 ]; then original_exit=$restore_exit; fi
	fi
	exit "$original_exit"
}

trap restore_symbols 0

matrix_exit=0
{
	echo "Project: $project_path"
	echo "Unity: $version"
	echo "Boundary-only: $boundary_only"
	echo "Matrix log: $matrix_log"
	echo "Original Standalone symbols: $original_standalone"
	echo "Original Android symbols: $original_android"
} > "$matrix_log"

run_row()
{
	label=$1
	standalone=$2
	run_playmode=$3
	row_stamp="${stamp}-${label}"

	printf '\n=== %s ===\n' "$label" >> "$matrix_log"
	echo "Standalone symbols: $standalone" >> "$matrix_log"
	if ! apply_symbols "$label" "$standalone" "$standalone" >> "$matrix_log" 2>&1; then
		matrix_exit=1
		return
	fi

	if ! DEXC_PROJECT_PATH="$project_path" DEXC_TEST_STAMP="$row_stamp-compile" \
		sh "$script_directory/check-compilation.sh" >> "$matrix_log" 2>&1; then
		matrix_exit=1
	fi
	row_edit_filter=${DEXC_TEST_FILTER:-}
	row_play_filter=${DEXC_TEST_FILTER:-}
	if [ "$boundary_only" -eq 1 ]; then
		row_edit_filter='DexcSymbolMatrixEditorTests;DexcDistributionBoundaryTests'
		case "$label" in
			enabled) row_play_filter='DexcDebuggerInitializationTests' ;;
			disabled) row_play_filter='DexcDisabledStateTests' ;;
		esac
	fi
	if ! DEXC_TEST_FILTER="$row_edit_filter" DEXC_PROJECT_PATH="$project_path" DEXC_TEST_STAMP="$row_stamp-editmode" \
		sh "$script_directory/run-editmode-tests.sh" >> "$matrix_log" 2>&1; then
		matrix_exit=1
	fi
	if [ "$run_playmode" -eq 1 ]; then
		if ! DEXC_TEST_FILTER="$row_play_filter" DEXC_PROJECT_PATH="$project_path" DEXC_TEST_STAMP="$row_stamp-playmode" \
			sh "$script_directory/run-playmode-tests.sh" >> "$matrix_log" 2>&1; then
			matrix_exit=1
		fi
	fi
}

run_row enabled 'DEXC_DEBUGGER' 1
run_row disabled '' 1
run_row assembly-excluded 'DISABLE_DEXC_DEBUGGER' 0
run_row conflict 'DEXC_DEBUGGER;DISABLE_DEXC_DEBUGGER' 0

echo "Matrix summary: $matrix_log"
if [ "$matrix_exit" -ne 0 ]; then
	exit "$matrix_exit"
fi
