#!/usr/bin/env python3
"""Encoding gate: a Persian view has to say which encoding it is in.

Razor compiles .cshtml at run time on the server, and it decides the encoding of a file
by its byte-order mark. Without one, IIS falls back to the machine's ANSI code page: on
a Windows runner that is Windows-1252, so the two bytes of `»` (C2 BB) become `Â»`, which
Razor then treats as part of the C# expression and refuses:

    CS1061: 'ImportPreviewModel' does not contain a definition for 'FileNameÂ'

That is not a rendering problem - it is a compile error on one screen, found only by
opening the screen (BUG-42). The same misreading turns every Persian label into garbage
in the other views, silently.

So: every text file that contains non-ASCII must be UTF-8 *with* a BOM, and Web.config
must say so as well, because the BOM only helps for files we ship as views.

    python3 tools/text-encoding-gate.py
"""
import codecs
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SITE = os.path.join(ROOT, "T5Web.Web")
SUFFIXES = (".cshtml", ".css", ".js", ".config")


def main():
    problems = []
    scanned = 0
    with_bom = 0

    for folder, dirs, files in os.walk(SITE):
        dirs[:] = [d for d in dirs if d.lower() not in ("bin", "obj")]
        for name in files:
            if not name.endswith(SUFFIXES):
                continue
            path = os.path.join(folder, name)
            relative = os.path.relpath(path, SITE).replace(os.sep, "/")
            raw = open(path, "rb").read()
            scanned += 1
            if raw.startswith(codecs.BOM_UTF8):
                with_bom += 1
                raw = raw[len(codecs.BOM_UTF8):]
            try:
                text = raw.decode("utf-8")
            except UnicodeDecodeError as error:
                problems.append("%s is not valid UTF-8: %s" % (relative, error))
                continue
            has_non_ascii = any(ord(ch) > 127 for ch in text)
            if has_non_ascii and not open(path, "rb").read().startswith(codecs.BOM_UTF8):
                # The one case that matters: Persian text, no BOM, so IIS guesses.
                first = next((ch for ch in text if ord(ch) > 127), "")
                problems.append("%s contains non-ASCII text (%r) but has no UTF-8 BOM; IIS compiles it "
                                "in the machine's ANSI code page and the C# it generates is broken"
                                % (relative, first))

    if scanned < 30:
        problems.append("only %d text files were found under T5Web.Web; the scan is looking in the "
                        "wrong place" % scanned)

    config = os.path.join(SITE, "Web.config")
    if os.path.exists(config):
        with open(config, encoding="utf-8-sig") as handle:
            web = handle.read()
        if 'fileEncoding="utf-8"' not in web:
            problems.append("Web.config does not set globalization fileEncoding=\"utf-8\"; the BOM on "
                            "our files is then the only thing standing between a view and the ANSI "
                            "code page")

    if problems:
        print("FAIL: text encoding")
        for problem in problems:
            print("  - " + problem)
        return 1

    print("OK: %d text files are UTF-8, %d with a BOM, and Web.config states the encoding"
          % (scanned, with_bom))
    return 0


if __name__ == "__main__":
    sys.exit(main())
