"""Runs Android Players of the generic-memory bench on the connected device and times them: the checks of
d-android-run.py for the builds of e-android-build.sh, and c-bench.py for a phone.

usage: e-android-run.py install <label>...
       e-android-run.py uninstall <label>...
       e-android-run.py checks <label> <mode>...                    results/checks/<label>.<mode>.txt
       e-android-run.py bench <result name> <rounds> <condition>... results/<result name>/<time of this call>/
  <label>      a folder of Logs/UnityTests/GenericMemory that e-android-build.sh made (android.json: the application id)
  <mode>       checks, reflection, interfaces, comparers or sharing
  <condition>  <label>[:<name>]  the Player and the name the condition has in the tables (the label when left out).
               The same label under two names measures the same app twice: the difference is the noise.
  E_BENCH_MODES="kernels itf speed numeric first start"   what a round runs (all of it by default); "engine" is the
                                                           bench of calls into the engine (EngineBench.cs), which a
                                                           round runs only when it is named here
  E_BENCH_FIRST="ListUse DictUse UserClass LinqHeavy"      the patterns whose first use is timed, 200 types each
  E_BENCH_PAUSE=<seconds>                                  the rest between two runs (2 by default)
  E_BENCH_NUMERIC_ARGS="-generic-memory-numeric-pointers" more arguments for the numeric bench (here: also call Burst
                                                           through function pointers)

Every run is a cold start: the app's process must be gone before it and is a new one in it, and it is over when that
process is gone again. A phone gets warm and then slower, so a round runs one kind of measurement for every condition
before the next kind, and every round starts with another condition: no condition is always the one that runs on the
coolest or the warmest device. temps.tsv of the result folder has, for every run, the battery's temperature, the
warmest big core, the thermal status and the frequency limit of the big cores, before and after. A run does not
start while the device says it is throttling (thermal status above 0).
The files are named as c-bench.py names them (<name>.<kind>.<round>.txt), so e-stats.py and c-stats.py read them.
Labels and result names start with e-. Only the bench's own packages are ever started, stopped, installed or removed.
"""
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))
PLAYERS = os.path.join(PROJECT, "Logs", "UnityTests", "GenericMemory")
LOGS = os.path.join(HERE, ".local", "e", "logs")
ADB = r"C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
OWN = "com.yazawaichio.genericmemorybench"
# What may be in front when a run starts: the launcher, or one of the bench's own apps that is about to go.
LAUNCHERS = ("com.google.android.apps.nexuslauncher",)


def adb(*arguments, timeout=120, binary=False):
    done = subprocess.run([ADB] + list(arguments), stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)
    return done.stdout if binary else done.stdout.decode("utf-8", errors="replace").replace("\r\n", "\n")


def shell(command, timeout=120):
    return adb("shell", command, timeout=timeout)


def note(text):
    print(text, flush=True)
    os.makedirs(LOGS, exist_ok=True)
    with open(os.path.join(LOGS, "run.log"), "a", encoding="utf-8") as file:
        file.write(time.strftime("%Y-%m-%d %H:%M:%S ") + text + "\n")


def own(name):
    if not name.startswith("e-"):
        sys.exit("not one of e's names: " + name)


def app(label):
    own(label)
    with open(os.path.join(PLAYERS, label, "android.json"), encoding="utf-8") as file:
        package = json.load(file)["applicationId"]
    if not package.startswith(OWN):
        sys.exit("not an application id of the bench: " + package)
    return package


def activity(package):
    lines = [line.strip() for line in shell("cmd package resolve-activity --brief " + package).split("\n") if "/" in line]
    if not lines:
        sys.exit("no launcher activity of " + package + ": is it installed?")
    return lines[-1]


def files(package):
    return "/storage/emulated/0/Android/data/" + package + "/files"


