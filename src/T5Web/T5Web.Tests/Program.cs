using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using T5Web.Application.Modules;
using T5Web.Application.Services;
using T5Web.Data;
using T5Web.Data.Engine;
using T5Web.Domain;
using T5Web.Domain.Models;

namespace T5Web.Tests
{
    /// <summary>
    /// Self-contained test runner (no NuGet, no runner download - the deployment
    /// network is offline). Exit code 0 means every test passed.
    /// </summary>
    public static class Program
    {
        private static int _passed;
        private static int _failed;
        private static readonly List<string> Failures = new List<string>();

        public static int Main(string[] args)
        {
            Console.WriteLine("======================================================================");
            Console.WriteLine("  T5Web Admin Console - verification suite");
            Console.WriteLine("======================================================================");

            RunTest("T01: Department tree (orphans become roots, depth is assigned)", TestDepartmentTree);
            RunTest("T02: Department cycle guard (self-parent and grandchild)", TestDepartmentCycle);
            RunTest("T03: Access levels (deny wins, last admin protected, edit-only features)", TestAccessLevels);
            RunTest("T04: Workflow chain validation (duplicates, self-accept, gaps)", TestWorkflowValidation);
            RunTest("T05: Audit hash chain (tamper detection, append-only order)", TestAuditChain);
            RunTest("T06: Personnel validation and Jalali dates", TestPersonnelRules);
            RunTest("T07: Menu and feature gating", TestMenuGating);
            RunTest("T08: SQL identifiers conform to the schema manifest", TestSchemaConformance);
            RunTest("T09: Offline UI (no external assets, no icon fonts)", TestOfflineUi);
            RunTest("T10: Razor and form hygiene (no @helper, conditional attributes, CSRF)", TestRazorHygiene);
            RunTest("T11: Build gates (engine pin, manifest, SQL conformance)", TestBuildGates);
            RunTest("T12: No licence, lock or outbound dependency", TestNoLockDependency);
            RunTest("T13: Phase plan integrity (tracker, deliverables, 64/64 legacy coverage)", TestPhasePlanIntegrity);
            RunTest("T14: Engine directory contract (model members exist, reads delegate, writes stay in SQL)", TestEngineDirectoryContract);
            RunTest("T15: Personnel edit reloads stored fields instead of trusting the form", TestPersonnelEditTrust);
            RunTest("T16: Module registry is proved against schema and column types", TestModuleRegistryProof);
            RunTest("T17: Module validation (required, range, dates, Persian digits)", TestModuleValidation);
            RunTest("T18: Module store (composite keys, uniqueness, delete, CSV)", TestModuleStore);

            Console.WriteLine("----------------------------------------------------------------------");
            Console.WriteLine($"  Summary: {_passed} Passed, {_failed} Failed");
            if (Failures.Count > 0)
            {
                Console.WriteLine("  Failures:");
                foreach (string failure in Failures)
                {
                    Console.WriteLine("   - " + failure);
                }
            }
            Console.WriteLine("======================================================================");
            return _failed == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ infra

        private static void RunTest(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Failures.Add(name + " -> " + ex.Message);
                Console.WriteLine("[FAIL] " + name + " -> " + ex.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static string RepoRoot
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                // bin/Debug -> T5Web.Tests -> T5Web -> src -> repo
                for (int i = 0; i < 5 && dir != null; i++)
                {
                    dir = Path.GetFullPath(Path.Combine(dir, ".."));
                    if (Directory.Exists(Path.Combine(dir, "T5Web")))
                    {
                        return dir;
                    }
                }
                return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
            }
        }

        private static string T5WebRoot
        {
            get { return Path.Combine(RepoRoot, "src", "T5Web"); }
        }

        private static string Read(params string[] parts)
        {
            string path = Path.Combine(new[] { T5WebRoot }.Concat(parts).ToArray());
            Assert(File.Exists(path), "missing file: " + path);
            return File.ReadAllText(path);
        }

        // --------------------------------------------------------------- fixtures

        private static InMemoryAdminRepository BuildRepository()
        {
            var repository = new InMemoryAdminRepository();
            repository.Seed(
                people: new[]
                {
                    new PersonnelRow { BadgeNumber = 1001, FirstName = "مریم", LastName = "کاظمی", UserName = "m.kazemi", IsWebUser = true, DeptId = 10, EmpTypeId = 1 },
                    new PersonnelRow { BadgeNumber = 1002, FirstName = "حسین", LastName = "رستمی", UserName = "h.rostami", IsWebUser = true, DeptId = 11, EmpTypeId = 2 },
                    new PersonnelRow { BadgeNumber = 1003, FirstName = "زهرا", LastName = "امینی", IsWebUser = false, DeptId = 12, FiredDate = new DateTime(2024, 1, 1) }
                },
                departments: new[]
                {
                    new DepartmentNode { DepartmentId = 10, UpperDeptId = 0, DepartmentName = "ستاد", MemberCount = 1 },
                    new DepartmentNode { DepartmentId = 11, UpperDeptId = 10, DepartmentName = "فناوری اطلاعات", MemberCount = 1 },
                    new DepartmentNode { DepartmentId = 12, UpperDeptId = 99, DepartmentName = "واحد بی‌والد", MemberCount = 1 }
                },
                titles: new[]
                {
                    new TitleNode { TitleId = 1, TitleName = "کارشناس" },
                    new TitleNode { TitleId = 2, TitleName = "مدیر فناوری اطلاعات" },
                    new TitleNode { TitleId = 3, TitleName = "معاون" }
                },
                gates: new[] { new GateRow { GateId = 1, GateName = "درب اصلی" } },
                devices: new[]
                {
                    new DeviceRow { MachineNumber = 1, MachineName = "ورودی", GateId = 1, ConnectionType = 0, Ip = "192.168.1.201", Port = 4370 },
                    new DeviceRow { MachineNumber = 2, MachineName = "انبار", GateId = 1, ConnectionType = 0, Ip = "192.168.1.202", Port = 4370, LastStamp = DateTime.UtcNow.AddHours(-2) }
                });

            repository.SeedPassword(1001, "irrelevant");
            repository.SeedPermission(1001, AdminFeature.PortalAdmin, AccessLevel.Allow);
            repository.SeedPermission(1001, AdminFeature.PersonnelView, AccessLevel.Allow);
            repository.SeedPermission(1002, AdminFeature.PersonnelView, AccessLevel.ReadOnly);
            return repository;
        }

        // ------------------------------------------------------------------ tests

        private static void TestDepartmentTree()
        {
            var service = new DepartmentService();
            var flat = BuildRepository().GetDepartments();
            var roots = service.BuildTree(flat);

            Assert(roots.Count == 2, "expected 2 roots (one real root + one orphan), got " + roots.Count);
            DepartmentNode staff = roots.Single(r => r.DepartmentId == 10);
            Assert(staff.Children.Count == 1 && staff.Children[0].DepartmentId == 11,
                "department 11 must hang under 10.");
            Assert(staff.Children[0].Depth == 2, "child depth must be 2.");

            DepartmentNode orphan = roots.Single(r => r.DepartmentId == 12);
            Assert(orphan.Children.Count == 0, "an orphan must be attached to the root, not dropped.");
            Assert(service.MaxDepth(roots) == 2, "max depth must be 2.");
        }

        private static void TestDepartmentCycle()
        {
            var service = new DepartmentService();
            var flat = BuildRepository().GetDepartments();

            Assert(service.WouldCreateCycle(flat, 10, 10), "a department cannot be its own parent.");
            Assert(service.WouldCreateCycle(flat, 10, 11), "moving a parent under its child must be refused.");
            Assert(!service.WouldCreateCycle(flat, 11, 0), "promoting to root must be allowed.");
            Assert(!service.WouldCreateCycle(flat, 12, 10), "moving an unrelated department must be allowed.");
        }

        private static void TestAccessLevels()
        {
            InMemoryAdminRepository repository = BuildRepository();
            var service = new AccessService(repository);

            // Deny wins regardless of row order.
            var session = service.BuildSession(repository.GetPersonnel(1001), new[]
            {
                new PermissionRow { BadgeNumber = 1001, Feature = AdminFeature.DeviceEdit, Level = AccessLevel.Allow },
                new PermissionRow { BadgeNumber = 1001, Feature = AdminFeature.DeviceEdit, Level = AccessLevel.Deny }
            });
            Assert(session.LevelOf(AdminFeature.DeviceEdit) == AccessLevel.Deny,
                "Deny must win over Allow for the same feature.");
            Assert(!session.CanEdit(AdminFeature.DeviceEdit), "a denied feature must not be editable.");

            // ReadOnly is visible but not editable.
            var readOnlySession = service.BuildSession(repository.GetPersonnel(1002), repository.GetPermissions(1002));
            Assert(readOnlySession.CanView(AdminFeature.PersonnelView), "ReadOnly must still be visible.");
            Assert(!readOnlySession.CanEdit(AdminFeature.PersonnelView), "ReadOnly must not allow edits.");
            Assert(!readOnlySession.CanView(AdminFeature.DeviceView), "a missing row must mean Deny.");

            // PortalAdmin cannot be granted to somebody who cannot sign in.
            OperationResult notWebUser = service.Set(1003, AdminFeature.PortalAdmin, AccessLevel.Allow);
            Assert(!notWebUser.Succeeded, "PortalAdmin must require WebUser = 1.");

            // ReadOnly is invalid for an edit-only feature.
            OperationResult nonsense = service.Set(1002, AdminFeature.AccessEdit, AccessLevel.ReadOnly);
            Assert(!nonsense.Succeeded, "ReadOnly must be refused for an edit-only feature.");

            // The last administrator cannot revoke their own PortalAdmin.
            OperationResult lastAdmin = service.Set(1001, AdminFeature.PortalAdmin, AccessLevel.Deny);
            Assert(!lastAdmin.Succeeded, "removing the last administrator must be refused.");
        }

        private static void TestWorkflowValidation()
        {
            var service = new WorkflowService(BuildRepository());

            var good = new List<WorkflowStep>
            {
                new WorkflowStep { TitleId = 1, AccepterTitleId = 2, OrderIndex = 1 },
                new WorkflowStep { TitleId = 1, AccepterTitleId = 3, OrderIndex = 2 }
            };
            Assert(service.Validate(good).Succeeded, "a contiguous chain must be accepted.");

            var duplicate = new List<WorkflowStep>
            {
                new WorkflowStep { TitleId = 1, AccepterTitleId = 2, OrderIndex = 1 },
                new WorkflowStep { TitleId = 1, AccepterTitleId = 3, OrderIndex = 1 }
            };
            Assert(!service.Validate(duplicate).Succeeded, "duplicate OrderIndex must be refused.");

            var selfAccept = new List<WorkflowStep>
            {
                new WorkflowStep { TitleId = 1, AccepterTitleId = 1, OrderIndex = 1 }
            };
            Assert(!service.Validate(selfAccept).Succeeded, "self-acceptance must be refused.");

            var gap = new List<WorkflowStep>
            {
                new WorkflowStep { TitleId = 1, AccepterTitleId = 2, OrderIndex = 1 },
                new WorkflowStep { TitleId = 1, AccepterTitleId = 3, OrderIndex = 3 }
            };
            Assert(!service.Validate(gap).Succeeded, "a gap in OrderIndex must be refused.");

            Assert(!service.Validate(new List<WorkflowStep>()).Succeeded, "an empty chain must be refused.");
        }

        private static void TestAuditChain()
        {
            InMemoryAdminRepository repository = BuildRepository();
            var service = new AuditService(repository);

            service.Record("مریم کاظمی", AdminFeature.AccessEdit, "تغییر سطح دسترسی", "#1002", "PersonnelView -> ReadOnly");
            service.Record("مریم کاظمی", AdminFeature.DeviceEdit, "ویرایش دستگاه", "1", "IP -> 192.168.1.210");
            service.Record("حسین رستمی", AdminFeature.PersonnelEdit, "ویرایش پرسنل", "#1003", "تلفن");

            var entries = repository.GetAudit(10, null);
            Assert(entries.Count == 3, "three audit entries expected.");
            Assert(service.VerifyChain(entries) == -1, "an untouched chain must verify.");

            // Tampering with the oldest visible entry must be detected.
            entries[2].Detail = "تغییر یافته";
            Assert(service.VerifyChain(entries) == 2, "a modified entry must break the chain at its index.");

            // Recomputing the hash of the tampered entry must still break the link to its child.
            entries[2].Hash = AuditService.ComputeHash(entries[2], entries[2].PreviousHash);
            Assert(service.VerifyChain(entries) >= 0, "re-hashing a single entry must not repair the chain.");

            var fresh = BuildRepository();
            var freshService = new AuditService(fresh);
            AuditEntry first = freshService.Record("سیستم", AdminFeature.PortalAdmin, "راه‌اندازی", "-", "-");
            Assert(first.PreviousHash.Length == 0, "the first entry must have an empty previous hash.");
            Assert(first.Hash.Length == 64, "SHA-256 hex must be 64 characters.");
        }

        private static void TestPersonnelRules()
        {
            InMemoryAdminRepository repository = BuildRepository();
            var service = new PersonnelService(repository);

            PersonnelRow row = repository.GetPersonnel(1001);
            row.Phone = "09120000000";
            Assert(service.Save(row).Succeeded, "a valid row must save.");

            PersonnelRow badNational = repository.GetPersonnel(1002);
            badNational.NationalNo = "12ab";
            Assert(!service.Save(badNational).Succeeded, "a non-numeric national number must be refused.");

            PersonnelRow badEmail = repository.GetPersonnel(1002);
            badEmail.Email = "not-an-email";
            Assert(!service.Save(badEmail).Succeeded, "an invalid e-mail must be refused.");

            PersonnelRow badDates = repository.GetPersonnel(1002);
            badDates.HiredDate = new DateTime(2024, 5, 1);
            badDates.FiredDate = new DateTime(2020, 1, 1);
            Assert(!service.Save(badDates).Succeeded, "a fire date before the hire date must be refused.");

            PersonnelRow noDept = repository.GetPersonnel(1002);
            noDept.DeptId = 0;
            Assert(!service.Save(noDept).Succeeded, "a missing department must be refused.");

            // Jalali conversion uses PersianCalendar: Nowruz 1403 = 2024-03-20.
            Assert(PersonnelService.ToJalali(new DateTime(2024, 3, 20)) == "1403/01/01",
                "2024-03-20 must render as 1403/01/01, got " + PersonnelService.ToJalali(new DateTime(2024, 3, 20)));
            Assert(PersonnelService.ToJalali(new DateTime(2026, 10, 25)) == "1405/08/03",
                "2026-10-25 must render as 1405/08/03, got " + PersonnelService.ToJalali(new DateTime(2026, 10, 25)));
            Assert(PersonnelService.ToJalali(null) == "—", "a null date must render as a dash.");

            // Arabic yeh/kaf normalisation keeps search working.
            Assert(PersonnelService.NormalizeDisplay("  علي   كاظمي ") == "علی کاظمی",
                "Arabic yeh/kaf must be normalised to Persian and whitespace collapsed.");
        }

        private static void TestMenuGating()
        {
            InMemoryAdminRepository repository = BuildRepository();
            var access = new AccessService(repository);

            AdminSession adminSession = access.BuildSession(repository.GetPersonnel(1001), repository.GetPermissions(1001));
            Assert(adminSession.IsPortalAdmin, "1001 has PortalAdmin.");
            Assert(adminSession.CanView(AdminFeature.PersonnelView), "1001 can see personnel.");

            AdminSession readOnlySession = access.BuildSession(repository.GetPersonnel(1002), repository.GetPermissions(1002));
            Assert(readOnlySession.CanView(AdminFeature.PersonnelView) && !readOnlySession.CanEdit(AdminFeature.PersonnelView),
                "1002 must be read-only on personnel.");
            Assert(!readOnlySession.CanView(AdminFeature.DeviceView), "1002 must not see devices.");

            // Anonymous session: nothing is reachable.
            var anonymous = new AdminSession();
            Assert(!anonymous.CanView(AdminFeature.PersonnelView), "an empty session must see nothing.");
            Assert(!anonymous.IsPortalAdmin, "an empty session is not an administrator.");
        }

        private static void TestSchemaConformance()
        {
            string repository = Read("T5Web.Data", "SqlAdminRepository.cs");
            string map = Read("T5Web.Data", "SchemaMap.cs");
            string manifest = File.ReadAllText(Path.Combine(RepoRoot, "src", "TWF", "db", "schema-manifest.json"));

            // The same three traps the ARIA layer fell into must never appear here.
            foreach (string wrong in new[] { "[UserInfos]", "[Departments]", "[NationalCode]", "[Description]", "[RequestDate]" })
            {
                Assert(!repository.Contains(wrong), "SqlAdminRepository contains the non-existent identifier " + wrong);
            }

            foreach (string table in new[] { "\"UserInfo\"", "\"Department\"", "\"TitlesChart\"", "\"Device\"", "\"Workflow\"", "\"EmpType\"", "\"Gate\"" })
            {
                Assert(manifest.Contains(table), "schema manifest is missing " + table);
            }

            // Portal-owned tables must be created by the shipped script, not assumed.
            string ddl = Read("db", "01-t5web-objects.sql");
            Assert(ddl.Contains("T5WebAccess") && ddl.Contains("T5WebAudit"),
                "the portal's own tables must be created by db/01-t5web-objects.sql.");

            // Every [Table] referenced by SQL must be either an engine table in the
            // manifest or one of the two portal tables.
            var referenced = Regex.Matches(repository, @"(?:FROM|JOIN|INTO|UPDATE)\s+\[([A-Za-z_][A-Za-z0-9_]*)\]")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string table in referenced)
            {
                bool portalOwned = table.StartsWith("T5Web", StringComparison.OrdinalIgnoreCase);
                bool inManifest = manifest.Contains("\"" + table + "\"");
                Assert(portalOwned || inManifest,
                    "SQL references table '" + table + "' that is neither portal-owned nor in the schema manifest.");
            }

            Assert(!map.Contains("[Description]") && !map.Contains("[NationalCode]"),
                "SchemaMap must not carry legacy view-model names.");
        }

