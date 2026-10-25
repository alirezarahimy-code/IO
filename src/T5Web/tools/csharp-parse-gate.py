#!/usr/bin/env python3
"""C# parse gate: every .cs file must be syntactically valid C#, not just balanced.

`lint-csharp.py` counts braces and looks for banned patterns. That catches a missing
`}` and nothing else: an operator in the wrong place, a `case` outside a `switch`, a
half-finished edit inside a method body - all of those are brace-balanced and still
will not compile. This gate parses each file with a real C# grammar
(tree-sitter-c-sharp) and reports the exact line of every syntax error.

What this gate is not
---------------------
A parse is not a compile. It proves the *syntax*; it cannot prove that
`AdminSession.CanEdit(AdminFeature.PersonnelView)` exists, that a `return` has the right
type, or that a method is not missing in an interface. Type checking needs a C#
compiler and the referenced assemblies (.NET Framework 4.7.2, System.Web.Mvc, Dapper
and the pinned engine build); that is what the Windows CI job does
(`.github/workflows/windows-build.yml`, docs/14). This gate is the fast half.

    python3 tools/csharp-parse-gate.py            # the gate (build runs this)
    python3 tools/csharp-parse-gate.py --list     # files that would be parsed

Exit code 1 on a syntax error. If tree-sitter-c-sharp is not installed the gate prints
a NOTE and exits 0, because a missing optional package must not fail a build that has
no way to install it (the deployment network is offline).
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SKIP_DIRS = {"bin", "obj", "build", ".git", "packages", "node_modules"}


def csharp_files():
    found = []
    for base, dirs, files in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in files:
            if name.endswith(".cs"):
                found.append(os.path.join(base, name))
    return sorted(found)


def main(argv):
    files = csharp_files()
    if "--list" in argv:
        for path in files:
            print(os.path.relpath(path, ROOT))
        print("%d file(s)" % len(files))
        return 0

    try:
        import tree_sitter
        import tree_sitter_c_sharp
    except ImportError:
        print("OK: C# parse skipped [NOTE: pip install tree-sitter tree-sitter-c-sharp to enable]")
        return 0

    language = tree_sitter.Language(tree_sitter_c_sharp.language())
    parser = tree_sitter.Parser(language)

    problems = []
    for path in files:
        with open(path, "rb") as handle:
            source = handle.read()
        tree = parser.parse(source)
        if not tree.root_node.has_error:
            continue

        def walk(node):
            if node.type == "ERROR" or node.is_missing:
                snippet = source[node.start_byte:node.end_byte][:70].decode("utf-8", "replace")
                problems.append("%s:%d  %s" % (os.path.relpath(path, ROOT),
                                               node.start_point[0] + 1,
                                               snippet.replace("\n", " ") or node.type))
                return True
            found = False
            for child in node.children:
                found = walk(child) or found
                if len(problems) > 40:
                    return found
            return found

        walk(tree.root_node)

    if problems:
        print("FAIL: C# syntax")
        for problem in problems[:40]:
            print("  " + problem)
        return 1

    print("OK: %d C# file(s) parse as C# (syntax only - types are checked on Windows CI)"
          % len(files))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
