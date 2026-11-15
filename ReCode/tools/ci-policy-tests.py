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
    if "push" not in workflow["on"]:
        return False
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
    def test_h01_index_checks_are_windows_offline_fixtures_not_hardware_acceptance(self):
        self.assertTrue(triggered(LIGHT, ["ReCode/tools/lab-h01.py"]))
        self.assertTrue(triggered(LIGHT, ["ReCode/tools/ci-h01-tests.py"]))
        steps = LIGHT["jobs"]["light"]["steps"]
        step = next(s for s in steps if s.get("name") == "Test offline H01 evidence index with synthetic fixtures only")
        self.assertEqual(step["run"], "python ReCode/tools/ci-h01-tests.py")
        self.assertNotIn("if", step)
        self.assertEqual(LIGHT["jobs"]["light"]["runs-on"], "windows-2022")

    def test_gateway_and_remote_enroll_contracts_cannot_implicitly_enable_native_or_linux_execution(self):
        verifier = (ROOT / "ReCode/tools/verify-platform.ps1").read_text()
        self.assertIn("--gateway-enroll-contracts", verifier)
        self.assertIn("GatewayEnrollmentTests.RunAsync()", (ROOT / "ReCode/tests/IO.DeviceRuntime.Tests/Program.cs").read_text())
        self.assertIn("audit-remote-enroll-typelib.py", (WORKFLOWS / "io-lightweight.yml").read_text())
        program = (ROOT / "ReCode/src/IO.Web/Program.cs").read_text()
        self.assertIn('GetValue<bool>("IO:PushGateway:EnableIngress")', program)
        endpoints = (ROOT / "ReCode/src/IO.Web/GatewayEndpoints.cs").read_text()
        self.assertIn('if(!enabled)return;', endpoints)
        self.assertIn('if(!c.Request.IsHttps)', endpoints)
        self.assertIn('AuthenticatedPushGateway.Path,Receive', endpoints)
        self.assertNotIn('app.MapPost("/iclock', program)
        adapter = (ROOT / "ReCode/src/IO.ZkBridge/RemoteEnrollmentAdapter.cs").read_text()
        self.assertIn('if(!input.IsValid())', adapter)
        self.assertIn('RemoteEnrollWorkflow', adapter)
        self.assertIn('EmptySlotErrorCode', adapter)
        self.assertIn('Approval.EventMask>0', (ROOT / "ReCode/src/IO.DeviceContracts/RemoteEnrollProtocol.cs").read_text())
        self.assertFalse(triggered(SQL, ["ReCode/src/IO.DeviceContracts/RemoteEnrollWorkflow.cs"]))

    def test_automatic_jobs_are_windows_only_and_sql_linux_requires_explicit_opt_in(self):
        self.assertEqual(PLATFORM["jobs"]["platform"]["runs-on"], "windows-2022")
        self.assertNotIn("strategy", PLATFORM["jobs"]["platform"])
        self.assertEqual(LIGHT["jobs"]["light"]["runs-on"], "windows-2022")
        self.assertEqual(LIGHT["jobs"]["light"]["defaults"]["run"]["shell"], "bash")
        self.assertNotIn("push", SQL["on"])
        self.assertFalse(triggered(SQL, ["ReCode/src/IO.Infrastructure/Storage/IoDbContext.cs"]))
        self.assertEqual(SQL["on"]["workflow_dispatch"]["inputs"]["allow_linux_runner"]["default"], "false")
        self.assertIn("inputs.allow_linux_runner == true", SQL["jobs"]["verify"]["if"])
        self.assertIn("github.event_name == 'workflow_dispatch'", SQL["jobs"]["verify"]["if"])

    def test_documentation_does_not_launch_heavy_jobs(self):
        for path in ["README.md", "ReCode/docs/part-03/progress-evidence.json", "ReCode/docs/part-03/s10-legacy-polling.fa.md"]:
            for workflow in [PLATFORM]:
                self.assertFalse(triggered(workflow, [path]), path)
            self.assertTrue(triggered(LIGHT, [path]), path)

    def test_ui_and_preview_use_light_checks(self):
        for path in ["ReCode/src/IO.Web/Ui/device-schedules.js", "ReCode/src/IO.Web/Ui/DeviceSchedules.html", "ReCode/preview/pages-first/pages.js", "ReCode/tests/IO.Infrastructure.SqlTests/device-access-ui-check.cjs"]:
            for workflow in [PLATFORM]:
                self.assertFalse(triggered(workflow, [path]), path)
            self.assertTrue(triggered(LIGHT, [path]), path)

    def test_native_dispatch_proof_is_reproduced_without_hardware(self):
        self.assertTrue(triggered(LIGHT, ['ReCode/tools/inspect-zkem-network-dispatch.py']))
        steps = LIGHT['jobs']['light']['steps']
        probe = next(s for s in steps if s.get('name') ==
                     'Verify hash-pinned network setter dispatch with trapped transport')
        self.assertEqual(probe['run'], 'python ReCode/tools/inspect-zkem-network-dispatch.py --verify')
        self.assertNotIn('if', probe)
        dependency = next(s for s in steps if s.get('name') ==
                          'Prepare offline native instruction emulator')
        self.assertIn('pefile==2024.8.26 unicorn==2.1.4', dependency['run'])

    def test_firmware_reference_and_production_mapper_cannot_skip_checks(self):
        self.assertTrue(triggered(LIGHT, ['ReCode/tools/audit-dhcp-firmware-reference.py']))
        audit = next(s for s in LIGHT['jobs']['light']['steps'] if s.get('name') ==
                     'Audit pinned DHCP firmware source reference without execution')
        self.assertEqual(audit['run'], 'python ReCode/tools/audit-dhcp-firmware-reference.py')
        self.assertNotIn('if', audit)
        for path in ['ReCode/src/IO.ZkBridge/ZkComProfileSession.cs',
                     'ReCode/tests/IO.DeviceRuntime.Tests/DhcpComProfileTests.cs']:
            for workflow in [PLATFORM]:
                self.assertTrue(triggered(workflow, [path]))

    def test_push_review_preserves_read_only_manual_sql_and_windows_dom_contracts(self):
        program = (ROOT / "ReCode/src/IO.Web/Program.cs").read_text()
        self.assertIn('app.MapGet("/device-push-evidence",', program)
        self.assertNotIn('app.MapPost("/device-push-evidence', program)
        self.assertNotIn('app.MapPost("/iclock', program)
        self.assertIn("await DevicePushReviewTests.RunAsync(connection, Check);",
                      (ROOT / "ReCode/tests/IO.Infrastructure.SqlTests/Program.cs").read_text())
        self.assertTrue((ROOT / "ReCode/tests/IO.Infrastructure.SqlTests/device-push-evidence-ui-check.cjs").exists())
        for workflow in [PLATFORM]:
            self.assertTrue(triggered(workflow, ["ReCode/src/IO.Infrastructure/Devices/DevicePushReview.cs"]))
        self.assertTrue(triggered(LIGHT, ["ReCode/tests/IO.Infrastructure.SqlTests/device-push-evidence-ui-check.cjs"]))

    def test_push_journal_migration_and_transaction_tests_are_mandatory(self):
        step = next(s for s in SQL["jobs"]["verify"]["steps"] if s.get("name") ==
                    "Require versioned PUSH evidence journal migration")
        self.assertIn("20261114110000_DevicePushJournal.cs", step["run"])
        self.assertNotIn("if", step)
        self.assertIn("await DevicePushJournalTests.RunAsync(connection, Check);",
                      (ROOT / "ReCode/tests/IO.Infrastructure.SqlTests/Program.cs").read_text())
        for workflow in [PLATFORM]:
            self.assertTrue(triggered(workflow, ["ReCode/src/IO.Infrastructure/Devices/DevicePushJournal.cs"]))

    def test_push_codec_remains_in_manual_sql_and_windows_without_claiming_ingress(self):
        step = next(s for s in SQL['jobs']['verify']['steps'] if s.get('name') ==
                    'Test explicit PUSH ATTLOG codec without claiming authentication or durable SQL receipt')
        self.assertNotIn('if', step)
        self.assertIn('--push-codec-contracts', step['run'])
        self.assertIn('--push-codec-contracts', (ROOT / 'ReCode/tools/verify-platform.ps1').read_text())
        for workflow in [PLATFORM]:
            self.assertTrue(triggered(workflow, ['ReCode/src/IO.DeviceContracts/ZkPushAttendanceCodec.cs']))

    def test_sensitive_or_unknown_changes_still_run_windows(self):
        for path in ["ReCode/src/IO.Infrastructure/Migrations/new.cs", "ReCode/src/IO.Infrastructure/Security/DeviceAccess.cs", "ReCode/src/IO.Web/Program.cs", "ReCode/src/IO.DeviceWorker/Program.cs", "ReCode/src/IO.Web/Ui/HelpContent.json", "ReCode/tests/IO.Infrastructure.SqlTests/VirtualWorkerTests.cs", "ReCode/tools/verify-platform.ps1", "ReCode/global.json", "ReCode/Directory.Build.props", "ReCode/src/IO.Web/IO.Web.csproj", ".github/workflows/io-cross-platform.yml", "future/unclassified.file"]:
            for workflow in [PLATFORM]:
                self.assertTrue(triggered(workflow, [path]), path)

    def test_mixed_push_cannot_hide_backend_changes(self):
        for workflow in [PLATFORM]:
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
        script = script.replace("python3 ", "python ")
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
  python -c 'print("PASS synthetic policy fixture\\n" * (int(__import__("os").environ["IO_TEST_PASS_LINES"]) - 1), end=""); print("PASS disposable SQL Server integration checks")'
fi
''', encoding="utf-8", newline="\n")
            host.chmod(0o755)
            calls, summary = tmp / "calls", tmp / "summary"
            env = {**os.environ, "PATH": str(tmp) + os.pathsep + os.environ["PATH"], "RUNNER_TEMP": tmp.as_posix(), "GITHUB_STEP_SUMMARY": summary.as_posix(), "IO_CI_SQL_SUITE": suite, "IO_TEST_CALLS": calls.as_posix(), "IO_TEST_FAIL": fail, "IO_TEST_PASS_LINES": re.search(r"len\(lines\) != (\d+)", (ROOT / "ReCode/tools/phase03-report-tests.py").read_text()).group(1)}
            bash = str(Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe") if os.name == "nt" else "bash"
            result = subprocess.run([bash, "-c", script], cwd=ROOT / "ReCode", env=env, capture_output=True, text=True)
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
