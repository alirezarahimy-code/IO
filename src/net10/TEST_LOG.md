# Test evidence — distinguish executed facts from plans

| Check | Evidence and exact scope | Verdict |
| --- | --- | --- |
| Starting branch / clean tree | `git reset --mixed FETCH_HEAD` after fetching **only** the current branch (remote descendant of initial clone); `git status --short --branch` -> `## arena/a4df3135-io`, HEAD `ebd8dff`. Working files were preserved and matched the remote. | PASS, Git state only |
| Existing T5Web Framework rewrite | GitHub Actions [run 40337319605](https://github.com/alirezarahimy-code/IO/actions/runs/40337319605), SHA `ebd8dff`: all 4 jobs succeeded, including Windows Framework MSBuild/test and IIS/SQL with synthetic fixture. | PASS for that commit / scope, **not** for .NET 10 web or customer DB |
| Existing T5Web portable lower layers | Same run, `net8.0` and `net10.0` build notices: 0 errors, 0 warnings. The spike excludes `PersonnelEngineDirectory.cs` and *all* MVC controllers/views. | PASS for lower-layer compilation only |
| Existing TWF budget | `python3 src/TWF/tools/port-budget.py --check` -> 905 coupling points; engine calls 401, 36 engine files; 30 controllers, 74 views, 0 antiforgery. | PASS for static inventory only |
| Existing T5Web budget | `python3 src/T5Web/tools/web-port-budget.py --check` -> 250 coupling points; 22 controllers, 34 views, 18 antiforgery. | PASS for static inventory only |
| Local TWF Framework static gates | First attempt without `dnfile` returned `STALE` (metadata not read). Installed `dnfile==0.17.0` in throwaway `.venv` and reran **the four separate static commands**: `verify-engine-pin.py --check`, `build-schema-manifest.py --check`, `verify-sql-schema.py`, `lint-csharp.py`. All returned 0; schema 129 tables, SQL 340 literals, C# 217 files. **No Framework compiler/test was executed.** | PASS static only; full build BLOCKED locally |
| T5Web all static gates | `PATH="$PWD/.venv/bin:$PATH" bash src/T5Web/build-and-test.sh --gates` -> `OK: static gates passed (compiler step skipped)`, exit 0; 343 legacy actions mapped, SQL 106 statements (85 complete, 21 fragments), 79 C# files syntax-checked. A PowerShell syntax pass was skipped by that script (`tree-sitter-powershell` absent). | PASS static only, PowerShell grammar NEEDS-INFRA |
| TWF Framework Windows baseline | Separate CI job under development. There are 47 registered test cases, not 48. | NOT RUN YET |
| New Aria/AriaAdmin `net10.0` builds, executable suites, Razor route/auth and IIS deployment | No projects or routes built at start. | NOT RUN YET |
| SQL Server 2016, real-device Link5, customer database, 100-user load | No environment or permission to prove them. | UNPROVEN / NEEDS-INFRA, NEEDS-HARDWARE |

**Release gate:** zero unresolved in-scope `BLOCKED`, no TODO/IN-PROGRESS, matched contracts, both actual web hosts and tests passing on net10.0, synthetic SQL/IIS/e2e/load evidence. A check is not marked ✅ unless its own output was captured.
