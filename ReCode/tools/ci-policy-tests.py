#!/usr/bin/env python3
"""Fast CI-policy regressions; PyYAML required, no .NET/SQL/browser execution.
Exercise actual workflow filters and SQL-suite shell branches with a fake dotnet.
"""
import importlib.util
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import yaml

ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github/workflows"
SQL = yaml.load((WORKFLOWS / "io-phase02-sql-check.yml").read_text(), Loader=yaml.BaseLoader)
PLATFORM = yaml.load((WORKFLOWS / "io-cross-platform.yml").read_text(), Loader=yaml.BaseLoader)
LIGHT = yaml.load((WORKFLOWS / "io-lightweight.yml").read_text(), Loader=yaml.BaseLoader)
spec = importlib.util.spec_from_file_location("light_scope", ROOT / "ReCode/tools/ci-light-scope.py")
light_scope = importlib.util.module_from_spec(spec)
spec.loader.exec_module(light_scope)


def glob_match(pattern, path):
    # GitHub path patterns used here: *, ** and **/ (including zero directories).
    expression = ""
    i = 0
    while i < len(pattern):
        if pattern[i:i + 3] == "**/":
            expression += "(?:.*/)?"
            i += 3
        elif pattern[i:i + 2] == "**":
            expression += ".*"
            i += 2
        elif pattern[i] == "*":
            expression += "[^/]*"
            i += 1
        else:
            expression += re.escape(pattern[i])
            i += 1
    return re.fullmatch(expression, path) is not None


def triggered(workflow, paths):
    for path in paths:
        selected = False
        for pattern in workflow["on"]["push"]["paths"]:
            negative = pattern.startswith("!")
            if glob_match(pattern.lstrip("!"), path):
                selected = not negative
        if selected:
            return True
    return False


class Routing(unittest.TestCase):
    def test_documentation_does_not_launch_heavy_jobs(self):
        for path in ["README.md", "ReCode/docs/part-03/progress-evidence.json", "ReCode/docs/part-03/s10-legacy-polling.fa.md"]:
            for workflow in [SQL, PLATFORM]:
                self.assertFalse(triggered(workflow, [path]), path)
            self.assertTrue(triggered(LIGHT, [path]), path)

    def test_ui_and_preview_use_light_checks(self):
        for path in ["ReCode/src/IO.Web/Ui/device-schedules.js", "ReCode/src/IO.Web/Ui/DeviceSchedules.html", "ReCode/preview/pages-first/pages.js", "ReCode/tests/IO.Infrastructure.SqlTests/device-access-ui-check.cjs"]:
            for workflow in [SQL, PLATFORM]:
                self.assertFalse(triggered(workflow, [path]), path)
            self.assertTrue(triggered(LIGHT, [path]), path)

    def test_sensitive_or_unknown_changes_still_run_full(self):
        for path in ["ReCode/src/IO.Infrastructure/Migrations/new.cs", "ReCode/src/IO.Infrastructure/Security/DeviceAccess.cs", "ReCode/src/IO.Web/Program.cs", "ReCode/src/IO.DeviceWorker/Program.cs", "ReCode/src/IO.Web/Ui/HelpContent.json", "ReCode/tests/IO.Infrastructure.SqlTests/VirtualWorkerTests.cs", "ReCode/tools/verify-platform.ps1", "ReCode/global.json", "ReCode/Directory.Build.props", "ReCode/src/IO.Web/IO.Web.csproj", ".github/workflows/io-cross-platform.yml", "future/unclassified.file"]:
            for workflow in [SQL, PLATFORM]:
                self.assertTrue(triggered(workflow, [path]), path)

    def test_mixed_push_cannot_hide_backend_changes(self):
        for workflow in [SQL, PLATFORM]:
            self.assertTrue(triggered(workflow, ["ReCode/docs/guide.md", "ReCode/src/IO.Infrastructure/Storage/IoDbContext.cs"]))

    def test_manual_and_superseded_runs(self):
        for workflow in [SQL, PLATFORM, LIGHT]:
            self.assertIn("workflow_dispatch", workflow["on"])
            self.assertEqual(workflow["concurrency"]["cancel-in-progress"], "true")
            self.assertIn("github.workflow", workflow["concurrency"]["group"])
            self.assertIn("github.ref", workflow["concurrency"]["group"])
            self.assertEqual(workflow["permissions"], {"contents": "read"})
        self.assertEqual(SQL["on"]["workflow_dispatch"]["inputs"]["suite"]["default"], "full")

    def test_light_scope_is_selective(self):
        self.assertEqual(light_scope.scope(["ReCode/docs/progress.json"]), {"ui": False, "preview": False})
        self.assertEqual(light_scope.scope(["ReCode/src/IO.Web/Ui/Devices.html"]), {"ui": True, "preview": False})
        self.assertEqual(light_scope.scope(["ReCode/preview/pages-first/pages.js"]), {"ui": False, "preview": True})
        self.assertEqual(light_scope.scope([".github/workflows/io-lightweight.yml"]), {"ui": True, "preview": True})

    def test_push_diff_uses_before_and_after_not_only_last_commit(self):
        with patch.dict(os.environ, {"IO_CI_BASE": "a" * 40, "IO_CI_HEAD": "b" * 40}), patch.object(light_scope.subprocess, "check_output", return_value=b"ReCode/src/IO.Web/Ui/Devices.html\0ReCode/docs/guide.md\0") as run:
            self.assertEqual(len(light_scope.changed_paths()), 2)
            self.assertEqual(run.call_args.args[0], ["git", "diff", "--name-only", "-z", "a" * 40, "b" * 40, "--"])

    def test_unknown_history_falls_back_instead_of_skipping(self):
        with patch.dict(os.environ, {"IO_CI_BASE": "0" * 40, "IO_CI_HEAD": "a" * 40}):
            self.assertIsNone(light_scope.changed_paths())
        with patch.dict(os.environ, {"IO_CI_BASE": "a" * 40, "IO_CI_HEAD": "b" * 40}), patch.object(light_scope.subprocess, "check_output", side_effect=subprocess.CalledProcessError(1, "git")):
            self.assertIsNone(light_scope.changed_paths())


