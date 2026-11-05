#!/bin/sh
# Builds the IL2CPP autopilot Player, runs it, and talks to it.
#   run-autopilot-player.sh build
#   run-autopilot-player.sh refs
#   run-autopilot-player.sh start | stop
#   run-autopilot-player.sh load <bot.dll>
#   run-autopilot-player.sh status
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_directory/common.sh"

project_argument=${DEXC_PROJECT_PATH:-$(pwd)}
project_path=$(unity_project_path "$project_argument")
unity_cli=$(unity_cli_path)
log_directory=$(unity_log_directory "$project_path")
output_directory="$log_directory/Autopilot"
player_directory="$output_directory/Player"
player_path="$player_directory/DexcAutopilot.exe"
token_file="$output_directory/token"
pid_file="$output_directory/player.pid"
port=${DEXC_AUTOPILOT_PORT:-7890}
stamp=${DEXC_TEST_STAMP:-$(date +%Y%m%d-%H%M%S)}
action=${1:-}

remote()
{
	"$(python_executable)" "$script_directory/dexc-remote.py" "$(native_path "$token_file")" "$port" "$@"
}

build()
{
	require_editor_closed "$project_path" "$unity_cli"
	editor=$(unity_editor_path "$project_path" "$unity_cli")
	mkdir -p "$output_directory"
	rm -f "$output_directory/build-report.json"
	settings_backup="$output_directory/ProjectSettings-before-$stamp.asset"
	snapshot_project_settings "$project_path" "$settings_backup"
	set +e
	DEXC_AUTOPILOT_BUILD_PATH="$(native_path "$player_path")" \
	DEXC_AUTOPILOT_BUILD_REPORT="$(native_path "$output_directory/build-report.json")" \
		"$editor" -batchmode -quit -nographics -projectPath "$(native_path "$project_path")" \
		-executeMethod DexcDebugger.Verification.DexcAutopilotPlayerBuildDriver.BuildAutopilotPlayer \
		-logFile "$(native_path "$log_directory/AutopilotBuild-$stamp.log")"
	build_exit=$?
	set -e
	restore_project_settings "$project_path" "$unity_cli" "$settings_backup" || exit 2
	# The Pipeline package leaves its empty folders behind after a build. rmdir refuses one that still holds anything.
	for leftover in "$project_path/Assets/Settings/Pipeline/Resources" "$project_path/Assets/Settings/Pipeline"; do
		if [ -d "$leftover" ] && rmdir "$leftover" 2>/dev/null; then
			rm -f "$leftover.meta"
		fi
	done
	cat "$output_directory/build-report.json" 2>/dev/null || true
	[ "$build_exit" -eq 0 ] && refs
	exit "$build_exit"
}

# What dexc-eval.py compiles against: the assemblies of the Player just built, kept beside it so a later build of something
# else cannot change them. Only the game's are publicized, because only the game's private members are opened to a bot.
refs()
{
	stripped="$project_path/Library/Bee/artifacts/WinPlayerBuildProgram/ManagedStripped"
	references="$output_directory/refs"
	rm -rf "$references"
	mkdir -p "$references"
	cp "$stripped"/*.dll "$references/"
	"$project_path/Autopilot/Tools~/Publicize/bin/Release/net10.0/Publicize.exe" "$(native_path "$references")" 		"$(native_path "$stripped/DexcDebugger.Samples.StealthStageDemo.Gameplay.dll")"
	{
		echo "-nologo -noconfig -nostdlib -target:library -langversion:9 -optimize- -debug- -nowarn:CS0162,CS1701,CS1702"
		for assembly in "$references"/*.dll; do
			echo "-r:\"$(native_path "$assembly")\""
		done
	} > "$output_directory/refs.rsp"
	sdk=$(dotnet --list-sdks | tail -n 1 | sed -E 's/^([^ ]+) \[(.*)\]$/\2\/\1/')
	echo "$sdk/Roslyn/bincore/csc.dll" > "$output_directory/csc.txt"
}

start()
{
	"$(python_executable)" -c "import secrets; print(secrets.token_hex(16))" > "$token_file"
	# A window by default, so a run can be watched. DEXC_AUTOPILOT_HEADLESS=1 is for machines with nobody at them.
	display="-screen-fullscreen 0 -screen-width 1024 -screen-height 640"
	if [ "${DEXC_AUTOPILOT_HEADLESS:-0}" = 1 ]; then
		display="-batchmode"
	fi
	# shellcheck disable=SC2086
	nohup "$player_path" $display -logFile "$(native_path "$log_directory/AutopilotRun-$stamp.log")" \
		-dexc-autopilot-token "$(cat "$token_file")" -dexc-autopilot-port "$port" >/dev/null 2>&1 &
	echo $! > "$pid_file"
	waited=0
	until remote status >/dev/null 2>&1; do
		if [ "$waited" -ge 30 ] || ! kill -0 "$(cat "$pid_file")" 2>/dev/null; then
			echo "The Player did not answer. Log: $log_directory/AutopilotRun-$stamp.log" >&2
			exit 1
		fi
		sleep 1
		waited=$((waited + 1))
	done
	echo "Player is up: $(cat "$pid_file")"
}

stop()
{
	if [ -f "$pid_file" ]; then
		player=$(cat "$pid_file")
		kill "$player" 2>/dev/null || true
		# The port stays taken until the process is gone, and the next start needs it.
		waited=0
		while kill -0 "$player" 2>/dev/null && [ "$waited" -lt 20 ]; do
			sleep 1
			waited=$((waited + 1))
		done
	fi
	rm -f "$pid_file" "$token_file"
}

# DEXC_AUTOPILOT_VM picks the VM. The Pipeline interpreter only gets a patched copy of a bot; see Autopilot/Tools~/Guard.
load()
{
	vm=${DEXC_AUTOPILOT_VM:-ilruntime}
	bot=$1
	if [ "$vm" = pipeline ]; then
		patched="$output_directory/eval/$(basename "$bot")"
		mkdir -p "$output_directory/eval"
		cp "$bot" "$patched"
		"$project_path/Autopilot/Tools~/Guard/Patch/bin/Release/net10.0/Dexc.Guard.Patch.exe" \
			"$(native_path "$patched")" "$(native_path "$output_directory/refs")"
		bot=$patched
	fi
	remote "load:$vm" "$(native_path "$bot")"
}

case "$action" in
	build) build ;;
	refs) refs ;;
	start) start ;;
	# Brings the Player back if it has gone: a crash in native code takes the whole process with it.
	ensure) remote status >/dev/null 2>&1 || { stop; start; } ;;
	stop) stop ;;
	status) remote status ;;
	load) load "$2" ;;
	*)
		echo "usage: run-autopilot-player.sh build | refs | start | stop | status | load <bot.dll>" >&2
		exit 2
		;;
esac
