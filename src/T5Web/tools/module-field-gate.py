#!/usr/bin/env python3
"""Module field gate: every column a module names must exist in the engine model.

`ModuleRegistry.cs` describes 29 generic screens: the table, the order-by column, and
each field. The store turns that description into SQL, so a field that names a column
the engine does not have is not a display bug - it is

    Invalid column name 'RuleType'.

at the first click, on one screen, hours after the build was green. That is exactly
what happened on the first real install: the "rule per person" module offered
`RuleType`, while [UserRule] carries `RuleTypeName` (BUG-40). Nothing in the compiler,
the lint, or the SQL smoke could see it - the statement is assembled from data.

The gate compares each descriptor with `engine/column-types.json` (the typed model the
synthetic schema and the smoke runner already trust) and with the catalog that owns
the table.

    python3 tools/module-field-gate.py
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
REGISTRY = os.path.join(ROOT, "T5Web.Application", "Modules", "ModuleRegistry.cs")
TYPES = os.path.join(ROOT, "engine", "column-types.json")
RELATIONS = os.path.join(ROOT, "engine", "relations.json")

# Key("X", ...), Number("X", ...), Text("X", ...), Date("X", ...), Show("X", ...)
FIELD_RE = re.compile(r'\b(Key|Number|Text|Date|Show)\(\s*"(\w+)"')


def descriptors():
    with open(REGISTRY, encoding="utf-8") as handle:
        source = handle.read()
    blocks = re.findall(r"new ModuleDescriptor\s*\{(.*?)\n            \}\);", source, re.S)
    if not blocks:
        # A parser that finds nothing must not look like a clean result.
        blocks = re.findall(r"new ModuleDescriptor\s*\{(.*?)\n\s*\}\);", source, re.S)
    found = []
    for block in blocks:
        code = re.search(r'Code\s*=\s*"(\w+)"', block)
        table = re.search(r'Table\s*=\s*"(\w+)"', block)
        if not table:
            continue
        order_by = re.search(r'OrderBy\s*=\s*"(\w+)"', block)
        fields = [name for _, name in FIELD_RE.findall(block)]
        found.append({
            "code": code.group(1) if code else "?",
            "table": table.group(1),
            "order_by": order_by.group(1) if order_by else None,
            "fields": fields,
        })
    return found


def main():
    problems = []
    with open(TYPES, encoding="utf-8") as handle:
        typed = json.load(handle)["tables"]
    with open(RELATIONS, encoding="utf-8") as handle:
        relations = json.load(handle)
    owners = {t: (v.get("database") if isinstance(v, dict) else v)
              for t, v in (relations.get("databases") or {}).items()}

    modules = descriptors()
    if len(modules) < 20:
        problems.append("only %d module descriptors were parsed; the registry parser is broken" % len(modules))

    for module in modules:
        table = module["table"]
        if table not in typed:
            # The engine model may not carry tables the portal only counts; those are
            # covered by engine/relations.json instead.
            if table not in owners:
                problems.append("module %s names table %s, which the engine model does not have"
                                % (module["code"], table))
            continue
        columns = typed[table]["columns"]
        for field in module["fields"]:
            if field not in columns:
                problems.append("module %s (%s) has a field '%s' that the engine model does not "
                                "have; the screen fails with \"Invalid column name '%s'\""
                                % (module["code"], table, field, field))
        if module["order_by"] and module["order_by"] not in columns:
            problems.append("module %s orders by '%s', which %s does not have"
                            % (module["code"], module["order_by"], table))

    if problems:
        print("FAIL: module fields do not match the engine model")
        for problem in sorted(set(problems)):
            print("  - " + problem)
        return 1

    total = sum(len(m["fields"]) for m in modules)
    print("OK: %d modules, %d field names, all present in the engine model"
          % (len(modules), total))
    return 0


if __name__ == "__main__":
    sys.exit(main())
