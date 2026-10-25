#!/usr/bin/env python3
"""Generate engine/column-types.json — the portal's column type contract.

Evidence, in priority order
---------------------------
1. The vendor DDL shipped with the hardware SDK ("Link5 v4.0.4/*.sql", UTF-16LE).
   It states the SQL type, length, nullability and identity flag exactly as the
   customer's database was created.
2. The pinned engine's ORM entities (Personnel.Model.*, T5V3.Model.*): when the
   DDL does not describe a table, the CLR type of the mapped property is used.

A column that neither source describes is *not* written by the portal: guessing a
type from a name is how BUG-01..BUG-07 happened. Such tables are reported under
"unmodelled" and get a read-only screen plus a probe script for the DBA
(db/column-types-probe.sql).

Usage:
    python3 tools/engine-column-types.py            # rewrite the contract
    python3 tools/engine-column-types.py --check    # fail when stale
"""

from __future__ import annotations

import glob
import hashlib
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
T5WEB = os.path.abspath(os.path.join(HERE, ".."))
TWF = os.path.abspath(os.path.join(T5WEB, "..", "TWF"))
REPO = os.path.abspath(os.path.join(T5WEB, "..", ".."))
PIN = os.path.join(TWF, "engine", "engine-pin.json")
OUT = os.path.join(T5WEB, "engine", "column-types.json")
TSV = os.path.join(T5WEB, "engine", "column-types.tsv")
DDL_GLOB = os.path.join(REPO, "Link5 v4.0.4", "*.sql")

ASSEMBLIES = ["Personnel.dll", "T5V3.Model.dll", "T5V3.DB.dll"]

# Tables the portal's phase-5 modules read or write.
TABLES = [
    "EmpType", "Certificate", "LeaveType", "LeaveDescription", "Calendar", "CalendarHoliday",
    "Ramezan", "Parameter", "Property", "PropertyDropdownValue", "UserInfoProperty",
    "TitlesChart", "Gate", "Deputy", "Shift", "ShiftDetail", "Schedule", "TempSchedule",
    "UserShift", "YearRemain", "LeavesAdded", "Rule", "UserRule", "DepartmentRule",
    "Record", "Request", "RequestAccept", "DailyLeaf", "HourlyLeave", "ValidLeave",
    "Dashboard", "DashboardItem", "PushCommand", "DeviceMessage", "DeviceMessagePersonnel",
    "DownloadRecordsLog", "UserDeviceValid", "UserDeviceUpload", "DeviceOperationLog",
    "CustomWorkflow", "Option", "TimeTable", "AccessGroup", "UserInfo", "Device", "Department",
    "Position", "PositionStep", "PositionStepData", "UserPositionData", "PersonnelDeputy",
    "UserInfoManager", "ReportAccess", "UserPermission", "AllowValue", "EditedValue", "AppRule",
    "Area", "AreaPoint", "DownloadTime", "DownloadTimeDevice", "SystemLog", "Card", "Workflow",
    "VaccineLog", "UserInfoVaccinationDetails", "Template", "UserBioData", "Ghadir1", "OperationLog",
    "DeviceProps", "DeviceConnectionState", "DeviceBioTypes", "ManagerDevice", "UserInfoTitles",
]


CREATE_RE = re.compile(r"(?is)CREATE\s+TABLE\s+\[dbo\]\.\[(\w+)\]\s*\((.*?)\n\)\s*;?")
COLUMN_RE = re.compile(
    r"^\s*\[(\w+)\]\s*\[(\w+)\](?:\s*\((\d+)(?:\s*,\s*(\d+))?\))?"
    r"(?:\s+(IDENTITY\(\d+,\s*\d+\)|NOT\s+NULL|NULL))*", re.I)

PRIMITIVE = {
    0x02: "bool", 0x03: "string", 0x04: "sbyte", 0x05: "byte", 0x06: "short",
    0x07: "ushort", 0x08: "int", 0x09: "uint", 0x0A: "long", 0x0B: "ulong",
    0x0C: "float", 0x0D: "double", 0x0E: "string", 0x18: "IntPtr", 0x19: "object",
    0x1C: "object",
}


# --------------------------------------------------------------------- vendor DDL

