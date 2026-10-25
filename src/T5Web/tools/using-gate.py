#!/usr/bin/env python3
"""Using gate: a type that is used must have its namespace imported.

This is the cheapest compiler complaint to catch offline, and it cost three CI
rounds in a row: `AdminFeature` (T5Web.Domain), `XmlDocument` (System.Xml) and
`IDbConnection` (System.Data) were all used in files that never imported the
namespace. The parse gate is happy - the identifier is valid C# - and the lint
gate is happy - the braces balance. Only a compiler notices.

The map below is deliberately small and explicit: symbols this codebase actually
uses, each with the exact `using` that makes it compile. An unknown symbol is not
guessed at; it is left to MSBuild.

    python3 tools/using-gate.py
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

# symbol -> the using that provides it. Order matters only for reporting.
REQUIRED = {
    "IDbConnection": "System.Data",
    "IDbTransaction": "System.Data",
    "IDbCommand": "System.Data",
    "DataTable": "System.Data",
    "DataRow": "System.Data",
    "SqlConnection": "System.Data.SqlClient",
    "SqlCommand": "System.Data.SqlClient",
    "SqlParameter": "System.Data.SqlClient",
    "SqlException": "System.Data.SqlClient",
    "CultureInfo": "System.Globalization",
    "XmlDocument": "System.Xml",
    "XDocument": "System.Xml.Linq",
    "AdminFeature": "T5Web.Domain",
    "AdminFeatures": "T5Web.Domain",
    "Controller": "System.Web.Mvc",
    "ActionResult": "System.Web.Mvc",
    "HttpContextBase": "System.Web",
    "ConfigurationManager": "System.Configuration",
    "Dapper": "Dapper",           # via `using Dapper;`
    "DynamicParameters": "Dapper",
}

SKIP_DIRECTORIES = {"bin", "obj", ".git"}


def source_files():
    for directory, dirs, files in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRECTORIES]
        for name in sorted(files):
            if name.endswith(".cs"):
                yield os.path.join(directory, name)


def strip_noise(text):
    """Remove comments and string literals before looking for a symbol.

    `Dapper` appears in a sentence in one file's message string, `Controller` in
    another's doc comment. Neither is a use of the type, and reporting them would
    teach everyone to ignore this gate.
    """
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.S)
    text = re.sub(r"//[^\n]*", " ", text)
    text = re.sub(r'@"(?:[^"]|"")*"', '""', text, flags=re.S)
    text = re.sub(r'"(?:[^"\\\n]|\\.)*"', '""', text)
    return text


def namespace_scopes(text):
    """The file's namespace plus its ancestors - they are in scope without a using."""
    match = re.search(r"^\s*namespace\s+([\w.]+)", text, re.M)
    if not match:
        return set()
    parts = match.group(1).split(".")
    return {".".join(parts[:i]) for i in range(1, len(parts) + 1)}


def main():
    problems = []
    checked = 0
    for path in source_files():
        raw = io.open(path, encoding="utf-8").read()
        usings = set(re.findall(r"^\s*using\s+([\w.]+)\s*;", raw, re.M))
        usings |= namespace_scopes(raw)
        text = strip_noise(raw)
        relative = os.path.relpath(path, ROOT)
        checked += 1
        # A file that declares the type itself needs no import for it.
        declared = set(re.findall(r"\b(?:class|interface|struct|enum)\s+(\w+)", text))
        for symbol, namespace in REQUIRED.items():
            if symbol in declared or namespace in usings:
                continue
            # Word-boundary match, but not inside a comment-only mention is not worth
            # distinguishing: a mention in a comment is a hint to import it anyway.
            if re.search(r"(?<![\w.])" + re.escape(symbol) + r"\b", text):
                problems.append("%s uses %s without `using %s;`"
                                % (relative, symbol, namespace))

    if problems:
        print("FAIL: missing usings")
        for problem in sorted(set(problems)):
            print("  - " + problem)
        return 1

    print("OK: %d C# file(s) import every namespace their types need" % checked)
    return 0


if __name__ == "__main__":
    sys.exit(main())
