# Initial .NET 10 migration audit (not an acceptance certificate)

The source C#/Razor inventories are still being generated; counts below are measured from the existing ratchet tools on the untouched rewrites, not a statement that any Core MVC route works.

| System | Measured current surface | Blocker / proposed boundary |
| --- | --- | --- |
| Aria Admin | 27 web C# files, 23 using `System.Web`, 22 controller types, 34 `.cshtml` views, 2 `HttpContext.Current`, 4 config-manager sites; 250 port coupling points. | Core MVC routing/filter/context/view rewrite. The other 51 lower-layer files compile on net10.0 in the existing spike *without* `PersonnelEngineDirectory.cs`. |
| Aria | 95 web C# files, 44 using `System.Web`, 30 controllers, 74 views, 401 direct engine calls in 36 files; 905 port coupling points. | Core MVC and Web API 2; **current Web sources** have 27 `HttpContextBase` occurrences in 9 files, 14 `LoginManager` occurrences in 7 files, and 5 `[ThreadStatic]` occurrences in 2 files (literal counts; not route counts). 3 `System.Drawing` files and VirtualPath features need separate handling; native/Framework engine boundary. Raw Windows MSBuild failed at 40395889978. |

## Technology-specific disposition, to be validated with routes/tests

| Technology / behaviour | Net10 disposition and evidence needed |
| --- | --- |
| `System.Web`, MVC 5, `Global.asax` | ASP.NET Core MVC `Program.cs`, endpoint/filter mapping and action/model-binding tests; no copied `System.Web` binary in the .NET 10 web process. |
| T5Web Windows authentication | Select IIS-integrated Windows scheme for the actual hosting model; preserve `HttpContext.User -> [UserInfo].[WebUser]=1 -> [T5WebAccess]`, 401 challenge and 403 denial, test absent/unknown/disabled/allowed operators. Not cookie/captcha auth. |
| TWF `mytimeauth` | Preserve token encoding, HMAC/expiry/secure/httponly/samesite semantics with negative tests; a new ASP.NET Core cookie ticket cannot silently invalidate existing cookies. The current hardcoded default encryption secret is a separate release blocker. |
| `[ThreadStatic]` | Replace with request-scoped data, prove async concurrency (the existing 30-thread test alone is insufficient). Record forced behaviour change. |
| Web API 2, `VirtualPathProvider` | Map HTTP routes/verbs/response/status/permissions; use built-in Razor compile/runtime view discovery, not a silent `T41` deletion. |
| `System.Drawing`, Registry, DllImport, COM/native, x86 | Explicit Windows-only path or separate agent; never load x86 engine in a 64-bit web app. Do not blanket-ban valid Windows interop without auditing and tests. |
| `BinaryFormatter`, `Thread.Abort`, `AppDomain`, `GetEncoding(1256)` | Scan actual occurrences; reject unsafe/unavailable APIs. `AppDomain` itself is not universally forbidden. Register CodePages provider only if real 1256 I/O is required; test Persian/Arabic data and culture with ICU/NLS. |
| T5Web SQL / TWF SQL | Two catalogs (`Personnel`, `ReportT5V3`) via correct factories; preserve schema, permissions and transaction semantics. TWF `TwfDataRepository.GetRequests` currently JOINs Personnel tables on a T5 connection: known blocker before declaring correctness. |
| Original reports and devices | Current T5Web `late-early`, `overtime`, `grid` report *raw punches* with a note, not computed engine reports. 32 original T5Web proxy actions do not mean 32 live Link5 endpoints; current transport exposes 5 command kinds. `RecordsController.Insert` **does exist** despite an outdated device document. Inventory legacy RDLC, ASPX, ToolbarItems.xml without inventing rows in the current rewrite. |
| Core assets / deploy | Offline bundled RTL CSS/JS/fonts; ANCM v2 + IIS No Managed Code, 64-bit web, split agent; synthetic CI smoke and reversible deployment. No production stop/deletion/deployment is authorised. |

The 343 original T5Web actions have existing `src/T5Web/docs/legacy-actions.csv` decisions: 178 `new`, 78 `module`, 87 `drop`. These are decisions, **not** 256 behaviourally verified actions. A legacy-action and view ledger and semantic route matrix must validate each mapping; a `drop` is not automatically an approved .NET 10 N-A. Desktop T5V3 binary hashes differ from the pinned Web 3.5.7 engine; signature equality is not calculation equivalence.

## New Phase-0 finding: TWF build/test baseline cannot be reported green

Raw Windows MSBuild was blocked by missing Web project references (`40395889978`). Adding reference metadata to a disposable copy exposed a real source error (`Init.Start` resolves to inherited `HttpApplication.Init`, `40396626924`). A diagnostic copy with an *explicit single-line qualification* built the web DLL but the 47-test project failed on fake-facade-only `HttpContext()` and writable `UserHostAddress` APIs plus unavailable Framework `string.Contains(value, StringComparison)` (`40396901909`). This is an evidence gap, not permission to declare the rewrite equivalent. The originals and rewrites remain byte-for-byte unchanged. New acceptance tests cannot inherit a passing-framework claim from this suite.