def read_ddl():
    """{table: {column: {type, length, nullable, identity}}} from the vendor scripts."""
    tables = {}
    for path in sorted(glob.glob(DDL_GLOB)):
        raw = open(path, "rb").read()
        text = raw.decode("utf-16-le", "ignore")
        for name, body in CREATE_RE.findall(text):
            columns = {}
            for line in body.split("\n"):
                if line.strip().upper().startswith("CONSTRAINT"):
                    continue
                match = COLUMN_RE.match(line)
                if not match or not match.group(1):
                    continue
                column = match.group(1)
                if column.upper() in ("CONSTRAINT", "PRIMARY", "INDEX"):
                    continue
                tail = line.upper()
                columns[column] = {
                    "type": match.group(2).lower(),
                    "length": int(match.group(3)) if match.group(3) else None,
                    "nullable": "NOT NULL" not in tail,
                    "identity": "IDENTITY" in tail,
                    "source": "ddl:" + os.path.basename(path),
                }
            if columns:
                tables.setdefault(name, columns)
    return tables


# -------------------------------------------------------------------- engine ORM

def blob(item):
    for attr in ("value", "data", "blob"):
        if hasattr(item, attr):
            value = getattr(item, attr)
            if isinstance(value, (bytes, bytearray)):
                return bytes(value)
    return bytes(item)


def read_compressed(data, index):
    first = data[index]
    if first & 0x80 == 0:
        return first, index + 1
    if first & 0xC0 == 0x80:
        return ((first & 0x3F) << 8) | data[index + 1], index + 2
    return ((first & 0x1F) << 24) | (data[index + 1] << 16) | (data[index + 2] << 8) | data[index + 3], index + 4


# Framework value types that are columns in their own right.
VALUE_TYPES = {
    "DateTime": "datetime", "DateTimeOffset": "datetimeoffset", "TimeSpan": "time",
    "Guid": "uniqueidentifier", "Decimal": "decimal", "Boolean": "bit", "Int32": "int",
    "Int64": "bigint", "Int16": "smallint", "Byte": "tinyint", "Double": "float",
    "Single": "real", "String": "nvarchar",
}


def read_type(data, index, type_rows, type_refs=None):
    """Decode one ELEMENT_TYPE, returning (name, next index).

    Handles the two shapes that actually matter for entities: a plain type, and a
    generic instantiation. `Nullable<T>` is reported as "<inner>?" so the nullability
    of a column survives into the contract; any other generic (IList<T>, ...) is a
    navigation property and is reported as "object" so the caller drops it.
    """
    code = data[index]
    index += 1
    if code in (0x11, 0x12):
        coded, index = read_compressed(data, index)
        row, tag = coded >> 2, coded & 3
        if tag == 0 and 1 <= row <= len(type_rows):
            # A type declared in this assembly (an engine enum, mostly).
            name = str(type_rows[row - 1].TypeName)
            return VALUE_TYPES.get(name, name), index
        if tag == 1 and type_refs and 1 <= row <= len(type_refs):
            # A type from another assembly - and this is where System.DateTime,
            # System.Guid and friends live. Without this branch every nullable date
            # column in the engine looks like "object" and becomes unwritable.
            reference = type_refs[row - 1]
            name = str(reference.TypeName or "")
            namespace = str(reference.TypeNamespace or "")
            if namespace == "System" or namespace == "System.Globalization":
                return VALUE_TYPES.get(name, "object"), index
            return "object", index
        return "object", index
    if code == 0x15:  # GENERICINST
        kind = data[index]
        index += 1
        coded, index = read_compressed(data, index)
        row, tag = coded >> 2, coded & 3
        count, index = read_compressed(data, index)
        args = []
        for _ in range(count):
            arg, index = read_type(data, index, type_rows, type_refs)
            args.append(arg)
        generic = ""
        if tag == 0 and 1 <= row <= len(type_rows):
            generic = str(type_rows[row - 1].TypeName)
        elif tag == 1 and type_refs and 1 <= row <= len(type_refs):
            generic = str(type_refs[row - 1].TypeName or "")
        if generic in ("Nullable`1", "Nullable") and len(args) == 1:
            return args[0] + "?", index
        return "object", index
    if code == 0x1D:
        inner, index = read_type(data, index, type_rows, type_refs)
        return inner + "[]", index
    return PRIMITIVE.get(code, "object"), index


def method_start(type_row):
    if not type_row.MethodList:
        return 0
    return type_row.MethodList[0].row_index or 0


def method_end(type_rows, index, total):
    start = method_start(type_rows[index])
    for later in range(index + 1, len(type_rows)):
        candidate = method_start(type_rows[later])
        if candidate > start:
            return candidate
    return total + 1


