"""Puts the timings of bench.sh side by side.

usage: bench.py <results/bench folder> <baseline label> <label>...

Every value is the best of the runs of a condition. The kernels are in nanoseconds for one call; the first use of a
pattern is in milliseconds for 200 types.
"""

import os
import re
import sys


def read(folder):
    kernels, first = {}, {}
    for name in sorted(os.listdir(folder)):
        match = re.match(r"([\w-]+)\.kernels\.\d+\.txt$", name)
        if match:
            with open(os.path.join(folder, name), encoding="utf-8") as file:
                for line in file:
                    parts = line.rstrip("\n").split("\t")
                    if parts[0] == "bench":
                        key = (match.group(1), parts[1])
                        kernels[key] = min(kernels.get(key, float("inf")), float(parts[2]))
            continue
        match = re.match(r"([\w-]+)\.first\.(\w+)\.\d+\.txt$", name)
        if match:
            with open(os.path.join(folder, name), encoding="utf-8") as file:
                for line in file:
                    parts = line.rstrip("\n").split("\t")
                    if parts[0] == "elapsedMs":
                        key = (match.group(1), match.group(2))
                        first[key] = min(first.get(key, float("inf")), float(parts[1]))
    return kernels, first


def table(title, unit, data, labels, digits):
    names = []
    for label, name in data:
        if name not in names:
            names.append(name)
    print("| %s | %s |" % (title, " | ".join(labels)))
    print("|---|" + "---:|" * len(labels))
    for name in names:
        base = data.get((labels[0], name))
        cells = []
        for label in labels:
            value = data.get((label, name))
            if value is None:
                cells.append("-")
            elif label == labels[0] or not base:
                cells.append(("%." + str(digits) + "f %s") % (value, unit))
            else:
                cells.append(("%." + str(digits) + "f %s (%+.0f%%)") % (value, unit, 100.0 * (value - base) / base))
        print("| %s | %s |" % (name, " | ".join(cells)))
    print()


def main():
    kernels, first = read(sys.argv[1])
    labels = sys.argv[2:]
    table("kernel", "ns", kernels, labels, 2)
    table("first use of 200 types", "ms", first, labels, 2)


main()
