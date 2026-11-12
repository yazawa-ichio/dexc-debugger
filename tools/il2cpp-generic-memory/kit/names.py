"""The classes and methods the runtime made for the second type of a pattern, by name, for each set of the lazy switches.

usage: names.py <folder of names-run.sh> <pattern> [--methods]
A pattern ran for two types. What was made for the second one names it (E0001, S0001 or K0001), so that is what one more
type costs; what names neither was made once.
"""
import collections
import os
import re
import sys

METHOD = 113  # bytes of a MethodInfo with what hangs off it, as measured


def short(name):
    name = re.sub(r", [\w.]+, Version=[\d.]+, Culture=\w+, PublicKeyToken=\w+", "", name)
    name = re.sub(r"GenericMemory\.(Data|Lib|Patterns)\.", "", name)
    name = re.sub(r"System\.(Collections\.Generic|Collections\.ObjectModel|Collections|Linq|Threading\.Tasks|Runtime\.CompilerServices)\.", "", name)
    name = re.sub(r"\b[ESK]0001\b", "T", name)
    name = name.replace("System.", "")
    name = re.sub(r"`\d+", "", name)
    name = name.replace("[[", "<").replace("]]", ">").replace("],[", ", ")
    return name


def read(path):
    classes, methods, seen = [], [], set()
    for line in open(path, encoding="utf-8", errors="replace"):
        parts = line.rstrip("\n").split("\t")
        if parts[1] in seen:
            continue
        seen.add(parts[1])
        if not re.search(r"\b[ESK]0001\b", parts[2]):
            continue
        if parts[0] == "class":
            classes.append((short(parts[2]), int(parts[3]), int(parts[4]), int(parts[5]), int(parts[7])))
        else:
            methods.append((short(parts[2]), parts[3]))
    return classes, methods


def main():
    folder, pattern = sys.argv[1], sys.argv[2]
    show_methods = "--methods" in sys.argv
    sets = ["000", "110", "111"]
    data = {s: read(os.path.join(folder, "%s.%s.txt.names" % (pattern, s))) for s in sets}
    names = []
    for s in sets:
        for entry in data[s][0]:
            if entry[0] not in names:
                names.append(entry[0])
    size = {}
    slots = {}
    for s in sets:
        for name, bytes_, vtable, _, _ in data[s][0]:
            size[name] = bytes_
            slots[name] = vtable
    count = {s: collections.Counter(owner for owner, _ in data[s][1]) for s in sets}
    print("%s: per type, classes %s, methods %s" % (pattern, " / ".join(str(len(data[s][0])) for s in sets), " / ".join(str(len(data[s][1])) for s in sets)))
    print("  class bytes %s, method bytes (at %d each) %s" % (
        " / ".join(str(sum(entry[1] for entry in data[s][0])) for s in sets), METHOD, " / ".join(str(len(data[s][1]) * METHOD) for s in sets)))
    print("  %-62s %6s %5s | made 000 110 111 | methods 000 110 111" % ("class", "bytes", "slots"))
    owners = list(names)
    for s in sets:
        for owner in count[s]:
            if owner not in owners:
                owners.append(owner)
    for name in sorted(owners, key=lambda n: -(size.get(n, 0) + METHOD * count["000"][n])):
        made = ["x" if any(entry[0] == name for entry in data[s][0]) else "." for s in sets]
        print("  %-62s %6s %5s |        %s   %s   %s  |       %4d %3d %3d" % (
            name[:62], size.get(name, "-"), slots.get(name, "-"), made[0], made[1], made[2], count["000"][name], count["110"][name], count["111"][name]))
    if show_methods:
        for s in ("111",):
            print("  methods left with every switch on:")
            by = collections.defaultdict(list)
            for owner, method in data[s][1]:
                by[owner].append(method)
            for owner, list_ in sorted(by.items(), key=lambda pair: -len(pair[1])):
                print("    %-50s %s" % (owner[:50], ", ".join(sorted(list_))[:230]))


main()
