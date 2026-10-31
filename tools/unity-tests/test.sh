#!/bin/sh
# Fast test flow entry point.
#
#   sh tools/unity-tests/test.sh quick [filter]   PlayMode tests (optionally filtered). Default loop while coding.
#   sh tools/unity-tests/test.sh edit  [filter]   EditMode tests (optionally filtered).
#   sh tools/unity-tests/test.sh full             EditMode + PlayMode + the player build + the remote check, then the
#                                                 slow-test report. Run before commit.
#   sh tools/unity-tests/test.sh player           Player build only. The only configuration that compiles the package
#                                                 without UNITY_INCLUDE_TESTS and without the Editor.
#   sh tools/unity-tests/test.sh remote           Builds a development Player and drives it from outside with the
#                                                 Unity CLI, the same client that drives the Editor.
#   sh tools/unity-tests/test.sh compile          Batch compile check only.
#   sh tools/unity-tests/test.sh matrix [--boundary]  Compile and test every DEXC_DEBUGGER / DISABLE_DEXC_DEBUGGER
#                                                 combination. Slow (7-8 min); run it before a merge, not in the loop.
#   sh tools/unity-tests/test.sh slow [xml...]    Slow-test report for the latest (or given) NUnit reports.
#
# A filter is a substring or regex of the full test name, e.g. DexcBootTests or
# "DexcDebugger.Tests.DexcSettingTests.WritePersists". Reports and logs go to Logs/UnityTests.
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
command=${1:-quick}
[ "$#" -gt 0 ] && shift

run_mode() {
	mode=$1
	filter=${2:-}
	if [ -n "$filter" ]; then
		"$script_directory/run-tests.sh" --mode "$mode" --filter "$filter"
	else
		"$script_directory/run-tests.sh" --mode "$mode"
	fi
}

latest_report() {
	ls -t "$script_directory/../../Logs/UnityTests/$1"-*.xml 2>/dev/null | head -n 1
}

python_command() {
	. "$script_directory/common.sh"
	python_executable
}

case "$command" in
	quick)
		run_mode PlayMode "${1:-}"
		;;
	edit)
		run_mode EditMode "${1:-}"
		;;
	compile)
		"$script_directory/check-compilation.sh"
		;;
	player)
		"$script_directory/run-stripping-player.sh"
		;;
	remote)
		"$script_directory/run-remote-player.sh"
		;;
	matrix)
		"$script_directory/run-symbol-matrix.sh" "$@"
		;;
	full)
		status=0
		run_mode EditMode "" || status=1
		run_mode PlayMode "" || status=1
		# The Editor defines UNITY_INCLUDE_TESTS and compiles the Editor side, so a guard that only holds there survives
		# every test above. A player is the one build that compiles the other side.
		"$script_directory/run-stripping-player.sh" || status=1
		# Every test above drives Dexc from inside the process. This is the one that drives it from outside.
		"$script_directory/run-remote-player.sh" || status=1
		"$(python_command)" "$script_directory/report-slow-tests.py" "$(latest_report EditMode)" "$(latest_report PlayMode)" || true
		exit "$status"
		;;
	slow)
		if [ "$#" -gt 0 ]; then
			"$(python_command)" "$script_directory/report-slow-tests.py" "$@"
		else
			"$(python_command)" "$script_directory/report-slow-tests.py" "$(latest_report EditMode)" "$(latest_report PlayMode)"
		fi
		;;
	*)
		sed -n '2,14p' "$0" >&2
		exit 2
		;;
esac
