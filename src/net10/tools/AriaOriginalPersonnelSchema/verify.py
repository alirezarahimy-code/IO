#!/usr/bin/env python3
"""Read ONLY the SHA-pinned original TWF 4.28 Personnel.dll's EMBEDDED EDMX.

No database, connection string, passwords/rows, config file, BinaryFormatter,
NuGet, service, or third-program source is opened. Print/emit only a fixed
whitelist of column IDENTIFIERS/types and the mapping to the original store.
An EDMX mapping is not runtime SQL Server 2016/authentication equivalence.
"""
import hashlib
import json
import pathlib
import sys
import xml.etree.ElementTree as ET

PINNED_SHA = "468e43ea66b9c591384ddcd033918e6f563a85b4caa2b6e6c4e8b85a4484050f"
FIELDS = {
    "BadgeNumber": ("bigint", None, False),
    "UserName": ("nvarchar", "100", False),
    "Password": ("nvarchar", "250", False),
    "WebUser": ("bit", None, False),
    "FiredDate": ("datetime", None, True),
    "ValidFrom": ("datetime", None, True),
    "ValidTo": ("datetime", None, True),
}


def local(tag):
    return tag.rsplit("}", 1)[-1]


def documents(binary, tag):
    start = 0
    opening = ("<" + tag + " ").encode("ascii")
    closing = ("</" + tag + ">").encode("ascii")
    while (position := binary.find(opening, start)) != -1:
        start = position + len(opening)
        end = binary.find(closing, position)
        if end == -1 or end - position > 200000:
            continue
        try:
            yield ET.fromstring(binary[position:end + len(closing)])
        except ET.ParseError:
            continue  # non-XML occurrence outside the managed resources


def only(elements, description):
    if len(elements) != 1:
        raise ValueError("expected exactly one original " + description)
    return elements[0]


def inspect(binary):
    schemas = list(documents(binary, "Schema"))
    conceptual = only([s for s in schemas if s.attrib.get("Namespace") == "personnelModel"],
                      "conceptual model")
    store = only([s for s in schemas if s.attrib.get("Namespace") == "personnelModel.Store"],
                 "SQL store model")
    mappings = list(documents(binary, "Mapping"))
    mapping = only([m for m in mappings if any(
        local(el.tag) == "EntitySetMapping" and el.attrib.get("Name") == "UserInfoes"
        for el in m.iter())], "UserInfoes mapping document")
    if store.attrib.get("Provider") != "System.Data.SqlClient":
        raise ValueError("the original SQL provider metadata has drifted")
    conceptual_set = only([el for el in conceptual.iter() if local(el.tag) == "EntitySet"
                           and el.attrib.get("Name") == "UserInfoes"], "conceptual entity set")
    if conceptual_set.attrib.get("EntityType") != "personnelModel.UserInfo":
        raise ValueError("unexpected original conceptual user type")
    table = only([el for el in store.iter() if local(el.tag) == "EntitySet"
                  and el.attrib.get("Name") == "UserInfo"], "store table")
    if table.attrib.get("Schema") != "dbo":
        raise ValueError("original Personnel user schema has drifted")
    store_user = only([el for el in store.iter() if local(el.tag) == "EntityType"
                       and el.attrib.get("Name") == "UserInfo"], "store user type")
    columns = {el.attrib.get("Name"): el for el in store_user
               if local(el.tag) == "Property"}
    conceptual_user = only([el for el in conceptual.iter() if local(el.tag) == "EntityType"
                            and el.attrib.get("Name") == "UserInfo"], "conceptual user type")
    conceptual_columns = {el.attrib.get("Name"): el for el in conceptual_user
                          if local(el.tag) == "Property"}
    user_map = only([el for el in mapping.iter() if local(el.tag) == "EntitySetMapping"
                     and el.attrib.get("Name") == "UserInfoes"], "user entity-set mapping")
    fragment = only([el for el in user_map.iter() if local(el.tag) == "MappingFragment"
                     and el.attrib.get("StoreEntitySet") == "UserInfo"], "store fragment")
    mapped = {el.attrib.get("Name"): el.attrib.get("ColumnName") for el in fragment
              if local(el.tag) == "ScalarProperty"}
    result = {}
    for name, (kind, size, nullable) in FIELDS.items():
        column = columns.get(name)
        concept = conceptual_columns.get(name)
        if column is None or concept is None or mapped.get(name) != name or \
           column.attrib.get("Type") != kind or column.attrib.get("MaxLength") != size or \
           (column.attrib.get("Nullable") != "false") != nullable:
            raise ValueError("original mapped Personnel column contract drift: " + name)
        if name == "UserName" and concept.attrib.get("MaxLength") != "100":
            raise ValueError("original conceptual account-name length drift")
        result[name] = {"store_type": kind, "nullable": nullable}
        if size is not None:
            result[name]["max_length"] = int(size)
    return {"source_sha256": PINNED_SHA, "provider_metadata": "System.Data.SqlClient",
            "table": "dbo.UserInfo", "entity_set": "UserInfoes", "columns": result,
            "scope": "EMBEDDED_EDMX_METADATA_ONLY_NOT_SQL_OR_PASSWORD_GOLDEN"}


def main(arguments):
    if len(arguments) != 2:
        raise SystemExit("Pass the read-only original Personnel.dll and an output fixture path")
    content = pathlib.Path(arguments[0]).read_bytes()
    if hashlib.sha256(content).hexdigest() != PINNED_SHA:
        raise ValueError("refusing an unpinned original Personnel.dll")
    schema = inspect(content)
    pathlib.Path(arguments[1]).write_text(json.dumps(schema, ensure_ascii=False,
                                                   sort_keys=True) + "\n", encoding="utf-8")
    print("Original TWF 4.28 Personnel.dll EDMX: dbo.UserInfo; mapped UserName nvarchar(100), "
          "BadgeNumber bigint, WebUser bit, dates; no rows, passwords, SQL or runtime auth")


if __name__ == "__main__":
    main(sys.argv[1:])
