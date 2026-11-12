"""Keeps what one Editor build of the bench Player was made from, so that build.py can make Players without the Editor.

usage: save-inputs.py <name> <label>
  <name>   the folder under tools/il2cpp-generic-memory/.local/inputs. Its Managed folder has to be there already: the Editor
           writes it during the build when GENERIC_MEMORY_SAVE_MANAGED names it (editor-build.sh does both).
  <label>  the folder of Logs/UnityTests/GenericMemory the Editor build went to.

Run it right after the build: the next Player build of the Editor writes over Library/Bee, which this reads.

What is kept in .local/inputs/<name>:
  Managed/              every assembly as the linker got it (written by the Editor, not by this script)
  bee/dag.json, bee/dag.payloads   the build graph of that build: every command line, response file and lump
  rsp/                  the response files of UnityLinker and of il2cpp, as files to read; build.py takes them from bee/
  project/              the files of the project the linker's response file names (link.xml and the Editor's data for the linker)
  player/               the built Player without its backup folder: what build.py copies and then puts its own code into
  il2cppOutput/cpp, il2cppOutput/data   the C++ and the metadata the Editor build generated
  ManagedStripped/      what the linker left in the Editor build
  libil2cpp-sources.txt the .cpp and .c files the il2cpp folder of the build had, to tell later which files are new
  info.json             where everything came from
"""
import datetime
import glob
import json
import os
import re
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", "..", "..")).replace("\\", "/")
LOCAL = os.path.normpath(os.path.join(HERE, "..", ".local")).replace("\\", "/")


def fail(message):
    print("save-inputs: " + message, file=sys.stderr)
    sys.exit(1)


def find_dag(label):
    """The newest build graph that copies its GameAssembly.dll into the label's folder."""
    wanted = "Logs/UnityTests/GenericMemory/%s/GameAssembly.dll" % label
    found = []
    for path in glob.glob(PROJECT + "/Library/Bee/Player*.dag.json"):
        with open(path, encoding="utf-8") as file:
            text = file.read()
        if '"' + wanted + '"' in text:
            found.append((os.path.getmtime(path), path))
    if not found:
        fail("no build graph in Library/Bee writes " + wanted)
    return max(found)[1].replace("\\", "/")


def copy_tree(source, target, skip=None):
    count = 0
    for root, folders, files in os.walk(source):
        if skip:
            folders[:] = [folder for folder in folders if not skip(folder)]
        for name in files:
            path = os.path.join(root, name)
            to = os.path.join(target, os.path.relpath(path, source))
            os.makedirs(os.path.dirname(to), exist_ok=True)
            shutil.copy2(path, to)
            count += 1
    return count


