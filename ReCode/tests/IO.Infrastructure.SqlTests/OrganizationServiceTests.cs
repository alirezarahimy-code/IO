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
            var permissionId = Guid.NewGuid();
            db.Permissions.Add(new() { Id = permissionId, Code = OrganizationManagement.Manage });
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
