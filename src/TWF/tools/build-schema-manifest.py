#!/usr/bin/env python3
"""Build db/schema-manifest.json — the ground truth about the T5 schema.

Sources, in order of authority:

  1. `Link5 v4.0.4/Personnel.sql`  -> vendor DDL (CREATE TABLE + columns)
  2. `T5Web [3.5.7]/App/bin/T5V3.DB.dll` -> EntityClasses properties
     (the ORM is hand-mapped 1:1 onto columns, so property names are columns)
  3. `src/TWF/TD5/**` -> bracket-quoted SQL written by the *working* legacy
     data layer (this is where the real, sometimes misspelled, column names
     such as `WofkflowType` and `NationalNo` come from)
  4. `src/TWF/OriginalDecompiled/MyTime3/**` -> model classes for app tables

The manifest is evidence, not documentation: every table/column records where
it was seen. `tools/verify-sql-schema.py` then rejects SQL identifiers that are
not in it, which is how the "UserInfos"/"NationalCode"/"[RequestDate]" class of
bug is caught before it reaches a production shift.

Usage:
    python3 tools/build-schema-manifest.py            # rewrite the manifest
    python3 tools/build-schema-manifest.py --check    # fail if it is stale
"""

from __future__ import annotations

import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TWF = os.path.abspath(os.path.join(HERE, ".."))
REPO = os.path.abspath(os.path.join(TWF, "..", ".."))
OUT = os.path.join(TWF, "db", "schema-manifest.json")

VENDOR_SQL = [
    os.path.join(REPO, "Link5 v4.0.4", "Personnel.sql"),
    os.path.join(REPO, "Link5 v4.0.4", "Link5.sql"),
]
ENGINE_DB = os.path.join(REPO, "T5Web [3.5.7]", "App", "bin", "T5V3.DB.dll")
SQL_SOURCES = [os.path.join(TWF, "TD5"), os.path.join(TWF, "TWF.Infrastructure", "Data")]

# Types whose properties are container/navigation properties, not columns.
NAVIGATION_SUFFIX = (
    "Accepts", "Accesses", "Charts", "Datas", "Tablixes", "Textboxes",
    "Holidays", "Schedules", "Points", "Steps", "Rules", "AppRules",
    "PositionDatas", "Request",
)

TABLE_RE = re.compile(r"CREATE\s+TABLE\s+\[dbo\]\.\[([A-Za-z_][A-Za-z0-9_]*)\]\s*\(", re.I)
COLUMN_RE = re.compile(r"^\s*\[([A-Za-z_][A-Za-z0-9_]*)\]\s*\[", re.M)