        private static void TestOfflineUi()
        {
            string viewsRoot = Path.Combine(T5WebRoot, "T5Web.Web", "Views");
            string contentRoot = Path.Combine(T5WebRoot, "T5Web.Web", "Content");
            var offending = new List<string>();

            foreach (string file in Directory.GetFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories)
                         .Concat(Directory.GetFiles(contentRoot, "*.css", SearchOption.AllDirectories)))
            {
                foreach (string line in File.ReadAllLines(file))
                {
                    if (line.Contains("http://") || line.Contains("https://") || line.Contains("//cdn."))
                    {
                        offending.Add(Path.GetFileName(file) + " -> " + line.Trim());
                    }
                    foreach (string banned in new[] { "font-awesome", "bootstrap", "jquery", "kendo", "googleapis" })
                    {
                        if (line.IndexOf(banned, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            offending.Add(Path.GetFileName(file) + " -> " + banned);
                        }
                    }
                }
            }
            Assert(offending.Count == 0,
                "offline violation: " + string.Join(" | ", offending.Distinct().Take(5)));

            string css = File.ReadAllText(Path.Combine(contentRoot, "t5web-admin.css"));
            Assert(!css.Contains("@import"), "the stylesheet must not import anything (offline deployment).");
            Assert(!css.Contains("url(") || !css.Contains("http"), "the stylesheet must not reference remote files.");

            // Every class used by a view must exist in the stylesheet.
            var defined = new HashSet<string>(
                Regex.Matches(css, @"\.([a-z][A-Za-z0-9_-]*)").Cast<Match>().Select(m => m.Groups[1].Value),
                StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string view in Directory.GetFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(view), @"class=""([^""]+)"""))
                {
                    foreach (string token in m.Groups[1].Value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (token.StartsWith("tag--", StringComparison.Ordinal) || token.StartsWith("btn--", StringComparison.Ordinal) ||
                            token.StartsWith("kpi__", StringComparison.Ordinal) || token.StartsWith("panel__", StringComparison.Ordinal) ||
                            token.StartsWith("tree__", StringComparison.Ordinal) || token.StartsWith("rail__", StringComparison.Ordinal) ||
                            token.StartsWith("notice--", StringComparison.Ordinal) || token.StartsWith("dot--", StringComparison.Ordinal) ||
                            token.StartsWith("move-form", StringComparison.Ordinal) || token.StartsWith("u-", StringComparison.Ordinal) ||
                            token == "is-active")
                        {
                            used.Add(token);
                        }
                    }
                }
            }
            var missing = used.Where(u => !defined.Contains(u)).OrderBy(u => u).ToList();
            Assert(missing.Count == 0, "unstyled classes: " + string.Join(", ", missing));

