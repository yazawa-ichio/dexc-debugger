"""What one more type costs, by what the memory is, for each pattern and each set of the lazy switches.

usage: breakdown.py <results folder> <label>...
The slope between the two counts a pattern ran at is the cost of one more type.
"""
import os
import re
import sys

# The allocation sites of the libil2cpp copy the itf-* results were measured with, by what they hold.
GROUPS = {
    "method": ["GenericMethod.cpp:54", "GenericMethod.cpp:271", "GenericMetadata.cpp:53", "ArrayMetadata.cpp:291", "ArrayMetadata.cpp:65", "ArrayMetadata.cpp:73"],
    "types": ["GenericMetadata.cpp:74", "GenericMetadata.cpp:121", "GenericMetadata.cpp:141"],
    "class": ["GenericClass.cpp:178", "ArrayMetadata.cpp:563"],
    "tables": ["GenericClass.cpp:32", "GenericClass.cpp:70", "GenericClass.cpp:109", "GenericClass.cpp:144", "Class.cpp:197", "Class.cpp:1388",
               "ArrayMetadata.cpp:121", "ArrayMetadata.cpp:346", "ArrayMetadata.cpp:417", "Il2CppGenericClass:0", "MetadataCache.cpp:481", "MetadataCache.cpp:483", "Class.cpp:1566"],
    "rgctx": ["GenericMetadata.cpp:275", "GenericMetadata.cpp:282"],
    # What the element type itself costs, generic or not.
    "element": ["Class.cpp:1147", "Class.cpp:1148", "Class.cpp:1176", "Class.cpp:1062", "Class.cpp:211", "Class.cpp:1425", "Class.cpp:1522", "Class.cpp:1232", "Class.cpp:1477", "Class.cpp:1613", "Class.cpp:235"],
}
OF = {site: group for group, sites in GROUPS.items() for site in sites}
COUNTS = {"methods": ["GenericMethod.cpp:54", "ArrayMetadata.cpp:291", "ArrayMetadata.cpp:65"], "classes": ["GenericClass.cpp:178", "ArrayMetadata.cpp:563"]}


def read(folder):
    runs = {}
    for name in sorted(os.listdir(folder)):
        match = re.match(r"(\w+)-(\d+)\.txt$", name)
        if not match:
            continue
        values, sites = {}, {}
        with open(os.path.join(folder, name), encoding="utf-8") as file:
            for line in file:
                parts = line.rstrip("\n").split("\t")
                if parts[0] == "site":
                    sites[parts[1]] = (int(parts[2]), int(parts[3]))
                elif len(parts) == 2:
                    values[parts[0]] = parts[1]
        runs.setdefault(match.group(1), {})[int(match.group(2))] = (values, sites)
    return runs


def slopes(run):
    low, high = sorted(run)[0], sorted(run)[-1]
    span = float(high - low)
    groups = {group: 0.0 for group in GROUPS}
    groups["other"] = 0.0
    counts = {name: 0.0 for name in COUNTS}
    for site in set(run[low][1]) | set(run[high][1]):
        count = (run[high][1].get(site, (0, 0))[0] - run[low][1].get(site, (0, 0))[0]) / span
        size = (run[high][1].get(site, (0, 0))[1] - run[low][1].get(site, (0, 0))[1]) / span
        groups[OF.get(site, "other")] += size
        for name, sites in COUNTS.items():
            if site in sites:
                counts[name] += count
    pool = (int(run[high][0]["pool"]) - int(run[low][0]["pool"])) / span
    return groups, counts, pool


def main():
    folder, labels = sys.argv[1], sys.argv[2:]
    data = {label: read(os.path.join(folder, label)) for label in labels}
    patterns = [p for p in data[labels[0]] if len(data[labels[0]][p]) >= 2]
    order = ["method", "types", "class", "tables", "rgctx", "element", "other"]
    print("%-14s %-13s %7s | %s | %8s %7s" % ("pattern", "label", "KB", " ".join("%7s" % g for g in order), "methods", "classes"))
    for pattern in patterns:
        for label in labels:
            if pattern not in data[label] or len(data[label][pattern]) < 2:
                continue
            groups, counts, pool = slopes(data[label][pattern])
            print("%-14s %-13s %7.1f | %s | %8.0f %7.0f" % (pattern, label, pool / 1000.0, " ".join("%7.1f" % (groups[g] / 1000.0) for g in order), counts["methods"], counts["classes"]))
        print()


main()
