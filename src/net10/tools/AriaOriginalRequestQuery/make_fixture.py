#!/usr/bin/env python3
"""TWF 4.28 ONLY: generate a private synthetic LocalDB Request table.

Never read an original Web.config, real request row, production credentials, the
Admin/T5Web original, or supplementary desktop T5V3. The literal SQL contains
only 5 fabricated rows; the schema comes from the pinned original TWF DLL.
"""
import hashlib
import os
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

PIN = "d8f0570b39635e21b883257cf4d6bba1d6011002921ba5227dcd9cdf567b1faf"
COLS = {
    "RequestID": ("bigint", "false"),
    "BadgeNumber": ("bigint", "false"),
    "WofkflowType": ("int", "false"),
    "CreateDate": ("datetime", "false"),
    "ForDate": ("datetime", "false"),
    "LeaveID": ("int", "false"),
    "LeaveDescriptionID": ("int", "false"),
    "FromTime": ("datetime", "false"),
    "ToTime": ("datetime", "false"),
    "RecordTime": ("datetime", "false"),
    "OverTimeType": ("int", "false"),
    "OverTimeAllowValue": ("float", "false"),
    "IsSuspend": ("bit", "false"),
    "Descriptions": ("nvarchar", "false"),
    "IsDaily": ("bit", "false"),
    "Latitude": ("float", "true"),
    "Longitude": ("float", "true"),
    "ImageData": ("image", "true"),
}
SQL_TYPES = {"bigint": "BIGINT", "int": "INT", "datetime": "DATETIME",
             "float": "FLOAT", "bit": "BIT", "nvarchar": "NVARCHAR(250)",
             "image": "IMAGE"}


def original_entity(dll):
    binary = pathlib.Path(dll).read_bytes()
    if hashlib.sha256(binary).hexdigest() != PIN:
        raise ValueError("Refuse changed Aria-packaged original T5V3.DB.dll")
    prefix = b'<Schema Namespace="T5V3Model.Store"'
    start = binary.find(prefix)
    end = binary.find(b"</Schema>", start)
    if start < 0 or binary.find(prefix, start + 1) >= 0 or end - start > 300000 or end < 0:
        raise ValueError("Expected exactly one original T5 EDMX store resource")
    root = ET.fromstring(binary[start:end + len(b"</Schema>")])
    ns = {"x": root.tag.split("}")[0].strip("{")}
    if root.attrib.get("Provider") != "System.Data.SqlClient":
        raise ValueError("Original provider metadata changed")
    tables = [e for e in root.findall(".//x:EntitySet", ns)
              if e.attrib.get("Name") == "Request" and e.attrib.get("Schema") == "dbo"]
    entities = [e for e in root.findall("x:EntityType", ns)
                if e.attrib.get("Name") == "Request"]
    if len(tables) != 1 or len(entities) != 1 or \
            tables[0].attrib.get("EntityType") != "Self.Request":
        raise ValueError("Original dbo.Request mapping changed")
    entity = entities[0]
    fields = [e for e in entity.findall("x:Property", ns)]
    observed = {e.attrib["Name"]: (e.attrib["Type"], e.attrib.get("Nullable", "true"))
                for e in fields}
    if observed != COLS or len(fields) != len(COLS):
        raise ValueError("Original complete Request table schema drift")
    descriptions = next(e for e in fields if e.attrib["Name"] == "Descriptions")
    if descriptions.attrib.get("MaxLength") != "250":
        raise ValueError("Original description column length changed")
    key = [e.attrib["Name"] for k in entity.findall("x:Key", ns)
           for e in k.findall("x:PropertyRef", ns)]
    if key != ["RequestID"]:
        raise ValueError("Original request primary key drift")
    return fields


def make_sql(fields, database):
    ddl = ",\n".join("  [{}] {} {}".format(e.attrib["Name"],
            SQL_TYPES[e.attrib["Type"]],
            "NOT NULL" if e.attrib.get("Nullable") == "false" else "NULL")
            for e in fields)
    # No row is copied from original. All IDs and dates below are disposable and
    # synthetic, and no database outside the generated CI-owned name is touched.
    dates = (
        (810001, 900101, "2026-10-27T00:00:00", 0, 0),
        (810002, 900101, "2026-10-27T12:00:00", 1, 1),
        (810003, 900101, "2026-10-28T00:00:00", 0, 0),
        (810004, 900101, "2026-10-26T23:59:00", 0, 0),
        (810005, 900102, "2026-10-27T12:00:00", 0, 1),
    )
    inserts = ",\n".join("({}, {}, 0, '2026-10-27T00:00:00', '{}', 0, 0, "
                         "'2026-10-27T09:00:00', '2026-10-27T10:00:00', "
                         "'2026-10-27T08:00:00', 0, 0, {}, N'fixture', {})".format(
                             id_, badge, date, suspended, daily)
                         for id_, badge, date, suspended, daily in dates)
    return """USE [master];
GO
SET NOCOUNT ON;
IF DB_ID(N'{db}') IS NOT NULL THROW 51012, 'Fixture database name collision', 1;
GO
CREATE DATABASE [{db}];
GO
USE [{db}];
GO
CREATE TABLE [dbo].[Request] (
{ddl},
  CONSTRAINT [PK_AriaOriginalRequestFixture] PRIMARY KEY ([RequestID])
);
GO
INSERT INTO [dbo].[Request] ([RequestID], [BadgeNumber], [WofkflowType], [CreateDate],
    [ForDate], [LeaveID], [LeaveDescriptionID], [FromTime], [ToTime], [RecordTime],
    [OverTimeType], [OverTimeAllowValue], [IsSuspend], [Descriptions], [IsDaily]) VALUES
{inserts};
GO
""".format(db=database, ddl=ddl, inserts=inserts)


def main(args):
    if len(args) != 3 or not re.fullmatch(r"AriaRequestT5_[0-9]{6,20}", args[1]):
        raise ValueError("Require pinned original DLL, CI-owned request database, runner-temp output")
    temp = pathlib.Path(os.environ["RUNNER_TEMP"]).resolve()
    dest = pathlib.Path(args[2]).resolve()
    if temp not in dest.parents:
        raise ValueError("Only runner-temp generated SQL output allowed")
    fields = original_entity(args[0])
    dest.write_text(make_sql(fields, args[1]), encoding="utf-8")
    print("Generated original-SSDL-driven private TWF Request fixture with 5 synthetic rows; no SQL opened")


if __name__ == "__main__":
    main(sys.argv[1:])
