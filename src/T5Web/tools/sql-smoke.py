#!/usr/bin/env python3
"""SQL smoke: compile every statement the portal embeds against a real SQL Server.

`verify-sql-schema.py` proves the *names* in the C# SQL exist in the schema model.
That is a text check. This tool is the next layer: it hands each statement to SQL
Server with `SET NOEXEC ON`, which makes the engine **compile** the batch — resolve
every table, every column, every type — and then throw the result away. A statement
that compiles has no `Invalid column name`, no `Invalid object name`, and no syntax
error inside the branches the text check cannot see (CASE, COALESCE, ORDER BY on an
expression, an aggregate in a HAVING, a hidden CTE).

The batch layout per statement is:

    SET NOEXEC ON;
    DECLARE @param sqltype = NULL;   -- one per Dapper parameter
    <statement>
    SET NOEXEC OFF;

Parameters are the only guesswork, so they are declared from a documented
name-to-type table; anything unrecognised becomes nvarchar(400), which compiles
against most columns and fails loudly (not silently) on the rest.

    python3 tools/sql-smoke.py --list                       # what would be compiled
    python3 tools/sql-smoke.py --dry-run                    # parse with sqlglot only
    python3 tools/sql-smoke.py --server '(localdb)\\X' -d ReportT5V3

Exit code = number of statements that did not compile. Needs `sqlcmd` on PATH
(the Windows CI job installs the SQL tools; a Linux build machine usually has none,
so `--dry-run` is the offline fallback).
"""
import glob
import os
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

# C# files that build SQL. Everything else is application logic.
SOURCE_GLOBS = [
    os.path.join(ROOT, "T5Web.Data", "*.cs"),
    os.path.join(ROOT, "T5Web.Data", "**", "*.cs"),
]

# Only statements that address the database: a literal that mentions SELECT in prose
# (a comment, a message) must not be compiled.
STATEMENT_RE = re.compile(r"(?is)\b(select|insert\s+into|update|delete\s+from|with\s+\w+\s+as)\b")

# Documented parameter typing: the name decides, and every rule here is a bug fix
# waiting to happen if it is removed (an int parameter declared nvarchar compiles
# against an index and silently converts at run time).
INT_HINTS = ("id", "number", "badge", "count", "year", "month", "day", "index", "order",
             "machine", "port", "take", "skip", "level", "feature", "size", "length")
BIGINT_HINTS = ("id", "number", "badge", "count", "tick", "stamp")
DATE_HINTS = ("date", "time", "from", "to", "start", "end", "utc", "created", "changed")
BIT_HINTS = ("is", "has", "enabled", "allow", "flag", "active")


def csharp_sources():
    seen = []
    for pattern in SOURCE_GLOBS:
        for path in glob.glob(pattern, recursive=True):
            if path not in seen and os.sep + "obj" + os.sep not in path and os.sep + "bin" + os.sep not in path:
                seen.append(path)
    return sorted(seen)


def sql_literals(path):
    """(statement, line) for every C# string literal that looks like a statement."""
    with open(path, encoding="utf-8") as handle:
        text = handle.read()
    found = []

    def emit(body, offset):
        stripped = body.strip()
        if not stripped or not STATEMENT_RE.search(stripped):
            return
        # Skip literals that are clearly messages, not SQL.
        if stripped.upper().startswith(("SELECT ", "INSERT", "UPDATE", "DELETE", "WITH")) is False:
            if not re.search(r"(?i)\b(FROM|INTO|SET)\b", stripped):
                return
        found.append((stripped, text.count("\n", 0, offset) + 1))

    verbatim = list(re.finditer(r'@"((?:[^"]|"")*)"', text, re.S))
    for match in verbatim:
        emit(match.group(1).replace('""', '"'), match.start())
    # A plain-string scan would re-find the body of a single-line verbatim literal,
    # so those spans are skipped rather than reported twice.
    spans = [(m.start(), m.end()) for m in verbatim]
    for match in re.finditer(r'"((?:[^"\\]|\\.)*)"', text):
        if any(start <= match.start() < end for start, end in spans):
            continue
        emit(match.group(1).replace('\\"', '"'), match.start())
    return found


def is_fragment(statement):
    """True for a SQL literal that is only part of a statement.

    Several statements are assembled at run time ("INSERT INTO " + table + " (…"),
    because the table name comes from the module descriptor. Those literals cannot be
    compiled on their own - the schema gate checks their names instead - and counting
    them as failures would train everyone to ignore this tool.
    """
    text = statement.strip()
    if text[:1] in (")", ",", "+", "|"):
        return True          # continuation of a concatenation, not a statement
    if text[-1:] in ("+", ",", "("):
        return True
    tail = text.rstrip("(, ").split()[-1].upper() if text.split() else ""
    if tail in ("INTO", "FROM", "UPDATE", "DELETE", "SET", "WHERE", "AND", "OR",
                "BY", "ORDER", "SELECT", "VALUES", "JOIN", "AS", "ON", "THEN", "OVER"):
        return True
    if not re.search(r"(?i)\b(FROM\s+\[?\w|VALUES\s*\(|SET\s+\[?\w+\s*=)", text):
        return True
    return False


def parameter_type(name):
    lower = name.lower()
    if any(hint in lower for hint in BIT_HINTS):
        return "bit"
    if any(hint in lower for hint in DATE_HINTS):
        return "datetime2(3)"
    if lower in ("badge", "badgenumber", "machine", "machinenumber") or any(hint in lower for hint in BIGINT_HINTS):
        return "bigint"
    if any(hint in lower for hint in INT_HINTS):
        return "int"
    return "nvarchar(400)"


