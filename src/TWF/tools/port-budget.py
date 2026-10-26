#!/usr/bin/env python3
"""The distance from TWF ("آریا") to .NET 10, as numbers, with a ratchet.

TWF differs from T5Web in exactly one way that decides the whole plan: its
*web layer* calls the T5 engine directly. T5Web needed one engine file replaced;
TWF needs 401 call sites in 32 files routed through the adapter interface that
already exists (`IT5BackendAdapter`). That is a measurable distance, and this
script is the measurement:

    python3 tools/port-budget.py --check     # fail if the distance grew
    python3 tools/port-budget.py --write     # re-measure after a port step
    python3 tools/port-budget.py --list      # which files still couple, and how much

Metrics, and the fix that removes each:

    systemWebFiles       System.Web.Mvc -> Microsoft.AspNetCore.Mvc
    engineFiles          `using T5V3.*` / `using Personnel.*` -> IT5BackendAdapter
    engineCallSites      `*DA`, `T5V3DBManager` calls -> the adapter (401 today)
    configurationManager WebConfigurationManager -> IConfiguration
    drawingFiles         System.Drawing.Common (Windows-only on Core) -> see docs
    webApiFiles          System.Web.Http -> ASP.NET Core Web API
    virtualPathFiles     VirtualPathProvider (no Core equivalent) -> delete: Core serves
                         views from disk natively, so this file is a *deletion*, not a port
    httpContextCurrent   IHttpContextAccessor
    antiForgery / actionResult / viewBag / views   mechanical, counted for the estimate

The budget is a ceiling: the CI fails when a number grows, so a port step cannot
quietly re-add System.Web next to the code it just ported.
"""
import argparse
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)          # src/TWF
BUDGET = os.path.join(ROOT, "port-budget.json")
SKIP_DIRS = {"obj", "bin", ".vs", "packages", "OriginalDecompiled"}

# (key, regex, kind, what removes it)
METRICS = [
    ("webFiles", None, "file", "C# files in the web project"),
    ("systemWebFiles", r"(?m)^\s*using\s+System\.Web(\.|;)", "file", "ASP.NET Core MVC"),
    ("controllers", r"class\s+\w+Controller\s*:", "file", "Controller -> ControllerBase/Controller"),
    ("views", None, "cshtml", "Razor: @model/@inherits lines"),
    ("engineFiles", r"(?m)^\s*using\s+(Personnel|T5V3)(\.|;)", "file", "IT5BackendAdapter (engine-free seam)"),
    ("engineCallSites", r"\b[A-Z][A-Za-z0-9_]*DA\b|\bT5V3DBManager\b", "occurrence", "adapter methods"),
    ("configurationManager", r"ConfigurationManager", "file", "IConfiguration"),
    ("drawingFiles", r"(?m)^\s*using\s+System\.Drawing", "file", "System.Drawing.Common (Windows-only)"),
    ("webApiFiles", r"(?m)^\s*using\s+System\.Web\.Http", "file", "ASP.NET Core Web API"),
    ("virtualPathFiles", r"System\.Web\.Hosting", "file", "delete: Core serves views from disk"),
    ("httpContextCurrent", r"HttpContext\.Current", "occurrence", "IHttpContextAccessor"),
    ("actionResult", r"\bActionResult\b", "occurrence", "IActionResult"),
    ("viewBag", r"\bViewBag\.", "occurrence", "ViewData / typed models"),
    ("antiForgery", r"\bValidateAntiForgeryToken\b", "occurrence", "AutoValidateAntiforgeryToken"),
]


def project_files(project="TWF.Web"):
    for base, dirs, files in os.walk(os.path.join(ROOT, project)):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in files:
            yield os.path.join(base, name)


def count(key, pattern, kind, _note):
    if kind == "cshtml":
        return sum(1 for p in project_files() if p.endswith(".cshtml"))
    if pattern is None:
        return sum(1 for p in project_files() if p.endswith(".cs"))
    hits = 0
    for path in project_files():
        if not path.endswith(".cs"):
            continue
        with open(path, encoding="utf-8-sig", errors="replace") as handle:
            text = handle.read()
        found = re.findall(pattern, text)
        if kind == "file":
            hits += 1 if found else 0
        else:
            hits += len(found)
    return hits


def files_behind(pattern):
    out = []
    for path in project_files():
        if not path.endswith(".cs"):
            continue
        with open(path, encoding="utf-8-sig", errors="replace") as handle:
            text = handle.read()
        found = re.findall(pattern, text)
        if found:
            out.append((os.path.relpath(path, ROOT), len(found)))
    return sorted(out, key=lambda pair: (-pair[1], pair[0]))


def main(argv=None):
    parser = argparse.ArgumentParser(description="Measure TWF's distance to .NET 10 and enforce the budget.")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--check", action="store_true")
    group.add_argument("--write", action="store_true")
    group.add_argument("--list", action="store_true")
    args = parser.parse_args(argv)

    now = {key: count(key, pattern, kind, note) for key, pattern, kind, note in METRICS}

    if args.write:
        payload = {
            "stage": "pre-port: .NET Framework 4.7.2, MVC 5 + Web API 2",
            "note": "Lowered in the same commit that removes coupling; see docs/18-three-system-upgrade-plan.md.",
            "budget": now,
        }
        with open(BUDGET, "w", encoding="utf-8", newline="\n") as handle:
            json.dump(payload, handle, indent=2, ensure_ascii=False)
            handle.write("\n")
        print("wrote " + os.path.relpath(BUDGET, ROOT))
        for key, value in now.items():
            print("  %-22s %d" % (key, value))
        return 0

    if args.list:
        for key, pattern, kind, note in METRICS:
            if pattern is None or kind == "cshtml":
                continue
            behind = files_behind(pattern)
            if not behind:
                continue
            print("%s (%d files) - %s" % (key, len(behind), note))
            for name, amount in behind[:40]:
                print("    %-58s %d" % (name, amount))
        return 0

    with open(BUDGET, encoding="utf-8") as handle:
        budget = json.load(handle)["budget"]

    grew, total = [], 0
    for key, value in now.items():
        limit = budget.get(key, value)
        print("  %-22s %4d / %-4d %s" % (key, value, limit, "ok" if value <= limit else "OVER"))
        if value > limit:
            grew.append((key, limit, value))
        total += value
    print("  %-22s %4d" % ("total coupling points", total))

    if grew:
        for key, limit, value in grew:
            print("::error::TWF %s grew from %d to %d; the distance to .NET 10 must only shrink "
                  "(docs/18-three-system-upgrade-plan.md)" % (key, limit, value))
        return 1

    print("::notice::TWF PORT | webFiles %d | System.Web %d | engine files %d | engine calls %d | "
          "controllers %d | views %d | Drawing %d | WebApi %d | VirtualPath %d | points %d"
          % (now["webFiles"], now["systemWebFiles"], now["engineFiles"], now["engineCallSites"],
             now["controllers"], now["views"], now["drawingFiles"], now["webApiFiles"],
             now["virtualPathFiles"], total))
    print("OK: TWF's distance to .NET 10 did not grow")
    return 0


if __name__ == "__main__":
    sys.exit(main())
