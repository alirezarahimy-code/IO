using IO.Infrastructure.Scheduling;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class SchedulingServiceTests
{
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        Guid admin; Guid limited; Guid company; Guid otherCompany; Guid site; Guid employee;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var role = await db.Roles.SingleAsync(x => x.Name == "Initial administrator");
            admin = await db.UserRoles.Where(x => x.RoleId == role.Id).Select(x => x.UserId).SingleAsync();
            limited = await db.Users.Where(x => x.UserName == "web-manager").Select(x => x.Id).SingleAsync();
            company = await db.Companies.Where(x => x.Name == "Synthetic company").Select(x => x.Id).SingleAsync();
            otherCompany = await db.Companies.Where(x => x.Name == "Other synthetic company").Select(x => x.Id).SingleAsync();
            site = await db.Sites.Where(x => x.CompanyId == company).Select(x => x.Id).SingleAsync();
            employee = await db.Employees.Where(x => x.PersonnelNumber == "ORG-SYN-1").Select(x => x.Id).SingleAsync();
            var permissionId = Guid.NewGuid();
            db.Permissions.Add(new() { Id = permissionId, Code = ScheduleManagement.Manage });
            db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = permissionId });
            await db.SaveChangesAsync();
        }
        async Task<T> Execute<T>(Func<ScheduleManagement, Task<T>> operation)
        {
            await using var scope = provider.CreateAsyncScope();
            return await operation(scope.ServiceProvider.GetRequiredService<ScheduleManagement>());
        }
        async Task<bool> Reject<T>(Func<Task> action) where T : Exception
        {
            try { await action(); return false; } catch (T) { return true; }
        }
        check(await Reject<UnauthorizedAccessException>(() => Execute(s => s.CreateCalendarAsync(limited, company, "Unauthorized"))),
            "schedule write denies actor without explicit permission");
        var calendar = await Execute(s => s.CreateCalendarAsync(admin, company, "Synthetic calendar"));
        var otherCalendar = await Execute(s => s.CreateCalendarAsync(admin, otherCompany, "Foreign calendar"));
        var date = new DateOnly(2027, 1, 1);
        await Execute(s => s.AddCalendarDayAsync(admin, calendar, date, 1, "Synthetic holiday"));
        check(await Reject<InvalidOperationException>(() => Execute(s => s.AddCalendarDayAsync(admin, calendar, date, 0, null))) &&
              await Reject<ArgumentException>(() => Execute(s => s.AddCalendarDayAsync(admin, calendar, date.AddDays(1), 3, null))),
            "calendar days explicit, duplicate and invalid kinds rejected");
        var shift = await Execute(s => s.CreateShiftAsync(admin, company, site, "Synthetic night", new TimeOnly(22, 0), new TimeOnly(6, 0), 1));
        check(await Reject<ArgumentException>(() => Execute(s => s.CreateShiftAsync(admin, company, site, "Invalid night", new TimeOnly(22, 0), new TimeOnly(6, 0), 0))) &&
              await Reject<ArgumentException>(() => Execute(s => s.CreateShiftAsync(admin, otherCompany, site, "Foreign site", new TimeOnly(9, 0), new TimeOnly(17, 0), 0))),
            "fixed shift requires explicit overnight and same-company site");
        check(await Reject<ArgumentException>(() => Execute(s => s.AssignAsync(admin, employee, otherCalendar, shift, date, null))) &&
              await Reject<ArgumentException>(() => Execute(s => s.AssignAsync(admin, employee, calendar, shift, date, date))),
            "schedule rejects cross-company calendar and empty interval");
        async Task<Guid?> TryAssign()
        {
            try { return await Execute(s => s.AssignAsync(admin, employee, calendar, shift, date, null)); }
            catch (InvalidOperationException e) when (e.Message == "Overlapping work schedule assignment.") { return null; }
        }
        var attempts = await Task.WhenAll(TryAssign(), TryAssign());
        check(attempts.Count(x => x.HasValue) == 1, "concurrent schedule assignments serialize with exactly one insert");
        check(await Reject<InvalidOperationException>(() => Execute(s => s.AddCalendarDayAsync(admin, calendar, date.AddDays(1), 0, null))),
            "assigned calendar version cannot be amended through service");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>();
            var id = attempts.Single(x => x.HasValue)!.Value;
            check(await db.WorkScheduleAssignments.CountAsync(x => x.EmployeeId == employee) == 1 &&
                  await audit.SecurityAudit.AnyAsync(x => x.Action == "schedule.assignment.created:" + id.ToString("N")),
                "schedule assignment persisted with audit");
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            db.CalendarDays.Add(new() { Id = Guid.NewGuid(), CalendarId = calendar, Date = date.AddDays(2), Kind = 4, RecordedAtUtc = DateTimeOffset.UtcNow });
            var rejected = false;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException e) when (e.InnerException is SqlException sql && sql.Number == 547) { rejected = true; }
            check(rejected, "SQL rejects invalid calendar day kind");
        }
    }
}
