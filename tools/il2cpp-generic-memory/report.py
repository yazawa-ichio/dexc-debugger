"""Turns the result files of measure.sh into what one more type costs.

usage: report.py <results folder> [<results folder to compare with>] [--sites <pattern>]

Each pattern ran at two counts. The slope between them is the cost of one more type, free of whatever is paid once.
bytes   what the runtime's allocator and its hash tables hold more (pool regions, so it moves in 64 KB steps)
pool    what was asked from the metadata pools, exact
"""

import os
import re
import sys

# What the allocation at a line of libil2cpp (Unity 6000.3.20f1) is. A line that is not here is shown by file and line.
SITES = {
    "GenericMethod.cpp:54": "MethodInfo (inflated)",
    "GenericMethod.cpp:271": "Il2CppGenericMethod of a MethodInfo",
    "GenericMetadata.cpp:44": "parameter type array",
    "GenericMetadata.cpp:65": "Il2CppType (inflated parameter)",
    "GenericMetadata.cpp:92": "Il2CppType (inflated array)",
    "GenericMetadata.cpp:95": "Il2CppArrayType",
    "GenericMetadata.cpp:112": "Il2CppType (inflated szarray or pointer)",
    "GenericMetadata.cpp:132": "Il2CppType (inflated generic inst)",
    "GenericMetadata.cpp:263": "rgctx array",
    # The same allocations once the rgctx change of Il2CppLazyMetadata.cs has moved the lines of GenericMetadata.cpp.
    "GenericMetadata.cpp:52": "parameter type array",
    "GenericMetadata.cpp:73": "Il2CppType (inflated parameter)",
    "GenericMetadata.cpp:100": "Il2CppType (inflated array)",
    "GenericMetadata.cpp:103": "Il2CppArrayType",
    "GenericMetadata.cpp:120": "Il2CppType (inflated szarray or pointer)",
    "GenericMetadata.cpp:140": "Il2CppType (inflated generic inst)",
    "GenericMetadata.cpp:274": "rgctx array (entries resolved when read)",
    "GenericMetadata.cpp:281": "rgctx array",
    "GenericClass.cpp:32": "method pointer array (generic instance)",
    "GenericClass.cpp:70": "PropertyInfo array (generic instance)",
    "GenericClass.cpp:108": "EventInfo array (generic instance)",
    "GenericClass.cpp:144": "FieldInfo array (generic instance)",
    "GenericClass.cpp:178": "Il2CppClass and vtable (generic instance)",
    "ArrayMetadata.cpp:524": "Il2CppClass and vtable (array)",
    "ArrayMetadata.cpp:287": "MethodInfo (array's generic interface method)",
    "ArrayMetadata.cpp:118": "method pointer array (array)",
    "ArrayMetadata.cpp:64": "MethodInfo (array ctor, Get, Set, Address)",
    "ArrayMetadata.cpp:72": "parameter type array (array)",
    "ArrayMetadata.cpp:342": "interface offsets (array)",
    "ArrayMetadata.cpp:402": "interface list (array)",
    "Class.cpp:1141": "method pointer array (plain class)",
    "Class.cpp:1142": "MethodInfo (plain class)",
    "Class.cpp:1170": "parameter type array (plain class)",
    "Class.cpp:1056": "FieldInfo array (plain class)",
    "Class.cpp:1413": "PropertyInfo array (plain class)",
    "Class.cpp:1457": "type hierarchy",
    "Class.cpp:191": "interface list (generic instance)",
    "Class.cpp:205": "interface list (plain class)",
    "Class.cpp:1256": "interface offsets (generic instance)",
    "Class.cpp:1316": "interface offsets (plain class)",
    "MetadataCache.cpp:481": "Il2CppGenericInst",
    "MetadataCache.cpp:483": "type arguments of an Il2CppGenericInst",
    "Il2CppGenericClass:0": "Il2CppGenericClass",
    "Il2CppGenericMethod:0": "Il2CppGenericMethod (pool)",
}


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


def slope(runs, pattern, key):
    counts = sorted(runs[pattern])
    if len(counts) < 2:
        return None
    low, high = counts[0], counts[-1]
    return (float(runs[pattern][high][0].get(key, 0)) - float(runs[pattern][low][0].get(key, 0))) / (high - low)


def site_slopes(runs, pattern):
    counts = sorted(runs[pattern])
    low, high = counts[0], counts[-1]
    result = {}
    for site in set(runs[pattern][low][1]) | set(runs[pattern][high][1]):
        a = runs[pattern][low][1].get(site, (0, 0))
        b = runs[pattern][high][1].get(site, (0, 0))
        result[site] = ((b[0] - a[0]) / (high - low), (b[1] - a[1]) / (high - low))
    return result


COLUMNS = [
    ("pool", "pool B"),
    ("heap", "heap B"),
    ("genericClasses", "gen.class"),
    ("inflatedMethods", "infl.method"),
    ("initializedClasses", "init.class"),
    ("genericInstances", "gen.inst"),
]


def main():
    argv = sys.argv[1:]
    args = [arg for at, arg in enumerate(argv) if not arg.startswith("--") and (at == 0 or argv[at - 1] != "--sites")]
    runs = read(args[0])
    other = read(args[1]) if len(args) > 1 else None
    if "--sites" in sys.argv:
        pattern = sys.argv[sys.argv.index("--sites") + 1]
        rows = sorted(site_slopes(runs, pattern).items(), key=lambda row: -row[1][1])
        base = site_slopes(other, pattern) if other and pattern in other else {}
        print("%-48s %9s %9s%s" % (pattern, "count", "bytes", "   (compared: bytes)" if other else ""))
        for site, (count, size) in rows:
            if abs(size) < 0.5 and abs(base.get(site, (0, 0))[1]) < 0.5:
                continue
            extra = "   %9.1f" % base.get(site, (0, 0))[1] if other else ""
            print("%-48s %9.2f %9.1f%s" % (SITES.get(site, site), count, size, extra))
        for site, (count, size) in sorted(base.items(), key=lambda row: -row[1][1]):
            if site not in dict(rows) and abs(size) >= 0.5:
                print("%-48s %9.2f %9.1f   %9.1f" % (SITES.get(site, site), 0, 0, size))
        return

    header = "%-14s" % "pattern" + "".join(" %11s" % title for _, title in COLUMNS)
    if other:
        header += " %11s %7s" % ("pool B was", "saved")
    print(header)
    for pattern in sorted(runs):
        if len(runs[pattern]) < 2:
            print("%-14s (one run only)" % pattern)
            continue
        line = "%-14s" % pattern + "".join(" %11.1f" % slope(runs, pattern, key) for key, _ in COLUMNS)
        if other and pattern in other and len(other[pattern]) > 1:
            was = slope(other, pattern, "pool")
            now = slope(runs, pattern, "pool")
            line += " %11.1f %6.1f%%" % (was, 100.0 * (was - now) / was if was else 0.0)
        pending = max(int(run[0].get("pending", 0)) for run in runs[pattern].values())
        if pending:
            line += "   (pending %d)" % pending
        print(line)


main()
