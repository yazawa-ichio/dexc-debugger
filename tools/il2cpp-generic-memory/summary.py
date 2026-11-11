"""Puts the results of several measured variants side by side, as the table of the README.

usage: summary.py <results folder> <baseline label> <label>...

Each cell is the bytes one more type asks from the metadata pools, and how much less that is than the baseline.
"""

import os
import re
import sys


def read(folder):
    runs = {}
    for name in sorted(os.listdir(folder)):
        match = re.match(r"(\w+)-(\d+)\.txt$", name)
        if not match:
            continue
        values = {}
        with open(os.path.join(folder, name), encoding="utf-8") as file:
            for line in file:
                parts = line.rstrip("\n").split("\t")
                if len(parts) == 2:
                    values[parts[0]] = parts[1]
        runs.setdefault(match.group(1), {})[int(match.group(2))] = values
    return runs


def slope(runs, pattern, key):
    if pattern not in runs or len(runs[pattern]) < 2:
        return None
    counts = sorted(runs[pattern])
    low, high = counts[0], counts[-1]
    if int(runs[pattern][high].get("pending", 0)) or int(runs[pattern][low].get("pending", 0)):
        return None
    return (float(runs[pattern][high].get(key, 0)) - float(runs[pattern][low].get(key, 0))) / (high - low)


def main():
    root = sys.argv[1]
    key = "pool"
    labels = [arg for arg in sys.argv[2:] if not arg.startswith("--")]
    if "--heap" in sys.argv:
        key = "heap"
    data = {label: read(os.path.join(root, label)) for label in labels}
    base = data[labels[0]]
    print("| pattern | " + " | ".join(labels) + " |")
    print("|---|" + "---:|" * len(labels))
    for pattern in sorted(base):
        was = slope(base, pattern, key)
        cells = []
        for label in labels:
            now = slope(data[label], pattern, key)
            if now is None:
                cells.append("-")
            elif label == labels[0] or not was:
                cells.append("%.1f KB" % (now / 1024.0))
            else:
                cells.append("%.1f KB (%+.0f%%)" % (now / 1024.0, 100.0 * (now - was) / was))
        print("| %s | %s |" % (pattern, " | ".join(cells)))


main()
