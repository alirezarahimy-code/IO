# Change register — proposed, not deployed

No item below is labelled implemented until its new-app test and destination ledger IDs are filled. Compare **Aria only to TWF 4.28 / TWF**; compare **Aria Admin only to T5Web 3.5.7 / T5Web**. Do not use T5V3 desktop behaviour as a web drop-in or port Link5.

## Required to port safely (PORT)

| ID | Product / original behaviour | Necessary change | Acceptance evidence pending |
| --- | --- | --- | --- |
| PORT-A-01 | TWF MVC5 / Web API 2 / `Global.asax`, request-scoped static access via `[ThreadStatic]` | ASP.NET Core host, explicit MVC/HTTP route and filter mapping, scoped per-request state under async load; do not translate ViewBag and virtual paths by text substitution. | Route/action matrix, concurrent e2e, Razor compilation, response/status comparison. |
| PORT-A-02 | TWF x86 Framework engine DLLs, 401 direct call sites; current `T5V3.Attendance`, `T5V3.LeaveCalcs` are `32BITREQUIRED`. | Only a genuinely engine-dependent Windows worker/test host may be x86. Keep Core web process 64-bit; define a versioned, authenticated local IPC protocol with timeouts, error codes and compensating transactions. Never add a lock. | Binary hash/protocol tests, per-calculation golden corpus, 32/64-bit loading proof, Windows agent install/rollback. **BLOCKED until implemented.** |
| PORT-A-03 | TWF existing `mytimeauth` token/cookie; fallback auth secret, no antiforgery on POST. | Preserve cookie validity across cut-over or explicitly plan reauthentication; remove unsafe fallback, require a locally supplied secret; add antiforgery on state changes while keeping public URLs/form contracts. | Old/new token vectors, malformed/tampered/expired tests, POST allow/deny/CSRF matrix. An unexplained 401/403 change is not parity. |
| PORT-AA-01 | T5Web Windows/Negotiate user, `UserInfo.WebUser` and `T5WebAccess` guard. | ASP.NET Core IIS Windows auth with the same badge/feature matrix, fail closed on missing DB. Never silently switch to a password cookie or bypass guard for diagnostics. | IIS anonymous/unknown/disabled/allowed tests at route level; 401 challenge vs 403 vs 503. |
| PORT-AA-02 | T5Web `System.Web`, Web.config + EF-format connection strings, SqlClient factories for `ReportT5V3` and `Personnel`. | Core configuration/connection provider from offline-pinned local source, preserve catalog boundary, SQL permissions and date/locale behaviour. No online NuGet restore. | Provider package SHA/feed, golden synthetic dual-catalog SQL, deploy rollback, guard tests. Current local provider/hosting bundle **NEEDS-INFRA**. |
| PORT-AA-03 | Original 343 actions, current rewrite 34 views, 3 raw-punch report stand-ins; 87 prior drops. | Track each original action and view, including drops; implement computed reports or return explicitly blocked/unavailable, never label raw punches calculated. | Separate original-to-new action decisions, report numeric golden, Razor-view compilation and access tests. |
| PORT-BOTH-01 | Framework views compile under MVC5 and use Persian RTL offline assets. | Compile ASP.NET Core Razor views in both distinct products; bundle assets locally, no CDN; set verified UTF-8/culture, CSP and response encoding. | Offline network-block test, Persian dates/digits and route rendering, 100 concurrent users. |

## Optional improvements (NOT a parity shortcut)

| ID | Idea | Status |
| --- | --- | --- |
| OPT-01 | UI polish without changing field names/actions, after golden routes pass. | Not started; no change allowed to conceal missing screens. |
| OPT-02 | Additional telemetry or performance caching after synthetic correctness and permissions tests. | Not started; cache must not leak cross-user or cross-catalog data. |
| OPT-03 | Desktop T5V3 modules not present in original T5Web web site. | Outside web parity by default; separate approval required for any new features, not a silent .NET 10 requirement. |

Risk remains open: Windows/IIS/SQL 2016 and real Link5/hardware/production data are untested; do not stop or alter installed software, clone a real DB, or claim calculated-engine equivalence without explicit authorization and evidence.
