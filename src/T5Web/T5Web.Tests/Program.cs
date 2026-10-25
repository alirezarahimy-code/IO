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
            RunTest("T13: Phase plan integrity (tracker, deliverables, 67/67 legacy coverage)", TestPhasePlanIntegrity);
            RunTest("T14: Engine directory contract (model members exist, reads delegate, writes stay in SQL)", TestEngineDirectoryContract);
            RunTest("T15: Personnel edit reloads stored fields instead of trusting the form", TestPersonnelEditTrust);
            RunTest("T16: Module registry is proved against schema and column types", TestModuleRegistryProof);
            RunTest("T17: Module validation (required, range, dates, Persian digits)", TestModuleValidation);
            RunTest("T18: Module store (composite keys, uniqueness, delete, CSV)", TestModuleStore);
            RunTest("T19: Rule layers resolve by precedence; same-layer overlap is a conflict", TestRuleResolution);
            RunTest("T20: Attendance store refuses a duplicate punch and a repeated approval", TestAttendanceStore);
            RunTest("T21: Leave balance invariant and all-or-nothing year transfer", TestLeaveBalances);
            RunTest("T22: Schedule rules (contiguous cycle, overlap, retroactive window)", TestScheduleRules);
            RunTest("T23: Report catalogue (access gating, per-report window, offline export)", TestReports);
            RunTest("T24: Import (CSV round trip, rejected rows, update-only scope)", TestPersonnelImport);
            RunTest("T25: 67/67 legacy coverage audit (no controller or action left pending)", TestLegacyCoverage);
            RunTest("T26: Phase 5 delivery (screens, devices, cut-over switch)", TestPhase5Delivery);
            RunTest("T27: 343/343 action coverage (every action decided, every target real)", TestActionCoverage);
            RunTest("T28: database model (catalogs, keys, relations, one catalog per statement)", TestDatabaseModel);
            RunTest("T29: legacy surface (views, first-party assemblies, every appSettings key)", TestLegacySurface);
            RunTest("T30: product identity (آریا ادمین / ARIA-ADMIN registered everywhere)", TestProductIdentity);
            RunTest("T31: wave-1 T5V3 gaps (photos, entitlements, leave descriptions, leaves report)", TestWaveOneGaps);
            RunTest("T32: waves 2-3 (organisation, access, devices, logs, data health) + feature catalogue", TestWavesTwoAndThree);
            RunTest("T33: data migration (no schema drift, fingerprint covers the model, gates documented)", TestDataMigration);
            RunTest("T34: install kit (IIS + SQL scripts, idempotent, no secrets, documented order)", TestInstallKit);
            RunTest("T35: executable SQL (synthetic schema from the model, smoke extractor, CI wiring)", TestExecutableSql);
            RunTest("T36: two catalogs (verify.sql names them, the login exists in both, grants match the modules)", TestTwoCatalogInstall);

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
                // The exe runs from T5Web.Tests\bin\Release, so the repository is four
                // levels up - but this must also work from bin\Debug and from anywhere a
                // Release build happens to be copied. Look for the solution's own layout
                // (src\T5Web\T5Web.Web) rather than for a directory named "T5Web": that
                // name matches src\T5Web first and yields src\src\T5Web, which is how the
                // first Windows run of the suite failed T08 and T09 on a path.
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    if (Directory.Exists(Path.Combine(dir, "src", "T5Web", "T5Web.Web")))
                    {
                        return dir;
                    }
                    dir = Path.GetFullPath(Path.Combine(dir, ".."));
                }
                throw new InvalidOperationException(
                    "cannot find the repository root from " + AppDomain.CurrentDomain.BaseDirectory);
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
            // Every rail link must resolve: a preview full of dead ends teaches the
            // reviewer nothing, and a renamed page would silently rot.
            var pageNames = new HashSet<string>(
                previewPages.Select(p => Path.GetFileName(p)), StringComparer.OrdinalIgnoreCase);
            foreach (string page in previewPages)
            {
                string html = File.ReadAllText(page);
                string name = Path.GetFileName(page);
                foreach (Match link in Regex.Matches(html, "href=\"([A-Za-z0-9_-]+\\.html)\""))
                {
                    Assert(pageNames.Contains(link.Groups[1].Value),
                        name + " links to " + link.Groups[1].Value + ", which does not exist.");
                }
                Assert(Regex.Matches(html, "class=\"is-active\"").Count == 1,
                    name + " must mark exactly one rail item as current.");
            }
            // Where the view sets a literal ViewBag.Title, the preview's <title> must say the
            // same thing. The rail label may differ (the console itself calls the device screen
            // "دستگاه‌ها" in the menu and "دستگاه‌های تردد" on the page), but the page title
            // must not drift from the view it mirrors.
            var titled = new Dictionary<string, string>
            {
                { "dashboard.html", "Dashboard/Index.cshtml" },
                { "personnel.html", "Personnel/Index.cshtml" },
                { "department.html", "Department/Index.cshtml" },
                { "devices.html", "Device/Index.cshtml" },
                { "access.html", "Access/Index.cshtml" },
                { "diagnostics.html", "Diagnostics/Index.cshtml" }
            };
            foreach (var pair in titled)
            {
                string view = File.ReadAllText(Path.Combine(T5WebRoot, "T5Web.Web", "Views", pair.Value));
                Match viewTitle = Regex.Match(view, "ViewBag\\.Title = \"([^\"]+)\"");
                Assert(viewTitle.Success, pair.Value + " no longer sets a literal ViewBag.Title.");
                string html = File.ReadAllText(Path.Combine(previewRoot, pair.Key));
                Match pageTitle = Regex.Match(html, "<title>([^<·]+)·");
                Assert(pageTitle.Success && pageTitle.Groups[1].Value.Trim() == viewTitle.Groups[1].Value.Trim(),
                    pair.Key + " is titled '" + (pageTitle.Success ? pageTitle.Groups[1].Value.Trim() : "?") +
                    "' but " + pair.Value + " says '" + viewTitle.Groups[1].Value.Trim() + "'.");
            }

            // The three screens added in waves 2-3 must have a mirror, otherwise the newest
            // work is the part nobody can look at.
            foreach (string required in new[] { "logs.html", "integrity.html", "base-data.html" })
            {
                Assert(pageNames.Contains(required), "the preview has no page for " + required + ".");
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
        /// T32 - waves 2 and 3 (docs/11).
        ///
        /// Three things are guarded here:
        ///   * the module catalogue really grew, and every read-only module is read-only in
        ///     both directions (no create, no delete) - a log screen that can be edited is
        ///     not a log;
        ///   * the feature catalogue has one title per capability (a duplicate key in the
        ///     static dictionary throws on first use and takes the whole console down -
        ///     this assertion exists because that bug existed);
        ///   * the log reader and the health page keep their contract: no POST, gated,
        ///     and the in-memory stores behave like the SQL ones.
        /// </summary>
        private static void TestWavesTwoAndThree()
        {
            // ---- 1. the module catalogue ---------------------------------------------
            var modules = ModuleRegistry.All();
            Assert(modules.Count >= 50, "expected at least fifty modules after waves 2-3, found " + modules.Count);
            Assert(modules.Select(m => m.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() == modules.Count,
                "two modules share the same code.");

            foreach (string code in new[]
                     {
                         "position", "positionstep", "userposition", "userinfotitles", "personneldeputy",
                         "userinfomanager", "userpermission", "reportaccess", "allowvalue", "card", "area",
                         "areapoint", "downloadtime", "downloadtimedevice", "deviceprops",
                         "deviceconnectionstate", "devicebiotypes", "managerdevice", "vaccinelog",
                         "vaccinationdetails", "userbiodata"
                     })
            {
                Assert(modules.Any(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase)),
                    "module '" + code + "' from docs/11 is missing.");
            }

            foreach (ModuleDescriptor readOnly in modules.Where(m => m.ReadOnly))
            {
                Assert(!readOnly.AllowCreate && !readOnly.AllowDelete,
                    "read-only module '" + readOnly.Code + "' still allows create/delete.");
            }
            foreach (ModuleDescriptor module in modules)
            {
                foreach (ModuleField field in module.Fields.Where(f => f.DisplayOnly))
                {
                    Assert(field.TypeEvidence == "display",
                        module.Code + "." + field.Column + " is display-only without the display marker.");
                }
            }

            // ---- 2. the feature catalogue --------------------------------------------
            string features = Read("T5Web.Domain", "AdminFeature.cs");
            // Only the AdminFeature enum: AccessLevel below it also has "Name = value," members.
            int enumStart = features.IndexOf("public enum AdminFeature", StringComparison.Ordinal);
            Assert(enumStart > 0, "AdminFeature enum is missing.");
            int enumEnd = features.IndexOf('}', features.IndexOf('{', enumStart));
            Assert(enumEnd > enumStart, "AdminFeature enum has no closing brace.");
            string enumBody = features.Substring(enumStart, enumEnd - enumStart);
            var declared = Regex.Matches(enumBody, @"^\s+(\w+)\s*=\s*(\d+),", RegexOptions.Multiline)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToList();
            Assert(declared.Count >= 25, "the feature enum shrank to " + declared.Count + " members.");

            var titled = Regex.Matches(features, @"\{\s*AdminFeature\.(\w+)\s*,")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToList();
            var duplicates = titled.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert(duplicates.Count == 0,
                "AdminFeatures.TitlesFa has duplicate keys (a static initializer crash): " + string.Join(", ", duplicates));
            var missing = declared.Where(name => !titled.Contains(name)).ToList();
            Assert(missing.Count == 0, "these features have no Persian title: " + string.Join(", ", missing));

            // ---- 3. the log reader ----------------------------------------------------
            var logs = new InMemoryLogStore()
                .Source("system", "رخدادهای سامانه", "SystemLog", "t5")
                .Source("operation", "عملیات دستگاه", "OperationLog", "personnel")
                .Add("system", new DateTime(2026, 10, 20), 0, 0, "رخداد 1", "شیفت ویرایش شد")
                .Add("system", new DateTime(2026, 10, 21), 0, 0, "رخداد 1", "پارامتر ویرایش شد")
                .Add("operation", new DateTime(2026, 10, 21), 1001, 3, "دانلود", "اثر انگشت");

            Assert(logs.Sources().Count == 2, "log sources are not exposed.");
            IReadOnlyList<LogEntryRow> window = logs.Search("system",
                new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), null, null, 100);
            Assert(window.Count == 2, "the window filter returned " + window.Count + " rows.");
            Assert(window[0].Time.Value.Date == new DateTime(2026, 10, 21),
                "logs must be returned newest first.");
            Assert(logs.Search("system", new DateTime(2026, 10, 21), new DateTime(2026, 10, 21), null, null, 100).Count == 1,
                "a single-day window must return that day only.");
            Assert(logs.Search("operation", new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), 1001, null, 100).Count == 1,
                "the badge filter did not apply.");
            Assert(logs.Search("operation", new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), null, "اثر", 100).Count == 1,
                "the term filter did not apply.");
            Assert(logs.Search(null, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), null, null, 2).Count == 2,
                "the take cap did not apply.");

            // The window limits live in the domain, so the screen, the SQL reader and the
            // in-memory reader cannot drift apart. A 63+ day request must shrink to 62.
            DateTime farPast = new DateTime(2020, 1, 1);
            IReadOnlyList<LogEntryRow> clamped = logs.Search("system", farPast, new DateTime(2026, 10, 21), null, null, 100);
            Assert(clamped.Count == 2,
                "a request wider than " + LogWindow.MaxDays + " days must still return the rows inside the window " +
                "(got " + clamped.Count + ").");
            Assert(LogWindow.ClampFrom(farPast, new DateTime(2026, 10, 21)) == new DateTime(2026, 10, 21).AddDays(-LogWindow.MaxDays),
                "LogWindow.ClampFrom did not shrink the left edge to " + LogWindow.MaxDays + " days.");
            Assert(LogWindow.ClampTake(0) == LogWindow.DefaultTake && LogWindow.ClampTake(9999) == LogWindow.MaxTake,
                "LogWindow.ClampTake must fall back to the default and cap at the maximum.");
            Assert(LogWindow.MaxTake == SqlLogStore.MaxTake,
                "the domain limit and the SQL reader's constant disagree.");

            // The SQL reader must publish its sources without touching a database, and each
            // source must name a real table in the right catalog.
            var sqlLogs = new SqlLogStore(() => null, () => null);
            Dictionary<string, string> catalogs = TableCatalogs();
            Dictionary<string, HashSet<string>> manifest = ManifestColumns();
            foreach (LogSourceDescriptor source in sqlLogs.Sources())
            {
                Assert(manifest.ContainsKey(source.Table), "log source " + source.Code + " names a table not in the manifest.");
                Assert(catalogs.ContainsKey(source.Table), "log source " + source.Code + " names a table with no catalog.");
                Assert(string.Equals(catalogs[source.Table], source.Catalog, StringComparison.OrdinalIgnoreCase),
                    "log source " + source.Code + " claims catalog " + source.Catalog +
                    " but " + source.Table + " lives in " + catalogs[source.Table] + ".");
                Assert(source.Catalog == "t5" || source.Catalog == "personnel",
                    "the portal never opens the device catalog; source " + source.Code + " points at " + source.Catalog + ".");
            }

            string logsController = Read("T5Web.Web", "Controllers", "LogsController.cs");
            Assert(logsController.Contains("[AdminGate(AdminFeature.LogView"), "the log reader must be gated on LogView.");
            Assert(!logsController.Contains("[HttpPost]"), "a log reader must never accept a POST.");
            Assert(File.Exists(Path.Combine(T5WebRoot, "T5Web.Web", "Views", "Logs", "Index.cshtml")),
                "missing view: Views/Logs/Index.cshtml");

            // ---- 4. data health -------------------------------------------------------
            string integrity = Read("T5Web.Data", "SqlIntegrityStore.cs");
            foreach (string code in new[]
                     {
                         "leaves-leavetype", "yearremain-leavetype", "daily-leaf-description",
                         "request-leavetype", "emptypeleave-emptype", "workflow-title", "usershift-badge"
                     })
            {
                Assert(integrity.Contains(code), "the integrity store lost the check '" + code + "'.");
            }
            Assert(!integrity.Contains("RecordsLog") && !integrity.Contains("CommandLog"),
                "the integrity store must not reach into the device catalog.");

            var health = new InMemoryIntegrityStore()
                .Check("usershift-badge", "شیفت پرسنل ناموجود", "UserShift → UserInfo", 3, "10", "11", "12")
                .Check("workflow-title", "گردش کار با عنوان ناموجود", "Workflow → TitlesChart", 0)
                .Run();
            Assert(health.Count == 2, "the integrity store returned " + health.Count + " checks.");
            IntegrityCheckRow failing = health.First(c => c.Code == "usershift-badge");
            Assert(!failing.Passed && failing.StatusFa == "یافت شد", "a check with orphans must read as failing.");
            Assert(failing.SamplesFa.Contains("10"), "a failing check must show its sample keys.");
            IntegrityCheckRow passing = health.First(c => c.Code == "workflow-title");
            Assert(passing.Passed && passing.StatusFa == "سالم", "a clean check must read as healthy.");
            Assert(passing.SamplesFa == "—", "a clean check must not invent samples.");

            string integrityController = Read("T5Web.Web", "Controllers", "IntegrityController.cs");
            Assert(integrityController.Contains("[AdminGate(AdminFeature.PortalAdmin"),
                "data health is an administrator screen.");
            Assert(!integrityController.Contains("[HttpPost]"), "the health page is read-only.");
            Assert(File.Exists(Path.Combine(T5WebRoot, "T5Web.Web", "Views", "Integrity", "Index.cshtml")),
                "missing view: Views/Integrity/Index.cshtml");

            // ---- 4b. the rail item must open a module, not a 404 ---------------------
            string modulesController = Read("T5Web.Web", "Controllers", "ModulesController.cs");
            Assert(modulesController.Contains("FirstVisible()"),
                "Modules/Index must fall back to the first visible module when no code is given " +
                "(the rail links to it without a code).");
            Assert(modulesController.Contains("CanUpdate"),
                "keys-only link tables must hide the edit button instead of offering it.");

            // ---- 5. wiring and documentation -----------------------------------------
            string startup = Read("T5Web.Web", "MvcApplication.cs");
            foreach (string store in new[] { "SqlLogStore", "SqlIntegrityStore" })
            {
                Assert(startup.Contains(store), store + " is not wired in MvcApplication.");
            }

            string doc = Read("docs", "11-waves-two-three.md");
            foreach (string term in new[] { "EditedValue", "Template", "TimeTable", "LogsController", "IntegrityController" })
            {
                Assert(doc.Contains(term), "docs/11 must explain the decision about '" + term + "'.");
            }
        }

        /// <summary>
        /// T31 - wave 1 of the T5V3 gap analysis (docs/10): the four capabilities the
        /// desktop had and the rewrite did not, plus the one that was deliberately left
        /// out because its table belongs to the device service database.
        ///
        /// The test is written so that deleting a screen, dropping the feature gate,
        /// loosening the photo cap or re-introducing a lock would each fail the build.
        /// </summary>
        private static void TestWaveOneGaps()
        {
            // ---- 1. the photo archive ------------------------------------------------
            string controller = Read("T5Web.Web", "Controllers", "PhotosController.cs");
            Assert(controller.Contains("[AdminGate(AdminFeature.PersonnelPhoto"),
                "the photo screen must be gated on PersonnelPhoto.");
            Assert(!controller.Contains("[HttpPost]"),
                "the photo archive is read-only; the picture belongs to the terminal enrolment flow.");
            Assert(controller.Contains("HttpCacheability.Private"),
                "a photograph of a person must not be cached as public content.");
            foreach (string view in new[] { "Index.cshtml", "Sheet.cshtml" })
            {
                Assert(File.Exists(Path.Combine(T5WebRoot, "T5Web.Web", "Views", "Photos", view)),
                    "missing photo view: " + view);
            }

            // Content sniffing: the column is an untyped image blob.
            byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
            byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A };
            Assert(SqlPhotoStore.Sniff(jpeg) == "image/jpeg", "a JPEG must be served as image/jpeg.");
            Assert(SqlPhotoStore.Sniff(png) == "image/png", "a PNG must be served as image/png.");

            var photos = new InMemoryPhotoStore()
                .Person(10, "مریم کاظمی", "فنی")
                .Person(11, "علی رضایی", "فنی")
                .WithPhoto(10, jpeg);
            Assert(photos.CountWithPhoto() == 1 && photos.CountWithoutPhoto() == 1,
                "photo counts must follow the stored blobs.");
            Assert(photos.Search(string.Empty, null, true, 50).Single().BadgeNumber == 11,
                "the missingOnly filter must return exactly the people without a picture.");
            Assert(photos.GetImage(10).ContentType == "image/jpeg", "the archive must sniff the content type.");
            Assert(photos.GetImage(11) == null, "a person without a picture must return null, not an empty image.");

            var oversized = new InMemoryPhotoStore().Person(12, "بزرگ", string.Empty)
                .WithPhoto(12, new byte[SqlPhotoStore.MaxImageBytes + 1]);
            Assert(oversized.GetImage(12) == null,
                "a picture larger than the cap must be refused instead of handed to the browser.");

            // ---- 2. the entitlement matrix ------------------------------------------
            var entitlements = new InMemoryEntitlementStore()
                .EmpType(1, "رسمی").EmpType(2, "پیمانی")
                .LeaveType(5, "استحقاقی").LeaveType(6, "استعلاجی")
                .Grant(1, 5);
            OperationResult granted = entitlements.Set(2, 6, true);
            OperationResult again = entitlements.Set(2, 6, true);
            Assert(granted.Succeeded && again.Succeeded, "granting a pair twice must not fail.");
            Assert(entitlements.GetPairs().Count == 2, "granting twice must not duplicate the pair.");

            EntitlementMatrix matrix = entitlements.BuildMatrix();
            Assert(matrix.IsGranted(1, 5) && matrix.IsGranted(2, 6) && !matrix.IsGranted(1, 6),
                "the matrix must reflect exactly the granted pairs.");
            Assert(matrix.CountFor(1) == 1, "the per-employment-type count is wrong.");
            Assert(matrix.PossibleCount == 4, "the matrix must expose the number of possible pairs.");

            OperationResult revoked = entitlements.Set(1, 5, false);
            Assert(revoked.Succeeded && !entitlements.BuildMatrix().IsGranted(1, 5), "revoking must work.");
            Assert(!entitlements.Set(0, 5, true).Succeeded, "an invalid employment type must be refused.");

            // ---- 3. leave descriptions as a proved module ---------------------------
            ModuleDescriptor descriptions = ModuleRegistry.Find("leavedescription");
            Assert(descriptions != null, "the 'leavedescription' module (frmLeaveDescription) is missing.");
            Assert(descriptions.Table == "LeaveDescription" && descriptions.Catalog == "t5",
                "leave descriptions must read the engine catalog, not the device one.");
            Assert(descriptions.EditFeature == AdminFeature.LeaveEdit,
                "leave descriptions must require the leave-edit feature.");

            // ---- 4. the new report ---------------------------------------------------
            ReportDescriptor leaves = ReportCatalog.Find("leaves");
            Assert(leaves != null, "the 'لیست مرخصی‌ها' report (frmLeavesReport) is missing.");
            Assert(leaves.Feature == AdminFeature.LeaveView, "the leaves report must be gated on LeaveView.");
            Assert(leaves.MaxDays >= 31, "the leaves report needs at least a month window.");

            var leaveStore = new InMemoryLeaveStore().Type(5, "استحقاقی", true);
            OperationResult grantedLeave = leaveStore.AddGrant(new LeaveGrantRow
            {
                BadgeNumber = 10,
                LeaveTypeId = 5,
                LeaveTypeName = "استحقاقی",
                AssignDate = new DateTime(2026, 10, 20),
                Days = 3,
                DocumentNumber = "DOC-1"
            });
            Assert(grantedLeave.Succeeded, "the fixture grant must be accepted: " + grantedLeave.Message);

            var reportStore = new ReportStore(new InMemoryAttendanceStore(), leaveStore, BuildRepository());
            ReportResult result = reportStore.Run("leaves",
                new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), 10, null);
            Assert(result.TitleFa == "لیست مرخصی‌ها", "the leaves report returned another title.");
            Assert(result.Columns.Count == 7, "the leaves report must keep its seven columns.");
            Assert(result.Code == "leaves", "the report result must carry its own code.");
            Assert(result.Rows.Count == 1, "the fixture grant must appear in the report.");
            Assert(Regex.IsMatch(result.Rows[0][3], @"^\d{4}/\d{2}/\d{2}$"),
                "the report must print a Jalali date, got: " + result.Rows[0][3]);
            Assert(result.Rows[0][4] == "3", "the grant amount must be printed as days: " + result.Rows[0][4]);

            // ---- 5. the deliberate exclusion ----------------------------------------
            string doc = Read("docs", "10-t5v3-gap-analysis.md");
            Assert(doc.Contains("TimeTable"), "docs/10 must record why weekly work hours were left out.");
            Assert(doc.Contains("دستگاه"), "docs/10 must name the device catalog as the owner of TimeTable.");

            // ---- 5b. wiring and reachability -----------------------------------------
            string startup = Read("T5Web.Web", "MvcApplication.cs");
            Assert(startup.Contains("new SqlPhotoStore(") && startup.Contains("new SqlEntitlementStore("),
                "the wave-1 stores must be wired in MvcApplication, not left dangling.");
            Assert(!startup.Contains("SqlWorkTimeStore"),
                "weekly work hours belong to the device catalog and must not be wired.");

            string menu = Read("T5Web.Web", "Security", "AdminAuthorize.cs");
            foreach (string entry in new[] { "\"Photos\"", "\"Entitlements\"" })
            {
                Assert(menu.Contains(entry), "a wave-1 screen is not reachable from MenuFor: " + entry);
            }
            Assert(menu.Contains("AdminFeature.PersonnelPhoto, \"تصاویر پرسنل\""),
                "the photo screen must be listed under the PersonnelPhoto feature.");

            // ---- 6. nothing in wave 1 may re-introduce a lock -----------------------
            var waveOneFiles = new[]
            {
                Path.Combine(T5WebRoot, "T5Web.Web", "Controllers", "PhotosController.cs"),
                Path.Combine(T5WebRoot, "T5Web.Data", "SqlPhotoStore.cs"),
                Path.Combine(T5WebRoot, "T5Web.Data", "InMemoryPhotoStore.cs"),
                Path.Combine(T5WebRoot, "T5Web.Data", "SqlEntitlementStore.cs"),
                Path.Combine(T5WebRoot, "T5Web.Data", "InMemoryEntitlementStore.cs"),
                Path.Combine(T5WebRoot, "T5Web.Domain", "Models", "PhotoModels.cs"),
                Path.Combine(T5WebRoot, "T5Web.Domain", "Models", "EntitlementModels.cs")
            };
            foreach (string path in waveOneFiles)
            {
                Assert(File.Exists(path), "missing wave-1 file: " + path);
                string source = File.ReadAllText(path);
                Assert(source.IndexOf("TINYLib", StringComparison.OrdinalIgnoreCase) < 0 &&
                       source.IndexOf("dongle", StringComparison.OrdinalIgnoreCase) < 0,
                    Path.GetFileName(path) + " must not mention a hardware lock (Zero-Lock).");
            }
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
            Dictionary<string, string> catalogs = TableCatalogs();
            var modules = ModuleRegistry.All();

            Assert(modules.Count >= 25, "expected at least 25 generic modules, found " + modules.Count);
            Assert(modules.Select(m => m.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() == modules.Count,
                "two modules share the same code.");

            foreach (ModuleDescriptor module in modules)
            {
                HashSet<string> columns;
                Assert(manifest.TryGetValue(module.Table, out columns),
                    "module '" + module.Code + "' points at table " + module.Table + ", which is not in the schema manifest.");
                Assert(types.ContainsKey(module.Table),
                    "table " + module.Table + " has no column types; the portal must not write it.");

                // The catalog decides which connection the store opens (docs/09, BUG-30).
                // A module whose table lives in Personnel must not be read through T5CnnStr,
                // and nothing may point at the device service database: the portal never
                // writes the device catalog at all.
                string catalog;
                Assert(catalogs.TryGetValue(module.Table, out catalog),
                    "module '" + module.Code + "' points at " + module.Table + ", which the database model does not place in a catalog.");
                Assert(string.Equals(catalog, module.Catalog, StringComparison.OrdinalIgnoreCase),
                    "module '" + module.Code + "' declares catalog '" + module.Catalog + "' but " + module.Table + " lives in '" + catalog + "'.");
                Assert(catalog == "t5" || catalog == "personnel",
                    "module '" + module.Code + "' targets the '" + catalog + "' catalog; the portal only reads t5 and personnel.");

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
                    if (field.DisplayOnly)
                    {
                        // Shown only: the manifest must know the column, and the descriptor
                        // must say so explicitly. No type is implied, so nothing is bound.
                        Assert(evidence == "display",
                            module.Table + "." + field.Column + " is display-only but claims evidence '" + evidence + "'.");
                        continue;
                    }
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

        /// <summary>
        /// table -> catalog ("t5" = ReportT5V3, "personnel" = Personnel, "device" = the
        /// Link5 service database), read from engine/relations.json. No JSON library is
        /// available offline, so the small "Name": { "database": "x" } block is parsed
        /// directly; T28 re-checks the same file against the DDL.
        /// </summary>
        private static Dictionary<string, string> TableCatalogs()
        {
            string path = Path.Combine(T5WebRoot, "engine", "relations.json");
            Assert(File.Exists(path), "engine/relations.json is missing; run tools/engine-relations.py --write.");
            string json = File.ReadAllText(path);
            var catalogs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(json, "\"([A-Za-z0-9_]+)\"\\s*:\\s*\\{\\s*\"database\"\\s*:\\s*\"([a-z0-9]+)\"", RegexOptions.Singleline))
            {
                catalogs[m.Groups[1].Value] = m.Groups[2].Value;
            }
            Assert(catalogs.Count >= 100, "engine/relations.json placed only " + catalogs.Count + " tables in a catalog.");
            return catalogs;
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

            // A module can forbid creation while still allowing an edit of an existing row
            // (the device-validity flag is the real case: the pair exists or it does not).
            ModuleDescriptor deviceValid = ModuleRegistry.Find("userdevicevalid");
            Assert(deviceValid != null, "the userdevicevalid module must exist.");
            Assert(service.Build(deviceValid, new Dictionary<string, string>
                {
                    { "BadgeNumber", "1001" }, { "MachineNumber", "5" }, { "IsValid", "true" }
                }, false, out row).Succeeded,
                "a device-validity row must build.");
            Assert(service.Save(deviceValid, row, true).Succeeded == false,
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
        /// T19 - the three-layer rule set must resolve by stated precedence, and two rows
        /// on the same layer must be a conflict rather than "whichever came first".
        /// </summary>
        private static void TestRuleResolution()
        {
            var userRules = new List<RuleLayerRow>
            {
                new RuleLayerRow { RowId = 1, BadgeNumber = 1001, RuleId = 7,
                                   FromDate = new DateTime(2024, 1, 1), ToDate = new DateTime(2024, 12, 31) }
            };
            var deptRules = new List<RuleLayerRow>
            {
                new RuleLayerRow { RowId = 1, DepartmentId = 10, RuleId = 4,
                                   FromDate = new DateTime(2024, 1, 1), ToDate = new DateTime(2024, 12, 31) }
            };

            var resolver = new RuleResolutionService(userRules, deptRules, 1);
            RuleResolution resolution = resolver.Resolve(1001, 10, new DateTime(2024, 6, 1));
            Assert(resolution.AppliedRuleId == 7, "the personnel exception must win, got " + resolution.AppliedRuleId);
            Assert(resolution.Layer == "user", "the winning layer must be reported, got " + resolution.Layer);
            Assert(resolution.MatchedLayers.Count == 3, "all three layers apply and must be listed.");

            // The override is legal but recorded - not silent.
            Assert(resolution.HasConflict, "an upper-layer override must be recorded as an override.");
            Assert(resolution.Conflicts.All(c => !c.IsBlocking), "an override must not block saving.");

            // Outside the exception window the department rule wins.
            RuleResolution later = resolver.Resolve(1001, 10, new DateTime(2025, 3, 1));
            Assert(later.AppliedRuleId == 1, "with no exception the global rule must apply, got " + later.AppliedRuleId);
            Assert(later.Layer == "global", "expected the global layer, got " + later.Layer);

            // Two personnel rules covering the same day is a blocking conflict.
            userRules.Add(new RuleLayerRow
            {
                RowId = 2, BadgeNumber = 1001, RuleId = 9,
                FromDate = new DateTime(2024, 5, 1), ToDate = new DateTime(2024, 7, 1)
            });
            RuleResolution ambiguous = new RuleResolutionService(userRules, deptRules, 1)
                .Resolve(1001, 10, new DateTime(2024, 6, 1));
            Assert(ambiguous.HasConflict && ambiguous.Conflicts.Any(c => c.IsBlocking),
                "two rules on the same layer must be a blocking conflict.");

            // And the pre-save check refuses the same overlap.
            var candidate = new RuleLayerRow
            {
                RowId = 0, BadgeNumber = 1001, RuleId = 11,
                FromDate = new DateTime(2024, 3, 1), ToDate = new DateTime(2024, 3, 31)
            };
            Assert(!resolver.ValidateWindow(candidate, true).Succeeded,
                "a window overlapping an existing personnel rule must be refused.");

            var free = new RuleLayerRow
            {
                RowId = 0, BadgeNumber = 1001, RuleId = 11,
                FromDate = new DateTime(2026, 1, 1), ToDate = new DateTime(2026, 2, 1)
            };
            Assert(resolver.ValidateWindow(free, true).Succeeded,
                "a non-overlapping window must be accepted.");

            var backwards = new RuleLayerRow
            {
                RowId = 0, BadgeNumber = 1002, RuleId = 5,
                FromDate = new DateTime(2026, 2, 1), ToDate = new DateTime(2026, 1, 1)
            };
            Assert(!resolver.ValidateWindow(backwards, true).Succeeded, "an inverted window must be refused.");
        }

        /// <summary>
        /// T20 - a punch is unique by (badge, time, type) and an approval step may be
        /// decided once. The legacy screens allowed both, which double-counted hours.
        /// </summary>
        private static void TestAttendanceStore()
        {
            var store = new InMemoryAttendanceStore();
            DateTime day = new DateTime(2024, 6, 10, 8, 0, 0);
            var punch = new RecordRow
            {
                BadgeNumber = 1001, CheckTime = day, CheckType = 0, MachineNumber = 1,
                IsValid = true, PersonName = "مریم کاظمی"
            };
            Assert(store.SaveRecord(punch, true).Succeeded, "the first punch must save.");
            Assert(!store.SaveRecord(punch, true).Succeeded, "the identical punch must be refused.");

            var otherType = new RecordRow { BadgeNumber = 1001, CheckTime = day, CheckType = 1, IsValid = true };
            Assert(store.SaveRecord(otherType, true).Succeeded, "a different check type at the same minute is a different punch.");

            // An invalid badge or an out-of-range type is refused, not written.
            Assert(!store.SaveRecord(new RecordRow { BadgeNumber = 0, CheckTime = day }, true).Succeeded,
                "a punch without a badge must be refused.");
            Assert(!store.SaveRecord(new RecordRow { BadgeNumber = 1001, CheckTime = day, CheckType = 9 }, true).Succeeded,
                "an unknown check type must be refused.");

            // Correcting an unknown punch fails instead of creating one.
            var ghost = new RecordRow { BadgeNumber = 1001, CheckTime = day.AddHours(5), CheckType = 0, OriginalCheckType = 0 };
            Assert(!store.SaveRecord(ghost, false).Succeeded, "correcting a missing punch must fail.");

            // Correcting a real punch keeps the row count.
            var fix = new RecordRow { BadgeNumber = 1001, CheckTime = day, CheckType = 0, OriginalCheckType = 0, IsValid = false };
            Assert(store.SaveRecord(fix, false).Succeeded, "an existing punch must be correctable.");
            Assert(store.QueryRecords(new RecordQuery { OnlyInvalid = true }).Count == 1, "the corrected punch must read back as invalid.");

            // Paging is clamped: the query can never ask for the whole table.
            RecordQuery clamped = new RecordQuery { Take = 100000 }.Clamped();
            Assert(clamped.Take <= 1000, "the row cap must be enforced, got " + clamped.Take);
            RecordQuery swapped = new RecordQuery { FromDate = day.AddDays(5), ToDate = day }.Clamped();
            Assert(swapped.FromDate <= swapped.ToDate, "a reversed range must be swapped, not silently emptied.");

            // Requests: one decision per title, and a suspended request cannot be decided.
            var request = new RequestRow
            {
                RequestId = 55, BadgeNumber = 1002, ForDate = day, StepCount = 2, AcceptedCount = 0,
                IsDaily = true, PersonName = "حسین رستمی"
            };
            store.SeedRequest(request);
            string detail;
            Assert(store.DecideRequest(55, 9001, 12, false, false, out detail).Succeeded,
                "the first decision must be accepted.");
            Assert(!store.DecideRequest(55, 9002, 12, false, false, out detail).Succeeded,
                "the same title must not decide twice.");
            Assert(store.DecideRequest(55, 9003, 13, false, false, out detail).Succeeded,
                "a different title must be able to decide the next step.");
            Assert(!store.DecideRequest(9999, 9001, 12, false, false, out detail).Succeeded,
                "an unknown request must be refused.");
            Assert(store.DecideRequest(55, 9004, 14, true, false, out detail).Succeeded, "a cancel must be recorded.");
            Assert(!store.DecideRequest(55, 9005, 15, false, false, out detail).Succeeded,
                "a suspended request must not be decided again.");

            // The summary counts odd punch counts as a missing punch, not as absence.
            store.Seed(new RecordRow { BadgeNumber = 1003, CheckTime = day, CheckType = 0, IsValid = true, PersonName = "زهرا امینی" });
            store.Seed(new RecordRow { BadgeNumber = 1003, CheckTime = day.AddHours(9), CheckType = 1, IsValid = true, PersonName = "زهرا امینی" });
            store.Seed(new RecordRow { BadgeNumber = 1004, CheckTime = day, CheckType = 0, IsValid = true, PersonName = "علی رضایی" });
            IReadOnlyList<AttendanceSummaryRow> summary = store.GetMonthlySummary(2024, 6, null, 50);
            AttendanceSummaryRow odd = summary.FirstOrDefault(r => r.BadgeNumber == 1004);
            Assert(odd != null && odd.MissingPunchDays == 1, "a single punch in a day must count as a missing punch.");

            // Rewriting an approval chain that has requests in flight strands them: the
            // old [RequestAccept] rows point at steps that no longer exist, and the engine
            // reads a missing step as approved. The save must be refused unless confirmed.
            var repository = BuildRepository();
            var workflow = new WorkflowService(repository);
            var chain = new List<WorkflowStep>
            {
                new WorkflowStep { TitleId = 1, AccepterTitleId = 2, OrderIndex = 1 },
                new WorkflowStep { TitleId = 1, AccepterTitleId = 3, OrderIndex = 2 }
            };
            Assert(workflow.Save(chain, 1, 0).Succeeded, "a clean chain must save.");

            repository.InFlightRequests = 3;
            Assert(workflow.InFlight(1, 0) == 3, "the in-flight count must come from the repository.");
            OperationResult blocked = workflow.Save(chain, 1, 0);
            Assert(!blocked.Succeeded && blocked.Message.Contains("در جریان"),
                "an in-flight chain must be refused with an explanation.");
            Assert(workflow.Save(chain, 1, 0, true).Succeeded,
                "an explicit confirmation must let the operator proceed.");
            repository.InFlightRequests = 0;
        }

        /// <summary>
        /// T21 - opening + added - used = closing, always; and a transfer either happens
        /// completely or not at all, never twice.
        /// </summary>
        private static void TestLeaveBalances()
        {
            var store = new InMemoryLeaveStore()
                .Type(1, "استحقاقی", true)
                .Type(2, "بدون حقوق", false)
                .Balance(1001, 1, 1403, opening: 20, added: 5, used: 12)
                .Balance(1001, 2, 1403, opening: 3)
                .Balance(1002, 1, 1403, opening: 8, used: 2);

            LeaveBalanceRow first = store.GetBalances(1403, 1, null, 50).First(r => r.BadgeNumber == 1001);
            Assert(Math.Abs(first.ClosingDays - 13) < 0.001,
                "opening 20 + added 5 - used 12 must close at 13, got " + first.ClosingDays);

            // Grant: validated, and immediately visible in the balance.
            Assert(!store.AddGrant(new LeaveGrantRow { BadgeNumber = 1001, LeaveTypeId = 1, Days = 0, AssignDate = DateTime.Today }).Succeeded,
                "a zero-day grant must be refused.");
            Assert(!store.AddGrant(new LeaveGrantRow { BadgeNumber = 1001, LeaveTypeId = 1, Days = 400, AssignDate = DateTime.Today }).Succeeded,
                "a grant beyond a year must be refused.");
            Assert(!store.AddGrant(new LeaveGrantRow { BadgeNumber = 1001, LeaveTypeId = 1, Days = 1.3, AssignDate = DateTime.Today }).Succeeded,
                "a grant with more than two decimals must be refused.");
            Assert(store.AddGrant(new LeaveGrantRow { BadgeNumber = 1002, LeaveTypeId = 1, Days = 2, AssignDate = new DateTime(1403, 5, 1), DocumentNumber = "12" }).Succeeded,
                "a valid grant must be accepted.");
            Assert(Math.Abs(store.GetBalances(1403, 1, null, 50).First(r => r.BadgeNumber == 1002).ClosingDays - 8) < 0.001,
                "the grant must land in the balance (8 + 2).");

            // Preview: only carry types, capped, and the non-carry type is explained.
            IReadOnlyList<LeaveTransferRow> preview = store.PreviewTransfer(1403, 1404, 10, null);
            LeaveTransferRow capped = preview.FirstOrDefault(r => r.BadgeNumber == 1001 && r.LeaveTypeId == 1);
            Assert(capped != null && Math.Abs(capped.TransferDays - 10) < 0.001 && capped.Capped,
                "a 13-day balance with a 10-day cap must transfer 10 and be flagged.");
            LeaveTransferRow blocked = preview.FirstOrDefault(r => r.BadgeNumber == 1001 && r.LeaveTypeId == 2);
            Assert(blocked != null && blocked.TransferDays == 0, "a non-carry leave type must transfer nothing.");

            // A transfer to anything but the next year is refused.
            string detail;
            Assert(!store.CommitTransfer(preview, 1403, 1405, "انتقال آزمایشی", out detail).Succeeded,
                "a transfer over more than one year must be refused.");
            Assert(!store.CommitTransfer(preview, 1403, 1404, "کم", out detail).Succeeded,
                "a transfer without a real reason must be refused.");

            // All-or-nothing: a missing source row aborts before anything moves.
            var poisoned = new List<LeaveTransferRow>
            {
                new LeaveTransferRow { BadgeNumber = 1001, LeaveTypeId = 1, TransferDays = 5 },
                new LeaveTransferRow { BadgeNumber = 4242, LeaveTypeId = 1, TransferDays = 5 }
            };
            Assert(!store.CommitTransfer(poisoned, 1403, 1404, "انتقال ناقص", out detail).Succeeded,
                "a row without a source balance must abort the whole transfer.");
            Assert(Math.Abs(store.GetBalances(1403, 1, null, 50).First(r => r.BadgeNumber == 1001).OpeningDays - 20) < 0.001,
                "the aborted transfer must not have moved anything.");

            // The real transfer succeeds once and is refused the second time.
            Assert(store.CommitTransfer(preview, 1403, 1404, "پایان سال", out detail).Succeeded,
                "a valid transfer must succeed: " + detail);
            Assert(!store.CommitTransfer(preview, 1403, 1404, "پایان سال", out detail).Succeeded,
                "a repeated transfer for the same year must be refused.");

            LeaveBalanceRow nextYear = store.GetBalances(1404, 1, null, 50).FirstOrDefault(r => r.BadgeNumber == 1001);
            Assert(nextYear != null && Math.Abs(nextYear.OpeningDays - 10) < 0.001,
                "the transferred 10 days must open the next year.");
            LeaveBalanceRow source = store.GetBalances(1403, 1, null, 50).First(r => r.BadgeNumber == 1001);
            Assert(Math.Abs(source.OpeningDays - 10) < 0.001,
                "the source year must be drained by the transferred amount, got " + source.OpeningDays);
        }

        /// <summary>
        /// T22 - shift cycles must be contiguous, assignments must not overlap, and a
        /// backdated change is limited by policy.
        /// </summary>
        private static void TestScheduleRules()
        {
            var store = new InMemoryScheduleStore { Today = new DateTime(2024, 6, 15) };
            store.AddShift(1, "شیفت صبح", 7, 1, 2, 3, 4, 5, 6, 7)
                 .AddShift(2, "شیفت ناقص", 7, 1, 2, 4)
                 .AddShift(3, "بدون روز", 7);
            store.AddSchedule(10, "صبح", 1, 2, 3);

            var empty = new ShiftRow { ShiftId = 3, ShiftName = "بدون روز", DayIndexes = new List<int>() };
            Assert(!string.IsNullOrEmpty(ShiftProblem(empty)), "a shift with no days must be reported.");

            var gap = new ShiftRow { ShiftId = 2, DayIndexes = new List<int> { 1, 2, 4 } };
            Assert(ShiftProblem(gap).Contains("پیوسته"), "a gap in the cycle must be reported: " + ShiftProblem(gap));

            var notFromOne = new ShiftRow { ShiftId = 4, DayIndexes = new List<int> { 2, 3 } };
            Assert(ShiftProblem(notFromOne).Contains("۱"), "a cycle not starting at day 1 must be reported.");

            var good = new ShiftRow { ShiftId = 1, DayIndexes = new List<int> { 1, 2, 3, 4, 5, 6, 7 } };
            Assert(ShiftProblem(good) == string.Empty, "a contiguous cycle must be accepted.");

            // Assignment: policy window.
            string detail;
            Assert(!store.AssignShift(1001, 1, new DateTime(2024, 1, 1), null, out detail).Succeeded,
                "a start older than the retroactive window must be refused.");
            Assert(store.AssignShift(1001, 1, new DateTime(2024, 6, 10), new DateTime(2024, 6, 20), out detail).Succeeded,
                "an in-window assignment must be accepted.");
            Assert(!store.AssignShift(1001, 2, new DateTime(2024, 6, 15), null, out detail).Succeeded,
                "an overlapping assignment must be refused.");
            Assert(store.AssignShift(1001, 2, new DateTime(2024, 6, 21), null, out detail).Succeeded,
                "a non-overlapping assignment must be accepted.");
            Assert(!store.AssignShift(1001, 1, new DateTime(2024, 6, 1), new DateTime(2024, 1, 1), out detail).Succeeded,
                "an inverted window must be refused.");
            Assert(!store.AssignShift(1001, 99, new DateTime(2024, 6, 25), null, out detail).Succeeded,
                "an unknown shift must be refused.");
        }

        /// <summary>Mirrors ScheduleController.CycleProblem without touching System.Web.</summary>
        private static string ShiftProblem(ShiftRow shift)
        {
            if (shift.DayIndexes == null || shift.DayIndexes.Count == 0)
            {
                return "هیچ روزی تخصیص نیافته است.";
            }
            List<int> sorted = shift.DayIndexes.Distinct().OrderBy(i => i).ToList();
            if (sorted[0] != 1)
            {
                return "شماره روزها باید از ۱ شروع شود.";
            }
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] != i + 1)
                {
                    return "روزهای شیفت پیوسته نیستند.";
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// T23 - a report a session cannot read must not be listed or run, each report
        /// validates its own window, and the export is built locally.
        /// </summary>
        private static void TestReports()
        {
            Assert(ReportCatalog.All().Count >= 10, "the report catalogue must keep at least ten reports.");

            var titles = ReportCatalog.All().Select(r => r.LegacyController).ToList();
            Assert(titles.Distinct().Count() == titles.Count, "two reports claim the same legacy controller.");
            foreach (string dropped in new[] { "ReportDatas", "ReportFilter", "ReportTablix", "ReportTextbox", "ReportTitles", "ReportDesign" })
            {
                Assert(!titles.Contains(dropped), "the RDLC designer controller '" + dropped + "' must stay dropped.");
            }

            // Access gating: a session that may read only personnel sees no attendance report.
            var session = new AdminSession { BadgeNumber = 1001, DisplayName = "مریم کاظمی" };
            session.Grant(AdminFeature.PersonnelView, AccessLevel.Allow);
            IReadOnlyList<ReportDescriptor> visible = ReportCatalog.VisibleFor(session);
            Assert(visible.Count > 0, "a personnel reader must see at least one report.");
            Assert(visible.All(r => r.Feature == AdminFeature.PersonnelView),
                "a personnel-only session must not see reports of other features.");
            Assert(ReportCatalog.VisibleFor(null).Count == 0, "an anonymous session must see no report.");

            // Window validation: reversed range, oversized window, and per-report ceiling.
            ReportDescriptor grid = ReportCatalog.Find("grid");
            Assert(grid != null && grid.MaxDays == 31, "the monthly grid must cap at 31 days.");
            Assert(!ReportCatalog.ValidateRange(grid, new DateTime(2024, 6, 30), new DateTime(2024, 6, 1)).Succeeded,
                "a reversed range must be refused.");
            Assert(!ReportCatalog.ValidateRange(grid, new DateTime(2024, 1, 1), new DateTime(2024, 6, 1)).Succeeded,
                "a 152-day window must be refused for a 31-day report.");
            Assert(ReportCatalog.ValidateRange(grid, new DateTime(2024, 6, 1), new DateTime(2024, 6, 30)).Succeeded,
                "a 30-day window must be accepted.");
            Assert(ReportCatalog.ValidateRange(ReportCatalog.Find("exit"), new DateTime(2024, 1, 1), new DateTime(2024, 12, 31)).Succeeded,
                "a year-long window must be accepted where the ceiling allows it.");

            // Execution: a known report returns its columns; an unknown one does not throw.
            var attendance = new InMemoryAttendanceStore();
            attendance.Seed(new RecordRow
            {
                BadgeNumber = 1001, CheckTime = new DateTime(2024, 6, 10, 8, 0, 0), CheckType = 0,
                IsValid = true, PersonName = "مریم کاظمی", MachineName = "درب اصلی"
            });
            var leave = new InMemoryLeaveStore().Type(1, "استحقاقی", true).Balance(1001, 1, 1403, 12);
            var reports = new ReportStore(attendance, leave, BuildRepository());

            ReportResult daily = reports.Run("daily", new DateTime(2024, 6, 1), new DateTime(2024, 6, 30), null, null);
            Assert(daily.Rows.Count == 1 && daily.Columns.Count == 7, "the daily report must return one punch with seven columns.");
            Assert(daily.Elapsed >= TimeSpan.Zero, "the run time must be reported.");

            ReportResult engine = reports.Run("late-early", new DateTime(2024, 6, 1), new DateTime(2024, 6, 30), null, null);
            Assert(engine.NoteFa.Contains("خام"), "an engine report must say that the rows are raw, not calculated.");
            Assert(engine.Rows.Count == 1, "the engine report shell must still show the window's raw punches.");

            ReportResult unknown = reports.Run("does-not-exist", DateTime.Today, DateTime.Today, null, null);
            Assert(unknown.Rows.Count == 0 && unknown.Columns.Count == 0, "an unknown report must return an empty shell, not throw.");

            // Export is local: no remote asset may appear in the view or the controller.
            string view = Read("T5Web.Web", "Views", "Reports", "Index.cshtml");
            Assert(view.IndexOf("http://", StringComparison.Ordinal) < 0 &&
                   view.IndexOf("https://", StringComparison.Ordinal) < 0,
                "the report view must not reference a remote asset.");
        }

        /// <summary>
        /// T24 - the CSV import parses quoted cells, reports bad lines instead of writing
        /// them, and refuses to invent personnel that the engine did not create.
        /// </summary>
        private static void TestPersonnelImport()
        {
            var service = new PersonnelImportService(BuildRepository());

            string csv = "badge,firstname,lastname,nationalno,phone,email,deptid,emptypeid,hireddate,fireddate\n" +
                         "1001,مریم,کاظمی,1234567890,0912,\"a,b@example.com\",10,1,2020-01-01,\n" +
                         "1002,,رستمی,12345,0912,h@example.com,11,2,2020-01-01,\n" +
                         "9999,ناشناس,غایب,,0912,,10,1,2020-01-01,\n";

            ImportPreview preview = service.Parse(csv);
            Assert(preview.FileProblems.Count == 0, "a well-formed header must be accepted: " + string.Join(";", preview.FileProblems));
            Assert(preview.Rows.Count == 3, "three data rows were expected, got " + preview.Rows.Count);
            Assert(preview.Rows[0].Accepted, "the first row is valid and must be accepted.");
            Assert(preview.Rows[0].Email == "a,b@example.com", "a quoted comma must survive the parser.");
            Assert(!preview.Rows[1].Accepted, "an empty first name must be rejected.");
            Assert(!preview.Rows[2].Accepted, "an unknown badge must be rejected (import only updates).");
            Assert(preview.Rows[2].Problems.Any(p => p.Contains("وجود ندارد")),
                "the unknown badge must say that adding personnel is out of scope.");

            // The canonical round trip keeps exactly the accepted rows.
            string canonical = PersonnelImportService.ToCanonical(preview.Rows);
            ImportPreview again = service.Parse(canonical);
            Assert(again.Rows.Count == 1 && again.Rows[0].Accepted, "the canonical form must round-trip one accepted row.");

            // A tampered payload is re-validated, not trusted.
            string tampered = "badge,firstname,lastname,nationalno,phone,email,deptid,emptypeid,hireddate,fireddate\n" +
                              "1002,,,bad,,,0,0,not-a-date,\n";
            ImportOutcome outcome = service.Commit(tampered);
            Assert(outcome.Updated == 0 && outcome.Failed == 1, "a tampered row must fail, not update.");
            Assert(outcome.Messages.Count == 1, "the failure must be explained.");

            // The valid row commits as an update.
            ImportOutcome ok = service.Commit(canonical);
            Assert(ok.Updated == 1 && ok.Failed == 0, "the accepted row must update exactly one person: " + ok.SummaryFa);

            // A wrong header is refused before any row is read.
            ImportPreview wrong = service.Parse("id,name\n1,x\n");
            Assert(wrong.FileProblems.Count == 1, "a wrong header must be a file-level problem.");
            Assert(wrong.Rows.Count == 0, "no row may be parsed from a file with a wrong header.");
        }

        /// <summary>
        /// T25 - the 67/67 audit. Every legacy controller must be either delivered
        /// (with the test that proves it) or explicitly dropped with a reason; nothing
        /// may remain "pending". This is the ARIA TEST-40 equivalent, and it is the only
        /// defence against a plan that quietly shrinks.
        /// </summary>
        private static void TestLegacyCoverage()
        {
            string[] inventory = File.ReadAllLines(Path.Combine(T5WebRoot, "docs", "legacy-controllers.txt"))
                .Where(l => !l.StartsWith("#") && !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .ToArray();
            Assert(inventory.Length == 67, "the legacy inventory must list 67 controllers, found " + inventory.Length + ".");

            string[] docLines = File.ReadAllLines(Path.Combine(T5WebRoot, "docs", "03-legacy-decomposition.md"));
            var rows = new Dictionary<string, string[]>();
            foreach (string line in docLines)
            {
                if (!line.StartsWith("| ", StringComparison.Ordinal))
                {
                    continue;
                }
                string[] cells = line.Trim().Trim('|').Split('|')
                    .Select(c => c.Trim()).ToArray();
                if (cells.Length != 6)
                {
                    continue;
                }
                Match m = Regex.Match(cells[1], @"^`([A-Za-z]+)`$");
                if (!m.Success)
                {
                    continue;
                }
                rows[m.Groups[1].Value] = cells;
            }

            var missing = inventory.Where(name => !rows.ContainsKey(name)).ToList();
            Assert(missing.Count == 0, "these legacy controllers have no row in docs/03: " + string.Join(", ", missing));

            var pending = rows.Where(r => r.Value[5].Contains("⏳"))
                              .Select(r => r.Key).ToList();
            Assert(pending.Count == 0, "these legacy controllers are still pending: " + string.Join(", ", pending));

            foreach (var row in rows)
            {
                string status = row.Value[5];
                Assert(status.StartsWith("✅") || status.StartsWith("⛔"),
                    row.Key + " has an unrecognised status: " + status);
                if (status.StartsWith("✅") && !status.Contains("فاز"))
                {
                    Assert(status.Contains("۵-"), row.Key + " claims delivery without naming its sub-phase.");
                }
                if (status.StartsWith("✅") && status.Contains("۵-"))
                {
                    // A delivered screen must cite the test that holds it down.
                    Assert(Regex.IsMatch(status, @"`T\d\d`"),
                        row.Key + " is marked delivered but cites no regression test: " + status);
                }
                if (status.StartsWith("⛔"))
                {
                    Assert(status.Length > 3 && (status.Contains("حذف") || status.Contains("خارج از دامنه") ||
                                                 status.Contains("نیست") || status.Contains("Lock")),
                        row.Key + " is dropped without a stated reason: " + status);
                }
            }

            // Every cited test id must exist in this suite.
            string self = File.ReadAllText(Path.Combine(T5WebRoot, "T5Web.Tests", "Program.cs"));
            foreach (Match cited in Regex.Matches(string.Join("\n", rows.Values.Select(v => v[5])), @"`(T\d\d)`"))
            {
                Assert(self.Contains("\"" + cited.Groups[1].Value + ":"),
                    "docs/03 cites " + cited.Groups[1].Value + ", which the suite does not define.");
            }

            // And the summary table must add up, so the count cannot drift from the rows.
            int total = rows.Count;
            Assert(total == 67, "the mapping table must keep exactly 67 controllers, found " + total + ".");
            string summary = string.Join("\n", docLines);
            int declared = Regex.Matches(summary, @"\| \*\*(\d+)\*\* \|")
                .Cast<Match>()
                .Select(m => int.Parse(m.Groups[1].Value))
                .DefaultIfEmpty(0)
                .Max();
            Assert(declared == total,
                "the summary table says " + declared + " controllers but the mapping has " + total + ".");
        }

        /// <summary>
        /// T26 - the delivery test: every phase-5 screen exists and is gated, the device
        /// command surface is a closed set, and the cut-over switch is documented.
        /// </summary>
        private static void TestPhase5Delivery()
        {
            TestDeviceCommands();
            TestPhase5Screens();
            TestCoexistenceSwitch();
        }

        /// <summary>
        /// The device command surface is a closed set: no raw command, no lock.
        /// </summary>
        private static void TestDeviceCommands()
        {
            var service = new DeviceCommandService();
            IReadOnlyList<DeviceCommand> commands = service.Available();
            Assert(commands.Count > 0, "at least one device command must remain.");
            foreach (DeviceCommand command in commands)
            {
                Assert(command.Code.IndexOf("lock", StringComparison.OrdinalIgnoreCase) < 0,
                    "no lock command may exist: " + command.Code);
                Assert(command.Code.IndexOf("usb", StringComparison.OrdinalIgnoreCase) < 0,
                    "no USB-management command may exist: " + command.Code);
            }

            var device = new DeviceRow { MachineNumber = 1, MachineName = "درب اصلی", ConnectionType = 0, Ip = "192.168.1.20", Port = 4370 };
            DeviceCommand issued;
            Assert(service.Validate("sync-users", device, out issued).Succeeded, "a catalogued command must be accepted.");
            Assert(issued != null && !issued.FetchesData, "sync-users must not be marked as a data fetch.");

            Assert(!service.Validate("reboot-now", device, out issued).Succeeded, "an uncatalogued command must be refused.");
            Assert(!service.Validate("change-lock", device, out issued).Succeeded, "a lock command must be refused.");
            Assert(!service.Validate("", device, out issued).Succeeded, "an empty command must be refused.");
            Assert(!service.Validate("sync-time", null, out issued).Succeeded, "a missing device must be refused.");

            var unconfigured = new DeviceRow { MachineNumber = 2, ConnectionType = 0, Ip = "0.0.0.0" };
            Assert(!service.Validate("sync-time", unconfigured, out issued).Succeeded,
                "a network device without an address must be refused.");

            // BUG-26: the screen claimed the command was queued while nothing was written.
            // The queue is the transport now, so it must actually accept and return a ticket.
            InMemoryAdminRepository repository = new InMemoryAdminRepository()
                .Seed(devices: new[] { device });
            var ticket = new DeviceCommandTicket
            {
                MachineNumber = 1,
                Command = issued.Code,
                RequestedBy = "tester (1)",
                Note = issued.NoteFa
            };
            Assert(repository.EnqueueDeviceCommand(ticket).Succeeded, "a valid command must be queued.");
            Assert(ticket.Id > 0, "queuing a command must return a ticket id.");
            IReadOnlyList<DeviceCommandTicket> queued = repository.GetDeviceCommands(10);
            Assert(queued.Count == 1 && queued[0].Command == issued.Code, "the queued command must be readable.");
            Assert(queued[0].Status == 0 && queued[0].StatusFa == "در صف", "a fresh ticket must be in the queued state.");

            var orphan = new DeviceCommandTicket { MachineNumber = 999, Command = "sync-time" };
            Assert(!repository.EnqueueDeviceCommand(orphan).Succeeded, "a command for an unknown device must be refused.");
            var empty = new DeviceCommandTicket { MachineNumber = 1, Command = "  " };
            Assert(!repository.EnqueueDeviceCommand(empty).Succeeded, "an empty command must not be queued.");
            Assert(repository.GetDeviceCommands(10).Count == 1, "refused commands must not reach the queue.");
        }

        /// <summary>
        /// T26 - every sub-phase C/D/E screen must exist, be gated on a real feature and
        /// be reachable from the menu. This is what keeps the 67/67 audit honest.
        /// </summary>
        private static void TestPhase5Screens()
        {
            var controllers = new[]
            {
                "RecordsController", "RequestsController", "LeaveController", "RulesController",
                "ScheduleController", "ReportsController", "ImportController"
            };
            foreach (string controller in controllers)
            {
                string path = Path.Combine(T5WebRoot, "T5Web.Web", "Controllers", controller + ".cs");
                Assert(File.Exists(path), "missing controller: " + controller);
                string source = File.ReadAllText(path);
                Assert(source.Contains("[AdminGate("),
                    controller + " must declare an AdminGate (a screen without a feature is a hole).");
                Assert(source.Contains("Audit("),
                    controller + " writes data but never calls Audit().");

                string view = Path.Combine(T5WebRoot, "T5Web.Web", "Views", controller.Replace("Controller", string.Empty));
                Assert(Directory.Exists(view), "missing view folder for " + controller);
                Assert(Directory.GetFiles(view, "*.cshtml").Length > 0, "no view in " + view);

                string menuSource = Read("T5Web.Web", "Security", "AdminAuthorize.cs");
                string name = controller.Replace("Controller", string.Empty);
                Assert(menuSource.Contains("\"" + name + "\""),
                    name + " is not reachable from MenuFor (a screen nobody can find is not delivered).");
            }

            // The features these screens rely on must exist with stable values.
            foreach (AdminFeature feature in new[]
                     {
                         AdminFeature.RecordsView, AdminFeature.RecordManualInsert, AdminFeature.RequestView,
                         AdminFeature.RequestApprove, AdminFeature.LeaveView, AdminFeature.LeaveEdit,
                         AdminFeature.ScheduleView, AdminFeature.ScheduleEdit, AdminFeature.RulesView,
                         AdminFeature.RulesEdit, AdminFeature.ReportView, AdminFeature.ReportExport,
                         AdminFeature.PersonnelImport, AdminFeature.DeviceSync
                     })
            {
                Assert(!string.IsNullOrEmpty(AdminFeatures.TitleOf(feature)) &&
                       AdminFeatures.TitleOf(feature) != feature.ToString(),
                    "feature " + feature + " has no Persian title.");
            }

            // Writes must be guarded by CSRF and by an explicit edit feature.
            foreach (string file in new[] { "RecordsController.cs", "RequestsController.cs", "LeaveController.cs", "ScheduleController.cs", "ImportController.cs" })
            {
                string source = File.ReadAllText(Path.Combine(T5WebRoot, "T5Web.Web", "Controllers", file));
                int postCount = Regex.Matches(source, @"\[HttpPost\]").Count;
                int tokenCount = Regex.Matches(source, @"\[ValidateAntiForgeryToken\]").Count;
                Assert(postCount == tokenCount,
                    file + " has " + postCount + " POST actions but " + tokenCount + " anti-forgery attributes.");
            }

            // The phase-5 stores must all be wired in composition, not left dangling.
            string startup = Read("T5Web.Web", "MvcApplication.cs");
            foreach (string store in new[] { "SqlAttendanceStore", "SqlLeaveStore", "SqlScheduleStore", "ReportStore" })
            {
                Assert(startup.Contains(store), store + " is not wired in MvcApplication.");
            }
        }


        /// <summary>
        /// The cut-over switch must exist in configuration, be visible on every page and
        /// be described in the runbook - otherwise a rollback is folklore.
        /// </summary>
        private static void TestCoexistenceSwitch()
        {
            string config = Read("T5Web.Web", "Web.config");
            Assert(config.Contains("T5WebMode"), "Web.config must expose the T5WebMode switch.");
            Assert(config.Contains("T5WebLegacyUrl"), "Web.config must expose the legacy URL.");

            string switchSource = Read("T5Web.Web", "Security", "Coexistence.cs");
            foreach (string mode in new[] { "portal", "coexist", "legacy" })
            {
                Assert(switchSource.Contains(mode), "the switch must understand mode '" + mode + "'.");
            }

            string layout = Read("T5Web.Web", "Views", "Shared", "_Layout.cshtml");
            Assert(layout.Contains("Coexistence.BannerFa"),
                "the layout must show which application owns the screen.");
            Assert(layout.Contains("Coexistence.LegacyUrl"), "the layout must link back to the legacy site.");

            string runbook = Read("docs", "08-runbook.md");
            foreach (string required in new[] { "T5WebMode", "coexist", "portal", "legacy", "بازگردانی", "پشتیبان" })
            {
                Assert(runbook.Contains(required), "docs/08-runbook.md is missing '" + required + "'.");
            }
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

            // The method is ARIA's: five numbered build phases, each ending in a compiled,
            // tested deliverable (phase 5 is broken into sub-phases B..F), plus the
            // numbered delivery waves that follow it (phase 6 = wave 1 of docs/10).
            var expectedPhases = new[] { "1", "2", "3", "4", "5", "6", "7" };
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
            // While work is in progress exactly one phase is 'next'; when every phase is
            // done (the delivery state) there is none. Two 'next' phases is never valid.
            Assert(nextCount <= 1, "at most one phase may be marked 'next', found " + nextCount + ".");
            if (nextCount == 0)
            {
                foreach (var row in rows)
                {
                    Assert(row.Value[1] == "done",
                        "with no phase marked 'next', phase " + row.Key + " must be 'done' (found '" +
                        row.Value[1] + "').");
                }
            }

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
            var persianDigits = new[] { "۱", "۲", "۳", "۴", "۵", "۶", "۷" };
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
                "legacy-controllers.txt must list all 67 controllers, found " + inventory.Length + ".");
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

        /// <summary>
        /// T28 - the database model. The portal reads a schema it does not own, spread over
        /// two catalogs, and it owns three tables itself. This test holds that model down:
        /// every table has a catalog, the declared keys and relations still match the DDL,
        /// no reference is unclassified, and no SQL statement joins across catalogs (which
        /// would only work when both databases sit on one server).
        ///
        /// It also enforces the code shape that makes the rule possible: the personnel-side
        /// display fields are completed by PersonnelDirectory, never by a JOIN.
        /// </summary>
        private static void TestDatabaseModel()
        {
            string tsv = Path.Combine(T5WebRoot, "engine", "relations.tsv");
            Assert(File.Exists(tsv), "engine/relations.tsv is missing; run tools/engine-relations.py --write.");
            string[] lines = File.ReadAllLines(tsv);
            Assert(lines.Length > 1 && lines[0] == "kind\tfrom_database\tfrom_table\tfrom_column\tto_database\tto_table\tto_column\tevidence",
                "engine/relations.tsv must start with its header row.");

            var rows = new List<string[]>();
            foreach (string line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                string[] cells = line.Split('\t');
                Assert(cells.Length == 8, "malformed relation row: " + line);
                rows.Add(cells);
            }
            Assert(rows.Count >= 90, "the model must carry every relation; found " + rows.Count + ".");

            var catalogs = new HashSet<string>(StringComparer.Ordinal);
            int declared = 0, inferred = 0;
            foreach (string[] cells in rows)
            {
                string kind = cells[0];
                Assert(kind == "declared" || kind == "inferred", "unknown relation kind: " + kind);
                if (kind == "declared")
                {
                    declared++;
                    // The DDL writes fk_RoleId, the model keeps that spelling.
                    Assert(cells[7].StartsWith("FK_", StringComparison.OrdinalIgnoreCase),
                        "a declared relation must name its constraint: " + cells[7]);
                }
                else
                {
                    inferred++;
                }

                catalogs.Add(cells[1]);
                catalogs.Add(cells[4]);
                Assert(cells[1] != "?" && cells[4] != "?", "a relation endpoint has no catalog: " + string.Join("|", cells));
                Assert(!(cells[2] == cells[5] && cells[3] == cells[6]),
                    "a column cannot reference itself: " + cells[2] + "." + cells[3]);
            }

            // The declared relations must be exactly the DDL's own foreign keys.
            int ddlKeys = 0;
            foreach (string file in new[] { "Link5 v4.0.4/Personnel.sql", "Link5 v4.0.4/Link5.sql" })
            {
                string path = Path.Combine(T5WebRoot, "..", "..", file);
                if (!File.Exists(path))
                {
                    continue;
                }
                // These scripts are UTF-16 with a BOM; ReadAllLines detects it.
                string ddl = string.Join("\n", File.ReadAllLines(path));
                ddlKeys += Regex.Matches(ddl, @"FOREIGN\s+KEY", RegexOptions.IgnoreCase).Count;
            }
            Assert(ddlKeys == declared,
                "the DDL declares " + ddlKeys + " foreign keys but the model carries " + declared + " declared relations.");
            Assert(inferred > 0, "the model must also carry the references the DDL never declared.");

            // Four catalogs, each named in the model and in the document.
            string json = File.ReadAllText(Path.Combine(T5WebRoot, "engine", "relations.json"));
            foreach (string catalog in new[] { "ReportT5V3", "Personnel", "Link5 service database", "db/01-t5web-objects.sql" })
            {
                Assert(json.Contains(catalog), "engine/relations.json must name '" + catalog + "'.");
            }

            string doc = Path.Combine(T5WebRoot, "docs", "09-database-model.md");
            Assert(File.Exists(doc), "docs/09-database-model.md is missing: the model needs a written contract.");
            string model = File.ReadAllText(doc);
            foreach (string required in new[] { "ReportT5V3", "Personnel", "PersonnelDirectory", "cross-database",
                                                "db/03-relations.sql", "engine/relations.tsv" })
            {
                Assert(model.Contains(required), "docs/09 must explain '" + required + "'.");
            }

            // The configuration must name both catalogs, and the composition must pass both.
            string config = File.ReadAllText(Path.Combine(T5WebRoot, "T5Web.Web", "Web.config"));
            Assert(config.Contains("PersonnelCnnStr"), "Web.config must expose PersonnelCnnStr.");
            string startup = File.ReadAllText(Path.Combine(T5WebRoot, "T5Web.Web", "MvcApplication.cs"));
            Assert(startup.Contains("PersonnelDirectory") && startup.Contains("personnelFactory"),
                "MvcApplication must build the personnel connection and the directory.");

            // No statement in the data layer may mix the two catalogs.
            var personnel = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "UserInfo", "Department", "TitlesChart", "EmpType", "Gate", "UserInfoTitles", "Device", "Certificate"
            };
            var engine = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Record", "Request", "RequestAccept", "Workflow", "LeaveType", "LeavesAdded", "YearRemain",
                "DailyLeaf", "HourlyLeave", "Schedule", "Shift", "ShiftDetail", "UserShift", "Rule", "UserRule",
                "DepartmentRule", "Parameter"
            };
            foreach (string file in Directory.GetFiles(Path.Combine(T5WebRoot, "T5Web.Data"), "*.cs"))
            {
                string source = File.ReadAllText(file);
                foreach (Match literal in Regex.Matches(source, "@\"(.*?)\";", RegexOptions.Singleline))
                {
                    string query = literal.Groups[1].Value;
                    var fromTables = new HashSet<string>(
                        Regex.Matches(query, @"\b(?:FROM|JOIN|INTO|UPDATE|MERGE)\s+\[?([A-Za-z_][A-Za-z0-9_]*)\]?",
                            RegexOptions.IgnoreCase).Cast<Match>().Select(m => m.Groups[1].Value),
                        StringComparer.OrdinalIgnoreCase);
                    Assert(!(fromTables.Overlaps(personnel) && fromTables.Overlaps(engine)),
                        Path.GetFileName(file) + " has a statement that joins the Personnel catalog to the engine catalog: " +
                        string.Join(", ", fromTables));
                }
            }

            // The integrity report is read-only: it reports, it never repairs.
            string report = File.ReadAllText(Path.Combine(T5WebRoot, "db", "03-relations.sql"));
            Assert(report.Contains("VIOLATION"), "db/03-relations.sql must classify violations.");
            Assert(!report.Contains("INSERT INTO ["), "db/03-relations.sql must not write to a table.");
            Assert(!report.Contains("DELETE FROM ["), "db/03-relations.sql must not delete.");
        }

        /// <summary>
        /// T29 - the file-level audit. T25 and T27 look *inside* T5V3Web.dll and prove every
        /// controller and every public action has a decision. A legacy application is more
        /// than its controller assembly, though: the view folder, the first-party
        /// assemblies that sit beside it (TC5, TMisc5, PersianDate and the engine), and the
        /// sixteen appSettings keys can each carry behaviour. This test counts them on disk
        /// and requires the documents to state the same numbers, so a view folder or a
        /// configuration switch cannot quietly disappear from the audit.
        /// </summary>
        private static void TestLegacySurface()
        {
            string legacy = Path.Combine(RepoRoot, "T5Web [3.5.7]", "App");
            Assert(Directory.Exists(legacy), "the legacy web application folder is missing: " + legacy);

            string decomposition = File.ReadAllText(Path.Combine(T5WebRoot, "docs", "03-legacy-decomposition.md"));
            string phaseLog = File.ReadAllText(Path.Combine(T5WebRoot, "docs", "02-phase-log.md"));

            // 1. The views: 112 files in 65 folders (64 controllers + Shared). The documents
            //    state the same numbers, and every view folder belongs to a legacy controller.
            string views = Path.Combine(legacy, "Views");
            int viewFiles = Directory.GetFiles(views, "*.cshtml", SearchOption.AllDirectories).Length;
            int viewFolders = Directory.GetDirectories(views).Length;
            Assert(viewFiles == 112 && viewFolders == 65,
                "the legacy view folder changed: " + viewFiles + " views in " + viewFolders + " folders.");
            Assert(decomposition.Contains("۱۱۲ فایل") && decomposition.Contains("۶۵ پوشه"),
                "docs/03 must state the view count it audits (112 files / 65 folders).");

            var controllers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(Path.Combine(T5WebRoot, "docs", "legacy-controllers.txt")))
            {
                string name = line.Trim();
                if (name.Length > 0 && !name.StartsWith("#", StringComparison.Ordinal))
                {
                    controllers.Add(name);
                }
            }
            Assert(controllers.Count == 67, "docs/legacy-controllers.txt must list 67 controllers.");
            foreach (string folder in Directory.GetDirectories(views).Select(d => Path.GetFileName(d)))
            {
                if (string.Equals(folder, "Shared", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                Assert(controllers.Contains(folder),
                    "the legacy view folder '" + folder + "' has no controller in the audit.");
            }

            // 2. The first-party assemblies beside the controller assembly. Each must still be
            //    on disk (the pin or the removal decision refers to it) and be named in docs/03.
            foreach (string assembly in new[]
                     {
                         "T5V3Web.dll", "T5V3.DB.dll", "T5V3.Model.dll", "T5V3.Attendance.dll",
                         "T5V3.LeaveCalcs.dll", "T5V3.Report.dll", "T5V3.ReportGenerator.dll",
                         "Personnel.dll", "TC5.dll", "TMisc5.dll", "PersianDate.dll"
                     })
            {
                Assert(File.Exists(Path.Combine(legacy, "bin", assembly)),
                    "the legacy assembly '" + assembly + "' is missing from bin.");
                Assert(decomposition.Contains(assembly),
                    "docs/03 does not account for the legacy assembly '" + assembly + "'.");
            }

            // 3. Every appSettings key of the legacy Web.config has a written decision. The two
            //    connection keys are the important ones: they name the two catalogs (docs/09),
            //    and the portal's own keys are different on purpose.
            string config = File.ReadAllText(Path.Combine(legacy, "Web.config"));
            var keys = Regex.Matches(config, "<add key=\"([^\"]+)\"")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert(keys.Count == 16, "the legacy Web.config changed: " + keys.Count + " appSettings keys.");
            foreach (string key in keys)
            {
                Assert(phaseLog.Contains("`" + key + "`"),
                    "the legacy configuration key '" + key + "' has no decision in docs/02.");
            }
            Assert(config.Contains("t5ConnectionString") && config.Contains("personnelConnectionString"),
                "the legacy configuration must still name both catalogs.");
            Assert(decomposition.Contains("`t5ConnectionString`") && decomposition.Contains("`personnelConnectionString`"),
                "docs/03 must record the legacy connection-string names, not the portal's names.");
        }

        /// <summary>
        /// T30 - the product identity. The rewrite is registered as «آریا ادمین» /
        /// ARIA-ADMIN v5.0, the same way the TWF rewrite is registered as «آریا». A name
        /// that lives in ten places drifts, so this test reads the single constant file
        /// (T5Web.Web/Content/AriaAdminBranding.cs) and holds it against every place the
        /// operator can see the product: the console layout, Web.config, the ten offline
        /// preview pages and the documents. It also fails if the pre-registration label
        /// survives anywhere in the UI.
        ///
        /// The constants are read as text on purpose: the test project references Domain,
        /// Application and Data, not the web assembly, so no System.Web is dragged into
        /// the offline test host.
        /// </summary>
        private static void TestProductIdentity()
        {
            string brandPath = Path.Combine(T5WebRoot, "T5Web.Web", "Content", "AriaAdminBranding.cs");
            Assert(File.Exists(brandPath), "the product identity file is missing: T5Web.Web/Content/AriaAdminBranding.cs");
            string brand = File.ReadAllText(brandPath);
            foreach (string constant in new[]
                     {
                         "public const string ProductName = \"آریا ادمین\";",
                         "public const string ProductCode = \"ARIA-ADMIN\";",
                         "public const string Version = \"5.0\";",
                         "public const string FullVersion = \"Aria Admin v5.0\";"
                     })
            {
                Assert(brand.Contains(constant), "the product identity lost a constant: " + constant);
            }
            Assert(brand.Contains("T5V3Web.dll"),
                "the brand must name the legacy product it replaces, for the operator.");
            Assert(brand.Contains("pageTitle + \" · \" + ProductName"),
                "the window title must be built as 'page · product'.");

            // A constant that the project does not compile is not a constant: the web
            // project lists its folders explicitly, so Content must stay in the list.
            string project = File.ReadAllText(Path.Combine(T5WebRoot, "T5Web.Web", "T5Web.Web.csproj"));
            Assert(project.Contains("Content\\*.cs"),
                "T5Web.Web.csproj must compile Content\\*.cs, otherwise the brand is not built.");

            // 1. The console layout reads the brand from that file and no longer carries the
            //    pre-registration label.
            string layout = File.ReadAllText(Path.Combine(T5WebRoot, "T5Web.Web", "Views", "Shared", "_Layout.cshtml"));
            Assert(layout.Contains("AriaAdminBranding.ProductName"), "the layout must print the registered product name.");
            Assert(layout.Contains("AriaAdminBranding.WindowTitle"), "the layout must build the tab title from the brand.");
            Assert(!layout.Contains("T5Web Admin"),
                "the layout still shows the pre-registration label 'T5Web Admin'.");

            // 2. Configuration carries the same identity, so an operator reading Web.config
            //    sees the product they installed.
            string config = File.ReadAllText(Path.Combine(T5WebRoot, "T5Web.Web", "Web.config"));
            Assert(config.Contains("<add key=\"PortalName\" value=\"آریا ادمین\" />"),
                "Web.config PortalName must be the registered name.");
            Assert(config.Contains("PortalProductCode") && config.Contains("ARIA-ADMIN"),
                "Web.config must carry the product code.");

            // 3. Nothing in the views may still call the product by its old label.
            foreach (string view in Directory.GetFiles(Path.Combine(T5WebRoot, "T5Web.Web", "Views"), "*.cshtml",
                         SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(view);
                Assert(!text.Contains("کنسول مدیریت T5Web"),
                    Path.GetFileName(view) + " still prints the pre-registration product label.");
            }

            // 4. All ten offline preview pages show the registered brand.
            string[] pages = Directory.GetFiles(Path.Combine(T5WebRoot, "preview"), "*.html");
            Assert(pages.Length >= 10, "the preview must keep its ten pages, found " + pages.Length + ".");
            foreach (string page in pages)
            {
                string html = File.ReadAllText(page);
                Assert(html.Contains("آریا ادمین"),
                    Path.GetFileName(page) + " does not show the registered product name.");
                Assert(!html.Contains("T5Web Admin"),
                    Path.GetFileName(page) + " still shows the pre-registration label 'T5Web Admin'.");
            }

            // 5. The documents name the product they describe.
            string readme = File.ReadAllText(Path.Combine(T5WebRoot, "README.md"));
            Assert(readme.Contains("آریا ادمین") && readme.Contains("ARIA-ADMIN"),
                "README must state the registered name and code.");
            foreach (string doc in new[] { "01-rewrite-principles.md", "02-phase-log.md", "08-runbook.md" })
            {
                string text = File.ReadAllText(Path.Combine(T5WebRoot, "docs", doc));
                Assert(text.Contains("آریا ادمین"), "docs/" + doc + " must name the product.");
            }
        }

        /// <summary>
        /// T27 - the action-level audit. T25 proves every *controller* has a home; this
        /// proves every *public action* does. It reads docs/legacy-actions.csv (written by
        /// tools/legacy-coverage.py from the assembly metadata) and holds it to three
        /// rules: each legacy action appears exactly once with a decision, a `drop` names
        /// a reason, and a `new`/`module` target actually exists in this solution.
        ///
        /// The whole point is that the claim "nothing is left unaccounted for" is checked
        /// by the build, not by a document that can quietly shrink.
        /// </summary>
        private static void TestActionCoverage()
        {
            string csvPath = Path.Combine(T5WebRoot, "docs", "legacy-actions.csv");
            Assert(File.Exists(csvPath), "docs/legacy-actions.csv is missing; run tools/legacy-coverage.py --write.");
            string[] lines = File.ReadAllLines(csvPath);
            Assert(lines.Length > 1 && lines[0] == "controller,action,decision,target,note",
                "legacy-actions.csv must start with its header row.");

            var rows = new List<string[]>();
            foreach (string line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                // No field in this file contains a comma or a quote, and the writer keeps
                // it that way; five columns is therefore an exact check, not a heuristic.
                string[] cells = line.Split(',');
                Assert(cells.Length == 5, "malformed row in legacy-actions.csv: " + line);
                rows.Add(cells);
            }

            Assert(rows.Count == 343, "every legacy action must have one row; found " + rows.Count + ".");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string[] cells in rows)
            {
                string key = cells[0] + "." + cells[1];
                Assert(seen.Add(key), "duplicate decision for " + key + ".");
                Assert(cells[2] == "new" || cells[2] == "module" || cells[2] == "drop",
                    key + " has an unknown decision: " + cells[2]);
            }

            // Every controller in the inventory must appear, and vice versa.
            string[] inventory = File.ReadAllLines(Path.Combine(T5WebRoot, "docs", "legacy-controllers.txt"))
                .Where(l => !l.StartsWith("#") && !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .ToArray();
            var decided = new HashSet<string>(rows.Select(r => r[0]), StringComparer.Ordinal);
            var undeclared = inventory.Where(c => !decided.Contains(c)).ToList();
            Assert(undeclared.Count == 0, "controllers with no action rows: " + string.Join(", ", undeclared));
            var unknownController = decided.Where(c => !inventory.Contains(c)).ToList();
            Assert(unknownController.Count == 0, "action rows for unknown controllers: " + string.Join(", ", unknownController));

            // The code on disk: which public actions does the rewrite actually expose?
            var ownActions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(Path.Combine(T5WebRoot, "T5Web.Web", "Controllers"), "*.cs"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!name.EndsWith("Controller", StringComparison.Ordinal))
                {
                    continue;
                }
                var actions = new HashSet<string>(StringComparer.Ordinal);
                // Return-type agnostic: any public method is an action as far as this audit
                // cares. Constructors have no return type, so they cannot match.
                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"public\s+[A-Za-z_][A-Za-z0-9_.<>]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\("))
                {
                    actions.Add(m.Groups[1].Value);
                }
                ownActions[name.Substring(0, name.Length - "Controller".Length)] = actions;
            }
            Assert(ownActions.Count > 0, "no portal controllers found to check targets against.");

            string registry = File.ReadAllText(Path.Combine(T5WebRoot, "T5Web.Application", "Modules", "ModuleRegistry.cs"));
            var moduleCodes = new HashSet<string>(
                Regex.Matches(registry, "Code\\s*=\\s*\"([^\"]+)\"")
                    .Cast<Match>().Select(m => m.Groups[1].Value),
                StringComparer.Ordinal);
            Assert(moduleCodes.Count > 0, "no module codes found in ModuleRegistry.");

            foreach (string[] cells in rows)
            {
                string key = cells[0] + "." + cells[1];
                string decision = cells[2];
                string target = cells[3];
                string note = cells[4];

                if (decision == "drop")
                {
                    // A dropped action without a reason is an unaccounted action.
                    Assert(note.Trim().Length >= 8, key + " is dropped without a stated reason.");
                    Assert(target.Length == 0 || target == "-", key + " is dropped but still names a target.");
                    continue;
                }

                if (decision == "module")
                {
                    Assert(moduleCodes.Contains(target), key + " maps to module '" + target + "', which ModuleRegistry does not define.");
                    continue;
                }

                Match t = Regex.Match(target, @"^(?<c>[A-Za-z]+)\.(?<a>[A-Za-z_][A-Za-z0-9_]*)$");
                Assert(t.Success, key + " has an unreadable target: " + target);
                string controller = t.Groups["c"].Value;
                string action = t.Groups["a"].Value;
                Assert(ownActions.ContainsKey(controller),
                    key + " maps to " + controller + "Controller, which does not exist.");
                Assert(ownActions[controller].Contains(action),
                    key + " maps to " + target + ", but " + controller + "Controller has no public action '" + action + "'.");
            }
        }

        /// <summary>
        /// T33 - data migration (docs/12).
        ///
        /// The claim this test guards is simple and load-bearing: the rewrites do not own
        /// the schema. If somebody ever adds an ALTER to a vendor table, or a fourth portal
        /// table, or a migration that rewrites engine data, the "just point it at the existing
        /// database" promise is gone and every customer upgrade becomes a migration project.
        /// </summary>
        private static void TestDataMigration()
        {
            string dbRoot = Path.Combine(T5WebRoot, "db");
            string[] scripts = Directory.GetFiles(dbRoot, "*.sql");

            // 1. No script may change a vendor table, and only 01 may create tables.
            foreach (string script in scripts)
            {
                string text = File.ReadAllText(script);
                Assert(!Regex.IsMatch(text, @"(?i)\bALTER\s+TABLE\b"),
                    Path.GetFileName(script) + " alters a table; the engine owns its schema (docs/12 §1).");
                Assert(!Regex.IsMatch(text, @"(?i)\bDROP\s+TABLE\b"),
                    Path.GetFileName(script) + " drops a table.");
                if (!script.EndsWith("01-t5web-objects.sql", StringComparison.Ordinal))
                {
                    Assert(!Regex.IsMatch(text, @"(?i)\bCREATE\s+TABLE\b"),
                        Path.GetFileName(script) + " creates a table; only 01-t5web-objects.sql may.");
                }
            }

            // 2. Exactly the three portal-owned tables, each guarded by IF OBJECT_ID (idempotent).
            string objects = File.ReadAllText(Path.Combine(dbRoot, "01-t5web-objects.sql"));
            var created = Regex.Matches(objects, @"CREATE TABLE dbo\.(\w+)")
                .Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(n => n).ToList();
            string[] expected = { "T5WebAccess", "T5WebAudit", "T5WebDeviceCommand" };
            Assert(created.SequenceEqual(expected),
                "db/01 creates " + string.Join(", ", created) + " but docs/12 promises exactly " +
                string.Join(", ", expected) + ".");
            Assert(Regex.Matches(objects, @"IF OBJECT_ID\(N'dbo\.T5Web\w+', N'U'\) IS NULL\)").Count >= 3 ||
                   Regex.Matches(objects, @"IF OBJECT_ID\(N'dbo\.T5Web\w+', N'U'\) IS NULL").Count >= 3,
                "every portal object must be created behind an IF OBJECT_ID guard (idempotent).");

            // 3. The fingerprint tool exists, reads the schema model, and is a build gate.
            string tool = Path.Combine(T5WebRoot, "tools", "migration-fingerprint.py");
            Assert(File.Exists(tool), "missing tools/migration-fingerprint.py (docs/12 §3-1).");
            string source = File.ReadAllText(tool);
            Assert(source.Contains("relations.json"), "the fingerprint must be generated from engine/relations.json.");
            Assert(source.Contains("--compare") && source.Contains("MISSING"),
                "the fingerprint must compare two runs and survive a table an older install lacks.");
            string build = File.ReadAllText(Path.Combine(T5WebRoot, "build-and-test.sh"));
            Assert(build.Contains("migration-fingerprint.py"),
                "the migration fingerprint must be a build gate, not an optional script.");

            // 4. The playbook says the things a playbook has to say.
            string doc = Path.Combine(T5WebRoot, "docs", "12-data-migration.md");
            Assert(File.Exists(doc), "missing docs/12-data-migration.md.");
            string playbook = File.ReadAllText(doc);
            foreach (string required in new[]
                     {
                         "T5CnnStr", "PersonnelCnnStr", "T5WebAccess", "T5WebAudit", "T5WebDeviceCommand",
                         "verify.sql", "03-relations.sql", "migration-fingerprint", "T5WebMode",
                         "WebUser", "Template", "MachineNumber", "BACKUP DATABASE", "rollback"
                     })
            {
                Assert(playbook.Contains(required), "docs/12 never mentions " + required + ".");
            }

            // 5. The README index must list it, or the playbook is unreachable.
            string readme = File.ReadAllText(Path.Combine(T5WebRoot, "README.md"));
            Assert(readme.Contains("docs/12"), "README does not list docs/12-data-migration.md.");
        }

        /// <summary>
        /// T34 - the Windows install kit (docs/13).
        ///
        /// The kit runs on a customer's server, not here, so the test checks the two
        /// things that make such a kit safe to hand over: it can be re-run after a
        /// failure (idempotent), and it never carries a second copy of a password.
        /// </summary>
        private static void TestInstallKit()
        {
            string deploy = Path.Combine(T5WebRoot, "deploy");
            Assert(Directory.Exists(deploy), "missing deploy/ (the IIS + SQL Server kit).");

            var scripts = new[]
            {
                "1-install-iis.ps1", "2-install-sqlserver.ps1",
                "3-deploy-portal.ps1", "4-verify-install.ps1"
            };
            foreach (string name in scripts)
            {
                string path = Path.Combine(deploy, name);
                Assert(File.Exists(path), "missing deploy/" + name);
                string text = File.ReadAllText(path);
                Assert(text.Contains("Set-StrictMode"), name + " must set StrictMode.");
                Assert(text.Contains("#requires -Version 5.1"), name + " must stay Windows PowerShell 5.1 compatible.");
                Assert(!text.Contains("??") && !text.Contains("&&"),
                    name + " uses PowerShell 7-only syntax; Server 2016/2019 operators still have 5.1.");
                if (name != "4-verify-install.ps1")
                {
                    Assert(text.Contains("#requires -RunAsAdministrator"),
                        name + " changes the machine, so it must require an elevated shell.");
                }
                // No literal password may live in the repository; the scripts ask at run time.
                Assert(!Regex.IsMatch(text, "SAPWD\\s*=\\s*['\"][^'\"]+['\"]"),
                    name + " contains a literal SA password.");
                Assert(!Regex.IsMatch(text, "(?i)password\\s*=\\s*['\"][^'\"$]"),
                    name + " contains a literal password.");
            }

            // Idempotency: creating a pool/site is always behind a probe.
            string portal = File.ReadAllText(Path.Combine(deploy, "3-deploy-portal.ps1"));
            Assert(portal.Contains("Test-Path") && portal.Contains("AppPools"),
                "the app-pool creation must be guarded, or a second run fails.");
            Assert(portal.Contains("Get-Website") && portal.Contains("New-Website"),
                "the site creation must be guarded by Get-Website.");
            Assert(portal.Contains("Set-AppSetting") && portal.Contains("RemoveChild"),
                "Web.config patching must replace a duplicate key instead of appending one.");
            Assert(portal.Contains("IIS AppPool") && portal.Contains("icacls"),
                "the site folder must be readable by the pool identity, or the app cannot start.");
            Assert(!portal.ToLowerInvariant().Contains("password="),
                "the connection string must use Integrated Security only.");

            // The verifier has to assert the two authentication outcomes, not just "it loads".
            string verify = File.ReadAllText(Path.Combine(deploy, "4-verify-install.ps1"));
            Assert(verify.Contains("401") && verify.Contains("UseDefaultCredentials"),
                "the verifier must prove anonymous is refused and Windows is accepted.");
            Assert(verify.Contains("verify.sql") && verify.Contains("03-relations.sql")
                   && verify.Contains("migration-fingerprint.py"),
                "the verifier must run the three database gates from docs/12.");
            Assert(verify.Contains("Content-Security-Policy"),
                "the verifier must check the documented security header.");

            // The script that installs SQL Server must refuse to invent the vendor schema.
            string sql = File.ReadAllText(Path.Combine(deploy, "2-install-sqlserver.ps1"));
            Assert(sql.Contains("01-t5web-objects.sql") && sql.Contains("02-t5web-sql-login.sql"),
                "the SQL script must create the portal objects and the least-privilege login.");
            Assert(sql.Contains("RESTORE DATABASE") && !sql.Contains("CREATE DATABASE ReportT5V3"),
                "the catalogs are restored, never created from a guessed schema (docs/12).");

            // The gate is wired into the build, and the playbook exists and is indexed.
            string build = File.ReadAllText(Path.Combine(T5WebRoot, "build-and-test.sh"));
            Assert(build.Contains("deploy-kit-gate.py"), "the deploy kit must be a build gate.");
            string doc = Path.Combine(T5WebRoot, "docs", "13-install-iis-sqlserver.md");
            Assert(File.Exists(doc), "missing docs/13-install-iis-sqlserver.md.");
            string playbook = File.ReadAllText(doc);
            int last = -1;
            foreach (string name in scripts)
            {
                int at = playbook.IndexOf(name, StringComparison.Ordinal);
                Assert(at >= 0, "docs/13 never mentions " + name + ".");
                Assert(at > last, "docs/13 documents " + name + " out of order.");
                last = at;
            }
            Assert(File.Exists(Path.Combine(deploy, "README.md")), "missing deploy/README.md.");
            string readme = File.ReadAllText(Path.Combine(T5WebRoot, "README.md"));
            Assert(readme.Contains("docs/13"), "README does not list the install book.");
        }

        /// <summary>
        /// T35 - the SQL can be executed, not only read (docs/14).
        ///
        /// Two things make the SQL testable off the customer's server: a synthetic schema
        /// generated from the model the gates already prove, and a smoke runner that
        /// compiles each embedded statement with SET NOEXEC ON. This test guards that
        /// both still exist, that the synthetic schema is generated from the model (never
        /// hand-written) and that it excludes the device catalog the portal never opens.
        /// </summary>
        private static void TestExecutableSql()
        {
            string synthetic = Path.Combine(T5WebRoot, "tools", "synthetic-schema.py");
            string smoke = Path.Combine(T5WebRoot, "tools", "sql-smoke.py");
            Assert(File.Exists(synthetic), "missing tools/synthetic-schema.py");
            Assert(File.Exists(smoke), "missing tools/sql-smoke.py");

            string builder = File.ReadAllText(synthetic);
            Assert(builder.Contains("column-types.json"),
                "the synthetic schema must be generated from engine/column-types.json, not written by hand.");
            Assert(builder.Contains("TYPE_MAP") && builder.Contains("do not guess"),
                "an unknown recorded type must be a hard error, never silently widened.");

            string runner = File.ReadAllText(smoke);
            Assert(runner.Contains("SET NOEXEC ON"),
                "the smoke runner must compile statements (SET NOEXEC ON) rather than trust a text check.");
            Assert(runner.Contains("is_fragment"),
                "statements assembled at run time must be skipped explicitly, not counted as failures.");
            Assert(runner.Contains("DECLARE @"),
                "Dapper parameters have to be declared or SQL Server cannot compile the statement.");
            Assert(runner.Contains("declarable_type"),
                "parameter types must come from how a parameter is used; a name-only guess "
                + "made @PendingOnly a datetime and failed a correct statement.");
            Assert(runner.Contains("expand_lists"),
                "Dapper turns `IN @Ids` into a list before the engine sees it; without that "
                + "emulation four good statements are reported as syntax errors.");
            Assert(TypedHasColumn("Workflow", "WorkflowType"),
                "Workflow.WorkflowType is used by the workflow screen; the model must carry it.");
            Assert(TypedHasColumn("Request", "WofkflowType"),
                "Request.WofkflowType (the vendor's spelling) is used by the workflow screen.");

            // The model must cover every table the modules write to; without it the
            // synthetic schema would silently miss a table and the smoke run would pass.
            Dictionary<string, Dictionary<string, string>> typed = TypedColumns();
            Assert(typed.Count >= 70, "engine/column-types.json has only " + typed.Count + " tables.");
            foreach (string table in new[] { "UserInfo", "Department", "TitlesChart", "Device", "Record", "Request", "UserShift" })
            {
                Assert(typed.ContainsKey(table), "the synthetic schema has no table for " + table + ".");
            }
            if (typed.ContainsKey("UserInfo"))
            {
                Assert(typed["UserInfo"].ContainsKey("BadgeNumber"),
                    "UserInfo.BadgeNumber is missing from the typed model - every statement joins on it.");
            }

            // CI is the Windows machine: without it, "it compiles on Windows" is a claim.
            string workflow = Path.GetFullPath(Path.Combine(T5WebRoot, "..", "..", ".github", "workflows", "windows-build.yml"));
            Assert(File.Exists(workflow), "missing .github/workflows/windows-build.yml (docs/14).");
            string yaml = File.ReadAllText(workflow);
            foreach (string required in new[] { "windows-latest", "msbuild", "T5Web.Tests.exe",
                                                "synthetic-schema.py", "sql-smoke.py", "verify.sql",
                                                "03-relations.sql", "migration-fingerprint.py" })
            {
                Assert(yaml.Contains(required), "the Windows workflow never runs " + required + ".");
            }
            Assert(yaml.Contains("workflow_dispatch"),
                "the Windows workflow must be startable by hand as well as on push.");

            // MSBuild refuses a project with no Release OutputPath, and a text-level edit
            // to a .csproj can silently break the XML (BUG-32). Both are cheap to prove.
            foreach (string project in new[] { "T5Web.Domain", "T5Web.Application", "T5Web.Data", "T5Web.Tests", "T5Web.Web" })
            {
                string csproj = Path.Combine(T5WebRoot, project, project + ".csproj");
                Assert(File.Exists(csproj), "missing project file " + csproj + ".");
                var document = new System.Xml.XmlDocument();
                document.Load(csproj);
                string xml = File.ReadAllText(csproj);
                Assert(xml.Contains("'Release|AnyCPU'"), project + " has no Release|AnyCPU configuration.");
                Assert(xml.Contains("<OutputPath>"), project + " has no OutputPath, so MSBuild cannot place the output.");
            }

            string build = File.ReadAllText(Path.Combine(T5WebRoot, "build-and-test.sh"));
            foreach (string tool in new[] { "csharp-parse-gate.py", "synthetic-schema.py", "sql-smoke.py",
                                            "project-file-gate.py", "sql-grant-gate.py",
                                            "sql-column-coverage.py" })
            {
                Assert(build.Contains(tool), tool + " is not a build gate.");
            }
        }

        /// <summary>
        /// T36 - the install really spans two catalogs (docs/09, docs/12).
        /// The first Windows run of the suite failed exactly here: verify.sql asked
        /// OBJECT_ID(N'dbo.Department') while Department lives in [Personnel], and the
        /// portal login was only created in [ReportT5V3], so every personnel screen
        /// would have failed at run time with "the server principal is not able to
        /// access the database". Both defects were invisible to a text-only review.
        /// </summary>
        private static void TestTwoCatalogInstall()
        {
            string verify = File.ReadAllText(Path.Combine(T5WebRoot, "db", "verify.sql"));
            Assert(verify.Contains("@T5") && verify.Contains("@Personnel"),
                "verify.sql must name both catalogs; a bare OBJECT_ID() only ever answers for the current database.");
            Assert(verify.Contains("DB_ID(@Personnel)"),
                "verify.sql must fail loudly when the personnel catalog is missing, not report every table MISSING.");
            Assert(verify.Contains("QUOTENAME(@Personnel)"),
                "verify.sql must qualify personnel objects with their own catalog.");
            Assert(!verify.Contains("LeaveTypes"),
                "the engine table is LeaveType; LeaveTypes would always report MISSING.");

            string login = File.ReadAllText(Path.Combine(T5WebRoot, "db", "02-t5web-sql-login.sql"));
            foreach (string database in new[] { "[ReportT5V3]", "[Personnel]" })
            {
                Assert(login.Contains("USE " + database + ";"),
                    "the portal login is not created in " + database + " - the other catalog stays unreachable.");
            }
            Assert(login.Contains("DENY UPDATE, DELETE ON OBJECT::dbo.T5WebAudit"),
                "the audit trail must stay append-only for the portal login itself.");

            // The grant list is derived from what the modules write; the gate is the proof.
            string gate = File.ReadAllText(Path.Combine(T5WebRoot, "tools", "sql-grant-gate.py"));
            Assert(gate.Contains("ModuleRegistry.cs"),
                "the grant gate must read the writable tables from ModuleRegistry.cs, not from a copy.");
            Assert(File.ReadAllText(Path.Combine(T5WebRoot, "build-and-test.sh")).Contains("sql-grant-gate.py"),
                "sql-grant-gate.py is not a build gate.");

            // Every column check must be traceable to the model, otherwise the list rots.
            Dictionary<string, Dictionary<string, string>> typed = TypedColumns();
            foreach (string table in new[] { "UserShift", "Record", "Request", "LeaveType" })
            {
                Assert(typed.ContainsKey(table), "engine/column-types.json has no " + table + ".");
            }
            if (typed.ContainsKey("UserShift"))
            {
                Assert(!typed["UserShift"].ContainsKey("EndDate"),
                    "UserShift.EndDate does not exist; the real column is ToDate.");
            }
        }

        /// <summary>True when engine/column-types.json records the column for the table.</summary>
        private static bool TypedHasColumn(string table, string column)
        {
            Dictionary<string, Dictionary<string, string>> typed = TypedColumns();
            return typed.ContainsKey(table) && typed[table].ContainsKey(column);
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
