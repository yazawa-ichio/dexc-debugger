#!/bin/sh
# The loop of SpeedBench.IndexArray on its own, on the connected Android device: 16 passes over an array of 1 << 20
# references to 1024 objects. It was written to find out why that kernel is slower in a Player built with -O3
# (2.31 -> 3.6 ns an element). The kernels are the C++ of the loop compiled with the flags of the Player (os, o2, o3,
# o3 without unrolling) and the machine code of the two Players themselves (asmos, asmo3; asmo3single is the -O3 loop
# once inside a loop of 16 passes).
# usage: array-walk.sh build
#        array-walk.sh run <taskset mask> [rounds] [log2 of the count] [spacing]
#        array-walk.sh layout <result file of the speed bench>
#   build    compiles with the clang of the Editor's NDK into .local/speed-unit, writes the disassembly beside the
#            binary and pushes the binary to /data/local/tmp/array-walk
#   run      f0 = the big cores of a Pixel 3 XL, 0f = the small ones. The spacing is the distance in bytes between two
#            of the 1024 objects (32 by default); a negative one puts them in a shuffled order; @<file on the device>
#            puts them at the offsets the file lists
#   layout   takes the line "layout<tab>items<tab>..." that SpeedBench writes (the offsets of its 1024 objects) out of
#            a result and pushes it to /data/local/tmp/array-walk-layout.txt, for run ... @/data/local/tmp/array-walk-layout.txt
# On the device only these two files of /data/local/tmp are written.
set -eu
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
editor="/c/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Data/PlaybackEngines/AndroidPlayer"
bin="$editor/NDK/toolchains/llvm/prebuilt/windows-x86_64/bin"
adb="$editor/SDK/platform-tools/adb.exe"
out="$here/../.local/speed-unit/array-walk"
# What every compile node of the Android Player gets (e-android-keep.py writes it to flags.txt), without the warnings.
common="-march=armv8-a -fomit-frame-pointer -std=c++17 -fvisibility=hidden -fexceptions -fno-rtti -funwind-tables -g -Os -fPIC -fno-strict-overflow -ffunction-sections -fdata-sections -fstack-protector -target aarch64-linux-android25 -fstrict-aliasing"

kernel() {
	name=$1
	shift
	"$bin/clang++.exe" $common "$@" -DNAME=walk_$name -c -o "$(cygpath -w "$out/kernel-$name.o")" "$(cygpath -w "$here/array-walk-kernel.cpp")"
}

case "${1:-}" in
	build)
		mkdir -p "$out"
		kernel os
		kernel o2 -O2
		kernel o3 -O3
		kernel o3nu -O3 -fno-unroll-loops
		"$bin/clang++.exe" -target aarch64-linux-android25 -c -o "$(cygpath -w "$out/player.o")" "$(cygpath -w "$here/array-walk-player.S")"
		"$bin/clang++.exe" $common -O2 -static-libstdc++ -o "$(cygpath -w "$out/array-walk")" "$(cygpath -w "$here/array-walk-main.cpp")" \
			"$(cygpath -w "$out/kernel-os.o")" "$(cygpath -w "$out/kernel-o2.o")" "$(cygpath -w "$out/kernel-o3.o")" "$(cygpath -w "$out/kernel-o3nu.o")" "$(cygpath -w "$out/player.o")"
		"$bin/llvm-objdump.exe" -d --no-show-raw-insn "$(cygpath -w "$out/array-walk")" > "$out/array-walk.dis"
		MSYS_NO_PATHCONV=1 "$adb" push "$(cygpath -w "$out/array-walk")" /data/local/tmp/array-walk > /dev/null
		MSYS_NO_PATHCONV=1 "$adb" shell "chmod 755 /data/local/tmp/array-walk"
		;;
	run)
		MSYS_NO_PATHCONV=1 "$adb" shell "taskset $2 /data/local/tmp/array-walk ${3:-5} ${4:-20} ${5:-32}"
		;;
	layout)
		mkdir -p "$out"
		grep -a "^layout	items	" "$2" | cut -f3 > "$out/layout.txt"
		MSYS_NO_PATHCONV=1 "$adb" push "$(cygpath -w "$out/layout.txt")" /data/local/tmp/array-walk-layout.txt > /dev/null
		;;
	*)
		echo "usage: array-walk.sh build | run <taskset mask> [rounds] [log2 of the count] [spacing] | layout <result file>" >&2
		exit 1 ;;
esac
