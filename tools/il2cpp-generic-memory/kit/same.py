"""Says whether two measured result folders hold the same numbers, run for run.

usage: same.py <results folder> <results folder>
Compared for each run: what was asked from the metadata pools, and the runtime's own counts of classes and methods.
The allocation sites are not compared one by one: a site is named by its line, and the lines of two copies differ.
"""
import os
import re
import sys

KEYS = ["pool", "initializedClasses", "methods", "genericInstances", "genericClasses", "inflatedMethods", "inflatedTypes", "sum", "pending"]


def read(folder):
    runs = {}
    for name in sorted(os.listdir(folder)):
        if not re.match(r"\w+-\d+\.txt$", name):
            continue
        values = {}
        allocations = 0
        for line in open(os.path.join(folder, name), encoding="utf-8"):
            parts = line.rstrip("\n").split("\t")
            if parts[0] == "site":
                allocations += int(parts[2])
            elif len(parts) == 2:
                values[parts[0]] = parts[1]
        values["allocations"] = str(allocations)
        runs[name] = values
    return runs


left, right = read(sys.argv[1]), read(sys.argv[2])
different = 0
for name in sorted(set(left) | set(right)):
    if name not in left or name not in right:
        print("%-22s only in %s" % (name, sys.argv[1] if name in left else sys.argv[2]))
        different += 1
        continue
    pairs = [(key, left[name].get(key), right[name].get(key)) for key in KEYS + ["allocations"]]
    wrong = [(key, a, b) for key, a, b in pairs if a != b]
    if wrong:
        different += 1
        print("%-22s %s" % (name, ", ".join("%s %s <> %s" % item for item in wrong)))
print("%d runs compared, %d differ" % (len(set(left) & set(right)), different))
