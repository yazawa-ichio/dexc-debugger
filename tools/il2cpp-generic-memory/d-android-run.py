"""Runs the Android Players of the generic-memory bench on the connected device. m-run.py for Android.

Every run is a cold start: the app's process must be gone before it and is a new one in it. A run is over when that
process is gone again (the Player quits itself after it wrote its result); the result is then read off the device.
Every run writes a file of its own on the device, so a run that wrote nothing cannot pass for the one before it.

usage: d-android-run.py install <label>
       d-android-run.py checks <label> <mode>...                 results/checks/<label>.<mode>.txt
       d-android-run.py repeat <label> <mode> <runs> <name>       results/checks/<name>/<run>.txt, <name> must be new
       d-android-run.py measure <label> <result name>             results/<result name>, must be new
       d-android-run.py uninstall <label>
  <label>  a folder of Logs/UnityTests/GenericMemory that d-android-build.sh made (it holds android.json: the application id)
  <mode>   checks, reflection, interfaces, comparers or sharing; race:<depth> is the race of the reflection checks alone,
           9 * (depth + 1) levels of first uses in one run
  GENERIC_MEMORY_PATTERNS="A B"   the patterns of measure (by default the 16 of the reference results)
  D_ANDROID_ARGS=file             hands the arguments over in a file of the app's folder, not in the intent
  D_ANDROID_LOAD=<n>:<seconds>    keeps n busy loops running in the device's shell during the runs, started again every
                                  <seconds> (each ends by itself then): threads of the Player get preempted, which is
                                  when a race between them shows
Labels and names start with d-. Only the bench's own packages are ever started, stopped, installed or removed.
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
GENERATED = os.path.join(PROJECT, "Assets", "Tests", "GenericMemory", "Scenario", "Generated")
LOGS = os.path.join(HERE, ".local", "d", "logs")
ADB = r"C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
PATTERNS = ("ArrayNew ArrayUse DelegateUse DictUse EnumDict HashSetUse LinqBasic LinqHeavy ListMore ListNew ListUse "
            "StructList TaskT UniTaskT UserClass UserMethod")
OWN = "com.yazawaichio.genericmemorybench"
# What may be in front when a run starts: the launcher, or one of the bench's own apps that is about to go.
LAUNCHERS = ("com.google.android.apps.nexuslauncher",)
BY_FILE = os.environ.get("D_ANDROID_ARGS") == "file"
LOAD = os.environ.get("D_ANDROID_LOAD")
load_until = 0.0


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
    if not name.startswith("d-"):
        sys.exit("not one of d's names: " + name)


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
    for attempt in range(60):
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
            if attempt in (20, 40):
                # The bench's own app, which did not go by itself.
                note("the process " + pid.group(1) + " of " + package + " is still there: am force-stop")
                shell("am force-stop " + package)
            time.sleep(1)
            continue
        return
    sys.exit("the device did not get ready")


def load():
    """Keeps busy loops running in the device's shell while runs go on. They end by themselves; nothing is killed."""
    global load_until
    if not LOAD or time.time() < load_until:
        return
    loops, seconds = LOAD.split(":")
    subprocess.Popen([ADB, "shell", "for i in $(seq %d); do timeout %d sh -c 'while :; do :; done' & done; wait" % (int(loops), int(seconds))],
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    load_until = time.time() + int(seconds)


def run(package, start, arguments, name, output, limit):
    """One cold run. Returns (what went wrong or None, pid, seconds)."""
    ready(package)
    load()
    remote = files(package) + "/" + name
    stamp = shell("date '+%Y-%m-%d %H:%M:%S.000'").strip()
    line = " ".join(arguments + ["-generic-memory-output", name])
    began = time.perf_counter()
    if BY_FILE:
        shell("echo '" + line + "' > " + files(package) + "/generic-memory-args.txt")
        started = shell("am start -W -n " + start)
    else:
        started = shell("am start -W -n " + start + " -e unity '" + line + "'")
    if "Error" in started:
        return "am start: " + started.strip().replace("\n", " | "), None, 0.0
    pid = None
    wrong = None
    while True:
        now = shell("pidof " + package).strip()
        if now:
            pid = pid or now
        elif pid is not None or time.perf_counter() - began > 20:
            break
        if time.perf_counter() - began > limit:
            wrong = "no exit after %d s" % limit
            break
        time.sleep(0.3)
    seconds = time.perf_counter() - began
    if pid is None:
        wrong = "the process was never seen"
    data = adb("exec-out", "cat " + remote + " 2>/dev/null", binary=True)
    if data:
        with open(output, "wb") as file:
            file.write(data)
    else:
        wrong = (wrong + ", " if wrong else "") + "no result"
    if wrong:
        # What the device logged from the start of this run on, and its crash buffer.
        with open(output[:-4] + ".logcat.txt", "wb") as file:
            file.write(adb("logcat", "-d", "-T", stamp, binary=True, timeout=300))
        with open(output[:-4] + ".crash.txt", "wb") as file:
            file.write(adb("logcat", "-b", "crash", "-d", "-T", stamp, binary=True, timeout=300))
    return wrong, pid, seconds


def arguments_of(mode):
    """race:<depth> is the race of the reflection checks alone, that many levels deep; any other mode is its own switch."""
    if mode.startswith("race:"):
        return ["-generic-memory-race", mode.split(":", 1)[1]]
    return ["-generic-memory-" + mode]


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


def install(label):
    package = app(label)
    apk = os.path.join(PLAYERS, label, "GenericMemory.apk")
    note("install " + label + " (" + package + "): " + adb("install", "-r", apk, timeout=900).strip().replace("\n", " | "))
    note("  " + shell("dumpsys package " + package + " | grep -E 'versionName|lastUpdateTime|primaryCpuAbi'").strip().replace("\n", " | "))
    return 0


def uninstall(label):
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
        output = os.path.join(folder, "%s.%s.txt" % (label, mode.replace(":", "")))
        if os.path.exists(output):
            os.replace(output, output + ".before")
        stamp = shell("date '+%Y-%m-%d %H:%M:%S.000'").strip()
        wrong, pid, seconds = run(package, start, arguments_of(mode), unique(label) + "-" + mode.replace(":", "") + ".txt", output, 900)
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


def repeat(label, mode, runs, name):
    own(name)
    package = app(label)
    start = activity(package)
    folder = os.path.join(HERE, "results", "checks", name)
    if os.path.exists(folder):
        sys.exit(folder + " exists already; give another name")
    os.makedirs(folder)
    first = None
    good = without = otherwise = 0
    pids = []
    stamp = shell("date '+%Y-%m-%d %H:%M:%S.000'").strip()
    prefix = unique(label)
    for at in range(1, runs + 1):
        output = os.path.join(folder, "%d.txt" % at)
        wrong, pid, seconds = run(package, start, arguments_of(mode), "%s-%s-%d.txt" % (prefix, mode.replace(":", ""), at), output, 600)
        if pid is not None and pid in pids[-1:]:
            wrong = (wrong + ", " if wrong else "") + "the same process as the run before"
        pids.append(pid)
        if not os.path.exists(output):
            without += 1
            note("%s run %d: %s (pid %s, %.1f s)" % (name, at, wrong, pid, seconds))
            continue
        passed, total, failed, declared = counted(output)
        # The order of the interfaces of a type is the order of the addresses of their classes: not compared.
        lines = [line for line in lines_of(output) if "\tInterfaceOrder\t" not in line]
        if first is None:
            first = lines
        different = sum(1 for a, b in zip(first, lines) if a != b) + abs(len(first) - len(lines))
        if not wrong and total != 0 and passed == total and declared == "0" and different == 0:
            good += 1
        else:
            otherwise += 1
            note("%s run %d: passed %d of %d, failed line %s, lines not as in the first run: %d%s"
                 % (name, at, passed, total, declared, different, ", " + wrong if wrong else ""))
            for line in failed:
                note("  " + line)
        if at % 10 == 0:
            note("%s: %d runs so far, %d good, last %.1f s" % (name, at, good, seconds))
    crashed = crashes(stamp, os.path.join(folder, "crashbuffer.txt"))
    note("%s %s %s: %d runs, %d good, %d without a result, %d otherwise, %d different processes, crash buffer lines %d"
         % (label, mode, name, runs, good, without, otherwise, len(set(pids) - {None}), crashed))
    return 0 if good == runs and not crashed else 1


def measure(label, name):
    own(name)
    package = app(label)
    start = activity(package)
    results = os.path.join(HERE, "results", name)
    if os.path.isdir(results) and any(item.endswith(".txt") for item in os.listdir(results)):
        sys.exit(results + " holds results already; give another result name")
    os.makedirs(results, exist_ok=True)
    stamp = shell("date '+%Y-%m-%d %H:%M:%S.000'").strip()
    prefix = unique(label)
    for pattern in os.environ.get("GENERIC_MEMORY_PATTERNS", PATTERNS).split():
        with open(os.path.join(GENERATED, pattern + ".cs"), encoding="utf-8") as file:
            types = int(re.search(r"public const int Types = (\d+);", file.read()).group(1))
        for count in (types // 2, types):
            output = os.path.join(results, "%s-%d.txt" % (pattern, count))
            wrong, pid, seconds = run(package, start, ["-generic-memory-pattern", pattern, "-generic-memory-n", str(count)],
                                      "%s-%s-%d.txt" % (prefix, pattern, count), output, 300)
            if wrong:
                note("%s %s %d: %s (pid %s, %.1f s)" % (name, pattern, count, wrong, pid, seconds))
    note("%s: crash buffer lines %d" % (name, crashes(stamp, os.path.join(results, "crashbuffer.log"))))
    subprocess.run([sys.executable, os.path.join(HERE, "report.py"), results])
    return 0


def main():
    command = sys.argv[1]
    if command == "install":
        return install(sys.argv[2])
    if command == "uninstall":
        return uninstall(sys.argv[2])
    if command == "checks":
        return checks(sys.argv[2], sys.argv[3:])
    if command == "repeat":
        return repeat(sys.argv[2], sys.argv[3], int(sys.argv[4]), sys.argv[5])
    if command == "measure":
        return measure(sys.argv[2], sys.argv[3])
    sys.exit("unknown command " + command)


sys.exit(main())
