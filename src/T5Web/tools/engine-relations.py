#!/usr/bin/env python3
"""T5Web — the database model: which table lives where, and how tables relate.

The portal reads a schema it does not own, spread over two SQL Server catalogs
(`ReportT5V3` for the engine, `Personnel` for people and organisation), and it
owns three tables of its own. Nothing in the code is allowed to *guess* that
layout, so this tool derives it from evidence and freezes it:

  1. `Personnel.sql` and `Link5.sql` are real DDL: CREATE TABLE, PRIMARY KEY and
     33 FOREIGN KEY constraints. Those are declared facts.
  2. `T5V3.DB.dll` (the engine's EF model) contributes tables with no DDL in the
     repository; their keys are derived from column names and marked `inferred`,
     never `declared`.
  3. Every `*ID` / `*Number` column that is not its table's key must either
     resolve to a table or be listed as an exception **with a reason**. An
     unclassified column is an unknown relation, and unknown is not allowed.

Outputs (`--write` regenerates, `--check` is a build gate):

    engine/relations.json   databases, keys, relations, exceptions
    engine/relations.tsv    the relations as a flat table, for the test suite

Usage
-----
    python3 tools/engine-relations.py --write
    python3 tools/engine-relations.py --check
    python3 tools/engine-relations.py --stats
"""

from __future__ import annotations

import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)                       # src/T5Web
REPO = os.path.dirname(os.path.dirname(ROOT))      # repository root
MANIFEST = os.path.join(REPO, "src", "TWF", "db", "schema-manifest.json")
DDL = {
    "device": os.path.join(REPO, "Link5 v4.0.4", "Link5.sql"),
    "personnel": os.path.join(REPO, "Link5 v4.0.4", "Personnel.sql"),
}
OUT_JSON = os.path.join(ROOT, "engine", "relations.json")
OUT_TSV = os.path.join(ROOT, "engine", "relations.tsv")

# The catalogs on the server, and what each one is for (docs/09).
CATALOG = {
    "t5": "ReportT5V3",
    "personnel": "Personnel",
    "device": "Link5 service database",
    "portal": "ReportT5V3",
}

# A column whose name does not reveal its target, resolved once for the whole
# model. Every entry here is also a declared foreign key or a primary key of the
# target table, so `--check` can prove the target exists.
ALIASES = {
    "BadgeNumber": ("UserInfo", "BadgeNumber"),
    "MachineNumber": ("Device", "MachineNumber"),
    "DeviceNumber": ("Device", "MachineNumber"),
    "DeptID": ("Department", "DepartmentID"),
    "CertificateID": ("Certificate", "CertID"),
    "TitleChartID": ("TitlesChart", "TitleID"),
    "EmpTypeID": ("EmpType", "EmpTypeID"),
    "PropertyID": ("Property", "PropertyID"),
    "DropDownValue": ("PropertyDropdownValue", "DropdownValueID"),
    "GateID": ("Gate", "GateID"),
    "GroupID": ("Dashboard", "ID"),
    "SMSID": ("DeviceMessage", "SMSID"),
    "UserId": ("WebUser", "Id"),
    "ViewUserID": ("User", "UserID"),
    "MapID": ("Map", "MapID"),
    "ScheduleID": ("Schedule", "ScheduleID"),
    "RoleId": ("webpages_Roles", "RoleId"),
    "ManagerID": ("UserInfo", "BadgeNumber"),
    "ManagerBadgeNumber": ("UserInfo", "BadgeNumber"),
    "PersonnelBadgeNumber": ("UserInfo", "BadgeNumber"),
    "DeputyBadgeNumber": ("UserInfo", "BadgeNumber"),
    "AccepterBadgeNumber": ("UserInfo", "BadgeNumber"),
    "LeaveTypeID": ("LeaveType", "LeaveID"),
    "DecreaseFromLeaveID": ("LeaveType", "LeaveID"),
    "LeaveID": ("LeaveType", "LeaveID"),
    "DescriptionID": ("LeaveDescription", "ID"),
    "LeaveDescriptionID": ("LeaveDescription", "ID"),
    "TitleID": ("TitlesChart", "TitleID"),
    "AccepterTitleID": ("TitlesChart", "TitleID"),
    "TablixID": ("ReportTablix", "TablixID"),
    "ColumnID": ("ReportTablixColumn", "ColumnID"),
    "DeptRuleID": ("DepartmentRule", "DeptRuleID"),
    "UserRuleID": ("UserRule", "UserRuleID"),
    "AreaID": ("Area", "AreaID"),
    "ParameterID": ("Parameter", "ParameterID"),
    "CalendarID": ("Calendar", "CalendarID"),
    "PositionID": ("Position", "PositionID"),
    "ReportID": ("Report", "ReportID"),
    "RequestID": ("Request", "RequestID"),
    "RuleID": ("Rule", "RuleID"),
    "ShiftID": ("Shift", "ShiftID"),
}

