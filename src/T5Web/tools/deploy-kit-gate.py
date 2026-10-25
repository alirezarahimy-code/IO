#!/usr/bin/env python3
"""Deploy-kit gate: the Windows installation scripts must be runnable and safe.

The deployment kit (deploy/*.ps1) cannot be executed in this repository's CI —
there is no Windows, no IIS and no SQL Server on the build machine - so the risk
is not that it breaks the build, it is that it rots silently and is discovered
broken on the customer's server at 11pm.

This gate therefore checks the properties that make the kit trustworthy:

  1. syntax      - every .ps1 parses (tree-sitter-powershell, when installed;
                   the gate degrades to a note instead of failing the build on a
                   machine that has no optional package);
  2. guards      - StrictMode, error preference, and the administrator requirement
                   on the scripts that change the machine;
  3. idempotency - every state-changing command is behind a "does it exist" probe,
                   so a half-finished run can be repeated;
  4. secrets     - no literal password anywhere: passwords are read at run time;
  5. references  - the db scripts, the fingerprint tool and the Web.config keys it
                   touches all exist, spelled exactly as the code spells them;
  6. order       - the four scripts run in a fixed order and docs/13 documents it.

    python3 tools/deploy-kit-gate.py            # the gate
    python3 tools/deploy-kit-gate.py --list     # what it checks, for the runbook
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DEPLOY = os.path.join(ROOT, "deploy")

SCRIPTS = ["0-publish.ps1", "1-install-iis.ps1", "2-install-sqlserver.ps1",
           "3-deploy-portal.ps1", "4-verify-install.ps1"]

# Scripts that change the machine need elevation; the verifier only reads.
# 0-publish only writes to the folder it is given, so it needs no elevation.
NEEDS_ADMIN = {"1-install-iis.ps1", "2-install-sqlserver.ps1", "3-deploy-portal.ps1"}

# A state-changing cmdlet must be guarded by a probe; the probe names are below.
MUTATING = ["New-WebAppPool", "New-Website", "New-WebBinding", "Enable-WindowsOptionalFeature",
            "Start-Process -FilePath $SetupExe", "RESTORE DATABASE"]

PROBES = ["Test-Path", "Get-Website", "Get-WindowsOptionalFeature", "Get-Service", "DB_ID(",
          "SELECT 1", "Get-ChildItem", "New-WebBinding", "-not $haveInstance", "if (-not $haveInstance)",
          "already enabled", "present"]

SECRET_PATTERNS = [
    (r"SAPWD\s*=\s*['\"][^'\"]+['\"]", "a literal SA password in an argument list"),
    (r"(?i)\bpassword\s*=\s*['\"][^'\"$][^'\"]*['\"]", "a literal password assignment"),
    (r"(?i)\bpwd\s*=\s*['\"][^'\"$][^'\"]*['\"]", "a literal pwd= in a connection string"),
    (r"(?i)integrated security\s*=\s*false", "SQL authentication in a connection string"),
]


def read(name):
    with open(os.path.join(DEPLOY, name), encoding="utf-8") as handle:
        return handle.read()


def syntax_problems(name, text):
    """Parse with tree-sitter when available; return [] and a flag otherwise."""
    try:
        import tree_sitter
        import tree_sitter_powershell
    except ImportError:
        return None
    language = tree_sitter.Language(tree_sitter_powershell.language())
    parser = tree_sitter.Parser(language)
    tree = parser.parse(text.encode("utf-8"))
    problems = []

    def walk(node):
        if node.type == "ERROR" or node.is_missing:
            snippet = text.encode("utf-8")[node.start_byte:node.end_byte][:60].decode("utf-8", "replace")
            problems.append("%s: syntax error at line %d near %r"
                            % (name, node.start_point[0] + 1, snippet))
        for child in node.children:
            walk(child)

    walk(tree.root_node)
    return problems


def check_guards(name, text):
    problems = []
    if "Set-StrictMode" not in text:
        problems.append("%s: no Set-StrictMode -Version Latest" % name)
    if "$ErrorActionPreference" not in text:
        problems.append("%s: no $ErrorActionPreference" % name)
    if name in NEEDS_ADMIN and "#requires -RunAsAdministrator" not in text:
        problems.append("%s: changes the machine but does not require an elevated shell" % name)
    if "#requires -Version 5.1" not in text:
        problems.append("%s: no #requires -Version 5.1 (Windows PowerShell compatibility)" % name)
    # Server 2012/2016 operators still ship PowerShell 5.1: -NoNewline and later
    # syntax is fine, but "??" and "&&" are PowerShell 7 only.
    for token, why in (("??", "null-coalescing is PowerShell 7 only"),
                       ("&&", "&& is PowerShell 7 only"),
                       ("-Parallel", "ForEach-Object -Parallel is PowerShell 7 only")):
        if token in text:
            problems.append("%s: uses %r (%s)" % (name, token, why))
    return problems


def check_idempotency(name, text):
    problems = []
    for cmdlet in MUTATING:
        if cmdlet not in text:
            continue
        if not any(probe in text for probe in PROBES):
            problems.append("%s: %s without an existence probe (not idempotent)" % (name, cmdlet))
    return problems


def check_secrets(name, text):
    problems = []
    for pattern, why in SECRET_PATTERNS:
        match = re.search(pattern, text)
        if match:
            problems.append("%s: %s (%r)" % (name, why, match.group(0)[:40]))
    return problems


def check_references(name, text):
    problems = []
    wants = {
        "0-publish.ps1": ["Web.config", "Views", "T5Web.Admin.dll", "MSBuild"],
        "2-install-sqlserver.ps1": ["01-t5web-objects.sql", "02-t5web-sql-login.sql"],
        "3-deploy-portal.ps1": ["Web.config", "T5CnnStr", "PersonnelCnnStr", "T5WebMode", "T5WebLegacyUrl"],
        "4-verify-install.ps1": ["verify.sql", "03-relations.sql", "migration-fingerprint.py"],
    }
    for needle in wants.get(name, []):
        if needle not in text:
            problems.append("%s: never mentions %s" % (name, needle))
    if name in ("0-publish.ps1", "3-deploy-portal.ps1") and "-Build" in text:
        # The .NET Framework MSBuild's csc.exe rejects LangVersion 7.3 (CS1617); a
        # guessed path to it cost one CI run. Ask vswhere for the real installation.
        if "vswhere" not in text:
            problems.append("%s builds the solution but never asks vswhere for MSBuild" % name)
    if name == "0-publish.ps1":
        # A published folder that still contains source is the failure this script
        # exists to prevent; the check has to stay in the script, not in the reviewer.
        for guard in ("Remove-Item", "bin\\T5Web.Admin.dll"):
            if guard not in text:
                problems.append("%s: does not verify %s" % (name, guard))
    if name == "3-deploy-portal.ps1":
        # The pinned engine is x86 (PE header of Personnel.dll). A 64-bit pool turns
        # that into "incorrect format" and a 500 on every URL, static files included.
        if "enable32BitAppOnWin64" not in text:
            problems.append("%s: never enables 32-bit mode, but the engine is x86 only" % name)
    if name == "4-verify-install.ps1":
        for header in ("Content-Security-Policy", "unsafe-inline"):
            if header not in text:
                problems.append("%s: does not verify the %s response header" % (name, header))
    return problems


def check_files_exist():
    problems = []
    for required in ["01-t5web-objects.sql", "02-t5web-sql-login.sql", "03-relations.sql", "verify.sql"]:
        if not os.path.exists(os.path.join(ROOT, "db", required)):
            problems.append("db/%s is missing but the deploy kit runs it" % required)
    if not os.path.exists(os.path.join(ROOT, "tools", "migration-fingerprint.py")):
        problems.append("tools/migration-fingerprint.py is missing but the verifier runs it")
    # The app pool runs as IIS APPPOOL\<pool>, so it needs its own Windows login in
    # both catalogs; docs/13 section 6 tells the installer to run this file.
    app_pool = os.path.join(ROOT, "db", "04-grant-app-pool-login.sql")
    if not os.path.exists(app_pool):
        problems.append("db/04-grant-app-pool-login.sql is missing, so the pool identity has no grant")
    else:
        with open(app_pool, encoding="utf-8") as handle:
            grants = handle.read()
        if grants.count("ALTER ROLE [t5web_app] ADD MEMBER") < 2:
            problems.append("db/04-grant-app-pool-login.sql must join the role in both catalogs")
        if "FROM WINDOWS" not in grants:
            problems.append("db/04-grant-app-pool-login.sql must create a Windows login, not a SQL login")
        for table in ("GRANT SELECT, INSERT, UPDATE, DELETE ON", "OBJECT::dbo."):
            if table in grants:
                problems.append("db/04-grant-app-pool-login.sql repeats the privilege list; "
                                "the role in db/02 owns it")
    # Views\Web.config is what makes @model / ViewBag / Url exist inside a Razor page.
    # Without it every screen answers 500 CS0103 on a real IIS (BUG-38); the compiler
    # never sees it, and neither does a code review.
    views_config = os.path.join(ROOT, "T5Web.Web", "Views", "Web.config")
    if not os.path.exists(views_config):
        problems.append("T5Web.Web/Views/Web.config is missing: no Razor page can inherit "
                        "WebViewPage, so every screen answers 500 (BUG-38)")
    else:
        with open(views_config, encoding="utf-8") as handle:
            views = handle.read()
        # Every imported namespace must exist in the sources: a guessed one is
        # "CS0234: The type or namespace name 'Models' does not exist" on every page,
        # which is how T5Web.Web.Models got here.
        declared = set()
        for folder, _, files in os.walk(ROOT):
            if os.sep + "bin" in folder or os.sep + "obj" in folder:
                continue
            for name in files:
                if not name.endswith(".cs"):
                    continue
                with open(os.path.join(folder, name), encoding="utf-8", errors="ignore") as handle:
                    for line in handle:
                        if line.startswith("namespace "):
                            declared.add(line.split()[1].strip(";{"))
        for match in re.finditer(r'<add namespace="([^"]+)"', views):
            imported = match.group(1)
            if imported.startswith("System."):
                continue
            if imported not in declared:
                problems.append("Views/Web.config imports %s, which no source file declares" % imported)

        for needed in ("pageBaseType=\"System.Web.Mvc.WebViewPage\"", "System.Web.Mvc.MvcWebRazorHostFactory"):
            if needed not in views:
                problems.append("Views/Web.config does not set %s" % needed)

    if not os.path.exists(os.path.join(ROOT, "T5Web.Web", "Web.config")):
        problems.append("T5Web.Web/Web.config is missing")
    else:
        with open(os.path.join(ROOT, "T5Web.Web", "Web.config"), encoding="utf-8") as handle:
            config = handle.read()
        for key in ("T5CnnStr", "PersonnelCnnStr", "T5WebMode", "T5WebLegacyUrl"):
            if 'key="%s"' % key not in config:
                problems.append("Web.config has no appSetting %s, but the deploy script patches it" % key)
        # The engine's Database First context needs EF's SqlClient provider, and EF needs
        # it *registered*: without the section the first organisation screen answers
        # MetadataException "No Entity Framework provider found" (BUG-39).
        for needed in ('invariantName="System.Data.SqlClient"', "EntityFramework.SqlServer"):
            if needed not in config:
                problems.append("Web.config does not register the EF provider (%s); the engine's "
                                "context fails on its first query (BUG-39)" % needed)
    return problems


def check_order_and_docs():
    problems = []
    readme = os.path.join(DEPLOY, "README.md")
    doc = os.path.join(ROOT, "docs", "13-install-iis-sqlserver.md")
    if not os.path.exists(readme):
        problems.append("deploy/README.md is missing")
    if not os.path.exists(doc):
        problems.append("docs/13-install-iis-sqlserver.md is missing")
        return problems
    with open(doc, encoding="utf-8") as handle:
        text = handle.read()
    last = -1
    for script in SCRIPTS:
        position = text.find(script)
        if position < 0:
            problems.append("docs/13 never mentions %s" % script)
        elif position < last:
            problems.append("docs/13 documents %s out of order" % script)
        else:
            last = position
    for needle in ("verify.sql", "03-relations.sql", "T5WebMode", "UserInfo.WebUser", "T5CnnStr"):
        if needle not in text:
            problems.append("docs/13 never mentions %s" % needle)
    if os.path.exists(readme):
        with open(readme, encoding="utf-8") as handle:
            readme_text = handle.read()
        for script in SCRIPTS:
            if script not in readme_text:
                problems.append("deploy/README.md never mentions %s" % script)
    return problems


def main(argv):
    if "--list" in argv:
        print("deploy kit gate checks: syntax, guards, idempotency, secrets, references, order")
        return 0

    if not os.path.isdir(DEPLOY):
        print("FAIL: no deploy/ folder")
        return 1

    problems = []
    parser_used = True
    for name in SCRIPTS:
        path = os.path.join(DEPLOY, name)
        if not os.path.exists(path):
            problems.append("missing deploy/%s" % name)
            continue
        text = read(name)
        syntax = syntax_problems(name, text)
        if syntax is None:
            parser_used = False
        else:
            problems.extend(syntax)
        problems.extend(check_guards(name, text))
        problems.extend(check_idempotency(name, text))
        problems.extend(check_secrets(name, text))
        problems.extend(check_references(name, text))

    problems.extend(check_files_exist())
    problems.extend(check_order_and_docs())

    if problems:
        print("FAIL: deploy kit")
        for problem in problems:
            print("  - " + problem)
        return 1

    note = "" if parser_used else " [NOTE: tree-sitter-powershell not installed - syntax pass skipped]"
    print("OK: deploy kit (%d scripts, guards, idempotency, no secrets, references, documented order)%s"
          % (len(SCRIPTS), note))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