def ready(package):
    """Waits until the device can take a run: awake, not locked, nobody else's app in front, the app's process gone."""
    for attempt in range(90):
        state = shell("dumpsys power 2>/dev/null | grep -m1 mWakefulness=; dumpsys activity activities 2>/dev/null | grep -m1 mResumedActivity; "
                      "dumpsys window 2>/dev/null | grep -m1 mDreamingLockscreen; echo pid=$(pidof " + package + ")")
        if "mWakefulness=Awake" not in state:
            note("the device is not awake (" + state.split("\n")[0].strip() + "): waking it")
            shell("input keyevent KEYCODE_WAKEUP")
            time.sleep(2)
            continue
        if "mDreamingLockscreen=true" in state:
            sys.exit("the device shows its lock screen; stopped here")
        front = re.search(r"mResumedActivity: ActivityRecord\{\S+ \S+ ([^/\s]+)/", state)
        front = front.group(1) if front else "?"
        if front not in LAUNCHERS and not front.startswith(OWN):
            note("another app is in front (" + front + "): waiting 30 s")
            time.sleep(30)
            continue
        pid = re.search(r"pid=(\d+)", state)
        if pid:
            if attempt in (30, 60):
                # The bench's own app, which did not go by itself.
                note("the process " + pid.group(1) + " of " + package + " is still there: am force-stop")
                shell("am force-stop " + package)
            time.sleep(1)
            continue
        return
    sys.exit("the device did not get ready")


def heat():
    """The battery's temperature, the warmest big core, the thermal status, and the frequency the big cores may reach."""
    text = shell("dumpsys battery | grep temperature; dumpsys thermalservice | grep -E 'Thermal Status|Current temperatures from HAL|gold-usr'; "
                 "echo maxfreq=$(cat /sys/devices/system/cpu/cpu7/cpufreq/scaling_max_freq)")
    battery = re.search(r"temperature: (\d+)", text)
    status = re.search(r"Thermal Status: (\d+)", text)
    limit = re.search(r"maxfreq=(\d+)", text)
    # The cached values come first; the ones the HAL has now are after their heading.
    now = text.split("Current temperatures from HAL")[-1]
    cores = [float(value) for value in re.findall(r"mValue=([\d.]+), mType=0, mName=cpu\d-gold-usr", now)]
    return {
        "battery": int(battery.group(1)) / 10.0 if battery else -1.0,
        "core": max(cores) if cores else -1.0,
        "status": int(status.group(1)) if status else -1,
        "limit": int(limit.group(1)) if limit else -1,
    }


def cool():
    """Waits while the device says it is throttling. Returns what heat() said last."""
    for waited in range(60):
        now = heat()
        if now["status"] <= 0:
            return now
        if waited % 6 == 0:
            note("the device is throttling (thermal status %d, battery %.1f, core %.1f): waiting" % (now["status"], now["battery"], now["core"]))
        time.sleep(10)
    return now


def run(package, start, arguments, name, output, limit, timed=False):
    """One cold run. Returns (what went wrong or None, pid, milliseconds on the device from before the start to the result, what am start said)."""
    ready(package)
    remote = files(package) + "/" + name
    stamp = shell("date '+%Y-%m-%d %H:%M:%S.000'").strip()
    line = " ".join(arguments + ["-generic-memory-output", name])
    began = time.perf_counter()
    wall = None
    if timed:
        # On the device, so that no adb round trip is in the time: from before am start until the result file has something in it.
        started = shell("s=$(date +%s%N); am start -W -n " + start + " -e unity '" + line + "'; "
                        "n=0; while [ ! -s " + remote + " ] && [ $n -lt 3000 ]; do sleep 0.02; n=$((n+1)); done; e=$(date +%s%N); echo began=$s ended=$e",
                        timeout=limit + 60)
        # The two times are subtracted here: the device's shell counts in 32 bits.
        found = re.search(r"began=(\d+) ended=(\d+)", started)
        wall = (int(found.group(2)) - int(found.group(1))) // 1000000 if found else None
    else:
        started = shell("am start -W -n " + start + " -e unity '" + line + "'")
    if "Error" in started:
        return "am start: " + started.strip().replace("\n", " | "), None, wall, started
    pid = None
    wrong = None
    while True:
        now = shell("pidof " + package).strip()
        if now:
            pid = pid or now
        elif pid is not None or timed or time.perf_counter() - began > 20:
            # A timed run comes here with its result written: its process may be gone before it was ever seen.
            break
        if time.perf_counter() - began > limit:
            wrong = "no exit after %d s" % limit
            break
        time.sleep(0.5)
    if pid is None and not timed:
        wrong = "the process was never seen"
    data = adb("exec-out", "cat " + remote + " 2>/dev/null", binary=True)
    if data:
        with open(output, "wb") as file:
            file.write(data)
    else:
        wrong = (wrong + ", " if wrong else "") + "no result"
    if wrong:
        with open(output[:-4] + ".logcat.txt", "wb") as file:
            file.write(adb("logcat", "-d", "-T", stamp, binary=True, timeout=300))
        with open(output[:-4] + ".crash.txt", "wb") as file:
            file.write(adb("logcat", "-b", "crash", "-d", "-T", stamp, binary=True, timeout=300))
    return wrong, pid, wall, started