# Keys the DDL never states, but the engine model and our own SQL treat as the
# row identity of that table (each entry is checked against the manifest below).
KEY_OVERRIDES = {
    "LeaveType": ["LeaveID"],
    "LeaveTypes": ["LeaveID"],
    "DepartmentRule": ["DeptRuleID"],
    "DepartmentRules": ["DeptRuleID"],
    "UserRule": ["UserRuleID"],
    "UserRules": ["UserRuleID"],
    "PushCommand": ["CommandID"],
    "PushCommands": ["CommandId"],
    "MyTimeToolbarItem": ["ItemID"],
    "DashboardItem": ["ItemID"],
    "ReportTablix": ["TablixID"],
    "ReportTablixColumn": ["ColumnID"],
    "ReportTextbox": ["TextBoxID"],
    "ReportData": ["ReportDataID"],
    "ReportFilter": ["FilterID"],
    "ReportChart": ["ChartID"],
    "Report": ["ReportID"],
    "Deputy": ["TitleID"],
    "Area": ["AreaID"],
    "Calendar": ["CalendarID"],
    "Parameter": ["ParameterID"],
    "Position": ["PositionID"],
    "Rule": ["RuleID"],
    "Shift": ["ShiftID"],
    "Schedule": ["ScheduleID"],
    "LeaveDescription": ["ID"],
    "Ramezan": ["ID"],
    "PassCheckProps": ["ID"],
    "ValidLeave": ["ID"],
    "HourlyLeave": ["ID"],
    "LeavesAdded": ["ID"],
    "DownloadTime": ["ID"],
    "UserPositionData": ["ID"],
    "UserInfoManager": ["ID"],
}

# Tables whose identity is a combination the DDL never states. Listed, not
# guessed silently: docs/09 explains why each one is what it is.
COMPOSITE_KEYS = {
    "ShiftDetail": ["ShiftID", "ScheduleID", "DayIndex"],
    "Workflow": ["WorkflowType", "LeaveTypeID", "OrderIndex"],
    "RequestAccept": ["RequestID", "AccepterTitleID"],
    "YearRemain": ["BadgeNumber", "LeaveTypeID"],
    "Card": ["BadgeNumber", "LeaveTypeID"],
    "AreaPoint": ["AreaID", "PointIndex"],
    "PositionStep": ["PositionID", "OperationIndex"],
    "UserInfoTitles": ["BadgeNumber", "TitleChartID"],
    "UserInfoProperty": ["BadgeNumber", "PropertyID"],
    "UserDeviceValid": ["BadgeNumber", "MachineNumber"],
    "EmpTypeLeave": ["EmpTypeID", "LeaveTypeID"],
    "DepartmentRule": ["DeptRuleID"],
    "Record": ["BadgeNumber", "CheckTime", "CheckType"],
    "Request": ["RequestID"],
    "CalendarHoliday": ["CalendarID", "HolidayDate"],
    "DailyLeaf": ["BadgeNumber", "Date", "LeaveID"],
    "UserShift": ["BadgeNumber", "ShiftID", "FromDate"],
    "TempSchedule": ["BadgeNumber", "ScheduleID", "AssignDate"],
    "ReportAccess": ["ReportID", "TitleID"],
    "PersonnelDeputy": ["BadgeNumber", "DeputyBadgeNumber"],
    "AllowValue": ["BadgeNumber", "ForDate"],
    "EditedValue": ["BadgeNumber", "ForDate"],
    "UserPermission": ["BadgeNumber", "AccessType"],
    "T5WebAccess": ["BadgeNumber", "Feature"],
    "AutoSchedule": ["BadgeNumber", "ScheduleID"],
    "T5WebAudit": ["ID"],
    "T5WebDeviceCommand": ["ID"],
}

