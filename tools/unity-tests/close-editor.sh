#!/bin/sh
# Closes the Unity Editor of the target project when it is open and not in Play Mode, so batch runs can start.
# Exit codes: 0 closed or not open, 2 could not close (still open, or in Play Mode), 3 CLI/status unavailable.
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_directory/common.sh"

project_argument=${DEXC_PROJECT_PATH:-$(pwd)}
project_path=$(unity_project_path "$project_argument")
unity_cli=$(unity_cli_path)
python_command=$(python_executable)
native_project=$(native_path "$project_path")

state=$("$unity_cli" status --format json --no-banner 2>/dev/null | "$python_command" -c '
import json, sys, os
target = os.path.normcase(os.path.normpath(sys.argv[1]))
try:
    data = json.load(sys.stdin)
except Exception:
    print("unknown"); sys.exit(0)
for instance in data.get("data", {}).get("instances", []):
    if os.path.normcase(os.path.normpath(instance.get("project", ""))) == target:
        print(instance.get("state", "unknown")); sys.exit(0)
print("none")
' "$native_project") || state=unknown

case "$state" in
	none) exit 0 ;;
	ready|idle|Ready|Idle) ;;
	unknown) echo "Unity Editor state is unknown; not closing: $project_path" >&2; exit 3 ;;
	*) echo "Unity Editor is $state (Play Mode or busy); not closing: $project_path" >&2; exit 2 ;;
esac

echo "Closing the Unity Editor for $project_path ..."
set +e
(cd "$project_path" && UNITY_PROJECT_PATH=. "$unity_cli" command --project-path . --timeout 20 --no-banner eval 'UnityEditor.EditorApplication.Exit(0);' >/dev/null 2>&1)
set -e
attempt=0
while [ "$attempt" -lt 30 ]; do
	if ! unity_editor_open "$project_path" "$unity_cli"; then
		echo "Unity Editor closed."
		exit 0
	fi
	attempt=$((attempt + 1))
	sleep 2
done
echo "Unity Editor is still open after 60s: $project_path" >&2
exit 2
