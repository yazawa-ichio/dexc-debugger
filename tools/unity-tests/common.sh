#!/bin/sh
set -eu

if [ -n "${script_directory:-}" ]; then
	. "$script_directory/load-local-config.sh"
	load_local_config
fi

unity_project_path() {
	project_path=${DEXC_PROJECT_PATH:-${1:-$(pwd)}}
	project_path=$(cd "$project_path" && pwd)
	if [ ! -f "$project_path/ProjectSettings/ProjectVersion.txt" ]; then
		echo "Unity project was not found: $project_path" >&2
		return 2
	fi
	echo "$project_path"
}

unity_cli_path() {
	if [ -n "${UNITY_CLI:-}" ]; then
		echo "$UNITY_CLI"
		return 0
	fi
	if command -v unity >/dev/null 2>&1; then
		command -v unity
		return 0
	fi
	echo 'Unity CLI was not found. Set UNITY_CLI or add unity to PATH.' >&2
	return 2
}

unity_editor_version() {
	project_path=$1
	version=$(sed -n 's/^m_EditorVersion: *//p' "$project_path/ProjectSettings/ProjectVersion.txt" | head -n 1)
	if [ -z "$version" ]; then
		echo "m_EditorVersion was not found in $project_path/ProjectSettings/ProjectVersion.txt" >&2
		return 2
	fi
	echo "$version"
}

unity_log_directory() {
	project_path=$1
	log_directory="$project_path/Logs/UnityTests"
	mkdir -p "$log_directory"
	echo "$log_directory"
}

unity_editor_open() {
	project_path=$1
	unity_cli=$2
	# Return 0=open, 1=closed, 2=unknown. Require a complete process list and match this project.
	# Ask from an empty directory, not from the project. The CLI resolves another process's relative -projectPath
	# against the asker's working directory, so asking from here reported any other repository's "-projectPath ."
	# batch run as this project being open. From an empty directory such a path names no project and drops out.
	editor_probe=$(mktemp -d) || return 2
	# Caught with || so that a caller running under set -e gets "unknown" back instead of being ended by the CLI's code.
	editor_probe_exit=0
	status=$(cd "$editor_probe" && "$unity_cli" editors running --format json --no-banner --non-interactive) || editor_probe_exit=$?
	rmdir "$editor_probe"
	[ "$editor_probe_exit" -eq 0 ] || return 2
	python_bin=$(python_executable) || return 2
	editor_state=$(printf '%s\n' "$status" | "$python_bin" -c '
import json, os, sys
try:
    envelope = json.load(sys.stdin)
    if not isinstance(envelope, dict) or envelope.get("success") is not True:
        raise ValueError("unsuccessful status response")
    data = envelope.get("data")
    count = data.get("count") if isinstance(data, dict) else None
    if type(count) is not int or count < 0:
        raise ValueError("missing or invalid Editor count")
    instances = data.get("instances")
    if not isinstance(instances, list) or len(instances) != count:
        raise ValueError("missing or inconsistent Editor instances")
    target = os.path.normcase(os.path.realpath(sys.argv[1]))
    matching = False
    for instance in instances:
        path = instance.get("projectPath") if isinstance(instance, dict) else None
        if not isinstance(path, str) or not os.path.isabs(path):
            raise ValueError("Editor project path is unknown")
        matching |= os.path.normcase(os.path.realpath(path)) == target
    print("open" if matching else "closed")
except (ValueError, TypeError) as error:
    print("Could not determine Unity Editor state: " + str(error), file=sys.stderr)
    sys.exit(2)
' "$(native_path "$project_path")") || return 2
	case "$editor_state" in
		open) return 0 ;;
		closed) return 1 ;;
		*) return 2 ;;
	esac
}

require_editor_closed() {
	if unity_editor_open "$1" "$2"; then
		echo "Unity Editor is open for this project. Close it before running batch operations: $1" >&2
		return 2
	else
		editor_state_exit=$?
	fi
	if [ "$editor_state_exit" -ne 1 ]; then
		echo "Unity Editor state is unknown. Batch operations were not started: $1" >&2
		return 2
	fi
}

require_editor_open() {
	if ! unity_editor_open "$1" "$2"; then
		echo "Unity Editor is not open or reachable for this project: $1" >&2
		return 2
	fi
}

snapshot_project_settings() {
	if [ -e "$2" ]; then
		echo "Refusing to overwrite a settings snapshot: $2" >&2
		return 2
	fi
	cp "$1/ProjectSettings/ProjectSettings.asset" "$2" || return 2
	cmp -s "$1/ProjectSettings/ProjectSettings.asset" "$2" || return 2
}

restore_project_settings() {
	# Restore the exact pre-run bytes after the Editor exits, including omitted defaults.
	require_editor_closed "$1" "$2" || return 2
	if cp "$3" "$1/ProjectSettings/ProjectSettings.asset" && \
		cmp -s "$3" "$1/ProjectSettings/ProjectSettings.asset"; then
		return 0
	fi
	echo "ProjectSettings restoration failed. Preserve the snapshot: $3" >&2
	return 2
}

native_path() {
	path=$1
	case "$(uname -s)" in
		MINGW*|MSYS*|CYGWIN*)
			if command -v cygpath >/dev/null 2>&1; then
				cygpath -w "$path"
			else
				echo "$path"
			fi
			;;
		*) echo "$path" ;;
	esac
}

unity_editor_path() {
	project_path=$1
	unity_cli=$2
	version=$(unity_editor_version "$project_path")
	if [ -n "${UNITY_EDITOR:-}" ]; then
		echo "$UNITY_EDITOR"
		return 0
	fi
	install_directory=$("$unity_cli" editors path "$version" --no-banner --non-interactive)
	case "$(uname -s)" in
		Darwin*) echo "$install_directory/Unity.app/Contents/MacOS/Unity" ;;
		MINGW*|MSYS*|CYGWIN*) echo "$install_directory/Editor/Unity.exe" ;;
	*) echo "$install_directory/Editor/Unity" ;;
	esac
}

python_executable() {
	if [ -n "${PYTHON_BIN:-}" ]; then
		echo "$PYTHON_BIN"
		return 0
	fi
	if command -v python3 >/dev/null 2>&1; then
		command -v python3
		return 0
	fi
	if command -v python >/dev/null 2>&1; then
		command -v python
		return 0
	fi
	echo 'Python 3 was not found. Set PYTHON_BIN or add python3/python to PATH.' >&2
	return 2
}
