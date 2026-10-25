#!/usr/bin/env python3
"""Grant gate: every table the portal writes must be writable by the portal's role.

db/02 grants privileges table by table. A new feature adds a write statement to
T5Web.Data, the screen renders, and saving fails on the server with

    The UPDATE permission was denied on the object 'UserInfo'

The render is what the acceptance checklist checks, so nothing notices until an
administrator clicks save (BUG-43). This gate reads the write statements out of the
data layer, works out which catalog each table lives in (engine/relations.json, plus
the portal's own T5Web* tables), and requires db/02 to grant the matching verb in
that catalog - or to say, explicitly, why it refuses it.

    python3 tools/db-grant-gate.py
"""
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

# A refusal is part of the design only when it is written down. Keys are
# (catalog, table, verb); the value is the reason a reader can check.
REFUSED = {
    ("ReportT5V3", "T5WebAudit", "UPDATE"): "the audit trail is append-only (DENY below)",
    ("ReportT5V3", "T5WebAudit", "DELETE"): "the audit trail is append-only (DENY below)",
    ("ReportT5V3", "T5WebAccess", "DELETE"): "the permission matrix is not deletable",
}

VERB_OF = {"INSERT": "INSERT", "UPDATE": "UPDATE", "DELETE": "DELETE"}

WRITE = re.compile(r"\b(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+\[?(?:dbo\]?\.\[)?(\w+)\]?", re.I)


def catalogs():
    relations = json.load(io.open(os.path.join(ROOT, "engine", "relations.json"), encoding="utf-8"))
    table_to_catalog = {}
    for table, info in relations["databases"].items():
        key = info.get("database")
        name = {"t5": "ReportT5V3", "personnel": "Personnel", "portal": "ReportT5V3",
                "device": "Link5 service database"}.get(key, key)
        table_to_catalog[table] = name
    return table_to_catalog


def write_targets():
    """(catalog-hint-unknown) -> {table: {verbs}} from the SQL in the data layer."""
    targets = {}
    for name in sorted(os.listdir(os.path.join(ROOT, "T5Web.Data"))):
        if not name.endswith(".cs") or not name.startswith("Sql"):
            continue
        text = io.open(os.path.join(ROOT, "T5Web.Data", name), encoding="utf-8-sig").read()
        for match in WRITE.finditer(text):
            statement = match.group(0)
            verb = re.match(r"\w+", statement).group(0).upper()
            if verb == "INSERT":
                verb = "INSERT"
            table = match.group(1)
            if table.upper() in ("SET", "WHERE", "SELECT"):
                continue  # "UPDATE ... SET" caught mid-statement from a subquery
            targets.setdefault(table, set()).add(VERB_OF.get(verb, verb))
    return targets


def parse_grants():
    """catalog -> {table: {'GRANT': verbs, 'DENY': verbs}} for the role [t5web_app]."""
    path = os.path.join(ROOT, "db", "02-t5web-sql-login.sql")
    text = io.open(path, encoding="utf-8-sig").read()
    # Join statements so a GRANT split across lines is still one statement.
    statements = []
    pending = ""
    for line in text.split("\n"):
        stripped = line.strip()
        if not stripped or stripped.startswith("--"):
            continue
        # A privilege statement always starts at the beginning of its line. Anything
        # before it belongs to an IF...BEGIN block and must not be glued on, or the
        # USE statement that sets the catalog gets swallowed by it.
        if re.match(r"(GRANT|DENY|USE)\b", stripped, re.I):
            pending = ""
        pending = (pending + " " + stripped).strip()
        if pending.endswith(";") or stripped.upper() == "GO":
            statements.append(pending)
            pending = ""

    book = {}
    catalog = None
    pattern = re.compile(r"^(GRANT|DENY)\s+([A-Z, ]+?)\s+ON\s+OBJECT::\[dbo\]\.\[(\w+)\]\s+TO\s+\[(\w+)\]", re.I)
    for statement in statements:
        use = re.match(r"USE\s+\[(\w+)\]", statement, re.I)
        if use:
            catalog = use.group(1)
            continue
        match = pattern.match(statement)
        if not match or match.group(4).lower() != "t5web_app":
            continue
        verb = match.group(1).upper()
        verbs = {part.strip().upper() for part in match.group(2).split(",") if part.strip()}
        entry = book.setdefault(catalog, {}).setdefault(match.group(3), {"GRANT": set(), "DENY": set()})
        entry[verb] |= verbs
    return book


def main():
    table_to_catalog = catalogs()
    book = parse_grants()
    targets = write_targets()
    problems = []

    checked = 0
    for table, verbs in sorted(targets.items()):
        catalog = table_to_catalog.get(table)
        if catalog is None:
            # A portal-owned table (T5WebDeviceCommand and friends) lives in ReportT5V3;
            # anything else unknown means the gate cannot prove the grant, so say so.
            catalog = "ReportT5V3" if table.startswith("T5Web") else None
        if catalog is None:
            problems.append("%s is written by the data layer but no catalog owns it in "
                            "engine/relations.json, so its grant cannot be checked" % table)
            continue
        for verb in sorted(verbs):
            checked += 1
            grant = book.get(catalog, {}).get(table, {"GRANT": set(), "DENY": set()})
            if verb in grant["DENY"]:
                if (catalog, table, verb) not in REFUSED:
                    problems.append("db/02 denies %s on [%s].[%s] but the data layer still does it"
                                    % (verb, catalog, table))
                continue
            if verb in grant["GRANT"]:
                continue
            if (catalog, table, verb) in REFUSED:
                continue
            problems.append("the data layer runs %s on [%s].[%s] but db/02 grants [t5web_app] no %s "
                            "there - the screen renders and saving fails with 'permission was denied'"
                            % (verb, catalog, table, verb))

    if checked < 10:
        problems.append("only %d write statements were found in T5Web.Data; the scan is broken" % checked)

    if problems:
        print("FAIL: grants cover the writes the portal performs")
        for problem in problems:
            print("  - " + problem)
        return 1

    print("OK: %d write statements, %d tables, every verb granted in its own catalog"
          % (checked, len(targets)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
