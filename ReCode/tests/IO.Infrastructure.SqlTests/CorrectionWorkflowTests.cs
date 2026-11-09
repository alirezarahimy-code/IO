using System.Text.Json;
using IO.Infrastructure.Security;
using IO.Infrastructure.Attendance;
using IO.Infrastructure.Calculation;
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
        Guid admin, self, manager, company, employee, outsider, otherActor = Guid.NewGuid(), duplicateActor = Guid.NewGuid();
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
            var otherEmployee = await db.Employees.SingleAsync(x => x.CompanyId == company && x.PersonnelNumber == "WEB-2");
            db.Users.Add(new() { Id = otherActor, UserName = "synthetic-other-correction-user", EmployeeId = otherEmployee.Id, IsActive = true });
            db.UserRoles.Add(new() { UserId = otherActor, RoleId = selfRole.Id });
            db.Users.Add(new() { Id = duplicateActor, UserName = "synthetic-same-employee-user", EmployeeId = employee, IsActive = true });
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
        var duplicatePolicy = await Run(s => s.CreatePolicyAsync(admin, company, duplicateActor, "same employee forbidden"));
        check(await Reject<ArgumentException>(() => Run(s => s.SubmitAsync(self, selfPolicy, workDate, at, 0, "test"))) &&
              await Reject<ArgumentException>(() => Run(s => s.SubmitAsync(self, duplicatePolicy, workDate, at, 0, "test"))) &&
              await Reject<ArgumentException>(() => Run(s => s.SubmitAsync(self, policy, workDate, at.ToOffset(TimeSpan.FromHours(1)), 0, "test"))) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.SubmitAsync(outsider, policy, workDate, at, 0, "test"))), "self approval, non-UTC and unauthorized correction submissions are rejected");
        var outside = await Run(s => s.SubmitAsync(otherActor, policy, workDate, at, 0, "outside unit"));
        var scopedInbox = JsonSerializer.Serialize(await Run(s => s.InboxAsync(manager, 0)));
        check(!scopedInbox.Contains(outside.ToString(), StringComparison.OrdinalIgnoreCase) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.DecideAsync(manager, outside, true, null))) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.CancelAsync(self, outside, "not mine"))),
              "correction inbox and decisions exclude employees outside exact unit and other users' requests");
        var policies = JsonSerializer.Serialize(await Run(s => s.ListPoliciesAsync(self, 0)));
        check(policies.Contains(policy.ToString(), StringComparison.OrdinalIgnoreCase) &&
              !policies.Contains(duplicatePolicy.ToString(), StringComparison.OrdinalIgnoreCase) &&
              !policies.Contains(selfPolicy.ToString(), StringComparison.OrdinalIgnoreCase),
              "policy list excludes any approver account linked to the requester employee");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            (await db.Users.SingleAsync(x => x.Id == manager)).IsActive = false;
            await db.SaveChangesAsync();
        }
        var inactivePolicies = JsonSerializer.Serialize(await Run(s => s.ListPoliciesAsync(self, 0)));
        check(!inactivePolicies.Contains(policy.ToString(), StringComparison.OrdinalIgnoreCase) &&
              await Reject<DomainConflictException>(() => Run(s => s.SubmitAsync(self, policy, workDate, at, 0, "inactive"))),
              "inactive approver policies cannot accept new requests");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            (await db.Users.SingleAsync(x => x.Id == manager)).IsActive = true;
            await db.SaveChangesAsync();
        }
        var request = await Run(s => s.SubmitAsync(self, policy, workDate, at, 0, "missed punch"));
        var inbox = JsonSerializer.Serialize(await Run(s => s.InboxAsync(manager, 0)));
        check(inbox.Contains(request.ToString(), StringComparison.OrdinalIgnoreCase) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.DecideAsync(outsider, request, true, null))) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.DecideAsync(self, request, true, null))), "only configured approver with decision permission and scope can act");
        check(await Reject<ArgumentException>(() => Run(s => s.DecideAsync(manager, request, false, null))) &&
              await Run(s => s.DecideAsync(manager, request, true, "checked")) &&
              await Reject<DomainConflictException>(() => Run(s => s.CancelAsync(self, request, "late"))), "rejection needs a reason and approved requests cannot be cancelled");
        var decisions = JsonSerializer.Serialize(await Run(s => s.ListDecisionsAsync(manager, 0)));
        check(decisions.Contains(request.ToString(), StringComparison.OrdinalIgnoreCase) &&
              !decisions.Contains(outside.ToString(), StringComparison.OrdinalIgnoreCase) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.ListDecisionsAsync(self, 0))),
              "decision history is limited to the approver and current authorized scope");
        var own = JsonSerializer.Serialize(await Run(s => s.ListOwnAsync(self, 0)));
        check(own.Contains(request.ToString(), StringComparison.OrdinalIgnoreCase) && own.Contains("checked", StringComparison.Ordinal), "requester sees own decision and reason");
        var cancel = await Run(s => s.SubmitAsync(self, policy, workDate, at.AddMinutes(1), 1, "missing exit"));
        check(await Run(s => s.CancelAsync(self, cancel, "not needed")) &&
              await Reject<DomainConflictException>(() => Run(s => s.DecideAsync(manager, cancel, true, null))), "cancelled request cannot later be approved");
        var integratedDate = new DateOnly(2028, 6, 6);
        var integratedAt = new DateTimeOffset(2028, 6, 6, 8, 0, 0, TimeSpan.Zero);
        var site = Guid.NewGuid(); var source = Guid.NewGuid(); var calendar = Guid.NewGuid(); var shift = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            // The integration fixture owns exactly this day. Earlier browser fixtures
            // must not leave an open-ended assignment that collides with it.
            if (await db.WorkScheduleAssignments.AnyAsync(x => x.EmployeeId == employee &&
                x.ValidFrom <= integratedDate && (x.ValidUntilExclusive == null || integratedDate < x.ValidUntilExclusive)))
                throw new InvalidOperationException("Correction integration fixture has a pre-existing schedule on its test day.");
            db.Sites.Add(new() { Id = site, CompanyId = company, Name = "correction site", TimeZoneId = "Etc/UTC" });
            db.EventSources.Add(new() { Id = source, SiteId = site, Name = "correction source" });
            db.Calendars.Add(new() { Id = calendar, CompanyId = company, Name = "correction calendar" });
            db.CalendarDays.Add(new() { Id = Guid.NewGuid(), CalendarId = calendar, Date = integratedDate, Kind = 0, RecordedAtUtc = DateTimeOffset.UtcNow });
            db.ShiftDefinitions.Add(new() { Id = shift, CompanyId = company, SiteId = site, Name = "correction shift", StartsAt = new(8, 0), EndsAt = new(16, 0) });
            db.WorkScheduleAssignments.Add(new() { Id = Guid.NewGuid(), CompanyId = company, EmployeeId = employee, CalendarId = calendar,
                ShiftId = shift, ValidFrom = integratedDate, ValidUntilExclusive = integratedDate.AddDays(1) });
            await db.SaveChangesAsync();
        }
        async Task<T> Calc<T>(Func<ObservedCalculation, Task<T>> f) { await using var scope = provider.CreateAsyncScope(); return await f(scope.ServiceProvider.GetRequiredService<ObservedCalculation>()); }
        async Task<T> Raw<T>(Func<AttendanceIngestion, Task<T>> f) { await using var scope = provider.CreateAsyncScope(); return await f(scope.ServiceProvider.GetRequiredService<AttendanceIngestion>()); }
        var calculationRule = await Calc(s => s.CreateRuleAsync(admin, company, "correction test rule", 0, 0));
        await Raw(s => s.AddMappingAsync(admin, source, "correction-person", employee, integratedAt.AddDays(-1), null));
        var rawExit = await Raw(s => s.AppendAsync(admin, source, "correction-exit", "correction-person", integratedAt.AddHours(8), null, "out", [9]));
        var pending = await Run(s => s.SubmitAsync(self, policy, integratedDate, integratedAt, 0, "forgot entry"));
        var before = await Calc(s => s.CalculateAsync(admin, employee, integratedDate, source, calculationRule));
        await using (var scope = provider.CreateAsyncScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<IoDbContext>().DailyCalculationRuns.SingleAsync(x => x.Id == before);
            check(run.ObservedTicks is null && run.AnomaliesJson.Contains("pending-correction", StringComparison.Ordinal),
                "pending correction blocks a numerical calculation");
        }
        await Run(s => s.DecideAsync(manager, pending, true, "verified"));
        var revision = JsonSerializer.SerializeToElement(await Calc(s => s.AssessDatabaseChangesAsync(admin, before)));
        check(revision.GetProperty("databaseChange").GetString() == "monitored-database-changed",
            "approval changes monitored database revision for earlier calculation");
        var after = await Calc(s => s.CalculateAsync(admin, employee, integratedDate, source, calculationRule));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var run = await db.DailyCalculationRuns.SingleAsync(x => x.Id == after);
            var prior = await db.DailyCalculationRuns.SingleAsync(x => x.Id == before);
            check(run.ObservedTicks == TimeSpan.FromHours(8).Ticks && run.AnomaliesJson == "[]" &&
                run.InputSnapshotJson.Contains(pending.ToString(), StringComparison.OrdinalIgnoreCase) &&
                run.InputSnapshotJson.Contains("verified", StringComparison.Ordinal) && prior.ObservedTicks is null &&
                await db.RawAttendanceEvents.CountAsync(x => x.Id == rawExit.EventId) == 1,
                "approved correction contributes to new snapshot without altering raw or previous run");
        }
        var outsideWindow = await Run(s => s.SubmitAsync(self, policy, integratedDate, integratedAt.AddHours(9), 1, "outside window"));
        await Run(s => s.DecideAsync(manager, outsideWindow, true, "verified"));
        var outsideRun = await Calc(s => s.CalculateAsync(admin, employee, integratedDate, source, calculationRule));
        await using (var scope = provider.CreateAsyncScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<IoDbContext>().DailyCalculationRuns.SingleAsync(x => x.Id == outsideRun);
            check(run.ObservedTicks is null && run.AnomaliesJson.Contains("correction-outside-window", StringComparison.Ordinal),
                "approved correction outside the work window blocks a numerical result");
        }
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
