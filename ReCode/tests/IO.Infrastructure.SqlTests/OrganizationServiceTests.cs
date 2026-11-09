using IO.Infrastructure.Organization;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class OrganizationServiceTests
{
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        Guid admin; Guid limited;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var administratorRole = await db.Roles.SingleAsync(x => x.Name == "Initial administrator");
            admin = await db.UserRoles.Where(x => x.RoleId == administratorRole.Id).Select(x => x.UserId).SingleAsync();
            limited = await db.Users.Where(x => x.UserName == "web-manager").Select(x => x.Id).SingleAsync();
            var existingPermission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == OrganizationManagement.Manage);
            var permissionId = existingPermission?.Id ?? Guid.NewGuid();
            if (existingPermission is null) db.Permissions.Add(new() { Id = permissionId, Code = OrganizationManagement.Manage });
            db.RolePermissions.Add(new() { RoleId = administratorRole.Id, PermissionId = permissionId });
            await db.SaveChangesAsync();
        }
        async Task<T> Execute<T>(Func<OrganizationManagement, Task<T>> action)
        {
            await using var scope = provider.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<OrganizationManagement>());
        }
        async Task<bool> Reject<T>(Func<Task> action) where T : Exception
        {
            try { await action(); return false; } catch (T) { return true; }
        }
        check(await Reject<UnauthorizedAccessException>(() => Execute(s => s.CreateOrganizationAsync(limited, "Unauthorized"))),
            "organization write denies actor without explicit permission");
        var org = await Execute(s => s.CreateOrganizationAsync(admin, "Synthetic organization for phase five"));
        var company = await Execute(s => s.CreateCompanyAsync(admin, org, "Synthetic company"));
        var otherCompany = await Execute(s => s.CreateCompanyAsync(admin, org, "Other synthetic company"));
        var site = await Execute(s => s.CreateSiteAsync(admin, company, "Synthetic site", "Asia/Tehran"));
        var unit = await Execute(s => s.CreateUnitAsync(admin, company, null, "First unit"));
        var secondUnit = await Execute(s => s.CreateUnitAsync(admin, company, unit, "Second unit"));
        var otherUnit = await Execute(s => s.CreateUnitAsync(admin, otherCompany, null, "Foreign unit"));
        var employee = await Execute(s => s.CreateEmployeeAsync(admin, company, "ORG-SYN-1", "Synthetic employee"));
        check(org != Guid.Empty && company != Guid.Empty && site != Guid.Empty && employee != Guid.Empty,
            "organization hierarchy, site and employee created");
        check(await Reject<ArgumentException>(() => Execute(s => s.CreateSiteAsync(admin, company, "Invalid", "Not/AZone"))) &&
              await Reject<ArgumentException>(() => Execute(s => s.CreateUnitAsync(admin, company, otherUnit, "Invalid parent"))),
            "invalid site time zone and cross-company parent denied");
        byte[] employeeVersion; byte[] rootVersion; byte[] childVersion;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            employeeVersion = (await db.Employees.SingleAsync(x => x.Id == employee)).Version;
            rootVersion = (await db.OrgUnits.SingleAsync(x => x.Id == unit)).Version;
            childVersion = (await db.OrgUnits.SingleAsync(x => x.Id == secondUnit)).Version;
        }
        check(await Reject<InvalidOperationException>(() => Execute(s => s.ReparentUnitAsync(admin, company, unit, secondUnit, rootVersion))) &&
              await Reject<ArgumentException>(() => Execute(s => s.ReparentUnitAsync(admin, company, unit, otherUnit, rootVersion))),
            "organizational cycles and cross-company parent changes rejected");
        async Task<bool> TryRename(string name)
        {
            try { return await Execute(s => s.RenameEmployeeAsync(admin, employee, name, employeeVersion)); }
            catch (DomainConflictException) { return false; }
        }
        var renamed = await Task.WhenAll(TryRename("First concurrent name"), TryRename("Second concurrent name"));
        check(renamed.Count(x => x) == 1, "concurrent employee edits reject stale rowversion");
        await Execute(s => s.ReparentUnitAsync(admin, company, secondUnit, null, childVersion));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            rootVersion = (await db.OrgUnits.SingleAsync(x => x.Id == unit)).Version;
            childVersion = (await db.OrgUnits.SingleAsync(x => x.Id == secondUnit)).Version;
        }
        async Task<bool> TryMove(Guid id, Guid parent, byte[] version)
        {
            try { return await Execute(s => s.ReparentUnitAsync(admin, company, id, parent, version)); }
            catch (DomainConflictException) { return false; }
        }
        var moves = await Task.WhenAll(TryMove(unit, secondUnit, rootVersion), TryMove(secondUnit, unit, childVersion));
        check(moves.Count(x => x) == 1, "concurrent opposing unit moves cannot create a cycle");
        var newSite = await Execute(s => s.CreateSiteAsync(admin, company, "Second site", "Asia/Tehran"));
        var foreignSite = await Execute(s => s.CreateSiteAsync(admin, otherCompany, "Foreign site", "Asia/Tehran"));
        var historyStart = new DateOnly(2026, 1, 1);
        check(await Reject<ArgumentException>(() => Execute(s => s.AddSiteAssignmentAsync(admin, employee, foreignSite, historyStart, null))) &&
            await Reject<ArgumentException>(() => Execute(s => s.AddEmploymentProfileAsync(admin, employee, "", null, historyStart, null))),
            "employment history rejects cross-company site and blank status code");
        async Task<Guid?> TrySite()
        {
            try { return await Execute(s => s.AddSiteAssignmentAsync(admin, employee, site, historyStart, null)); }
            catch (DomainConflictException) { return null; }
        }
        async Task<Guid?> TryEmployment()
        {
            try { return await Execute(s => s.AddEmploymentProfileAsync(admin, employee, "synthetic-status", "Synthetic title", historyStart, null)); }
            catch (DomainConflictException) { return null; }
        }
        var siteAttempts = await Task.WhenAll(TrySite(), TrySite());
        var profileAttempts = await Task.WhenAll(TryEmployment(), TryEmployment());
        check(siteAttempts.Count(x => x.HasValue) == 1 && profileAttempts.Count(x => x.HasValue) == 1,
            "concurrent primary site and employment profile writes each allow exactly one");
        var oldSite = siteAttempts.Single(x => x.HasValue)!.Value;
        var oldProfile = profileAttempts.Single(x => x.HasValue)!.Value;
        var effective = new DateOnly(2026, 2, 1);
        var movedSite = await Execute(s => s.MoveSiteAsync(admin, employee, oldSite, newSite, effective));
        var changedProfile = await Execute(s => s.ChangeEmploymentProfileAsync(admin, employee, oldProfile, "another-synthetic-status", null, effective));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var beforeSite = await db.EmployeeSiteAssignments.SingleAsync(x => x.Id == oldSite);
            var afterSite = await db.EmployeeSiteAssignments.SingleAsync(x => x.Id == movedSite);
            var beforeProfile = await db.EmploymentProfiles.SingleAsync(x => x.Id == oldProfile);
            var afterProfile = await db.EmploymentProfiles.SingleAsync(x => x.Id == changedProfile);
            check(beforeSite.ValidUntilExclusive == effective && afterSite.ValidFrom == effective && afterSite.SiteId == newSite &&
                beforeProfile.ValidUntilExclusive == effective && afterProfile.ValidFrom == effective && afterProfile.StatusCode == "another-synthetic-status",
                "site and employment changes preserve half-open history atomically");
        }
        check(await Reject<DomainConflictException>(() => Execute(s => s.MoveSiteAsync(admin, employee, oldSite, site, effective.AddDays(1)))) &&
              await Reject<DomainConflictException>(() => Execute(s => s.ChangeEmploymentProfileAsync(admin, employee, oldProfile, "status", null, effective.AddDays(1)))),
            "closed site and employment history cannot be replaced again");
        var jan = new DateOnly(2026, 1, 1); var feb = new DateOnly(2026, 2, 1); var march = new DateOnly(2026, 3, 1);
        await Execute(s => s.AddAssignmentAsync(admin, employee, unit, jan, feb));
        await Execute(s => s.AddAssignmentAsync(admin, employee, secondUnit, feb, march));
        check(await Reject<InvalidOperationException>(() => Execute(s => s.AddAssignmentAsync(admin, employee, unit, jan, march))) &&
              await Reject<ArgumentException>(() => Execute(s => s.AddAssignmentAsync(admin, employee, otherUnit, march, null))),
            "adjacent assignments allowed; overlaps and cross-company assignments denied");
        async Task<Guid?> TryInsert()
        {
            try { return await Execute(s => s.AddAssignmentAsync(admin, employee, unit, new DateOnly(2027, 1, 1), null)); }
            catch (InvalidOperationException e) when (e.Message == "Overlapping primary assignment.") { return null; }
        }
        var results = await Task.WhenAll(TryInsert(), TryInsert());
        check(results.Count(x => x.HasValue) == 1, "concurrent primary assignments serialize with exactly one insert");
        var existing = results.Single(x => x.HasValue)!.Value;
        var transferred = await Execute(s => s.TransferAsync(admin, employee, existing, secondUnit, new DateOnly(2027, 2, 1)));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var previous = await db.EmployeeAssignments.SingleAsync(x => x.Id == existing);
            var next = await db.EmployeeAssignments.SingleAsync(x => x.Id == transferred);
            var audit = scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>();
            check(previous.ValidUntilExclusive == new DateOnly(2027, 2, 1) && next.ValidFrom == previous.ValidUntilExclusive &&
                  next.OrgUnitId == secondUnit && next.ValidUntilExclusive is null &&
                  await audit.SecurityAudit.AnyAsync(x => x.Action == "org.transfer." + existing.ToString("N") + ":" + transferred.ToString("N")),
                "transfer closes previous interval atomically and records audit");
        }
        check(await Reject<InvalidOperationException>(() => Execute(s => s.TransferAsync(admin, employee, existing, unit, new DateOnly(2027, 3, 1)))),
            "completed assignment cannot be transferred again");
    }
}