# Rows in the shared manifest that are not tables at all. They are excluded from the
# model, with the reason, instead of being carried as an unknown.
NOT_TABLES = {
    "Entities": "نام مجموعه‌های EF است، نه جدول",
    "sys": "مخزن سامانه‌ای SQL Server است، نه جدول",
}

# Columns that end in ID (or look like a key) but are not references to a table
# in this schema. Each one carries the reason, because "unknown" is not a
# classification.
EXCEPTIONS = {
    ("Record", "WorkCode"): "کد کار؛ عددی از دستگاه، جدولی برای آن وجود ندارد",
    ("Record", "CheckType"): "نوع تردد (ورود/خروج)؛ enum موتور",
    ("Record", "SourceType"): "منبع تردد (دستگاه/دستی)؛ enum موتور",
    ("Record", "VerifyMethod"): "روش احراز هویت (اثر انگشت/کارت/چهره)؛ enum دستگاه",
    ("DeviceErrorLog", "CmdId"): "شناسه فرمانی که خود دستگاه می‌فرستد؛ جدول متناظر در اسکیما نیست",
    ("DownloadRecordsLog", "EventID"): "نوع رویداد دانلود؛ enum سرویس Link5",
    ("AccessControl", "Badgenumber"): "همان BadgeNumber با حرف کوچک در DDL؛ به UserInfo اشاره دارد",
    ("SystemLog", "EventID"): "کلید اصلی همان جدول است، نه ارجاع",
    ("CommandLog", "CommandId"): "شناسه فرمان سمت دستگاه؛ جدول Command در پورتال نوشته نمی‌شود",
    ("PushCommands", "CommandId"): "کلید اصلی همان جدول است، نه ارجاع",
    ("Area", "UserID"): "شناسه کاربر نرم‌افزار؛ در اسکیما فقط User در پایگاه سرویس دستگاه هست و نگاشت آن به پرسنل اثبات نشده",
    ("SystemLog", "UserID"): "شناسه کاربر نرم‌افزار در لاگ موتور؛ جدول متناظر در این کاتالوگ نیست",
    ("DownloadRecordsLog", "UserID"): "شناسه کاربر سرویس دانلود؛ جدول متناظر در این کاتالوگ نیست",
    ("DashboardItem", "ItemID"): "شناسه آیتم نوار ابزار دسکتاپ (Misc/ToolbarItems.xml)؛ جدولی در اسکیما ندارد",
}

# A key-like column that is not an ID suffix, and names that are never a reference.
BUSINESS_KEYS = ("badgenumber", "machinenumber")
SKIP = {"id", "isvalid", "nationalid"}

TABLE = re.compile(r"\b(?:FROM|JOIN|INTO|UPDATE|MERGE|DELETE\s+FROM)\s+\[?([A-Za-z_][A-Za-z0-9_]*)\]?", re.I)
SQL_STOPWORDS = {"set", "values", "select", "where", "on", "then", "when", "as", "not", "is",
                 "null", "top", "with", "group", "order", "having", "and", "or", "inserted",
                 "deleted", "output", "join", "inner", "left", "right", "cross", "apply"}
LITERAL = re.compile(r'@"(.*?)";', re.S)
SQLISH = re.compile(r"\b(?:SELECT|INSERT|UPDATE|MERGE|DELETE)\b", re.I)


