#!/usr/bin/env python3
"""Offline H01 evidence-index checker. Never connects, executes SDK, grants or accepts hardware.
References point to private organizational evidence; this is NOT a signature verifier.
"""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import sys

SCHEMA = "io-h01-evidence-index-v1"
# Every check needs its own result/reference, on EACH of at least two physical devices.
SCENARIOS = {
    "H01-01": ("scm-start-stop-restart", "child-exit", "identity-acl-keyring-sql"),
    "H01-02": ("native-connect-pin", "wrong-endpoint-serial-rejected", "operator-scope"),
    "H01-03": ("full-users", "full-native-attendance", "duplicates-unknown-leading-zero"),
    "H01-04": ("one-device-outage-other-continues", "service-restart", "read-repeat-dedup"),
    "H01-05": ("poll-pause-resume-revoke-overlap", "real-file-format", "quarantine-original-bytes"),
    "H01-06": ("settings-read", "approved-setters-readback", "network-reconnect-lease", "per-setting-support-matrix"),
    "H01-07": ("single-multiple-two-device-upload", "no-overwrite-protected-preserved", "partial-late-ack"),
    "H01-08": ("gateway-native-trust-boundary", "real-enrollment-readback", "biometric-format-retention-revoke", "private-photo-staging-or-disabled"),
    "H01-09": ("all-attendance-clear", "range-boundaries-dst-seconds", "outside-range-duplicates-preserved"),
    "H01-10": ("ordinary-user-erase", "selected-fingerprint-slots", "administrator-demotion-preservation"),
    "H01-11": ("clear-data-5-not-factory-reset", "same-endpoint-key-reconnect", "empty-users-logs-orphan-probes"),
    "H01-12": ("loss-of-ack-cancel-kill", "old-child-stop-nondestructive-reset", "fresh-archives-read-only-inspection", "independent-release-original-unknown", "scope-policy-revoke"),
}
DESTRUCTIVE = {"H01-09", "H01-10", "H01-11", "H01-12"}
REF = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{2,95}\Z")
VERSION = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.+() -]{0,95}\Z")
SHA = re.compile(r"[0-9a-f]{40}\Z")
HASH = re.compile(r"[0-9a-f]{64}\Z")
LIMIT = 1024 * 1024


def template():
    return {
        "schema": SCHEMA,
        "buildSha": None,
        "packageSha256": None,
        "environment": {"kind": "unverified", "windowsVersion": None, "runtimeVersion": None,
                        "sdkVersion": None, "deploymentReference": None, "physicalLabReference": None},
        "devices": [{"alias": alias, "physicalIdentityReference": None, "model": None, "firmware": None,
                     "readFormat": None} for alias in ("LAB-A", "LAB-B")],
        "scenarios": [{"id": scenario, "device": alias, "outcome": "pending", "observedAtUtc": None,
                       "operatorReference": None, "reviewerReference": None,
                       "evidenceReferences": [], "receiptReferences": [],
                       "checks": {name: None for name in checks},
                       "authorizationReference": None, "effectReference": None,
                       "exclusiveControlReference": None, "recoveryPlanReference": None,
                       "capabilityDisabled": True, "unsupportedDispositionReference": None}
                      for alias in ("LAB-A", "LAB-B") for scenario, checks in SCENARIOS.items()],
        "review": {"operatorReference": None, "independentReviewerReference": None,
                   "matrixApprovalReference": None},
        # These MUST remain false. External human acceptance is a separate record/process.
        "hardwareVerified": False, "part3Complete": False,
    }


