#!/usr/bin/env python3
"""Verify every hand-written SQL identifier in Aria against the real T5 schema.

Why this exists
---------------
Aria is a *consumer* of a schema it does not own (docs/02-shared-database-contract.md).
The engine DA layer is type-safe, but ~40 places in Aria still write SQL by hand
(Dapper query strings). A typo there does not fail at build time - it fails at
21:00 on a shift, in front of the operator, as "Invalid object name" or
"Invalid column name".

This tool compares each hand-written identifier with db/schema-manifest.json
(built from vendor DDL + engine ORM metadata + the legacy data layer that runs
in production). It distinguishes three cases:

  ERROR  identifier is not in the manifest, but a near-miss *is*
         (case, singular/plural, edit distance <= 2). This is a typo.
  WARN   identifier is unknown and has no near-miss. Possibly a column that is
         newer than the manifest; reported so a human can decide.
  OK     identifier found.

Exit code: 0 clean, 1 errors (or warnings with --strict).

Usage:
    python3 tools/verify-sql-schema.py [--strict] [--json out.json] [FILE...]
"""

from __future__ import annotations

import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TWF = os.path.abspath(os.path.join(HERE, ".."))
MANIFEST = os.path.join(TWF, "db", "schema-manifest.json")

SCAN_ROOTS = ["TD5", "TWF.Infrastructure", "TWF.Application", "TWF.Web"]

SQL_KEYWORDS = {
    "select", "from", "where", "join", "left", "right", "inner", "outer", "cross",
    "apply", "on", "and", "or", "not", "null", "is", "as", "order", "by", "group",
    "having", "insert", "into", "values", "update", "set", "delete", "top", "case",
    "when", "then", "else", "end", "exists", "in", "like", "between", "count", "sum",
    "max", "min", "avg", "isnull", "coalesce", "cast", "convert", "int", "bigint",
    "smallint", "tinyint", "bit", "nvarchar", "varchar", "datetime", "date", "time",
    "decimal", "float", "money", "uniqueidentifier", "dbo", "desc", "asc", "distinct",
    "union", "all", "any", "some", "with", "nolock", "begin", "commit", "rollback",
    "declare", "if", "while", "return", "output", "into", "merge", "using", "matched",
    "rows", "rowcount", "error", "tran", "transaction", "scope_identity", "getdate",
}

# System views/schemas are never part of the application schema.
SYSTEM_OBJECTS = {"information_schema", "sys", "sysdiagrams", "queries", "tables", "columns"}

TABLE_DECL = re.compile(
    r"(?i)\b(?:FROM|JOIN|INSERT\s+INTO|UPDATE|DELETE\s+FROM|MERGE\s+INTO)\s+"
    r"\[?([A-Za-z_][A-Za-z0-9_]*)\]?(?:\s+(?:AS\s+)?([A-Za-z_][A-Za-z0-9_]*))?")
QUALIFIED = re.compile(r"\b(?:\[([A-Za-z_][A-Za-z0-9_]*)\]|([A-Za-z_][A-Za-z0-9_]*))\s*\.\s*\[([A-Za-z_][A-Za-z0-9_]*)\]")
BRACKETED = re.compile(r"\[([A-Za-z_][A-Za-z0-9_]*)\]")


def load_manifest():
    if not os.path.exists(MANIFEST):
        print("ERROR: db/schema-manifest.json missing - run tools/build-schema-manifest.py")
        sys.exit(2)
    raw = json.load(open(MANIFEST, encoding="utf-8"))["tables"]
    return {t: set(v["columns"]) for t, v in raw.items()}


def sql_literals(text):
    """Yield SQL string literals from C# source with their line numbers."""
    for m in re.finditer(r'@"((?:[^"]|"")*)"', text, re.S):
        body = m.group(1).replace('""', '"')
        yield body, text.count("\n", 0, m.start()) + 1
    for m in re.finditer(r'"((?:[^"\\]|\\.)*)"', text):
        body = m.group(1)
        if re.search(r"(?i)\b(select|insert\s+into|update\s+\w|delete\s+from)\b", body):
            yield body.replace('\\"', '"'), text.count("\n", 0, m.start()) + 1


def normalize(name):
    return re.sub(r"[^a-z0-9]", "", name.lower())


def edit_distance(a, b, cap=3):
    if abs(len(a) - len(b)) > cap:
        return cap + 1
    prev = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        cur = [i]
        for j, cb in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (ca != cb)))
        prev = cur
    return prev[-1]


def near_miss(name, candidates):
    """Return the closest candidate when `name` looks like a typo of it."""
    n = normalize(name)
    for cand in candidates:
        c = normalize(cand)
        if n == c:
            return cand
        # singular/plural
        if n == c + "s" or c == n + "s" or n == c + "es" or c == n + "es":
            return cand
    for cand in candidates:
        if edit_distance(n, normalize(cand)) <= 2:
            return cand
    return None


