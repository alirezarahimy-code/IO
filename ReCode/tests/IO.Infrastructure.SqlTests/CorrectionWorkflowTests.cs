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
        var operation=Guid.NewGuid();
        var identical=await Run(s=>s.SubmitAsync(self,policy,workDate,at,0,"safe retry",operationId:operation));
        check(identical==operation && await Run(s=>s.SubmitAsync(self,policy,workDate,at,0,"safe retry",operationId:operation))==operation &&
              await Reject<DomainConflictException>(()=>Run(s=>s.SubmitAsync(self,policy,workDate,at,1,"safe retry",operationId:operation))) &&
              await Reject<DomainConflictException>(()=>Run(s=>s.SubmitAsync(otherActor,policy,workDate,at,0,"safe retry",operationId:operation))) &&
              await Reject<ArgumentException>(()=>Run(s=>s.SubmitAsync(self,policy,workDate,at,0,"safe retry",operationId:Guid.Empty))),
              "correction submission retries use one immutable operation id and reject changed payload or actor");
        var concurrentOp=Guid.NewGuid();
        async Task<bool> SubmitOnce(){try{return await Run(s=>s.SubmitAsync(self,policy,workDate,at,0,"concurrent retry",operationId:concurrentOp))==concurrentOp;}catch(DomainConflictException){return false;}}
        var attempts=await Task.WhenAll(SubmitOnce(),SubmitOnce());
        await using(var s=provider.CreateAsyncScope())
        {
            var db=s.ServiceProvider.GetRequiredService<IoDbContext>();
            check(attempts.Any(x=>x) && await db.CorrectionRequests.CountAsync(x=>x.Id==concurrentOp)==1 &&
                  await Run(w=>w.SubmitAsync(self,policy,workDate,at,0,"concurrent retry",operationId:concurrentOp))==concurrentOp,
                  "concurrent correction retries persist at most one request and a later retry resolves it");
        }
        var configured = JsonSerializer.Serialize(await Run(s => s.ListConfiguredPoliciesAsync(admin, company, 0)));
        check(configured.Contains(policy.ToString(), StringComparison.OrdinalIgnoreCase) && configured.Contains(manager.ToString(), StringComparison.OrdinalIgnoreCase) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.ListConfiguredPoliciesAsync(self, company, 0))) &&
              await Reject<ArgumentException>(() => Run(s => s.ListConfiguredPoliciesAsync(admin, Guid.NewGuid(), 0))) &&
              await Reject<ArgumentException>(() => Run(s => s.ListConfiguredPoliciesAsync(admin, company, -1))),
              "configured correction policy catalog requires configure permission and valid bounded company query");
        var selfPolicy = await Run(s => s.CreatePolicyAsync(admin, company, self, "self forbidden"));
        var duplicatePolicy = await Run(s => s.CreatePolicyAsync(admin, company, duplicateActor, "same employee forbidden"));
        check(await Reject<ArgumentException>(() => Run(s => s.SubmitAsync(self, selfPolicy, workDate, at, 0, "test"))) &&
              await Reject<ArgumentException>(() => Run(s => s.SubmitAsync(self, duplicatePolicy, workDate, at, 0, "test"))) &&
              await Reject<ArgumentException>(() => Run(s => s.SubmitAsync(self, policy, workDate, at.ToOffset(TimeSpan.FromHours(1)), 0, "test"))) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.SubmitAsync(outsider, policy, workDate, at, 0, "test"))), "self approval, non-UTC and unauthorized correction submissions are rejected");
        var outside = await Run(s => s.SubmitAsync(otherActor, policy, workDate, at, 0, "outside unit"));
        var otherToCancel = await Run(s => s.SubmitAsync(otherActor, policy, workDate.AddDays(2), at.AddDays(2), 1, "cancel independently"));
        // A separate role exercises the split without changing the other actor's identity.
        Guid testReadRole, readPermission, submitPermission, legacySelfRole;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            legacySelfRole = (await db.Roles.SingleAsync(x => x.Name == "web self role")).Id;
            submitPermission = (await db.Permissions.SingleAsync(x => x.Code == CorrectionWorkflow.Submit)).Id;
            var read = await db.Permissions.SingleOrDefaultAsync(x => x.Code == CorrectionWorkflow.ReadOwn);
            if (read is null) { read = new() { Id = Guid.NewGuid(), Code = CorrectionWorkflow.ReadOwn }; db.Permissions.Add(read); }
            readPermission = read.Id; testReadRole = Guid.NewGuid();
            db.Roles.Add(new() { Id = testReadRole, Name = "synthetic read-only correction role" });
            db.UserRoles.Add(new() { UserId = otherActor, RoleId = testReadRole });
            db.RolePermissions.Add(new() { RoleId = testReadRole, PermissionId = readPermission });
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == legacySelfRole && x.PermissionId == submitPermission));
            await db.SaveChangesAsync();
        }
        var readOnlyHistory = JsonSerializer.Serialize(await Run(s => s.ListOwnAsync(otherActor, 0)));
        var readOnlyDenied = await Reject<UnauthorizedAccessException>(() => Run(s => s.SubmitAsync(otherActor, policy, workDate, at, 0, "denied"))) &&
            await Reject<UnauthorizedAccessException>(() => Run(s => s.CancelAsync(otherActor, outside, "denied"))) &&
            await Reject<UnauthorizedAccessException>(() => Run(s => s.ListPoliciesAsync(otherActor, 0)));
        Guid cancelPermission;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var cancelGrant = await db.Permissions.SingleOrDefaultAsync(x => x.Code == CorrectionWorkflow.CancelOwn);
            if (cancelGrant is null) { cancelGrant = new() { Id = Guid.NewGuid(), Code = CorrectionWorkflow.CancelOwn }; db.Permissions.Add(cancelGrant); }
            cancelPermission = cancelGrant.Id;
            db.RolePermissions.Add(new() { RoleId = testReadRole, PermissionId = cancelPermission });
            await db.SaveChangesAsync();
        }
        var cancellableHistory = JsonSerializer.Serialize(await Run(s => s.ListOwnAsync(otherActor, 0)));
        var independentCancel = await Run(s => s.CancelAsync(otherActor, otherToCancel, "no longer needed"));
        var cancelCannotSubmit = await Reject<UnauthorizedAccessException>(() => Run(s => s.SubmitAsync(otherActor, policy, workDate, at, 0, "denied"))) &&
            await Reject<UnauthorizedAccessException>(() => Run(s => s.ListPoliciesAsync(otherActor, 0))) &&
            await Reject<UnauthorizedAccessException>(() => Run(s => s.CancelAsync(otherActor, identical, "not mine")));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == testReadRole && x.PermissionId == cancelPermission));
            await db.SaveChangesAsync();
        }
        var revokedCancel = await Reject<UnauthorizedAccessException>(() => Run(s => s.CancelAsync(otherActor, outside, "denied")));
        check(cancellableHistory.Contains("\"canCancel\":true", StringComparison.OrdinalIgnoreCase) && independentCancel && cancelCannotSubmit && revokedCancel,
              "own cancellation is separately grantable and revocable without granting submission");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            (await db.Users.SingleAsync(x => x.Id == otherActor)).IsActive = false;
            await db.SaveChangesAsync();
        }
        var inactiveDenied = await Reject<UnauthorizedAccessException>(() => Run(s => s.ListOwnAsync(otherActor, 0)));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            (await db.Users.SingleAsync(x => x.Id == otherActor)).IsActive = true;
            (await db.Permissions.SingleAsync(x => x.Id == readPermission)).Code = CorrectionWorkflow.ReadOwn.ToUpperInvariant();
            await db.SaveChangesAsync();
        }
        var caseDenied = await Reject<UnauthorizedAccessException>(() => Run(s => s.ListOwnAsync(otherActor, 0)));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            (await db.Permissions.SingleAsync(x => x.Id == readPermission)).Code = CorrectionWorkflow.ReadOwn + " ";
            await db.SaveChangesAsync();
        }
        var paddingDenied = await Reject<UnauthorizedAccessException>(() => Run(s => s.ListOwnAsync(otherActor, 0)));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            (await db.Permissions.SingleAsync(x => x.Id == readPermission)).Code = CorrectionWorkflow.ReadOwn;
            db.RolePermissions.Add(new() { RoleId = legacySelfRole, PermissionId = submitPermission });
            await db.SaveChangesAsync();
        }
        var legacyHistory = JsonSerializer.Serialize(await Run(s => s.ListOwnAsync(self, 0)));
        check(readOnlyHistory.Contains(outside.ToString(), StringComparison.OrdinalIgnoreCase) &&
              !readOnlyHistory.Contains(identical.ToString(), StringComparison.OrdinalIgnoreCase) &&
              readOnlyHistory.Contains("\"canCancel\":false", StringComparison.OrdinalIgnoreCase) && readOnlyDenied && inactiveDenied && caseDenied && paddingDenied &&
              legacyHistory.Contains(identical.ToString(), StringComparison.OrdinalIgnoreCase) && legacyHistory.Contains("\"canCancel\":true", StringComparison.OrdinalIgnoreCase),
              "own correction read-only permission is exact, active, isolated and preserves legacy submit access");
        var splitApproved = await Run(s => s.SubmitAsync(self, policy, workDate.AddDays(3), at.AddDays(3), 0, "independent approval"));
        var splitRejected = await Run(s => s.SubmitAsync(self, policy, workDate.AddDays(4), at.AddDays(4), 0, "independent rejection"));
        Guid managerRoleId, decidePermissionId, scopedPermissionId, inboxReadId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            managerRoleId = (await db.Roles.SingleAsync(x => x.Name == "web manager role")).Id;
            decidePermissionId = (await db.Permissions.SingleAsync(x => x.Code == CorrectionWorkflow.Decide)).Id;
            scopedPermissionId = (await db.Permissions.SingleAsync(x => x.Code == CorrectionWorkflow.DecideScoped)).Id;
            var read = await db.Permissions.SingleOrDefaultAsync(x => x.Code == CorrectionWorkflow.ReadInbox);
            if (read is null) { read = new() { Id = Guid.NewGuid(), Code = CorrectionWorkflow.ReadInbox }; db.Permissions.Add(read); }
            inboxReadId = read.Id;
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == managerRoleId && x.PermissionId == decidePermissionId));
            db.RolePermissions.Add(new() { RoleId = managerRoleId, PermissionId = inboxReadId });
            await db.SaveChangesAsync();
        }
        var readInbox = JsonSerializer.Serialize(await Run(s => s.InboxAsync(manager, 0, employeeId: employee)));
        var inboxEmployees = JsonSerializer.SerializeToElement(await Run(s => s.InboxEmployeesAsync(manager, 0)));
        Guid otherEmployeeId;
        await using (var scope = provider.CreateAsyncScope())
            otherEmployeeId = (await scope.ServiceProvider.GetRequiredService<IoDbContext>().Employees.SingleAsync(x => x.CompanyId == company && x.PersonnelNumber == "WEB-2")).Id;
        var outsideInbox = JsonSerializer.Serialize(await Run(s => s.InboxAsync(manager, 0, employeeId: otherEmployeeId)));
        var noDecision = await Reject<UnauthorizedAccessException>(() => Run(s => s.DecideAsync(manager, identical, true, "denied"))) &&
            await Reject<UnauthorizedAccessException>(() => Run(s => s.ListDecisionsAsync(manager, 0)));
        Guid approvePermissionId, rejectPermissionId, historyPermissionId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            async Task<Guid> EnsurePermission(string code)
            {
                var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == code);
                if (permission is null) { permission = new() { Id = Guid.NewGuid(), Code = code }; db.Permissions.Add(permission); }
                return permission.Id;
            }
            approvePermissionId = await EnsurePermission(CorrectionWorkflow.Approve);
            rejectPermissionId = await EnsurePermission(CorrectionWorkflow.Reject);
            historyPermissionId = await EnsurePermission(CorrectionWorkflow.ReadDecisions);
            db.RolePermissions.Add(new() { RoleId = managerRoleId, PermissionId = approvePermissionId });
            await db.SaveChangesAsync();
        }
        var approveOnlyInbox = JsonSerializer.Serialize(await Run(s => s.InboxAsync(manager, 0)));
        var approveOnlyDenied = await Reject<UnauthorizedAccessException>(() => Run(s => s.DecideAsync(manager, splitRejected, false, "denied"))) &&
            await Reject<UnauthorizedAccessException>(() => Run(s => s.DecideAsync(manager, outside, true, null)));
        var approvedSeparately = await Run(s => s.DecideAsync(manager, splitApproved, true, "verified"));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == managerRoleId && x.PermissionId == approvePermissionId));
            db.RolePermissions.Add(new() { RoleId = managerRoleId, PermissionId = rejectPermissionId });
            await db.SaveChangesAsync();
        }
        var rejectOnlyInbox = JsonSerializer.Serialize(await Run(s => s.InboxAsync(manager, 0)));
        var rejectOnlyDenied = await Reject<UnauthorizedAccessException>(() => Run(s => s.DecideAsync(manager, splitRejected, true, null))) &&
            await Reject<ArgumentException>(() => Run(s => s.DecideAsync(manager, splitRejected, false, null)));
        var rejectedSeparately = await Run(s => s.DecideAsync(manager, splitRejected, false, "not accepted"));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == managerRoleId && x.PermissionId == rejectPermissionId));
            db.RolePermissions.Add(new() { RoleId = managerRoleId, PermissionId = historyPermissionId });
            await db.SaveChangesAsync();
        }
        var splitHistory = JsonSerializer.Serialize(await Run(s => s.ListDecisionsAsync(manager, 0)));
        var revokedActions = await Reject<UnauthorizedAccessException>(() => Run(s => s.DecideAsync(manager, identical, true, null))) &&
            await Reject<UnauthorizedAccessException>(() => Run(s => s.DecideAsync(manager, identical, false, "denied")));
        check(approveOnlyInbox.Contains("\"canApprove\":true", StringComparison.OrdinalIgnoreCase) && approveOnlyInbox.Contains("\"canReject\":false", StringComparison.OrdinalIgnoreCase) &&
              rejectOnlyInbox.Contains("\"canApprove\":false", StringComparison.OrdinalIgnoreCase) && rejectOnlyInbox.Contains("\"canReject\":true", StringComparison.OrdinalIgnoreCase) &&
              approveOnlyDenied && approvedSeparately && rejectOnlyDenied && rejectedSeparately && revokedActions &&
              splitHistory.Contains(splitApproved.ToString(), StringComparison.OrdinalIgnoreCase) && splitHistory.Contains(splitRejected.ToString(), StringComparison.OrdinalIgnoreCase) &&
              !splitHistory.Contains(outside.ToString(), StringComparison.OrdinalIgnoreCase),
              "approval, rejection and own decision history have distinct permissions with scoped legacy compatibility");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == managerRoleId && x.PermissionId == scopedPermissionId));
            await db.SaveChangesAsync();
        }
        var noScopeInbox = JsonSerializer.Serialize(await Run(s => s.InboxAsync(manager, 0, employeeId: employee)));
        var noScopeHistory = JsonSerializer.Serialize(await Run(s => s.ListDecisionsAsync(manager, 0)));
        var noScopeEmployees = JsonSerializer.SerializeToElement(await Run(s => s.InboxEmployeesAsync(manager, 0)));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            db.RolePermissions.Add(new() { RoleId = managerRoleId, PermissionId = scopedPermissionId });
            (await db.Users.SingleAsync(x => x.Id == manager)).IsActive = false;
            await db.SaveChangesAsync();
        }
        var inactiveInbox = await Reject<UnauthorizedAccessException>(() => Run(s => s.InboxAsync(manager, 0))) &&
            await Reject<UnauthorizedAccessException>(() => Run(s => s.ListDecisionsAsync(manager, 0)));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            (await db.Users.SingleAsync(x => x.Id == manager)).IsActive = true;
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == managerRoleId && x.PermissionId == inboxReadId));
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == managerRoleId && x.PermissionId == historyPermissionId));
            await db.SaveChangesAsync();
        }
        var revokedInbox = await Reject<UnauthorizedAccessException>(() => Run(s => s.InboxAsync(manager, 0)));
        var revokedEmployees = await Reject<UnauthorizedAccessException>(() => Run(s => s.InboxEmployeesAsync(manager, 0)));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            db.RolePermissions.Add(new() { RoleId = managerRoleId, PermissionId = decidePermissionId });
            await db.SaveChangesAsync();
        }
        var employeeChoices = inboxEmployees.GetProperty("items").EnumerateArray().ToArray();
        check(employeeChoices.Count(x => x.GetProperty("Id").GetGuid() == employee) == 1 &&
              employeeChoices.All(x => x.GetProperty("Id").GetGuid() != otherEmployeeId) &&
              employeeChoices.Single(x => x.GetProperty("Id").GetGuid() == employee).GetProperty("PersonnelNumber").GetString() == "WEB-1" &&
              !noScopeEmployees.GetProperty("items").EnumerateArray().Any() && revokedEmployees &&
              await Reject<ArgumentException>(() => Run(s => s.InboxEmployeesAsync(manager, -1))) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.InboxEmployeesAsync(outsider, 0))),
              "inbox employee picker is deduplicated and limited to assigned pending requests within scope");
        var legacyInbox = JsonSerializer.Serialize(await Run(s => s.InboxAsync(manager, 0, employeeId: employee)));
        check(readInbox.Contains(identical.ToString(), StringComparison.OrdinalIgnoreCase) &&
              readInbox.Contains("\"EmployeeName\":\"First\"", StringComparison.Ordinal) && readInbox.Contains("WEB-1", StringComparison.Ordinal) &&
              readInbox.Contains("\"canDecide\":false", StringComparison.OrdinalIgnoreCase) && !outsideInbox.Contains(outside.ToString(), StringComparison.OrdinalIgnoreCase) &&
              !noScopeInbox.Contains(identical.ToString(), StringComparison.OrdinalIgnoreCase) && !noScopeHistory.Contains(splitApproved.ToString(), StringComparison.OrdinalIgnoreCase) && noDecision && inactiveInbox && revokedInbox &&
              legacyInbox.Contains(identical.ToString(), StringComparison.OrdinalIgnoreCase) && legacyInbox.Contains("\"canDecide\":true", StringComparison.OrdinalIgnoreCase) &&
              await Reject<ArgumentException>(() => Run(s => s.InboxAsync(manager, 0, employeeId: Guid.Empty))),
              "inbox read permission requires assigned approver and scope, filters employees and never grants decisions");
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
        var nextDayRequest = await Run(s => s.SubmitAsync(self, policy, workDate.AddDays(1), at.AddDays(1), 0, "next day filter"));
        var ascendingInbox = JsonSerializer.SerializeToElement(await Run(s => s.InboxAsync(manager, 0, employeeId: employee, sort: "work-asc")));
        var descendingInbox = JsonSerializer.SerializeToElement(await Run(s => s.InboxAsync(manager, 0, employeeId: employee, sort: "work-desc")));
        var ascendingItems = ascendingInbox.GetProperty("items").EnumerateArray().ToArray();
        var descendingItems = descendingInbox.GetProperty("items").EnumerateArray().ToArray();
        var ascendingDays = ascendingItems.Select(x => x.GetProperty("WorkDate").GetString()).ToArray();
        var descendingDays = descendingItems.Select(x => x.GetProperty("WorkDate").GetString()).ToArray();
        check(ascendingItems.Any(x => x.GetProperty("Id").GetGuid() == nextDayRequest) &&
              ascendingItems.Any(x => x.GetProperty("Id").GetGuid() == identical) &&
              ascendingDays.SequenceEqual(ascendingDays.OrderBy(x => x)) && descendingDays.SequenceEqual(descendingDays.OrderByDescending(x => x)) &&
              ascendingItems.Select(x => x.GetProperty("Id").GetGuid()).ToHashSet().SetEquals(descendingItems.Select(x => x.GetProperty("Id").GetGuid())) &&
              !ascendingItems.Any(x => x.GetProperty("Id").GetGuid() == outside) &&
              await Reject<ArgumentException>(() => Run(s => s.InboxAsync(manager, 0, sort: "unknown"))),
              "inbox work-date ordering is applied before pagination and preserves scope");
        var datedInbox = JsonSerializer.SerializeToElement(await Run(s => s.InboxAsync(manager, 0, employeeId: employee, fromDate: workDate, toDate: workDate)));
        var datedItems = datedInbox.GetProperty("items").EnumerateArray().ToArray();
        check(datedInbox.GetProperty("fromDate").GetString() == workDate.ToString("yyyy-MM-dd") &&
              datedItems.Any(x => x.GetProperty("Id").GetGuid() == identical) &&
              !datedItems.Any(x => x.GetProperty("Id").GetGuid() == nextDayRequest) &&
              datedItems.All(x => x.GetProperty("WorkDate").GetString() == workDate.ToString("yyyy-MM-dd")) &&
              await Reject<ArgumentException>(() => Run(s => s.InboxAsync(manager, 0, fromDate: workDate))) &&
              await Reject<ArgumentException>(() => Run(s => s.InboxAsync(manager, 0, fromDate: workDate.AddDays(1), toDate: workDate))),
              "inbox work-date filter is inclusive, applies before pagination and rejects invalid ranges");
        var exactApproved = JsonSerializer.Serialize(await Run(s => s.ListOwnAsync(self, 0, fromDate: workDate, toDate: workDate, state: 1)));
        var rangePending = JsonSerializer.Serialize(await Run(s => s.ListOwnAsync(self, 0, fromDate: workDate, toDate: workDate.AddDays(1), state: 0)));
        var exactCancelled = JsonSerializer.Serialize(await Run(s => s.ListOwnAsync(self, 0, fromDate: workDate, toDate: workDate, state: 3)));
        check(exactApproved.Contains(request.ToString(), StringComparison.OrdinalIgnoreCase) && !exactApproved.Contains(cancel.ToString(), StringComparison.OrdinalIgnoreCase) &&
              !exactApproved.Contains(outside.ToString(), StringComparison.OrdinalIgnoreCase) && !exactApproved.Contains(nextDayRequest.ToString(), StringComparison.OrdinalIgnoreCase) &&
              rangePending.Contains(nextDayRequest.ToString(), StringComparison.OrdinalIgnoreCase) && rangePending.Contains(identical.ToString(), StringComparison.OrdinalIgnoreCase) &&
              exactCancelled.Contains(cancel.ToString(), StringComparison.OrdinalIgnoreCase) && !exactCancelled.Contains(request.ToString(), StringComparison.OrdinalIgnoreCase) &&
              await Reject<ArgumentException>(() => Run(s => s.ListOwnAsync(self, 0, fromDate: workDate))) &&
              await Reject<ArgumentException>(() => Run(s => s.ListOwnAsync(self, 0, fromDate: workDate.AddDays(1), toDate: workDate))) &&
              await Reject<ArgumentException>(() => Run(s => s.ListOwnAsync(self, 0, state: 4))) &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.ListOwnAsync(outsider, 0, fromDate: workDate, toDate: workDate))),
              "own correction filters include both dates, state and actor and reject invalid ranges");
        await Run(s => s.DecideAsync(manager, nextDayRequest, false, "rejected for test"));
        var datedDecisions = JsonSerializer.SerializeToElement(await Run(s => s.ListDecisionsAsync(manager, 0, fromDate: workDate, toDate: workDate)));
        var datedDecisionItems = datedDecisions.GetProperty("items").EnumerateArray().ToArray();
        check(datedDecisions.GetProperty("fromDate").GetString() == workDate.ToString("yyyy-MM-dd") &&
              datedDecisionItems.Any(x => x.GetProperty("RequestId").GetGuid() == request) &&
              !datedDecisionItems.Any(x => x.GetProperty("RequestId").GetGuid() == nextDayRequest) &&
              await Reject<ArgumentException>(() => Run(s => s.ListDecisionsAsync(manager, 0, fromDate: workDate))) &&
              await Reject<ArgumentException>(() => Run(s => s.ListDecisionsAsync(manager, 0, fromDate: workDate.AddDays(1), toDate: workDate))),
              "decision history work-date filter is inclusive and rejects invalid ranges");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            (await db.Users.SingleAsync(x => x.Id == manager)).IsActive = false;
            await db.SaveChangesAsync();
        }
        var historyDetails = JsonSerializer.SerializeToElement(await Run(s => s.ListOwnAsync(self, 0)));
        var detailItems = historyDetails.GetProperty("items").EnumerateArray().ToArray();
        var approvedDetail = detailItems.Single(x => x.GetProperty("Id").GetGuid() == request);
        var cancelledDetail = detailItems.Single(x => x.GetProperty("Id").GetGuid() == cancel);
        var rejectedDetail = detailItems.Single(x => x.GetProperty("Id").GetGuid() == nextDayRequest);
        var pendingDetail = detailItems.Single(x => x.GetProperty("Id").GetGuid() == identical);
        check(detailItems.All(x => x.GetProperty("Id").GetGuid() != outside) &&
              approvedDetail.GetProperty("PolicyName").GetString() == "synthetic single approver" &&
              approvedDetail.GetProperty("ApproverUserId").GetGuid() == manager &&
              approvedDetail.GetProperty("ApproverDisplayName").ValueKind == JsonValueKind.Null &&
              approvedDetail.GetProperty("DecisionKind").GetInt32() == 1 && approvedDetail.GetProperty("DecisionActorUserId").GetGuid() == manager &&
              approvedDetail.GetProperty("DecisionReason").GetString() == "checked" && approvedDetail.GetProperty("DecisionAtUtc").ValueKind == JsonValueKind.String &&
              rejectedDetail.GetProperty("DecisionKind").GetInt32() == 2 && rejectedDetail.GetProperty("DecisionActorUserId").GetGuid() == manager &&
              rejectedDetail.GetProperty("DecisionReason").GetString() == "rejected for test" &&
              cancelledDetail.GetProperty("DecisionKind").GetInt32() == 3 && cancelledDetail.GetProperty("DecisionActorUserId").GetGuid() == self &&
              cancelledDetail.GetProperty("DecisionActorDisplayName").GetString() == "First" &&
              pendingDetail.GetProperty("DecisionKind").ValueKind == JsonValueKind.Null && pendingDetail.GetProperty("DecisionActorUserId").ValueKind == JsonValueKind.Null &&
              pendingDetail.GetProperty("DecisionAtUtc").ValueKind == JsonValueKind.Null &&
              await Reject<UnauthorizedAccessException>(() => Run(s => s.ListOwnAsync(outsider, 0))),
              "own correction tracking shows recorded actor and decision without leaking other requests or losing inactive approvers");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            (await db.Users.SingleAsync(x => x.Id == manager)).IsActive = true;
            await db.SaveChangesAsync();
        }
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
            var appliedSettings = (await db.CorrectionRequests.AsNoTracking().SingleAsync(x => x.Id == pending)).CorrectionSettingsId;
            check(run.ObservedTicks == TimeSpan.FromHours(8).Ticks && run.AnomaliesJson == "[]" && appliedSettings.HasValue &&
                run.InputSnapshotJson.Contains(appliedSettings.Value.ToString(), StringComparison.OrdinalIgnoreCase) &&
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
