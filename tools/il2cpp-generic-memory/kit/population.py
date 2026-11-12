"""What the closed generic class instances of a Player would ask for if every one of them were initialized, by definition.

usage: population.py <il2cppOutput/cpp folder> <types file from "Census --types">
An estimate from the measured prices: a MethodInfo with what hangs off it, a class with its vtable, the tables of a class.
The types of the bench scenario are left out.
"""
import collections
import os
import re
import sys

METHOD = 113        # MethodInfo, its Il2CppGenericMethod and its parameter array (19.4 KB over 172 methods, measured)
CLASS = 312         # Il2CppClass without its vtable
SLOT = 16           # one vtable slot
TABLE = 8           # the pointer of a method in the class's method table
SCENARIO = "DexcDebugger.GenericMemory.Scenario"


def base(name):
    return re.sub(r"_\d+_\d+_\d+$", "", name)


def simple(name):
    return re.sub(r"_t[0-9A-F]{40}$", "", name)


def main():
    folder = sys.argv[1]
    known = {}
    for line in open(sys.argv[2], encoding="utf-8", errors="replace"):
        parts = line.rstrip("\n").split("\t")
        if len(parts) < 11:
            continue
        known[parts[0]] = None if parts[0] in known else parts
    known = {name: parts for name, parts in known.items() if parts}

    kinds, classes, insts = {}, {}, {}
    text = open(os.path.join(folder, "Il2CppTypeDefinitions.c"), encoding="utf-8-sig").read()
    for match in re.finditer(r"Il2CppType (\w+) = \{ (\(void\*\)\d+|&\w+), \d+, IL2CPP_TYPE_(\w+),", text):
        kinds[base(match.group(1))] = (match.group(3), match.group(2).lstrip("&"))
    for match in re.finditer(r"Il2CppGenericClass (\w+)_GenericClass = \{ &(\w+), \{ (?:&(\w+)|NULL), (?:&(\w+)|NULL) \}", text):
        classes[match.group(1)] = (base(match.group(2)), match.group(3))
    text = open(os.path.join(folder, "Il2CppGenericInstDefinitions.c"), encoding="utf-8-sig").read()
    for match in re.finditer(r"static const Il2CppType\* (\w+)_Types\[\] = \{ (.*?) \};", text):
        insts[match.group(1)] = [base(name) for name in re.findall(r"&(\w+)", match.group(2))]

    def of_scenario(name, depth=0):
        kind, data = kinds.get(name, (None, None))
        if kind == "GENERICINST" and depth < 6:
            definition, inst = classes.get(data.replace("_GenericClass", ""), (None, None))
            return definition is not None and any(of_scenario(part, depth + 1) for part in [definition] + insts.get(inst, []))
        info = known.get(simple(name))
        return bool(info) and info[8] == SCENARIO

    count = collections.Counter()
    for name, (definition, inst) in classes.items():
        arguments = insts.get(inst, [])
        if any(kinds.get(argument, ("",))[0] in ("VAR", "MVAR") for argument in arguments):
            continue
        if of_scenario(definition) or any(of_scenario(argument) for argument in arguments):
            continue
        count[simple(definition)] += 1

    rows = []
    for key, instances in count.items():
        info = known.get(key)
        if not info:
            continue
        methods, slots = int(info[3]), int(info[4])
        one = methods * (METHOD + TABLE) + CLASS + slots * SLOT
        rows.append((instances * one, instances, methods, slots, one, info[1], info[9], instances * methods * (METHOD + TABLE), instances * (CLASS + slots * SLOT)))
    rows.sort(reverse=True)
    total = sum(row[0] for row in rows)
    of_methods = sum(row[7] for row in rows)
    of_classes = sum(row[8] for row in rows)
    print("%d instances of %d definitions: %.1f MB if all were initialized (methods %.1f MB, classes and vtables %.1f MB)" % (
        sum(row[1] for row in rows), len(rows), total / 1e6, of_methods / 1e6, of_classes / 1e6))
    by_kind = collections.defaultdict(lambda: [0, 0, 0])
    for row in rows:
        by_kind[row[5]][0] += row[1]
        by_kind[row[5]][1] += row[0]
        by_kind[row[5]][2] += 1
    for kind, (instances, size, definitions) in sorted(by_kind.items(), key=lambda pair: -pair[1][1]):
        print("  %-10s %5d instances of %3d definitions  %6.2f MB  %4.1f%%" % (kind, instances, definitions, size / 1e6, 100.0 * size / total))
    print()
    print("   MB     %  cum%  inst  meth slots  KB/inst kind       definition")
    running = 0
    for row in rows[:25]:
        running += row[0]
        print("%5.2f %5.1f %5.1f %5d %5d %5d %8.1f %-10s %s" % (row[0] / 1e6, 100.0 * row[0] / total, 100.0 * running / total, row[1], row[2], row[3], row[4] / 1000.0, row[5], row[6]))


main()
