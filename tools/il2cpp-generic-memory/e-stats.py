"""Puts the timings of e-android-run.py bench side by side. c-stats.py with the numeric bench, the start times of a
phone and the temperatures added.

usage: e-stats.py <folder of one bench call>... [--base <condition>] [--only <kind,kind>] [--min-runs <n>]
       e-stats.py <folder>... --numeric [<condition>,<condition>...]   the numeric bench as kernel x way, one table for each condition
       e-stats.py <folder>... --temps                                  what temps.tsv says for every condition and every round
       e-stats.py <folder>... --flags                                  the lines of the numeric and the engine bench that are not timings
       e-stats.py <folder>... --compact [--base <condition>] [--only <kind,kind>]
                                                                       one short line for each metric: the base's best and median, and
                                                                       for every other condition its median against the base's

Several folders are read as one set of runs (the same conditions measured at different times).
Every cell is "best / median (spread%)": the fastest run, the middle run, and (slowest - fastest) / median.
Behind it, the median against the base condition (the first one, or --base). A difference is marked "=" when it is
inside the noise: the median of each of the two lies between the fastest and the slowest run of the other.
Kernels are nanoseconds for one call (a kernel already keeps the best of several passes inside one process); the
numeric bench is nanoseconds for one element; the first use of a pattern is milliseconds for 200 types; start times
are milliseconds.
--numeric: a cell is the best run and, in brackets, how many times faster than the way "cs" of the same kernel that
is (below 1 is slower). The first table's "cs" column is also what the other tables are read against.
"""
import os
import re
import statistics
import sys

KINDS = [
    ("kernels", "kernel (Bench.cs), ns", 2),
    ("itf", "kernel (InterfaceBench.cs), ns", 2),
    ("speed", "kernel (SpeedBench.cs), ns; .bytes is GC bytes for one call", 2),
    ("numeric", "kernel (NumericBench.cs), ns for one element (one call for call.*)", 3),
    ("engine", "kernel (EngineBench.cs), ns for one iteration", 3),
    ("first", "first use of 200 types, ms", 1),
    ("start", "start of the app, ms: wallMs is from before am start to the result on the device, launchMs is am start's TotalTime", 0),
]
WAYS = ["bcl", "blockcopy", "unity", "cs", "cs4", "csnc", "csnc4", "csmath", "csmathnc", "burst", "burstfast", "burstdelegate", "burstptr", "burstfastptr", "cpp", "cppsimd"]


def read(folders):
    data = {}  # kind -> metric -> condition -> [values]
    order = []
    other = {}  # condition -> line -> count, for the lines of the numeric bench that are not timings

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
            match = re.match(r"(.+)\.(kernels|itf|speed|numeric|engine)\.(\d+)\.txt$", name)
            if match:
                for line in open(os.path.join(folder, name), encoding="utf-8"):
                    parts = line.rstrip("\n").split("\t")
                    if parts[0] == "bench" and len(parts) == 3:
                        add(match.group(2), parts[1], match.group(1), float(parts[2]))
                    elif match.group(2) in ("numeric", "engine") and parts[0] in ("burst", "native", "pointers", "same", "differs", "difference", "differing", "handle", "engineObjects"):
                        text = "\t".join(parts)
                        other.setdefault(match.group(1), {}).setdefault(text, 0)
                        other[match.group(1)][text] += 1
                continue
            match = re.match(r"(.+)\.first\.(\w+)\.(\d+)\.txt$", name)
            if match:
                for line in open(os.path.join(folder, name), encoding="utf-8"):
                    parts = line.rstrip("\n").split("\t")
                    if parts[0] == "elapsedMs":
                        add("first", match.group(2), match.group(1), float(parts[1]))
                continue
            match = re.match(r"(.+)\.start\.(\d+)\.txt$", name)
            if match:
                for line in open(os.path.join(folder, name), encoding="utf-8"):
                    parts = line.rstrip("\n").split("\t")
                    if parts[0] in ("wallMs", "launchMs", "waitMs") and float(parts[1]) > 0:
                        add("start", parts[0], match.group(1), float(parts[1]))
    return data, order, other


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