def read_orm(bin_dir):
    import dnfile

    found = {}
    for assembly in ASSEMBLIES:
        path = os.path.join(bin_dir, assembly)
        if not os.path.exists(path):
            continue
        dn = dnfile.dnPE(path)
        tables = dn.net.mdtables
        type_rows = list(tables.TypeDef.rows)
        type_refs = list(tables.TypeRef.rows)
        total = len(tables.MethodDef.rows)
        for index, row in enumerate(type_rows):
            name = str(row.TypeName or "")
            if name not in TABLES or name in found:
                continue
            start = method_start(row)
            if start <= 0:
                continue
            props = {}
            for rid in range(start, method_end(type_rows, index, total)):
                method = tables.MethodDef.rows[rid - 1]
                mname = str(method.Name or "")
                if not mname.startswith("get_"):
                    continue
                data = blob(method.Signature)
                position = 1
                flags = data[0]
                if flags & 0x10:
                    _, position = read_compressed(data, position)
                _, position = read_compressed(data, position)
                clr, _ = read_type(data, position, type_rows, type_refs)
                if clr in ("object", "string[]") or clr.endswith("[]"):
                    continue  # navigation property, not a column
                props[mname[4:]] = clr
            if props:
                found[name] = (assembly, props)
    return found


# ------------------------------------------------------------------------ merging

CLR_TO_SQL = {
    "bool": "bit", "byte": "tinyint", "sbyte": "smallint", "short": "smallint",
    "ushort": "int", "int": "int", "uint": "bigint", "long": "bigint", "ulong": "bigint",
    "float": "float", "double": "float", "string": "nvarchar",
    "datetime": "datetime", "datetimeoffset": "datetimeoffset", "time": "time",
    "uniqueidentifier": "uniqueidentifier", "decimal": "decimal", "real": "real",
    "tinyint": "tinyint", "smallint": "smallint", "bigint": "bigint", "bit": "bit",
    "int": "int",
}


def orm_sql_type(clr):
    """(sql type, nullable) for a CLR property type, or None when there is no mapping."""
    nullable = clr.endswith("?")
    base = clr.rstrip("?")
    if base not in CLR_TO_SQL:
        return None  # an enum: the engine stores it as int, but that is a guess we refuse
    value_type = base in ("bool", "int", "long", "double", "float", "decimal", "real",
                          "byte", "short", "datetime", "time", "uniqueidentifier")
    return CLR_TO_SQL[base], (nullable or not value_type)


# Columns the engine ORM does not expose as a property but the table really has, proven
# by two independent sources: the shared schema manifest (src/TWF/db/schema-manifest.json,
# built from vendor DDL + ORM metadata + the legacy data layer) and the portal's own SQL,
# which the engine has been running in production for years. Without them the synthetic
# schema cannot compile `WHERE w.[WorkflowType] = @WorkflowType`, and the Windows CI
# reported exactly that (Msg 207, Invalid column name 'WorkflowType').
#
# The type is what the SQL compares against - an int parameter in both cases - and
# db/verify.sql asks the real server whether the column is there before anything is
# installed. A guess would be appended here silently; this table is the opposite: a
# named, reviewable exception with its evidence written down.
EXTRA_COLUMNS = {
    "Workflow": {
        "WorkflowType": {"type": "int", "nullable": False},
    },
    "Request": {
        # The typo is the vendor's: the column is spelled "WofkflowType" in the engine.
        "WofkflowType": {"type": "int", "nullable": False},
        "CreateDate": {"type": "datetime", "nullable": False},
    },
    "LeaveType": {
        "LeaveID": {"type": "int", "nullable": False},
    },
    "LeavesAdded": {
        "AssignDate": {"type": "datetime", "nullable": True},
        "DailyValue": {"type": "float", "nullable": True},
    },
    "Schedule": {
        "SchName": {"type": "nvarchar", "length": 200, "nullable": True},
        "StartTime": {"type": "time", "nullable": True},
        "EndTime": {"type": "time", "nullable": True},
    },
    "Shift": {
        "Unit": {"type": "int", "nullable": True},
        "WeekEnds": {"type": "nvarchar", "length": 100, "nullable": True},
    },
    "ShiftDetail": {
        "DayIndex": {"type": "int", "nullable": False},
    },
    "UserShift": {
        "EndDate": {"type": "datetime", "nullable": True},
    },
    # Four columns a generic module writes, present in the vendor schema manifest but
    # not modelled by the engine's ORM. They were invisible to the synthetic schema, so
    # the CI install would have failed on those screens with "Invalid column name" while
    # a real server was fine - the opposite of a useful fixture (tools/module-field-gate.py
    # now keeps this list and ModuleRegistry.cs in step).
    "HourlyLeave": {
        "LeaveID": {"type": "int", "nullable": True},
    },
    "LeaveDescription": {
        "LeaveDescriptions": {"type": "nvarchar", "length": 250, "nullable": True},
    },
    "PositionStep": {
        "OperationIndex": {"type": "int", "nullable": True},
    },
    "TempSchedule": {
        "ScheduleID": {"type": "int", "nullable": True},
    },
}

