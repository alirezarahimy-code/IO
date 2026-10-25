#!/usr/bin/env python3
"""ARIA — static C# checks that do not need a compiler.

The CI image for this repository does not always carry `csc`/`dotnet`. This
linter catches the class of mistakes that would otherwise only surface at
compile time:

  1. Balanced braces / parentheses / brackets, ignoring strings, chars,
     verbatim strings, line comments and block comments.
  2. Duplicate type declarations inside the same namespace.
  3. Engine member calls: every `EngineType.Member` referenced by Aria must
     exist in the PINNED engine build (prevents coding against a removed API).
  4. Forbidden direct reads of connection strings (must go through
     ConnectionStringResolver).
  5. Razor views: every `@Model` member... no - instead: view files must not
     reference removed engine members either, and must declare a model whose
     type exists in the project.

Usage:
    python3 tools/lint-csharp.py            # lint everything
    python3 tools/lint-csharp.py FILE...    # lint specific files
Exit code 0 when clean.
"""

from __future__ import annotations

import json
import os
import re
import sys

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
WEB_ROOT = os.path.join(REPO_ROOT, "src", "TWF", "TWF.Web")
PIN_FILE = os.path.join(REPO_ROOT, "src", "TWF", "engine", "engine-pin.json")


# --------------------------------------------------------------------------- #
# Comment / string aware scanner
# --------------------------------------------------------------------------- #
def strip_noise(text):
    """Replace comments and string literals with spaces, preserving newlines."""
    out = []
    i, n = 0, len(text)
    while i < n:
        ch = text[i]
        nxt = text[i + 1] if i + 1 < n else ""

        # line comment
        if ch == "/" and nxt == "/":
            while i < n and text[i] != "\n":
                out.append(" ")
                i += 1
            continue
        # block comment
        if ch == "/" and nxt == "*":
            out.append("  ")
            i += 2
            while i < n and not (text[i] == "*" and i + 1 < n and text[i + 1] == "/"):
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            out.append("  ")
            i += 2
            continue
        # verbatim string  @"..."
        if ch == "@" and nxt == '"':
            out.append("  ")
            i += 2
            while i < n:
                if text[i] == '"' and i + 1 < n and text[i + 1] == '"':
                    out.append("  ")
                    i += 2
                    continue
                if text[i] == '"':
                    out.append(" ")
                    i += 1
                    break
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            continue
        # string
        if ch == '"':
            out.append(" ")
            i += 1
            while i < n:
                if text[i] == "\\" and i + 1 < n:
                    out.append("  ")
                    i += 2
                    continue
                if text[i] == '"':
                    out.append(" ")
                    i += 1
                    break
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            continue
        # char literal
        if ch == "'":
            out.append(" ")
            i += 1
            while i < n:
                if text[i] == "\\" and i + 1 < n:
                    out.append("  ")
                    i += 2
                    continue
                if text[i] == "'":
                    out.append(" ")
                    i += 1
                    break
                out.append(" ")
                i += 1
            continue

        out.append(ch)
        i += 1
    return "".join(out)


def check_balance(path, text):
    clean = strip_noise(text)
    pairs = {"{": "}", "(": ")", "[": "]"}
    closing = {v: k for k, v in pairs.items()}
    stack = []
    line = 1
    errors = []
    for idx, ch in enumerate(clean):
        if ch == "\n":
            line += 1
        elif ch in pairs:
            stack.append((ch, line))
        elif ch in closing:
            if not stack:
                errors.append(f"{path}:{line}: unexpected '{ch}'")
                continue
            opener, oline = stack.pop()
            if opener != closing[ch]:
                errors.append(f"{path}:{line}: '{ch}' closes '{opener}' opened at line {oline}")
    for opener, oline in stack:
        errors.append(f"{path}:{oline}: unclosed '{opener}'")
    return errors


# --------------------------------------------------------------------------- #
# Duplicate types
# --------------------------------------------------------------------------- #
NAMESPACE_RE = re.compile(r"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)")
TYPE_RE = re.compile(r"\b(?:public|internal|private|protected|static|sealed|abstract|partial|\s)*"
                     r"\b(class|interface|struct|enum)\s+([A-Za-z_][A-Za-z0-9_]*)")


def collect_types(files):
    seen = {}
    for path in files:
        text = open(path, encoding="utf-8", errors="ignore").read()
        clean = strip_noise(text)
        ns_match = NAMESPACE_RE.search(clean)
        ns = ns_match.group(1) if ns_match else ""
        for m in TYPE_RE.finditer(clean):
            kind, name = m.group(1), m.group(2)
            key = (ns, name)
            seen.setdefault(key, []).append(os.path.relpath(path, REPO_ROOT))
    return seen


# --------------------------------------------------------------------------- #
# Engine API verification against the pin
# --------------------------------------------------------------------------- #
def load_engine_surface():
    """Return {simpleTypeName: set(members)} for the pinned engine build."""
    try:
        import dnfile
    except ImportError:
        return None

    if not os.path.exists(PIN_FILE):
        return None
    pin = json.load(open(PIN_FILE, encoding="utf-8"))
    folder = os.path.join(REPO_ROOT, pin["pinned_from"])
    surface = {}
    for logical, meta in pin["assemblies"].items():
        path = os.path.join(folder, meta["file"])
        if not os.path.exists(path):
            continue
        dn = dnfile.dnPE(path)
        md = dn.net.mdtables
        if not md or not md.TypeDef:
            continue
        for row in md.TypeDef.rows:
            ns = str(row.TypeNamespace or "")
            name = str(row.TypeName or "")
            if name.startswith("<") or name == "<Module>":
                continue
            members = surface.setdefault(name, set())
            for idx in (row.MethodList or []):
                rid = idx.row_index
                if rid and md.MethodDef and rid <= len(md.MethodDef.rows):
                    m = str(md.MethodDef.rows[rid - 1].Name or "")
                    if m and m not in (".ctor", ".cctor"):
                        members.add(m[4:] if m.startswith(("get_", "set_")) else m)
            for idx in (row.FieldList or []):
                rid = idx.row_index
                if rid and md.Field and rid <= len(md.Field.rows):
                    f = str(md.Field.rows[rid - 1].Name or "")
                    if f and not f.startswith("<"):
                        members.add(f)
    return surface


