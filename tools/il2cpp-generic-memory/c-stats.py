"""Puts the timings of c-bench.sh side by side: the best run, the median and the spread of every condition.

usage: c-stats.py <folder of one c-bench.sh call>... [--base <condition>] [--only <kind,kind>] [--min-runs <n>]

Several folders are read as one set of runs (the same conditions measured at different times).
Every cell is "best / median (spread%)": the fastest run, the middle run, and (slowest - fastest) / median.
Behind it, the median against the base condition (the first one, or --base). A difference is marked "=" when it is
inside the noise: the median of each of the two lies between the fastest and the slowest run of the other.
Kernels are nanoseconds for one call (a kernel already keeps the best of 7 passes inside one process); the first use
of a pattern is milliseconds for 200 types; start times are milliseconds.
"""
import os
import re
import statistics
import sys

KINDS = [
    ("kernels", "kernel (Bench.cs), ns", 2),
    ("itf", "kernel (InterfaceBench.cs), ns", 2),
    ("speed", "kernel (SpeedBench.cs), ns; .bytes is GC bytes for one call", 2),
    ("first", "first use of 200 types, ms", 1),
    ("start", "start of the process, ms", 1),
]


def read(folders):
    data = {}  # kind -> metric -> condition -> [values]
    order = []

    def add(kind, metric, condition, value):
        data.setdefault(kind, {}).setdefault(metric, {}).setdefault(condition, []).append(value)
        if condition not in order:
            order.append(condition)

    for folder in folders:
        listed = folder + "/conditions.txt"
        if os.path.isfile(listed):
            for line in open(listed, encoding="utf-8"):
                parts = line.strip().split(":")
                if len(parts) >= 2:
                    shown = parts[2] if len(parts) > 2 and parts[2] else parts[0] + "@" + parts[1]
                    if shown not in order:
                        order.append(shown)
        for name in sorted(os.listdir(folder)):
            match = re.match(r"(.+)\.(kernels|itf|speed)\.(\d+)\.txt$", name)
            if match:
                for line in open(os.path.join(folder, name), encoding="utf-8"):
                    parts = line.rstrip("\n").split("\t")
                    if parts[0] == "bench" and len(parts) == 3:
                        add(match.group(2), parts[1], match.group(1), float(parts[2]))
                continue
            match = re.match(r"(.+)\.first\.(\w+)\.(\d+)\.txt$", name)
            if match:
                for line in open(os.path.join(folder, name), encoding="utf-8"):
                    parts = line.rstrip("\n").split("\t")
                    if parts[0] == "elapsedMs":
                        add("first", match.group(2), match.group(1), float(parts[1]))
                    elif parts[0] in ("initMs", "snapshotMs", "initBeginMs") and len(parts) == 2 and float(parts[1]) > 0:
                        # Every pattern run is also a start of the process, up to the frame the pattern runs in.
                        add("start", parts[0] + " (all pattern runs)", match.group(1), float(parts[1]))
                continue
            match = re.match(r"(.+)\.start\.(\d+)\.txt$", name)
            if match:
                for line in open(os.path.join(folder, name), encoding="utf-8"):
                    parts = line.rstrip("\n").split("\t")
                    if parts[0] in ("wallMs", "initMs", "snapshotMs", "initBeginMs") and float(parts[1]) > 0:
                        add("start", parts[0], match.group(1), float(parts[1]))
    return data, order


def cell(values, digits):
    best, middle, worst = min(values), statistics.median(values), max(values)
    spread = 100.0 * (worst - best) / middle if middle else 0.0
    return ("%." + str(digits) + "f / %." + str(digits) + "f (%.0f%%)") % (best, middle, spread)


def against(values, base):
    middle, base_middle = statistics.median(values), statistics.median(base)
    if not base_middle:
        return ""
    inside = min(base) <= middle <= max(base) and min(values) <= base_middle <= max(values)
    return " %+.0f%%%s" % (100.0 * (middle - base_middle) / base_middle, " =" if inside else "")


def main():
    argv = sys.argv[1:]
    folders, base, only, least = [], None, None, 1
    at = 0
    while at < len(argv):
        if argv[at] == "--base":
            base = argv[at + 1]
            at += 2
        elif argv[at] == "--only":
            only = argv[at + 1].split(",")
            at += 2
        elif argv[at] == "--min-runs":
            least = int(argv[at + 1])
            at += 2
        else:
            folders.append(argv[at].replace("\\", "/").rstrip("/"))
            at += 1
    data, order = read(folders)
    if base and base in order:
        order.remove(base)
        order.insert(0, base)
    for kind, title, digits in KINDS:
        if kind not in data or (only and kind not in only):
            continue
        conditions = [condition for condition in order if any(condition in metric for metric in data[kind].values())]
        runs = max(len(values) for metric in data[kind].values() for values in metric.values())
        print("### %s (best / median (spread), up to %d runs; behind it the median against %s, = is inside the noise)" % (title, runs, conditions[0]))
        print()
        print("| | %s |" % " | ".join(conditions))
        print("|---|" + "---:|" * len(conditions))
        for metric in data[kind]:
            cells = []
            base_values = data[kind][metric].get(conditions[0])
            for condition in conditions:
                values = data[kind][metric].get(condition)
                if not values or len(values) < least:
                    cells.append("-")
                    continue
                text = cell(values, digits)
                if condition != conditions[0] and base_values:
                    text += against(values, base_values)
                cells.append(text)
            print("| %s | %s |" % (metric, " | ".join(cells)))
        print()


main()