for _table, _columns in EXTRA_COLUMNS.items():
    for _column, _detail in _columns.items():
        _detail.setdefault("length", None)
        _detail.setdefault("identity", False)
        _detail["source"] = "manifest:TWF/db/schema-manifest.json + sql:T5Web.Data"
        _detail["evidence"] = "manifest"


def merge(ddl, orm):
    tables = {}
    unmodelled = []
    for table in TABLES:
        columns = {}
        if table in ddl:
            for column, info in ddl[table].items():
                columns[column] = {
                    "type": info["type"],
                    "length": info["length"],
                    "nullable": info["nullable"],
                    "identity": info["identity"],
                    "source": info["source"],
                    "evidence": "ddl",
                }
        if table in orm:
            assembly, props = orm[table]
            for prop, clr in props.items():
                if prop in columns:
                    continue  # the vendor DDL is authoritative
                mapped = orm_sql_type(clr)
                if mapped is None:
                    continue  # enum or unmapped type: no evidence, so no writing
                sql_type, nullable = mapped
                columns[prop] = {
                    "type": sql_type,
                    "length": None,
                    "nullable": nullable,
                    "identity": False,
                    "source": "orm:" + assembly,
                    "evidence": "orm",
                    "clr": clr,
                }
        for column, info in EXTRA_COLUMNS.get(table, {}).items():
            columns.setdefault(column, dict(info))
        if columns:
            sources = sorted({c["source"] for c in columns.values()})
            tables[table] = {"sources": sources, "columns": dict(sorted(columns.items()))}
        else:
            unmodelled.append(table)
    return tables, unmodelled


def main(argv):
    pin = json.load(open(PIN, encoding="utf-8"))
    bin_dir = os.path.join(REPO, pin["pinned_from"])

    ddl = read_ddl()
    orm = read_orm(bin_dir)
    tables, unmodelled = merge(ddl, orm)

    ddl_files = []
    for path in sorted(glob.glob(DDL_GLOB)):
        digest = hashlib.sha256(open(path, "rb").read()).hexdigest()
        ddl_files.append({"file": os.path.relpath(path, REPO).replace(os.sep, "/"), "sha256": digest})

    payload = {
        "description": (
            "Column types for every table a phase-5 module touches. Vendor DDL wins; "
            "the pinned engine's ORM entities fill the gaps. A column absent here is not "
            "written by the portal - see db/column-types-probe.sql."
        ),
        "generated_by": "tools/engine-column-types.py",
        "engine_build": pin["pinned_build"],
        "ddl_sources": ddl_files,
        "tables": tables,
        "unmodelled": sorted(unmodelled),
    }
    text = json.dumps(payload, indent=2, ensure_ascii=False, sort_keys=True) + "\n"

    # Flat companion file: the test suite reads this instead of parsing JSON, so a
    # broken parser can never be mistaken for a passing schema check.
    rows = ["# table\tcolumn\ttype\tevidence", "# generated by tools/engine-column-types.py"]
    for table in sorted(tables):
        for column, info in tables[table]["columns"].items():
            rows.append("\t".join([table, column, info["type"], info["evidence"]]))
    tsv = "\n".join(rows) + "\n"

    if "--check" in argv:
        current = open(OUT, encoding="utf-8").read() if os.path.exists(OUT) else ""
        current_tsv = open(TSV, encoding="utf-8").read() if os.path.exists(TSV) else ""
        if current != text or current_tsv != tsv:
            print("STALE: engine/column-types.{json,tsv} do not match the sources.")
            print("       run: python3 tools/engine-column-types.py")
            return 1
        print("OK: engine/column-types.json is current (%d tables, %d columns)"
              % (len(tables), sum(len(t["columns"]) for t in tables.values())))
        return 0

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as handle:
        handle.write(text)
    with open(TSV, "w", encoding="utf-8") as handle:
        handle.write(tsv)
    print("wrote engine/column-types.json: %d tables, %d columns, %d unmodelled (%s)"
          % (len(tables), sum(len(t["columns"]) for t in tables.values()), len(unmodelled),
             ", ".join(unmodelled) if unmodelled else "-"))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
