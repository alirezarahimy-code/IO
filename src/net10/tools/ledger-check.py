#!/usr/bin/env python3
"""Independent, stdlib-only inventory/release gates for Roslyn LEDGER.csv.

--inventory: exact ID, SHA, source-count and frozen-baseline checks. This does
NOT imply any ported behaviour. --release additionally fails on every unmapped,
untested in-scope row and every blocked item. No silent count reduction.
"""
import argparse
import collections
import csv
import hashlib
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[3]
LEDGER = ROOT / "src/net10/LEDGER.csv"
HEADERS = ["ID", "system", "kind", "source", "signature", "source_sha256",
           "line", "legacy_reference", "destination", "status", "evidence", "notes"]
REQUIRED_VIEWS = {
    "TWF 4.28/App/Views/": 59,
    "T5Web [3.5.7]/App/Views/": 112,
    "src/TWF/TWF.Web/Views/": 74,
    "src/T5Web/T5Web.Web/Views/": 34,
}


def read(path):
    with path.open(newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        if reader.fieldnames != HEADERS:
            raise ValueError(f"invalid ledger header: {reader.fieldnames}")
        return list(reader)


def check(rows):
    errors = []
    ids = set()
    for row in rows:
        id_ = row["ID"]
        key = "|".join(row[k] for k in ("system", "kind", "source", "signature"))
        expected = f'{row["system"]}:{row["kind"]}:{hashlib.sha256(key.encode()).hexdigest()[:16]}'
        if id_ != expected:
            errors.append(f"{id_}: unstable/invalid ID (expected {expected})")
        if id_ in ids:
            errors.append(f"duplicate ID {id_}")
        ids.add(id_)
        if row["status"] not in {"TODO", "TESTED", "MAPPED", "REFERENCE_ONLY", "OUT_OF_SCOPE", "BLOCKED", "N-A"}:
            errors.append(f"{id_}: unsupported status {row['status']}")
        if row["kind"] == "DATA_FILE" and row["source_sha256"]:
            errors.append(f"{id_}: restricted database/data file must not be read or hashed")
        if row["status"] in {"TESTED", "N-A"} and not row["evidence"]:
            errors.append(f"{id_}: {row['status']} requires test/rationale evidence")
        if row["kind"] == "CONFIG_KEY" and any(text in row["notes"].lower()
            for text in ("password=", "data source=", "connection string=", "pwd=")):
            errors.append(f"{id_}: never store config values")
    counts = collections.Counter((r["system"], r["kind"]) for r in rows)
    for prefix, expected in REQUIRED_VIEWS.items():
        actual = sum(r["kind"] == "VIEW" and r["source"].startswith(prefix) for r in rows)
        if actual != expected:
            errors.append(f"{prefix}: expected {expected} Razor views, found {actual}")
    for system, test_count in (("ARIA", 47), ("ARIA_ADMIN", 44)):
        if counts[system, "TEST_CASE"] != test_count:
            errors.append(f"{system}: expected {test_count} registered tests, found {counts[system, 'TEST_CASE']}")
    if counts["ARIA_ADMIN", "LEGACY_ACTION_DECISION"] != 343:
        errors.append("Admin: must map all 343 original public actions")
    for system in ("ARIA", "ARIA_ADMIN"):
        for kind in ("TYPE", "METHOD", "ACTION_CANDIDATE", "PROJECT_DEPENDENCY", "CONFIG_KEY"):
            if counts[system, kind] == 0:
                errors.append(f"{system}: no {kind} rows")
    if not any(r["source"] == "TWF 4.28/App/bin/MyTime3.dll" for r in rows):
        errors.append("Original Aria web assembly missing")
    if not any(r["source"] == "T5Web [3.5.7]/App/bin/T5V3Web.dll" for r in rows):
        errors.append("Original Admin web assembly missing")
    print(f"Inventory: {len(rows)} rows, {len(ids)} distinct IDs, "
          f"{counts['ARIA', 'TEST_CASE']}/{counts['ARIA_ADMIN', 'TEST_CASE']} registered tests, "
          f"{counts['ARIA_ADMIN', 'LEGACY_ACTION_DECISION']} original Admin actions")
    print("By status:", dict(collections.Counter(r["status"] for r in rows)))
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--inventory", dest="mode", action="store_const", const="inventory")
    mode.add_argument("--release", dest="mode", action="store_const", const="release")
    parser.add_argument("file", type=pathlib.Path)
    args = parser.parse_args()
    try:
        rows = read(args.file)
    except (OSError, ValueError) as exc:
        print("FAIL:", exc, file=sys.stderr)
        return 2
    errors = check(rows)
    if args.mode == "inventory" and LEDGER.is_file() and args.file.resolve() != LEDGER.resolve():
        frozen = {r["ID"]: r for r in read(LEDGER)}
        generated = {r["ID"]: r for r in rows}
        for id_ in sorted(frozen.keys() ^ generated.keys()):
            errors.append(f"ledger drift (added or removed): {id_}")
        for id_ in sorted(frozen.keys() & generated.keys()):
            a, b = frozen[id_], generated[id_]
            if a["source_sha256"] != b["source_sha256"]:
                errors.append(f"source content drift: {id_}")
    if args.mode == "inventory" and not LEDGER.is_file():
        print("BOOTSTRAP ONLY: LEDGER.csv is not frozen; import generated CI artifact then rerun.")
    if args.mode == "release":
        for row in rows:
            if row["system"] not in {"ARIA", "ARIA_ADMIN"}:  # Link5 is explicitly out of scope.
                continue
            if row["status"] in {"TODO", "BLOCKED", "MAPPED"}:
                errors.append(f"release gap: {row['ID']} {row['source']} {row['status']}")
            if row["status"] == "TESTED" and (not row["destination"] or not row["evidence"]):
                errors.append(f"tested row lacks destination/evidence: {row['ID']}")
        if not LEDGER.is_file():
            errors.append("LEDGER.csv is not committed")
    for error in errors[:30]:
        print("FAIL:", error, file=sys.stderr)
    if len(errors) > 30:
        print(f"...and {len(errors) - 30} more", file=sys.stderr)
    if errors:
        return 1
    print("PASS:", args.mode, "(inventory alone is not a release gate)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
