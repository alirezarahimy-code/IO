#!/usr/bin/env python3
"""Synthetic fixtures for offline H01 index validation; NOT physical acceptance. Run on Windows CI."""
import contextlib
from copy import deepcopy
from datetime import datetime, timezone
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("h01", Path(__file__).with_name("lab-h01.py"))
h01 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h01)
NOW = datetime(2026, 11, 8, tzinfo=timezone.utc)


def indexed_fixture():
    """Obviously synthetic references. Valid INDEX, never evidence of actual execution."""
    value = h01.template()
    value.update(buildSha="a" * 40, packageSha256="b" * 64)
    value["environment"].update(kind="physical-windows-lab", windowsVersion="test-windows",
        runtimeVersion="test-runtime", sdkVersion="test-sdk", deploymentReference="TEST-deployment",
        physicalLabReference="TEST-physical-lab")
    for i, device in enumerate(value["devices"]):
        device.update(physicalIdentityReference=f"TEST-identity-{i}", model="test-model", firmware="test-firmware", readFormat="ssr-v1")
    for row in value["scenarios"]:
        row.update(outcome="pass", observedAtUtc="2020-01-01T12:00:00Z", operatorReference="TEST-operator",
                   reviewerReference="TEST-reviewer", evidenceReferences=["TEST-evidence"], receiptReferences=["TEST-receipt"], capabilityDisabled=False)
        row["checks"] = dict.fromkeys(row["checks"], "TEST-check-evidence")
        if row["id"] in h01.DESTRUCTIVE:
            for key in ("authorizationReference", "effectReference", "exclusiveControlReference", "recoveryPlanReference"):
                row[key] = "TEST-approval"
    value["review"].update(operatorReference="TEST-operator", independentReviewerReference="TEST-reviewer", matrixApprovalReference="TEST-matrix")
    return value


