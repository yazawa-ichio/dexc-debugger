#!/bin/sh
# Builds the bench Player in the one open Editor, one caller at a time, and can keep the inputs of that build for build.py.
# usage: editor-build.sh <il2cpp folder> <label> [name of the inputs to save]
#   <il2cpp folder>  a folder of tools/il2cpp-generic-memory/.local, or a path
#   <label>          a new folder of Logs/UnityTests/GenericMemory
#   [name]           a new folder of .local/inputs: its Managed is copied during the build, the rest by save-inputs.py after it
# Environment:
#   GENERIC_MEMORY_BUILD_VARIANT    what GenericMemoryPreLink applies to the assemblies (default baseline; inputs are saved from baseline only)
#   GENERIC_MEMORY_BUILD_STRIPPING  the managed stripping level (default High)
#   GENERIC_MEMORY_BUILD_DEFINES    extra scripting defines, separated by ';'
#   GENERIC_MEMORY_LOCK_WAIT        seconds to wait for the lock before giving up (default 14400)
# The lock is the folder .local/editor-build.lock. info.txt in it says who took it, when and for what. A lock older than
# 40 minutes is taken over when the Editor answers, because an Editor that is in a build does not.
# Exit codes: 1 usage or lock, 2 the Editor is not this project's or does not answer, 3 the scripts do not compile,
# 4 the build failed, 5 UNITY_IL2CPP_PATH was left set (it is cleared), 6 the inputs were not saved.
set -eu

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
script_directory="$here/../../unity-tests"
. "$script_directory/common.sh"
python=$(python_executable)
unity=$(unity_cli_path)
project=$(cygpath -m "$(cd "$here/../../.." && pwd)")
local="$project/tools/il2cpp-generic-memory/.local"
lock="$local/editor-build.lock"