def numeric(data, order, chosen):
    metrics = data.get("numeric", {})
    kernels = []
    for metric in metrics:
        kernel, way = metric.rsplit(".", 1)
        if kernel not in kernels:
            kernels.append(kernel)
    for condition in chosen or order:
        ways = [way for way in WAYS if any(condition in metrics.get(kernel + "." + way, {}) for kernel in kernels)]
        if not ways:
            continue
        runs = max(len(values.get(condition, [])) for values in metrics.values())
        print("### %s: ns for one element, the best of %d runs (in brackets: times faster than cs)" % (condition, runs))
        print()
        print("| kernel.size | %s |" % " | ".join(ways))
        print("|---|" + "---:|" * len(ways))
        for kernel in kernels:
            plain = metrics.get(kernel + ".cs", {}).get(condition)
            cells = []
            for way in ways:
                values = metrics.get(kernel + "." + way, {}).get(condition)
                if not values:
                    cells.append("")
                    continue
                best = min(values)
                text = "%.3f" % best
                if plain and way != "cs" and best > 0:
                    text += " (%.2f)" % (min(plain) / best)
                cells.append(text)
            print("| %s | %s |" % (kernel, " | ".join(cells)))
        print()


def temps(folders):
    rows = []
    for folder in folders:
        path = folder + "/temps.tsv"
        if not os.path.isfile(path):
            continue
        lines = open(path, encoding="utf-8").read().split("\n")
        head = lines[0].split("\t")
        for line in lines[1:]:
            if line.strip():
                rows.append(dict(zip(head, line.split("\t"))))
    if not rows:
        print("no temps.tsv")
        return
    for key, title in (("condition", "by condition"), ("round", "by round"), ("kind", "by kind of run")):
        print("### temperatures %s: battery before a run (lowest / mean / highest), the warmest big core before and after (mean), runs that began or ended throttled, runs with a lowered frequency limit" % title)
        print()
        print("| %s | runs | battery before | battery after (mean) | core before (mean) | core after (mean) | throttled | limited |" % key)
        print("|---|---:|---:|---:|---:|---:|---:|---:|")
        groups = []
        for row in rows:
            if row[key] not in groups:
                groups.append(row[key])
        highest = max(int(row["limitBefore"]) for row in rows)
        for group in groups:
            mine = [row for row in rows if row[key] == group]
            before = [float(row["batteryBefore"]) for row in mine]
            print("| %s | %d | %.1f / %.1f / %.1f | %.1f | %.1f | %.1f | %d | %d |" % (
                group, len(mine), min(before), statistics.mean(before), max(before),
                statistics.mean(float(row["batteryAfter"]) for row in mine),
                statistics.mean(float(row["coreBefore"]) for row in mine),
                statistics.mean(float(row["coreAfter"]) for row in mine),
                sum(1 for row in mine if int(row["statusBefore"]) > 0 or int(row["statusAfter"]) > 0),
                sum(1 for row in mine if int(row["limitBefore"]) < highest or int(row["limitAfter"]) < highest)))
        print()


def main():
    argv = sys.argv[1:]
    folders, base, only, least = [], None, None, 1
    mode, chosen = "tables", None
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
        elif argv[at] == "--numeric":
            mode = "numeric"
            if at + 1 < len(argv) and not argv[at + 1].startswith("--") and not os.path.isdir(argv[at + 1]):
                chosen = argv[at + 1].split(",")
                at += 1
            at += 1
        elif argv[at] in ("--temps", "--flags", "--compact"):
            mode = argv[at][2:]
            at += 1
        else:
            folders.append(argv[at].replace("\\", "/").rstrip("/"))
            at += 1
    if mode == "temps":
        temps(folders)
        return
    data, order, other = read(folders)
    if base and base in order:
        order.remove(base)
        order.insert(0, base)
    if mode == "numeric":
        numeric(data, order, chosen)
        return
    if mode == "flags":
        for condition in order:
            for text, count in sorted(other.get(condition, {}).items()):
                if not text.startswith("same\t") or not text.endswith("\ttrue"):
                    print("%s\t%d runs\t%s" % (condition, count, text))
            same = sum(count for text, count in other.get(condition, {}).items() if text.startswith("same\t") and text.endswith("\ttrue"))
            print("%s\t%d lines of same ... true" % (condition, same))
        return
    if mode == "compact":
        for kind, title, digits in KINDS:
            if kind not in data or (only and kind not in only):
                continue
            conditions = [condition for condition in order if any(condition in metric for metric in data[kind].values())]
            print("### %s" % title)
            print()
            print("| | %s best / median (spread) | %s |" % (conditions[0], " | ".join(conditions[1:])))
            print("|---|---:|" + "---:|" * (len(conditions) - 1))
            for metric in data[kind]:
                base_values = data[kind][metric].get(conditions[0])
                if not base_values or metric.endswith(".bytes") or "Bytes" in metric:
                    continue
                cells = []
                for condition in conditions[1:]:
                    values = data[kind][metric].get(condition)
                    cells.append(against(values, base_values).strip() if values else "-")
                print("| %s | %s | %s |" % (metric, cell(base_values, digits), " | ".join(cells)))
            print()
        return
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
