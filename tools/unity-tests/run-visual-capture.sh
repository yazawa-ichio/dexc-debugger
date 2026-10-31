#!/bin/sh
# Captures the Runtime menus of all three renderers as PNG and verifies the manifests.
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
project_argument=${DEXC_PROJECT_PATH:-$(pwd)}

. "$script_directory/common.sh"
project_path=$(unity_project_path "$project_argument")
unity_cli=$(unity_cli_path)
require_editor_closed "$project_path" "$unity_cli"

stamp=$(date +%Y%m%d-%H%M%S)
log_directory=$(unity_log_directory "$project_path")
export DEXC_VISUAL_CAPTURE_MANIFEST="$log_directory/VisualCapture-$stamp.json"
export DEXC_RUNTIME_UI_CAPTURE_MANIFEST="$log_directory/RuntimeIMGUI-VisualCapture-$stamp.json"
export DEXC_UGUI_CAPTURE_MANIFEST="$log_directory/RuntimeUGUI-VisualCapture-$stamp.json"
export DEXC_UITOOLKIT_CAPTURE_MANIFEST="$log_directory/RuntimeUIToolkit-VisualCapture-$stamp.json"
export DEXC_UITOOLKIT_VERIFIER_RESULT="$log_directory/RuntimeUIToolkit-PixelVerifier-$stamp.json"
export DEXC_VISUAL_CAPTURE_STAMP="$stamp"
export DEXC_TEST_STAMP="$stamp"

"$script_directory/run-tests.sh" --project "$project_path" --mode PlayMode --visual-capture
