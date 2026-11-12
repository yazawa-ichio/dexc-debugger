#!/bin/sh
# Says whether two built Players behave the same: the three kinds of checks, the reflection lines, and the measured numbers.
# usage: compare.sh <label> <label> [lazy sets, default "000 110"]
# Runs checks.sh in its three modes on both labels, compares the reflection output line by line (lines with
# InterfaceOrder are left out: their order is the order of the addresses of the classes), then measure.sh for each
# set and kit/same.py. The measured results go to results/compare-<label>-<set>, a name no other tool uses. A label is
# measured again only when its GameAssembly.dll or the patterns changed since.
#   GENERIC_MEMORY_PATTERNS     the patterns to measure (default: the seven the offline build was checked with)
#   GENERIC_MEMORY_SKIP_CHECKS  when set, checks.sh is not run again and its last output for the two labels is compared
set -u

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
tools="$here/.."
script_directory="$tools/../unity-tests"
. "$script_directory/common.sh"
# common.sh turns on "exit at the first failure"; here a failed step is counted and the rest still runs.
set +e
python=$(python_executable)
left=$1
right=$2
sets=${3:-000 110}
patterns=${GENERIC_MEMORY_PATTERNS:-ListNew ListUse DictUse ArrayUse LinqHeavy UserClass TaskT}
status=0

if [ -z "${GENERIC_MEMORY_SKIP_CHECKS:-}" ]; then
	for mode in checks reflection interfaces; do
		GENERIC_MEMORY_CHECKS=$mode GENERIC_MEMORY_LAZY_SETS="$sets" "$tools/checks.sh" "$left" "$right" || status=1
	done
fi
for set in $sets; do
	a="$tools/results/checks/$left.reflection.$set.txt"
	b="$tools/results/checks/$right.reflection.$set.txt"
	lines=$(diff "$a" "$b" | grep '^[<>]' | grep -vc '	InterfaceOrder	')
	echo "reflection $set: $(grep -c '' "$a") and $(grep -c '' "$b") lines, $lines differ outside InterfaceOrder"
	[ "$lines" = 0 ] || status=1
done
for set in $sets; do
	methods=$(printf %s "$set" | cut -c1)
	rgctx=$(printf %s "$set" | cut -c2)
	interfaces=$(printf %s "$set" | cut -c3)
	for label in "$left" "$right"; do
		name="compare-$label-$set"
		stamp="$patterns / $(stat -c '%Y %s' "$tools/../../Logs/UnityTests/GenericMemory/$label/GameAssembly.dll" 2>/dev/null)"
		if [ ! -d "$tools/results/$name" ] || [ "$(cat "$tools/results/$name.stamp" 2>/dev/null)" != "$stamp" ]; then
			rm -f "$tools/results/$name.stamp"
			GENERIC_MEMORY_PATTERNS="$patterns" GENERIC_MEMORY_LAZY_METHODS=$methods GENERIC_MEMORY_LAZY_RGCTX=$rgctx GENERIC_MEMORY_LAZY_INTERFACES=$interfaces \
				"$tools/measure.sh" "$label" "$name" > /dev/null || status=1
			printf '%s\n' "$stamp" > "$tools/results/$name.stamp"
		fi
	done
	same=$("$python" "$tools/kit/same.py" "$tools/results/compare-$left-$set" "$tools/results/compare-$right-$set")
	echo "measure $set: $same"
	case "$same" in "0 runs compared"*) status=1 ;; *", 0 differ") ;; *) status=1 ;; esac
done
[ "$status" = 0 ] && echo "same: $left and $right" || echo "NOT the same: $left and $right"
exit $status
