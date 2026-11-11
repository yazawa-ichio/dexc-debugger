"""Sums the rows of the generic definitions over the instances the il2cpp output knows, by whether a row could leave."""
import collections, os, re, sys
folder, types, left_out = sys.argv[1], sys.argv[2], re.compile(sys.argv[3])
base = lambda name: re.sub(r"_\d+_\d+_\d+$", "", name)
simple = lambda name: re.sub(r"_t[0-9A-F]{40}$", "", name)
known = {}
for line in open(types, encoding="utf-8", errors="replace"):
    parts = line.rstrip("\n").split("\t")
    if len(parts) >= 14:
        known[parts[0]] = None if parts[0] in known else parts
known = {name: parts for name, parts in known.items() if parts}
text = open(os.path.join(folder, "Il2CppTypeDefinitions.c"), encoding="utf-8-sig").read()
kinds = {base(m.group(1)): m.group(2) for m in re.finditer(r"Il2CppType (\w+) = \{ (?:\(void\*\)\d+|&\w+), \d+, IL2CPP_TYPE_(\w+),", text)}
classes = {m.group(1): (base(m.group(2)), m.group(3)) for m in re.finditer(r"Il2CppGenericClass (\w+)_GenericClass = \{ &(\w+), \{ (?:&(\w+)|NULL), (?:&(\w+)|NULL) \}", text)}
insts = {}
text = open(os.path.join(folder, "Il2CppGenericInstDefinitions.c"), encoding="utf-8-sig").read()
for m in re.finditer(r"static const Il2CppType\* (\w+)_Types\[\] = \{ (.*?) \};", text):
    insts[m.group(1)] = [base(name) for name in re.findall(r"&(\w+)", m.group(2))]
count = collections.Counter()
for name, (definition, inst) in classes.items():
    arguments = insts.get(inst, [])
    if any(left_out.search(a) for a in arguments) or any(kinds.get(a) in ("VAR", "MVAR") for a in arguments):
        continue
    count[simple(definition)] += 1
total = collections.Counter()
by_kind = collections.defaultdict(collections.Counter)
rows = []
for key, instances in count.items():
    info = known.get(key)
    if not info:
        total["unknown definitions"] += instances
        continue
    methods, virtual, ctors, accessors = int(info[3]), int(info[11]), int(info[12]), int(info[13])
    movable = methods - virtual - ctors if info[1] in ("class", "struct") else 0
    for label, value in (("rows", methods), ("virtual", virtual), ("constructors", ctors), ("can leave", movable), ("field accessors", accessors if info[1] in ("class", "struct") else 0)):
        total[label] += value * instances
        by_kind[info[1]][label] += value * instances
    by_kind[info[1]]["instances"] += instances
    rows.append((movable * instances, instances, methods, movable, accessors, info[1], info[9]))
print("rows of generic definitions summed over the instances: %d" % total["rows"])
for label in ("can leave", "field accessors", "virtual", "constructors"):
    print("  %-16s %7d (%4.1f%%)" % (label, total[label], 100.0 * total[label] / total["rows"]))
print()
print("%-10s %9s %8s %10s %8s" % ("kind", "instances", "rows", "can leave", "share"))
for kind, values in sorted(by_kind.items(), key=lambda pair: -pair[1]["rows"]):
    print("%-10s %9d %8d %10d %7.1f%%" % (kind, values["instances"], values["rows"], values["can leave"], 100.0 * values["can leave"] / max(1, values["rows"])))
print()
print(" leave*inst  inst  rows leave accs  kind       definition")
for row in sorted(rows, reverse=True)[:22]:
    print("%10d %5d %5d %5d %4d  %-9s  %s" % row)
