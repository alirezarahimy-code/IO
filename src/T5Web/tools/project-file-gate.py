#!/usr/bin/env python3
"""Project gate: every .csproj must be well-formed XML *and* able to build Release.

Two bugs, both found by the Windows CI rather than by any gate, are the reason this
file exists:

  * No project declared a `Debug`/`Release` PropertyGroup, so MSBuild refused all four
    of them with "The BaseOutputPath/OutputPath property is not set ... Configuration=
    'Release'". `build-and-test.sh` had always called `csc` directly, so nothing checked.
  * The fix for that was written with a text substitution, which matched the closing tag
    of the `<Project>{guid}</Project>` element inside a `ProjectReference` and produced a
    malformed XML file that `csc` never read. It took another CI run to find.
  * The next fix put the Debug/Release groups *after* the Microsoft.CSharp.targets
    import. MSBuild records `_OutputPathWasMissing` at the moment it imports
    Microsoft.Common.targets, so a later property group sets OutputPath but the flag is
    already true and the build still fails with "OutputPath is not set". Evaluating the
    project with the MSBuild API proved it: OutputPath=bin\Debug\ and
    _OutputPathWasMissing=true in the same evaluation. Hence the ordering check below -
    it is not style, it is semantics.

So: parse every project file as XML (a text-level edit cannot survive this), and require
the properties MSBuild needs for both configurations.

    python3 tools/project-file-gate.py
"""
import os
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
NS = "{http://schemas.microsoft.com/developer/msbuild/2003}"


def projects():
    for name in sorted(os.listdir(ROOT)):
        directory = os.path.join(ROOT, name)
        if not os.path.isdir(directory) or not name.startswith("T5Web."):
            continue
        for entry in sorted(os.listdir(directory)):
            if entry.endswith(".csproj"):
                yield os.path.join(directory, entry)


def main():
    problems = []
    found = 0
    for path in projects():
        found += 1
        relative = os.path.relpath(path, ROOT)
        try:
            tree = ET.parse(path)
        except ET.ParseError as error:
            problems.append("%s is not well-formed XML: %s" % (relative, error))
            continue

        root = tree.getroot()
        conditions = []
        output_paths = 0
        for group in root.findall(NS + "PropertyGroup"):
            condition = group.get("Condition") or ""
            if "Configuration" in condition:
                conditions.append(condition.replace(" ", ""))
            for child in group:
                if child.tag == NS + "OutputPath" and (child.text or "").strip():
                    output_paths += 1

        for configuration in ("Debug", "Release"):
            wanted = "'$(Configuration)|$(Platform)'=='%s|AnyCPU'" % configuration
            if wanted not in conditions:
                problems.append("%s has no PropertyGroup for %s|AnyCPU, so MSBuild "
                                "cannot resolve OutputPath" % (relative, configuration))
        if output_paths < 2:
            problems.append("%s declares %d OutputPath value(s); both configurations need one"
                            % (relative, output_paths))

        # A project that never imports the C# targets compiles nothing at all.
        text = open(path, encoding="utf-8").read()
        if "Microsoft.CSharp.targets" not in text and "Microsoft.WebApplication.targets" not in text:
            problems.append("%s imports neither Microsoft.CSharp.targets nor "
                            "Microsoft.WebApplication.targets" % relative)

        # The configurations must come BEFORE the import: Microsoft.Common.targets sets
        # _OutputPathWasMissing at import time, so a group after it is too late even
        # though every value looks right when the project is evaluated by hand.
        import_at = min([text.find(marker) for marker in
                         ("Microsoft.CSharp.targets", "Microsoft.WebApplication.targets")
                         if text.find(marker) != -1] or [len(text)])
        first_group = text.find("'$(Configuration)|$(Platform)'")
        if first_group == -1 or first_group > import_at:
            problems.append("%s declares Configuration|Platform output paths after the "
                            "targets import; MSBuild has already decided OutputPath is "
                            "missing by then" % relative)

    if not found:
        problems.append("no .csproj found under %s" % ROOT)

    if problems:
        print("FAIL: project files")
        for problem in problems:
            print("  - " + problem)
        return 1

    print("OK: %d project file(s) are well-formed and build Debug/Release|AnyCPU" % found)
    return 0


if __name__ == "__main__":
    sys.exit(main())
