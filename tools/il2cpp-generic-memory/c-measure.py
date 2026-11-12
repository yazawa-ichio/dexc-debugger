"""measure.sh for agent C: every pattern of a built Player at two counts, one result file for each run, the counters on.
The same Player arguments and the same result files as measure.sh, so report.py, kit/same.py and
.local/c/heap-compare.py read them. The difference is how a run ends: this does not wait for the Player's process to
be gone (which takes 20 to 55 seconds for this project's Player; see c-bench.py), only for its result file.

usage: c-measure.py <label> <set> [<result name>]    -> results/<result name>, by default results/<label>-<set>
  <set>  the digits of the lazy switches: methods, rgctx, interface slots, and optionally the pair caches
  GENERIC_MEMORY_PATTERNS="A B"  the patterns (by default the 15 of the shared table)
The result name starts with c-. The folder must not hold results already: nothing is removed.
"""
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))
PLAYERS = os.path.join(PROJECT, "Logs", "UnityTests", "GenericMemory")
GENERATED = os.path.join(PROJECT, "Assets", "Tests", "GenericMemory", "Scenario", "Generated")
SHARED = "ArrayNew ArrayUse DictUse EnumDict HashSetUse LinqBasic LinqHeavy ListMore ListNew ListUse StructList TaskT UniTaskT UserClass UserMethod"
MAX_LEAVING = 6


def complete(path):
    try:
        if os.path.getsize(path) == 0:
            return False
        with open(path, "ab"):
            return True
    except OSError:
        return False


def main():
    label, switches = sys.argv[1], sys.argv[2]
    name = sys.argv[3] if len(sys.argv) > 3 else label + "-" + switches
    if not name.startswith("c-"):
        sys.exit("the result name starts with c-: " + name)
    results = os.path.join(HERE, "results", name)
    if os.path.isdir(results) and any(item.endswith(".txt") for item in os.listdir(results)):
        sys.exit(results + " holds results already; give another result name")
    os.makedirs(results, exist_ok=True)
    player = os.path.join(PLAYERS, label, "GenericMemory.exe")
    patterns = os.environ.get("GENERIC_MEMORY_PATTERNS", SHARED).split()
    environment = dict(os.environ)
    environment["GENERIC_MEMORY_COUNTERS"] = "1"
    environment["GENERIC_MEMORY_LAZY_METHODS"] = switches[0:1]
    environment["GENERIC_MEMORY_LAZY_RGCTX"] = switches[1:2]
    environment["GENERIC_MEMORY_LAZY_INTERFACES"] = switches[2:3]
    environment["GENERIC_MEMORY_PAIR_CACHE"] = switches[3:4]
    leaving = []
    for pattern in patterns:
        with open(os.path.join(GENERATED, pattern + ".cs"), encoding="utf-8") as file:
            types = int(re.search(r"public const int Types = (\d+);", file.read()).group(1))
        for count in (types // 2, types):
            output = os.path.join(results, "%s-%d.txt" % (pattern, count))
            environment["GENERIC_MEMORY_FAST_EXIT_FILE"] = output
            while True:
                leaving[:] = [process for process in leaving if process.poll() is None]
                if len(leaving) < MAX_LEAVING:
                    break
                time.sleep(0.2)
            started = time.perf_counter()
            process = subprocess.Popen([player, "-batchmode", "-nographics", "-generic-memory-pattern", pattern, "-generic-memory-n", str(count),
                                        "-generic-memory-output", output, "-logFile", os.path.join(results, "%s-%d.log" % (pattern, count))],
                                       stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=environment)
            while True:
                if complete(output):
                    leaving.append(process)
                    break
                if process.poll() is not None:
                    if not complete(output):
                        print("%s %d: no result, Player exit %s" % (pattern, count, process.returncode), file=sys.stderr, flush=True)
                    break
                if time.perf_counter() - started > 180:
                    print("%s %d: no result after 180 s, left running" % (pattern, count), file=sys.stderr, flush=True)
                    leaving.append(process)
                    break
                time.sleep(0.02)
    subprocess.run([sys.executable, os.path.join(HERE, "report.py"), results])


main()