def validate(value, now=None):
    """Return only fixed field paths/codes; never echo supplied text or secrets."""
    issues = []
    now = now or datetime.now(timezone.utc)

    def issue(path, code):
        issues.append({"path": path, "code": code})

    def obj(v, fields, path):
        if not isinstance(v, dict) or set(v) != set(fields):
            issue(path, "exact-fields-required")
            return False
        return True

    def matching(v, pattern, path):
        if not isinstance(v, str) or not pattern.fullmatch(v):
            issue(path, "bounded-identifier-required")
            return False
        return True

    def ref(v, path):
        return matching(v, REF, path)

    expected = template()
    if not obj(value, expected, "$"):
        return issues
    if value["schema"] != SCHEMA:
        issue("$.schema", "unsupported-schema")
    matching(value["buildSha"], SHA, "$.buildSha")
    matching(value["packageSha256"], HASH, "$.packageSha256")
    for key in ("hardwareVerified", "part3Complete"):
        if value[key] is not False:
            issue("$." + key, "checker-cannot-assert-acceptance")
    env = value["environment"]
    if obj(env, expected["environment"], "$.environment"):
        if env["kind"] != "physical-windows-lab":
            issue("$.environment.kind", "physical-windows-lab-required")
        for key in ("windowsVersion", "runtimeVersion", "sdkVersion"):
            matching(env[key], VERSION, "$.environment." + key)
        for key in ("deploymentReference", "physicalLabReference"):
            ref(env[key], "$.environment." + key)
    devices = value["devices"]
    if not isinstance(devices, list) or not 2 <= len(devices) <= 32:
        issue("$.devices", "two-to-32-physical-devices-required")
        return issues
    aliases, physical = set(), set()
    for i, device in enumerate(devices):
        path = f"$.devices[{i}]"
        if not obj(device, expected["devices"][0], path):
            continue
        for key in ("alias", "physicalIdentityReference"):
            if ref(device[key], path + "." + key):
                seen = aliases if key == "alias" else physical
                if device[key] in seen:
                    issue(path + "." + key, "duplicate-device-identity")
                seen.add(device[key])
        for key in ("model", "firmware"):
            matching(device[key], VERSION, path + "." + key)
        if device["readFormat"] not in ("ssr", "legacy-int32"):
            issue(path + ".readFormat", "explicit-read-format-required")
    scenarios = value["scenarios"]
    if not isinstance(scenarios, list) or len(scenarios) > 32 * len(SCENARIOS):
        issue("$.scenarios", "bounded-scenario-list-required")
        return issues
    seen, observed_times = set(), {}
    for i, row in enumerate(scenarios):
        path = f"$.scenarios[{i}]"
        if not obj(row, expected["scenarios"][0], path):
            continue
        sid, alias = row["id"], row["device"]
        if not isinstance(sid, str) or sid not in SCENARIOS or not isinstance(alias, str) or alias not in aliases:
            issue(path, "unknown-scenario-or-device")
            continue
        if (sid, alias) in seen:
            issue(path, "duplicate-scenario-device")
        seen.add((sid, alias))
        unsupported = row["outcome"] == "unsupported"
        if row["outcome"] not in ("pass", "unsupported"):
            issue(path + ".outcome", "unresolved-or-failed")
        # Only optional PUSH/enrollment/biometric scope can be disabled wholesale here.
        # Per-setting differences need a reviewed matrix, not a blanket settings skip.
        if type(row["capabilityDisabled"]) is not bool:
            issue(path + ".capabilityDisabled", "boolean-required")
        if unsupported:
            if sid != "H01-08" or row["capabilityDisabled"] is not True:
                issue(path, "required-scenario-cannot-be-waived")
            ref(row["unsupportedDispositionReference"], path + ".unsupportedDispositionReference")
        elif row["unsupportedDispositionReference"] is not None:
            issue(path + ".unsupportedDispositionReference", "unexpected-disposition")
        for key in ("operatorReference", "reviewerReference"):
            ref(row[key], path + "." + key)
        if row["operatorReference"] == row["reviewerReference"]:
            issue(path, "independent-review-required")
        observed = row["observedAtUtc"]
        try:
            if not isinstance(observed, str) or not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,6})?Z", observed):
                raise ValueError()
            instant = datetime.fromisoformat(observed[:-1] + "+00:00")
            if instant > now:
                raise ValueError()
            observed_times[(sid, alias)] = instant
        except (ValueError, OverflowError):
            issue(path + ".observedAtUtc", "past-or-current-utc-required")
        for key in ("evidenceReferences", "receiptReferences"):
            refs = row[key]
            minimum = 0 if key == "receiptReferences" and (sid in ("H01-01", "H01-08") or unsupported) else 1
            if not isinstance(refs, list) or not minimum <= len(refs) <= 64:
                issue(path + "." + key, "bounded-evidence-references-required")
                continue
            for j, item in enumerate(refs):
                ref(item, path + f".{key}[{j}]")
        if obj(row["checks"], SCENARIOS[sid], path + ".checks"):
            for key, evidence in row["checks"].items():
                # Even disabled optional checks need an explicit disposition reference.
                ref(evidence, path + ".checks." + key)
        for key in ("authorizationReference", "effectReference", "exclusiveControlReference", "recoveryPlanReference"):
            if sid in DESTRUCTIVE:
                ref(row[key], path + "." + key)
            elif row[key] is not None:
                ref(row[key], path + "." + key)
    if seen != {(sid, alias) for sid in SCENARIOS for alias in aliases}:
        issue("$.scenarios", "all-scenarios-on-every-device-required")
    for alias in aliases:
        last_clear = observed_times.get(("H01-11", alias))
        if last_clear is not None and any(
            instant > last_clear for (sid, device), instant in observed_times.items()
            if device == alias and sid not in ("H01-11", "H01-12")
        ):
            issue("$.scenarios", "r15-must-follow-preceding-scenarios")
    review = value["review"]
    if obj(review, expected["review"], "$.review"):
        for key, reference in review.items():
            ref(reference, "$.review." + key)
        if review["operatorReference"] == review["independentReviewerReference"]:
            issue("$.review", "independent-review-required")
    return issues


