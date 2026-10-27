#!/usr/bin/env python3
"""Generate an isolated T5 UserPermission fixture from the TWF 4.28 bin ONLY.

This is Aria's own original *packaged dependency*, not T5V3 desktop/Link5 or
T5Web's distinct original. No original configuration, user data or secret read.
"""
import hashlib
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

PIN = "d8f0570b39635e21b883257cf4d6bba1d6011002921ba5227dcd9cdf567b1faf"
TYPES = (-1, 0, 1, 2, 3, 4, 5, 10, 100)
LEVELS = (-1, 0, 1, 2, 3, 4, 5, 10, 100)


def local(tag):
    return tag.rsplit("}", 1)[-1]


def original_store(binary):
    prefix = b'<Schema Namespace="T5V3Model.Store"'
    pos = binary.find(prefix)
    if pos < 0 or binary.find(prefix, pos + 1) >= 0:
        raise ValueError("expected exactly one original T5 store schema")
    end = binary.find(b"</Schema>", pos)
    if end < 0 or end - pos > 300000:
        raise ValueError("invalid original T5 store model size")
    return ET.fromstring(binary[pos:end + 9])


def make_sql(binary, db):
    if not re.fullmatch(r"AriaGoldenT5_[0-9]{6,20}", db):
        raise ValueError("not a CI-owned synthetic T5 database name")
    store = original_store(binary)
    if store.attrib.get("Provider") != "System.Data.SqlClient":
        raise ValueError("unexpected original T5 provider metadata")
    tables = [e for e in store.iter() if local(e.tag) == "EntitySet" and
              e.attrib.get("Name") == "UserPermission" and e.attrib.get("Schema") == "dbo"]
    entities = [e for e in store.iter() if local(e.tag) == "EntityType" and
                e.attrib.get("Name") == "UserPermission"]
    if len(tables) != 1 or len(entities) != 1:
        raise ValueError("unexpected original UserPermission mapping")
    entity = entities[0]
    columns = {e.attrib.get("Name"): e.attrib for e in entity if local(e.tag) == "Property"}
    if len(columns) != 3 or columns != {
        "BadgeNumber": {"Name": "BadgeNumber", "Type": "bigint", "Nullable": "false"},
        "AccessType": {"Name": "AccessType", "Type": "int", "Nullable": "false"},
        "AccessLevel": {"Name": "AccessLevel", "Type": "int", "Nullable": "false"},
    }:
        raise ValueError("original permission table schema drift")
    keys = [e.attrib.get("Name") for k in entity if local(k.tag) == "Key"
            for e in k if local(e.tag) == "PropertyRef"]
    if keys != ["BadgeNumber", "AccessType"]:
        raise ValueError("original permission key drift")
    pairs = [(access, level) for access in TYPES for level in LEVELS]
    # Synthetic badge deterministically encodes one (AccessType, AccessLevel)
    # pair. Test harness accepts ONLY this isolated catalog and these badges.
    rows = ",\n".join("({}, {}, {})".format(910000 + i, access, level)
                       for i, (access, level) in enumerate(pairs))
    return """USE [master];
GO
SET NOCOUNT ON;
IF DB_ID(N'{db}') IS NOT NULL THROW 51011, 'Golden database name collision', 1;
GO
CREATE DATABASE [{db}];
GO
USE [{db}];
GO
CREATE TABLE [dbo].[UserPermission] (
    [BadgeNumber] BIGINT NOT NULL,
    [AccessType] INT NOT NULL,
    [AccessLevel] INT NOT NULL,
    CONSTRAINT [PK_AriaGolden_UserPermission] PRIMARY KEY ([BadgeNumber], [AccessType])
);
GO
INSERT INTO [dbo].[UserPermission] ([BadgeNumber], [AccessType], [AccessLevel])
VALUES
{rows};
GO
""".format(db=db, rows=rows)


def main(argv):
    if len(argv) != 3:
        raise SystemExit("Pass original Aria-packaged T5V3.DB.dll, synthetic T5 DB, runner-temp SQL path")
    data = pathlib.Path(argv[0]).read_bytes()
    if hashlib.sha256(data).hexdigest() != PIN:
        raise ValueError("refuse unpinned original Aria-packaged T5V3.DB.dll")
    pathlib.Path(argv[2]).write_text(make_sql(data, argv[1]), encoding="utf-8")
    print("Generated isolated T5 permission fixture from pinned Aria-packaged EDMX; NO DB touched")


if __name__ == "__main__":
    main(sys.argv[1:])
