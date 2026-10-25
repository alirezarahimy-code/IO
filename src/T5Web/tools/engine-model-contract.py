#!/usr/bin/env python3
"""Generate engine/model-contract.json — the engine model members the portal reads.

Why
---
The portal now reads organisational data through the engine's own DA layer, so C#
property names must match the *engine model* (Personnel.Model.*), not the database
columns. Those two differ in places that matter (`Department.Name` is the column
`DepartmentName`; `Device.SerialNumber` is the column `SerialNo`).

This tool records, from the pinned build, exactly which model members exist, so a
typo like `d.DepartmentName` fails a test instead of an operator's screen.

Usage:
    python3 tools/engine-model-contract.py           # rewrite the contract
    python3 tools/engine-model-contract.py --check   # fail when stale
"""

from __future__ import annotations

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TWF = os.path.abspath(os.path.join(HERE, "..", "..", "TWF"))            # src/TWF
REPO = os.path.abspath(os.path.join(TWF, "..", ".."))
PIN = os.path.join(TWF, "engine", "engine-pin.json")
OUT = os.path.join(os.path.abspath(os.path.join(HERE, "..")), "engine", "model-contract.json")

# Model types the portal consumes, and where each one is used.
CONSUMED = {
    "Department": "T5Web.Data/Engine/PersonnelEngineDirectory.cs (GetDepartments)",
    "TitleChart": "T5Web.Data/Engine/PersonnelEngineDirectory.cs (GetTitles)",
    "EmpType": "T5Web.Data/Engine/PersonnelEngineDirectory.cs (GetEmpTypes)",
    "Device": "T5Web.Data/Engine/PersonnelEngineDirectory.cs (GetDevices)",
}


def blob(item):
    for attr in ("value", "data", "blob"):
        if hasattr(item, attr):
            value = getattr(item, attr)
            if isinstance(value, (bytes, bytearray)):
                return bytes(value)
    return bytes(item)


def model_members(assembly_path, wanted):
    import dnfile

    dn = dnfile.dnPE(assembly_path)
    tables = dn.net.mdtables
    type_rows = list(tables.TypeDef.rows)
    found = {}

    for index, row in enumerate(type_rows):
        name = str(row.TypeName or "")
        namespace = str(row.TypeNamespace or "")
        if not namespace.startswith("Personnel.Model") or name not in wanted:
            continue

        start = row.MethodList[0].row_index if row.MethodList else 0
        if not start:
            continue
        end = (type_rows[index + 1].MethodList[0].row_index
               if index + 1 < len(type_rows) and type_rows[index + 1].MethodList
               else len(tables.MethodDef.rows) + 1)

        members = set()
        for rid in range(start, end):
            method = tables.MethodDef.rows[rid - 1]
            mname = str(method.Name or "")
            if mname.startswith("get_"):
                members.add(mname[4:])
            elif mname.startswith("set_"):
                members.add(mname[4:])
            elif mname and not mname.startswith((".", "<")):
                members.add(mname)
        found[name] = sorted(members)

    return found


def main(argv):
    if not os.path.exists(PIN):
        print("ERROR: engine/engine-pin.json missing")
        return 2
    pin = json.load(open(PIN, encoding="utf-8"))
    folder = os.path.join(REPO, pin["pinned_from"])
    assembly = os.path.join(folder, "Personnel.dll")
    if not os.path.exists(assembly):
        print("ERROR: pinned Personnel.dll not found at " + assembly)
        return 2

    members = model_members(assembly, set(CONSUMED))
    missing = sorted(set(CONSUMED) - set(members))
    if missing:
        print("ERROR: model types missing from the pinned build: " + ", ".join(missing))
        return 1

    payload = {
        "description": (
            "Engine model members consumed by the T5Web portal. Generated from the pinned "
            "engine build; do not edit by hand. Used by T14 to catch a property name that "
            "does not exist in the engine model (a class of bug that only fails at runtime)."
        ),
        "generated_by": "tools/engine-model-contract.py",
        "engine_build": pin["pinned_build"],
        "assembly": "Personnel.dll",
        "types": {
            name: {"used_by": CONSUMED[name], "members": members[name]}
            for name in sorted(members)
        },
    }
    text = json.dumps(payload, indent=2, ensure_ascii=False) + "\n"

    if "--check" in argv:
        current = open(OUT, encoding="utf-8").read() if os.path.exists(OUT) else ""
        if current != text:
            print("STALE: engine/model-contract.json does not match the pinned build.")
            print("       run: python3 tools/engine-model-contract.py")
            return 1
        print("OK: engine/model-contract.json is current (%d types)" % len(payload["types"]))
        return 0

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as handle:
        handle.write(text)
    print("wrote engine/model-contract.json: %d types, %d members"
          % (len(payload["types"]), sum(len(t["members"]) for t in payload["types"].values())))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
