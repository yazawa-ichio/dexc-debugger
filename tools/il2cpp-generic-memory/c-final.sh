#!/bin/sh
# The speed checks of agent C, measured again one after the other on a quiet machine. Builds nothing: it runs the
# Players that exist (a condition whose Player is missing is left out) and prints the tables at the end.
#
# usage: sh c-final.sh            (from tools/il2cpp-generic-memory; nothing else should be building or measuring)
#   C_FINAL_RUNS=5                 rounds of every group (5 by default; every condition runs once in each round)
#   C_FINAL_GROUPS="hash lazy options il cache"   the groups to run (all by default)
#   C_FINAL_NAME=c-final           the prefix of the result names (c-prov was used for runs made during builds)
# Results: results/c-final-<group>/<time>/ and results/c-final-<group>.md (the table of the newest run).
#
# How long: with 5 rounds, from the runs made on 2026-11-12 while other builds and measurements shared the machine
# (a run of a Player is 2 to 25 seconds; the Players end themselves when their result is written, see c-bench.py):
#   hash     5 conditions, no SpeedBench      about 16 minutes
#   lazy     6 conditions                     about 24 minutes
#   options  5 conditions                     about 25 minutes
#   il       4 conditions, kernels only       about 13 minutes
#   cache    3 conditions, kernels only       about 8 minutes
# About 1 hour 25 minutes in all, less on an idle machine. C_FINAL_GROUPS runs a part of it; C_FINAL_RUNS=3 takes
# three fifths of the time.
#
# Every group has the condition "base" (c-dense with nothing lazy) and takes turns between its conditions, so each
# table compares runs that were made minutes apart. "base2" is the same Player once more: what it differs from "base"
# by is the noise of the table.
set -eu

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
script_directory="$here/../unity-tests"
. "$script_directory/common.sh"
cd "$here"

runs=${C_FINAL_RUNS:-5}
groups=${C_FINAL_GROUPS:-hash lazy options il cache}
# The name the results get. Runs made while something else was building went to c-prov.
prefix=${C_FINAL_NAME:-c-final}
python=$(python_executable)

# Leaves out the conditions whose Player was not built.
existing() {
	for condition in "$@"; do
		label=$(printf %s "$condition" | cut -d: -f1)
		if [ -f "$here/../../Logs/UnityTests/GenericMemory/$label/GenericMemory.exe" ]; then
			printf '%s ' "$condition"
		else
			echo "no Player $label: $condition is left out" >&2
		fi
	done
}

group() {
	# <name> <modes> <condition>...
	name=$1
	modes=$2
	shift 2
	conditions=$(existing "$@")
	echo "== $name: $conditions"
	# shellcheck disable=SC2086
	C_BENCH_MODES="$modes" sh "$here/c-bench.sh" "$prefix-$name" "$runs" $conditions | tee "$here/results/$prefix-$name.last"
	folder=$(tail -n 1 "$here/results/$prefix-$name.last")
	"$python" "$here/c-stats.py" "$folder" --base base > "$here/results/$prefix-$name.md"
}

for item in $groups; do
	case "$item" in
	hash)
		# The hash table of libil2cpp: dense (the default on Windows) against sparse (the default on Android and iOS).
		group hash "start first kernels itf" \
			c-dense:000:base c-dense:000:base2 c-sparse:000:sparse c-dense:110:dense-110 c-sparse:110:sparse-110
		;;
	lazy)
		# What the lazy metadata costs and gives: the stock read of an rgctx (no test), the committed patch off and on,
		# and the interface-slot prototype off and on.
		group lazy "start first kernels itf speed" \
			c-dense:000:base c-norgctx:000:no-rgctx-test c-dense:110:lazy-110 c-itf:000:itf-000 c-itf:110:itf-110 c-itf:111:itf-111
		;;
	options)
		# What il2cpp and the compiler can be told.
		group options "start first kernels itf speed" \
			c-dense:000:base c-fullshare:000:fullshare c-fullshare:110:fullshare-110 c-nochecks:000:nochecks c-cl-fast:000:cl-fast
		;;
	il)
		# Rewrites of the IL (the weaver's steps of Speed.cs) and the hand rewrite of the generated C++.
		group il "kernels itf speed" \
			c-dense:000:base c-dense:000:base2 c-x1:000:hand-x1 c-speedil:000:il-seal-final-inline
		;;
	cache)
		# The cast and variance-dispatch caches in libil2cpp (patch-paircache.py), off and on in one Player.
		group cache "kernels itf speed" \
			c-dense:000:base c-vcache:0000:cache-off c-vcache:0001:cache-on
		;;
	*) echo "unknown group $item" >&2; exit 2 ;;
	esac
done

for item in $groups; do
	echo
	echo "## $item (results/$prefix-$item.md)"
	echo
	cat "$here/results/$prefix-$item.md"
done
