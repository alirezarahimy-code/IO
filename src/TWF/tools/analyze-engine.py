#!/usr/bin/env python3
"""ARIA — T5V3 engine compatibility analyzer.

The repository ships THREE divergent builds of the T5 engine:

    twf-4.28      TWF 4.28/App/bin        baseline Aria was compiled against
    t5web-3.5.7   T5Web [3.5.7]/App/bin    admin web package
    t5v3-3.9.2    T5V3.9.2/                desktop installer

Aria (the rewritten portal) is a consumer of that engine. Moving Aria to a
different engine build is only safe if every engine type and member Aria calls
still exists there. This tool answers that from real .NET metadata (dnfile).

Checks
------
 1. Build identity (size + md5 per engine assembly).
 2. API surface diff between builds (types/members removed and added).
 3. Aria usage verification, two kinds:
       a) direct manager calls      ->  DailyLeaveDA.GetAll(...)
       b) manager property chains   ->  DB.TitlesCharts.GetAllGuardAccess(...)
 4. Verdict per build: reports *regressions only* (member present in the
    baseline but missing in that build). Members missing in every build are
    heuristic noise (Aria's own types / local variable names) and are listed
    separately instead of poisoning the verdict.

Usage
-----
    python3 tools/analyze-engine.py
    python3 tools/analyze-engine.py --json report.json
    python3 tools/analyze-engine.py --quiet
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
from collections import defaultdict

try:
    import dnfile
except ImportError:  # pragma: no cover
    sys.stderr.write("dnfile is required:  pip install --break-system-packages dnfile\n")
    raise

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))

BUILDS = {
    "twf-4.28": os.path.join(REPO_ROOT, "TWF 4.28", "App", "bin"),
    "t5web-3.5.7": os.path.join(REPO_ROOT, "T5Web [3.5.7]", "App", "bin"),
    "t5v3-3.9.2": os.path.join(REPO_ROOT, "T5V3.9.2"),
}
BASELINE = "twf-4.28"

ENGINE_ASSEMBLIES = [
    ("T5V3.DB.dll", ["T5V3.DB.dll"]),
    ("T5V3.Model.dll", ["T5V3.Model.dll"]),
    ("T5V3.Attendance.dll", ["T5V3.Attendance.dll"]),
    ("T5V3.LeaveCalcs.dll", ["T5V3.LeaveCalcs.dll", "T5V3.LeaveCalcs.DLL"]),
    ("T5V3.Misc.dll", ["T5V3.Misc.dll"]),
    ("T5V3.Report.dll", ["T5V3.Report.dll"]),
    ("T5V3.ReportGenerator.dll", ["T5V3.ReportGenerator.dll"]),
    ("Personnel.dll", ["Personnel.dll", "Personnel.DLL"]),
]

# Manager classes that expose the DA objects as properties.
MANAGER_TYPES = ("T5V3.DB.T5V3DBManager", "Personnel.DB.DBManager")

ARIA_SRC = os.path.join(REPO_ROOT, "src", "TWF")

NOT_ENGINE = {
    "ConfigurationManager", "WebConfigurationManager", "LoginManager",
    "NotificationManager", "ResourceManager", "RandomNumberGenerator",
    "ReportSchemaGenerator", "ConnectionStringResolver", "DBInitializer",
    "GlobalHelper", "DateTime", "Math", "String", "Convert", "TimeSpan",
}

MANAGER_CALL_RE = re.compile(
    r"\b([A-Z][A-Za-z0-9_]*(?:DA|Manager|Calc|Generator|Builder|System))\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)"
)
CHAIN_CALL_RE = re.compile(r"\bDB\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)")


# --------------------------------------------------------------------------- #
# Metadata
# --------------------------------------------------------------------------- #
def md5(path):
    h = hashlib.md5()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def resolve(folder, names):
    for name in names:
        path = os.path.join(folder, name)
        if os.path.exists(path):
            return path
    return None


def load_surface(path):
    """{typeFullName: {'methods': set, 'fields': set, 'props': set}} from metadata."""
    dn = dnfile.dnPE(path)
    md = dn.net.mdtables
    if md is None or md.TypeDef is None:
        return {}

    surface = {}
    for row in md.TypeDef.rows:
        ns = str(row.TypeNamespace or "")
        name = str(row.TypeName or "")
        if name == "<Module>" or name.startswith("<"):
            continue
        full = f"{ns}.{name}" if ns else name
        entry = surface.setdefault(full, {"methods": set(), "fields": set(), "props": set()})

        for idx in (row.MethodList or []):
            rid = idx.row_index
            if rid and md.MethodDef and rid <= len(md.MethodDef.rows):
                mname = str(md.MethodDef.rows[rid - 1].Name or "")
                if not mname:
                    continue
                if mname.startswith("get_") or mname.startswith("set_"):
                    entry["props"].add(mname[4:])
                elif mname not in (".ctor", ".cctor"):
                    entry["methods"].add(mname)

        for idx in (row.FieldList or []):
            rid = idx.row_index
            if rid and md.Field and rid <= len(md.Field.rows):
                fname = str(md.Field.rows[rid - 1].Name or "")
                if fname and not fname.startswith("<"):
                    entry["fields"].add(fname)

    return surface


def engine_only(surface):
    return {k: v for k, v in surface.items()
            if k.split(".")[0] in ("T5V3", "Personnel") and isinstance(v, dict)}


def all_members(entry):
    return set(entry["methods"]) | set(entry["props"]) | set(entry["fields"])


# --------------------------------------------------------------------------- #
# Aria usage
# --------------------------------------------------------------------------- #
def aria_sources():
    texts = []
    for root, _dirs, files in os.walk(ARIA_SRC):
        if "OriginalDecompiled" in root or f"{os.sep}build" in root:
            continue
        for fname in files:
            if fname.endswith(".cs"):
                try:
                    texts.append(open(os.path.join(root, fname), encoding="utf-8", errors="ignore").read())
                except OSError:
                    pass
    return texts


def collect_usage(texts):
    direct = defaultdict(set)   # Type -> {members}
    chain = defaultdict(set)    # Property -> {members}
    for text in texts:
        for m in MANAGER_CALL_RE.finditer(text):
            if m.group(1) not in NOT_ENGINE:
                direct[m.group(1)].add(m.group(2))
        for m in CHAIN_CALL_RE.finditer(text):
            chain[m.group(1)].add(m.group(2))
    return direct, chain


# --------------------------------------------------------------------------- #
# Analysis
# --------------------------------------------------------------------------- #
def analyze():
    report = {"builds": {}, "surface": {}, "diff": {}, "verdict": {}, "errors": []}

    for label, folder in BUILDS.items():
        report["builds"][label] = {}
        report["surface"][label] = {}
        for logical, candidates in ENGINE_ASSEMBLIES:
            path = resolve(folder, candidates)
            if not path:
                report["builds"][label][logical] = None
                continue
            report["builds"][label][logical] = {
                "path": os.path.relpath(path, REPO_ROOT),
                "size": os.path.getsize(path),
                "md5": md5(path),
            }
            try:
                surf = engine_only(load_surface(path))
                report["surface"][label][logical] = {
                    full: {k: sorted(v) for k, v in data.items()} for full, data in surf.items()
                }
            except Exception as exc:  # pragma: no cover
                report["surface"][label][logical] = {}
                report["errors"].append(f"{label}/{logical}: {exc}")

    # ---- surface diff vs baseline
    for label in BUILDS:
        if label == BASELINE:
            continue
        diffs = {}
        for logical, _ in ENGINE_ASSEMBLIES:
            base_s = report["surface"][BASELINE].get(logical) or {}
            othr_s = report["surface"][label].get(logical) or {}
            if not base_s or not othr_s:
                continue
            removed_types, added_types, changed = [], [], {}
            for full, data in base_s.items():
                if full not in othr_s:
                    removed_types.append(full)
                    continue
                bm, om = all_members(data), all_members(othr_s[full])
                if bm - om or om - bm:
                    changed[full] = {"removed": sorted(bm - om), "added": sorted(om - bm)}
            for full in othr_s:
                if full not in base_s:
                    added_types.append(full)
            diffs[logical] = {"removed_types": sorted(removed_types),
                              "added_types": sorted(added_types),
                              "changed_types": changed}
        report["diff"][f"{BASELINE} -> {label}"] = diffs

    # ---- Aria usage verification
    texts = aria_sources()
    direct, chain = collect_usage(texts)
    report["aria_usage"] = {
        "direct": {k: sorted(v) for k, v in sorted(direct.items())},
        "chains": {k: sorted(v) for k, v in sorted(chain.items())},
    }

    def locate(label, type_name):
        """Return (logical, fullName, entry) for a type simple name in a build."""
        for logical, _ in ENGINE_ASSEMBLIES:
            for full, data in (report["surface"][label].get(logical) or {}).items():
                if full.split(".")[-1] == type_name:
                    return logical, full, data
        return None, None, None

    # (a) direct manager calls
    direct_matrix = {}
    for type_name, members in sorted(direct.items()):
        per_build = {}
        for label in BUILDS:
            logical, full, entry = locate(label, type_name)
            if entry is None:
                per_build[label] = {"state": "absent", "missing": sorted(members)}
            else:
                avail = all_members(entry)
                missing = sorted(m for m in members if m not in avail)
                per_build[label] = {"state": "ok" if not missing else "partial",
                                    "assembly": logical, "type": full, "missing": missing}
        direct_matrix[type_name] = per_build
    report["direct_matrix"] = direct_matrix

    # (b) DB.<Prop>.<Member> chains — resolve the member against engine types and
    #     require the manager to expose the property.
    chain_matrix = {}
    for prop, members in sorted(chain.items()):
        per_build = {}
        for label in BUILDS:
            manager_has_prop = False
            for mtype in MANAGER_TYPES:
                _, _, entry = locate(label, mtype.split(".")[-1])
                if entry and prop in all_members(entry):
                    manager_has_prop = True
                    break

            resolved = {}
            for member in members:
                owners = []
                for logical, _ in ENGINE_ASSEMBLIES:
                    for full, data in (report["surface"][label].get(logical) or {}).items():
                        if member in all_members(data):
                            owners.append(full)
                resolved[member] = sorted(set(owners))

            missing = [m for m, owners in resolved.items() if not owners]
            per_build[label] = {
                "manager_has_property": manager_has_prop,
                "state": "ok" if (manager_has_prop and not missing) else "partial",
                "missing": missing,
                "resolved_to": {m: o[:3] for m, o in resolved.items() if o},
            }
        chain_matrix[prop] = per_build
    report["chain_matrix"] = chain_matrix

    # ---- verdict: regressions only
    for label in BUILDS:
        regressions = {}

        for type_name, per_build in direct_matrix.items():
            base_state = per_build[BASELINE]
            if base_state["state"] == "absent":
                continue  # heuristic noise, absent everywhere
            cur = per_build[label]
            lost = sorted(set(base_state.get("missing", [])) ^ set(cur.get("missing", [])))
            newly_missing = [m for m in base_state.get("missing", []) if m not in cur.get("missing", [])]
            base_missing = set(base_state.get("missing", []))
            cur_missing = set(cur.get("missing", []))
            gaps = sorted(cur_missing - base_missing)
            if gaps:
                regressions[type_name] = {"kind": "direct", "missing": gaps}

        for prop, per_build in chain_matrix.items():
            base = per_build[BASELINE]
            cur = per_build[label]
            base_missing = set(base.get("missing", []))
            cur_missing = set(cur.get("missing", []))
            gaps = sorted(cur_missing - base_missing)
            lost_prop = base["manager_has_property"] and not cur["manager_has_property"]
            if gaps or lost_prop:
                regressions[f"DB.{prop}"] = {
                    "kind": "chain",
                    "missing": gaps,
                    "property_lost": lost_prop,
                }

        report["verdict"][label] = {
            "compatible": not regressions,
            "regressions": regressions,
            "types_checked": len(direct_matrix),
            "chains_checked": len(chain_matrix),
        }

    return report


# --------------------------------------------------------------------------- #
# Output
# --------------------------------------------------------------------------- #
def print_report(report):
    W = 92
    print("=" * W)
    print("  ARIA — T5V3 engine compatibility report")
    print("=" * W)

    print("\n1) BUILD IDENTITY")
    for label, entries in report["builds"].items():
        print(f"\n   {label}")
        for logical, meta in entries.items():
            if not meta:
                print(f"     {logical:26s} (not shipped)")
            else:
                print(f"     {logical:26s} {meta['size']:>9,} B  {meta['md5']}")

    print("\n2) API SURFACE DIFF  (baseline = %s)" % BASELINE)
    for key, diffs in report["diff"].items():
        print(f"\n   {key}")
        for logical, d in diffs.items():
            rm_t, add_t = len(d["removed_types"]), len(d["added_types"])
            rm_m = sum(len(v["removed"]) for v in d["changed_types"].values())
            add_m = sum(len(v["added"]) for v in d["changed_types"].values())
            if not (rm_t or add_t or rm_m or add_m):
                print(f"     {logical:26s} identical API surface")
                continue
            flag = "   <-- BREAKING" if rm_m or rm_t else ""
            print(f"     {logical:26s} -{rm_t} types +{add_t} types  "
                  f"-{rm_m} mbr +{add_m} mbr{flag}")
            for t in d["removed_types"]:
                print(f"        removed type   : {t}")
            for t, ch in d["changed_types"].items():
                if ch["removed"]:
                    print(f"        {t}: -{', '.join(ch['removed'][:8])}")

    print("\n3) ARIA USAGE — direct manager calls")
    builds = list(BUILDS)
    print(f"   {'type':30s}" + "".join(f"{b:>19s}" for b in builds))
    print("   " + "-" * (30 + 19 * len(builds)))
    for type_name, per_build in report["direct_matrix"].items():
        cells = []
        all_absent = all(per_build[b]["state"] == "absent" for b in builds)
        for b in builds:
            info = per_build[b]
            if info["state"] == "absent":
                cells.append("absent")
            elif info["missing"]:
                cells.append(f"-{len(info['missing'])}")
            else:
                cells.append("ok")
        suffix = "   (heuristic, absent everywhere)" if all_absent else ""
        print(f"   {type_name:30s}" + "".join(f"{c:>19s}" for c in cells) + suffix)

    print("\n4) ARIA USAGE — manager property chains  DB.<Prop>.<Member>")
    print(f"   {'property':30s}" + "".join(f"{b:>19s}" for b in builds))
    print("   " + "-" * (30 + 19 * len(builds)))
    for prop, per_build in report["chain_matrix"].items():
        cells = []
        for b in builds:
            info = per_build[b]
            if not info["manager_has_property"]:
                cells.append("no-property")
            elif info["missing"]:
                cells.append(f"-{len(info['missing'])}")
            else:
                cells.append("ok")
        print(f"   {prop:30s}" + "".join(f"{c:>19s}" for c in cells))

    print("\n5) VERDICT  (regressions vs baseline only)")
    for label, v in report["verdict"].items():
        mark = "COMPATIBLE" if v["compatible"] else "INCOMPATIBLE"
        print(f"   {label:16s} {mark}   ({v['types_checked']} types, {v['chains_checked']} chains checked)")
        for name, info in v["regressions"].items():
            detail = ", ".join(info["missing"]) if info["missing"] else ""
            prop = " property lost" if info.get("property_lost") else ""
            print(f"       BREAK: {name}{prop} {detail}")
    print()


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--json", help="write full report as JSON")
    ap.add_argument("--quiet", action="store_true", help="verdict only")
    args = ap.parse_args()

    report = analyze()
    if args.json:
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump(report, fh, indent=2, ensure_ascii=False)
        print(f"JSON report written to {args.json}")

    if args.quiet:
        for label, v in report["verdict"].items():
            print(f"{label}: {'COMPATIBLE' if v['compatible'] else 'INCOMPATIBLE'}")
    else:
        print_report(report)

    return 0 if any(v["compatible"] for v in report["verdict"].values()) else 1


if __name__ == "__main__":
    sys.exit(main())