def main():
    if len(sys.argv) != 3:
        fail("usage: save-inputs.py <name> <label>")
    name, label = sys.argv[1], sys.argv[2]
    inputs = LOCAL + "/inputs/" + name
    managed = inputs + "/Managed"
    if not os.path.isdir(managed):
        fail(managed + " is not there: build in the Editor with GENERIC_MEMORY_SAVE_MANAGED set to it (editor-build.sh)")
    if os.path.exists(inputs + "/info.json"):
        fail(inputs + " is complete already; give another name")
    player = PROJECT + "/Logs/UnityTests/GenericMemory/" + label
    if not os.path.isfile(player + "/GameAssembly.dll"):
        fail(player + " has no GameAssembly.dll")
    backup = [folder for folder in os.listdir(player) if folder.endswith("_BackUpThisFolder_ButDontShipItWithYourGame")]
    data = [folder for folder in os.listdir(player) if folder.endswith("_Data")]
    if len(backup) != 1 or len(data) != 1 or not os.path.isfile(player + "/" + data[0][:-len("_Data")] + ".exe"):
        fail("%s does not look like one Player: backup %s, data %s" % (player, backup, data))

    dag_path = find_dag(label)
    newest = max(glob.glob(PROJECT + "/Library/Bee/Player*.dag.json"), key=os.path.getmtime).replace("\\", "/")
    if newest != dag_path:
        fail("%s is the graph of %s, but %s is newer: another Player was built since, and Library/Bee no longer matches" % (dag_path, label, newest))
    with open(dag_path, encoding="utf-8") as file:
        dag = json.load(file)
    payloads_path = dag["PayloadsFile"]
    with open(payloads_path, "rb") as file:
        payloads = file.read()

    def payload_of(output):
        for node in dag["Nodes"]:
            if node.get("ActionType") == "WriteFile" and node["Outputs"][0].replace("\\", "/") == output.replace("\\", "/"):
                return payloads[node["PayloadOffset"]:node["PayloadOffset"] + node["PayloadLength"]]
        fail("the graph does not write " + output)

    def node_of(kind):
        nodes = [node for node in dag["Nodes"] if node.get("Annotation", "").split(" ")[0] == kind]
        if len(nodes) != 1:
            fail("%d nodes of %s in %s" % (len(nodes), kind, dag_path))
        return nodes[0]

    def response_of(node):
        return payload_of(re.search(r'@"([^"]+)"', node["Action"]).group(1))

    linker = node_of("UnityLinker")
    convert = node_of("IL2CPP_CodeGen")
    il2cpp = re.match(r'"(.+)/UnityLinker/bin/Release/net/UnityLinker\.exe"', linker["Action"])
    if not il2cpp:
        fail("the il2cpp folder is not in the linker's command: " + linker["Action"][:300])
    il2cpp = il2cpp.group(1)
    linker_response = response_of(linker).decode("utf-8-sig")
    convert_response = response_of(convert).decode("utf-8-sig")

    assemblies = re.findall(r'--allowed-assembly="([^"]+)"', linker_response)
    missing = [path for path in assemblies if not os.path.isfile(managed + "/" + os.path.basename(path))]
    if missing:
        fail("%d assemblies of the linker's response file are not in %s, the first is %s" % (len(missing), managed, missing[0]))

    os.makedirs(inputs + "/bee")
    shutil.copy2(dag_path, inputs + "/bee/dag.json")
    shutil.copy2(payloads_path, inputs + "/bee/dag.payloads")
    os.makedirs(inputs + "/rsp")
    with open(inputs + "/rsp/UnityLinker.rsp", "w", encoding="utf-8", newline="") as file:
        file.write(linker_response)
    with open(inputs + "/rsp/il2cpp.rsp", "w", encoding="utf-8", newline="") as file:
        file.write(convert_response)

    # Files of the project the linker reads, other than the assemblies. They keep their path below the project.
    project_files = []
    staging = PROJECT + "/Temp/StagingArea/"
    for path in sorted(set(re.findall(r'"([^"]+)"', linker_response))):
        path = path.replace("\\", "/")
        if path.startswith(PROJECT + "/") and not path.startswith(staging) and os.path.isfile(path):
            relative = path[len(PROJECT) + 1:]
            os.makedirs(os.path.dirname(inputs + "/project/" + relative), exist_ok=True)
            shutil.copy2(path, inputs + "/project/" + relative)
            project_files.append(relative)

    player_files = copy_tree(player, inputs + "/player", skip=lambda folder: folder in backup)
    cpp_files = copy_tree(player + "/" + backup[0] + "/il2cppOutput", inputs + "/il2cppOutput/cpp")
    copy_tree(player + "/" + data[0] + "/il2cpp_data", inputs + "/il2cppOutput/data")
    copy_tree(player + "/" + backup[0] + "/Managed", inputs + "/ManagedStripped")

    sources = []
    for root, _, files in os.walk(il2cpp + "/libil2cpp"):
        for file_name in files:
            if file_name.endswith((".cpp", ".c")):
                sources.append(os.path.relpath(os.path.join(root, file_name), il2cpp + "/libil2cpp").replace("\\", "/"))
    with open(inputs + "/libil2cpp-sources.txt", "w", encoding="utf-8", newline="\n") as file:
        file.write("\n".join(sorted(sources)) + "\n")

    info = {
        "name": name,
        "label": label,
        "saved": datetime.datetime.now().isoformat(timespec="seconds"),
        "project": PROJECT,
        "il2cpp": il2cpp,
        "dag": os.path.basename(dag_path),
        "dataFolder": data[0],
        "assemblies": len(assemblies),
        "projectFiles": project_files,
        "playerFiles": player_files,
        "generatedFiles": cpp_files,
    }
    # Written last: its presence says the set is complete.
    with open(inputs + "/info.json", "w", encoding="utf-8", newline="\n") as file:
        json.dump(info, file, indent=1)
    print("saved %s: %d assemblies, %d project files, %d Player files, %d generated files, il2cpp folder %s" % (
        inputs, len(assemblies), len(project_files), player_files, cpp_files, il2cpp))


main()
