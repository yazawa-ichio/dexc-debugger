#!/bin/sh

# Load optional developer-local settings without committing machine-specific paths.
# The file is shell-compatible and should contain assignments such as:
# UNITY_CLI="/Applications/Unity Hub.app/.../unity"
# PYTHON_BIN="/opt/homebrew/bin/python3"
# DEXC_PROJECT_PATH="/path/to/dexc-debugger"

load_local_config() {
	config_file=${DEXC_UNITY_TEST_CONFIG:-${script_directory}/../../.local/unity-tests.env}
	if [ ! -f "$config_file" ]; then
		return 0
	fi

	# Keep values supplied by the caller higher priority than the local file.
	caller_unity_cli=${UNITY_CLI-}
	caller_python_bin=${PYTHON_BIN-}
	caller_project_path=${DEXC_PROJECT_PATH-}
	caller_unity_editor=${UNITY_EDITOR-}

	set -a
	. "$config_file"
	set +a

	if [ -n "$caller_unity_cli" ]; then UNITY_CLI=$caller_unity_cli; export UNITY_CLI; fi
	if [ -n "$caller_python_bin" ]; then PYTHON_BIN=$caller_python_bin; export PYTHON_BIN; fi
	if [ -n "$caller_project_path" ]; then DEXC_PROJECT_PATH=$caller_project_path; export DEXC_PROJECT_PATH; fi
	if [ -n "$caller_unity_editor" ]; then UNITY_EDITOR=$caller_unity_editor; export UNITY_EDITOR; fi
}