def lines_of(path):
    with open(path, encoding="utf-8", errors="replace") as file:
        return file.read().split("\n")


def counted(path):
    lines = lines_of(path)
    total = [line for line in lines if line.startswith("check\t")]
    passed = [line for line in total if "\tPASS\t" in line + "\t" or line.rstrip().endswith("\tPASS")]
    failed = [line for line in total if line not in passed]
    declared = [line for line in lines if line.startswith("failed\t")]
    return len(passed), len(total), failed, declared[-1].split("\t")[1] if declared else None


def crashes(stamp, where):
    """Keeps the crash buffer since a moment, and says how many of its lines there are."""
    data = adb("logcat", "-b", "crash", "-d", "-T", stamp, binary=True, timeout=300)
    text = [line for line in data.decode("utf-8", errors="replace").split("\n") if line.strip() and not line.startswith("-----")]
    if text:
        with open(where, "wb") as file:
            file.write(data)
    return len(text)


def unique(label):
    return time.strftime("%m%d%H%M%S") + "-" + label


def install(labels):
    for label in labels:
        package = app(label)
        apk = os.path.join(PLAYERS, label, "GenericMemory.apk")
        note("install " + label + " (" + package + "): " + adb("install", "-r", apk, timeout=900).strip().replace("\n", " | "))
        note("  " + shell("dumpsys package " + package + " | grep -E 'versionName|lastUpdateTime|primaryCpuAbi'").strip().replace("\n", " | "))
    return 0


def uninstall(labels):
    for label in labels:
        package = app(label)
        note("uninstall " + label + " (" + package + "): " + adb("uninstall", package, timeout=300).strip())
    return 0


def checks(label, modes):
    package = app(label)
    start = activity(package)
    folder = os.path.join(HERE, "results", "checks")
    os.makedirs(folder, exist_ok=True)
    status = 0
    for mode in modes:
        output = os.path.join(folder, "%s.%s.txt" % (label, mode))
        if os.path.exists(output):
            os.replace(output, output + ".before")
        stamp = shell("date '+%Y-%m-%d %H:%M:%S.000'").strip()
        began = time.perf_counter()
        wrong, pid, wall, started = run(package, start, ["-generic-memory-" + mode], unique(label) + "-" + mode + ".txt", output, 900)
        seconds = time.perf_counter() - began
        crashed = crashes(stamp, output[:-4] + ".crashbuffer.txt")
        if not os.path.exists(output):
            note("%s.%s: %s (pid %s, %.1f s)" % (label, mode, wrong, pid, seconds))
            status = 1
            continue
        passed, total, failed, declared = counted(output)
        note("%s.%s: %d of %d passed, failed line %s, pid %s, %.1f s, crash buffer lines %d%s"
             % (label, mode, passed, total, declared, pid, seconds, crashed, ", " + wrong if wrong else ""))
        for line in failed:
            note("  " + line)
        if wrong or total == 0 or passed != total or declared != "0" or crashed:
            status = 1
    return status