class SqlModes(unittest.TestCase):
    def run_suite(self, suite, fail=""):
        step = next(step for step in SQL["jobs"]["verify"]["steps"] if step.get("name") == "Test isolated SQL Server Developer")
        script = "set -euo pipefail\n" + step["run"][step["run"].index('case "$IO_CI_SQL_SUITE"'):]
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            host = tmp / "dotnet"
            host.write_text('''#!/usr/bin/env bash
set -euo pipefail
mode=full
[[ "${*: -1}" != --virtual-worker-only ]] || mode=virtual
echo "$mode" >> "$IO_TEST_CALLS"
[[ "$IO_TEST_FAIL" != "$mode" ]] || { echo 'Synthetic failure'; exit 9; }
if [[ "$mode" == virtual ]]; then
  echo 'PASS disposable virtual worker preflight complete'
else
  python3 -c 'print("PASS synthetic policy fixture\\n" * 850, end=""); print("PASS disposable SQL Server integration checks")'
fi
''')
            host.chmod(0o755)
            calls, summary = tmp / "calls", tmp / "summary"
            env = {**os.environ, "PATH": str(tmp) + os.pathsep + os.environ["PATH"], "RUNNER_TEMP": str(tmp), "GITHUB_STEP_SUMMARY": str(summary), "IO_CI_SQL_SUITE": suite, "IO_TEST_CALLS": str(calls), "IO_TEST_FAIL": fail}
            result = subprocess.run(["bash", "-c", script], cwd=ROOT / "ReCode", env=env, capture_output=True, text=True)
            return result.returncode, calls.read_text().splitlines() if calls.exists() else [], summary.read_text() if summary.exists() else ""

    def test_default_full_does_not_duplicate_worker_lab(self):
        status, calls, summary = self.run_suite("full")
        self.assertEqual(status, 0)
        self.assertEqual(calls, ["full"])
        self.assertIn("Full SQL regression passed", summary)

    def test_focused_run_never_claims_full_regression(self):
        status, calls, summary = self.run_suite("virtual-worker")
        self.assertEqual(status, 0)
        self.assertEqual(calls, ["virtual"])
        self.assertIn("full SQL regression was NOT run", summary)
        self.assertNotIn("Full SQL regression passed", summary)

    def test_extra_fresh_database_is_explicitly_available(self):
        status, calls, _ = self.run_suite("full-with-preflight")
        self.assertEqual(status, 0)
        self.assertEqual(calls, ["virtual", "full"])

    def test_invalid_mode_cannot_succeed_without_tests(self):
        status, calls, _ = self.run_suite("typo")
        self.assertNotEqual(status, 0)
        self.assertEqual(calls, [])

    def test_worker_failure_stops_before_full(self):
        status, calls, summary = self.run_suite("full-with-preflight", "virtual")
        self.assertNotEqual(status, 0)
        self.assertEqual(calls, ["virtual"])
        self.assertNotIn("passed", summary)

    def test_full_failure_cannot_be_reported_as_success(self):
        status, calls, summary = self.run_suite("full", "full")
        self.assertNotEqual(status, 0)
        self.assertEqual(calls, ["full"])
        self.assertNotIn("Full SQL regression passed", summary)


if __name__ == "__main__":
    unittest.main(verbosity=2)
