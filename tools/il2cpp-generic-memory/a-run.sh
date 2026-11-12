#!/bin/sh
# A's copy of checks.sh, measure.sh, kit/repeat.sh and kit/names-run.sh for sets of more than three digits.
# A set is: lazy methods, lazy RGCTX, lazy interface slots, lazy interface classes, then one digit for each further
# switch (GENERIC_MEMORY_LAZY_MORE). A digit that is left out is 0.
#
# usage: a-run.sh checks <label> <mode: checks|reflection|interfaces> <set>...
#        a-run.sh measure <label> <set> [pattern...]      results/<label>-<set>, which this removes first as measure.sh does
#        a-run.sh repeat <label> <set> <runs> <name>      results/checks/<name>, which must not exist
#        a-run.sh again <label> <mode> <set> <runs> <name>  the same for any kind of checks: counts the runs in which every check passed
#        a-run.sh names <label> <folder under results, must not exist> <set> <pattern>...
#        a-run.sh ibench <label> <set> <runs> <name>      results/<name>/<label>.<set>.<run>.txt
# Every label and every name has to start with "a-". With GENERIC_MEMORY_BRANCHES=1 a check, a repeat or an ibench run
# also writes one line for each branch of the lazy code it took, to <its output>.branches.
set -u

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
logs="$here/../../Logs/UnityTests/GenericMemory"

switches() {
	set=$1
	GENERIC_MEMORY_LAZY_METHODS=$(printf %s "$set" | cut -c1)
	GENERIC_MEMORY_LAZY_RGCTX=$(printf %s "$set" | cut -c2)
	GENERIC_MEMORY_LAZY_INTERFACES=$(printf %s "$set" | cut -c3)
	GENERIC_MEMORY_LAZY_INTERFACE_CLASSES=$(printf %s "$set" | cut -c4)
	GENERIC_MEMORY_LAZY_MORE=$(printf %s "$set" | cut -c5-)
	export GENERIC_MEMORY_LAZY_METHODS GENERIC_MEMORY_LAZY_RGCTX GENERIC_MEMORY_LAZY_INTERFACES GENERIC_MEMORY_LAZY_INTERFACE_CLASSES GENERIC_MEMORY_LAZY_MORE
}

branches() {
	if [ -n "${GENERIC_MEMORY_BRANCHES:-}" ]; then
		: > "$1.branches"
		GENERIC_MEMORY_BRANCH_LOG=$(cygpath -w "$1.branches")
		export GENERIC_MEMORY_BRANCH_LOG
	fi
}

own() {
	case "$1" in a-*) ;; *) echo "not one of A's names: $1" >&2; exit 2 ;; esac
}

command=$1
shift
case "$command" in
checks)
	label=$1
	mode=$2
	shift 2
	own "$label"
	mkdir -p "$here/results/checks"
	status=0
	for set in "$@"; do
		switches "$set"
		name="$label.$mode.$set"
		output="$here/results/checks/$name.txt"
		# What an earlier run left is moved aside, so that a run that writes nothing is not taken for one that passed.
		[ ! -f "$output" ] || mv "$output" "$output.previous"
		branches "$output"
		code=0
		timeout "${GENERIC_MEMORY_TIMEOUT:-900}" "$logs/$label/GenericMemory.exe" -batchmode -nographics \
			"-generic-memory-$mode" -generic-memory-output "$(cygpath -w "$output")" \
			-logFile "$(cygpath -w "$here/results/checks/$name.log")" >/dev/null 2>&1 || code=$?
		if [ ! -f "$output" ]; then
			echo "$name: no result, Player exit $code"
			status=1
			continue
		fi
		passed=$(grep -c "	PASS	" "$output" || true)
		total=$(grep -c "^check	" "$output" || true)
		echo "$name: $passed of $total passed, Player exit $code"
		grep "^check	" "$output" | grep -v "	PASS	" || true
		[ "$code" = 0 ] && [ "$total" != 0 ] && [ "$passed" = "$total" ] || status=1
	done
	exit $status
	;;
