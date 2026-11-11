#!/bin/sh
# Runs AsyncChecks.cs or InterfaceChecks.cs in built Players and counts what passed.
# usage: checks.sh <label> [label...]
# Each label is a folder of Logs/UnityTests/GenericMemory. Every Player runs once for each set of the lazy switches of
# the bench runtime. A set is three digits: lazy methods, lazy RGCTX, lazy interface slots.
#   GENERIC_MEMORY_CHECKS=interfaces  runs InterfaceChecks.cs instead of AsyncChecks.cs
#   GENERIC_MEMORY_LAZY_SETS="000 110 111"  the sets to run; the default leaves the interface slots alone
set -eu

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
script_directory="$here/../unity-tests"
. "$script_directory/common.sh"

results="$here/results/checks"
mkdir -p "$results"
mode=${GENERIC_MEMORY_CHECKS:-checks}
sets=${GENERIC_MEMORY_LAZY_SETS:-000 110}
status=0

for label in "$@"; do
	player="$here/../../Logs/UnityTests/GenericMemory/$label/GenericMemory.exe"
	for set in $sets; do
		methods=$(printf %s "$set" | cut -c1)
		rgctx=$(printf %s "$set" | cut -c2)
		interfaces=$(printf %s "$set" | cut -c3)
		name="$label.$mode.$set"
		output="$results/$name.txt"
		rm -f "$output"
		code=0
		GENERIC_MEMORY_LAZY_METHODS=$methods GENERIC_MEMORY_LAZY_RGCTX=$rgctx GENERIC_MEMORY_LAZY_INTERFACES=$interfaces \
			timeout "${GENERIC_MEMORY_TIMEOUT:-900}" "$player" -batchmode -nographics \
			"-generic-memory-$mode" -generic-memory-output "$(native_path "$output")" \
			-logFile "$(native_path "$results/$name.log")" >/dev/null 2>&1 || code=$?
		if [ ! -f "$output" ]; then
			echo "$name: no result, Player exit $code"
			status=1
			continue
		fi
		passed=$(grep -c "	PASS	" "$output" || true)
		total=$(grep -c "^check	" "$output" || true)
		echo "$name: $passed of $total passed, $(grep "^rewritten" "$output" | tr '\t' ' ') Player exit $code"
		grep "^check	" "$output" | grep -v "	PASS	" || true
		[ "$code" = 0 ] && [ "$total" != 0 ] && [ "$passed" = "$total" ] || status=1
	done
done
exit $status