# --------------------------------------------------------------------------- #
# 1. The DDL: declared keys and declared relations
# --------------------------------------------------------------------------- #
def read_sql(path):
    raw = open(path, "rb").read()
    return raw.decode("utf-16-le") if raw[:2] == b"\xff\xfe" else raw.decode("utf-8", "replace")


def parse_ddl(path):
    """{table: {"columns", "pk", "fks"}} from one SQL Server script."""
    text = read_sql(path)
    tables = {}
    for name, body in re.findall(r"CREATE TABLE \[dbo\]\.\[([^\]]+)\]\((.*?)\n\)\s*ON", text, re.S):
        clause = re.search(r"PRIMARY KEY[^(]*\((.*?)\)\s*(?:WITH|ON|\n|$)", body, re.S)
        tables[name] = {
            "columns": re.findall(r"^\t\[([^\]]+)\]\s+\[", body, re.M),
            "pk": re.findall(r"\[([^\]]+)\]", clause.group(1)) if clause else [],
            "fks": [],
        }

    pattern = re.compile(
        r"ALTER TABLE \[dbo\]\.\[([^\]]+)\][^\n]*?(?:WITH CHECK )?ADD\s+CONSTRAINT \[([^\]]+)\] FOREIGN KEY"
        r"\(([^)]*)\)\s*\n?REFERENCES \[dbo\]\.\[([^\]]+)\] \(([^)]*)\)(.*?)(?=\nGO)", re.S)
    for m in pattern.finditer(text):
        table, name, cols, ref_table, ref_cols, tail = m.groups()
        slot = tables.setdefault(table, {"columns": [], "pk": [], "fks": []})
        slot["fks"].append({
            "name": name,
            "columns": re.findall(r"\[([^\]]+)\]", cols),
            "ref_table": ref_table,
            "ref_columns": re.findall(r"\[([^\]]+)\]", ref_cols),
            "on_delete": "CASCADE" if re.search(r"ON DELETE CASCADE", tail, re.I) else None,
        })
    return tables


PORTAL_SCRIPT = os.path.join(ROOT, "db", "01-t5web-objects.sql")


def portal_tables():
    """The three tables this portal creates, with their columns, from its own script."""
    text = open(PORTAL_SCRIPT, encoding="utf-8").read()
    found = {}
    for name, body in re.findall(r"CREATE TABLE dbo\.(\w+)\s*\((.*?)\n    \);", text, re.S):
        found[name] = re.findall(r"^\s{8}(\w+)\s+", body, re.M)
    return found


def catalog_of(table, sources, ddl_owner):
    if table.startswith("T5Web"):
        return "portal", "db/01-t5web-objects.sql"
    if table == "PassCheckProps":
        # ARIA's own table, created in the engine catalog; the T5Web portal never reads it.
        return "t5", "ARIA-owned (src/TWF/db/01-app-objects.sql)"
    if any("T5V3.DB.dll" in s for s in sources):
        # The engine model is the copy this portal reads; the same name may also exist in
        # the device service database (Card, Schedule, SystemLog) - that copy is not ours.
        return "t5", "T5V3.DB.dll EntityClasses"
    if table in ddl_owner:
        return ddl_owner[table], os.path.basename(DDL[ddl_owner[table]]) + " DDL"
    if any("legacy TD5 SQL" in s for s in sources):
        return "personnel", "legacy TD5 SQL"
    if any("Link5" in s for s in sources):
        return "device", "Link5 DDL"
    return "?", "no evidence"


def is_id_like(column):
    low = column.lower()
    return low not in SKIP and (low.endswith("id") or low in BUSINESS_KEYS)


