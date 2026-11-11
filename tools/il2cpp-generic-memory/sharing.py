"""Counts how far the generic class instances of a Player would collapse if instances whose code is the same were one.

usage: sharing.py <il2cppOutput/cpp folder> <types file from "Census --types"> [name pattern of the types to leave out]

The il2cpp output lists every generic class instance it knows (Il2CppTypeDefinitions.c) and the arguments of each
(Il2CppGenericInstDefinitions.c). Each argument is replaced by what stands for it at three levels:
  refs    every reference type is one (what il2cpp already does for the code)
  enums   and every enum is its underlying integer (il2cpp shares these too, through Int32Enum and the like)
  shapes  and every value type is its fields flattened to machine kinds (same layout and same way of being passed)
The count of distinct instances at each level is how many would be left. This is the population the conversion knows
of, not what a run initializes.
"""

import collections
import os
import re
import sys

REFERENCE = {"CLASS", "STRING", "OBJECT", "SZARRAY", "ARRAY"}
PRIMITIVE_SHAPE = {
    "BOOLEAN": "1", "I1": "1", "U1": "1", "CHAR": "2", "I2": "2", "U2": "2", "I4": "4", "U4": "4", "R4": "f",
    "I8": "8", "U8": "8", "R8": "d", "I": "8", "U": "8", "PTR": "8",
}
KEPT = {"List_1"}


def base(name):
    return re.sub(r"_\d+_\d+_\d+$", "", name)


def simple(name):
    return re.sub(r"_t[0-9A-F]{40}$", "", name)


def owner(assembly):
    if assembly is None:
        return "?"
    if assembly in ("mscorlib", "netstandard", "System") or assembly.startswith("System.") or assembly == "UnityEngine" or assembly.startswith("UnityEngine."):
        return "engine"
    if assembly.startswith("Unity.") or assembly.startswith("UniTask"):
        return "package"
    return "project"


