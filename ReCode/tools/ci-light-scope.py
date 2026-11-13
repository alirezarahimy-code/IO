#!/usr/bin/env python3
"""Select only lightweight checks; never decides whether to skip SQL/platform CI.
The push event's before..after range includes every commit, not just HEAD~1.
Unknown/initial history conservatively enables all lightweight checks.
"""
import os
from pathlib import Path
import re
import subprocess


def scope(paths):
    ui = preview = False
    for path in paths:
        if path.startswith("ReCode/preview/"):
            preview = True
        elif path.startswith("ReCode/src/IO.Web/Ui/") or path.endswith("-ui-check.cjs"):
            ui = True
        elif path.startswith(("ReCode/docs/",)) or path.endswith(".md"):
            continue
        else:
            # Workflow/tooling changes or unexpected files: do not under-test.
            ui = preview = True
    return {"ui": ui, "preview": preview}


def changed_paths():
    base, head = os.environ.get("IO_CI_BASE", ""), os.environ.get("IO_CI_HEAD", "")
    if not all(re.fullmatch(r"[0-9a-f]{40}", ref) and ref != "0" * 40 for ref in (base, head)):
        return None
    try:
        data = subprocess.check_output(
            ["git", "diff", "--name-only", "-z", base, head, "--"],
            cwd=Path(__file__).resolve().parents[2],
        )
        return data.decode("utf-8").rstrip("\0").split("\0") if data else []
    except (subprocess.CalledProcessError, UnicodeError):
        return None


if __name__ == "__main__":
    paths = changed_paths()
    selected = scope(paths) if paths is not None else {"ui": True, "preview": True}
    text = "".join(f"{key}={str(value).lower()}\n" for key, value in selected.items())
    print(text, end="")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write(text)
