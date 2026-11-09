using System.Text.Json;
using IO.Infrastructure.Periods;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class PeriodDraftTests
{
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        Guid admin, limited, employee;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var role = await db.Roles.SingleAsync(x => x.Name == "Initial administrator");
            admin = await db.UserRoles.Where(x => x.RoleId == role.Id).Select(x => x.UserId).SingleAsync();
            limited = await db.Users.Where(x => x.UserName == "web-locked").Select(x => x.Id).SingleAsync();
            employee = await db.Employees.Where(x => x.PersonnelNumber == "CALC-1").Select(x => x.Id).SingleAsync();
            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == PeriodDrafts.Manage);
            if (permission is null) { permission = new() { Id = Guid.NewGuid(), Code = PeriodDrafts.Manage }; db.Permissions.Add(permission); }
            if (!await db.RolePermissions.AnyAsync(x => x.RoleId == role.Id && x.PermissionId == permission.Id))
                db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = permission.Id });
            await db.SaveChangesAsync();
        }
        async Task<T> Run<T>(Func<PeriodDrafts, Task<T>> f) { await using var scope = provider.CreateAsyncScope(); return await f(scope.ServiceProvider.GetRequiredService<PeriodDrafts>()); }
        async Task<bool> Reject<T>(Func<Task> f) where T : Exception { try { await f(); return false; } catch (T) { return true; } }
        var date = new DateOnly(2027, 5, 5);
        check(await Reject<UnauthorizedAccessException>(() => Run(s => s.CreateAsync(limited, employee, date, date.AddDays(1)))) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.ListAsync(limited, employee, 0))) &&
              await Reject<ArgumentException>(() => Run(s => s.CreateAsync(admin, employee, date, date.AddDays(63)))) &&
              await Reject<ArgumentException>(() => Run(s => s.CreateAsync(admin, Guid.NewGuid(), date, date.AddDays(1)))),
              "period drafts require permission, known employee and bounded interval");
        var ids = await Task.WhenAll(Run(s => s.CreateAsync(admin, employee, date, date.AddDays(2))),
                                     Run(s => s.CreateAsync(admin, employee, date, date.AddDays(2))));
        var overlap = await Run(s => s.CreateAsync(admin, employee, date.AddDays(1), date.AddDays(3)));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            check(ids[0] == ids[1] && overlap != ids[0] && await db.PeriodDrafts.CountAsync(x => x.EmployeeId == employee) == 2,
                "concurrent identical draft retries are idempotent and diagnostic overlaps are allowed");
            var immutable = false; var duplicate = false;
            try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.PeriodDrafts WHERE Id = {ids[0]}"); } catch (SqlException e) when (e.Number == 51009) { immutable = true; }
            try { await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.PeriodDrafts (Id, CompanyId, EmployeeId, Start, EndExclusive, CreatedByUserId, CreatedAtUtc) SELECT {Guid.NewGuid()}, CompanyId, EmployeeId, Start, EndExclusive, CreatedByUserId, CreatedAtUtc FROM dbo.PeriodDrafts WHERE Id = {ids[0]}"); } catch (SqlException e) when (e.Number is 2601 or 2627) { duplicate = true; }
            check(immutable && duplicate && await scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().SecurityAudit
                .AnyAsync(x => x.Action == "period.draft.registered:" + ids[0].ToString("N")),
                "SQL protects draft history and exact uniqueness and registration is audited");
        }
        var list = JsonSerializer.SerializeToElement(await Run(s => s.ListAsync(admin, employee, 0)));
        check(!list.GetProperty("approvable").GetBoolean() && list.GetProperty("status").GetString() == "diagnostic-draft-only" &&
              list.GetProperty("items").GetArrayLength() == 2, "draft list explicitly prevents interpreting drafts as approved periods");
    }
}
