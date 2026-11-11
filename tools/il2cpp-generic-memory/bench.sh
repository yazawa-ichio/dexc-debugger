#!/bin/sh
# Times the kernels of Bench.cs, and the first use of the types of a few patterns, in the built variants.
# usage: bench.sh [runs]
# Each condition runs several times in its own process, and bench.py keeps the best of them.
set -eu

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
script_directory="$here/../unity-tests"
. "$script_directory/common.sh"

runs=${1:-3}
results="$here/results/bench"
rm -rf "$results"
mkdir -p "$results"

run() {
	variant=$1
	label=$2
	lazy=$3
	player="$here/../../Logs/UnityTests/GenericMemory/$variant/GenericMemory.exe"
	for at in $(seq 1 "$runs"); do
		GENERIC_MEMORY_LAZY_METHODS=$lazy GENERIC_MEMORY_LAZY_RGCTX=$lazy timeout 300 "$player" -batchmode -nographics -generic-memory-bench \
			-generic-memory-output "$(native_path "$results/$label.kernels.$at.txt")" \
			-logFile "$(native_path "$results/$label.kernels.$at.log")" || echo "$label kernels $at: Player exit $?" >&2
		for pattern in ListUse DictUse UserClass LinqHeavy UniTaskVoid; do
			GENERIC_MEMORY_LAZY_METHODS=$lazy GENERIC_MEMORY_LAZY_RGCTX=$lazy timeout 120 "$player" -batchmode -nographics \
				-generic-memory-pattern "$pattern" -generic-memory-n 200 -generic-memory-output "$(native_path "$results/$label.first.$pattern.$at.txt")" \
				-logFile "$(native_path "$results/$label.first.$pattern.$at.log")" || echo "$label $pattern $at: Player exit $?" >&2
		done
	done
}

run baseline baseline 0
run baseline vm 1
run hoist+async hoist-async 0
run hoist+canon+async all 0
run hoist+canon+async all-vm 1
rm -f "$results"/*.log
"$(python_executable)" "$here/bench.py" "$results" baseline vm hoist-async all all-vm
