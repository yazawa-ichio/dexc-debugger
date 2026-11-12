"""Builds a Player of the generic-memory bench without the Editor, from what save-inputs.py kept of one Editor build.

usage: c-build.py --inputs <name> --il2cpp <folder> --work <folder> --label <label> [options]
  --inputs <name>       a folder of .local/inputs (or a path)
  --il2cpp <folder>     the il2cpp folder to build with: a folder of .local (or a path). Its libil2cpp is what gets compiled.
  --work <folder>       where everything in between goes: a folder of .local/work (or a path). One build at a time uses it;
                        two builds with two work folders do not touch each other.
  --label <label>       the Player goes to Logs/UnityTests/GenericMemory/<label>, where measure.sh and checks.sh look.
                        The folder has to be new, or one an earlier build.py made.
  --weave <step+step>   rewrite a copy of the assemblies with WeaverHost first (build WeaverHost with dotnet build -c Release)
  --weaver <exe>        the WeaverHost.exe to use instead of the one in WeaverHost/bin/Release, for a build of its own
  --cpp-from <folder>   do not link or generate: compile the C++ of "inputs" (what the Editor build generated) or of another
                        work folder. This is the short way when only libil2cpp changed.
  --define <NAME=VALUE> one more define for libil2cpp and the generated C++ (may be given several times)
  --il2cpp-arg <arg>    one more argument for il2cpp's C++ generation, as --il2cpp-arg=--some-option (several times)
  --jobs <n>            compilers at a time, the number of cores by default
  --il2cpp-drop <arg>   take an argument of the saved build away from il2cpp, as --il2cpp-drop=--emit-null-checks
  --cl-drop <flag>      take a flag away from the compiler, for libil2cpp, the precompiled headers and the generated C++
  --cl-add <flag>       one more flag for the same compilations, as --cl-add=/GL
  --link-add <flag>     one more line for the linker, as --link-add=/LTCG
  --lib-add <flag>      one more line for lib
  --stop-after <stage>  managed, linker, convert or compile

The stages: (weave) -> UnityLinker -> il2cpp to C++ -> compile and link -> copy the saved Player and put the new
GameAssembly.dll and metadata into it. Each stage is skipped when what it reads is as it was the last time in this work
folder. The compiler's command lines are the ones of the saved Editor build, with the paths moved; nothing is written
to Library/Bee.
"""
import argparse
import concurrent.futures
import datetime
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import threading
import time

# This copy sits in tools/il2cpp-generic-memory, one folder above offline/build.py.
HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", "..")).replace("\\", "/")
LOCAL = os.path.normpath(os.path.join(HERE, ".local")).replace("\\", "/")
WEAVER = os.path.normpath(os.path.join(HERE, "WeaverHost", "bin", "Release")).replace("\\", "/")
TOKEN = re.compile(r'(?:[^\s"]|"[^"]*")+')
MARKER = "offline-build.json"
WORK_MARKER = "offline-work.json"


def fail(message):
    print("build: " + message, file=sys.stderr)
    sys.exit(1)


def say(message):
    print(message, flush=True)


def folder_argument(value, base):
    path = value if os.path.isabs(value) or "/" in value or "\\" in value else base + "/" + value
    return os.path.abspath(path).replace("\\", "/").rstrip("/")


def digest(*parts):
    sha = hashlib.sha1()
    for part in parts:
        sha.update(part if isinstance(part, bytes) else str(part).encode("utf-8"))
        sha.update(b"\0")
    return sha.hexdigest()


def file_digest(path):
    sha = hashlib.sha1()
    with open(path, "rb") as file:
        while True:
            block = file.read(1 << 20)
            if not block:
                break
            sha.update(block)
    return sha.hexdigest()


def read_json(path):
    try:
        with open(path, encoding="utf-8") as file:
            return json.load(file)
    except (OSError, ValueError):
        return None


def write_json(path, value):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path + ".tmp", "w", encoding="utf-8", newline="\n") as file:
        json.dump(value, file, indent=1)
    os.replace(path + ".tmp", path)


