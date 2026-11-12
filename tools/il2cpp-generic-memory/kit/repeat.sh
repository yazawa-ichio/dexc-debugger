#!/bin/sh
# Runs the reflection lines of one Player again and again at one set of the lazy switches, and compares each run with the first.
# usage: repeat2.sh <tools/il2cpp-generic-memory> <label of the Player> <set, three digits> <runs> <name of the new folder under results/checks>
set -u
here=$1
label=$2
set=$3
runs=$4
out="$here/results/checks/$5"
if [ -e "$out" ]; then
	echo "$out exists already; give another name"
	exit 1
fi
mkdir -p "$out"
player="$here/../../Logs/UnityTests/GenericMemory/$label/GenericMemory.exe"
methods=$(printf %s "$set" | cut -c1)
rgctx=$(printf %s "$set" | cut -c2)
interfaces=$(printf %s "$set" | cut -c3)
ok=0
crashed=0
differing=0
count=0
while [ "$count" -lt "$runs" ]; do
	count=$((count + 1))
	file="$out/$set.$count.txt"
	code=0
	GENERIC_MEMORY_LAZY_METHODS=$methods GENERIC_MEMORY_LAZY_RGCTX=$rgctx GENERIC_MEMORY_LAZY_INTERFACES=$interfaces \
		timeout 300 "$player" -batchmode -nographics -generic-memory-reflection \
		-generic-memory-output "$(cygpath -w "$file")" -logFile "$(cygpath -w "$out/$set.$count.log")" >/dev/null 2>&1 || code=$?
	if [ ! -f "$file" ]; then
		crashed=$((crashed + 1))
		echo "$set run $count: no result, Player exit $code"
		continue
	fi
	passed=$(grep -c "	PASS	" "$file")
	checks=$(grep -c "^check	" "$file")
	lines=$(diff "$out/$set.1.txt" "$file" | grep '^[<>]' | grep -vc '	InterfaceOrder	')
	if [ "$code" = 0 ] && [ "$passed" = "$checks" ] && [ "$lines" = 0 ]; then
		ok=$((ok + 1))
	else
		differing=$((differing + 1))
		echo "$set run $count: exit $code, passed $passed of $checks, lines not as in the first run: $lines"
	fi
done
echo "$label $set: $runs runs, $ok as the first, $crashed crashed, $differing otherwise"
