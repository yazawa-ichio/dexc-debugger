#!/bin/sh
# The read of an rgctx entry in shared generic code, on its own: what the test of the lazy patch costs for each read,
# with the compiler and the flags of the Android Player (arm64, the clang of the Editor's NDK). Five ways of reading
# (stock, the test as it was, the test with one read, that with the empty case marked as unlikely, and that with a
# resolver that keeps the caller's registers), each at -Os (Android) and at -O3 (what iOS uses), in a loop and in a
# method that only passes on.
# usage: rgctx-read.sh build              compiles into .local/speed-unit/rgctx-read, writes the disassembly beside
#                                         the binary and pushes the binary to /data/local/tmp/rgctx-read
#        rgctx-read.sh run <taskset mask> [rounds]   f0 = the big cores of a Pixel 3 XL, 0f = the small ones
#        rgctx-read.sh loops              prints the disassembly of the kernels
# On the device only /data/local/tmp/rgctx-read is written.
set -eu
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
editor="/c/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Data/PlaybackEngines/AndroidPlayer"
bin="$editor/NDK/toolchains/llvm/prebuilt/windows-x86_64/bin"
adb="$editor/SDK/platform-tools/adb.exe"
out="$here/../.local/speed-unit/rgctx-read"
# What every compile node of the Android Player gets (e-android-keep.py writes it to flags.txt), without the warnings.
common="-march=armv8-a -fomit-frame-pointer -std=c++17 -fvisibility=hidden -fexceptions -fno-rtti -funwind-tables -g -Os -fPIC -fno-strict-overflow -ffunction-sections -fdata-sections -fstack-protector -target aarch64-linux-android25 -fstrict-aliasing"

kernel() {
	name=$1
	form=$2
	shift 2
	"$bin/clang++.exe" $common "$@" -DFORM=$form -DNAME=walk_$name -DFORWARD=forward_$name -c -o "$(cygpath -w "$out/kernel-$name.o")" "$(cygpath -w "$here/rgctx-read-kernel.cpp")"
}

case "${1:-}" in
	build)
		mkdir -p "$out"
		objects=
		for flags in os o3; do
			at=0
			for way in stock old new cold keep; do
				if [ "$flags" = o3 ]; then kernel ${way}_$flags $at -O3; else kernel ${way}_$flags $at; fi
				objects="$objects $(cygpath -w "$out/kernel-${way}_$flags.o")"
				at=$((at + 1))
			done
		done
		"$bin/clang++.exe" $common -O2 -static-libstdc++ -o "$(cygpath -w "$out/rgctx-read")" "$(cygpath -w "$here/rgctx-read-main.cpp")" $objects
		"$bin/llvm-objdump.exe" -d --no-show-raw-insn "$(cygpath -w "$out/rgctx-read")" > "$out/rgctx-read.dis"
		MSYS_NO_PATHCONV=1 "$adb" push "$(cygpath -w "$out/rgctx-read")" /data/local/tmp/rgctx-read > /dev/null
		MSYS_NO_PATHCONV=1 "$adb" shell "chmod 755 /data/local/tmp/rgctx-read"
		;;
	run)
		MSYS_NO_PATHCONV=1 "$adb" shell "taskset $2 /data/local/tmp/rgctx-read ${3:-5}"
		;;
	loops)
		awk '/^[0-9a-f]+ <_Z[0-9]+(walk|forward)_/ { show = 1 } /^$/ { show = 0 } show { print }' "$out/rgctx-read.dis"
		;;
	*)
		echo "usage: rgctx-read.sh build | run <taskset mask> [rounds] | loops" >&2
		exit 1 ;;
esac
