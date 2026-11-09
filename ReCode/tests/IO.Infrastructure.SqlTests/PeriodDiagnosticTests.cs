using System.Text.Json;
using IO.Infrastructure.Calculation;
using IO.Infrastructure.Periods;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class PeriodDiagnosticTests
{
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        Guid admin, limited, employee, draft, source, rule; var date = new DateOnly(2027, 5, 5);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var role = await db.Roles.SingleAsync(x => x.Name == "Initial administrator");
            admin = await db.UserRoles.Where(x => x.RoleId == role.Id).Select(x => x.UserId).SingleAsync();
            limited = await db.Users.Where(x => x.UserName == "web-locked").Select(x => x.Id).SingleAsync();
            employee = await db.Employees.Where(x => x.PersonnelNumber == "CALC-1").Select(x => x.Id).SingleAsync();
            draft = await db.PeriodDrafts.Where(x => x.EmployeeId == employee && x.Start == date && x.EndExclusive == date.AddDays(2)).Select(x => x.Id).SingleAsync();
            var latest = await db.DailyCalculationRuns.Where(x => x.EmployeeId == employee && x.WorkDate == date)
                .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).FirstAsync();
            source = latest.SourceId; rule = latest.RuleVersionId;
            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == PeriodDiagnostics.Manage);
            if (permission is null) { permission = new() { Id = Guid.NewGuid(), Code = PeriodDiagnostics.Manage }; db.Permissions.Add(permission); }
            if (!await db.RolePermissions.AnyAsync(x => x.RoleId == role.Id && x.PermissionId == permission.Id))
                db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = permission.Id });
            await db.SaveChangesAsync();
        }
        async Task<T> Use<T>(Func<PeriodDiagnostics, Task<T>> f) { await using var scope = provider.CreateAsyncScope(); return await f(scope.ServiceProvider.GetRequiredService<PeriodDiagnostics>()); }
        async Task<T> Calc<T>(Func<ObservedCalculation, Task<T>> f) { await using var scope = provider.CreateAsyncScope(); return await f(scope.ServiceProvider.GetRequiredService<ObservedCalculation>()); }
        async Task<bool> Reject<T>(Func<Task> f) where T : Exception { try { await f(); return false; } catch (T) { return true; } }
        check(await Reject<UnauthorizedAccessException>(() => Use(s => s.RecordAsync(limited, draft))) &&
              await Reject<UnauthorizedAccessException>(() => Use(s => s.ListAsync(limited, draft, 0))) &&
              await Reject<ArgumentException>(() => Use(s => s.RecordAsync(admin, Guid.NewGuid()))),
              "period diagnostic recording requires explicit permission and existing draft");
        var first = await Use(s => s.RecordAsync(admin, draft));
        var firstReport = JsonSerializer.SerializeToElement(await Use(s => s.AssessAsync(admin, first)));
        check(firstReport.GetProperty("status").GetString() == "no-monitored-diagnostic-change-detected" &&
              firstReport.GetProperty("freshness").GetString() == "unknown" && !firstReport.GetProperty("approvable").GetBoolean() &&
              firstReport.GetProperty("missingDays").GetInt32() == 1 && firstReport.GetProperty("daysWithAnomaliesOrNoValue").GetInt32() == 1 &&
              firstReport.GetProperty("runsWithChangeOrUnknown").GetInt32() == 1 && firstReport.GetProperty("days").GetArrayLength() == 2,
              "period diagnostic captures missing days and stale run signal without claiming approval");
        var listing = JsonSerializer.SerializeToElement(await Use(s => s.ListAsync(admin, draft, 0)));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var blockedDelete = false; var blockedUpdate = false;
            try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.PeriodDiagnostics WHERE Id = {first}"); }
            catch (SqlException e) when (e.Number == 51014) { blockedDelete = true; }
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.PeriodDiagnostics SET EvidenceJson = {'{'.ToString() + '}'} WHERE Id = {first}"); }
            catch (SqlException e) when (e.Number == 51014) { blockedUpdate = true; }
            check(blockedDelete && blockedUpdate && !listing.GetProperty("approvable").GetBoolean() && listing.GetProperty("items").GetArrayLength() == 1 &&
                  await scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().SecurityAudit.AnyAsync(x => x.Action == "period.diagnostic.recorded:" + first.ToString("N")) &&
                  await scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().SecurityAudit.AnyAsync(x => x.Action == "period.diagnostic.assessed:" + first.ToString("N")),
                  "period diagnostic evidence is immutable, listed and audited");
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Employees SET DisplayName = DisplayName WHERE Id = {employee}");
        }
        var changed = JsonSerializer.SerializeToElement(await Use(s => s.AssessAsync(admin, first)));
        check(changed.GetProperty("status").GetString() == "monitored-database-changed" &&
              !changed.GetProperty("approvable").GetBoolean(), "period diagnostic detects later global input mutations");
        var second = await Use(s => s.RecordAsync(admin, draft));
        await Calc(s => s.CalculateAsync(admin, employee, date, source, rule));
        var recalculated = JsonSerializer.SerializeToElement(await Use(s => s.AssessAsync(admin, second)));
        check(recalculated.GetProperty("status").GetString() == "latest-results-changed" && recalculated.GetProperty("changedDays").GetInt32() == 1 &&
              recalculated.GetProperty("databaseChange").GetString() == "no-monitored-database-change-detected" &&
              !recalculated.GetProperty("approvable").GetBoolean(), "period diagnostic detects new runs even without an input revision change");
        var invalid = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            db.PeriodDiagnostics.Add(new() { Id = invalid, DraftId = draft, EvidenceJson = "{}", CreatedByUserId = admin, CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var invalidStatus = JsonSerializer.SerializeToElement(await Use(s => s.AssessAsync(admin, invalid)));
        check(invalidStatus.GetProperty("status").GetString() == "unrecognized-evidence" && !invalidStatus.GetProperty("approvable").GetBoolean() &&
              await Reject<UnauthorizedAccessException>(() => Use(s => s.AssessAsync(limited, first))),
              "unrecognized evidence fails closed and diagnostic results require permission");
        var concurrent = await Task.WhenAll(Use(s => s.RecordAsync(admin, draft)), Use(s => s.RecordAsync(admin, draft)));
        check(concurrent.Distinct().Count() == 2, "concurrent diagnostic captures preserve independent historical records");
    }
}
