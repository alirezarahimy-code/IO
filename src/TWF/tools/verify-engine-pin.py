#!/usr/bin/env python3
"""ARIA — engine pin verification.

The repository contains three divergent T5V3 engine builds. Aria must run on
exactly ONE of them, otherwise two operators can see two different leave
balances for the same request.

This tool enforces the pin:

    engine/engine-pin.json     <- committed lock file (chosen build + md5 per DLL)
    --write                    <- (re)generate the lock file from the pinned source
    --check [--dir <folder>]   <- verify a deployment folder matches the pin
    --list                     <- list the pin in human form

Default --check target is the folder Aria would deploy from; if Aria's bin
folder is empty (as in this repository) the tool checks every candidate folder
it knows about and reports which ones match.

Exit codes:  0 = pinned engine present and verified
             1 = mismatch / divergent engine found
             2 = pinned engine files not found at all
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
PIN_FILE = os.path.join(os.path.dirname(__file__), "..", "engine", "engine-pin.json")

# Logical name -> candidate file names (case differs between packages)
ASSEMBLIES = [
    ("T5V3.DB.dll", ["T5V3.DB.dll"]),
    ("T5V3.Model.dll", ["T5V3.Model.dll"]),
    ("T5V3.Attendance.dll", ["T5V3.Attendance.dll"]),
    ("T5V3.LeaveCalcs.dll", ["T5V3.LeaveCalcs.dll", "T5V3.LeaveCalcs.DLL"]),
    ("T5V3.Misc.dll", ["T5V3.Misc.dll"]),
    ("T5V3.Report.dll", ["T5V3.Report.dll"]),
    ("T5V3.ReportGenerator.dll", ["T5V3.ReportGenerator.dll"]),
    ("Personnel.dll", ["Personnel.dll", "Personnel.DLL"]),
]

# Where an operator is likely to have placed the engine
CANDIDATE_DIRS = {
    "aria-bin": os.path.join(REPO_ROOT, "src", "TWF", "TWF.Web", "bin"),
    "aria-build": os.path.join(REPO_ROOT, "src", "TWF", "build"),
    "twf-4.28": os.path.join(REPO_ROOT, "TWF 4.28", "App", "bin"),
    "t5web-3.5.7": os.path.join(REPO_ROOT, "T5Web [3.5.7]", "App", "bin"),
    "t5v3-3.9.2": os.path.join(REPO_ROOT, "T5V3.9.2"),
}


def md5(path):
    h = hashlib.md5()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def find(folder, candidates):
    for name in candidates:
        p = os.path.join(folder, name)
        if os.path.exists(p):
            return p
    return None


def write_pin(folder, build_label):
    pin = {
        "pinned_build": build_label,
        "pinned_from": os.path.relpath(folder, REPO_ROOT),
        "reason": (
            "Only engine build whose public API covers every type/member Aria calls. "
            "Verified by tools/analyze-engine.py: the desktop 3.9.2 build removed "
            "Personnel TitleChartDA.GetAllGuardAccess / TitlesChart.GuardAccess, which "
            "LeavesReportController and T5V3BackendAdapter depend on."
        ),
        "verify_command": "python3 tools/verify-engine-pin.py --check",
        "assemblies": {},
    }
    for logical, candidates in ASSEMBLIES:
        path = find(folder, candidates)
        if not path:
            sys.stderr.write(f"WARNING: {logical} not found in {folder}\n")
            continue
        pin["assemblies"][logical] = {
            "file": os.path.basename(path),
            "size": os.path.getsize(path),
            "md5": md5(path),
        }
    os.makedirs(os.path.dirname(PIN_FILE), exist_ok=True)
    with open(PIN_FILE, "w", encoding="utf-8") as fh:
        json.dump(pin, fh, indent=2, ensure_ascii=False)
    print(f"Pin written to {os.path.relpath(PIN_FILE, REPO_ROOT)}")
    print(f"  build  : {build_label}")
    print(f"  source : {os.path.relpath(folder, REPO_ROOT)}")
    for logical, meta in pin["assemblies"].items():
        print(f"  {logical:26s} {meta['size']:>9,} B  {meta['md5']}")
    return 0


def load_pin():
    if not os.path.exists(PIN_FILE):
        sys.stderr.write(f"No pin file at {PIN_FILE}. Run --write first.\n")
        sys.exit(2)
    with open(PIN_FILE, encoding="utf-8") as fh:
        return json.load(fh)


def check_folder(pin, folder, label):
    """Return (status, details). status in {'match','mismatch','missing'}"""
    details = []
    status = "match"
    for logical, expected in pin["assemblies"].items():
        path = find(folder, [expected["file"], *[c for _, cs in ASSEMBLIES if _ == logical for c in cs]])
        if not path:
            details.append((logical, "MISSING", "-", expected["md5"]))
            status = "missing"
            continue
        actual = md5(path)
        if actual != expected["md5"]:
            state = "mismatch"
            status = "mismatch" if status != "missing" else status
            details.append((logical, state, actual, expected["md5"]))
        else:
            details.append((logical, "ok", actual, expected["md5"]))
    if status == "match":
        return status, details
    # A folder with *no* engine files at all is "missing", not a mismatch
    if all(d[1] == "MISSING" for d in details):
        return "missing", details
    return ("mismatch" if any(d[1] == "mismatch" for d in details) else "missing"), details


def run_check(target_dir):
    pin = load_pin()
    print("=" * 78)
    print("  ARIA engine pin check")
    print("=" * 78)
    print(f"  pinned build : {pin['pinned_build']}  ({pin['pinned_from']})")
    print()

    folders = {}
    if target_dir:
        folders["target"] = os.path.abspath(target_dir)
    else:
        folders.update(CANDIDATE_DIRS)

    results = {}
    for label, folder in folders.items():
        if not os.path.isdir(folder):
            continue
        status, details = check_folder(pin, folder, label)
        results[label] = (status, folder, details)

    if not results:
        print("  No candidate engine folder exists yet.")
        print("  Copy the pinned engine into src/TWF/TWF.Web/bin, then re-run.")
        return 2

    worst = 0
    for label, (status, folder, details) in results.items():
        icon = {"match": "OK       ", "mismatch": "DIVERGENT", "missing": "EMPTY    "}[status]
        print(f"  [{icon}] {label:14s} {os.path.relpath(folder, REPO_ROOT)}")
        if status != "match":
            for logical, state, actual, expected in details:
                if state == "ok":
                    continue
                print(f"        {logical:26s} {state:9s} actual={actual} expected={expected}")
            if status == "mismatch":
                worst = max(worst, 1)
    print()

    if target_dir:
        status = results["target"][0]
        if status == "match":
            print("  RESULT: target folder runs the PINNED engine. Safe to deploy.")
            return 0
        if status == "mismatch":
            print("  RESULT: target folder contains a DIFFERENT engine build.")
            print("          Two rules of arithmetic will disagree. Do not deploy.")
            return 1
        print("  RESULT: target folder has no engine assemblies yet.")
        return 2

    pinned_present = any(s == "match" for s, _f, _d in results.values())
    print("  RESULT:", "pinned engine available" if pinned_present else "pinned engine NOT found")
    return 0 if pinned_present else 1


# Assemblies whose bytes decide how attendance/leave is computed. If these are
# byte-identical between two builds, both builds produce identical arithmetic.
COMPUTATION_CRITICAL = ["T5V3.Attendance.dll", "T5V3.LeaveCalcs.dll"]


def run_equality(baseline_dir):
    """Prove that pinning does not change the arithmetic.

    Compares the computation-critical assemblies between the baseline build
    (what Aria was compiled against) and the pinned build.
    """
    pin = load_pin()
    pinned_dir = os.path.join(REPO_ROOT, pin["pinned_from"])
    baseline_dir = os.path.abspath(baseline_dir)

    print("=" * 78)
    print("  ARIA engine computation-equality check")
    print("=" * 78)
    print(f"  baseline : {os.path.relpath(baseline_dir, REPO_ROOT)}")
    print(f"  pinned   : {pin['pinned_from']}")
    print()

    all_equal = True
    for logical in COMPUTATION_CRITICAL:
        base = find(baseline_dir, [logical])
        pin_path = find(pinned_dir, [logical])
        if not base or not pin_path:
            print(f"  {logical:26s} MISSING (baseline={bool(base)}, pinned={bool(pin_path)})")
            all_equal = False
            continue
        same = md5(base) == md5(pin_path)
        all_equal &= same
        print(f"  {logical:26s} {'IDENTICAL' if same else 'DIFFERENT'}")
        if not same:
            print(f"      baseline md5 = {md5(base)}")
            print(f"      pinned   md5 = {md5(pin_path)}")

    print()
    if all_equal:
        print("  RESULT: computation assemblies are byte-identical.")
        print("          Pinning to this build cannot change attendance/leave arithmetic.")
        return 0
    print("  RESULT: computation assemblies DIFFER - arithmetic may change.")
    print("          Run a parallel calculation on real data before switching.")
    return 1


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--write", metavar="FOLDER", help="(re)generate the pin from FOLDER")
    g.add_argument("--check", action="store_true", help="verify deployment folder(s)")
    g.add_argument("--list", action="store_true", help="print the pin")
    g.add_argument("--equality", metavar="BASELINE_FOLDER",
                   help="prove arithmetic equality between BASELINE_FOLDER and the pin")
    ap.add_argument("--dir", help="with --check: verify exactly this folder")
    ap.add_argument("--build-label", default="t5web-3.5.7", help="label recorded in the pin")
    args = ap.parse_args()

    if args.write:
        return write_pin(os.path.abspath(args.write), args.build_label)
    if args.equality:
        return run_equality(args.equality)
    if args.list:
        pin = load_pin()
        print(json.dumps(pin, indent=2, ensure_ascii=False))
        return 0
    return run_check(args.dir)


if __name__ == "__main__":
    sys.exit(main())
