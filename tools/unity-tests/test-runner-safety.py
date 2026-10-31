#!/usr/bin/env python3
"""Exercise real shell runners with fake CLI/Editor processes (never launches Unity)."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

SOURCE = Path(__file__).resolve().parent
ROOT = SOURCE.parent.parent
SHELL = os.environ.get("DEXC_TEST_SHELL") or shutil.which("sh")
if not SHELL and os.name == "nt":
    SHELL = "C:/Program Files/Git/bin/bash.exe"


class RunnerSafetyTests(unittest.TestCase):
    def setUp(self):
        logs = ROOT / "Logs/UnityTests"
        logs.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix="runner safety ", dir=logs)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.assertTrue(self.root.is_relative_to(logs.resolve()))
        self.scripts = self.root / "tools/unity-tests"
        shutil.copytree(SOURCE, self.scripts, ignore=shutil.ignore_patterns("__pycache__"))
        settings = self.root / "ProjectSettings"
        settings.mkdir()
        (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.20f1\n")
        self.settings = settings / "ProjectSettings.asset"
        self.original = "  scriptingDefineSymbols:\n    Standalone: USER_STANDALONE\n    Android: USER_ANDROID\n  additionalCompilerArguments: {}\n"
        self.settings.write_text(self.original)
        self.cli = self.script("fake cli", '''case "$1" in
editors) printf '%s\\n' "$FAKE_STATUS"; exit "$FAKE_STATUS_EXIT" ;;
test)
    while [ "$#" -gt 0 ]; do
        if [ "$1" = --output ]; then report=$2; fi
        shift
    done
    printf '<test-run result="Passed" total="1" passed="1" failed="0"/>' > "$report"
    exit "${FAKE_TEST_EXIT:-0}" ;;
*) exit 91 ;;
esac
''')
        self.python = self.script("python tools/python runner", 'exec "' + Path(sys.executable).as_posix() + '" "$@"\n')
        self.editor = self.script("fake editor", '''if [ "$DEXC_SYMBOLS_STANDALONE" = USER_STANDALONE ]; then
    printf 'restore\\n' >> "$FAKE_TRACE"
    if [ "$FAKE_RESTORE" = fail ]; then exit 23; fi
    if [ "$FAKE_RESTORE" = mismatch ]; then exit 0; fi
    if [ "$FAKE_RESTORE" = unreadable ]; then printf 'invalid settings' > "$FAKE_SETTINGS"; exit 0; fi
fi
printf '  scriptingDefineSymbols:\\n    Standalone: %s\\n    Android: %s\\n  additionalCompilerArguments: {}\\n' "$DEXC_SYMBOLS_STANDALONE" "$DEXC_SYMBOLS_ANDROID" > "$FAKE_SETTINGS"
''')
        self.env = {key: value for key, value in os.environ.items()
                    if not key.startswith(("DEXC_", "UNITY_", "PYTHON_BIN"))}
        self.env.update(UNITY_CLI=self.cli.as_posix(), UNITY_EDITOR=self.editor.as_posix(),
                        PYTHON_BIN=self.python.as_posix(), DEXC_PROJECT_PATH=self.root.as_posix(),
                        FAKE_STATUS=json.dumps({"success": True, "data": {"count": 0, "instances": []}}),
                        FAKE_STATUS_EXIT="0", FAKE_RESTORE="ok", FAKE_SETTINGS=self.settings.as_posix(),
                        FAKE_TRACE=(self.root / "trace").as_posix())

    def script(self, name, body):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("#!/bin/sh\n" + body, newline="\n")
        path.chmod(0o755)
        return path

    def run_shell(self, *args):
        return subprocess.run([SHELL, *args], cwd=self.root, env=self.env,
                              capture_output=True, text=True, timeout=30)

    def guard(self, name):
        return self.run_shell("-c", '. "$1"; ' + name + ' "$2" "$3"', "guard",
                              (self.scripts / "common.sh").as_posix(), self.root.as_posix(), self.cli.as_posix())

    def test_closed_and_open(self):
        self.assertEqual(self.guard("require_editor_closed").returncode, 0)
        self.assertNotEqual(self.guard("require_editor_open").returncode, 0)
        self.env["FAKE_STATUS"] = json.dumps({"success": True, "data": {"count": 1, "instances": [{"projectPath": self.root.as_posix()}]}})
        self.assertNotEqual(self.guard("require_editor_closed").returncode, 0)
        self.assertEqual(self.guard("require_editor_open").returncode, 0)

    def test_other_project_does_not_block_target(self):
        self.env["FAKE_STATUS"] = json.dumps({"success": True, "data": {"count": 1,
            "instances": [{"projectPath": (self.root / "other project").as_posix()}]}})
        self.assertEqual(self.guard("require_editor_closed").returncode, 0)

    def test_missing_instance_path_or_count_mismatch_is_unknown(self):
        for data in [{"count": 1, "instances": [{}]}, {"count": 0, "instances": [{}]},
                     {"count": 0}, {"count": 1, "instances": [{"projectPath": "relative"}]}]:
            with self.subTest(data=data):
                self.env["FAKE_STATUS"] = json.dumps({"success": True, "data": data})
                self.assertEqual(self.guard("unity_editor_open").returncode, 2)
                self.assertNotEqual(self.guard("require_editor_closed").returncode, 0)

    def test_unknown_never_allows_batch_or_interactive(self):
        for payload in ["", "bad json", "null", "[]", '{"success":false}',
                        '{"success":true,"data":{}}', '{"success":true,"data":null}',
                        '{"success":1,"data":{"count":0}}',
                        *[json.dumps({"success": True, "data": {"count": value}})
                          for value in [-1, 0.5, False, "0"]]]:
            with self.subTest(payload=payload):
                self.env["FAKE_STATUS"] = payload
                self.assertEqual(self.guard("unity_editor_open").returncode, 2)
                self.assertNotEqual(self.guard("require_editor_closed").returncode, 0)
                self.assertNotEqual(self.guard("require_editor_open").returncode, 0)

    def test_cli_failure_and_python_failure_are_unknown(self):
        self.env["FAKE_STATUS_EXIT"] = "17"
        self.assertEqual(self.guard("unity_editor_open").returncode, 2)
        self.assertNotEqual(self.guard("require_editor_closed").returncode, 0)
        self.env["FAKE_STATUS_EXIT"] = "0"
        self.env["PYTHON_BIN"] = str(self.root / "missing python")
        self.assertEqual(self.guard("unity_editor_open").returncode, 2)
        self.assertNotEqual(self.guard("require_editor_closed").returncode, 0)

    def test_python_path_with_spaces_runs_real_parser(self):
        result = self.run_shell(str(self.scripts / "run-tests.sh"))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('"total": 1', result.stdout)
        self.env["FAKE_TEST_EXIT"] = "7"
        self.assertNotEqual(self.run_shell(str(self.scripts / "run-tests.sh")).returncode, 0)

    def test_unknown_prevents_runner_launch(self):
        self.env["FAKE_STATUS"] = '{"success":false}'
        result = self.run_shell(str(self.scripts / "run-tests.sh"))
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(list(self.root.glob("Logs/UnityTests/*.xml")))

    def matrix(self, row_exit=0, last_row_leaves=None):
        # The matrix writes the define lines itself rather than through the Editor, so a broken restore is staged by
        # having the last row - conflict, whose EditMode run is the final step before restoring - leave the settings
        # in a state the restore cannot put right.
        for name in ["check-compilation.sh", "run-editmode-tests.sh", "run-playmode-tests.sh"]:
            body = f"#!/bin/sh\nexit {row_exit}\n"
            if name == "run-editmode-tests.sh" and last_row_leaves is not None:
                self.env["FAKE_LEFTOVER"] = last_row_leaves
                body = ('#!/bin/sh\ncase "$DEXC_TEST_STAMP" in *-conflict-editmode) '
                        'printf "%s" "$FAKE_LEFTOVER" > "$FAKE_SETTINGS" ;; esac\n' + f"exit {row_exit}\n")
            (self.scripts / name).write_text(body, newline="\n")
        return self.run_shell(str(self.scripts / "run-symbol-matrix.sh"))

    def test_matrix_restores_preexisting_settings(self):
        result = self.matrix()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.settings.read_text(), self.original)
        # Put back without the Editor: it will not start while scripts fail to compile, which is when it matters.
        self.assertFalse((self.root / "trace").exists())

    def test_matrix_failure_still_restores(self):
        self.assertNotEqual(self.matrix(row_exit=7).returncode, 0)
        self.assertEqual(self.settings.read_text(), self.original)

    def test_matrix_restore_failure_is_failure(self):
        # No define block left at all: the restore has nothing to write into.
        self.assertNotEqual(self.matrix(last_row_leaves="  somethingElse: 1\n").returncode, 0)

    def test_matrix_restore_success_with_wrong_values_is_failure(self):
        # The block is there but the per-platform lines are gone, so writing them "succeeds" and changes nothing.
        leftover = "  scriptingDefineSymbols:\n  additionalCompilerArguments: {}\n"
        self.assertNotEqual(self.matrix(last_row_leaves=leftover).returncode, 0)

    def test_unreadable_settings_do_not_pass_as_empty_symbols(self):
        # Defines written as {} have no per-platform lines to replace. Every row would run with no symbols and pass;
        # the matrix has to refuse instead, and leave the settings as they were.
        self.settings.write_text("  scriptingDefineSymbols: {}\n")
        self.original = self.settings.read_text()
        self.assertNotEqual(self.matrix().returncode, 0)
        self.assertEqual(self.settings.read_text(), self.original)

    def test_unreadable_initial_settings_prevent_matrix_launch(self):
        self.settings.write_text("invalid settings")
        self.assertNotEqual(self.matrix().returncode, 0)
        self.assertFalse((self.root / "trace").exists())

    def test_serialized_snapshot_restores_exact_bytes_and_refuses_overwrite(self):
        backup = self.root / "before.asset"
        result = self.run_shell("-c", '. "$1"; snapshot_project_settings "$2" "$3"', "snapshot",
                                str(self.scripts / "common.sh"), self.root.as_posix(), backup.as_posix())
        self.assertEqual(result.returncode, 0, result.stderr)
        self.settings.write_text("changed including default keys")
        result = self.run_shell("-c", '. "$1"; snapshot_project_settings "$2" "$3"', "snapshot",
                                str(self.scripts / "common.sh"), self.root.as_posix(), backup.as_posix())
        self.assertNotEqual(result.returncode, 0)
        result = self.restore_snapshot(backup)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.settings.read_bytes(), backup.read_bytes())
        self.assertEqual(self.settings.read_text(), self.original)

    def restore_snapshot(self, backup, before=""):
        return self.run_shell("-c", '. "$1"; ' + before + 'restore_project_settings "$2" "$3" "$4"',
                              "restore", str(self.scripts / "common.sh"), self.root.as_posix(),
                              self.cli.as_posix(), backup.as_posix())

    def test_snapshot_copy_failure_comparison_failure_and_open_editor_are_rejected(self):
        backup = self.root / "before.asset"
        backup.write_text(self.original)
        self.assertNotEqual(self.restore_snapshot(backup, 'cp() { return 19; }; ').returncode, 0)
        self.assertNotEqual(self.restore_snapshot(backup, 'cmp() { return 19; }; ').returncode, 0)
        self.settings.write_text("in use")
        self.env["FAKE_STATUS"] = json.dumps({"success": True, "data": {"count": 1,
            "instances": [{"projectPath": self.root.as_posix()}]}})
        self.assertNotEqual(self.restore_snapshot(backup).returncode, 0)
        self.assertEqual(self.settings.read_text(), "in use")

    def test_build_runners_restore_snapshot_after_editor_success_or_failure(self):
        self.env["UNITY_EDITOR"] = self.script("fake build editor", '''printf 'build\\n' >> "$FAKE_TRACE"
printf 'modified by build including omitted defaults' > "$FAKE_SETTINGS"
exit "$FAKE_BUILD_EXIT"
''').as_posix()
        for runner in ["run-stripping-player.sh", "run-remote-player.sh"]:
            for exit_code in [0, 5]:
                with self.subTest(runner=runner, editor_exit=exit_code):
                    self.env["DEXC_TEST_STAMP"] = f"{runner}-{exit_code}"
                    self.env["FAKE_BUILD_EXIT"] = str(exit_code)
                    result = self.run_shell(str(self.scripts / runner))
                    # Fake builds intentionally create no Player/report: no real Player can launch.
                    self.assertNotEqual(result.returncode, 0)
                    self.assertEqual(self.settings.read_text(), self.original, result.stdout + result.stderr)
        # Per runner: a successful Editor applies the symbols and then builds (2 launches); a failing one stops at the
        # symbols (1 launch). Two runners, both outcomes.
        self.assertEqual(len((self.root / "trace").read_text().splitlines()), 6)


if __name__ == "__main__":
    unittest.main()
