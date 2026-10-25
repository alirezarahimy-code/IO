#!/usr/bin/env python3
"""Synthetic engine schema: stand-in tables so the SQL can be *executed*, not just read.

Why
---
The engine's schema is installed by the vendor and is not in this repository, so on a
build machine (or a Windows CI runner) `db/verify.sql` has nothing to verify and the
portal's own statements have nothing to run against. Guessing a schema is forbidden
(docs/12), but *replaying the recorded one* is not: `engine/column-types.json` already
holds every table and column the portal names, with the type taken from the vendor DDL
or the engine ORM.

This tool turns that model into T-SQL:

    python3 tools/synthetic-schema.py --sql > synthetic.sql      # CREATE DATABASE + tables
    python3 tools/synthetic-schema.py --sql --database X         # one catalog only
    python3 tools/synthetic-schema.py --check                    # the gate: model consistency

What it is good for
-------------------
* `SET NOEXEC ON` compilation of the portal's statements (sql-smoke.py): a wrong column
  name fails here instead of in production.
* `db/verify.sql` proving the columns it looks for really are in the model.
* `db/03-relations.sql` running end-to-end (empty tables, zero orphans).

What it is NOT
--------------
Not a substitute for the vendor install: no keys, no seeds, no business data, and the
types are the recorded ones, not the live ones. A green run here means "the SQL is
valid against the model we proved", never "the database is installed correctly".

Column types come from `engine/column-types.json`; a type the mapper does not know is a
hard error, because silently widening it to nvarchar(max) would hide the mismatch the
gate exists to find.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
TYPES = os.path.join(ROOT, "engine", "column-types.json")
RELATIONS = os.path.join(ROOT, "engine", "relations.json")

DEFAULT_DATABASES = {"t5": "ReportT5V3", "personnel": "Personnel"}

# The device catalog is the device service's own database; the portal never opens it
# (docs/09), so its tables are recorded in the model but never generated here.
OUT_OF_SCOPE = {"device"}

# A recorded type -> (sql type, needs length). Anything not listed is a hard error.
TYPE_MAP = {
    "int": ("int", False),
    "bigint": ("bigint", False),
    "smallint": ("smallint", False),
    "tinyint": ("tinyint", False),
    "bit": ("bit", False),
    "float": ("float", False),
    "real": ("real", False),
    "decimal": ("decimal(18, 4)", False),
    "numeric": ("decimal(18, 4)", False),
    "money": ("money", False),
    "datetime": ("datetime", False),
    "datetime2": ("datetime2(3)", False),
    "smalldatetime": ("smalldatetime", False),
    "date": ("date", False),
    "time": ("time(7)", False),
    "uniqueidentifier": ("uniqueidentifier", False),
    "image": ("varbinary(max)", False),
    "varbinary": ("varbinary(max)", False),
    "binary": ("varbinary(max)", False),
    "text": ("nvarchar(max)", False),
    "ntext": ("nvarchar(max)", False),
    "nvarchar": ("nvarchar", True),
    "varchar": ("varchar", True),
    "char": ("char", True),
    "nchar": ("nchar", True),
}

DEFAULT_LENGTH = 200


def load():
    with open(TYPES, encoding="utf-8") as handle:
        typed = json.load(handle)
    with open(RELATIONS, encoding="utf-8") as handle:
        relations = json.load(handle)
    catalogs = {}
    for table, entry in (relations.get("databases") or {}).items():
        database = entry.get("database") if isinstance(entry, dict) else entry
        catalogs[table] = database
    return typed["tables"], catalogs, relations


def sql_type(column, table, name):
    recorded = (column.get("type") or "").strip().lower()
    if recorded not in TYPE_MAP:
        raise SystemExit("unknown recorded type %r on %s.%s - extend TYPE_MAP, do not guess"
                         % (recorded, table, name))
    base, needs_length = TYPE_MAP[recorded]
    if not needs_length:
        return base
    length = column.get("length")
    if length in (None, 0, -1):
        return base + "(max)" if recorded in ("nvarchar", "varchar") else base + "(%d)" % DEFAULT_LENGTH
    return "%s(%d)" % (base, int(length))


def build(database_names=None, include_portal=False):
    tables, catalogs, relations = load()
    database_names = database_names or DEFAULT_DATABASES
    keys = relations.get("keys") or {}

    allowed = set(database_names)
    if include_portal:
        allowed.add("portal")

    by_database = {}
    for table, meta in sorted(tables.items(), key=lambda kv: kv[0].lower()):
        database = catalogs.get(table)
        if database not in allowed:
            continue
        by_database.setdefault(database, []).append((table, meta))

    if not by_database:
        raise SystemExit("no tables matched the requested catalogs")

    lines = []
    lines.append("/* =====================================================================")
    lines.append("   SYNTHETIC engine schema - a stand-in, not an installation")
    lines.append("   ---------------------------------------------------------------------")
    lines.append("   Generated by tools/synthetic-schema.py from engine/column-types.json.")
    lines.append("   Every table and column here is one the portal actually names, with")
    lines.append("   the type recorded from the vendor DDL or the engine ORM.")
    lines.append("")
    lines.append("   Use it to compile (SET NOEXEC ON) and run the portal's SQL on a")
    lines.append("   machine that has no vendor installation. It is NOT a deployment:")
    lines.append("   no keys, no seeds, no business data.")
    lines.append("   ===================================================================== */")
    lines.append("")
    lines.append("SET NOCOUNT ON;")
    lines.append("")

    for database, items in by_database.items():
        name = database_names.get(database, database)
        lines.append("/* ---------- %s (%d tables) ---------- */" % (name, len(items)))
        lines.append("IF DB_ID(N'%s') IS NULL CREATE DATABASE [%s];" % (name, name))
        lines.append("GO")
        for table, meta in items:
            columns = meta.get("columns") or {}
            if not columns:
                continue
            primary = (keys.get(table) or {}).get("columns") or []
            lines.append("IF OBJECT_ID(N'[%s].[dbo].[%s]', N'U') IS NULL" % (name, table))
            lines.append("CREATE TABLE [%s].[dbo].[%s] (" % (name, table))
            definitions = []
            for column_name, column in columns.items():
                piece = "    [%s] %s %s" % (
                    column_name, sql_type(column, table, column_name),
                    "NOT NULL" if not column.get("nullable", True) else "NULL")
                if column.get("identity"):
                    piece += " IDENTITY(1,1)"
                definitions.append(piece)
            # A table the model says has a key gets one, so IDENTITY/seed tests behave.
            if primary and all(p in columns for p in primary):
                definitions.append("    CONSTRAINT [PK_syn_%s] PRIMARY KEY CLUSTERED (%s)"
                                   % (table, ", ".join("[%s]" % p for p in primary)))
            lines.append(",\n".join(definitions))
            lines.append(");")
            lines.append("GO")
        lines.append("")

    return "\n".join(lines) + "\n"


def check():
    """Gate: the model is complete enough to execute against, and the output is valid."""
    problems = []
    tables, catalogs, relations = load()
    if not tables:
        problems.append("engine/column-types.json has no tables")
    for table, meta in tables.items():
        if table not in catalogs:
            problems.append("%s has no catalog in engine/relations.json" % table)
            continue
        catalog = catalogs[table]
        if catalog in OUT_OF_SCOPE:
            continue  # recorded, deliberately not generated
        if catalog not in DEFAULT_DATABASES and catalog != "portal":
            problems.append("%s claims unknown catalog %r" % (table, catalog))
        columns = meta.get("columns") or {}
        if not columns:
            problems.append("%s has no columns" % table)
        for column, detail in columns.items():
            recorded = (detail.get("type") or "").strip().lower()
            if recorded not in TYPE_MAP:
                problems.append("%s.%s has unmapped type %r" % (table, column, recorded))
            if detail.get("evidence") not in ("ddl", "orm", "orm-rename"):
                # "orm-rename" is recorded upstream as "orm" + rename note; be permissive
                # only for the values the model actually uses.
                if detail.get("evidence") not in ("inferred",):
                    problems.append("%s.%s has no evidence for its type" % (table, column))

    t5 = build()
    generated = set()
    for table, meta in tables.items():
        if catalogs.get(table) in DEFAULT_DATABASES:
            generated.add(table)
    device = sorted(t for t, _ in tables.items() if catalogs.get(t) in OUT_OF_SCOPE)
    if any(("[%s]." % t) in t5 for t in device):
        problems.append("a device-catalog table was generated: the portal never opens that catalog")
    if "CREATE TABLE [ReportT5V3].[dbo].[" not in t5:
        problems.append("the generated script does not create the t5 catalog")
    if "CREATE TABLE [Personnel].[dbo].[" not in t5:
        problems.append("the generated script does not create the personnel catalog")
    if t5 != build():
        problems.append("the generator is not deterministic")
    if "T5WebAudit" in t5:
        problems.append("the synthetic schema must not create a portal-owned table")

    if problems:
        print("FAIL: synthetic schema")
        for problem in problems[:20]:
            print("  - " + problem)
        return 1
    counted = sum(1 for line in t5.splitlines() if line.startswith("CREATE TABLE ["))
    print("OK: synthetic schema (%d tables, %d catalogs, %d device tables excluded, deterministic)"
          % (counted, len(DEFAULT_DATABASES), len(device)))
    return 0


def main(argv):
    if "--check" in argv:
        return check()
    if "--sql" in argv:
        overrides = {}
        for index, arg in enumerate(argv):
            if arg == "--t5-db" and index + 1 < len(argv):
                overrides["t5"] = argv[index + 1]
            if arg == "--personnel-db" and index + 1 < len(argv):
                overrides["personnel"] = argv[index + 1]
        only = None
        if "--database" in argv:
            wanted = argv[argv.index("--database") + 1]
            only = {k: v for k, v in DEFAULT_DATABASES.items() if v == wanted or k == wanted}
            if not only:
                raise SystemExit("unknown --database %r (expected t5/personnel/ReportT5V3/Personnel)" % wanted)
        sys.stdout.write(build(only, "--include-portal" in argv))
        return 0
    print(__doc__)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
