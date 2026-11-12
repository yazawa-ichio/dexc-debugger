#!/bin/sh
# Runs patterns for two types each and has the Player name every class and method it made, for each set of the lazy switches.
# usage: names-run.sh <tools/il2cpp-generic-memory> <a folder for the outputs, which must not exist yet> <pattern>...
set -u
here=$1
out=$2
shift 2
if [ -e "$out" ]; then
	echo "$out exists already; give another name"
	exit 1
fi
mkdir -p "$out"
player="$here/../../Logs/UnityTests/GenericMemory/names/GenericMemory.exe"
for pattern in "$@"; do
	for set in 000 110 111; do
		methods=$(printf %s "$set" | cut -c1)
		rgctx=$(printf %s "$set" | cut -c2)
		interfaces=$(printf %s "$set" | cut -c3)
		file="$out/$pattern.$set.txt"
		code=0
		GENERIC_MEMORY_LAZY_METHODS=$methods GENERIC_MEMORY_LAZY_RGCTX=$rgctx GENERIC_MEMORY_LAZY_INTERFACES=$interfaces \
			timeout 300 "$player" -batchmode -nographics -generic-memory-pattern "$pattern" -generic-memory-n 2 -generic-memory-names \
			-generic-memory-output "$(cygpath -w "$file")" -logFile "$(cygpath -w "$out/$pattern.$set.log")" >/dev/null 2>&1 || code=$?
		lines=0
		[ -f "$file.names" ] && lines=$(wc -l < "$file.names")
		echo "$pattern $set: exit $code, names $lines"
	done
done