if [ $# -lt 2 ] || [ $# -gt 3 ]; then
	echo "usage: editor-build.sh <il2cpp folder> <label> [name of the inputs to save]" >&2
	exit 1
fi
case "$1" in
	*/*|*\\*) il2cpp=$(cygpath -m "$(cd "$1" && pwd)") ;;
	*) il2cpp="$local/$1" ;;
esac
label=$2
name=${3:-}
variant=${GENERIC_MEMORY_BUILD_VARIANT:-baseline}
stripping=${GENERIC_MEMORY_BUILD_STRIPPING:-High}
defines=${GENERIC_MEMORY_BUILD_DEFINES:-}

for word in "$label" "$name" "$variant" "$stripping"; do
	case "$word" in *[!A-Za-z0-9._+-]*) echo "editor-build: not a plain name: $word" >&2; exit 1 ;; esac
done
case "$defines$il2cpp" in *\"*|*\\*) echo "editor-build: a quote or a backslash in the defines or in the il2cpp folder" >&2; exit 1 ;; esac
if [ ! -f "$il2cpp/libil2cpp/il2cpp-config.h" ]; then
	echo "editor-build: $il2cpp is not an il2cpp folder" >&2
	exit 1
fi
if [ -e "$project/Logs/UnityTests/GenericMemory/$label" ]; then
	echo "editor-build: Logs/UnityTests/GenericMemory/$label exists already; give a new label" >&2
	exit 1
fi
if [ -n "$name" ]; then
	if [ -e "$local/inputs/$name" ]; then
		echo "editor-build: .local/inputs/$name exists already; give a new name" >&2
		exit 1
	fi
	if [ "$variant" != baseline ]; then
		echo "editor-build: inputs are saved from a baseline build only: build.py --weave does the rewriting" >&2
		exit 1
	fi
fi

# Prints what a C# snippet returned in this project's Editor. Fails when the Editor did not answer or did not compile it.
editor_eval() {
	"$unity" command --project-path "$project" --timeout "$1" --result-only eval "$2" 2>/dev/null | "$python" -c '
import json, sys
try:
    value = json.load(sys.stdin)
except ValueError:
    sys.exit(3)
if value.get("success") is not True:
    sys.stderr.write(json.dumps(value.get("errors"), ensure_ascii=False) + "\n")
    sys.exit(4)
print(value.get("result"))
'
}

lock_epoch() {
	epoch=$(sed -n 's/^epoch: //p' "$lock/info.txt" 2>/dev/null || true)
	[ -n "$epoch" ] || epoch=$(stat -c %Y "$lock" 2>/dev/null || true)
	echo "$epoch"
}

lock_text() {
	tr '\n' ' ' < "$lock/info.txt" 2>/dev/null || true
}

held=0
release() {
	# Only the lock this process took: after 40 minutes another caller may hold it.
	if [ "$held" = 1 ] && [ "$(sed -n 's/^pid: //p' "$lock/info.txt" 2>/dev/null || true)" = "$$" ]; then
		rm -f "$lock/info.txt"
		rmdir "$lock" 2>/dev/null || true
	fi
	held=0
}

acquire() {
	waited=0
	limit=${GENERIC_MEMORY_LOCK_WAIT:-14400}
	while ! mkdir "$lock" 2>/dev/null; do
		taken=$(lock_epoch)
		now=$(date +%s)
		if [ -n "$taken" ] && [ $((now - taken)) -ge 2400 ]; then
			if answer=$(editor_eval 60 'return UnityEngine.Application.dataPath;' 2>/dev/null) && [ "$answer" = "$project/Assets" ]; then
				# Moved away in one step, so that of two callers who see the old lock only one takes it over.
				stale="$lock.stale.$$"
				if [ "$(lock_epoch)" = "$taken" ] && mv "$lock" "$stale" 2>/dev/null; then
					echo "editor-build: took over a lock that was $(( (now - taken) / 60 )) minutes old: $(tr '\n' ' ' < "$stale/info.txt" 2>/dev/null || true)"
					rm -f "$stale/info.txt"
					rmdir "$stale" 2>/dev/null || true
					continue
				fi
			fi
		fi
		if [ "$waited" -ge "$limit" ]; then
			echo "editor-build: no lock after $waited seconds: $(lock_text)" >&2
			exit 1
		fi
		[ $((waited % 300)) -ne 0 ] || echo "editor-build: waiting for the lock: $(lock_text)"
		sleep 30
		waited=$((waited + 30))
	done
	held=1
	trap release EXIT
	trap 'release; exit 130' INT TERM
	printf 'epoch: %s\ntime: %s\nfor: build %s with %s%s\npid: %s\n' "$(date +%s)" "$(date '+%Y-%m-%d %H:%M:%S')" "$label" "$il2cpp" "${name:+, inputs $name}" "$$" > "$lock/info.txt"
}

mkdir -p "$local"
acquire

answer=$(editor_eval 60 'return UnityEngine.Application.dataPath;') || { echo "editor-build: the Editor of $project did not answer" >&2; exit 2; }
if [ "$answer" != "$project/Assets" ]; then
	echo "editor-build: the answer came from another Editor ($answer); nothing was done" >&2
	exit 2
fi

# The scripts as they are on disk have to compile before the build starts: a build with errors leaves the Editor in them.
editor_eval 600 "if (UnityEngine.Application.dataPath != \"$project/Assets\") return \"other\"; UnityEditor.AssetDatabase.Refresh(); return \"refreshed\";" > /dev/null || true
tries=0
steady=0
while [ "$steady" -lt 2 ]; do
	state=$(editor_eval 60 'return UnityEngine.Application.dataPath + "|" + UnityEditor.EditorApplication.isCompiling + "|" + UnityEditor.EditorUtility.scriptCompilationFailed;' 2>/dev/null) || state=
	case "$state" in
		"$project/Assets|False|False") steady=$((steady + 1)) ;;
		"$project/Assets|False|True")
			echo "editor-build: the scripts of the project do not compile (EditorUtility.scriptCompilationFailed); nothing was built" >&2
			exit 3 ;;
		"$project/Assets|True|"*|"") steady=0 ;;
		*)
			echo "editor-build: the answer came from another Editor ($state); nothing was done" >&2
			exit 2 ;;
	esac
	tries=$((tries + 1))
	if [ "$tries" -ge 200 ]; then
		echo "editor-build: the Editor did not finish compiling" >&2
		exit 3
	fi
	sleep 3
done

save=null
[ -z "$name" ] || save="\"$local/inputs/$name/Managed\""
extra=null
[ -z "$defines" ] || extra="\"$defines\""
started=$(date +%s)
echo "editor-build: building $label with $il2cpp ($variant, $stripping)"
status=0
result=$(editor_eval 3600 "if (UnityEngine.Application.dataPath != \"$project/Assets\") return \"other Editor: \" + UnityEngine.Application.dataPath; System.Environment.SetEnvironmentVariable(\"GENERIC_MEMORY_SAVE_MANAGED\", $save); try { return GenericMemory.Editor.GenericMemoryBuild.Run(\"$variant\", \"$il2cpp\", \"$stripping\", \"$label\", $extra); } finally { System.Environment.SetEnvironmentVariable(\"GENERIC_MEMORY_SAVE_MANAGED\", null); }") || status=4
echo "editor-build: ${result:-no answer}, $(( $(date +%s) - started )) s"
case "$result" in *" Succeeded, errors 0,"*) ;; *) status=4 ;; esac

# GenericMemoryBuild clears the variable itself. Left set, the Editor would compile its own scripts with the tools of that folder.
left=$(editor_eval 300 'return "[" + System.Environment.GetEnvironmentVariable("UNITY_IL2CPP_PATH") + "][" + System.Environment.GetEnvironmentVariable("GENERIC_MEMORY_SAVE_MANAGED") + "]";') || left="no answer"
if [ "$left" != "[][]" ]; then
	echo "editor-build: the Editor still has UNITY_IL2CPP_PATH or GENERIC_MEMORY_SAVE_MANAGED: $left; clearing them" >&2
	editor_eval 300 'System.Environment.SetEnvironmentVariable("UNITY_IL2CPP_PATH", null); System.Environment.SetEnvironmentVariable("GENERIC_MEMORY_SAVE_MANAGED", null); return 1;' > /dev/null || true
	[ "$status" != 0 ] || status=5
fi

if [ "$status" = 0 ] && [ -n "$name" ]; then
	# Inside the lock: the next build writes over Library/Bee, which this reads.
	"$python" "$here/save-inputs.py" "$name" "$label" || status=6
fi
exit "$status"