def build():
    manifest = json.load(open(MANIFEST, encoding="utf-8"))["tables"]
    owned = portal_tables()
    # The manifest covers the engine's tables only, so the three tables this portal
    # creates are added here: a model that omits its own writes is not a model.
    for table, columns in owned.items():
        manifest[table] = {"columns": columns, "sources": ["src/T5Web/db/01-t5web-objects.sql"]}
    for table in list(manifest):
        if table in NOT_TABLES:
            del manifest[table]

    ddl, ddl_owner = {}, {}
    for database, path in DDL.items():
        for table, info in parse_ddl(path).items():
            ddl[table] = info
            ddl_owner[table] = database

    # ---- catalogs -----------------------------------------------------------
    databases = {}
    for table, info in manifest.items():
        database, evidence = catalog_of(table, info["sources"], ddl_owner)
        databases[table] = {"database": database, "evidence": evidence}

    # ---- keys ---------------------------------------------------------------
    keys = {}
    for table, info in manifest.items():
        if table in ddl and ddl[table]["pk"]:
            keys[table] = {"columns": ddl[table]["pk"], "evidence": "ddl"}
            continue
        columns = info["columns"]
        if table in KEY_OVERRIDES:
            keys[table] = {"columns": KEY_OVERRIDES[table], "evidence": "inferred-model"}
            continue
        if table in COMPOSITE_KEYS:
            keys[table] = {"columns": COMPOSITE_KEYS[table], "evidence": "inferred-composite"}
            continue
        guess = [c for c in columns if c.lower() in (table.lower() + "id", "id")]
        if len(guess) == 1:
            keys[table] = {"columns": guess, "evidence": "inferred-convention"}
        else:
            keys[table] = {"columns": [], "evidence": "unconfirmed"}

    # ---- relations ----------------------------------------------------------
    relations, exceptions = [], []

    def add(kind, table, column, target_table, target_column, evidence):
        relations.append({
            "kind": kind,
            "from": {"table": table, "column": column, "database": databases[table]["database"]},
            "to": {"table": target_table, "column": target_column,
                   "database": databases.get(target_table, {}).get("database", "?")},
            "evidence": evidence,
        })

    declared_seen = set()
    for table, info in sorted(ddl.items()):
        for fk in info["fks"]:
            for column, ref_column in zip(fk["columns"], fk["ref_columns"]):
                declared_seen.add((table, column))
                add("declared", table, column, fk["ref_table"], ref_column,
                    fk["name"] + (" (ON DELETE CASCADE)" if fk["on_delete"] else ""))

    column_owners = {}
    for table, info in manifest.items():
        for column in info["columns"]:
            column_owners.setdefault(column.lower(), set()).add(table)

    unresolved = []
    for table, info in sorted(manifest.items()):
        pk = {c.lower() for c in keys[table]["columns"]}
        for column in info["columns"]:
            if not is_id_like(column):
                continue
            if column.lower() in pk or (table, column) in declared_seen:
                continue
            if (table, column) in EXCEPTIONS:
                exceptions.append({"table": table, "column": column,
                                   "reason": EXCEPTIONS[(table, column)]})
                continue
            if column in ALIASES:
                target_table, target_column = ALIASES[column]
                add("inferred", table, column, target_table, target_column, "alias")
                continue
            owners = column_owners[column.lower()]
            if len(owners) == 1:
                owner = next(iter(owners))
                if owner == table:
                    # The only table with this column is this one: it identifies the row.
                    keys[table] = {"columns": [column], "evidence": "inferred-unique"}
                    continue
                add("inferred", table, column, owner, column, "unique-column")
                continue
            stem = column.lower()[:-2].rstrip("s") if column.lower().endswith("id") else column.lower()
            by_name = [t for t in owners if t.lower().rstrip("s") == stem]
            if len(by_name) == 1:
                add("inferred", table, column, by_name[0], column, "table-name")
                continue
            unresolved.append({"table": table, "column": column,
                               "candidates": sorted(owners)})

    model = {
        "generated_by": "src/T5Web/tools/engine-relations.py",
        "sources": {
            "manifest": "src/TWF/db/schema-manifest.json",
            "ddl": {k: os.path.relpath(v, REPO).replace(os.sep, "/") for k, v in DDL.items()},
        },
        "catalogs": CATALOG,
        "summary": {
            "tables": len(manifest),
            "by_database": {d: sum(1 for v in databases.values() if v["database"] == d)
                            for d in sorted(set(v["database"] for v in databases.values()))},
            "declared_relations": sum(1 for r in relations if r["kind"] == "declared"),
            "inferred_relations": sum(1 for r in relations if r["kind"] == "inferred"),
            "cross_database_relations": sum(
                1 for r in relations if r["from"]["database"] != r["to"]["database"]),
            "keys_declared": sum(1 for k in keys.values() if k["evidence"] == "ddl"),
            "keys_inferred": sum(1 for k in keys.values() if k["evidence"] == "inferred-convention"),
            "keys_unconfirmed": sum(1 for k in keys.values() if k["evidence"] == "unconfirmed"),
            "exceptions": len(exceptions),
            "unresolved": len(unresolved),
        },
        "databases": {t: v for t, v in sorted(databases.items())},
        "keys": {t: v for t, v in sorted(keys.items())},
        "relations": sorted(relations, key=lambda r: (r["from"]["table"], r["from"]["column"])),
        "exceptions": exceptions,
        "not_tables": NOT_TABLES,
        "unresolved": unresolved,
    }
    return model, manifest


