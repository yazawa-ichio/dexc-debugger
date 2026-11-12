"""Keeps what a later look at an Android build of the bench needs, before the next build writes over Library/Bee.

usage: d-android-keep.py <label> <il2cpp folder>

Called by d-android-build.sh inside the Editor's lock. It finds the build's dag (the newest Library/Bee/Player*.dag.json
that links libil2cpp.so for Android), checks that the dag names the libil2cpp of <il2cpp folder> and nothing of the Editor's own il2cpp folder, and copies
to .local/d/builds/<label>: the dag, the library with its symbols (libil2cpp.sym.so; the one with the debug information
too when it is under 1.5 GB), and keep.json with the sizes and what was counted. Fails when another libil2cpp is named.
"""
import glob
import json
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))


def main():
    label, il2cpp = sys.argv[1], sys.argv[2].replace("\\", "/").rstrip("/")
    target = os.path.join(HERE, ".local", "d", "builds", label)
    os.makedirs(target, exist_ok=True)
    found = None
    for path in sorted(glob.glob(os.path.join(PROJECT, "Library", "Bee", "Player*.dag.json")), key=os.path.getmtime, reverse=True):
        with open(path, encoding="utf-8", errors="replace") as file:
            text = file.read()
        if "libil2cpp.so" in text and "aarch64-linux-android" in text:
            found = (path, text)
            break
    if found is None:
        sys.exit("d-android-keep: no dag of an Android build in Library/Bee")
    path, text = found
    # How often the dag and its payloads (the lumps and the response files) name the libil2cpp of the folder, and how
    # often anything of the Editor's own il2cpp folder.
    roots = {il2cpp: 0, "Editor/Data/il2cpp": 0}
    for part in (path, path[:-len(".json")] + ".payloads"):
        if not os.path.exists(part):
            continue
        with open(part, "rb") as file:
            low = file.read().lower().replace(b"\\\\", b"/").replace(b"\\", b"/")
        roots[il2cpp] += low.count((il2cpp + "/libil2cpp").lower().encode("utf-8"))
        roots["Editor/Data/il2cpp"] += low.count(b"editor/data/il2cpp/")
        shutil.copyfile(part, os.path.join(target, "dag" + os.path.splitext(part)[1]))
    others = {root: count for root, count in roots.items() if root != il2cpp and count}
    nodes = json.loads(text)["Nodes"]
    kept = {}
    for node in nodes:
        annotation = node.get("Annotation", "")
        if annotation.startswith("NdkObjCopy ") and annotation.endswith("/libil2cpp.sym.so"):
            folder = os.path.dirname(os.path.join(PROJECT, annotation.split(" ", 1)[1]))
            for name in ("libil2cpp.sym.so", "libil2cpp.dbg.so", "stripped/libil2cpp.so"):
                source = os.path.join(folder, name)
                if not os.path.exists(source):
                    continue
                kept[name] = os.path.getsize(source)
                if name == "libil2cpp.sym.so" or (name == "libil2cpp.dbg.so" and kept[name] < 1500 * 1024 * 1024):
                    shutil.copyfile(source, os.path.join(target, os.path.basename(name)))
    apk = os.path.join(PROJECT, "Logs", "UnityTests", "GenericMemory", label, "GenericMemory.apk")
    summary = {
        "label": label,
        "dag": os.path.basename(path),
        "il2cpp": il2cpp,
        "libil2cppPathsByRoot": roots,
        "otherRoots": others,
        "compileNodes": sum(1 for node in nodes if node.get("Annotation", "").startswith("C_")),
        "libraries": kept,
        "apkBytes": os.path.getsize(apk) if os.path.exists(apk) else None,
    }
    with open(os.path.join(target, "keep.json"), "w", encoding="utf-8") as file:
        json.dump(summary, file, indent=1)
    print("d-android-keep: %s: libil2cpp paths %s, libraries %s, apk %s" % (label, roots, kept, summary["apkBytes"]))
    if others or not roots[il2cpp]:
        sys.exit("d-android-keep: the dag names a libil2cpp that is not of " + il2cpp + ": " + json.dumps(others))


main()
