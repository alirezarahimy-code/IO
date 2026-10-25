using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using T5Web.Application.Services;
using T5Web.Data;
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
                            token.StartsWith("move-form", StringComparison.Ordinal) || token == "is-active")
                        {
                            used.Add(token);
                        }
                    }
                }
            }
            var missing = used.Where(u => !defined.Contains(u)).OrderBy(u => u).ToList();
            Assert(missing.Count == 0, "unstyled classes: " + string.Join(", ", missing));
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

            var expectedPhases = new[] { "A", "B", "C", "D", "E", "F" };
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

            // 4. Every phase in the tracker has a section in the plan document.
            string plan = File.ReadAllText(Path.Combine(docs, "06-phase-plan.md"));
            foreach (string phase in expectedPhases)
            {
                Assert(plan.Contains("فاز " + phase), "docs/06-phase-plan.md has no section for phase " + phase + ".");
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