class EvidenceIndex(unittest.TestCase):
    def setUp(self):
        self.value = indexed_fixture()

    def issues(self):
        return h01.validate(self.value, NOW)

    def test_fixture_is_only_structurally_indexed(self):
        self.assertEqual(self.issues(), [])
        self.assertIs(self.value["hardwareVerified"], False)
        self.assertIs(self.value["part3Complete"], False)

    def test_template_is_pending_and_never_ready(self):
        self.assertTrue(h01.validate(h01.template(), NOW))
        self.assertEqual(len(h01.template()["scenarios"]), 24)
        self.assertEqual(set(h01.SCENARIOS), {f"H01-{i:02d}" for i in range(1, 13)})

    def test_published_template_matches_generator(self):
        path = Path(__file__).resolve().parents[1] / "docs/part-03/h01-evidence-index.template.json"
        self.assertEqual(h01.load(path), h01.template())

    def test_no_missing_or_extra_root_fields(self):
        for key in list(self.value):
            changed = deepcopy(self.value)
            del changed[key]
            self.assertTrue(h01.validate(changed, NOW), key)
        self.value["password"] = "MUST-NOT-APPEAR"
        self.assertNotIn("MUST-NOT-APPEAR", json.dumps(self.issues()))

    def test_unknown_nested_fields_fail_closed(self):
        for section in (self.value["environment"], self.value["devices"][0], self.value["scenarios"][0], self.value["review"]):
            section["secret"] = "MUST-NOT-APPEAR"
        result = json.dumps(self.issues())
        self.assertNotIn("MUST-NOT-APPEAR", result)
        self.assertTrue(self.issues())

    def test_untrusted_values_not_echoed(self):
        self.value["scenarios"][0]["operatorReference"] = "password=MUST-NOT-APPEAR"
        self.value["scenarios"][0]["id"] = "MUST-NOT-APPEAR"
        self.assertNotIn("MUST-NOT-APPEAR", json.dumps(self.issues()))
        self.assertTrue(self.issues())

    def test_schema_and_hashes(self):
        for field, bad in (("schema", "unknown"), ("buildSha", "a" * 39), ("packageSha256", "a" * 63), ("buildSha", True)):
            v = deepcopy(self.value)
            v[field] = bad
            self.assertTrue(h01.validate(v, NOW))

    def test_no_automatic_acceptance_even_integer_flags(self):
        for field in ("hardwareVerified", "part3Complete"):
            for flag in (True, 0, None, "false"):
                self.value[field] = flag
                self.assertTrue(self.issues())
            self.value[field] = False

    def test_nonphysical_environments_block(self):
        for kind in ("unverified", "ci", "synthetic", "simulator", "linux"):
            self.value["environment"]["kind"] = kind
            self.assertTrue(self.issues())

    def test_two_distinct_device_aliases_and_identities_required(self):
        for key in ("alias", "physicalIdentityReference"):
            v = deepcopy(self.value)
            v["devices"][1][key] = v["devices"][0][key]
            self.assertTrue(h01.validate(v, NOW))
        self.value["devices"] = self.value["devices"][:1]
        self.assertTrue(self.issues())

    def test_device_model_firmware_and_format_required(self):
        for field in ("model", "firmware", "readFormat"):
            v = deepcopy(self.value)
            v["devices"][0][field] = None
            self.assertTrue(h01.validate(v, NOW))
        self.value["devices"][0]["readFormat"] = "legacy-int32-v1"
        self.assertEqual(self.issues(), [])

    def test_versioned_read_formats_match_native_contract(self):
        path = Path(__file__).resolve().parents[1] / "src/IO.DeviceContracts/ZkUserReadExecutor.cs"
        source = path.read_text(encoding="utf-8")
        self.assertIn('Ssr = "ssr-v1"', source)
        self.assertIn('Legacy = "legacy-int32-v1"', source)
        self.value["devices"][0]["readFormat"] = "ssr"
        self.assertTrue(self.issues())

    def test_each_device_needs_every_scenario(self):
        self.value["scenarios"].pop()
        self.assertTrue(self.issues())

    def test_duplicate_scenario_not_a_second_device(self):
        self.value["scenarios"][-1] = deepcopy(self.value["scenarios"][0])
        self.assertTrue(self.issues())

    def test_unknown_or_injected_scenario_device(self):
        for field, bad in (("id", "H01-99"), ("device", "LAB-OTHER"), ("id", []), ("device", {})):
            v = deepcopy(self.value)
            v["scenarios"][0][field] = bad
            self.assertTrue(h01.validate(v, NOW))

    def test_pending_failure_not_run_block(self):
        for outcome in ("pending", "fail", "not-run", "success", None, True):
            self.value["scenarios"][0]["outcome"] = outcome
            self.assertTrue(self.issues())

    def test_optional_scope_only_explicit_disabled_disposition(self):
        row = self.value["scenarios"][7]
        row.update(outcome="unsupported", capabilityDisabled=True, unsupportedDispositionReference="TEST-disabled-scope")
        self.assertEqual(self.issues(), [])
        row["capabilityDisabled"] = False
        self.assertTrue(self.issues())
        row["capabilityDisabled"] = True
        row["unsupportedDispositionReference"] = None
        self.assertTrue(self.issues())

    def test_required_scenarios_cannot_be_waived(self):
        for row in self.value["scenarios"]:
            if row["id"] == "H01-08":
                continue
            original = deepcopy(row)
            row.update(outcome="unsupported", capabilityDisabled=True, unsupportedDispositionReference="TEST-waiver")
            self.assertTrue(self.issues())
            row.update(original)

    def test_success_cannot_hide_disposition(self):
        self.value["scenarios"][0]["unsupportedDispositionReference"] = "TEST-waiver"
        self.assertTrue(self.issues())

    def test_distinct_reviewers_required_per_scenario_and_final_matrix(self):
        self.value["scenarios"][0]["reviewerReference"] = "TEST-operator"
        self.assertTrue(self.issues())
        self.value = indexed_fixture()
        self.value["review"]["independentReviewerReference"] = "TEST-operator"
        self.assertTrue(self.issues())

    def test_utc_observation_must_be_present_and_not_future(self):
        for date in (None, "2026-11-07T12:00:00", "2026-11-09T12:00:00Z", "2026-02-30T00:00:00Z", "MUST-NOT-APPEAR", "2026-11-07Z"):
            self.value["scenarios"][0]["observedAtUtc"] = date
            self.assertTrue(self.issues())

    def test_r15_is_last_clear_after_previous_scenarios(self):
        self.value["scenarios"][0]["observedAtUtc"] = "2026-11-07T12:00:00Z"
        self.assertTrue(self.issues())

    def test_each_subcheck_needs_private_evidence(self):
        for scenario in h01.SCENARIOS:
            v = deepcopy(self.value)
            row = next(x for x in v["scenarios"] if x["id"] == scenario)
            row["checks"][next(iter(row["checks"]))] = None
            self.assertTrue(h01.validate(v, NOW))

    def test_no_unknown_subchecks(self):
        self.value["scenarios"][0]["checks"]["factory-reset"] = "TEST-reference"
        self.assertTrue(self.issues())

    def test_software_receipt_not_substitute_for_evidence(self):
        self.value["scenarios"][0]["evidenceReferences"] = []
        self.assertTrue(self.issues())
        self.value = indexed_fixture()
        self.value["scenarios"][2]["receiptReferences"] = []
        self.assertTrue(self.issues())

    def test_destructive_requires_all_external_approval_refs(self):
        for sid in h01.DESTRUCTIVE:
            for key in ("authorizationReference", "effectReference", "exclusiveControlReference", "recoveryPlanReference"):
                v = deepcopy(self.value)
                next(x for x in v["scenarios"] if x["id"] == sid)[key] = None
                self.assertTrue(h01.validate(v, NOW))

    def test_references_not_urls_paths_raw_or_multiline(self):
        for reference in ("https://example.org/secret", "C:\\secret", "../private.json", "raw\ntemplate", "x" * 97):
            self.value["review"]["matrixApprovalReference"] = reference
            self.assertTrue(self.issues())

    def test_bounded_collection_types(self):
        for field, bad in (("devices", {}), ("devices", [None] * 33), ("scenarios", {}), ("scenarios", [None] * 385)):
            v = deepcopy(self.value)
            v[field] = bad
            self.assertTrue(h01.validate(v, NOW))

    def test_strict_json_input(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "index.json"
            for content in (b'{"x":1,"x":2}', b'{"x":NaN}', b'{"x":Infinity}', b'\xff', b'{' * 2000, b' ' * (h01.LIMIT + 1)):
                path.write_bytes(content)
                with self.assertRaises((ValueError, RecursionError)):
                    h01.load(path)

    def test_init_never_overwrites_and_logs_no_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "MUST-NOT-APPEAR.json"
            stdout, stderr = io.StringIO(), io.StringIO()
            with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                self.assertEqual(h01.main(["--init", str(path)]), 0)
                original = path.read_bytes()
                self.assertEqual(h01.main(["--init", str(path)]), 2)
                self.assertEqual(path.read_bytes(), original)
            self.assertNotIn("MUST-NOT-APPEAR", stdout.getvalue() + stderr.getvalue())

    def test_cli_exit_codes_and_no_acceptance(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "index.json"
            for value, expected in ((h01.template(), 1), (indexed_fixture(), 0)):
                path.write_text(json.dumps(value), encoding="utf-8")
                output = io.StringIO()
                with contextlib.redirect_stdout(output):
                    self.assertEqual(h01.main(["--check", str(path)]), expected)
                report = json.loads(output.getvalue())
                self.assertIs(report["hardwareVerified"], False)
                self.assertIs(report["part3Complete"], False)
                self.assertIs(report["physicalEvidenceAuthenticityVerified"], False)
            path.write_text('{"password":"MUST-NOT-APPEAR",', encoding="utf-8")
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                self.assertEqual(h01.main(["--check", str(path)]), 1)
            self.assertNotIn("MUST-NOT-APPEAR", output.getvalue())


if __name__ == "__main__":
    unittest.main(verbosity=2)