measure)
	label=$1
	set=$2
	shift 2
	own "$label"
	switches "$set"
	[ $# = 0 ] || { GENERIC_MEMORY_PATTERNS="$*"; export GENERIC_MEMORY_PATTERNS; }
	exec "$here/measure.sh" "$label" "$label-$set"
	;;
repeat)
	label=$1
	set=$2
	runs=$3
	own "$label"
	own "$4"
	out="$here/results/checks/$4"
	if [ -e "$out" ]; then
		echo "$out exists already; give another name"
		exit 1
	fi
	mkdir -p "$out"
	switches "$set"
	ok=0
	crashed=0
	differing=0
	count=0
	while [ "$count" -lt "$runs" ]; do
		count=$((count + 1))
		file="$out/$set.$count.txt"
		[ "$count" != 1 ] || branches "$out/$set"
		code=0
		timeout 300 "$logs/$label/GenericMemory.exe" -batchmode -nographics -generic-memory-reflection \
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
	;;
again)
	label=$1
	mode=$2
	set=$3
	runs=$4
	own "$label"
	own "$5"
	out="$here/results/checks/$5"
	if [ -e "$out" ]; then
		echo "$out exists already; give another name"
		exit 1
	fi
	mkdir -p "$out"
	switches "$set"
	ok=0
	crashed=0
	failing=0
	count=0
	while [ "$count" -lt "$runs" ]; do
		count=$((count + 1))
		file="$out/$set.$count.txt"
		[ "$count" != 1 ] || branches "$out/$set"
		code=0
		timeout "${GENERIC_MEMORY_TIMEOUT:-900}" "$logs/$label/GenericMemory.exe" -batchmode -nographics "-generic-memory-$mode" \
			-generic-memory-output "$(cygpath -w "$file")" -logFile "$(cygpath -w "$out/$set.$count.log")" >/dev/null 2>&1 || code=$?
		if [ ! -f "$file" ]; then
			crashed=$((crashed + 1))
			echo "$set run $count: no result, Player exit $code"
			continue
		fi
		passed=$(grep -c "	PASS	" "$file")
		checks=$(grep -c "^check	" "$file")
		if [ "$code" = 0 ] && [ "$checks" != 0 ] && [ "$passed" = "$checks" ]; then
			ok=$((ok + 1))
		else
			failing=$((failing + 1))
			echo "$set run $count: exit $code, passed $passed of $checks"
		fi
	done
	echo "$label $mode $set: $runs runs, $ok with every check passed, $crashed without a result, $failing otherwise"
	;;
names)
	label=$1
	own "$label"
	own "$2"
	out="$here/results/$2"
	set=$3
	shift 3
	mkdir -p "$out"
	switches "$set"
	for pattern in "$@"; do
		file="$out/$pattern.$set.txt"
		code=0
		timeout 300 "$logs/$label/GenericMemory.exe" -batchmode -nographics -generic-memory-pattern "$pattern" -generic-memory-n 2 -generic-memory-names \
			-generic-memory-output "$(cygpath -w "$file")" -logFile "$(cygpath -w "$out/$pattern.$set.log")" >/dev/null 2>&1 || code=$?
		lines=0
		[ -f "$file.names" ] && lines=$(wc -l < "$file.names")
		echo "$pattern $set: exit $code, names $lines"
	done
	;;
ibench)
	label=$1
	set=$2
	runs=$3
	own "$label"
	own "$4"
	out="$here/results/$4"
	mkdir -p "$out"
	switches "$set"
	count=0
	while [ "$count" -lt "$runs" ]; do
		count=$((count + 1))
		file="$out/$label.$set.$count.txt"
		[ "$count" != 1 ] || branches "$out/$label.$set"
		timeout 300 "$logs/$label/GenericMemory.exe" -batchmode -nographics -generic-memory-interface-bench \
			-generic-memory-output "$(cygpath -w "$file")" -logFile "$(cygpath -w "$out/$label.$set.$count.log")" >/dev/null 2>&1 || echo "$label $set $count: Player exit $?"
		unset GENERIC_MEMORY_BRANCH_LOG
	done
	;;
*)
	echo "unknown command: $command" >&2
	exit 2
	;;
esac