def no_duplicates(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate field")
        result[key] = value
    return result


def load(path):
    # Bounded read; do not read arbitrary multi-gigabyte raw files into memory.
    with Path(path).open("rb") as source:
        data = source.read(LIMIT + 1)
    if len(data) > LIMIT:
        raise ValueError("oversize")
    def invalid_constant(_):
        raise ValueError("non-finite number")
    return json.loads(data.decode("utf-8-sig"), object_pairs_hook=no_duplicates, parse_constant=invalid_constant)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--init", metavar="NEW_JSON", help="Create draft index exclusively; never overwrite existing files")
    group.add_argument("--check", metavar="JSON", help="Check evidence-index structure only; does NOT accept H01")
    args = parser.parse_args(argv)
    if args.init:
        try:
            # Private reference index, not a config or raw-data export. Parent must exist.
            with Path(args.init).open("x", encoding="utf-8") as target:
                json.dump(template(), target, ensure_ascii=False, indent=2)
                target.write("\n")
        except OSError:
            print("H01 draft not created; require a new writable path in a private directory.", file=sys.stderr)
            return 2
        print("H01 draft created; pending only. No device/SDK/SQL access or acceptance.")
        return 0
    try:
        issues = validate(load(args.check))
    except (OSError, ValueError, RecursionError, TypeError):
        # Do not leak input text, file paths or decoder exception fragments.
        issues = [{"path": "$", "code": "unreadable-or-invalid-bounded-json"}]
    print(json.dumps({"status": "blocked" if issues else "indexed-for-independent-human-review",
                      "issues": issues, "hardwareVerified": False, "part3Complete": False,
                      "physicalEvidenceAuthenticityVerified": False,
                      "notice": "Offline index only; no evidence fetch, SDK execution, signature verification or automatic H01 acceptance."}, indent=2))
    return 1 if issues else 0


if __name__ == "__main__":
    sys.exit(main())
