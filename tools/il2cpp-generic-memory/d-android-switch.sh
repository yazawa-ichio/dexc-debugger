#!/bin/sh
# Makes Android the active build target of the one open Editor, inside the lock of offline/editor-build.sh, and waits
# until the scripts are compiled for it. d-android-build.sh builds only when Android is active, and never switches.
# usage: d-android-switch.sh
# Exit codes: 1 lock, 2 the Editor is not this project's or does not answer, 3 the scripts do not compile,
# 4 the switch failed.
set -eu

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
script_directory="$here/../unity-tests"
. "$script_directory/common.sh"
python=$(python_executable)
unity=$(unity_cli_path)
project=$(cygpath -m "$(cd "$here/../.." && pwd)")
local="$project/tools/il2cpp-generic-memory/.local"
lock="$local/editor-build.lock"

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

held=0
release() {
	if [ "$held" = 1 ] && [ "$(sed -n 's/^pid: //p' "$lock/info.txt" 2>/dev/null || true)" = "$$" ]; then
		rm -f "$lock/info.txt"
		rmdir "$lock" 2>/dev/null || true
	fi
	held=0
}

waited=0
while ! mkdir "$lock" 2>/dev/null; do
	if [ "$waited" -ge 3600 ]; then
		echo "d-android-switch: no lock after $waited seconds: $(tr '\n' ' ' < "$lock/info.txt" 2>/dev/null || true)" >&2
		exit 1
	fi
	sleep 30
	waited=$((waited + 30))
done
held=1
trap release EXIT
trap 'release; exit 130' INT TERM
printf 'epoch: %s\ntime: %s\nfor: switch to Android\npid: %s\n' "$(date +%s)" "$(date '+%Y-%m-%d %H:%M:%S')" "$$" > "$lock/info.txt"

answer=$(editor_eval 60 'return UnityEngine.Application.dataPath + "|" + UnityEditor.EditorUserBuildSettings.activeBuildTarget + "|" + UnityEditor.EditorApplication.isCompiling + "|" + UnityEditor.EditorUtility.scriptCompilationFailed;') || { echo "d-android-switch: the Editor of $project did not answer" >&2; exit 2; }
echo "d-android-switch: before: $answer"
case "$answer" in
	"$project/Assets|Android|"*) echo "d-android-switch: Android is active already" ;;
	"$project/Assets|"*"|False|False")
		started=$(date +%s)
		result=$(editor_eval 3600 "if (UnityEngine.Application.dataPath != \"$project/Assets\") return \"other Editor\"; return UnityEditor.EditorUserBuildSettings.SwitchActiveBuildTarget(UnityEditor.BuildTargetGroup.Android, UnityEditor.BuildTarget.Android).ToString();") || result="no answer"
		echo "d-android-switch: SwitchActiveBuildTarget returned $result, $(( $(date +%s) - started )) s"
		;;
	"$project/Assets|"*)
		echo "d-android-switch: the Editor is compiling or has compile errors; nothing was done" >&2
		exit 3 ;;
	*)
		echo "d-android-switch: the answer came from another Editor ($answer); nothing was done" >&2
		exit 2 ;;
esac

tries=0
steady=0
while [ "$steady" -lt 3 ]; do
	state=$(editor_eval 60 'return UnityEngine.Application.dataPath + "|" + UnityEditor.EditorUserBuildSettings.activeBuildTarget + "|" + UnityEditor.EditorApplication.isCompiling + "|" + UnityEditor.EditorApplication.isUpdating + "|" + UnityEditor.EditorUtility.scriptCompilationFailed;' 2>/dev/null) || state=
	case "$state" in
		"$project/Assets|Android|False|False|False") steady=$((steady + 1)) ;;
		"$project/Assets|Android|False|False|True")
			echo "d-android-switch: the scripts do not compile for Android (EditorUtility.scriptCompilationFailed)" >&2
			exit 3 ;;
		"$project/Assets|Android|"*|"") steady=0 ;;
		"$project/Assets|"*)
			echo "d-android-switch: the active build target is still ${state#*|}" >&2
			exit 4 ;;
		*)
			echo "d-android-switch: the answer came from another Editor ($state)" >&2
			exit 2 ;;
	esac
	tries=$((tries + 1))
	if [ "$tries" -ge 400 ]; then
		echo "d-android-switch: the Editor did not settle" >&2
		exit 3
	fi
	sleep 3
done
echo "d-android-switch: after: $state"