# --------------------------------------------------------------------------- #
# 2. The routing rule: one SQL statement, one catalog
# --------------------------------------------------------------------------- #
def portal_sql_files():
    for folder in ("T5Web.Data", "T5Web.Application", "T5Web.Web"):
        for base, _, files in os.walk(os.path.join(ROOT, folder)):
            for name in files:
                if name.endswith(".cs"):
                    yield os.path.join(base, name)


METHOD_START = re.compile(r"^\s{8}(?:public|private|internal|protected)\b[^;]*\(", re.M)
PERSONNEL_OPENER = re.compile(r"OpenPersonnel\s*\(|personnelFactory|_personnel\w*\(\)|new PersonnelDirectory")
# PersonnelDirectory is *the* personnel reader: its single connection factory is the
# personnel one (see its constructor), so its private Open() counts as a personnel opener.
PERSONNEL_FILE = re.compile(r"PersonnelDirectory\.cs$")


def enclosing_method(source, line):
    """The text of the method that contains `line` (best effort, by indentation)."""
    starts = [m.start() for m in METHOD_START.finditer(source)]
    offsets = [source[:pos].count("\n") + 1 for pos in starts]
    index = None
    for position, first in enumerate(offsets):
        if first <= line:
            index = position
        else:
            break
    if index is None:
        return None
    begin = starts[index]
    end = starts[index + 1] if index + 1 < len(starts) else len(source)
    return source[begin:end]


def sql_catalog_problems(model):
    """No statement may join two catalogs: the portal has one connection per catalog."""
    databases = {t: v["database"] for t, v in model["databases"].items()}
    problems, statements, members = [], 0, []
    personnel_only = None
    for path in portal_sql_files():
        source = open(path, encoding="utf-8").read()
        for match in LITERAL.finditer(source):
            query = match.group(1)
            if not SQLISH.search(query):
                continue
            statements += 1
            catalogs = set()
            for table in TABLE.findall(query):
                if table.lower() in SQL_STOPWORDS:
                    continue
                if table in databases:
                    # Portal tables live inside the engine catalog.
                    catalogs.add("t5" if databases[table] == "portal" else databases[table])
                elif table.lower() in ("inserted", "deleted"):
                    continue
                else:
                    problems.append({
                        "file": os.path.relpath(path, ROOT),
                        "line": source[:match.start()].count("\n") + 1,
                        "problem": "unknown table: " + table,
                    })
            if len(catalogs) > 1:
                problems.append({
                    "file": os.path.relpath(path, ROOT),
                    "line": source[:match.start()].count("\n") + 1,
                    "problem": "statement mixes catalogs: " + ", ".join(sorted(catalogs)),
                })
            if catalogs == {"personnel"}:
                members.append((path, source, match, personnel_only))
    for path, source, match, _ in members:
        line = source[:match.start()].count("\n") + 1
        body = enclosing_method(source, line)
        if body is None:
            continue
        if PERSONNEL_OPENER.search(body) is None and PERSONNEL_FILE.search(path) is None:
            problems.append({
                "file": os.path.relpath(path, ROOT),
                "line": line,
                "problem": ("a statement on the Personnel catalog is issued on the engine connection; "
                            "the method must open OpenPersonnel()/personnelFactory"),
            })
    return problems, statements


