#!/bin/sh
# Builds the bench as an Android Player (arm64) in the one open Editor, one caller at a time. offline/editor-build.sh
# for Android: the same lock, the same checks before and after, and GenericMemoryAndroidBuild.Run in place of
# GenericMemoryBuild.Run. It lives in tools/il2cpp-generic-memory.
# usage: d-android-build.sh <il2cpp folder> <label> <application id>
#   <il2cpp folder>   a folder of tools/il2cpp-generic-memory/.local, or a path
#   <label>           a new folder of Logs/UnityTests/GenericMemory, starting with d-
#   <application id>  the id of the app on the device; never one of the project's own
# Environment:
#   GENERIC_MEMORY_BUILD_VARIANT    what GenericMemoryPreLink applies to the assemblies (default baseline)
#   GENERIC_MEMORY_BUILD_STRIPPING  the managed stripping level (default High)
#   GENERIC_MEMORY_BUILD_DEFINES    extra scripting defines, separated by ';'
#   GENERIC_MEMORY_LOCK_WAIT        seconds to wait for the lock before giving up (default 14400)
# Nothing is built unless Android is the Editor's active build target: this never switches the platform.
# Exit codes: 1 usage or lock, 2 the Editor is not this project's or does not answer, 3 the scripts do not compile,
# 4 the build failed, 5 UNITY_IL2CPP_PATH was left set (it is cleared), 7 the active build target is not Android,
# 8 ProjectSettings is not what it was before the build.
set -eu

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
script_directory="$here/../unity-tests"
. "$script_directory/common.sh"
python=$(python_executable)
unity=$(unity_cli_path)
project=$(cygpath -m "$(cd "$here/../.." && pwd)")
local="$project/tools/il2cpp-generic-memory/.local"
lock="$local/editor-build.lock"
mine="$local/d"