def write_if_changed(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    try:
        with open(path, "rb") as file:
            if file.read() == data:
                return
    except OSError:
        pass
    with open(path, "wb") as file:
        file.write(data)


class Paths:
    """Moves the paths of the saved build to the ones of this build, in a command line or a response file."""

    def __init__(self):
        self.rules = []

    def add(self, old, new, relative=False):
        pattern = r"[\\/]".join(re.escape(part) for part in old.split("/"))
        if relative:
            # Not the tail of a longer path: the copies of the project's files below the inputs keep their Library/... names.
            pattern = r"(?<![\w\\/:.\-])" + pattern
        self.rules.append((re.compile(pattern + r"(?![\w.\-])", re.IGNORECASE), new))

    def move(self, text):
        for pattern, new in self.rules:
            text = pattern.sub(lambda match, new=new: new.replace("/", "\\") if "\\" in match.group(0) else new, text)
        return text


class Graph:
    """The build graph of the saved Editor build."""

    def __init__(self, inputs):
        with open(inputs + "/bee/dag.json", encoding="utf-8") as file:
            self.nodes = json.load(file)["Nodes"]
        with open(inputs + "/bee/dag.payloads", "rb") as file:
            self.payloads = file.read()

    @staticmethod
    def kind(node):
        return node.get("Annotation", "").split(" ")[0]

    def one(self, prefix):
        nodes = [node for node in self.nodes if self.kind(node).startswith(prefix)]
        if len(nodes) != 1:
            fail("%d nodes of %s in the saved graph" % (len(nodes), prefix))
        return nodes[0]

    def written(self, output):
        output = output.replace("\\", "/")
        for node in self.nodes:
            if node.get("ActionType") == "WriteFile" and node["Outputs"][0].replace("\\", "/") == output:
                return self.payloads[node["PayloadOffset"]:node["PayloadOffset"] + node["PayloadLength"]]
        fail("the saved graph does not write " + output)

    def response(self, node):
        """The path of a node's response file as the graph has it, and its text."""
        path = re.search(r'@"([^"]+)"', node["Action"]).group(1)
        return path, self.written(path).decode("utf-8-sig")


class Build:
    def __init__(self, options):
        self.options = options
        self.inputs = folder_argument(options.inputs, LOCAL + "/inputs")
        self.il2cpp = folder_argument(options.il2cpp, LOCAL)
        self.work = folder_argument(options.work, LOCAL + "/work")
        self.info = read_json(self.inputs + "/info.json")
        if not self.info:
            fail(self.inputs + " has no info.json: it is not a set save-inputs.py completed")
        if not os.path.isfile(self.il2cpp + "/libil2cpp/il2cpp-config.h"):
            fail(self.il2cpp + " is not an il2cpp folder")
        if not re.fullmatch(r"[\w.+\-]+", options.label):
            fail("the label is a folder name: " + options.label)
        self.player = PROJECT + "/Logs/UnityTests/GenericMemory/" + options.label
        if os.path.exists(self.player) and not os.path.isfile(self.player + "/" + MARKER):
            fail(self.player + " exists and was not made by build.py; give another label")
        for path in (self.work, self.il2cpp):
            if path.lower().startswith((PROJECT + "/Library").lower()) or path.lower().startswith((PROJECT + "/Logs").lower()):
                fail(path + " is inside Library or Logs")
        self.graph = Graph(self.inputs)
        self.times = []
        self.notes = []
        self.signatures = {}
        self.signature_lock = threading.Lock()

        self.managed = self.inputs + "/Managed"
        if options.weave:
            self.managed = self.work + "/Managed"
        # UnityLinker writes the full path of each .pdb into its assembly, so the layout of an assembly follows the length
        # of this folder's path, and il2cpp was seen to number the types of a few assemblies in another order when only
        # that length differed. The folder gets a name that makes its path as long as the Editor's, whatever the work folder is called.
        editor = self.info["project"] + "/Library/Bee/artifacts/WinPlayerBuildProgram/ManagedStripped"
        room = len(editor) - len(self.work) - 1
        self.stripped = self.work + "/" + (("ManagedStripped" + "_" * room)[:room] if room > 0 else "ManagedStripped")
        self.path_length_differs = room <= 0
        generated = self.work
        if options.cpp_from:
            generated = self.inputs if options.cpp_from == "inputs" else folder_argument(options.cpp_from, LOCAL + "/work")
            if options.weave or options.il2cpp_arg or options.il2cpp_drop:
                fail("--cpp-from compiles C++ that exists already; --weave and --il2cpp-arg would change what is generated")
            if not os.path.isdir(generated + "/il2cppOutput/cpp") or not os.path.isdir(generated + "/il2cppOutput/data/Metadata"):
                fail(generated + " has no il2cppOutput/cpp and il2cppOutput/data")
        self.cpp = generated + "/il2cppOutput/cpp"
        self.data = generated + "/il2cppOutput/data"

        saved = self.info["project"]
        artifacts = "Library/Bee/artifacts"
        program = artifacts + "/WinPlayerBuildProgram"
        self.paths = Paths()
        self.paths.add(saved + "/" + program + "/il2cppOutput/cpp", self.cpp)
        self.paths.add(saved + "/" + program + "/il2cppOutput/data", self.data)
        self.paths.add(saved + "/" + program + "/ManagedStripped", self.stripped)
        self.paths.add(saved + "/" + program, self.work + "/build")
        self.paths.add(saved + "/" + artifacts + "/UnityLinkerInputs", self.inputs + "/project/" + artifacts + "/UnityLinkerInputs")
        self.paths.add(saved + "/" + artifacts, self.work + "/artifacts")
        self.paths.add(saved + "/Temp/StagingArea/Data/Managed", self.managed)
        self.paths.add(self.info["il2cpp"], self.il2cpp)
        self.paths.add(saved + "/Library", self.inputs + "/project/Library")
        self.paths.add(program + "/ManagedStripped", self.stripped, relative=True)
        self.paths.add(program, self.work + "/build", relative=True)
        self.paths.add(artifacts, self.work + "/artifacts", relative=True)
        # What must not be left after the move: anything of the project's Library or Temp.
        self.leftover = re.compile(
            r"(?<![\w\\/:.\-])(Library|Temp)[\\/]|" + r"[\\/]".join(re.escape(part) for part in saved.split("/")) + r"[\\/](Library|Temp)(?![\w.\-])",
            re.IGNORECASE)

    # ---- small helpers

    def moved(self, text):
        text = self.paths.move(text)
        left = self.leftover.search(text)
        if left:
            fail("a path of the Editor build is left in a command: ..." + text[max(0, left.start() - 120):left.end() + 120] + "...")
        return text

    def timed(self, name, action):
        start = time.time()
        result = action()
        self.times.append((name, time.time() - start, result or "done"))
        say("[%s] %s, %.0f s" % (name, result or "done", time.time() - start))

    def remove_tree(self, path):
        """Removes a folder this tool made in its own work folder."""
        path = os.path.abspath(path).replace("\\", "/")
        if not os.path.isfile(self.work + "/" + WORK_MARKER) or not path.startswith(self.work + "/"):
            fail("not removing " + path)
        if os.path.isdir(path):
            shutil.rmtree(path)

    def stamp(self, stage, key, outputs):
        """True when the stage ran with this key the last time and what it made is still there. Otherwise the stage runs now, and its old stamp goes first: a run that fails must not leave it."""
        path = self.work + "/stamps/" + stage + ".json"
        if (read_json(path) or {}).get("key") == key and all(os.path.exists(output) for output in outputs):
            return True
        try:
            os.remove(path)
        except OSError:
            pass
        return False

    def set_stamp(self, stage, key):
        write_json(self.work + "/stamps/" + stage + ".json", {"key": key, "written": datetime.datetime.now().isoformat(timespec="seconds")})

    def run(self, command, log):
        """Runs one tool with the work folder as the current folder, its output going to a log. Returns the exit code."""
        os.makedirs(os.path.dirname(log), exist_ok=True)
        with open(log, "wb") as file:
            file.write((command + "\n\n").encode("utf-8"))
            file.flush()
            return subprocess.run(command, cwd=self.work, env=self.environment, stdout=file, stderr=subprocess.STDOUT).returncode

    def signature(self, path):
        """What a file a compilation read looks like now: its content, or its time and size for the installed tools and the precompiled headers."""
        key = os.path.normcase(os.path.abspath(path))
        with self.signature_lock:
            known = self.signatures.get(key)
        if known:
            return known
        try:
            status = os.stat(key)
        except OSError:
            return "missing"
        if key.endswith(".pch"):
            # A precompiled header is written again whenever anything it holds changed, and that can be during this build: not remembered.
            return "%d-%d" % (status.st_mtime_ns, status.st_size)
        installed = key.startswith(os.path.normcase("C:\\Program Files"))
        value = "%d-%d" % (status.st_mtime_ns, status.st_size) if installed else file_digest(key)
        with self.signature_lock:
            self.signatures[key] = value
        return value

    # ---- the stages before the compiler

    def weave(self):
        steps = self.options.weave
        if self.options.weaver:
            hosts = [os.path.abspath(self.options.weaver).replace("\\", "/")]
        else:
            hosts = [WEAVER + "/" + folder + "/WeaverHost.exe" for folder in (os.listdir(WEAVER) if os.path.isdir(WEAVER) else [])]
        hosts = [path for path in hosts if os.path.isfile(path)]
        if len(hosts) != 1:
            fail("WeaverHost is not built: dotnet build -c Release in tools/il2cpp-generic-memory/WeaverHost, or give --weaver <WeaverHost.exe>")
        key = digest(self.info["saved"], steps, file_digest(hosts[0][:-4] + ".dll"))
        self.managed_key = key
        if self.stamp("managed", key, [self.managed]):
            return "as before"
        self.remove_tree(self.managed)
        shutil.copytree(self.inputs + "/Managed", self.managed)
        code = self.run('"%s" "%s" %s' % (hosts[0], self.managed, steps), self.work + "/logs/weave.log")
        if code != 0:
            fail("WeaverHost ended with %d, see %s" % (code, self.work + "/logs/weave.log"))
        self.set_stamp("managed", key)
        return steps

    def link(self):
        node = self.graph.one("UnityLinker")
        response_path, response = self.graph.response(node)
        response = self.moved(response)
        command = self.moved(node["Action"])
        key = digest(self.managed_key, command, response)
        self.linker_key = key
        if self.path_length_differs:
            self.notes.append("the path of the work folder is too long to give the linked assemblies a path as long as the Editor's: "
                              "some types can get other numbers in the generated C++ than in the Editor's build (the code is the same)")
        if self.stamp("linker", key, [self.stripped]):
            return "as before"
        self.remove_tree(self.stripped)
        write_if_changed(self.moved(response_path), response.encode("utf-8"))
        code = self.run(command, self.work + "/logs/UnityLinker.log")
        if code != 0:
            fail("UnityLinker ended with %d, see %s" % (code, self.work + "/logs/UnityLinker.log"))
        self.set_stamp("linker", key)
        return "%d assemblies left" % len([name for name in os.listdir(self.stripped) if name.endswith(".dll")])

    def convert(self):
        node = self.graph.one("IL2CPP_CodeGen")
        response_path, response = self.graph.response(node)
        response = self.moved(response)
        # The assemblies are the ones the linker left this time, in the order of the saved build, new ones last.
        saved = [os.path.basename(path) for path in re.findall(r'--assembly="([^"]+)"', response)]
        present = sorted(name for name in os.listdir(self.stripped) if name.endswith(".dll"))
        names = [name for name in saved if name in present] + [name for name in present if name not in saved]
        if names != saved:
            self.notes.append("assemblies to il2cpp differ from the saved build: gone %s, new %s" % (
                [name for name in saved if name not in present], [name for name in present if name not in saved]))
        response = re.sub(r'--assembly="[^"]+"\s*', "", response)
        response = response.replace("--convert-to-cpp", "--convert-to-cpp " + " ".join('--assembly="%s/%s"' % (self.stripped, name) for name in names), 1)
        for argument in self.options.il2cpp_drop:
            dropped, count = re.subn(r"(?<!\S)" + re.escape(argument) + r"(?!\S)\s*", "", response)
            if count != 1:
                fail("the saved il2cpp arguments have %d of %s" % (count, argument))
            response = dropped
        for argument in self.options.il2cpp_arg:
            response += " " + argument
        command = self.moved(node["Action"])
        key = digest(self.linker_key, command, response)
        if self.stamp("convert", key, [self.cpp, self.data + "/Metadata/global-metadata.dat"]):
            return "as before"
        self.remove_tree(self.work + "/il2cppOutput")
        write_if_changed(self.moved(response_path), response.encode("utf-8"))
        code = self.run(command, self.work + "/logs/il2cpp.log")
        if code != 0:
            fail("il2cpp ended with %d, see %s" % (code, self.work + "/logs/il2cpp.log"))
        self.set_stamp("convert", key)
        return "%d files" % len(os.listdir(self.cpp))

    # ---- the compiler

    def parse(self, node):
        """One compiler node of the saved graph: its command with the paths moved, and the tokens that name its source and object."""
        command = self.moved(node["Action"])
        tokens = TOKEN.findall(command)
        sources = [token for token in tokens[1:] if not token.startswith("/") and token.strip('"').lower().endswith((".c", ".cpp"))]
        objects = [token for token in tokens if token.startswith("/Fo")]
        if len(sources) != 1 or len(objects) != 1:
            fail("could not read the compiler command: " + command[:400])
        made = [token[3:].strip('"').replace("\\", "/") for token in tokens if token.startswith("/Fp")]
        creates = any(token.startswith("/Yc") for token in tokens)
        return {
            "command": command,
            "header": made[0] if made and creates else None,
            "source_token": sources[0],
            "object_token": objects[0],
            "source": sources[0].strip('"').replace("\\", "/"),
            "object": objects[0][3:].strip('"').replace("\\", "/"),
            "cpp": "/TP" in tokens,
            "pch": creates,
        }

    def job(self, parsed, source=None, object_path=None, extra="", tuned=True):
        command = parsed["command"]
        if tuned:
            # The compilations that get the defines are the ones whose flags may change: libil2cpp, the precompiled headers, the generated C++.
            for flag in self.options.cl_drop:
                command, count = re.subn(r"(?<!\S)" + re.escape(flag) + r"(?!\S)\s*", "", command)
                if count != 1:
                    fail("a compiler command has %d of %s" % (count, flag))
        if source:
            command = command.replace(parsed["source_token"], '"%s"' % source.replace("/", "\\"))
            command = command.replace(parsed["object_token"], '/Fo"%s"' % object_path.replace("/", "\\"))
        object_path = object_path or parsed["object"]
        command += extra + ' /sourceDependencies "%s"' % (object_path + ".deps.json").replace("/", "\\")
        return {"command": command, "source": source or parsed["source"], "object": object_path, "header": None if source else parsed["header"]}

    def plan(self):
        """Every compilation of this build, from the saved graph: what is compiled first, what after the precompiled headers, and the objects by their source."""
        defines = "".join(" /D" + define for define in self.options.define) + "".join(" " + flag for flag in self.options.cl_add)
        runtime = self.il2cpp.lower() + "/libil2cpp/"
        build = (self.work + "/build/").lower()
        cpp_folder = self.cpp.lower() + "/"
        first, generated, templates, runtime_templates = [], {}, {}, {}
        lumps = {}
        gone = []
        for node in self.graph.nodes:
            kind = self.graph.kind(node)
            if kind == "MakeLump":
                path = self.moved(node["Outputs"][0])
                text = self.moved(self.graph.written(node["Outputs"][0]).decode("utf-8"))
                lines = []
                for line in text.splitlines():
                    included = re.match(r'#include "(.+)"', line)
                    if included and included.group(1).lower().startswith(runtime) and not os.path.isfile(included.group(1)):
                        gone.append(included.group(1))
                        continue
                    lines.append(line)
                lumps[path.replace("\\", "/")] = ("\r\n".join(lines) + "\r\n").encode("utf-8")
            elif kind == "WriteText" and node["Outputs"][0].replace("\\", "/").startswith("Library/Bee/artifacts/WinPlayerBuildProgram/") and "/csharpactions/" not in node["Outputs"][0]:
                path = self.moved(node["Outputs"][0]).replace("\\", "/")
                if path.lower().startswith(build) and os.path.basename(path) != "app.info":
                    lumps[path] = self.moved(self.graph.written(node["Outputs"][0]).decode("utf-8")).encode("utf-8")
        for path, data in lumps.items():
            write_if_changed(path, data)

        for node in self.graph.nodes:
            kind = self.graph.kind(node)
            if not kind.startswith("C_"):
                continue
            parsed = self.parse(node)
            source = parsed["source"].lower()
            if source.startswith(cpp_folder) and not parsed["pch"]:
                generated[os.path.basename(source)] = parsed["object"]
                templates.setdefault(parsed["cpp"], parsed)
                continue
            in_runtime = source.startswith(runtime)
            if in_runtime:
                runtime_templates.setdefault(parsed["cpp"], parsed)
                if not os.path.isfile(parsed["source"]):
                    gone.append(parsed["source"])
                    continue
            uses_runtime = in_runtime or parsed["pch"] or source.startswith(build) and source.endswith(".lump.cpp")
            first.append(self.job(parsed, extra=defines if uses_runtime else "", tuned=uses_runtime))

        # Files of libil2cpp the saved build did not have are compiled on their own, with the flags of the others.
        with open(self.inputs + "/libil2cpp-sources.txt", encoding="utf-8") as file:
            known = {line.strip().lower() for line in file if line.strip()}
        added = []
        for root, _, files in os.walk(self.il2cpp + "/libil2cpp"):
            for name in sorted(files):
                relative = os.path.relpath(os.path.join(root, name), self.il2cpp + "/libil2cpp").replace("\\", "/")
                if name.endswith((".cpp", ".c")) and relative.lower() not in known:
                    template = runtime_templates[name.endswith(".cpp")]
                    object_path = os.path.dirname(template["object"]) + "/x-" + re.sub(r"\W", "-", relative) + ".obj"
                    first.append(self.job(template, self.il2cpp + "/libil2cpp/" + relative, object_path, defines))
                    added.append(object_path)
        if added:
            self.notes.append("%d files of libil2cpp are new and compiled on their own" % len(added))
        if gone:
            self.notes.append("%d files of the saved build's libil2cpp are gone and left out: %s" % (len(gone), ", ".join(os.path.basename(path) for path in gone[:5])))

        # The Editor's build gives the generated files to the linker as the folder lists them, the .cpp files and then the
        # .c files: by name without regard to case, as the upper-case letters sort. The same order here, whatever files there are now.
        second = []
        new_objects = []
        names = [name for name in os.listdir(self.cpp) if name.endswith((".cpp", ".c")) and os.path.isfile(self.cpp + "/" + name)]
        for name in sorted(names, key=lambda name: (name.endswith(".c"), name.upper())):
            template = templates[name.endswith(".cpp")]
            object_path = generated.get(name.lower())
            if not object_path:
                object_path = os.path.dirname(template["object"]) + "/x-" + re.sub(r"\W", "-", name) + ".obj"
                new_objects.append(object_path)
            second.append(self.job(template, self.cpp + "/" + name, object_path, defines))
        saved_objects = {path.lower() for path in generated.values()}
        dropped = saved_objects - {job["object"].lower() for job in second}
        if new_objects or dropped:
            self.notes.append("generated C++ differs from the saved build in its files: %d new, %d gone" % (len(new_objects), len(dropped)))
        return first, second, {"runtime_added": added, "generated_saved": saved_objects}

    def compile_one(self, job):
        """Compiles one file unless its object is up to date. Returns (ran, error text)."""
        state_path = job["object"] + ".state.json"
        state = read_json(state_path)
        made = [job["object"]] + ([job["header"]] if job["header"] else [])
        if state and state.get("command") == job["command"] and all(os.path.isfile(path) for path in made):
            if all(self.signature(path) == value for path, value in state["deps"].items()):
                return False, None
        os.makedirs(os.path.dirname(job["object"]), exist_ok=True)
        try:
            os.remove(state_path)
        except OSError:
            pass
        done = subprocess.run(job["command"], cwd=self.work, env=self.environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if done.returncode != 0:
            return True, "%s\n%s" % (job["source"], done.stdout.decode("utf-8", "replace")[-6000:])
        data = (read_json(job["object"] + ".deps.json") or {}).get("Data")
        if not data:
            return True, job["source"] + ": the compiler wrote no list of what it read"
        read = [data["Source"]] + data.get("Includes", []) + ([data["PCH"]] if data.get("PCH") else [])
        write_json(state_path, {"command": job["command"], "deps": {path: self.signature(path) for path in read}})
        return True, None

    def compile_all(self, jobs, name):
        ran = 0
        errors = []
        started = time.time()
        with concurrent.futures.ThreadPoolExecutor(max_workers=self.options.jobs) as pool:
            futures = [pool.submit(self.compile_one, job) for job in jobs]
            reported = time.time()
            for count, future in enumerate(concurrent.futures.as_completed(futures), 1):
                compiled, error = future.result()
                ran += compiled
                if error:
                    errors.append(error)
                    for other in futures:
                        other.cancel()
                    break
                if time.time() - reported > 30:
                    reported = time.time()
                    say("  %s: %d of %d, %d compiled, %.0f s" % (name, count, len(jobs), ran, time.time() - started))
        if errors:
            fail("the compiler failed:\n" + "\n".join(errors))
        return ran

    def tool(self, node, objects, outputs, name):
        """Runs lib or link with the saved response file, the objects replaced by the ones of this build, unless nothing it reads changed."""
        response_path, response = self.graph.response(node)
        lines = [line for line in self.moved(response).splitlines() if line.strip()]
        kept = [line for line in lines if not line.strip('"').lower().endswith(".obj")]
        first_object = next(index for index, line in enumerate(lines) if line.strip('"').lower().endswith(".obj"))
        more = self.options.link_add if name == "link" else self.options.lib_add
        text = "\n".join(kept[:first_object] + list(more) + ['"%s"' % path for path in objects] + kept[first_object:]) + "\n"
        command = self.moved(node["Action"])
        reads = objects + [line.strip('"') for line in kept if line.strip('"').lower().endswith(".lib") and os.path.isabs(line.strip('"'))]
        key = digest(command, text, *["%s %d %d" % (path, os.stat(path).st_mtime_ns, os.stat(path).st_size) for path in reads])
        state_path = outputs[0] + ".state.json"
        if (read_json(state_path) or {}).get("key") == key and all(os.path.isfile(path) for path in outputs):
            return False
        try:
            os.remove(state_path)
        except OSError:
            pass
        write_if_changed(self.moved(response_path), ("\ufeff" + text).encode("utf-8"))
        code = self.run(command, self.work + "/logs/" + name + ".log")
        if code != 0:
            fail("%s ended with %d, see %s" % (name, code, self.work + "/logs/" + name + ".log"))
        write_json(state_path, {"key": key})
        return True

    def objects_of(self, node, replaced=(), added=()):
        """The objects of a saved response file in its order, without the replaced ones and the ones that are gone, then the added ones."""
        _, response = self.graph.response(node)
        objects = [line.strip('"').replace("\\", "/") for line in self.moved(response).splitlines() if line.strip('"').lower().endswith(".obj")]
        objects = [path for path in objects if path.lower() not in replaced and os.path.isfile(path)]
        return objects + list(added)

    def compile(self):
        link_node = self.graph.one("Link_")
        lib_node = self.graph.one("Lib_")
        compiler = next(node for node in self.graph.nodes if self.graph.kind(node).startswith("C_"))
        variables = {item["Key"]: item["Value"] for item in compiler.get("Env") or []}
        self.environment = dict(os.environ)
        for name in ("INCLUDE", "LIB", "LIBPATH", "CL", "_CL_", "LINK", "_LINK_"):
            self.environment.pop(name, None)
        system = os.environ.get("SystemRoot", "C:\\Windows")
        self.environment["PATH"] = variables.get("PATH", "") + ";" + system + "\\System32;" + system
        # The service that writes .pdb files is shared by endpoint: one for each work folder.
        self.environment["_MSPDBSRV_ENDPOINT_"] = str(int(digest(self.work)[:15], 16))
        os.makedirs(self.work + "/tmp", exist_ok=True)
        self.environment["TMP"] = self.environment["TEMP"] = (self.work + "/tmp").replace("/", "\\")

        first, second, changes = self.plan()
        ran = self.compile_all(first, "libil2cpp and the precompiled headers")
        ran += self.compile_all(second, "generated C++")

        library = self.moved(lib_node["Outputs"][0]).replace("\\", "/")
        # Only what this build compiles goes into the library: the object of a source that was taken away may still be in the work folder.
        planned = {job["object"].lower() for job in first}
        members = [path for path in self.objects_of(lib_node) if path.lower() in planned]
        self.tool(lib_node, members + changes["runtime_added"], [library], "lib")
        outputs = [self.moved(path).replace("\\", "/") for path in link_node["Outputs"]]
        self.assembly = outputs[0]
        linked = self.tool(link_node, self.objects_of(link_node, changes["generated_saved"], [job["object"] for job in second]), outputs, "link")
        if linked:
            server = os.path.dirname(TOKEN.findall(link_node["Action"])[0].strip('"')) + "/mspdbsrv.exe"
            subprocess.run('"%s" -endpoint %s -stop' % (server, self.environment["_MSPDBSRV_ENDPOINT_"]), cwd=self.work, env=self.environment,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

        # Which libil2cpp the compiler really read: the folders of every header and source below a libil2cpp.
        roots = set()
        for job in first + second:
            for path in (read_json(job["object"] + ".state.json") or {"deps": {}})["deps"]:
                found = re.match(r"(.*)[\\/]libil2cpp[\\/]", path, re.IGNORECASE)
                if found:
                    roots.add(found.group(1).replace("\\", "/").lower())
        self.runtime_roots = sorted(roots)
        if self.runtime_roots != [self.il2cpp.lower()]:
            fail("libil2cpp was read from %s, not only from %s" % (self.runtime_roots, self.il2cpp))
        return "%d of %d compiled, %s" % (ran, len(first) + len(second), "linked" if linked else "link as before")

    # ---- the Player

    def deploy(self):
        # The saved Player is copied when the label is new, and again when the label was built from other inputs before:
        # its data, its list of assemblies and its Burst library belong to the build the inputs were saved from.
        template = [self.inputs, self.info["saved"]]
        if (read_json(self.player + "/" + MARKER) or {}).get("template") != template:
            for root, _, files in os.walk(self.inputs + "/player"):
                for name in files:
                    to = os.path.join(self.player, os.path.relpath(os.path.join(root, name), self.inputs + "/player"))
                    os.makedirs(os.path.dirname(to), exist_ok=True)
                    shutil.copy2(os.path.join(root, name), to)
        shutil.copy2(self.assembly, self.player + "/GameAssembly.dll")
        target = self.player + "/" + self.info["dataFolder"] + "/il2cpp_data"
        count = 0
        for root, _, files in os.walk(self.data):
            for name in files:
                to = os.path.join(target, os.path.relpath(os.path.join(root, name), self.data))
                os.makedirs(os.path.dirname(to), exist_ok=True)
                shutil.copy2(os.path.join(root, name), to)
                count += 1
        write_json(self.player + "/" + MARKER, {
            "built": datetime.datetime.now().isoformat(timespec="seconds"),
            "template": template,
            "inputs": self.inputs,
            "il2cpp": self.il2cpp,
            "work": self.work,
            "generatedCpp": self.cpp,
            "symbols": os.path.splitext(self.assembly)[0] + ".pdb",
            "weave": self.options.weave,
            "defines": self.options.define,
            "il2cppArguments": self.options.il2cpp_arg,
            "il2cppDropped": self.options.il2cpp_drop,
            "compilerDropped": self.options.cl_drop,
            "compilerAdded": self.options.cl_add,
            "linkerAdded": self.options.link_add,
            "libAdded": self.options.lib_add,
            "assemblyBytes": os.path.getsize(self.assembly),
            "seconds": {name: round(seconds, 1) for name, seconds, _ in self.times},
            "notes": self.notes,
        })
        return "%s, %d data files" % (self.player, count)

    def main(self):
        os.makedirs(self.work, exist_ok=True)
        lock = self.work + "/build.lock"
        try:
            handle = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        except FileExistsError:
            fail("%s is there: another build uses this work folder (when none does, delete the file)" % lock)
        try:
            os.write(handle, ("%d %s\n" % (os.getpid(), datetime.datetime.now().isoformat(timespec="seconds"))).encode("utf-8"))
            os.close(handle)
            write_json(self.work + "/" + WORK_MARKER, {"inputs": self.inputs, "il2cpp": self.il2cpp, "label": self.options.label})
            self.environment = dict(os.environ)
            started = time.time()
            stages = [("compile", self.compile), ("player", self.deploy)]
            if not self.options.cpp_from:
                self.managed_key = self.info["saved"]
                stages = [("linker", self.link), ("convert", self.convert)] + stages
                if self.options.weave:
                    stages = [("managed", self.weave)] + stages
            for name, action in stages:
                self.timed(name, action)
                if self.options.stop_after == name:
                    break
            for note in self.notes:
                say("note: " + note)
            say("total %.0f s" % (time.time() - started))
        finally:
            os.remove(lock)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--inputs", required=True)
    parser.add_argument("--il2cpp", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--label", required=True)
    parser.add_argument("--weave")
    parser.add_argument("--weaver")
    parser.add_argument("--cpp-from")
    parser.add_argument("--define", action="append", default=[])
    parser.add_argument("--il2cpp-arg", action="append", default=[])
    parser.add_argument("--il2cpp-drop", action="append", default=[])
    parser.add_argument("--cl-drop", action="append", default=[])
    parser.add_argument("--cl-add", action="append", default=[])
    parser.add_argument("--link-add", action="append", default=[])
    parser.add_argument("--lib-add", action="append", default=[])
    parser.add_argument("--jobs", type=int, default=os.cpu_count())
    parser.add_argument("--stop-after", choices=["managed", "linker", "convert", "compile"])
    Build(parser.parse_args()).main()


main()
