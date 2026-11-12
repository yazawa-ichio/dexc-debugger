#!/bin/sh
# Makes a new il2cpp folder in .local whose libil2cpp is a copy of another folder's, patches and counters included.
# kit/make-copy.sh starts from the Editor's libil2cpp instead; this one starts from a variant that works already.
# usage: clone-il2cpp.sh <folder of .local to copy> <name of the new folder>
set -eu
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
local="$here/../.local"
from=$1
name=$2
if [ ! -f "$local/$from/libil2cpp/il2cpp-config.h" ]; then
	echo "$local/$from is not an il2cpp folder" >&2
	exit 1
fi
if [ -e "$local/$name" ]; then
	echo "$local/$name exists already" >&2
	exit 1
fi
mkdir "$local/$name"
cp -r "$local/$from/libil2cpp" "$local/$name/libil2cpp"
for entry in "$local/il2cpp"/*; do
	item=$(basename "$entry")
	[ "$item" != libil2cpp ] || continue
	if [ -d "$entry" ]; then
		# Everything but libil2cpp is the same in every copy: joined to the first one, as make-copy.sh does.
		cmd //c mklink //J "$(cygpath -w "$local/$name/$item")" "$(cygpath -w "$local/il2cpp/$item")" > /dev/null
	else
		cp "$entry" "$local/$name/$item"
	fi
done
echo "$name: $(ls "$local/$name" | wc -l) entries, libil2cpp from $from"
