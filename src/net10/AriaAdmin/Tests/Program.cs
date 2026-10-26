using System.Globalization;
using System.Text;
using System.Text.Json;
using T5Web.Application.Services;
using T5Web.Domain;
using T5Web.Domain.Models;

namespace AriaAdmin.Core.Tests;

// No runner/NuGet/SQL/real users. These *same-source* synthetic assertions cover
// lower-layer behaviour only; they are not original-web parity or authorization HTTP.
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fa-IR");
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "golden.json");
        using var golden = JsonDocument.Parse(File.ReadAllText(fixturePath, Encoding.UTF8));
        JsonElement expected = golden.RootElement;
        Require(expected.GetProperty("fixture").GetString() == "aria-admin-core-synthetic-01", "Wrong golden fixture");

        Run("AA-CORE-01: Jalali calendar from rewrite T06", () =>
        {
            Equal(expected.GetProperty("jalaliNowruz").GetString(),
                PersonnelService.ToJalali(new DateTime(2024, 3, 20)), "Nowruz");
            Equal(expected.GetProperty("jalaliAutumn").GetString(),
                PersonnelService.ToJalali(new DateTime(2026, 10, 25)), "Autumn");
            Equal(expected.GetProperty("emptyDate").GetString(), PersonnelService.ToJalali(null), "empty date");
        });
        Run("AA-CORE-02: Persian/Arabic text and whitespace from rewrite T06", () =>
            Equal(expected.GetProperty("normalizedName").GetString(),
                PersonnelService.NormalizeDisplay("  علي   كاظمي "), "Persian normalization"));

        Run("AA-CORE-03: organization forest from rewrite T01", () =>
        {
            var service = new DepartmentService();
            var flat = SyntheticDepartments();
            var roots = service.BuildTree(flat);
            Require(roots.Count == expected.GetProperty("departmentRoots").GetInt32(), "orphan/root count");
            var realRoot = roots.Single(x => x.DepartmentId == 10);
            Require(realRoot.Children.Single().DepartmentId == 11, "child under original parent");
            Require(realRoot.Children.Single().Depth == 2, "child depth");
            Require(roots.Single(x => x.DepartmentId == 12).Children.Count == 0, "orphan remains visible");
            Require(service.MaxDepth(roots) == expected.GetProperty("departmentMaxDepth").GetInt32(), "maximum depth");
            Require(service.TotalMembers(roots) == 6, "member total for fixed synthetic seed");
        });
        Run("AA-CORE-04: cycle guard from rewrite T02", () =>
        {
            var service = new DepartmentService();
            var flat = SyntheticDepartments();
            Require(service.WouldCreateCycle(flat, 10, 10), "self-parent must fail");
            Require(service.WouldCreateCycle(flat, 10, 11), "move parent under child must fail");
            Require(!service.WouldCreateCycle(flat, 11, 0), "new root allowed");
            Require(!service.WouldCreateCycle(flat, 12, 10), "unrelated parent allowed");
        });
        Run("AA-CORE-05: explicit deny/read-only from rewrite T03", () =>
        {
            var session = new AdminSession { BadgeNumber = 1001, DisplayName = "کاربر آزمایشی" };
            session.Grant(AdminFeature.DeviceEdit, AccessLevel.Allow);
            session.Grant(AdminFeature.DeviceEdit, AccessLevel.Deny);
            Require((int)session.LevelOf(AdminFeature.DeviceEdit) == expected.GetProperty("permissionDenied").GetInt32(),
                "deny wins after allow");
            Require(!session.CanEdit(AdminFeature.DeviceEdit), "no edit after deny");
            session.Grant(AdminFeature.PersonnelView, AccessLevel.ReadOnly);
            Require((int)session.LevelOf(AdminFeature.PersonnelView) == expected.GetProperty("readOnlyLevel").GetInt32(),
                "read-only level");
            Require(session.CanView(AdminFeature.PersonnelView) && !session.CanEdit(AdminFeature.PersonnelView),
                "read-only must not edit");
            Require(!session.CanView(AdminFeature.DeviceView), "missing permission must deny");
        });
        Run("AA-CORE-06: feature IDs and edit-only capabilities", () =>
        {
            Require((int)AdminFeature.PortalAdmin == 90, "stable persisted capability ID");
            Require(AdminFeatures.RequiredLevelFor(AdminFeature.AccessEdit) == AccessLevel.Allow,
                "edit-only capability requires Allow");
            Require(AdminFeatures.RequiredLevelFor(AdminFeature.PersonnelView) == AccessLevel.ReadOnly,
                "read screen accepts ReadOnly");
        });
        Run("AA-CORE-07: personnel display and termination state", () =>
        {
            var person = new PersonnelRow { BadgeNumber = 1001, FirstName = "مریم", LastName = "آزمایشی" };
            Equal("مریم آزمایشی", person.FullName, "Persian full name");
            Require(!person.IsFormer, "initial employee is active");
            person.FiredDate = new DateTime(2024, 6, 15);
            Require(person.IsFormer, "fired person cannot be treated active");
        });
        Run("AA-CORE-08: no phantom device delivery", () =>
        {
            Require(DeviceTransport.Parse(null).Kind == DeviceTransportKind.None, "no transport by default");
            Require(!DeviceTransport.Parse("link5:").IsConfigured, "invalid Link5 URL must disable command");
            Require(!DeviceTransport.Parse("typo").IsConfigured, "unknown transport must disable command");
            // Do not send commands to Link5 or claim an agent exists here.
        });
        Run("AA-CORE-09: calculated report is an unresolved engine boundary", () =>
        {
            var report = ReportCatalog.Find("late-early") ?? throw new Exception("legacy report missing");
            Equal(expected.GetProperty("reportEngineMarker").GetString(), report.Source,
                "not a raw-punch calculation substitute");
            Require(ReportCatalog.Find("unknown-report") == null, "unknown report refused");
        });

        Console.WriteLine($"Summary: {_passed} Passed, {_failed} Failed (net10.0 linked Domain/Application only)");
        return _failed == 0 && _passed == 9 ? 0 : 1;
    }

    private static List<DepartmentNode> SyntheticDepartments() =>
        new()
        {
            new DepartmentNode { DepartmentId = 10, UpperDeptId = 0, DepartmentName = "مدیریت", MemberCount = 2 },
            new DepartmentNode { DepartmentId = 11, UpperDeptId = 10, DepartmentName = "واحد", MemberCount = 1 },
            new DepartmentNode { DepartmentId = 12, UpperDeptId = 999, DepartmentName = "بدون والد", MemberCount = 3 }
        };

    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("[PASS] " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("[FAIL] " + name + ": " + ex.Message); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal(string? expected, string? actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }
}