def read_text(path):
    with open(path, "rb") as fh:
        raw = fh.read()
    if raw[:2] in (b"\xff\xfe", b"\xfe\xff"):
        return raw.decode("utf-16", errors="ignore")
    for enc in ("utf-8-sig", "utf-8", "cp1256", "latin-1"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1")


def from_vendor_sql(manifest):
    for path in VENDOR_SQL:
        if not os.path.exists(path):
            continue
        sql = read_text(path)
        matches = list(TABLE_RE.finditer(sql))
        for i, m in enumerate(matches):
            table = m.group(1)
            end = matches[i + 1].start() if i + 1 < len(matches) else len(sql)
            body = sql[m.end():end]
            body = body.split("CONSTRAINT")[0]
            cols = COLUMN_RE.findall(body)
            entry = manifest.setdefault(table, {"columns": set(), "sources": set()})
            entry["columns"].update(cols)
            entry["sources"].add("Link5 v4.0.4/*.sql")


def from_engine_metadata(manifest):
    try:
        import dnfile
    except ImportError:
        print("NOTE: dnfile missing - engine metadata not read")
        return
    if not os.path.exists(ENGINE_DB):
        print("NOTE: engine assembly missing - engine metadata not read")
        return

    dn = dnfile.dnPE(ENGINE_DB)
    md = dn.net.mdtables
    for row in md.TypeDef.rows:
        ns = str(row.TypeNamespace or "")
        name = str(row.TypeName or "")
        if "EntityClasses" not in ns or name.startswith("<"):
            continue
        props = []
        for idx in (row.MethodList or []):
            rid = idx.row_index
            if rid and md.MethodDef and rid <= len(md.MethodDef.rows):
                method = str(md.MethodDef.rows[rid - 1].Name or "")
                if method.startswith("get_") and not method.startswith("get_Item"):
                    prop = method[4:]
                    if prop in props:
                        continue
                    if prop.endswith(NAVIGATION_SUFFIX):
                        continue
                    props.append(prop)
        if not props:
            continue
        entry = manifest.setdefault(name, {"columns": set(), "sources": set()})
        entry["columns"].update(props)
        entry["sources"].add("T5V3.DB.dll EntityClasses")


def _sql_literals(text):
    """Yield SQL-ish string literals from C# source (verbatim and regular)."""
    for m in re.finditer(r'@"((?:[^"]|"")*)"', text, re.S):
        yield m.group(1).replace('""', '"')
    for m in re.finditer(r'"((?:[^"\\]|\\.)*)"', text):
        body = m.group(1)
        if re.search(r"(?i)\b(select|insert\s+into|update|delete\s+from)\b", body):
            yield body.replace("\\\"", '"')


TABLE_DECL = re.compile(
    r"(?i)\b(?:FROM|JOIN|INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+\[?([A-Za-z_][A-Za-z0-9_]*)\]?"
    r"(?:\s+(?:AS\s+)?([A-Za-z_][A-Za-z0-9_]*))?")
QUALIFIED = re.compile(r"\b([A-Za-z_][A-Za-z0-9_]*|\[[A-Za-z_][A-Za-z0-9_]*\])\s*\.\s*\[([A-Za-z_][A-Za-z0-9_]*)\]")
BRACKETED = re.compile(r"\[([A-Za-z_][A-Za-z0-9_]*)\]")
SQL_KEYWORDS = {
    "select", "from", "where", "join", "left", "right", "inner", "outer", "on",
    "and", "or", "not", "null", "is", "as", "order", "by", "group", "having",
    "insert", "into", "values", "update", "set", "delete", "top", "case", "when",
    "then", "else", "end", "exists", "in", "like", "count", "sum", "max", "min",
    "isnull", "coalesce", "cast", "int", "bigint", "nvarchar", "datetime", "bit",
    "dbo", "desc", "asc", "distinct", "union", "all",
}


def _parse_literal(literal):
    """Return {table: set(columns)} for one SQL literal, resolving aliases."""
    tables = {}
    for m in TABLE_DECL.finditer(literal):
        name = m.group(1)
        alias = m.group(2)
        if name.lower() in SQL_KEYWORDS:
            continue
        tables.setdefault(name, set())
        if alias and alias.lower() not in SQL_KEYWORDS:
            tables.setdefault("@" + alias, name)

    resolved = {t: set() for t in tables if not t.startswith("@")}
    aliases = {a[1:]: t for a, t in tables.items() if a.startswith("@")}

    for m in QUALIFIED.finditer(literal):
        owner = m.group(1).strip("[]")
        column = m.group(2)
        table = aliases.get(owner, owner if owner in resolved else None)
        if table:
            resolved.setdefault(table, set()).add(column)

    # Unqualified columns are only attributed when the literal names one table.
    real_tables = [t for t in resolved if t]
    if len(real_tables) == 1:
        only = real_tables[0]
        for column in BRACKETED.findall(literal):
            if column == only or column.lower() in SQL_KEYWORDS:
                continue
            resolved[only].add(column)
    return resolved


def from_legacy_sql(manifest):
    """Columns written by the *legacy* data layer - the app that works in production.

    Deliberately excludes the rewritten Aria layer: using our own SQL as evidence
    would make any invented column look legitimate.
    """
    # The decompiled legacy tree is intentionally NOT used: it still contains the
    # old copy-paste bug that inserted LeaveTypes columns into [UserRules], and a
    # manifest built from it would bless that bug as schema truth.
    sources = [os.path.join(TWF, "TD5")]

    for base in sources:
        for root, _dirs, names in os.walk(base):
            for name in names:
                if not name.endswith(".cs"):
                    continue
                for literal in _sql_literals(read_text(os.path.join(root, name))):
                    for table, columns in _parse_literal(literal).items():
                        entry = manifest.setdefault(table, {"columns": set(), "sources": set()})
                        entry["columns"].update(columns)
                        entry["sources"].add("legacy TD5 SQL")


def main(argv):
    manifest = {}
    from_vendor_sql(manifest)
    from_engine_metadata(manifest)
    from_legacy_sql(manifest)

    # Aria-owned table (not part of the T5 schema) is documented explicitly.
    entry = manifest.setdefault("PassCheckProps", {"columns": set(), "sources": set()})
    entry["columns"].update(["ID", "Badge", "LastChangeDate"])
    entry["sources"].add("db/01-app-objects.sql")

    payload = {
        "description": (
            "Ground truth for the shared T5 schema. Built by "
            "tools/build-schema-manifest.py from vendor DDL, engine ORM metadata and "
            "the legacy (working) data layer. Hand-editing this file is not allowed."
        ),
        "generated_by": "tools/build-schema-manifest.py",
        "section": "|".join(s for s in ("vendor-ddl", "engine-orm", "legacy-sql")),
        "tables": {
            name: {
                "columns": sorted(data["columns"]),
                "sources": sorted(data["sources"]),
            }
            for name, data in sorted(manifest.items())
            if data["columns"]
        },
    }
    text = json.dumps(payload, indent=2, ensure_ascii=False) + "\n"

    if "--check" in argv:
        current = open(OUT, encoding="utf-8").read() if os.path.exists(OUT) else ""
        if current != text:
            print("STALE: db/schema-manifest.json does not match its sources.")
            print("       run: python3 tools/build-schema-manifest.py")
            return 1
        print(f"OK: db/schema-manifest.json is current ({len(payload['tables'])} tables)")
        return 0

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as fh:
        fh.write(text)
    print(f"wrote {os.path.relpath(OUT, TWF)}: {len(payload['tables'])} tables, "
          f"{sum(len(t['columns']) for t in payload['tables'].values())} columns")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
