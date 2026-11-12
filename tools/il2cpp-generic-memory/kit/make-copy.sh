#!/bin/sh
# Makes a new il2cpp folder for the bench: a fresh copy of the Editor's libil2cpp, and everything else joined to .local/il2cpp.
# usage: make-copy.sh <tools/il2cpp-generic-memory/.local> <name of the new folder>
set -eu
local=$1
name=$2
editor="/c/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Data/il2cpp"
if [ -e "$local/$name" ]; then
	echo "$local/$name exists already"
	exit 1
fi
mkdir "$local/$name"
cp -r "$editor/libil2cpp" "$local/$name/libil2cpp"
for file in Il2CppEagerStaticClassConstructionAttribute.cs Il2CppSetOptionAttribute.cs il2cpp_default_extra_types.txt il2cpp_root; do
	cp "$local/il2cpp/$file" "$local/$name/$file"
done
for folder in Analytics BCLExtensions LinkerDescriptors Unity.Api.Attributes Unity.IL2CPP.Api Unity.IL2CPP.Bee.BuildLogic Unity.IL2CPP.Bee.IL2CPPExeCompileCppBuildProgram.Data Unity.Linker.Api UnityLinker build createdump external il2cpp il2cpp-compile libmono repos; do
	cmd //c mklink //J "$(cygpath -w "$local/$name/$folder")" "$(cygpath -w "$local/il2cpp/$folder")" > /dev/null
done
echo "$name: $(ls "$local/$name" | wc -l) entries"
