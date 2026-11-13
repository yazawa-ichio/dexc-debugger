"""Keeps what a later look at an Android build of the bench needs, before the next build writes over Library/Bee,
and writes down which flags the C++ compiler and the linker really got. d-android-keep.py for the builds of
e-android-build.sh, which calls this inside the Editor's lock.

usage: e-android-keep.py <label> <il2cpp folder>

It finds the build's dag (the newest Library/Bee/Player*.dag.json that links libil2cpp.so for Android), checks that
the dag names the libil2cpp of <il2cpp folder> and nothing of the Editor's own il2cpp folder, and copies to
.local/e/builds/<label>: the dag and its payloads, the library with its symbols (libil2cpp.sym.so; not the one with
the debug information, which is 670 MB), and writes
  keep.json   the sizes, what was counted, and for every kind of compilation how many nodes had which -O flags
  flags.txt   one whole command line of each kind (generated C++, libil2cpp, the collector's C, the source plugin
              NumericNative.cpp), the linker's line with its response file, and what the APK holds of native libraries
Fails when another libil2cpp is named.
"""
import glob
import json
import os
import re
import shutil
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))
FLAG = re.compile(r'(?<![\w/"=-])(-O[0-3sz]|-Ofast|-flto(?:=\w+)?|-ffast-math|-march=[\w.+-]+|-DGENERIC_MEMORY_NATIVE)(?![\w-])')


def kind_of(action):
    """Which of the programs of the build a compiler's command line belongs to."""
    low = action.replace("\\", "/")
    if "NumericNative.cpp" in low:
        return "source plugin (NumericNative.cpp)"
    if "/external/bdwgc/" in low and low.rstrip().endswith('.c"'):
        return "collector (C)"
    if "/libil2cpp/" in low.split(" -c ")[-1] or ".lump.cpp" in low.split(" -c ")[-1]:
        return "libil2cpp"
    if "il2cppOutput" in low.split(" -c ")[-1]:
        return "generated C++"
    return "other"


def main():
    label, il2cpp = sys.argv[1], sys.argv[2].replace("\\", "/").rstrip("/")
    target = os.path.join(HERE, ".local", "e", "builds", label)
    os.makedirs(target, exist_ok=True)
    found = None
    for path in sorted(glob.glob(os.path.join(PROJECT, "Library", "Bee", "Player*.dag.json")), key=os.path.getmtime, reverse=True):
        with open(path, encoding="utf-8", errors="replace") as file:
            text = file.read()
        if "libil2cpp.so" in text and "aarch64-linux-android" in text:
            found = (path, text)
            break
    if found is None:
        sys.exit("e-android-keep: no dag of an Android build in Library/Bee")
    path, text = found
    roots = {il2cpp: 0, "Editor/Data/il2cpp": 0}
    payloads = b""
    for part in (path, path[:-len(".json")] + ".payloads"):
        if not os.path.exists(part):
            continue
        with open(part, "rb") as file:
            data = file.read()
        if part.endswith(".payloads"):
            payloads = data
        low = data.lower().replace(b"\\\\", b"/").replace(b"\\", b"/")
        roots[il2cpp] += low.count((il2cpp + "/libil2cpp").lower().encode("utf-8"))
        roots["Editor/Data/il2cpp"] += low.count(b"editor/data/il2cpp/")
        shutil.copyfile(part, os.path.join(target, "dag" + os.path.splitext(part)[1]))
    others = {root: count for root, count in roots.items() if root != il2cpp and count}
    nodes = json.loads(text)["Nodes"]

    kept = {}
    flags = {}
    samples = {}
    lines = []
    for node in nodes:
        annotation = node.get("Annotation", "")
        action = node.get("Action") or ""
        if annotation.startswith("NdkObjCopy ") and annotation.endswith("/libil2cpp.sym.so"):
            folder = os.path.dirname(os.path.join(PROJECT, annotation.split(" ", 1)[1]))
            for name in ("libil2cpp.sym.so", "libil2cpp.dbg.so", "stripped/libil2cpp.so"):
                source = os.path.join(folder, name)
                if not os.path.exists(source):
                    continue
                kept[name] = os.path.getsize(source)
                if name == "libil2cpp.sym.so":
                    shutil.copyfile(source, os.path.join(target, name))
        if annotation.startswith("C_Android_arm64"):
            kind = kind_of(action)
            order = " ".join(FLAG.findall(action))
            flags.setdefault(kind, {}).setdefault(order, 0)
            flags[kind][order] += 1
            samples.setdefault(kind, action)
        if annotation.startswith("Link_Android_arm64") and "libil2cpp.so" in annotation:
            lines.append("== link: " + annotation)
            lines.append(action)
            response = re.search(r'@"([^"]+\.rsp)"', action)
            if response:
                name = response.group(1).replace("\\", "/")
                file = os.path.join(PROJECT, name)
                if os.path.exists(file):
                    with open(file, encoding="utf-8", errors="replace") as opened:
                        content = opened.read()
                    shutil.copyfile(file, os.path.join(target, "link.rsp"))
                    # Without the object files: the flags of the link.
                    words = [word for word in re.findall(r'(?:[^\s"]|"[^"]*")+', content) if not word.strip('"').endswith(".o")]
                    lines.append("== link response file (%s), without its %d object files:" % (name, content.count(".o\"") + content.count(".o ")))
                    lines.append(" ".join(words))
                    flags["link"] = {" ".join(FLAG.findall(content)): 1}
    for kind in sorted(samples):
        lines.append("== " + kind + ": " + json.dumps(flags[kind]))
        lines.append(samples[kind])

    apk = os.path.join(PROJECT, "Logs", "UnityTests", "GenericMemory", label, "GenericMemory.apk")
    libraries = {}
    if os.path.exists(apk):
        with zipfile.ZipFile(apk) as archive:
            for item in archive.infolist():
                if item.filename.startswith("lib/"):
                    libraries[item.filename] = item.file_size
    lines.append("== native libraries of the APK (bytes before compression): " + json.dumps(libraries))
    with open(os.path.join(target, "flags.txt"), "w", encoding="utf-8", newline="\n") as file:
        file.write("\n".join(lines) + "\n")

    summary = {
        "label": label,
        "dag": os.path.basename(path),
        "il2cpp": il2cpp,
        "libil2cppPathsByRoot": roots,
        "otherRoots": others,
        "compileNodes": sum(1 for node in nodes if node.get("Annotation", "").startswith("C_")),
        "flagsByKind": flags,
        "libraries": kept,
        "apkLibraries": libraries,
        "apkBytes": os.path.getsize(apk) if os.path.exists(apk) else None,
    }
    with open(os.path.join(target, "keep.json"), "w", encoding="utf-8") as file:
        json.dump(summary, file, indent=1)
    print("e-android-keep: %s: libil2cpp paths %s, libraries %s, apk %s" % (label, roots, kept, summary["apkBytes"]))
    for kind in sorted(flags):
        print("e-android-keep:   %s: %s" % (kind, json.dumps(flags[kind])))
    if others or not roots[il2cpp]:
        sys.exit("e-android-keep: the dag names a libil2cpp that is not of " + il2cpp + ": " + json.dumps(others))


main()
