"""Times built Players: the kernels of Bench.cs, InterfaceBench.cs and SpeedBench.cs, the first use of the types of a
few patterns, and the start of the process. The speed checks of agent C; c-bench.sh calls this.

usage: c-bench.py <result name> <runs> <condition>...
  <result name>  results/<result name>/<time of this call>/ gets one file for each run. The name starts with c-.
  <condition>    <label>:<set>[:<name>]  a folder of Logs/UnityTests/GenericMemory, the three digits of the lazy
                 switches (methods, rgctx, interface slots), and the name the condition has in the tables
                 (<label>@<set> when left out). A fourth digit 1 turns on the cast and dispatch caches of a Player
                 whose libil2cpp has .local/c/patch-paircache.py (GENERIC_MEMORY_PAIR_CACHE).
The conditions take turns: every one runs once, then every one runs a second time, and so on. What else the machine
does while this runs then falls on all of them alike.
  C_BENCH_MODES="kernels itf speed first start"   what to run (all of it by default)
  C_BENCH_FIRST="ListUse DictUse ..."              the patterns whose first use is timed, 200 types each

Why this is not a shell loop around "timeout <Player>": a Player of this project that has written its result takes
another 20 to 55 seconds until its process is gone, nearly all of it waiting (measured 2026-11-12: 10 s idle and up to
15 s of one core on a normal quit). A run is therefore over when its result file is written, not when the process is
gone, and the next one starts then. Nothing is killed: a Player that is still leaving is left alone, and no more than
MAX_LEAVING of them are allowed at a time (one that left by the switch below holds about 170 MB and no processor).
A Player whose libil2cpp does not have that switch quits the normal way, which was seen to take 17 to 33 seconds of
processor time: for such a Player the run waits until the process is gone, so that this does not fall on the next run.

Two switches of the libil2cpp clones of agent C are set for every run (a Player without them ignores them):
  GENERIC_MEMORY_COUNTERS=0            the counters of the bench runtime are off, so the times do not hold their cost
                                       (.local/c/patch-timing.py)
  GENERIC_MEMORY_FAST_EXIT_FILE=<out>  the process ends itself when its result file is closed, and so uses no
                                       processor while it is leaving (.local/c/patch-fastexit2.py)
c-stats.py puts the results side by side.
"""
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
PLAYERS = os.path.normpath(os.path.join(HERE, "..", "..", "Logs", "UnityTests", "GenericMemory"))
MAX_LEAVING = 12
leaving = []
hooked = {}


def ends_itself(label):
    """Whether the Player's libil2cpp knows GENERIC_MEMORY_FAST_EXIT_FILE (.local/c/patch-fastexit2.py)."""
    if label not in hooked:
        with open(os.path.join(PLAYERS, label, "GameAssembly.dll"), "rb") as file:
            hooked[label] = b"GENERIC_MEMORY_FAST_EXIT_FILE" in file.read()
        if not hooked[label]:
            print("%s quits the normal way: its runs wait for the process to be gone" % label, file=sys.stderr, flush=True)
    return hooked[label]


def wait_for_room():
    while True:
        leaving[:] = [process for process in leaving if process.poll() is None]
        if len(leaving) < MAX_LEAVING:
            return
        time.sleep(0.2)


def complete(path):
    """True once the result file is there and whoever wrote it has closed it."""
    try:
        if os.path.getsize(path) == 0:
            return False
        # The writer holds the file open for writing without sharing it for writing; opening it for append fails until then.
        with open(path, "ab"):
            return True
    except OSError:
        return False


