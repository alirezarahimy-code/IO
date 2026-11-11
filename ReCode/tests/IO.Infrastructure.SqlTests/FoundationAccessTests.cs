using IO.Infrastructure.Security;
using IO.Infrastructure.Organization;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class FoundationAccessTests
{
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        Guid actor;
        await using (var s = provider.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<IoDbContext>();
            actor = await (from ur in db.UserRoles join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
                join p in db.Permissions on rp.PermissionId equals p.Id where p.Code == "identity.manage" select ur.UserId).FirstAsync();
        }
        async Task<T> Use<T>(Func<IServiceProvider, Task<T>> fn)
        { await using var s = provider.CreateAsyncScope(); return await fn(s.ServiceProvider); }
        async Task<bool> Denied(Func<Task> fn)
        { try { await fn(); return false; } catch (UnauthorizedAccessException) { return true; } }
        var result = await Use(p => p.GetRequiredService<IdentityAdministration>().CreateTemplatesAsync(actor));
        check(result is not null, "foundation explicit creation of role templates succeeds");
        var before = await Use(p => p.GetRequiredService<IoDbContext>().UserRoles.CountAsync());
        await Use(p => p.GetRequiredService<IdentityAdministration>().CreateTemplatesAsync(actor));
        var templateCount = await Use(p => p.GetRequiredService<IoDbContext>().Roles.CountAsync(r => PermissionCatalog.Templates.Select(t => t.Id).Contains(r.Id)));
        check(templateCount == 5 && before == await Use(p => p.GetRequiredService<IoDbContext>().UserRoles.CountAsync()), "foundation template repeat creates no duplicates or automatic user grants");
        var role = PermissionCatalog.Templates.Single(t => t.Key == "executive");
        await Use(p => p.GetRequiredService<IdentityAdministration>().RevokePermissionAsync(actor, role.Id, "executive.dashboard.read"));
        await Use(p => p.GetRequiredService<IdentityAdministration>().CreateTemplatesAsync(actor));
        var remained = await Use(p => p.GetRequiredService<IoDbContext>().RolePermissions.AnyAsync(r => r.RoleId == role.Id));
        check(!remained, "foundation templates never restore a revoked grant");
        // Regrant through the public administration service, not by direct test mutation.
        await Use(p => p.GetRequiredService<IdentityAdministration>().GrantPermissionAsync(actor, role.Id, "executive.dashboard.read"));
        var reader = Guid.NewGuid(); Guid readerRole;
        await using (var s = provider.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<IoDbContext>();
            db.Users.Add(new UserRecord { Id = reader, UserName = "foundation-reader", IsActive = true }); await db.SaveChangesAsync();
        }
        readerRole = await Use(p => p.GetRequiredService<IdentityAdministration>().CreateRoleAsync(actor, "foundation read-only"));
        await Use(p => p.GetRequiredService<IdentityAdministration>().GrantUserRoleAsync(actor, reader, readerRole));
        await Use(p => p.GetRequiredService<IdentityAdministration>().GrantPermissionAsync(actor, readerRole, "organization.read"));
        var page = await Use(p => p.GetRequiredService<OrganizationManagement>().ListAsync(reader, "companies", 0));
        check(page is not null, "foundation granular organization read can list data");
        check(await Denied(async () => { await Use(p => p.GetRequiredService<OrganizationManagement>().CreateOrganizationAsync(reader, "must not create")); }), "foundation granular read cannot write organization data");
        await Use(p => p.GetRequiredService<IdentityAdministration>().RevokePermissionAsync(actor, readerRole, "organization.read"));
        check(await Denied(async () => { await Use(p => p.GetRequiredService<OrganizationManagement>().ListAsync(reader, "companies", 0)); }), "foundation permission revoke is effective in next service request");
        await Use(p => p.GetRequiredService<IdentityAdministration>().GrantPermissionAsync(actor, readerRole, "identity.audit.read"));
        check(await Use(p => p.GetRequiredService<IdentityAdministration>().ListAsync(reader, "audit", 0)) is not null &&
            await Denied(async () => { await Use(p => p.GetRequiredService<IdentityAdministration>().CreateRoleAsync(reader, "forbidden")); }), "foundation audit-only permission cannot manage roles");
        // Test the exact server catalogue and the three previously omitted permissions with grant/revoke.
        foreach (var code in new[] { "correction.edit.self", "leave.request.submit", "leave.request.decide" })
        {
            await Use(p => p.GetRequiredService<IdentityAdministration>().GrantPermissionAsync(actor, readerRole, code));
            await Use(p => p.GetRequiredService<IdentityAdministration>().RevokePermissionAsync(actor, readerRole, code));
        }
        check(true, "foundation formerly omitted permissions support real grant and revoke");
        var unknownDenied = false;
        try { await Use(p => p.GetRequiredService<IdentityAdministration>().GrantPermissionAsync(actor, readerRole, "device.future.manage")); }
        catch (ArgumentException) { unknownDenied = true; }
        check(unknownDenied, "foundation unimplemented device permission cannot be granted");
        await Use(p => p.GetRequiredService<IdentityAdministration>().GrantPermissionAsync(actor, readerRole, "executive.dashboard.read"));
        var beforeCodes = await Use(p => WorkspaceCatalog.EffectiveAsync(reader, p.GetRequiredService<IoDbContext>(), default));
        await Use(p => p.GetRequiredService<IdentityAdministration>().RevokeUserRoleAsync(actor, reader, readerRole));
        var afterCodes = await Use(p => WorkspaceCatalog.EffectiveAsync(reader, p.GetRequiredService<IoDbContext>(), default));
        check(beforeCodes.Contains("executive.dashboard.read") && afterCodes.Count == 0, "foundation exact menu permissions reflect role revocation without cached grants");
    }
}
