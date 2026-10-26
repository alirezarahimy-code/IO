# .NET 10 migration — resumable execution state

**Branch:** `arena/a4df3135-io` only. **Read-only source SHA at start:** `ebd8dff4c82ae762568426047829dcfac9089871`. Do not recreate the branch or edit original `src/TWF`, `src/T5Web`, `TWF 4.28`, `T5Web [3.5.7]`, `T5V3.9.2`, or `Link5 v4.0.4` tracked source files.

| Step | State / next executable action |
| --- | --- |
| Phase 0: real inventory & references | PARTIALLY BLOCKED: raw TWF build failed `40395889978`; identical-source shadow failed `40396626924`; patched-copy diagnostic built Web but TWF.Tests failed to compile `40396901909` on real Framework (facade-only test APIs). **Zero** real TWF tests executed. Existing T5Web Framework CI passed at source SHA `ebd8dff`; see TEST_LOG. |
| Phase 1: member and legacy ledger | IN-PROGRESS: stand-alone offline Roslyn inventory CI `40398282521` passed. Artifact download is blocked by the storage host, so a guarded CI step will freeze and push only the checked CSV on the existing branch; verify after fetching. Do not mark a member TESTED by counting filenames. |
| Phase 2: deterministic golden tests | TODO: measure actual Framework HTTP/status/auth/HTML and calculation outputs; synthetic fixture only. |
| Phase 3: independent products | TODO: `src/net10/AriaAdmin/` first, `src/net10/Aria/` second; avoid an MVC 5 shim masquerading as ASP.NET Core. |
| Phase 4: integration/security/load/IIS | TODO: user role matrix, synthetic DB and attendance workflow, 100 users, rollback; Link5 live NEEDS-HARDWARE. |
| Release gate | NOT PASSED. There are no executable `net10.0` web apps or full parity/CI proofs yet. |

**Next work:** (1) Freeze/inspect the generated ledger and resolve extraction gaps; (2) start one `net10.0` AriaAdmin slice with real lower-layer and Razor build/tests; (3) only then build an Aria slice with the Windows engine boundary. TWF real Framework tests remain BLOCKED by the documented existing test/source issues; no route/golden parity can be asserted from them. After each unit update this table, the ledger and TEST_LOG.md with exact CI identifiers. Preserve tested commits and continue from the first unfinished unit after interruption. No user commands are required.
