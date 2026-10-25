#!/usr/bin/env python3
"""Fixture gate: the CI seed has to match the code it is supposed to open.

`tools/ci-seed.sql` exists so that a runner can *use* the portal instead of only
knocking on the door: one Windows account in `[UserInfo]` (WebUser = 1), one badge
with a row in `[T5WebAccess]` for every feature. Three things can rot silently:

  * a feature is added to `AdminFeature` and not to the seed, so its screen answers
    403 and the sweep "passes" without ever rendering it;
  * a NOT NULL column is added to `[UserInfo]` and the INSERT stops working - a
    failure that only appears on the runner, after the schema was generated;
  * the seed drifts into the deploy kit, and test data ships to a customer.

The gate reads the C# enum, the typed engine model and the SQL, and compares them.
No server needed.

    python3 tools/ci-fixture-gate.py
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SEED = os.path.join(HERE, "ci-seed.sql")
FEATURES = os.path.join(ROOT, "T5Web.Domain", "AdminFeature.cs")
TYPES = os.path.join(ROOT, "engine", "column-types.json")
DEPLOY = os.path.join(ROOT, "deploy")


def enum_features():
    with open(FEATURES, encoding="utf-8") as handle:
        text = handle.read()
    body = text[text.index("public enum AdminFeature"):]
    body = body[:body.index("}")]
    return {name: int(value) for name, value in re.findall(r"(\w+)\s*=\s*(\d+)", body)}


def seed_rows(text):
    block = text[text.index("USING (VALUES"):]
    block = block[:block.index(") AS source")]
    return {(int(badge), int(feature), int(level))
            for badge, feature, level in re.findall(r"\((\d+),\s*(\d+),\s*(\d+)\)", block)}


def insert_columns(text):
    match = re.search(r"INSERT INTO dbo\.\[UserInfo\]\s*\((.*?)\)\s*VALUES", text, re.S)
    if not match:
        return None
    return [name for name in re.findall(r"\[(\w+)\]", match.group(1))]


def main():
    problems = []
    with open(SEED, encoding="utf-8") as handle:
        text = handle.read()

    features = enum_features()
    if len(features) < 20:
        problems.append("the AdminFeature parser found only %d features" % len(features))

    rows = seed_rows(text)
    if not rows:
        problems.append("the seed has no T5WebAccess rows")

    seeded = {feature for _, feature, _ in rows}
    missing = sorted(name for name, value in features.items() if value not in seeded)
    for name in missing:
        problems.append("AdminFeature.%s (%d) has no row in the seed, so its screen can only answer 403"
                        % (name, features[name]))

    unknown = sorted(value for value in seeded if value not in set(features.values()))
    for value in unknown:
        problems.append("the seed grants feature %d, which is not in AdminFeature" % value)

    levels = {level for _, _, level in rows}
    if levels != {2}:
        problems.append("the fixture badge must be a full administrator (level 2); found %s" % sorted(levels))
    if {badge for badge, _, _ in rows} != {1001}:
        problems.append("the fixture must use badge 1001 only, or two rows would fight over one account")

    # [UserInfo] is created from the typed model; a NOT NULL column that the INSERT
    # does not fill fails at the first run, far from this file.
    with open(TYPES, encoding="utf-8") as handle:
        typed = json.load(handle)["tables"]
    if "UserInfo" not in typed:
        problems.append("engine/column-types.json has no UserInfo table")
    else:
        columns = insert_columns(text)
        if columns is None:
            problems.append("the seed has no INSERT INTO dbo.[UserInfo] block")
        else:
            required = {name for name, meta in typed["UserInfo"]["columns"].items()
                        if not meta.get("nullable", True)}
            forgotten = sorted(required - set(columns))
            for name in forgotten:
                problems.append("UserInfo.%s is NOT NULL but the seed never sets it" % name)
            imaginary = sorted(set(columns) - set(typed["UserInfo"]["columns"]))
            for name in imaginary:
                problems.append("the seed sets UserInfo.%s, which the engine model does not have" % name)

    # The fixture must stay out of the shipping kit.
    for name in sorted(os.listdir(DEPLOY)):
        if not name.endswith(".ps1"):
            continue
        with open(os.path.join(DEPLOY, name), encoding="utf-8") as handle:
            if "ci-seed" in handle.read():
                problems.append("deploy/%s runs the test fixture; the kit must never seed data" % name)
    if "test fixture" not in text.lower():
        problems.append("ci-seed.sql must say in its header that it is a test fixture")

    if problems:
        print("FAIL: the CI fixture does not match the code")
        for problem in sorted(set(problems)):
            print("  - " + problem)
        return 1

    print("OK: the CI fixture covers %d features, badge 1001, and every NOT NULL UserInfo column"
          % len(features))
    return 0


if __name__ == "__main__":
    sys.exit(main())