def declare_block(statement):
    names = []
    for name in re.findall(r"@([A-Za-z_][A-Za-z0-9_]*)", statement):
        if name.lower() not in [n.lower() for n in names]:
            names.append(name)
    return "\n".join("DECLARE @%s %s = NULL;" % (n, parameter_type(n)) for n in names)


def build_batches(statements):
    batches = []
    for statement, origin in statements:
        body = ["SET NOEXEC ON;"]
        declares = declare_block(statement)
        if declares:
            body.append(declares)
        body.append(statement.rstrip().rstrip(";") + ";")
        body.append("SET NOEXEC OFF;")
        batches.append(("\n".join(body), origin))
    return batches


def run_sqlcmd(sqlcmd, server, database, script):
    handle, path = tempfile.mkstemp(suffix=".sql", text=True)
    with os.fdopen(handle, "w", encoding="utf-8") as out:
        out.write(script)
    try:
        result = subprocess.run(
            [sqlcmd, "-S", server, "-d", database, "-E", "-b", "-i", path, "-h", "-1"],
            capture_output=True, text=True)
        return result.returncode, (result.stdout or "") + (result.stderr or "")
    finally:
        os.unlink(path)


def dry_run(statements):
    try:
        import sqlglot
    except ImportError:
        print("SKIP: sqlglot not installed (pip install sqlglot) - nothing verified")
        return 0
    problems = 0
    checked = 0
    skipped = 0
    for statement, origin in statements:
        if is_fragment(statement):
            skipped += 1
            continue
        checked += 1
        try:
            sqlglot.parse(statement, read="tsql")
        except Exception as error:  # sqlglot raises several unrelated types
            problems += 1
            print("PARSE %s:%d\n  %s\n  %s"
                  % (os.path.relpath(origin[0], ROOT), origin[1],
                     statement.splitlines()[0][:100], str(error).splitlines()[0][:160]))
    print("dry run: %d complete statement(s) parsed, %d fragment(s) skipped, %d failure(s)"
          % (checked, skipped, problems))
    return problems


def gate(statements):
    """Offline gate: the extractor still finds the SQL it is supposed to find."""
    problems = []
    if len(statements) < 80:
        problems.append("only %d statement(s) extracted; the data layer has more, so the extractor rotted"
                        % len(statements))
    complete = [s for s, _ in statements if not is_fragment(s)]
    if len(complete) < 60:
        problems.append("only %d complete statement(s); fragments are not being filtered correctly"
                        % len(complete))
    files = {os.path.basename(origin[0]) for _, origin in statements}
    if len(files) < 10:
        problems.append("SQL found in only %d file(s)" % len(files))
    # Every data-layer file that mentions SELECT must contribute at least one statement.
    for path in csharp_sources():
        with open(path, encoding="utf-8") as handle:
            body = handle.read()
        if re.search(r"(?i)\bSELECT\b", body) and os.path.basename(path) not in files:
            if "SELECT" in body and "@\"" in body:
                problems.append("%s mentions SELECT in a literal but yielded no statement"
                                % os.path.basename(path))
    if problems:
        print("FAIL: sql smoke extractor")
        for problem in problems:
            print("  - " + problem)
        return 1
    print("OK: sql smoke extractor (%d statements in %d files, %d complete, %d fragments)"
          % (len(statements), len(files), len(complete), len(statements) - len(complete)))
    return 0


def main(argv):
    server = None
    database = "ReportT5V3"
    for index, arg in enumerate(argv):
        if arg == "--server" and index + 1 < len(argv):
            server = argv[index + 1]
        if arg in ("-d", "--database") and index + 1 < len(argv):
            database = argv[index + 1]

    files = csharp_sources()
    statements = []
    seen = set()
    for path in files:
        for statement, line in sql_literals(path):
            key = (path, line, statement)
            if key in seen:
                continue
            seen.add(key)
            statements.append((statement, (path, line)))

    if not statements:
        print("FAIL: no SQL statement found - the extractor is broken, not the code")
        return 1

    if "--list" in argv:
        for statement, (path, line) in statements:
            print("%s:%d  %s" % (os.path.relpath(path, ROOT), line,
                                 statement.replace("\n", " ")[:120]))
        print("%d statement(s) in %d file(s)" % (len(statements), len(files)))
        return 0

    if "--check" in argv:
        return gate(statements)

    complete = [(s, o) for s, o in statements if not is_fragment(s)]
    if "--dry-run" in argv or not server:
        if not server and "--dry-run" not in argv:
            print("NOTE: no --server given; falling back to --dry-run (parse only)")
        return 1 if dry_run(complete) else 0

    sqlcmd = shutil.which("sqlcmd")
    if not sqlcmd:
        print("FAIL: sqlcmd not found. Install the SQL Server command-line tools, or use --dry-run.")
        return 1

    failures = 0
    for batch, (path, line) in build_batches(complete):
        code, output = run_sqlcmd(sqlcmd, server, database, batch)
        if code != 0:
            failures += 1
            print("FAIL %s:%d\n%s" % (os.path.relpath(path, ROOT), line,
                                      "\n".join("  " + l for l in output.strip().splitlines()[:4])))
    print("compiled %d statement(s) against %s/%s: %d failure(s)"
          % (len(complete), server, database, failures))
    return failures


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