def main():
    folder = sys.argv[1]
    left_out = re.compile(sys.argv[3]) if len(sys.argv) > 3 else None

    known = {}
    for line in open(sys.argv[2], encoding="utf-8", errors="replace"):
        parts = line.rstrip("\n").split("\t")
        if len(parts) < 11:
            continue
        # A simple name that names two different things is of no use.
        known[parts[0]] = None if parts[0] in known else parts
    known = {name: parts for name, parts in known.items() if parts}

    kinds = {}
    classes = {}
    text = open(os.path.join(folder, "Il2CppTypeDefinitions.c"), encoding="utf-8-sig").read()
    for match in re.finditer(r"Il2CppType (\w+) = \{ (\(void\*\)\d+|&\w+), \d+, IL2CPP_TYPE_(\w+),", text):
        kinds[base(match.group(1))] = (match.group(3), match.group(2).lstrip("&"))
    for match in re.finditer(r"Il2CppGenericClass (\w+)_GenericClass = \{ &(\w+), \{ (?:&(\w+)|NULL), (?:&(\w+)|NULL) \}", text):
        classes[match.group(1)] = (base(match.group(2)), match.group(3))
    # An array type says what it is an array of right in its row.
    arrays = set(re.findall(r"Il2CppType \w+ = \{ \(void\*\)&(\w+?)_\d+_\d+_\d+, \d+, IL2CPP_TYPE_SZARRAY,", text))

    insts = {}
    text = open(os.path.join(folder, "Il2CppGenericInstDefinitions.c"), encoding="utf-8-sig").read()
    for match in re.finditer(r"static const Il2CppType\* (\w+)_Types\[\] = \{ (.*?) \};", text):
        insts[match.group(1)] = [base(name) for name in re.findall(r"&(\w+)", match.group(2))]

    def canon(name, level, depth=0):
        """What stands for a type argument, or None when it is still a type parameter."""
        kind, data = kinds.get(name, (None, None))
        if kind is None:
            return "?" + name
        if kind in ("VAR", "MVAR"):
            return None
        if kind in REFERENCE:
            return "ref"
        if kind in PRIMITIVE_SHAPE:
            return kind if level < 3 else PRIMITIVE_SHAPE[kind]
        if kind == "GENERICINST":
            definition, inst = classes.get(data.replace("_GenericClass", ""), (None, None))
            if definition is None or depth > 6:
                return "?" + name
            if kinds.get(definition, ("CLASS",))[0] in REFERENCE:
                return "ref"
            arguments = [canon(argument, level, depth + 1) for argument in insts.get(inst, [])]
            if any(argument is None for argument in arguments):
                return None
            return simple(definition) + "<" + ",".join(arguments) + ">"
        if kind == "VALUETYPE":
            info = known.get(simple(name))
            if info and info[1] == "enum" and level >= 2:
                return "enum:" + info[10] if level < 3 else info[10]
            if info and info[1] == "struct" and level >= 3 and info[10] != "-":
                return info[10]
            return name
        return name

    def who(arguments, depth=0):
        """Whose types the arguments are: the project's if any is, else a package's if any is, else the engine's."""
        found = "engine"
        for argument in arguments:
            kind, data = kinds.get(argument, (None, None))
            if kind == "GENERICINST" and depth < 6:
                definition, inst = classes.get(data.replace("_GenericClass", ""), (None, None))
                inner = who([definition] + insts.get(inst, []), depth + 1) if definition else "engine"
            else:
                info = known.get(simple(argument))
                inner = owner(info[8]) if info else "engine"
            if inner == "project" or (inner == "package" and found == "engine"):
                found = inner
        return found

    distinct = [collections.defaultdict(set) for _ in range(4)]
    owners = collections.defaultdict(collections.Counter)
    weight = {}
    for name, (definition, inst) in classes.items():
        arguments = insts.get(inst, [])
        if left_out and any(left_out.search(argument) for argument in arguments):
            continue
        if any(kinds.get(argument, ("",))[0] in ("VAR", "MVAR") for argument in arguments):
            continue
        keys = [",".join(arguments)]
        for level in (1, 2, 3):
            parts = [canon(argument, level) for argument in arguments]
            keys.append(None if any(part is None for part in parts) else ",".join(parts))
        if None in keys:
            continue
        key = simple(definition)
        for level in range(4):
            distinct[level][key].add(keys[level])
        owners[key][who(arguments)] += 1
        info = known.get(key)
        weight[key] = int(info[3]) if info else 0

    def count(level, keys):
        return sum(len(distinct[level][key]) for key in keys)

    def methods(level, keys):
        return sum(len(distinct[level][key]) * weight.get(key, 0) for key in keys)

    everything = list(distinct[0])
    kept = [key for key in everything if key in KEPT]
    others = [key for key in everything if key not in KEPT]
    for label, keys in (("all", everything), ("List (kept as it is)", kept), ("all but List", others)):
        print("%s: %d closed generic class instances of %d definitions, %d methods on them" % (label, count(0, keys), len(keys), methods(0, keys)))
        for level, name in ((1, "refs"), (2, "+ enums"), (3, "+ shapes")):
            print("    %-9s %6d instances left (%4.1f%%)   methods %7d (%4.1f%%)" % (
                name, count(level, keys), 100.0 * count(level, keys) / max(1, count(0, keys)), methods(level, keys), 100.0 * methods(level, keys) / max(1, methods(0, keys))))
    whose = collections.Counter()
    for key in everything:
        whose.update(owners[key])
    print("whose types the arguments are: " + ", ".join("%s %d" % pair for pair in whose.most_common()))

    elements = [name for name in arrays if not (left_out and left_out.search(name))]
    by_kind = collections.Counter("ref" if kinds.get(name, ("?",))[0] in REFERENCE or kinds.get(name, ("?",))[0] == "GENERICINST" and canon(name, 1) == "ref" else "value" for name in elements)
    print("array types (not generic classes, so not in the counts above): %d  (%s)" % (len(elements), ", ".join("%s %d" % pair for pair in by_kind.most_common())))
    print()
    print("  now  refs enums shape  meth  kind      proj  pkg  definition")
    rows = sorted(everything, key=lambda key: -len(distinct[0][key]) * max(1, weight.get(key, 0)))
    for key in rows[:int(os.environ.get("TOP", "40"))]:
        info = known.get(key)
        print("%5d %5d %5d %5d %5d  %-9s %4d %4d  %s" % (
            len(distinct[0][key]), len(distinct[1][key]), len(distinct[2][key]), len(distinct[3][key]),
            weight.get(key, 0), info[1] if info else "?", owners[key]["project"], owners[key]["package"], info[9] if info else key))


main()