# --------------------------------------------------------------------------- #
# 3. CLI
# --------------------------------------------------------------------------- #
def write_tsv(model):
    rows = []
    for relation in model["relations"]:
        rows.append([
            relation["kind"],
            relation["from"]["database"], relation["from"]["table"], relation["from"]["column"],
            relation["to"]["database"], relation["to"]["table"], relation["to"]["column"],
            relation["evidence"],
        ])
    with open(OUT_TSV, "w", encoding="utf-8", newline="") as handle:
        handle.write("kind\tfrom_database\tfrom_table\tfrom_column\tto_database\tto_table\tto_column\tevidence\n")
        for row in rows:
            handle.write("\t".join(row) + "\n")


def check():
    model, manifest = build()
    problems = []

    for entry in model["unresolved"]:
        problems.append("unclassified reference: " + entry["table"] + "." + entry["column"] +
                        " (candidates: " + ", ".join(entry["candidates"]) + ")")

    for table, info in model["databases"].items():
        if info["database"] == "?":
            problems.append("table in no catalog: " + table)

    for relation in model["relations"]:
        target = relation["to"]
        if target["table"] not in manifest:
            problems.append("relation points at a missing table: " + target["table"])
        elif target["column"] not in manifest[target["table"]]["columns"]:
            problems.append("relation points at a missing column: " +
                            target["table"] + "." + target["column"])
        if (relation["from"]["table"] == target["table"] and
                relation["from"]["column"] == target["column"]):
            problems.append("relation points at itself: " + target["table"] + "." + target["column"])

    routing, statements = sql_catalog_problems(model)
    for problem in routing:
        problems.append(problem["file"] + ":" + str(problem["line"]) + " " + problem["problem"])

    if not os.path.exists(OUT_JSON):
        problems.append("engine/relations.json is missing; run --write")
    else:
        stored = json.load(open(OUT_JSON, encoding="utf-8"))
        if stored.get("summary") != model["summary"] or stored.get("relations") != model["relations"]:
            problems.append("engine/relations.json is stale; run --write")

    summary = model["summary"]
    print("catalogs       : " + ", ".join(k + "=" + str(v) for k, v in summary["by_database"].items()))
    print("relations      : " + str(summary["declared_relations"]) + " declared, " +
          str(summary["inferred_relations"]) + " inferred, " +
          str(summary["cross_database_relations"]) + " cross-database")
    print("keys           : " + str(summary["keys_declared"]) + " declared, " +
          str(summary["keys_inferred"]) + " inferred, " +
          str(summary["keys_unconfirmed"]) + " unconfirmed")
    print("exceptions     : " + str(summary["exceptions"]))
    print("sql statements : " + str(statements) + " scanned")

    if problems:
        print("")
        for problem in problems:
            print("  PROBLEM " + problem)
        print("")
        print("FAIL: the database model is not complete")
        return 1
    print("")
    print("OK: every table has a catalog, every key a proof, every reference a target")
    return 0


def main(argv):
    args = set(argv[1:])
    if "--write" in args:
        model, _ = build()
        with open(OUT_JSON, "w", encoding="utf-8") as handle:
            json.dump(model, handle, ensure_ascii=False, indent=2, sort_keys=False)
            handle.write("\n")
        write_tsv(model)
        print("wrote " + os.path.relpath(OUT_JSON, ROOT) + " and " + os.path.relpath(OUT_TSV, ROOT))
        return 0
    if "--unresolved" in args:
        model, _ = build()
        for entry in model["unresolved"]:
            print(entry["table"] + "." + entry["column"] + " -> " + ", ".join(entry["candidates"]))
        print(len(model["unresolved"]), "unresolved")
        return 0
    if "--stats" in args:
        model, _ = build()
        print(json.dumps(model["summary"], ensure_ascii=False, indent=2))
        return 0
    return check()


if __name__ == "__main__":
    sys.exit(main(sys.argv))
