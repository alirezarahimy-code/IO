#!/usr/bin/env python3
"""Grant gate: the least-privilege login must be able to do exactly what the portal does.

`db/02-t5web-sql-login.sql` grants write access per table, in two catalog sections.
The list of tables that *should* be writable is not a matter of taste: it is the set of
tables a generic module can create or delete, which lives in
`T5Web.Application/Modules/ModuleRegistry.cs`. When a module is added and the grant is
not, the screen fails in production with "The UPDATE permission was denied" - a bug
that is invisible on a developer's db_owner connection.

This gate reads both sides and compares them:

  * every writable module table appears in the grant list of its own catalog;
  * no granted table is missing from the model (a typo would fail at install time);
  * each table is granted in the section of the catalog that actually owns it
    (engine/relations.json), which is the same proof the two-catalog gate uses.

    python3 tools/sql-grant-gate.py
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
REGISTRY = os.path.join(ROOT, "T5Web.Application", "Modules", "ModuleRegistry.cs")
GRANTS = os.path.join(ROOT, "db", "02-t5web-sql-login.sql")
TYPES = os.path.join(ROOT, "engine", "column-types.json")
RELATIONS = os.path.join(ROOT, "engine", "relations.json")

SECTION_RE = re.compile(r"USE\s+\[(\w+)\]\s*;", re.I)
GRANT_RE = re.compile(r"GRANT\s+[^;]*?ON\s+(.*?)TO\s+\[", re.I | re.S)
OBJECT_RE = re.compile(r"OBJECT::(?:dbo\.|\[dbo\]\.)\[?(\w+)\]?")


def module_writes():
    with open(REGISTRY, encoding="utf-8") as handle:
        source = handle.read()
    writes = {}
    for block in re.findall(r"new ModuleDescriptor\s*\{(.*?)\n            \}\);", source, re.S):
        table = re.search(r'Table\s*=\s*"(\w+)"', block)
        if not table:
            continue
        catalog = re.search(r'Catalog\s*=\s*"(\w+)"', block)
        create = re.search(r"AllowCreate\s*=\s*(\w+)", block)
        delete = re.search(r"AllowDelete\s*=\s*(\w+)", block)
        writable = (create and create.group(1) == "true") or (delete and delete.group(1) == "true")
        if writable:
            writes.setdefault((catalog.group(1) if catalog else "t5"), set()).add(table.group(1))
    return writes


class GrantSyntaxProblem(Exception):
    pass


def multi_object_grants(text):
    """GRANT statements whose ON clause lists more than one securable.

    T-SQL takes exactly one securable per GRANT: `GRANT SELECT ON OBJECT::a, OBJECT::b`
    is Msg 102 ("Incorrect syntax near ','"), and it takes the whole batch with it, so
    *none* of the grants in that file run. This file was written that way, and no test
    noticed because the database job called sqlcmd without -b: a syntax error exited 0.
    Found by the IIS job, which needed the grants to actually work (BUG-36).
    """
    # Comments are prose, not T-SQL: this file *quotes* the broken form in a comment, and
    # a gate that cannot tell a quotation from a statement is a gate nobody trusts.
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.S)
    text = re.sub(r"--[^\n]*", " ", text)

    problems = []
    # `OBJECT::dbo.Rule` is Msg 156: [Rule] is a reserved word, and the grant that names
    # it fails the batch. Bracket every identifier instead of remembering which table
    # names happen to be keywords.
    for match in re.finditer(r"OBJECT::dbo\.", text, re.I):
        problems.append("an object name is not bracketed (OBJECT::dbo.%s...): a reserved word "
                        "such as [Rule] turns the whole batch into Msg 156"
                        % text[match.end():match.end() + 12].split()[0].split(";")[0])

    for match in re.finditer(r"GRANT\s+[^;]*?\bON\b([^;]*?)\bTO\b", text, re.I | re.S):
        securables = re.findall(r"OBJECT::[\w\.\[\]]+|SCHEMA::\w+", match.group(1))
        if len(securables) > 1:
            problems.append("one GRANT lists %d objects (%s ...); T-SQL accepts one and "
                            "answers Msg 102 for the rest, which silently cancels every grant "
                            "in the batch" % (len(securables), ", ".join(securables[:3])))
    return problems


def granted():
    with open(GRANTS, encoding="utf-8") as handle:
        text = handle.read()
    # Walk the file, remembering which database each GRANT belongs to.
    sections = {}
    current = None
    for index, line in enumerate(text.splitlines()):
        match = SECTION_RE.search(line)
        if match:
            current = match.group(1)
            sections.setdefault(current, set())
            continue
        if current is None:
            continue
        for table in OBJECT_RE.findall(line):
            sections[current].add(table)
    return sections, text


def catalog_of(database_name):
    """ReportT5V3 -> t5, Personnel -> personnel."""
    return {"ReportT5V3": "t5", "Personnel": "personnel"}.get(database_name, database_name)


def main():
    problems = []
    writes = module_writes()
    sections, text = granted()

    if not writes:
        problems.append("no writable module found - the registry parser is broken")

    with open(TYPES, encoding="utf-8") as handle:
        typed = set(json.load(handle)["tables"])
    with open(RELATIONS, encoding="utf-8") as handle:
        owners = {t: (v.get("database") if isinstance(v, dict) else v)
                  for t, v in (json.load(handle).get("databases") or {}).items()}

    by_catalog = {}
    for database, tables in sections.items():
        by_catalog[catalog_of(database)] = (database, tables)

    for catalog, tables in sorted(writes.items()):
        if catalog not in by_catalog:
            problems.append("no grant section for catalog %r" % catalog)
            continue
        database, granted_tables = by_catalog[catalog]
        for table in sorted(tables):
            if table not in granted_tables:
                problems.append("%s (catalog %s) is writable by a module but not granted in USE [%s]"
                                % (table, catalog, database))
            if table not in typed:
                problems.append("%s is granted but missing from engine/column-types.json" % table)
            owner = owners.get(table)
            if owner and owner != catalog:
                problems.append("%s belongs to catalog %s but is granted under %s"
                                % (table, owner, catalog))

    # A grant in the wrong section is the same bug wearing a different hat.
    for catalog, (database, tables) in sorted(by_catalog.items()):
        for table in sorted(tables):
            owner = owners.get(table)
            if owner == "portal":
                continue  # db/01 creates these in the t5 catalog, so their grants belong there
            if owner and owner != catalog:
                problems.append("%s is granted under USE [%s] but lives in catalog %s"
                                % (table, database, owner))

    for problem in multi_object_grants(text):
        problems.append(problem)

    if problems:
        print("FAIL: grants do not match what the modules write")
        for problem in sorted(set(problems)):
            print("  - " + problem)
        return 1

    total = sum(len(t) for t in writes.values())
    print("OK: grants match the modules (%d writable tables across %d catalogs)"
          % (total, len(writes)))
    return 0


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        # The three shapes that matter: the multi-object form (Msg 102), a bare
        # identifier (Msg 156 for [Rule]), and a comment that merely quotes them.
        bad = "GRANT SELECT, INSERT ON\n    OBJECT::[dbo].[A],\n    OBJECT::[dbo].[B] TO [x];"
        unbracketed = "GRANT SELECT ON OBJECT::dbo.Rule TO [x];"
        good = ("GRANT SELECT ON OBJECT::[dbo].[A] TO [x];\n"
                "GRANT SELECT ON OBJECT::[dbo].[B] TO [x];")
        quoted = ("-- GRANT ON OBJECT::a, OBJECT::b is invalid, and so is OBJECT::dbo.Rule\n"
                  "GRANT SELECT ON OBJECT::[dbo].[A] TO [x];")
        ok = (len(multi_object_grants(bad)) == 1
              and len(multi_object_grants(unbracketed)) == 1
              and not multi_object_grants(good)
              and not multi_object_grants(quoted))
        print("OK: the single-securable guard behaves" if ok else "FAIL: the guard does not detect a multi-object GRANT")
        sys.exit(0 if ok else 1)
    sys.exit(main())
