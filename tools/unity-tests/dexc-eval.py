"""Compiles a C# snippet against what the running Player really contains, and runs it there.

usage: dexc-eval.py <snippet>
       dexc-eval.py --file <path>     one snippet per block, blocks separated by a line holding only ---

A snippet with no semicolon is an expression. The references are the Player's own stripped assemblies, so a member the
build does not contain is a compile error here rather than a failure there, and they are publicized, so private members
compile. Everything slow is done once: run-autopilot-player.sh refs writes the references, and the compiler server stays
up between calls.
"""
import base64
import json
import os
import pathlib
import socket
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
WORK = ROOT / "Logs" / "UnityTests" / "Autopilot"
GUARD = ROOT / "Autopilot" / "Tools~" / "Guard"
ANALYZER = GUARD / "Analyzer" / "bin" / "Release" / "net10.0" / "Dexc.Guard.Analyzer.dll"
ILRUNTIME_RULES = GUARD / "ilruntime.globalconfig"
PATCH = GUARD / "Patch" / "bin" / "Release" / "net10.0" / "Dexc.Guard.Patch.exe"
SOURCE = """using System.Collections.Generic;
using System.Threading.Tasks;
using DexcDebugger.Samples.StealthStageDemo;
using DexcDebugger.Samples.StealthStageDemo.Autopilot;
using UnityEngine;

{types}

public static class DexcEvalSnippet
{{
	public static object Eval()
	{{
		{body}
	}}
}}
"""


def compile_snippet(snippet, compiler):
    # Text before a line holding only @@ goes outside the method, for declaring types.
    types, _, code = snippet.rpartition("\n@@\n")
    body = code + "\nreturn null;" if ";" in code else "return (object)(" + code + ");"
    directory = WORK / "eval"
    directory.mkdir(exist_ok=True)
    source = directory / "Eval.cs"
    source.write_text(SOURCE.format(types=types, body=body), encoding="utf-8")
    assembly = directory / "Eval.dll"
    command = ["dotnet", compiler, "@" + str(WORK / "refs.rsp"), "-shared:dexceval", "-preferreduilang:en", "-out:" + str(assembly)]
    # Every VM gets the analyzer: ILRuntime only warns about blocking waits, Pipeline refuses all it cannot run.
    # Set DEXC_EVAL_GUARD=0 to see what the Pipeline interpreter does without its guardrails.
    guarded = pipeline_vm() and os.environ.get("DEXC_EVAL_GUARD") != "0"
    if guarded or not pipeline_vm():
        command.append("-analyzer:" + str(ANALYZER))
    if not pipeline_vm():
        command.append("-analyzerconfig:" + str(ILRUNTIME_RULES))
    result = subprocess.run(command + [str(source)], capture_output=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        errors = [line.split("Eval.cs", 1)[-1] for line in result.stdout.splitlines() if "error" in line]
        return None, errors
    if guarded:
        patched = subprocess.run([str(PATCH), str(assembly), str(WORK / "refs")], capture_output=True, encoding="utf-8", errors="replace")
        if patched.returncode != 0:
            return None, ["patch failed: " + patched.stderr[:400]]
    return assembly.read_bytes(), None


def pipeline_vm():
    return os.environ.get("DEXC_EVAL_VM", "ilruntime") == "pipeline"


def send(assembly):
    token = (WORK / "token").read_text(encoding="utf-8").strip()
    vm = os.environ.get("DEXC_EVAL_VM", "ilruntime")
    operation = {"pipeline": "eval:pipeline", "ilruntime-keep": "eval-keep:ilruntime"}.get(vm, "eval:ilruntime")
    line = token + " " + operation + " " + base64.b64encode(assembly).decode("ascii") + "\n"
    with socket.create_connection(("127.0.0.1", 7890), timeout=30) as connection:
        connection.sendall(line.encode("utf-8"))
        with connection.makefile("r", encoding="utf-8") as reader:
            return json.loads(reader.readline())


def run(snippet, compiler):
    started = time.perf_counter()
    assembly, errors = compile_snippet(snippet, compiler)
    compiled = time.perf_counter()
    if assembly is None:
        return {"ok": False, "compile": errors, "compileMs": int((compiled - started) * 1000)}
    answer = send(assembly)
    answer["compileMs"] = int((compiled - started) * 1000)
    answer["totalMs"] = int((time.perf_counter() - started) * 1000)
    return answer


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    compiler = (WORK / "csc.txt").read_text(encoding="utf-8").strip()
    if sys.argv[1] == "--file":
        text = pathlib.Path(sys.argv[2]).read_text(encoding="utf-8")
        for block in text.split("\n---\n"):
            block = block.strip()
            if not block:
                continue
            title = block.splitlines()[0] if block.startswith("//") else block[:70]
            answer = run(block, compiler)
            keep = {k: answer[k] for k in ("ok", "value", "error", "compile", "unbound", "trace") if k in answer}
            print(title + "\n   " + json.dumps(keep, ensure_ascii=False)[:1500])
    else:
        print(json.dumps(run(sys.argv[1], compiler), ensure_ascii=False))


if __name__ == "__main__":
    main()
