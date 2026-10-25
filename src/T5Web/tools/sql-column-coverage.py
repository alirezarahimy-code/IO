#!/usr/bin/env python3
"""Column coverage gate: every `alias.[Column]` in the data layer must be in the model.

The Windows CI found this class of bug four times in a row: a statement named
`q.[CreateDate]` or `s.[SchName]`, the column existed in the engine but not in
`engine/column-types.json` (its ORM entity does not expose it), and the synthetic
schema therefore could not compile the statement. Each round cost a CI run to discover
one batch of names.

This gate does the same lookup offline: it resolves the table aliases in every SQL
literal of `T5Web.Data`, and reports any column the typed model does not know. That is
not a type check - `sql-smoke.py --server` on the Windows runner remains the proof - but
it turns "one CI run per missing column" into "one build".

    python3 tools/sql-column-coverage.py           # the gate
    python3 tools/sql-column-coverage.py --list    # every resolved reference
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
TYPES = os.path.join(ROOT, "engine", "column-types.json")
MANIFEST = os.path.join(ROOT, "..", "TWF", "db", "schema-manifest.json")
DATA = os.path.join(ROOT, "T5Web.Data")
PORTAL_DDL = os.path.join(ROOT, "db", "01-t5web-objects.sql")

# FROM/JOIN [Table] [alias] - enough for the flat statements this layer writes.
TABLE_RE = re.compile(r"(?i)\b(?:FROM|JOIN)\s+\[?(\w+)\]?(?:\s+(?:AS\s+)?(\w+))?")
COLUMN_RE = re.compile(r"\b(\w+)\.\[(\w+)\]")
NOT_TABLES = {"select", "where", "on", "and", "or", "in", "exists", "as", "join"}


def sql_literals(text):
    """Verbatim and ordinary C# string literals that look like SQL."""
    for match in re.finditer(r'@"((?:[^"]|"")*)"', text, re.S):
        yield match.group(1).replace('""', '"')
    for match in re.finditer(r'"((?:[^"\\]|\\.)*)"', text):
        body = match.group(1)
        if re.search(r"(?i)\b(select|insert\s+into|update\s+\w|delete\s+from)\b", body):
            yield body


def portal_tables():
    """{table: {column}} from the portal's own DDL.

    T5WebAccess / T5WebAudit / T5WebDeviceCommand are created by db/01, not by the
    engine, so they are absent from engine/column-types.json by design. Their columns
    are read from the DDL rather than hard-coded here - otherwise this gate would need
    editing every time a column is added to the audit trail.
    """
    try:
        with open(PORTAL_DDL, encoding="utf-8") as handle:
            text = handle.read()
    except OSError:
        return {}

    tables = {}
    for match in re.finditer(r"CREATE TABLE\s+dbo\.(\w+)\s*\((.*?)\n\s*\);", text, re.S):
        name = match.group(1)
        columns = set()
        for line in match.group(2).splitlines():
            line = line.strip()
            if not line or line.upper().startswith(("CONSTRAINT", "PRIMARY", "UNIQUE", ")")):
                continue
            column = re.match(r"\[?(\w+)\]?\s+\w", line)
            if column:
                columns.add(column.group(1))
        tables[name] = columns
    return tables


def references():
    """Yield (table, column, file, line) for every resolved alias.[Column]."""
    for name in sorted(os.listdir(DATA)):
        if not name.endswith(".cs"):
            continue
        path = os.path.join(DATA, name)
        with open(path, encoding="utf-8") as handle:
            text = handle.read()
        for literal in sql_literals(text):
            aliases = {}
            for match in TABLE_RE.finditer(literal):
                table = match.group(1)
                if table.lower() in NOT_TABLES:
                    continue
                aliases[(match.group(2) or table).lower()] = table
            for match in COLUMN_RE.finditer(literal):
                table = aliases.get(match.group(1).lower())
                if not table:
                    continue
                line = text.count("\n", 0, text.find(literal)) + 1
                yield table, match.group(2), name, line


def load(path):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def main(argv):
    model = load(TYPES)["tables"]
    portal = portal_tables()
    try:
        manifest_raw = load(MANIFEST)["tables"]
    except (OSError, KeyError, ValueError):
        manifest_raw = {}
    manifest = {}
    for table, detail in manifest_raw.items():
        listed = detail if isinstance(detail, list) else detail.get("columns", [])
        manifest[table] = {column.lower() for column in listed}

    seen = []
    problems = []
    for table, column, filename, line in references():
        entry = (table, column, filename, line)
        if entry in seen:
            continue
        seen.append(entry)
        if table not in model:
            if column in portal.get(table, set()):
                continue  # portal-owned table, proven by db/01
            problems.append("%s.%s (%s:%d): neither the typed model nor db/01 knows it"
                            % (table, column, filename, line))
            continue
        if column in model[table]["columns"]:
            continue
        if column.lower() in manifest.get(table, set()):
            problems.append("%s.%s (%s:%d): the engine has it (manifest) but the model "
                            "does not - add it to EXTRA_COLUMNS in "
                            "tools/engine-column-types.py" % (table, column, filename, line))
        else:
            problems.append("%s.%s (%s:%d): not in the model and not in the manifest - "
                            "this is a typo" % (table, column, filename, line))

    if "--list" in argv:
        for table, column, filename, line in seen:
            print("%s.%s\t%s:%d" % (table, column, filename, line))
        print("%d reference(s)" % len(seen))
        return 0

    if problems:
        print("FAIL: SQL references columns the model does not have")
        for problem in sorted(set(problems)):
            print("  - " + problem)
        return 1

    print("OK: %d column reference(s) in the data layer resolve to the typed model" % len(seen))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
