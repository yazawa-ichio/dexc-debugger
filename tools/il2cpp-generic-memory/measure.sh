#!/bin/sh
# Runs every pattern of a built variant at two counts and writes one result file for each run.
# usage: measure.sh <variant> [label]
# Each run is its own process, so no pattern pays for classes that another one already initialized.
# The environment of this script reaches the Player: GENERIC_MEMORY_LAZY_METHODS=1 turns on the lazy inflation in the bench runtime.
set -eu

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
script_directory="$here/../unity-tests"
. "$script_directory/common.sh"

variant=${1:-baseline}
label=${2:-$variant}
player="$here/../../Logs/UnityTests/GenericMemory/$variant/GenericMemory.exe"
results="$here/results/$label"
rm -rf "$results"
mkdir -p "$results"
generated="$here/../../Assets/Tests/GenericMemory/Scenario/Generated"

for file in "$generated"/*.cs; do
	pattern=$(basename "$file" .cs)
	case "$pattern" in Data|Registry) continue ;; esac
	[ -z "${GENERIC_MEMORY_PATTERNS:-}" ] || case " $GENERIC_MEMORY_PATTERNS " in *" $pattern "*) ;; *) continue ;; esac
	types=$(sed -n 's/.*public const int Types = \([0-9]*\);.*/\1/p' "$file")
	for count in $((types / 2)) "$types"; do
		output="$results/$pattern-$count.txt"
		timeout "${GENERIC_MEMORY_TIMEOUT:-120}" "$player" -batchmode -nographics \
			-generic-memory-pattern "$pattern" -generic-memory-n "$count" -generic-memory-output "$(native_path "$output")" \
			-logFile "$(native_path "$results/$pattern-$count.log")" || echo "$pattern $count: Player exit $?" >&2
		[ -f "$output" ] || echo "$pattern $count: no result" >&2
	done
done
"$(python_executable)" "$here/report.py" "$results"