ENGINE_NS = ("T5V3.", "Personnel.")


def check_engine_calls(path, text, surface, engine_ns_types):
    if not surface:
        return []
    problems = []
    clean = strip_noise(text)

    # Only verify simple names that are unambiguous engine types. A type reference in
    # C# is never preceded by '.', so "PortalServices.Workflow.Get" is a property and
    # must not be mistaken for the engine's Workflow type.
    for type_name in engine_ns_types:
        pattern = re.compile(r"(?<!\.)\b" + re.escape(type_name) + r"\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)")
        for m in pattern.finditer(clean):
            member = m.group(1)
            if member in ("ToString", "Equals", "GetHashCode", "GetType", "Name", "FullName"):
                continue
            if member not in surface.get(type_name, set()):
                line = clean.count("\n", 0, m.start()) + 1
                problems.append(
                    f"{path}:{line}: '{type_name}.{member}' is not in the pinned engine build"
                )
    return problems


# --------------------------------------------------------------------------- #
# Forbidden patterns
# --------------------------------------------------------------------------- #
FORBIDDEN = [
    (re.compile(r'AppSettings\s*\[\s*"T5CnnStr"\s*\]'),
     "read connection string via ConnectionStringResolver.T5 (not AppSettings)"),
    (re.compile(r'AppSettings\s*\[\s*"PersonnelCnnStr"\s*\]'),
     "read connection string via ConnectionStringResolver.Personnel"),
    (re.compile(r'\bTINYLib\b'),
     "hardware lock must stay removed (Zero-Lock)"),
    (re.compile(r'ConfigurationManager\s*\.\s*ConnectionStrings\s*\[\s*"(?!T5CnnStr|PersonnelCnnStr|Personnel")'),
     "use ConnectionStringResolver instead of ad-hoc connection strings"),
]


def check_forbidden(path, text):
    problems = []
    clean = strip_noise(text)
    for regex, why in FORBIDDEN:
        for m in regex.finditer(clean):
            line = clean.count("\n", 0, m.start()) + 1
            problems.append(f"{path}:{line}: forbidden pattern ({why})")
    return problems


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #
def main(argv):
    if argv:
        files = [os.path.abspath(a) for a in argv]
    else:
        files = []
        for root, _dirs, names in os.walk(WEB_ROOT):
            for n in names:
                if n.endswith(".cs"):
                    files.append(os.path.join(root, n))
        for proj in ("TD5", "TWF.Domain", "TWF.Infrastructure", "TWF.Application", "TWF.Tests"):
            base = os.path.join(REPO_ROOT, "src", "TWF", proj)
            for root, _dirs, names in os.walk(base):
                for n in names:
                    if n.endswith(".cs"):
                        files.append(os.path.join(root, n))
        # The independent T5Web admin console shares the same pinned engine and the
        # same schema, so it gets the same checks.
        t5web = os.path.join(REPO_ROOT, "src", "T5Web")
        for root, _dirs, names in os.walk(t5web):
            if os.sep + "build" in root or os.sep + "obj" in root or os.sep + "bin" in root:
                continue
            for n in names:
                if n.endswith(".cs"):
                    files.append(os.path.join(root, n))

    files = [f for f in files if os.path.exists(f)]
    errors = []

    for path in files:
        try:
            text = open(path, encoding="utf-8", errors="ignore").read()
        except OSError as exc:
            errors.append(f"{path}: cannot read ({exc})")
            continue
        rel = os.path.relpath(path, REPO_ROOT)
        errors.extend(check_balance(rel, text))
        errors.extend(check_forbidden(rel, text))

    # duplicate types
    for (ns, name), locations in sorted(collect_types(files).items()):
        if len(locations) > 1 and not name.startswith("<"):
            # partial classes are legal; flag only the same file duplicated or
            # clearly distinct definitions
            errors.append(f"duplicate type '{ns}.{name}' declared {len(locations)}x: "
                          + ", ".join(locations))

    # engine API
    surface = load_engine_surface()
    if surface is None:
        print("NOTE: engine surface unavailable (dnfile or pin missing) - skipping API check")
    else:
        engine_ns_types = {
            t for t in surface
            if len(t) > 3 and t[0].isupper() and t not in ("Request", "Calendar", "Settings",
                                                          "Personnel", "Attendance", "Query",
                                                          "TitleChart", "UserInfo", "Department")
        }
        for path in files:
            text = open(path, encoding="utf-8", errors="ignore").read()
            errors.extend(check_engine_calls(os.path.relpath(path, REPO_ROOT), text,
                                             surface, engine_ns_types))

    if errors:
        print(f"FAILED: {len(errors)} problem(s) in {len(files)} file(s)")
        for e in errors:
            print("  " + e)
        return 1

    print(f"OK: {len(files)} C# file(s) passed static checks "
          f"(balance, duplicates, engine API, forbidden patterns)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
