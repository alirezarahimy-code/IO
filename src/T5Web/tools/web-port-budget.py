#!/usr/bin/env python3
"""The distance to ASP.NET Core, as numbers, with a ratchet.

`docs/17-net10-path.md` says the web layer is the only thing between this portal
and .NET 10, and that the way to get there without breakage is to make that
distance *only ever shrink*. A promise is not a control, so this script is:

    python3 tools/web-port-budget.py --check     # fail if the distance grew
    python3 tools/web-port-budget.py --write     # re-measure after a port step
    python3 tools/web-port-budget.py --list      # which files still couple

Every metric is counted the same way it is documented, from the files that a
.NET 8/10 build would have to compile. `budget` in web-port-budget.json is the
number agreed for the current stage; a port removes files from that list and the
same commit lowers the budget, because the CI fails otherwise.

Why a ceiling and not a target: the port will be done in slices (one screen at a
time, each proven by a test), and the failure mode that ruins a slice is not a
compile error - it is a new `System.Web` reference appearing next to the ported
code and quietly reintroducing the dependency on the day the assembly is dropped.
"""
import argparse
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
WEB = os.path.join(ROOT, "T5Web.Web")
BUDGET = os.path.join(ROOT, "spike", "web-port-budget.json")

SKIP_DIRS = {"obj", "bin", ".vs", "packages"}

# Each metric: name, what it counts, and the fix that makes it go away.
METRICS = [
    # (key, pattern or None, file-or-occurrence, note)
    ("webFiles", None, "file", "C# files in the web project"),
    ("systemWebFiles", r"(?m)^\s*using\s+System\.Web(\.|;)", "file", "files needing System.Web (MVC, Routing, Http)"),
    ("controllers", r"class\s+\w+Controller\s*:", "file", "controllers (`Controller` base, ActionResult, ViewBag)"),
    ("views", None, "cshtml", "Razor views (port as-is; only the @inherits/@model line changes)"),
    ("httpContextCurrent", r"HttpContext\.Current", "occurrence", "static HttpContext: does not exist in ASP.NET Core"),
    ("configurationManager", r"ConfigurationManager", "file", "System.Configuration: replaced by IConfiguration"),
    ("actionResult", r"\bActionResult\b", "occurrence", "ActionResults: IActionResult in Core"),
    ("viewBag", r"\bViewBag\.", "occurrence", "ViewBag members: ViewData in Core (compiles, but typed is better)"),
    ("antiForgery", r"\bValidateAntiForgeryToken\b", "occurrence", "attribute renamed AutoValidateAntiforgeryToken in Core"),
]


def csharp_files():
    for base, dirs, files in os.walk(WEB):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in files:
            if name.endswith(".cs"):
                yield os.path.join(base, name)


def view_files():
    for base, dirs, files in os.walk(WEB):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in files:
            if name.endswith(".cshtml"):
                yield os.path.join(base, name)


def count(key, pattern, kind, _note):
    if kind == "cshtml":
        return len(list(view_files()))
    cs = list(csharp_files())
    if pattern is None:
        return len(cs)
    hits = 0
    for path in cs:
        with open(path, encoding="utf-8-sig", errors="replace") as handle:
            text = handle.read()
        found = re.findall(pattern, text)
        if kind == "file":
            if found:
                hits += 1
        else:
            hits += len(found)
    return hits


def measure():
    return ({key: count(key, pattern, kind, note) for key, pattern, kind, note in METRICS},
            {key: (pattern, kind, note) for key, pattern, kind, note in METRICS})


def coupled_files(pattern):
    """Which files a metric is made of - the port work list, file by file."""
    out = []
    for path in csharp_files():
        with open(path, encoding="utf-8-sig", errors="replace") as handle:
            if re.search(pattern, handle.read()):
                out.append(os.path.relpath(path, ROOT))
    return sorted(out)


def main(argv=None):
    parser = argparse.ArgumentParser(description="Measure the distance to ASP.NET Core and enforce the budget.")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--check", action="store_true", help="fail when a count is above the committed budget")
    group.add_argument("--write", action="store_true", help="re-measure and rewrite the budget file")
    group.add_argument("--list", action="store_true", help="print the files behind each metric")
    args = parser.parse_args(argv)

    now, meta = measure()

    if args.write:
        payload = {
            "stage": "pre-port: .NET Framework 4.8.1, MVC 5",
            "note": "Lowered in the same commit that removes coupling; see docs/17-net10-path.md.",
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
            if kind == "cshtml" or pattern is None:
                continue
            files = coupled_files(pattern)
            print("%s (%d) - %s" % (key, len(files), note))
            for name in files:
                print("    " + name)
        return 0

    with open(BUDGET, encoding="utf-8") as handle:
        budget = json.load(handle)["budget"]

    grew, total = [], 0
    for key, value in now.items():
        limit = budget.get(key, value)
        state = "ok" if value <= limit else "OVER"
        if value > limit:
            grew.append((key, limit, value))
        print("  %-22s %4d / %-4d %s" % (key, value, limit, state))
        total += value
    print("  %-22s %4d" % ("total coupling points", total))

    if grew:
        for key, limit, value in grew:
            print("::error::%s grew from %d to %d; the distance to ASP.NET Core must only shrink "
                  "(docs/17-net10-path.md)" % (key, limit, value))
        print("::error::if the increase is a deliberate port step, run tools/web-port-budget.py --write "
              "and explain it in the commit message")
        return 1

    print("::notice::WEB PORT | webFiles %d | System.Web %d | controllers %d | views %d | "
          "HttpContext.Current %d | ConfigManager %d | coupling points %d"
          % (now["webFiles"], now["systemWebFiles"], now["controllers"], now["views"],
             now["httpContextCurrent"], now["configurationManager"], total))
    print("OK: the distance to ASP.NET Core did not grow (budget " +
          json.dumps(budget, ensure_ascii=True, sort_keys=True) + ")")
    return 0


if __name__ == "__main__":
    sys.exit(main())