if [ $# -ne 3 ]; then
	echo "usage: d-android-build.sh <il2cpp folder> <label> <application id>" >&2
	exit 1
fi
case "$1" in
	*/*|*\\*) il2cpp=$(cygpath -m "$(cd "$1" && pwd)") ;;
	*) il2cpp="$local/$1" ;;
esac
label=$2
application=$3
variant=${GENERIC_MEMORY_BUILD_VARIANT:-baseline}
stripping=${GENERIC_MEMORY_BUILD_STRIPPING:-High}
defines=${GENERIC_MEMORY_BUILD_DEFINES:-}

for word in "$label" "$variant" "$stripping" "$application"; do
	case "$word" in *[!A-Za-z0-9._+-]*) echo "d-android-build: not a plain name: $word" >&2; exit 1 ;; esac
done
case "$label" in d-*) ;; *) echo "d-android-build: the label has to start with d-" >&2; exit 1 ;; esac
case "$application" in
	com.yazawaichio.genericmemorybench*) ;;
	*) echo "d-android-build: not an application id of the bench: $application" >&2; exit 1 ;;
esac
case "$defines$il2cpp" in *\"*|*\\*) echo "d-android-build: a quote or a backslash in the defines or in the il2cpp folder" >&2; exit 1 ;; esac
if [ ! -f "$il2cpp/libil2cpp/il2cpp-config.h" ]; then
	echo "d-android-build: $il2cpp is not an il2cpp folder" >&2
	exit 1
fi
if [ -e "$project/Logs/UnityTests/GenericMemory/$label" ]; then
	echo "d-android-build: Logs/UnityTests/GenericMemory/$label exists already; give a new label" >&2
	exit 1
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
				stale="$lock.stale.$$"
				if [ "$(lock_epoch)" = "$taken" ] && mv "$lock" "$stale" 2>/dev/null; then
					echo "d-android-build: took over a lock that was $(( (now - taken) / 60 )) minutes old: $(tr '\n' ' ' < "$stale/info.txt" 2>/dev/null || true)"
					rm -f "$stale/info.txt"
					rmdir "$stale" 2>/dev/null || true
					continue
				fi
			fi
		fi
		if [ "$waited" -ge "$limit" ]; then
			echo "d-android-build: no lock after $waited seconds: $(lock_text)" >&2
			exit 1
		fi
		[ $((waited % 300)) -ne 0 ] || echo "d-android-build: waiting for the lock: $(lock_text)"
		sleep 30
		waited=$((waited + 30))
	done
	held=1
	trap release EXIT
	trap 'release; exit 130' INT TERM
	printf 'epoch: %s\ntime: %s\nfor: Android build %s with %s\npid: %s\n' "$(date +%s)" "$(date '+%Y-%m-%d %H:%M:%S')" "$label" "$il2cpp" "$$" > "$lock/info.txt"
}

mkdir -p "$mine/logs"
acquire

answer=$(editor_eval 60 'return UnityEngine.Application.dataPath + "|" + UnityEditor.EditorUserBuildSettings.activeBuildTarget;') || { echo "d-android-build: the Editor of $project did not answer" >&2; exit 2; }
case "$answer" in
	"$project/Assets|Android") ;;
	"$project/Assets|"*)
		echo "d-android-build: the active build target is ${answer#*|}, not Android; nothing was done" >&2
		exit 7 ;;
	*)
		echo "d-android-build: the answer came from another Editor ($answer); nothing was done" >&2
		exit 2 ;;
esac

# The scripts as they are on disk have to compile before the build starts: a build with errors leaves the Editor in them.
editor_eval 600 "if (UnityEngine.Application.dataPath != \"$project/Assets\") return \"other\"; UnityEditor.AssetDatabase.Refresh(); return \"refreshed\";" > /dev/null || true
tries=0
steady=0
while [ "$steady" -lt 2 ]; do
	state=$(editor_eval 60 'return UnityEngine.Application.dataPath + "|" + UnityEditor.EditorApplication.isCompiling + "|" + UnityEditor.EditorUtility.scriptCompilationFailed;' 2>/dev/null) || state=
	case "$state" in
		"$project/Assets|False|False") steady=$((steady + 1)) ;;
		"$project/Assets|False|True")
			echo "d-android-build: the scripts of the project do not compile (EditorUtility.scriptCompilationFailed); nothing was built" >&2
			exit 3 ;;
		"$project/Assets|True|"*|"") steady=0 ;;
		*)
			echo "d-android-build: the answer came from another Editor ($state); nothing was done" >&2
			exit 2 ;;
	esac
	tries=$((tries + 1))
	if [ "$tries" -ge 200 ]; then
		echo "d-android-build: the Editor did not finish compiling" >&2
		exit 3
	fi
	sleep 3
done

# What ProjectSettings is before the build, as the difference to what is committed. The build has to leave the same.
(cd "$project" && git diff ProjectSettings/) > "$mine/logs/$label.settings-before.diff"

extra=null
[ -z "$defines" ] || extra="\"$defines\""
started=$(date +%s)
echo "d-android-build: building $label with $il2cpp ($variant, $stripping) as $application"
status=0
result=$(editor_eval 5400 "if (UnityEngine.Application.dataPath != \"$project/Assets\") return \"other Editor: \" + UnityEngine.Application.dataPath; return GenericMemory.Editor.GenericMemoryAndroidBuild.Run(\"$variant\", \"$il2cpp\", \"$label\", \"$application\", \"$stripping\", $extra);") || status=4
echo "d-android-build: ${result:-no answer}, $(( $(date +%s) - started )) s"
case "$result" in *" Succeeded, errors 0,"*) ;; *) status=4 ;; esac

# GenericMemoryAndroidBuild clears the variable itself. Left set, the Editor would compile its own scripts with the tools of that folder.
left=$(editor_eval 300 'return "[" + System.Environment.GetEnvironmentVariable("UNITY_IL2CPP_PATH") + "][" + System.Environment.GetEnvironmentVariable("GENERIC_MEMORY_VARIANT") + "]";') || left="no answer"
if [ "$left" != "[][]" ]; then
	echo "d-android-build: the Editor still has UNITY_IL2CPP_PATH or GENERIC_MEMORY_VARIANT: $left; clearing them" >&2
	editor_eval 300 'System.Environment.SetEnvironmentVariable("UNITY_IL2CPP_PATH", null); System.Environment.SetEnvironmentVariable("GENERIC_MEMORY_VARIANT", null); return 1;' > /dev/null || true
	[ "$status" != 0 ] || status=5
fi

# The settings in the Editor's memory are written out, and the file has to differ from the commit as it did before.
editor_eval 300 'UnityEditor.AssetDatabase.SaveAssets(); return 1;' > /dev/null || true
(cd "$project" && git diff ProjectSettings/) > "$mine/logs/$label.settings-after.diff"
if ! cmp -s "$mine/logs/$label.settings-before.diff" "$mine/logs/$label.settings-after.diff"; then
	echo "d-android-build: ProjectSettings is not what it was before the build: compare $mine/logs/$label.settings-before.diff and $label.settings-after.diff" >&2
	[ "$status" != 0 ] || status=8
fi
case "$result" in *", settings as before") ;; *) [ "$status" != 0 ] || status=8 ;; esac

if [ "$status" = 0 ]; then
	# What d-android-run.py reads to know which app on the device this label is.
	printf '{"applicationId": "%s", "il2cpp": "%s", "variant": "%s", "stripping": "%s"}\n' "$application" "$il2cpp" "$variant" "$stripping" \
		> "$project/Logs/UnityTests/GenericMemory/$label/android.json"
	# Inside the lock: the next build writes over Library/Bee. Which libil2cpp was compiled, and the library with its symbols.
	if [ -f "$here/d-android-keep.py" ]; then
		"$python" "$here/d-android-keep.py" "$label" "$il2cpp" || status=6
	fi
fi
exit "$status"