def check_literal(literal, manifest):
    """Return (errors, warnings) for one SQL literal."""
    errors, warnings = [], []

    tables, aliases = {}, {}
    for m in TABLE_DECL.finditer(literal):
        name, alias = m.group(1), m.group(2)
        if name.lower() in SQL_KEYWORDS:
            continue
        tables[name] = tables.get(name, 0) + 1
        if alias and alias.lower() not in SQL_KEYWORDS and alias.lower() not in ("where", "set", "values", "on"):
            aliases[alias] = name

    for name in tables:
        if name in manifest or name.lower() in SYSTEM_OBJECTS:
            continue
        guess = near_miss(name, manifest.keys())
        if guess:
            errors.append(f"table '{name}' does not exist (did you mean '{guess}'?)")
        else:
            warnings.append(f"table '{name}' is not in the manifest")

    known_tables = {t for t in tables if t in manifest}

    for m in QUALIFIED.finditer(literal):
        owner = m.group(1) or m.group(2)
        column = m.group(3)
        table = aliases.get(owner, owner if owner in manifest else None)
        if not table or table not in manifest:
            continue
        if column not in manifest[table]:
            guess = near_miss(column, manifest[table])
            if guess:
                errors.append(f"'{table}.{column}' does not exist (did you mean '{table}.{guess}'?)")
            else:
                warnings.append(f"'{table}.{column}' is not in the manifest")

    if len(known_tables) == 1:
        table = next(iter(known_tables))
        seen = set()
        for column in BRACKETED.findall(literal):
            if column == table or column.lower() in SQL_KEYWORDS or column in seen:
                continue
            seen.add(column)
            if column in manifest[table]:
                continue
            guess = near_miss(column, manifest[table])
            if guess:
                errors.append(f"'{table}.{column}' does not exist (did you mean '{table}.{guess}'?)")
            else:
                warnings.append(f"'{table}.{column}' is not in the manifest")

    return errors, warnings


GUARD = os.path.join(TWF, "TWF.Web", "Security", "SchemaGuard.cs")
ENTRY_RE = re.compile(r'\{\s*"([A-Za-z_][A-Za-z0-9_]*)",\s*(?:new\[\]\s*\{([^}]*)\}|new string\[0\])\s*\}')


def check_guard_contract(manifest):
    """The guard may only *block* on objects that carry real evidence.

    SchemaGuard.Contract is parsed and cross-checked against the manifest; an
    entry that cannot be proven would turn a healthy database into a locked portal.
    """
    errors = []
    if not os.path.exists(GUARD):
        return errors
    text = open(GUARD, encoding="utf-8").read()
    if "public static readonly IReadOnlyDictionary<string, string[]> Contract" not in text:
        return errors
    required = text.split("public static readonly IReadOnlyDictionary<string, string[]> Contract", 1)[1]
    required = required.split("AdvisoryContract", 1)[0]

    for table, raw_columns in ENTRY_RE.findall(required):
        columns = [c.strip().strip('"') for c in raw_columns.split(",") if c.strip()]
        if table not in manifest:
            errors.append(
                f"SchemaGuard.Contract requires table '{table}', which has no evidence in "
                f"db/schema-manifest.json - move it to AdvisoryContract")
            continue
        for column in columns:
            if column not in manifest[table]:
                errors.append(
                    f"SchemaGuard.Contract requires '{table}.{column}', which has no evidence "
                    f"in db/schema-manifest.json - move it to AdvisoryContract")
    return errors


def iter_sources(argv):
    explicit = [a for a in argv if not a.startswith("-")]
    if explicit:
        for path in explicit:
            if os.path.isfile(path):
                yield path
        return
    for root_name in SCAN_ROOTS:
        root = os.path.join(TWF, root_name)
        if not os.path.isdir(root):
            continue
        for dirpath, _dirs, files in os.walk(root):
            for name in files:
                if name.endswith(".cs") or name.endswith(".cshtml"):
                    yield os.path.join(dirpath, name)


def main(argv):
    manifest = load_manifest()
    errors, warnings = [], []
    files = 0
    literals = 0

    for path in iter_sources(argv):
        files += 1
        text = open(path, encoding="utf-8", errors="ignore").read()
        rel = os.path.relpath(path, TWF)
        for literal, line in sql_literals(text):
            literals += 1
            e, w = check_literal(literal, manifest)
            errors += [f"{rel}:{line}: {x}" for x in e]
            warnings += [f"{rel}:{line}: {x}" for x in w]

    guard_errors = check_guard_contract(manifest)
    errors += guard_errors

    strict = "--strict" in argv
    print(f"schema: {len(manifest)} tables · scanned {literals} SQL literal(s) in {files} file(s)")

    if warnings:
        print(f"\nWARN ({len(warnings)}):")
        for w in sorted(set(warnings)):
            print("  " + w)

    if errors:
        print(f"\nERROR ({len(errors)}):")
        for e in sorted(set(errors)):
            print("  " + e)
        return 1

    if strict and warnings:
        print("\nFAILED: --strict treats warnings as errors.")
        return 1

    print("\nOK: every SQL identifier matches the manifest")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
