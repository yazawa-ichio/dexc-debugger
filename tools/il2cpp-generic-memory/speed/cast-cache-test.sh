#!/bin/sh
# Builds and runs speed/cast-cache-test.cpp against a copy of Il2CppCastCache.h.
# usage: cast-cache-test.sh <folder that holds Il2CppCastCache.h> [windows|android] [seconds for each run]
#   windows  MSVC (Visual Studio 2022), runs here
#   android  the clang of the Editor's NDK for arm64, pushed to /data/local/tmp of the one connected device and run there
# The build goes to .local/speed-unit, which Git does not see. On the device only the test's own file is written.
set -eu
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
header=$(cd "$1" && pwd)
target=${2:-windows}
seconds=${3:-2}
out="$here/../.local/speed-unit"
mkdir -p "$out"
editor="/c/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Data/PlaybackEngines/AndroidPlayer"

if [ "$target" = windows ]; then
	cat > "$out/build.bat" <<EOF
@echo off
call "C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\VC\\Auxiliary\\Build\\vcvars64.bat" >nul 2>nul
cl /nologo /O2 /EHsc /W3 /I "$(cygpath -w "$header")" /Fo"$(cygpath -w "$out")\\\\" /Fe"$(cygpath -w "$out")\\cast-cache-test.exe" "$(cygpath -w "$here/cast-cache-test.cpp")"
EOF
	cmd //c "$(cygpath -w "$out/build.bat")"
	"$out/cast-cache-test.exe" "$seconds"
else
	clang="$editor/NDK/toolchains/llvm/prebuilt/windows-x86_64/bin/clang++.exe"
	"$clang" --target=aarch64-linux-android25 -O2 -std=c++14 -Wall -pthread -static-libstdc++ \
		-I "$(cygpath -w "$header")" -o "$(cygpath -w "$out/cast-cache-test-arm64")" "$(cygpath -w "$here/cast-cache-test.cpp")"
	adb="$editor/SDK/platform-tools/adb.exe"
	MSYS_NO_PATHCONV=1 "$adb" push "$(cygpath -w "$out/cast-cache-test-arm64")" /data/local/tmp/cast-cache-test > /dev/null
	MSYS_NO_PATHCONV=1 "$adb" shell "chmod 755 /data/local/tmp/cast-cache-test && /data/local/tmp/cast-cache-test $seconds"
fi
