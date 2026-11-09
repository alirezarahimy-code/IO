using System.Text.Json;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using IO.Infrastructure.Workflows;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class CorrectionWorkflowTests
{
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        Guid admin, self, manager, company, employee, outsider;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var adminRole = await db.Roles.SingleAsync(x => x.Name == "Initial administrator");
            var selfRole = await db.Roles.SingleAsync(x => x.Name == "web self role");
            var managerRole = await db.Roles.SingleAsync(x => x.Name == "web manager role");
            admin = await db.UserRoles.Where(x => x.RoleId == adminRole.Id).Select(x => x.UserId).SingleAsync();
            self = await db.Users.Where(x => x.UserName == "web-self").Select(x => x.Id).SingleAsync();
            manager = await db.Users.Where(x => x.UserName == "web-manager").Select(x => x.Id).SingleAsync();
            outsider = await db.Users.Where(x => x.UserName == "web-locked").Select(x => x.Id).SingleAsync();
            var selfUser = await db.Users.SingleAsync(x => x.Id == self);
            selfUser.IsActive = true; // Previous HTTP security checks intentionally deactivated this synthetic account.
            if (!await db.UserRoles.AnyAsync(x => x.UserId == manager && x.RoleId == managerRole.Id))
                db.UserRoles.Add(new() { UserId = manager, RoleId = managerRole.Id });
            employee = selfUser.EmployeeId!.Value;
            company = (await db.Employees.SingleAsync(x => x.Id == employee)).CompanyId;
            foreach (var (role, code) in new[] { (adminRole.Id, CorrectionWorkflow.Configure), (selfRole.Id, CorrectionWorkflow.Submit),
                (managerRole.Id, CorrectionWorkflow.Decide), (managerRole.Id, CorrectionWorkflow.DecideScoped) })
            {
                var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == code);
                if (permission is null) { permission = new() { Id = Guid.NewGuid(), Code = code }; db.Permissions.Add(permission); }
                if (!await db.RolePermissions.AnyAsync(x => x.RoleId == role && x.PermissionId == permission.Id))
                    db.RolePermissions.Add(new() { RoleId = role, PermissionId = permission.Id });
            }
            await db.SaveChangesAsync();
        }
        async Task<T> Run<T>(Func<CorrectionWorkflow, Task<T>> f) { await using var scope = provider.CreateAsyncScope(); return await f(scope.ServiceProvider.GetRequiredService<CorrectionWorkflow>()); }
        async Task<bool> Reject<T>(Func<Task> f) where T : Exception { try { await f(); return false; } catch (T) { return true; } }
        var workDate = new DateOnly(2027, 5, 5); var at = new DateTimeOffset(2027, 5, 5, 8, 0, 0, TimeSpan.Zero);
        check(await Reject<UnauthorizedAccessException>(() => Run(s => s.CreatePolicyAsync(self, company, manager, "denied"))) &&
              await Reject<ArgumentException>(() => Run(s => s.CreatePolicyAsync(admin, company, Guid.NewGuid(), "invalid"))), "correction policy requires configured permission and existing active approver");
        var policy = await Run(s => s.CreatePolicyAsync(admin, company, manager, "synthetic single approver"));
        var selfPolicy = await Run(s => s.CreatePolicyAsync(admin, company, self, "self forbidden"));
        check(await Reject<ArgumentException>(() => Run(s => s.SubmitAsync(self, selfPolicy, workDate, at, 0, "test"))) &&
              await Reject<ArgumentException>(() => Run(s => s.SubmitAsync(self, policy, workDate, at.ToOffset(TimeSpan.FromHours(1)), 0, "test"))) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.SubmitAsync(outsider, policy, workDate, at, 0, "test"))), "self approval, non-UTC and unauthorized correction submissions are rejected");
        var request = await Run(s => s.SubmitAsync(self, policy, workDate, at, 0, "missed punch"));
        var inbox = JsonSerializer.Serialize(await Run(s => s.InboxAsync(manager, 0)));
        check(inbox.Contains(request.ToString(), StringComparison.OrdinalIgnoreCase) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.DecideAsync(outsider, request, true, null))) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.DecideAsync(self, request, true, null))), "only configured approver with decision permission and scope can act");
        check(await Reject<ArgumentException>(() => Run(s => s.DecideAsync(manager, request, false, null))) &&
              await Run(s => s.DecideAsync(manager, request, true, "checked")) &&
              await Reject<DomainConflictException>(() => Run(s => s.CancelAsync(self, request, "late"))), "rejection needs a reason and approved requests cannot be cancelled");
        var own = JsonSerializer.Serialize(await Run(s => s.ListOwnAsync(self, 0)));
        check(own.Contains(request.ToString(), StringComparison.OrdinalIgnoreCase) && own.Contains("checked", StringComparison.Ordinal), "requester sees own decision and reason");
        var cancel = await Run(s => s.SubmitAsync(self, policy, workDate, at.AddMinutes(1), 1, "missing exit"));
        check(await Run(s => s.CancelAsync(self, cancel, "not needed")) &&
              await Reject<DomainConflictException>(() => Run(s => s.DecideAsync(manager, cancel, true, null))), "cancelled request cannot later be approved");
        var race = await Run(s => s.SubmitAsync(self, policy, workDate, at.AddMinutes(2), 1, "race"));
        async Task<bool> Attempt(Func<Task> f) { try { await f(); return true; } catch (DomainConflictException) { return false; } }
        var winners = await Task.WhenAll(Attempt(() => Run(s => s.CancelAsync(self, race, "cancel"))), Attempt(() => Run(s => s.DecideAsync(manager, race, true, null))));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            check(winners.Count(x => x) == 1 && await db.CorrectionDecisions.CountAsync(x => x.RequestId == race) == 1,
                "concurrent cancellation and approval produce exactly one immutable decision");
            var payloadBlocked = false; var decisionBlocked = false; var policyBlocked = false;
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.CorrectionRequests SET Reason = {'x'.ToString()} WHERE Id = {request}"); } catch (SqlException e) when (e.Number == 51008) { payloadBlocked = true; }
            try { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.CorrectionDecisions WHERE RequestId = {request}"); } catch (SqlException e) when (e.Number == 51007) { decisionBlocked = true; }
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.CorrectionPolicies SET Name = {'x'.ToString()} WHERE Id = {policy}"); } catch (SqlException e) when (e.Number == 51006) { policyBlocked = true; }
            check(payloadBlocked && decisionBlocked && policyBlocked &&
                  (await db.CorrectionRequests.AsNoTracking().SingleAsync(x => x.Id == request)).State == 1 &&
                  (await db.CorrectionRequests.AsNoTracking().SingleAsync(x => x.Id == cancel)).State == 3 &&
                  await db.RawAttendanceEvents.CountAsync() > 0 &&
                  await scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().SecurityAudit.AnyAsync(x => x.Action == "correction.request.decided:" + request.ToString("N")),
                "SQL protects correction payload, decisions and policy while decisions are audited");
        }
    }
}
