#!/bin/sh
# Builds the benchmark Unity project in sample-references and runs it as a non-development IL2CPP Player.
# It works only on that separate project, so this project's settings and Library stay as they are.
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_directory/common.sh"

repository=$(CDPATH= cd -- "$script_directory/../.." && pwd)
bench="$repository/sample-references/telemetry-bench"
project_path=$(unity_project_path "$repository/sample-references/telemetry-bench-unity")
unity_cli=$(unity_cli_path)
require_editor_closed "$project_path" "$unity_cli"
editor=$(unity_editor_path "$project_path" "$unity_cli")
log_directory=$(unity_log_directory "$project_path")
stamp=${DEXC_TEST_STAMP:-$(date +%Y%m%d-%H%M%S)}
stripping=${BENCH_STRIPPING:-High}
output_directory="$log_directory/Bench-$stripping-dev${BENCH_DEVELOPMENT:-0}-$stamp"
player_path="$output_directory/Bench.exe"
result="$output_directory/result.txt"
mkdir -p "$output_directory" "$project_path/Assets/Plugins"

if [ -n "${BENCH_EDITOR_METHOD:-}" ]; then
	# Runs one Editor method of the benchmark project instead of building, such as reading a Profiler file.
	"$editor" -batchmode -quit -nographics -projectPath "$(native_path "$project_path")" 		-executeMethod "$BENCH_EDITOR_METHOD" -logFile "$(native_path "$output_directory/editor.log")"
	grep -h "^\[profile\]" "$output_directory/editor.log" || true
	exit 0
fi

cp "$bench"/publish/*.dll "$project_path/Assets/Plugins/"

BENCH_PLAYER_PATH="$(native_path "$player_path")" BENCH_STRIPPING="$stripping" \
	"$editor" -batchmode -quit -nographics -projectPath "$(native_path "$project_path")" \
	-buildTarget Win64 -executeMethod BenchBuild.Build \
	-logFile "$(native_path "$output_directory/build.log")"

timeout "${BENCH_TIMEOUT:-300}" "$player_path" -batchmode -nographics \
	-bench-output "$(native_path "$result")" \
	-logFile "$(native_path "$output_directory/player.log")" "$@" || echo "Player exit $?"

cat "$result"
