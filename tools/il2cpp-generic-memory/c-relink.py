"""Makes a Player that differs from a built one in a few files of generated C++: the unit test of "what if il2cpp had
generated this instead". The files are compiled with the exact command of the work folder that built the Player, and
the link is the same link with those objects swapped. Nothing of the work folder or of the built Player is changed.

usage: c-relink.py --work <work folder> --from <label> --label <new label> --replace <generated file>=<edited copy> [...]
  --work     a folder of .local/work that built the Player of --from, still as that build left it
  --from     the label of that Player (Logs/UnityTests/GenericMemory/<label>)
  --label    the label of the new Player (starts with c-)
  --replace  DexcDebugger.GenericMemory.Scenario__1.cpp=.local/c/cppexp/Scenario__1.cpp  (several times)

The objects, the link's files and its log go to .local/c/relink/<new label>.
"""
import argparse
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__)).replace("\\", "/")
PROJECT = os.path.normpath(os.path.join(HERE, "..", "..")).replace("\\", "/")
LOCAL = HERE + "/.local"


def fail(message):
    sys.exit("c-relink: " + message)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--work", required=True)
    parser.add_argument("--from", dest="source", required=True)
    parser.add_argument("--label", required=True)
    parser.add_argument("--replace", action="append", required=True)
    options = parser.parse_args()
    if not options.label.startswith("c-"):
        fail("the label starts with c-")
    work = LOCAL + "/work/" + options.work
    players = PROJECT + "/Logs/UnityTests/GenericMemory/"
    built = json.load(open(players + options.source + "/offline-build.json", encoding="utf-8"))
    if built["work"].lower() != work.lower():
        fail("%s was built in %s, not in %s" % (options.source, built["work"], work))
    out = LOCAL + "/c/relink/" + options.label
    os.makedirs(out, exist_ok=True)

    compiler = next(node for node in json.load(open(built["inputs"] + "/bee/dag.json", encoding="utf-8"))["Nodes"] if node.get("Annotation", "").startswith("C_"))
    environment = dict(os.environ)
    for name in ("INCLUDE", "LIB", "LIBPATH", "CL", "_CL_", "LINK", "_LINK_"):
        environment.pop(name, None)
    variables = {item["Key"]: item["Value"] for item in compiler.get("Env") or []}
    system = os.environ.get("SystemRoot", "C:\\Windows")
    environment["PATH"] = variables.get("PATH", "") + ";" + system + "\\System32;" + system
    environment["_MSPDBSRV_ENDPOINT_"] = "c-relink-" + options.label
    os.makedirs(out + "/tmp", exist_ok=True)
    environment["TMP"] = environment["TEMP"] = (out + "/tmp").replace("/", "\\")

    states = {}
    for state in glob.glob(work + "/build/*/*.obj.state.json"):
        data = json.load(open(state, encoding="utf-8"))
        source = re.search(r'/FC "([^"]+)"', data["command"])
        if source:
            states[os.path.basename(source.group(1)).lower()] = (state[:-len(".state.json")].replace("\\", "/"), data["command"])

    swapped = {}
    started = time.time()
    for item in options.replace:
        name, edited = item.split("=", 1)
        edited = os.path.abspath(edited).replace("\\", "/")
        if name.lower() not in states:
            fail("the work folder compiled no " + name)
        object_path, command = states[name.lower()]
        new_object = out + "/" + os.path.splitext(name)[0] + ".obj"
        source = re.search(r'/FC ("[^"]+")', command).group(1)
        command = command.replace(source, '"%s"' % edited.replace("/", "\\"))
        command = re.sub(r'/Fo"[^"]*"', lambda _: '/Fo"%s"' % new_object.replace("/", "\\"), command)
        command = re.sub(r'\s/sourceDependencies "[^"]*"', "", command)
        done = subprocess.run(command, cwd=work, env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if done.returncode != 0:
            fail("the compiler failed for %s:\n%s" % (name, done.stdout.decode("utf-8", "replace")[-6000:]))
        swapped[object_path.lower()] = new_object
        print("compiled %s, %.0f s" % (name, time.time() - started), flush=True)

    link_log = open(work + "/logs/link.log", encoding="utf-8", errors="replace").readline().strip()
    response_path = re.search(r'@"([^"]+)"', link_log).group(1)
    lines = open(response_path, encoding="utf-8-sig").read().splitlines()
    assembly = out + "/GameAssembly.dll"
    found = 0
    for at, line in enumerate(lines):
        bare = line.strip('"').replace("\\", "/")
        if bare.lower() in swapped:
            lines[at] = '"%s"' % swapped[bare.lower()]
            found += 1
        elif line.startswith("/OUT:"):
            lines[at] = '/OUT:"%s"' % assembly
        elif line.startswith("/PDB:"):
            lines[at] = '/PDB:"%s"' % (out + "/GameAssembly.pdb")
        elif line.startswith("/IMPLIB:"):
            lines[at] = '/IMPLIB:"%s"' % (out + "/GameAssembly.dll.lib")
    if found != len(swapped):
        fail("the link of the work folder names %d of the %d objects" % (found, len(swapped)))
    new_response = out + "/link.rsp"
    with open(new_response, "w", encoding="utf-8-sig", newline="\n") as file:
        file.write("\n".join(lines) + "\n")
    command = link_log.replace('@"%s"' % response_path, '@"%s"' % new_response.replace("/", "\\"))
    with open(out + "/link.log", "wb") as log:
        code = subprocess.run(command, cwd=work, env=environment, stdout=log, stderr=subprocess.STDOUT).returncode
    server = os.path.dirname(re.match(r'"([^"]+)"', link_log).group(1)) + "/mspdbsrv.exe"
    subprocess.run('"%s" -endpoint %s -stop' % (server, environment["_MSPDBSRV_ENDPOINT_"]), cwd=work, env=environment, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if code != 0:
        fail("link ended with %d, see %s" % (code, out + "/link.log"))
    print("linked, %.0f s" % (time.time() - started), flush=True)

    player = players + options.label
    if not os.path.isdir(player):
        # Without the marker of build.py: this Player is not the product of the build it was copied from.
        shutil.copytree(players + options.source, player, ignore=shutil.ignore_patterns("offline-build.json"))
    elif not os.path.isfile(player + "/c-relink.json"):
        fail(player + " exists and was not made by c-relink.py")
    shutil.copy2(assembly, player + "/GameAssembly.dll")
    with open(player + "/c-relink.json", "w", encoding="utf-8", newline="\n") as file:
        json.dump({"from": options.source, "work": work, "replaced": options.replace, "objects": out,
                   "assemblyBytes": os.path.getsize(assembly)}, file, indent=1)
    print("%s: GameAssembly.dll %d bytes (was %d)" % (player, os.path.getsize(assembly), os.path.getsize(players + options.source + "/GameAssembly.dll")))


main()
