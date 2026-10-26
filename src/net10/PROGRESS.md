# .NET 10 migration — resumable execution state

**Branch:** `arena/a4df3135-io` only. **Read-only source SHA at start:** `ebd8dff4c82ae762568426047829dcfac9089871`. Do not recreate the branch or edit original `src/TWF`, `src/T5Web`, `TWF 4.28`, `T5Web [3.5.7]`, `T5V3.9.2`, or `Link5 v4.0.4` tracked source files.

| Step | State / next executable action |
| --- | --- |
| Phase 0: real inventory & references | PARTIALLY BLOCKED: raw TWF build failed `40395889978`; identical-source shadow failed `40396626924`; patched-copy diagnostic built Web but TWF.Tests failed to compile `40396901909` on real Framework (facade-only test APIs). **Zero** real TWF tests executed. Existing T5Web Framework CI passed at source SHA `ebd8dff`; see TEST_LOG. |
| Phase 1: member and legacy ledger | INVENTORY FROZEN (not behaviourally done): CI `40398533166` generated/validated 9,837 rows using local SDK 10.0.112 and pushed commit `1c755b1` on this branch only. Local `ledger-check.py --inventory` passed; `--release` intentionally fails (6,764 TODO, 41 MAPPED synthetic-core rows, 2 restricted-data BLOCKED). Most destinations and original-behaviour evidence remain open. Do not mark a member TESTED by counting filenames. |
| Phase 2: deterministic golden tests | TODO: measure actual Framework HTTP/status/auth/HTML and calculation outputs; synthetic fixture only. |
| Phase 3: independent products | AA-Core-01 VERIFIED for **portable lower layers only**: independent CI `40399185543`, Ubuntu SDK 10.0.112 + Windows SDK 10.0.113; offline Release builds of 25 linked source files and 9/9 synthetic tests each. Exactly 41 source/member ledger rows are MAPPED with evidence, not TESTED/original parity. **No ASP.NET Core/IIS web host yet**; Aria has not started. Next single slice: AriaAdmin Web fail-closed route + Razor compile/HTTP tests. |
| Phase 4: integration/security/load/IIS | TODO: user role matrix, synthetic DB and attendance workflow, 100 users, rollback; Link5 live NEEDS-HARDWARE. |
| Release gate | NOT PASSED. There are no executable `net10.0` web apps or full parity/CI proofs yet. |

**Next work:** (1) start one `net10.0` AriaAdmin slice with real linked lower layers, a fail-closed HTTP surface and Razor build/tests; (2) update source-to-destination ledger rows with measured evidence, never blanket-mark TODO complete; (3) only after this slice is verified, build one Aria slice with an explicit Windows engine boundary. TWF real Framework tests remain BLOCKED by the documented existing test/source issues; no route/golden parity can be asserted from them. After each unit update this table, the ledger and TEST_LOG.md with exact CI identifiers. Preserve tested commits and continue from the first unfinished unit after interruption. No user commands are required.
