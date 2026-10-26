# .NET 10 migration — resumable execution state

**Branch:** `arena/a4df3135-io` only. **Read-only source SHA at start:** `ebd8dff4c82ae762568426047829dcfac9089871`. Do not recreate the branch or edit original `src/TWF`, `src/T5Web`, `TWF 4.28`, `T5Web [3.5.7]`, `T5V3.9.2`, or `Link5 v4.0.4` tracked source files.

| Step | State / next executable action |
| --- | --- |
| Phase 0: real inventory & references | IN-PROGRESS: toolchain recorded in BUILD_INFO.md, existing CI scope recorded in TEST_LOG.md; Windows baseline for TWF remains unproved. |
| Phase 1: member and legacy ledger | TODO: generate with Roslyn + supplementary Razor/config/SQL/DLL extractors, freeze baseline; do not mark a member TESTED by counting filenames. |
| Phase 2: deterministic golden tests | TODO: measure actual Framework HTTP/status/auth/HTML and calculation outputs; synthetic fixture only. |
| Phase 3: independent products | TODO: `src/net10/AriaAdmin/` first, `src/net10/Aria/` second; avoid an MVC 5 shim masquerading as ASP.NET Core. |
| Phase 4: integration/security/load/IIS | TODO: user role matrix, synthetic DB and attendance workflow, 100 users, rollback; Link5 live NEEDS-HARDWARE. |
| Release gate | NOT PASSED. There are no executable `net10.0` web apps or full parity/CI proofs yet. |

**Next work:** (1) Windows CI TWF Framework baseline including all 47 tests; (2) automated ledger and legacy action inventory; (3) establish a real `net10.0` AriaAdmin lower-layer/app build in the new path; (4) test a minimal MVC route and compare Framework responses before migrating more controllers. After each unit update this table, the ledger and TEST_LOG.md with exact CI identifiers. Preserve tested commits and continue from the first unfinished unit after interruption. No user commands are required.