def player_run(label, switches, limit, output, arguments):
    """Runs one Player until its result file is written. Returns the seconds from the start of the process to then."""
    player = os.path.join(PLAYERS, label, "GenericMemory.exe")
    if not os.path.isfile(player):
        print("%s: no Player %s" % (output, label), file=sys.stderr, flush=True)
        return None
    environment = dict(os.environ)
    environment["GENERIC_MEMORY_COUNTERS"] = "0"
    environment["GENERIC_MEMORY_FAST_EXIT_FILE"] = output
    environment["GENERIC_MEMORY_LAZY_METHODS"] = switches[0:1]
    environment["GENERIC_MEMORY_LAZY_RGCTX"] = switches[1:2]
    environment["GENERIC_MEMORY_LAZY_INTERFACES"] = switches[2:3]
    environment["GENERIC_MEMORY_PAIR_CACHE"] = switches[3:4]
    wait_for_room()
    started = time.perf_counter()
    process = subprocess.Popen([player, "-batchmode", "-nographics"] + arguments + ["-generic-memory-output", output, "-logFile", output + ".log"],
                               stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=environment)
    while True:
        if complete(output):
            seconds = time.perf_counter() - started
            if ends_itself(label):
                leaving.append(process)
            else:
                process.wait()
            return seconds
        if process.poll() is not None:
            # Gone: either the result is there now, or the run failed.
            if complete(output):
                return time.perf_counter() - started
            print("%s: no result, Player exit %s" % (output, process.returncode), file=sys.stderr, flush=True)
            return None
        if time.perf_counter() - started > limit:
            print("%s: no result after %d s, left running" % (output, limit), file=sys.stderr, flush=True)
            leaving.append(process)
            return None
        time.sleep(0.02)


def main():
    name, runs = sys.argv[1], int(sys.argv[2])
    conditions = sys.argv[3:]
    if not name.startswith("c-"):
        sys.exit("the result name starts with c-: " + name)
    modes = os.environ.get("C_BENCH_MODES", "kernels itf speed first start").split()
    patterns = os.environ.get("C_BENCH_FIRST", "ListUse DictUse UserClass LinqHeavy").split()
    results = os.path.join(HERE, "results", name, time.strftime("%m%d-%H%M%S"))
    os.makedirs(results)
    with open(os.path.join(results, "conditions.txt"), "w", encoding="utf-8", newline="\n") as file:
        file.write("\n".join(conditions) + "\n")
    for at in range(1, runs + 1):
        # A way to end a queued or running call without ending any process: a line with this call's result name in
        # .local/c/stop-bench makes it stop before its next round.
        stop = os.path.join(HERE, ".local", "c", "stop-bench")
        if os.path.isfile(stop) and name in open(stop, encoding="utf-8").read().split():
            print("stopped before round %d: %s is named in %s" % (at, name, stop), flush=True)
            break
        for condition in conditions:
            parts = condition.split(":")
            label, switches = parts[0], parts[1]
            shown = parts[2] if len(parts) > 2 and parts[2] else label + "@" + switches

            def output(kind):
                return os.path.join(results, "%s.%s.%d.txt" % (shown, kind, at))

            for mode in modes:
                if mode == "kernels":
                    player_run(label, switches, 300, output("kernels"), ["-generic-memory-bench"])
                elif mode == "itf":
                    player_run(label, switches, 300, output("itf"), ["-generic-memory-interface-bench"])
                elif mode == "speed":
                    player_run(label, switches, 600, output("speed"), ["-generic-memory-speed-bench"])
                elif mode == "first":
                    for pattern in patterns:
                        player_run(label, switches, 120, output("first." + pattern), ["-generic-memory-pattern", pattern, "-generic-memory-n", "200"])
                elif mode == "start":
                    # The smallest thing the Runner does, one type of ArrayNew: from the start of the process to its result on disk.
                    run = output("start") + ".run"
                    seconds = player_run(label, switches, 120, run, ["-generic-memory-pattern", "ArrayNew", "-generic-memory-n", "1"])
                    if seconds is not None:
                        lines = ["wallMs\t%.0f" % (seconds * 1000.0)]
                        with open(run, encoding="utf-8") as file:
                            lines += [line.rstrip("\n") for line in file if line.split("\t")[0] in ("initBeginMs", "initMs", "snapshotMs", "counters")]
                        with open(output("start"), "w", encoding="utf-8", newline="\n") as file:
                            file.write("\n".join(lines) + "\n")
                else:
                    sys.exit("unknown mode " + mode)
        print("round %d of %d done, %s" % (at, runs, time.strftime("%H:%M:%S")), flush=True)
    print(results.replace("\\", "/"), flush=True)


main()
