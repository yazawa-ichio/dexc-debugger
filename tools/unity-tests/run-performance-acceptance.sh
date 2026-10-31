#!/bin/sh
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_directory/common.sh"

project_argument=${DEXC_PROJECT_PATH:-$(pwd)}
project_path=$(unity_project_path "$project_argument")
stamp=${DEXC_TEST_STAMP:-$(date +%Y%m%d-%H%M%S)}
log_directory=$(unity_log_directory "$project_path")
result="$log_directory/I18-Performance-$stamp.json"

if [ -e "$result" ]; then
	printf '%s\n' "Refusing to reuse an existing I18 performance artifact: $result" >&2
	exit 2
fi

DEXC_I18_PERFORMANCE_RESULT="$result" \
	DEXC_TEST_STAMP="$stamp" \
	sh "$script_directory/run-playmode-tests.sh"

python_bin=$(python_executable)
"$python_bin" - "$result" <<'PY'
import json
import sys

path = sys.argv[1]
with open(path, encoding="utf-8") as stream:
    evidence = json.load(stream)
checks = {
    "unityVersion": evidence.get("UnityVersion") == "6000.3.20f1",
    "commandFixture": evidence.get("CommandFixtureCount", 0) >= 16,
    "settingFixture": evidence.get("SettingFixtureCount", 0) >= 16,
    "reflectionCacheReuse": evidence.get("QueryMilliseconds", 10**9) < 1000,
    "queryAllocationBound": evidence.get("QueryAllocatedBytes", 10**12) < 8 * 1024 * 1024,
    "hierarchyBudget": evidence.get("SearchMaxInspectedPerFrame", 10**9) <= evidence.get("SearchWorkPerFrame", 0),
    "hierarchyFixture": evidence.get("SearchInspectedCount", 0) >= 256,
    "watchFixture": evidence.get("WatchCount", 0) == 32,
    "visualFixture": evidence.get("MaxVisualsObserved", 0) == 256,
    "providerCache": evidence.get("ProviderCanEditCalls", 10**9) == 2,
    "imguiWidth": 260 <= evidence.get("PanelWidth", 0) <= 480,
    "imguiHeight": 220 <= evidence.get("PanelHeight", 0) <= 640,
    "imguiRow": evidence.get("ListRowHeight") == 38,
}
print(json.dumps({"checks": checks, "evidence": evidence}, indent=2))
if not all(checks.values()):
    raise SystemExit(1)
PY

echo "I18 performance acceptance passed: $result"
