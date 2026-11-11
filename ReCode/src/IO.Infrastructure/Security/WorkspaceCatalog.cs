using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Security;

public sealed record WorkspacePage(string Route, string Title, string Group, string Resource, string[] AnyPermissions);

public static class WorkspaceCatalog
{
    public static readonly IReadOnlyList<WorkspacePage> Pages = Array.AsReadOnly(new WorkspacePage[]
    {
        new("/login", "ورود به سامانه", "common", "Login.html", []),
        new("/workspace", "پنل من", "common", "Index.html", []),
        new("/workspace/account", "حساب و گذرواژه", "common", "Account.html", []),
        new("/workspace/employee-home", "نمای کلی من", "employee", "EmployeeHome.html", ["correction.requests.read.self", "correction.submit.self"]),
        new("/workspace/new-request", "درخواست‌های من", "employee", "MyRequests.html", ["correction.submit.self"]),
        new("/workspace/my-requests", "درخواست‌های من", "employee", "MyRequests.html", ["correction.requests.read.self", "correction.submit.self"]),
        new("/workspace/my-attendance", "ترددهای من", "employee", "MyAttendance.html", ["attendance.read.self"]),
        new("/workspace/my-schedule", "برنامهٔ من", "employee", "MySchedule.html", ["schedule.read.self"]),
        new("/workspace/my-leave", "دفتر مرخصی من", "employee", "MyLeave.html", ["leave.read.self", "leave.request.submit"]),
        new("/workspace/my-reports", "گزارش تشخیصی من", "employee", "MyReports.html", ["attendance.results.read.self"]),
        new("/workspace/delegation", "ثبت آزمایشی جانشین", "employee", "Delegation.html", ["delegation.manage.self"]),
        new("/workspace/manager-home", "نمای کلی مدیر", "manager", "ManagerHome.html", ["correction.inbox.read", "correction.decide"]),
        new("/workspace/inbox", "بررسی درخواست‌ها", "manager", "ManagerRequests.html", ["correction.inbox.read", "correction.decide", "correction.approve", "correction.reject"]),
        new("/workspace/decision-history", "بررسی درخواست‌ها", "manager", "ManagerRequests.html", ["correction.decisions.read.self", "correction.decide"]),
        new("/workspace/team-attendance", "تردد تیم", "manager", "TeamAttendance.html", ["attendance.read.team"]),
        new("/workspace/organization", "ساختار سازمانی | سامانهٔ حضور", "admin", "Organization.html", ["organization.manage", "organization.read"]),
        new("/workspace/employees", "کارکنان | سامانهٔ حضور", "admin", "Employees.html", ["organization.manage", "organization.read"]),
        new("/workspace/users", "حساب‌های کاربری", "admin", "Users.html", ["identity.manage", "identity.users.read", "identity.users.create", "identity.roles.manage"]),
        new("/workspace/roles", "نقش‌ها و مجوزها", "admin", "Roles.html", ["identity.manage", "identity.roles.read", "identity.roles.manage"]),
        new("/workspace/audit", "رویدادهای سامانه", "admin", "Audit.html", ["identity.manage", "identity.audit.read"]),
        new("/workspace/calendars", "برنامه‌ریزی | سامانهٔ حضور", "admin", "Scheduling.html", ["schedule.manage", "schedule.catalog.read"]),
        new("/workspace/shifts", "برنامه‌ریزی | سامانهٔ حضور", "admin", "Scheduling.html", ["schedule.manage", "schedule.catalog.read"]),
        new("/workspace/schedules", "برنامه‌ریزی | سامانهٔ حضور", "admin", "Scheduling.html", ["schedule.manage", "schedule.catalog.read"]),
        new("/workspace/employee-history", "برنامه‌ریزی | سامانهٔ حضور", "admin", "Scheduling.html", ["schedule.manage", "schedule.catalog.read"]),
        new("/workspace/sources", "برنامه‌ریزی | سامانهٔ حضور", "admin", "Scheduling.html", ["attendance.ingest", "attendance.sources.read", "attendance.sources.create"]),
        new("/workspace/raw-events", "تردد خام | سامانهٔ حضور", "admin", "RawEvents.html", ["attendance.ingest", "attendance.raw.read", "attendance.events.receive"]),
        new("/workspace/mappings", "نگاشت منبع | سامانهٔ حضور", "admin", "Mappings.html", ["attendance.ingest", "attendance.mappings.read", "attendance.mappings.manage"]),
        new("/workspace/conflicts", "برخوردهای تردد", "admin", "Conflicts.html", ["attendance.ingest", "attendance.conflicts.read", "attendance.conflicts.note"]),
        new("/workspace/calculations", "اجراهای محاسبه حضور", "admin", "Calculations.html", ["attendance.calculate", "calculation.read", "calculation.run"]),
        new("/workspace/rules", "قواعد حضور | سامانهٔ حضور", "admin", "Rules.html", ["attendance.calculate", "calculation.read", "calculation.rules.create"]),
        new("/workspace/periods", "دوره‌های کارکرد | پیش‌نویس", "admin", "Periods.html", ["period.preflight", "period.draft.manage", "period.diagnostic.manage"]),
        new("/workspace/settings", "تنظیمات گردش اصلاح تردد", "admin", "Settings.html", ["settings.correction.manage", "settings.correction.read"]),
        new("/workspace/workflow", "سیاست گردش اصلاح تردد", "admin", "Workflow.html", ["correction.configure"]),
        new("/workspace/leave-admin", "دفتر آزمایشی مرخصی", "admin", "LeaveAdmin.html", ["leave.manage", "leave.admin.read", "leave.entries.record", "leave.types.create", "leave.request.decide"]),
        new("/workspace/reports", "گزارش تشخیصی حضور", "admin", "Reports.html", ["period.preflight"]),
        new("/workspace/report-builder", "چیدمان گزارش تشخیصی", "admin", "ReportBuilder.html", ["period.preflight"]),
        new("/workspace/executive-home", "نمای مدیر ارشد", "executive", "ExecutiveHome.html", ["executive.dashboard.read"]),
        new("/panel", "ورود و مدیریت آزمایشی IO", "technical", "Panel.html", ["identity.manage", "identity.roles.manage"]),
        new("/workspace/help", "راهنمای سامانه", "common", "Help.html", []),
    });

    public static bool CanView(WorkspacePage page, IReadOnlySet<string> permissions) =>
        page.AnyPermissions.Length == 0 || page.AnyPermissions.Any(permissions.Contains);

    public static async Task<HashSet<string>> EffectiveAsync(Guid actor, IoDbContext db, CancellationToken ct)
    {
        var codes = await (from user in db.Users.AsNoTracking()
            join ur in db.UserRoles on user.Id equals ur.UserId
            join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where user.Id == actor && user.IsActive select p.Code).ToListAsync(ct);
        // Ordinal, exact membership. Never normalize case or trim a stored permission.
        return codes.Where(PermissionCatalog.Codes.Contains).ToHashSet(StringComparer.Ordinal);
    }
}