def bench(name, rounds, conditions):
    own(name)
    modes = os.environ.get("E_BENCH_MODES", "kernels itf speed numeric first start").split()
    patterns = os.environ.get("E_BENCH_FIRST", "ListUse DictUse UserClass LinqHeavy").split()
    pause = float(os.environ.get("E_BENCH_PAUSE", "2"))
    parsed = []
    for condition in conditions:
        parts = condition.split(":")
        label = parts[0]
        shown = parts[1] if len(parts) > 1 and parts[1] else label
        package = app(label)
        parsed.append((label, shown, package, activity(package)))
    results = os.path.join(HERE, "results", name, time.strftime("%m%d-%H%M%S"))
    os.makedirs(results)
    with open(os.path.join(results, "conditions.txt"), "w", encoding="utf-8", newline="\n") as file:
        # The middle part is where c-bench.py has its switches: a Player for Android has none.
        file.write("".join("%s:android:%s\n" % (label, shown) for label, shown, package, start in parsed))
    # What a round runs: (kind for the file name, arguments, seconds a run may take, timed on the device).
    kinds = []
    for mode in modes:
        if mode == "kernels":
            kinds.append(("kernels", ["-generic-memory-bench"], 600, False))
        elif mode == "itf":
            kinds.append(("itf", ["-generic-memory-interface-bench"], 600, False))
        elif mode == "speed":
            kinds.append(("speed", ["-generic-memory-speed-bench"], 900, False))
        elif mode == "numeric":
            kinds.append(("numeric", ["-generic-memory-numeric-bench"] + os.environ.get("E_BENCH_NUMERIC_ARGS", "").split(), 900, False))
        elif mode == "engine":
            kinds.append(("engine", ["-generic-memory-engine-bench"], 900, False))
        elif mode == "first":
            for pattern in patterns:
                kinds.append(("first." + pattern, ["-generic-memory-pattern", pattern, "-generic-memory-n", "200"], 300, False))
        elif mode == "start":
            # The smallest thing the Runner does, one type of ArrayNew: from before am start to its result on the device.
            kinds.append(("start", ["-generic-memory-pattern", "ArrayNew", "-generic-memory-n", "1"], 300, True))
        else:
            sys.exit("unknown mode " + mode)
    temps = os.path.join(results, "temps.tsv")
    with open(temps, "w", encoding="utf-8", newline="\n") as file:
        file.write("time\tround\tcondition\tkind\tbatteryBefore\tbatteryAfter\tcoreBefore\tcoreAfter\tstatusBefore\tstatusAfter\tlimitBefore\tlimitAfter\tseconds\n")
    stamp = shell("date '+%Y-%m-%d %H:%M:%S.000'").strip()
    began_all = time.perf_counter()
    for at in range(1, rounds + 1):
        # A way to end a call without ending any process: a line with this call's result name in .local/e/stop-bench.
        stop = os.path.join(HERE, ".local", "e", "stop-bench")
        if os.path.isfile(stop) and name in open(stop, encoding="utf-8").read().split():
            note("stopped before round %d: %s is named in %s" % (at, name, stop))
            break
        # Every round starts with another condition.
        turn = (at - 1) % len(parsed)
        order = parsed[turn:] + parsed[:turn]
        for kind, arguments, limit, timed in kinds:
            for label, shown, package, start in order:
                output = os.path.join(results, "%s.%s.%d.txt" % (shown, kind, at))
                before = cool()
                began = time.perf_counter()
                target = output + ".run" if timed else output
                wrong, pid, wall, started = run(package, start, arguments, "%s-%s-%s-%d.txt" % (unique(label), shown, kind, at), target, limit, timed)
                seconds = time.perf_counter() - began
                after = heat()
                with open(temps, "a", encoding="utf-8", newline="\n") as file:
                    file.write("%s\t%d\t%s\t%s\t%.1f\t%.1f\t%.1f\t%.1f\t%d\t%d\t%d\t%d\t%.1f\n" % (
                        time.strftime("%H:%M:%S"), at, shown, kind, before["battery"], after["battery"], before["core"], after["core"],
                        before["status"], after["status"], before["limit"], after["limit"], seconds))
                if wrong:
                    note("%s %s round %d: %s (pid %s, %.1f s)" % (shown, kind, at, wrong, pid, seconds))
                elif timed:
                    lines = []
                    if wall is not None:
                        lines.append("wallMs\t%d" % wall)
                    for key, called in (("TotalTime", "launchMs"), ("WaitTime", "waitMs")):
                        found = re.search(key + r": (\d+)", started)
                        if found:
                            lines.append("%s\t%s" % (called, found.group(1)))
                    with open(output, "w", encoding="utf-8", newline="\n") as file:
                        file.write("\n".join(lines) + "\n")
                time.sleep(pause)
        now = heat()
        note("%s: round %d of %d done, %.0f s so far, battery %.1f, core %.1f, status %d" % (name, at, rounds, time.perf_counter() - began_all, now["battery"], now["core"], now["status"]))
    note("%s: crash buffer lines %d" % (name, crashes(stamp, os.path.join(results, "crashbuffer.log"))))
    print(results.replace("\\", "/"), flush=True)
    return 0


def main():
    command = sys.argv[1]
    if command == "install":
        return install(sys.argv[2:])
    if command == "uninstall":
        return uninstall(sys.argv[2:])
    if command == "checks":
        return checks(sys.argv[2], sys.argv[3:])
    if command == "bench":
        return bench(sys.argv[2], int(sys.argv[3]), sys.argv[4:])
    sys.exit("unknown command " + command)


sys.exit(main())
