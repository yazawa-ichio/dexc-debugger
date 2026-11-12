"""m-run.py under the names of the Android check (d-), for the Windows Players it builds to compare with.

Runs built Players for the combined check of the three tracks, without waiting for a Player's process to be gone.

A Player of this project takes 20 to 55 seconds to quit after it wrote its result (see c-bench.py). A run is over here
when its result file is written and closed. A Player whose libil2cpp has the switch GENERIC_MEMORY_FAST_EXIT_FILE
(.local/c/patch-fastexit2.py) ends itself then; any other one is left to quit on its own. Nothing is killed.

usage: d-win-run.py measure <label> <set> [<result name>]          results/<result name> (default <label>-<set>), must be new
       d-win-run.py checks <label> <mode> <set>...                  results/checks/<label>.<mode>.<set>.txt
       d-win-run.py repeat <label> <mode> <set> <runs> <name>       results/checks/<name>/<set>.<run>.txt, <name> must be new
  <set>   the digits of the switches: lazy methods, lazy rgctx, lazy interface slots, lazy interface classes, then
          GENERIC_MEMORY_LAZY_MORE, whose first digit is the cast cache of the speed branch
  <mode>  checks, reflection, interfaces, comparers or sharing
  GENERIC_MEMORY_PATTERNS="A B"   the patterns of measure (by default the 16 of the reference results)
  M_RUN_WAIT=1                    waits for every process to be gone and reports its exit code (the normal way to quit)
Labels and names start with m- (the combined memory checks) or s- (the speed branch).
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
PATTERNS = ("ArrayNew ArrayUse DelegateUse DictUse EnumDict HashSetUse LinqBasic LinqHeavy ListMore ListNew ListUse "
            "StructList TaskT UniTaskT UserClass UserMethod")
MAX_LEAVING = 10
WAIT = os.environ.get("M_RUN_WAIT") == "1"
leaving = []


def complete(path):
    """True once the result file is there and whoever wrote it has closed it."""
    try:
        if os.path.getsize(path) == 0:
            return False
        with open(path, "ab"):
            return True
    except OSError:
        return False


def run(label, switches, arguments, output, limit, counters):
    """Runs one Player until its result is written. Returns None when there is one, else what went wrong."""
    # ComparerChecks writes its result file again after every check, so that a crash shows where it stopped: such a
    # run is over only when the process is gone.
    wait = WAIT or "-generic-memory-comparers" in arguments
    environment = dict(os.environ)
    environment["GENERIC_MEMORY_COUNTERS"] = counters
    environment["GENERIC_MEMORY_LAZY_METHODS"] = switches[0:1]
    environment["GENERIC_MEMORY_LAZY_RGCTX"] = switches[1:2]
    environment["GENERIC_MEMORY_LAZY_INTERFACES"] = switches[2:3]
    environment["GENERIC_MEMORY_LAZY_INTERFACE_CLASSES"] = switches[3:4]
    environment["GENERIC_MEMORY_LAZY_MORE"] = switches[4:]
    if not wait:
        environment["GENERIC_MEMORY_FAST_EXIT_FILE"] = output
    while True:
        leaving[:] = [process for process in leaving if process.poll() is None]
        if len(leaving) < MAX_LEAVING:
            break
        time.sleep(0.2)
    started = time.perf_counter()
    process = subprocess.Popen([os.path.join(PLAYERS, label, "GenericMemory.exe"), "-batchmode", "-nographics"] + arguments
                               + ["-generic-memory-output", output, "-logFile", output[:-4] + ".log"],
                               stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=environment)
    while True:
        if wait:
            try:
                process.wait(timeout=limit)
            except subprocess.TimeoutExpired:
                leaving.append(process)
                return "no exit after %d s, left running" % limit
            if not complete(output):
                return "no result, Player exit %s" % process.returncode
            return None if process.returncode == 0 else "Player exit %s" % process.returncode
        if complete(output):
            leaving.append(process)
            return None
        if process.poll() is not None:
            return None if complete(output) else "no result, Player exit %s" % process.returncode
        if time.perf_counter() - started > limit:
            leaving.append(process)
            return "no result after %d s, left running" % limit
        time.sleep(0.02)


def own(name):
    if not name.startswith("d-"):
        sys.exit("a label or a name here starts with d-: " + name)


def lines_of(path):
    with open(path, encoding="utf-8", errors="replace") as file:
        return file.read().split("\n")


def counted(path):
    lines = lines_of(path)
    total = [line for line in lines if line.startswith("check\t")]
    passed = [line for line in total if "\tPASS\t" in line + "\t" or line.rstrip().endswith("\tPASS")]
    failed = [line for line in total if line not in passed]
    return len(passed), len(total), failed


def measure(label, switches, name):
    own(label)
    own(name)
    results = os.path.join(HERE, "results", name)
    if os.path.isdir(results) and any(item.endswith(".txt") for item in os.listdir(results)):
        sys.exit(results + " holds results already; give another result name")
    os.makedirs(results, exist_ok=True)
    for pattern in os.environ.get("GENERIC_MEMORY_PATTERNS", PATTERNS).split():
        with open(os.path.join(GENERATED, pattern + ".cs"), encoding="utf-8") as file:
            types = int(re.search(r"public const int Types = (\d+);", file.read()).group(1))
        for count in (types // 2, types):
            output = os.path.join(results, "%s-%d.txt" % (pattern, count))
            wrong = run(label, switches, ["-generic-memory-pattern", pattern, "-generic-memory-n", str(count)], output, 180, "1")
            if wrong:
                print("%s %d: %s" % (pattern, count, wrong), flush=True)
    subprocess.run([sys.executable, os.path.join(HERE, "report.py"), results])


def checks(label, mode, sets):
    own(label)
    folder = os.path.join(HERE, "results", "checks")
    os.makedirs(folder, exist_ok=True)
    status = 0
    for switches in sets:
        output = os.path.join(folder, "%s.%s.%s.txt" % (label, mode, switches))
        if os.path.exists(output):
            os.replace(output, output + ".before")
        wrong = run(label, switches, ["-generic-memory-" + mode], output, 900, "1")
        if wrong and not os.path.exists(output):
            print("%s.%s.%s: %s" % (label, mode, switches, wrong), flush=True)
            status = 1
            continue
        passed, total, failed = counted(output)
        print("%s.%s.%s: %d of %d passed%s" % (label, mode, switches, passed, total, ", " + wrong if wrong else ""), flush=True)
        for line in failed:
            print("  " + line, flush=True)
        if wrong or total == 0 or passed != total:
            status = 1
    return status


def repeat(label, mode, switches, runs, name):
    # Any label may be run: a Player is only read. What is written goes under the name, which is one of d's.
    own(name)
    folder = os.path.join(HERE, "results", "checks", name)
    if os.path.exists(folder):
        sys.exit(folder + " exists already; give another name")
    os.makedirs(folder)
    first = None
    good = crashed = otherwise = 0
    for at in range(1, runs + 1):
        output = os.path.join(folder, "%s.%d.txt" % (switches, at))
        wrong = run(label, switches, ["-generic-memory-" + mode], output, 600, "1")
        if not os.path.exists(output):
            crashed += 1
            print("%s run %d: %s" % (switches, at, wrong), flush=True)
            continue
        passed, total, failed = counted(output)
        # The order of the interfaces of a type is the order of the addresses of their classes: not compared.
        lines = [line for line in lines_of(output) if "\tInterfaceOrder\t" not in line]
        if first is None:
            first = lines
        different = sum(1 for a, b in zip(first, lines) if a != b) + abs(len(first) - len(lines)) if mode == "reflection" else 0
        if not wrong and total != 0 and passed == total and different == 0:
            good += 1
        else:
            otherwise += 1
            print("%s run %d: passed %d of %d, lines not as in the first run: %d%s" % (switches, at, passed, total, different, ", " + wrong if wrong else ""), flush=True)
    print("%s %s %s: %d runs, %d good, %d without a result, %d otherwise" % (label, mode, switches, runs, good, crashed, otherwise), flush=True)
    return 0 if good == runs else 1


def main():
    command = sys.argv[1]
    if command == "measure":
        measure(sys.argv[2], sys.argv[3], sys.argv[4] if len(sys.argv) > 4 else sys.argv[2] + "-" + sys.argv[3])
        return 0
    if command == "checks":
        return checks(sys.argv[2], sys.argv[3], sys.argv[4:])
    if command == "repeat":
        return repeat(sys.argv[2], sys.argv[3], sys.argv[4], int(sys.argv[5]), sys.argv[6])
    sys.exit("unknown command " + command)


sys.exit(main())
