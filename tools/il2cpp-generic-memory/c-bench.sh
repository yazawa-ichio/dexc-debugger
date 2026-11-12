#!/bin/sh
# Times built Players (agent C's copy of bench.sh): the kernels of Bench.cs, InterfaceBench.cs and SpeedBench.cs, the
# first use of the types of a few patterns, and the start of the process. It takes the Players and the lazy sets as
# arguments, writes below its own name, and removes nothing. The work is in c-bench.py; see there for the arguments.
# usage: c-bench.sh <result name> <runs> <label>:<set>[:<name>]...
set -eu
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
script_directory="$here/../unity-tests"
. "$script_directory/common.sh"
exec "$(python_executable)" "$here/c-bench.py" "$@"