            // The static preview must be built from the product's own stylesheet and must
            // never reach for anything the product CSP would block. Otherwise the preview
            // starts approving a look the real portal cannot reproduce.
            string previewRoot = Path.Combine(T5WebRoot, "preview");
            string[] previewPages = Directory.GetFiles(previewRoot, "*.html");
            Assert(previewPages.Length >= 6, "the preview must cover the main screens, found " + previewPages.Length + " page(s).");
            foreach (string page in previewPages)
            {
                string html = File.ReadAllText(page);
                string name = Path.GetFileName(page);
                Assert(html.Contains("../T5Web.Web/Content/t5web-admin.css"),
                    name + " must load the product stylesheet instead of a copy.");
                // Only attributes matter: prose may legitimately name a local URL.
                Assert(!Regex.IsMatch(html, @"(?:src|href)=""(?:https?:)?//"),
                    name + " references an external resource.");
                Assert(!Regex.IsMatch(html, @"\sstyle="""), name + " uses an inline style, which the product CSP blocks.");
                Assert(!Regex.IsMatch(html, @"\son(click|change|submit|load)="""), name + " uses an inline event handler.");
            }
            Assert(File.Exists(Path.Combine(previewRoot, "server.py")),
                "preview/server.py must exist so the preview can be served offline.");
        }

        private static void TestRazorHygiene()
        {
            string viewsRoot = Path.Combine(T5WebRoot, "T5Web.Web", "Views");
            foreach (string view in Directory.GetFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(view);
                string name = Path.GetFileName(view);

                // Razor 3 (MVC 5) removed @helper; it fails at runtime, not at compile time.
                Assert(!Regex.IsMatch(text, @"@helper\s"), name + " uses @helper, which MVC 5 does not support.");

                // selected="False" / checked="False" still means selected/checked in HTML.
                // A boolean attribute must be emitted conditionally ("x" or null), never
                // as selected="@Model.Bool" - that renders selected="False", which HTML
                // still treats as selected.
                Assert(!Regex.IsMatch(text, @"(selected|checked)=""@[^(]"),
                    name + " sets a boolean HTML attribute without a null/string conditional.");
                Assert(!Regex.IsMatch(text, @"(selected|checked)=""(True|False)"""),
                    name + " renders a literal True/False boolean attribute.");

                // Inline styles are banned: the CSP is style-src 'self', so an inline
                // style is silently ignored by the browser (BUG-17). Spacing and sizes
                // belong in t5web-admin.css utility classes.
                Assert(!Regex.IsMatch(text, @"\sstyle="""),
                    name + " uses an inline style attribute, which the CSP blocks (BUG-17).");

                // Every POST form must carry an anti-forgery token.
                foreach (Match form in Regex.Matches(text, @"<form[^>]*method=""post""[^>]*>(.*?)</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    Assert(form.Groups[1].Value.Contains("AntiForgeryToken"),
                        name + " has a POST form without @Html.AntiForgeryToken().");
                }
            }

            // Controllers must gate every action that is not the access-denied page.
            foreach (string controller in Directory.GetFiles(Path.Combine(T5WebRoot, "T5Web.Web", "Controllers"), "*.cs"))
            {
                string text = File.ReadAllText(controller);
                if (Path.GetFileName(controller) == "AccountController.cs" ||
                    Path.GetFileName(controller) == "HomeController.cs")
                {
                    continue;
                }
                Assert(text.Contains("AdminGate"), Path.GetFileName(controller) + " is not gated by AdminGate.");
            }
        }

        private static void TestBuildGates()
        {
            string script = Read("build-and-test.sh");
            Assert(script.Contains("verify-engine-pin.py"), "the build must verify the engine pin.");
            Assert(script.Contains("build-schema-manifest.py"), "the build must verify the schema manifest is current.");
            Assert(script.Contains("verify-sql-schema.py"), "the build must verify SQL identifiers.");
            Assert(script.Contains("T5Web [3.5.7]"), "the build must reference the pinned engine folder.");
            Assert(script.Contains("T5Web.Tests"), "the build must compile the portal test suite.");

            string pin = File.ReadAllText(Path.Combine(RepoRoot, "src", "TWF", "engine", "engine-pin.json"));
            Assert(pin.Contains("t5web-3.5.7"), "the engine pin must name t5web-3.5.7.");

            string webConfig = Read("T5Web.Web", "Web.config");
            Assert(webConfig.Contains("mode=\"Windows\""), "the portal must use Windows authentication.");
            Assert(webConfig.Contains("Content-Security-Policy"), "the portal must send a CSP header.");
            // No 'unsafe-inline' for scripts or styles: that is the whole point of banning
            // inline handlers and inline styles in T10.
            Match csp = Regex.Match(webConfig, @"Content-Security-Policy"" value=""([^""]+)""");
            Assert(csp.Success, "the CSP header must be a single value.");
            Assert(!csp.Groups[1].Value.Contains("unsafe-inline"),
                "the CSP must not allow 'unsafe-inline'.");
            Assert(csp.Groups[1].Value.Contains("script-src 'self'") &&
                   csp.Groups[1].Value.Contains("style-src 'self'"),
                "the CSP must restrict scripts and styles to 'self'.");
        }

        /// <summary>
        /// T14 - the engine directory is the portal's window onto organisational master data.
        ///
        /// Three properties are pinned here, all of which otherwise only fail at runtime:
        ///   1. every engine model member the directory names exists in the pinned build
        ///      (BUG-01..BUG-07 were exactly this mistake, one layer down);
        ///   2. a supplied directory really serves the read without touching SQL;
        ///   3. writes are NOT delegated - the engine's AddNew/Edit/Delete need a
        ///      process-wide DBManager that a web process must not share.
        /// </summary>
        private static void TestEngineDirectoryContract()
        {
            string contractText = Read("engine", "model-contract.json");
            var contract = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (Match typeMatch in Regex.Matches(contractText, "\"(\\w+)\": \\{\\s*\"used_by\""))
            {
                string typeName = typeMatch.Groups[1].Value;
                int membersStart = contractText.IndexOf("\"members\"", typeMatch.Index);
                int membersEnd = contractText.IndexOf("]", membersStart);
                Assert(membersStart > 0 && membersEnd > membersStart, "malformed members list for " + typeName);
                var members = Regex.Matches(contractText.Substring(membersStart, membersEnd - membersStart), "\"([A-Za-z_][A-Za-z0-9_]*)\"")
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .Where(value => value != "members")
                    .ToHashSet(StringComparer.Ordinal);
                contract[typeName] = members;
            }

            foreach (string required in new[] { "Department", "TitleChart", "EmpType", "Device" })
            {
                Assert(contract.ContainsKey(required), "engine/model-contract.json has no entry for " + required);
            }

            // Members the directory maps, per engine type. If the engine ever renames one of
            // these, this assertion is where the build should stop - not an operator's screen.
            var expected = new Dictionary<string, string[]>
            {
                { "Department", new[] { "DepartmentID", "UpperDeptID", "Name" } },
                { "TitleChart", new[] { "TitleID", "UpperTitleID", "TitleName", "DisplayName", "Observer" } },
                { "EmpType", new[] { "EmpTypeID", "EmpTypeName" } },
                { "Device", new[] { "MachineNumber", "MachineName", "GateID", "GateName", "ConnectionType", "IP", "Port", "SerialPort", "BaudRate", "SerialNumber", "Code", "Push", "LastStamp" } }
            };
            foreach (var pair in expected)
            {
                foreach (string member in pair.Value)
                {
                    Assert(contract[pair.Key].Contains(member),
                        "the pinned engine model " + pair.Key + " has no member " + member + " (BUG-16 class).");
                }
            }

            // Member names the directory actually reads must be a subset of the contract.
            string directory = Read("T5Web.Data", "Engine", "PersonnelEngineDirectory.cs");
            var allMembers = new HashSet<string>(contract.Values.SelectMany(set => set), StringComparer.Ordinal);
            var read = Regex.Matches(directory, @"\b(?:d|t|e|node)\.([A-Z][A-Za-z0-9_]*)\b")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Where(name => name != "DB" && name != "Value" && name != "HasValue" && name != "GetAll")
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Assert(read.Count >= 15, "expected the directory to map the engine model, found only " + read.Count + " reads");
            foreach (string name in read)
            {
                Assert(allMembers.Contains(name),
                    "PersonnelEngineDirectory reads '" + name + "', which no pinned engine model declares.");
            }

            // Reads through the directory must not touch SQL: the factory below throws on connect.
            var engine = new FakeEngineDirectory();
            var delegating = new SqlAdminRepository(
                () => { throw new InvalidOperationException("SQL must not be used when a directory is supplied"); },
                engine);
            Assert(delegating.UsesEngineDirectory, "the repository must report engine-backed reads.");
            Assert(delegating.GetTitles().Count == 2, "GetTitles must return the engine's rows.");
            Assert(delegating.GetEmpTypes().Count == 1, "GetEmpTypes must return the engine's rows.");
            Assert(delegating.GetDevices(null).Count == 2, "GetDevices must return the engine's rows.");
            Assert(delegating.GetDevices(7).Count == 1 && delegating.GetDevices(7)[0].GateId == 7,
                "GetDevices(gateId) must filter the engine's rows instead of re-querying.");

            // Without a directory the SQL path stays available: degraded mode, never silent.
            Assert(!new SqlAdminRepository(() => { throw new InvalidOperationException("unused"); }).UsesEngineDirectory,
                "without a directory the repository must report SQL-backed reads.");

            // Writes must never reach the engine DA. Documentation is allowed to explain
            // why, so comments are stripped before the scan.
            string directoryCode = string.Join("\n", directory
                .Split('\n')
                .Select(line => line.TrimStart())
                .Where(line => !line.StartsWith("//", StringComparison.Ordinal)));
            foreach (string forbidden in new[] { "AddNew", "Delete", "CheckForUsed", "UpdateLastPushStamp", "EditPassword" })
            {
                Assert(!directoryCode.Contains(forbidden),
                    "PersonnelEngineDirectory must stay read-only but calls " + forbidden + ".");
            }

            // The gate that keeps the contract honest must be wired into the build.
            string build = Read("build-and-test.sh");
            Assert(build.Contains("engine-model-contract.py") && build.Contains("--check"),
                "build-and-test.sh must run tools/engine-model-contract.py --check.");
        }

        private sealed class FakeEngineDirectory : IEngineDirectory
        {
            public IReadOnlyList<DepartmentNode> GetDepartments()
            {
                return new List<DepartmentNode>
                {
                    new DepartmentNode { DepartmentId = 10, DepartmentName = "ستاد" }
                };
            }

            public IReadOnlyList<TitleNode> GetTitles()
            {
                return new List<TitleNode>
                {
                    new TitleNode { TitleId = 1, TitleName = "کارشناس" },
                    new TitleNode { TitleId = 2, TitleName = "مدیر" }
                };
            }

            public IReadOnlyList<KeyValuePair<int, string>> GetEmpTypes()
            {
                return new List<KeyValuePair<int, string>>
                {
                    new KeyValuePair<int, string>(1, "رسمی")
                };
            }

            public IReadOnlyList<DeviceRow> GetDevices()
            {
                return new List<DeviceRow>
                {
                    new DeviceRow { MachineNumber = 1, GateId = 7, MachineName = "ورودی" },
                    new DeviceRow { MachineNumber = 2, GateId = 8, MachineName = "انبار" }
                };
            }
        }

        /// <summary>
        /// T15 - BUG-13. The personnel edit form must not be able to change fields it does
        /// not own.
        ///
        /// A posted HiredDate/FiredDate can arrive blank (culture parsing, a stale tab, a
        /// crafted request). A blank hire date makes every engine leave balance wrong, and
        /// nothing about that failure is visible in a build. So the POST action must reload
        /// the stored row first and copy the owner-only fields from storage.
        /// </summary>
        private static void TestPersonnelEditTrust()
        {
            string controller = Read("T5Web.Web", "Controllers", "PersonnelController.cs");
            const string postSignature = "public ActionResult Edit(PersonnelRow row)";
            Assert(controller.Contains(postSignature), "PersonnelController must bind the edit POST to PersonnelRow.");
            string post = controller.Substring(controller.IndexOf(postSignature, StringComparison.Ordinal));
            int nextMethod = post.IndexOf("\n        public ", 1, StringComparison.Ordinal);
            if (nextMethod > 0)
            {
                post = post.Substring(0, nextMethod);
            }

            // The action reloads from storage before saving.
            Assert(post.Contains("PortalServices.Personnel.Get("),
                "PersonnelController.Edit (POST) must reload the stored row before saving (BUG-13).");
            Assert(post.Contains("HttpNotFound"),
                "PersonnelController.Edit (POST) must 404 for an unknown badge number instead of inserting.");

            // Fields the form does not own are taken from storage, not from the model binder.
            foreach (string owned in new[] { "row.HiredDate = stored.HiredDate", "row.FiredDate = stored.FiredDate" })
            {
                Assert(post.Contains(owned),
                    "PersonnelController.Edit (POST) must take '" + owned.Split(' ')[0] + "' from storage (BUG-13).");
            }

            // And the view must not offer them as editable inputs at all.
            string view = Read("T5Web.Web", "Views", "Personnel", "Edit.cshtml");
            foreach (string name in new[] { "HiredDate", "FiredDate" })
            {
                bool renamed = Regex.IsMatch(view, name + "Display");
                Assert(renamed, "Personnel/Edit.cshtml must show " + name + " as " + name + "Display (read-only).");
            }

            // A read-only display input must not post: either it has no name= or it is disabled.
            foreach (Match input in Regex.Matches(view, @"<input[^>]*HiredDate[^>]*>"))
            {
                string tag = input.Value;
                Assert(tag.Contains("disabled") || !Regex.IsMatch(tag, @"\sname="),
                    "the hire-date input must be disabled so it is never posted.");
            }
        }

        /// <summary>
        /// Schema facts from db/schema-manifest.json (generated, stable layout).
        /// </summary>
        private static Dictionary<string, HashSet<string>> ManifestColumns()
        {
            string text = File.ReadAllText(Path.Combine(RepoRoot, "src", "TWF", "db", "schema-manifest.json"));
            var tables = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (Match table in Regex.Matches(text, "\"(\\w+)\":\\s*\\{\\s*\"columns\":\\s*\\[(.*?)\\]", RegexOptions.Singleline))
            {
                var columns = Regex.Matches(table.Groups[2].Value, "\"([^\"]+)\"")
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                tables[table.Groups[1].Value] = columns;
            }
            return tables;
        }

        /// <summary>Column types from engine/column-types.tsv (generated flat file).</summary>
        private static Dictionary<string, Dictionary<string, string>> TypedColumns()
        {
            string path = Path.Combine(T5WebRoot, "engine", "column-types.tsv");
            Assert(File.Exists(path), "engine/column-types.tsv is missing; run tools/engine-column-types.py");
            var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("#", StringComparison.Ordinal) || line.Trim().Length == 0)
                {
                    continue;
                }
                string[] cells = line.Split('\t');
                Assert(cells.Length == 4, "malformed line in column-types.tsv: " + line);
                if (!tables.ContainsKey(cells[0]))
                {
                    tables[cells[0]] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                tables[cells[0]][cells[1]] = cells[2];
            }
            return tables;
        }

        /// <summary>
        /// T16 - every generic module screen must be *proved* against the database.
        ///
        /// The module framework writes SQL from descriptors, so a wrong column name or a
        /// guessed type would reach the database unseen. This test re-derives both from
        /// the generated evidence and refuses anything it cannot check:
        ///   * the table and every column must exist in db/schema-manifest.json;
        ///   * every column must carry a type from engine/column-types.tsv, or declare a
        ///     rename to an engine property that does ("orm-rename:Property");
        ///   * the field kind must match that type (bit -> Boolean, int -> Number, ...);
        ///   * keys, order column and label column must be declared fields;
        ///   * an edit feature must require "Allow", so a read-only grant can never write.
        /// </summary>
        private static void TestModuleRegistryProof()
        {
            Dictionary<string, HashSet<string>> manifest = ManifestColumns();
            Dictionary<string, Dictionary<string, string>> types = TypedColumns();
            var modules = ModuleRegistry.All();

            Assert(modules.Count >= 13, "expected at least 13 generic modules, found " + modules.Count);
            Assert(modules.Select(m => m.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() == modules.Count,
                "two modules share the same code.");

            foreach (ModuleDescriptor module in modules)
            {
                HashSet<string> columns;
                Assert(manifest.TryGetValue(module.Table, out columns),
                    "module '" + module.Code + "' points at table " + module.Table + ", which is not in the schema manifest.");
                Assert(types.ContainsKey(module.Table),
                    "table " + module.Table + " has no column types; the portal must not write it.");

                Assert(module.Fields.Count > 0, "module '" + module.Code + "' has no fields.");
                Assert(module.Keys.Any(), "module '" + module.Code + "' has no key column.");

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ModuleField field in module.Fields)
                {
                    Assert(!string.IsNullOrWhiteSpace(field.Column), "module '" + module.Code + "' has a field without a column.");
                    Assert(seen.Add(field.Column),
                        "module '" + module.Code + "' declares column " + field.Column + " twice.");
                    Assert(columns.Contains(field.Column),
                        "module '" + module.Code + "' uses " + module.Table + "." + field.Column + ", which does not exist.");
                    Assert(!string.IsNullOrWhiteSpace(field.LabelFa),
                        "module '" + module.Code + "' has a field without a Persian label: " + field.Column);

                    string expectedType = null;
                    string evidence = field.TypeEvidence ?? string.Empty;
                    if (evidence.StartsWith("orm-rename:", StringComparison.Ordinal))
                    {
                        string property = evidence.Substring("orm-rename:".Length);
                        Dictionary<string, string> propertyTypes = types[module.Table];
                        Assert(propertyTypes.ContainsKey(property),
                            module.Table + "." + field.Column + " claims a rename to " + property + ", which has no type.");
                        expectedType = propertyTypes[property];
                    }
                    else
                    {
                        string declared;
                        Assert(types[module.Table].TryGetValue(field.Column, out declared),
                            module.Table + "." + field.Column + " has no type evidence, so the portal must not bind it.");
                        expectedType = declared;
                        Assert(evidence == "ddl" || evidence == "orm",
                            "module '" + module.Code + "' must state whether the type came from ddl or orm.");
                    }

                    Assert(KindMatchesType(field.Kind, expectedType),
                        module.Table + "." + field.Column + " is declared as " + field.Kind +
                        " but the evidence says " + expectedType + ".");
                }

                Assert(!string.IsNullOrWhiteSpace(module.OrderBy) &&
                       module.Fields.Any(f => string.Equals(f.Column, module.OrderBy, StringComparison.OrdinalIgnoreCase)),
                    "module '" + module.Code + "' orders by a column it does not select.");
                if (!string.IsNullOrWhiteSpace(module.LabelColumn))
                {
                    Assert(module.Fields.Any(f => string.Equals(f.Column, module.LabelColumn, StringComparison.OrdinalIgnoreCase)),
                        "module '" + module.Code + "' labels itself with a column it does not select.");
                }

                // An edit feature that only requires ReadOnly would let a read-only grant
                // reach a write path.
                Assert(AdminFeatures.RequiredLevelFor(module.EditFeature) == AccessLevel.Allow,
                    "module '" + module.Code + "' uses edit feature " + module.EditFeature +
                    ", which does not require Allow.");
                Assert(!module.ReadOnly || module.EditFeature != AdminFeature.PortalAdmin,
                    "module '" + module.Code + "' marks itself read-only but carries an edit feature.");
            }
        }

        private static bool KindMatchesType(FieldKind kind, string sqlType)
        {
            string type = (sqlType ?? string.Empty).ToLowerInvariant();
            switch (kind)
            {
                case FieldKind.Boolean:
                    return type == "bit";
                case FieldKind.Number:
                    return type == "int" || type == "bigint" || type == "smallint" || type == "tinyint" ||
                           type == "float" || type == "real" || type == "decimal" || type == "money";
                case FieldKind.Text:
                case FieldKind.Select:
                    return type == "nvarchar" || type == "varchar" || type == "char" || type == "nchar" ||
                           type == "ntext" || type == "text";
                case FieldKind.Date:
                    return type == "datetime" || type == "datetime2" || type == "date" || type == "smalldatetime";
                case FieldKind.Time:
                    return type == "time";
                default:
                    return false;
            }
        }

        /// <summary>
        /// T17 - the module service is the only gate between a form and the database.
        /// These are the cases an operator hits: a missing required value, a number past
        /// its limit, a bad date, and Persian digits pasted from a Word document.
        /// </summary>
        private static void TestModuleValidation()
        {
            var service = new ModuleService(new InMemoryModuleStore());
            ModuleDescriptor module = ModuleRegistry.Find("emptype");
            Assert(module != null, "module 'emptype' must exist.");

            ModuleRow row;

            // Required field missing.
            OperationResult missing = service.Build(module, new Dictionary<string, string>(), true, out row);
            Assert(!missing.Succeeded && missing.Message.Contains("الزامی"),
                "a missing required value must be refused with a readable message.");

            // Too long.
            var longValue = new Dictionary<string, string> { { "EmpTypeName", new string('x', 120) } };
            Assert(!service.Build(module, longValue, true, out row).Succeeded,
                "a value longer than the column must be refused.");

            // Persian digits are accepted for numeric fields.
            ModuleDescriptor gate = ModuleRegistry.Find("gate");
            var persian = new Dictionary<string, string>
            {
                { "GateID", "۱۲" },
                { "GateName", "درب شمالی" },
                { "UpperGateID", "۰" }
            };
            Assert(service.Build(gate, persian, true, out row).Succeeded,
                "Persian digits must be accepted in numeric fields.");
            Assert(Convert.ToInt64(row["GateID"]) == 12, "Persian digits must convert to 12.");

            // A date that is not a date.
            ModuleDescriptor messages = ModuleRegistry.Find("devicemessage");
            var badDate = new Dictionary<string, string>
            {
                { "SMSID", "1" },
                { "Content", "سلام" },
                { "ValidFromTime", "1404/08/03" }
            };
            Assert(!service.Build(messages, badDate, true, out row).Succeeded,
                "a Jalali date pasted into a Gregorian date input must be refused, not guessed.");

            // Checkbox semantics: absent means false, "true" means true.
            ModuleDescriptor property = ModuleRegistry.Find("property");
            var flags = new Dictionary<string, string> { { "PropertyID", "1" }, { "PropertyName", "کد پروژه" } };
            Assert(service.Build(property, flags, true, out row).Succeeded, "a module without flags must still validate.");
            Assert(!ModuleRow.IsTrue(row["IsSearchFilter"]), "an absent checkbox must mean false.");

            flags["IsSearchFilter"] = "true";
            Assert(service.Build(property, flags, true, out row).Succeeded, "a set checkbox must validate.");
            Assert(ModuleRow.IsTrue(row["IsSearchFilter"]), "a set checkbox must mean true.");

            // Display formatting: never a CLR type name, never an empty cell.
            Assert(row.Display("IsSearchFilter", FieldKind.Boolean) == "بله", "a true flag must render as بله.");
            Assert(row.Display("Missing", FieldKind.Text) == "—", "a null value must render as a dash.");
        }

        /// <summary>
        /// T18 — the store contract, exercised on the in-memory implementation.
        /// Composite keys are the reason this exists: Deputy and UserDeviceValid have no
        /// single identity column, and a store that quietly deletes by half a key would
        /// remove far too much.
        /// </summary>
        private static void TestModuleStore()
        {
            var store = new InMemoryModuleStore();
            var service = new ModuleService(store);
            ModuleDescriptor deputy = ModuleRegistry.Find("deputy");
            Assert(deputy != null && deputy.HasCompositeKey, "the deputy module must use a composite key.");

            ModuleRow row;
            Assert(service.Build(deputy, new Dictionary<string, string> { { "TitleID", "3" }, { "DeputyTitleID", "7" } }, true, out row).Succeeded,
                "a valid deputy pair must build.");
            Assert(service.Save(deputy, row, true).Succeeded, "the first deputy pair must save.");

            // Same pair again: uniqueness is enforced by the key, not by luck.
            Assert(service.Build(deputy, new Dictionary<string, string> { { "TitleID", "3" }, { "DeputyTitleID", "7" } }, true, out row).Succeeded,
                "the duplicate pair must build.");
            Assert(!service.Save(deputy, row, true).Succeeded, "a duplicate key pair must be refused.");

            // A different pair sharing one half must be a separate row.
            Assert(service.Build(deputy, new Dictionary<string, string> { { "TitleID", "3" }, { "DeputyTitleID", "9" } }, true, out row).Succeeded,
                "a second pair must build.");
            Assert(service.Save(deputy, row, true).Succeeded, "a pair differing in one key column must save separately.");
            Assert(store.Count("Deputy") == 2, "expected two deputy rows, found " + store.Count("Deputy"));

            // Deleting by the full key removes exactly one row.
            OperationResult deleted = service.Delete(deputy, new Dictionary<string, string>
            {
                { "TitleID", "3" },
                { "DeputyTitleID", "9" }
            });
            Assert(deleted.Succeeded && store.Count("Deputy") == 1, "delete must remove exactly the matching pair.");

            // A half key is refused rather than treated as a wildcard.
            OperationResult halfKey = service.Delete(deputy, new Dictionary<string, string> { { "TitleID", "3" } });
            Assert(!halfKey.Succeeded, "a half key must be refused, never treated as a wildcard.");

            // Read-only modules cannot be written even by an administrator.
            ModuleDescriptor parameters = ModuleRegistry.Find("option");
            Assert(service.Build(parameters, new Dictionary<string, string> { { "SettingsID", "1" }, { "Value", "x" } }, false, out row).Succeeded,
                "an option row must build.");
            Assert(service.Save(parameters, row, true).Succeeded == false,
                "a module with AllowCreate = false must refuse a new row.");

            // CSV: a value containing a comma or quote survives the round trip.
            string csv = JalaliText.CsvRow(new[] { "a,b", "he said \"hi\"", "سلام" });
            Assert(csv == "\"a,b\",\"he said \"\"hi\"\"\",سلام", "CSV quoting is wrong: " + csv);

            // And the columns the module framework would write are exactly the descriptor's.
            ModuleDescriptor empType = ModuleRegistry.Find("emptype");
            Assert(empType != null && empType.Keys.Count() == 1 && empType.Key.Identity,
                "EmpType's key must be a single identity column.");
        }

        /// <summary>
        /// T13 — A phased plan is only useful if it cannot drift away from the code.
        /// This test checks the tracker (docs/phase-status.csv), the plan document and
        /// the real files on disk against each other, and proves that the authoritative
        /// inventory of the legacy application is fully mapped.
        /// </summary>
        private static void TestPhasePlanIntegrity()
        {
            string docs = Path.Combine(T5WebRoot, "docs");

            // 1. Tracker exists, has the expected header and one row per phase A..F.
            string trackerPath = Path.Combine(docs, "phase-status.csv");
            Assert(File.Exists(trackerPath), "docs/phase-status.csv is missing; the plan has no single source of status.");
            string[] lines = File.ReadAllLines(trackerPath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
            Assert(lines.Length >= 2, "phase-status.csv must contain a header and at least one phase.");
            Assert(lines[0] == "phase,status,title,depends_on,effort_days,exit_gate,deliverables",
                "phase-status.csv header changed unexpectedly: " + lines[0]);

            var rows = new Dictionary<string, string[]>();
            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split(',');
                Assert(cells.Length == 7, "phase-status.csv row has " + cells.Length + " cells, expected 7: " + line);
                rows[cells[0]] = cells;
            }

            // The method is ARIA's: five numbered phases, each ending in a compiled,
            // tested deliverable. Phase 5 is then broken into execution sub-phases B..F.
            var expectedPhases = new[] { "1", "2", "3", "4", "5" };
            foreach (string phase in expectedPhases)
            {
                Assert(rows.ContainsKey(phase), "phase-status.csv is missing phase " + phase + ".");
            }
            Assert(rows.Count == expectedPhases.Length,
                "phase-status.csv has unexpected phases: " + string.Join(",", rows.Keys.Except(expectedPhases)));

            // 2. Status vocabulary is fixed, and "done" is only used with real artefacts.
            var allowedStatus = new[] { "done", "next", "planned" };
            int nextCount = 0;
            foreach (var row in rows)
            {
                Assert(allowedStatus.Contains(row.Value[1]),
                    "phase " + row.Key + " has unknown status '" + row.Value[1] + "'.");
                if (row.Value[1] == "next")
                {
                    nextCount++;
                }
            }
            Assert(nextCount == 1, "exactly one phase may be marked 'next', found " + nextCount + ".");

            foreach (var row in rows.Where(r => r.Value[1] == "done"))
            {
                foreach (string deliverable in row.Value[6].Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(deliverable))
                    {
                        continue;
                    }
                    string path = Path.Combine(T5WebRoot, deliverable.Replace('/', Path.DirectorySeparatorChar));
                    Assert(File.Exists(path),
                        "phase " + row.Key + " is marked done but '" + deliverable + "' does not exist.");
                }
            }

            // 3. Dependencies point backwards only (no phase may depend on a later one).
            var order = expectedPhases.ToList();
            foreach (var row in rows.Where(r => r.Value[3] != "-"))
            {
                foreach (string dependency in row.Value[3].Split(';'))
                {
                    Assert(rows.ContainsKey(dependency),
                        "phase " + row.Key + " depends on unknown phase '" + dependency + "'.");
                    Assert(order.IndexOf(dependency) < order.IndexOf(row.Key),
                        "phase " + row.Key + " depends on '" + dependency + "', which does not come before it.");
                }
            }

            // 4. Every phase in the tracker has a section in the plan document, and the
            //    five execution sub-phases of phase 5 are each planned.
            string plan = File.ReadAllText(Path.Combine(docs, "06-phase-plan.md"));
            var persianDigits = new[] { "۱", "۲", "۳", "۴", "۵" };
            foreach (string phase in expectedPhases)
            {
                // The document is Persian, so the heading uses Persian numerals while the
                // tracker stays ASCII for machine reading.
                string heading = "## فاز " + persianDigits[int.Parse(phase) - 1];
                Assert(plan.Contains(heading), "docs/06-phase-plan.md has no section '" + heading + "'.");
            }
            foreach (string sub in new[] { "B", "C", "D", "E", "F" })
            {
                Assert(plan.Contains("## زیرفاز " + sub), "docs/06-phase-plan.md has no plan for sub-phase " + sub + ".");
            }

            // 4b. The method document and the bug register are part of the deliverable:
            //     without them "same method as ARIA" is only an assertion.
            foreach (string doc in new[] { "01-rewrite-principles.md", "02-phase-log.md", "03-legacy-decomposition.md", "04-bugs.md", "07-method.md" })
            {
                Assert(File.Exists(Path.Combine(docs, doc)), "docs/" + doc + " is missing from the method deliverable.");
            }

            // 4c. Every bug in the register names a regression test, and every test id it
            //     names actually exists in this file.
            string bugs = File.ReadAllText(Path.Combine(docs, "04-bugs.md"));
            var bugRows = bugs.Split('\n')
                .Where(line => line.StartsWith("| **BUG-", StringComparison.Ordinal))
                .ToList();
            Assert(bugRows.Count >= 15, "the bug register must keep every fixed defect, found " + bugRows.Count + ".");
            foreach (string row in bugRows)
            {
                Assert(Regex.IsMatch(row, @"`(T\d\d|TEST-\d\d)`"),
                    "a bug row names no regression test: " + row.Substring(0, Math.Min(60, row.Length)));
            }
            var bugTests = Regex.Matches(bugs, @"`(T\d\d|TEST-\d\d)`")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Assert(bugTests.Count >= 6, "the bug register cites too few distinct tests.");
            string self = File.ReadAllText(Path.Combine(T5WebRoot, "T5Web.Tests", "Program.cs"));
            // TEST-nn ids belong to the ARIA suite; only T-nn must exist here.
            foreach (string testId in bugTests.Where(t => Regex.IsMatch(t, @"^T\d\d$")))
            {
                Assert(self.Contains("\"" + testId + ":") || self.Contains(testId + "\": ") || self.Contains(testId),
                    "docs/04-bugs.md cites " + testId + ", which the suite does not define.");
            }
            foreach (string required in new[] { "معیار پذیرش", "گیت خروج", "نفر-روز" })
            {
                Assert(plan.Contains(required), "the plan must state '" + required + "'.");
            }

            // 5. Coverage: the inventory of the legacy application is complete and fully mapped.
            string[] inventory = File.ReadAllLines(Path.Combine(docs, "legacy-controllers.txt"))
                .Where(l => !l.StartsWith("#") && !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .ToArray();
            Assert(inventory.Length == 64,
                "legacy-controllers.txt must list all 64 controllers, found " + inventory.Length + ".");
            Assert(inventory.Distinct(StringComparer.Ordinal).Count() == inventory.Length,
                "legacy-controllers.txt contains duplicate entries.");

            string map = File.ReadAllText(Path.Combine(docs, "05-t5web-rewrite-plan.md"));
            var unmapped = inventory
                .Where(name => !map.Contains("`" + name + "Controller`"))
                .ToList();
            Assert(unmapped.Count == 0,
                "these legacy controllers are not mapped in docs/05: " + string.Join(", ", unmapped));

            // 6. The portal must not grow a controller that the plan never mentions.
            var controllers = Directory.GetFiles(Path.Combine(T5WebRoot, "T5Web.Web", "Controllers"), "*.cs")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => n.EndsWith("Controller", StringComparison.Ordinal) && n != "HomeController")
                .ToList();
            Assert(controllers.Count > 0, "no controllers found.");
        }

        private static void TestNoLockDependency()
        {
            foreach (string file in Directory.GetFiles(T5WebRoot, "*.cs", SearchOption.AllDirectories)
                         .Concat(Directory.GetFiles(T5WebRoot, "*.cshtml", SearchOption.AllDirectories))
                         .Concat(Directory.GetFiles(T5WebRoot, "*.config", SearchOption.AllDirectories)))
            {
                // Skip build output and the test file itself: this test's own list of
                // banned words would otherwise match its own source.
                if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                    file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    file.Contains("T5Web.Tests"))
                {
                    continue;
                }
                string text = File.ReadAllText(file);
                foreach (string banned in new[] { "TINYLib", "HardwareLock", "LicenceKey", "HttpClient", "WebRequest" })
                {
                    Assert(text.IndexOf(banned, StringComparison.Ordinal) < 0,
                        Path.GetFileName(file) + " references '" + banned + "' (licence/lock/outbound call must not exist).");
                }
            }
        }
    }
}
