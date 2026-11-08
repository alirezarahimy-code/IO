using System.Data;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IO.Infrastructure.Security;

/// <summary>Experimental global administration; caller must supply authenticated actor ID.</summary>
public sealed class IdentityAdministration(LoginIdentityDbContext identity, IoDbContext foundation, UserManager<LoginAccount> users)
{
    // Authorization and writes happen in the same serializable transaction.
    // A role name alone is never a grant of the management permission.
    private async Task<T> ExecuteAsync<T>(Guid actor, string action, Func<Task<(T Result, Guid Target)>> operation,
        CancellationToken cancellationToken)
    {
        await using var tx = await identity.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await foundation.Database.CloseConnectionAsync();
        foundation.Database.SetDbConnection(identity.Database.GetDbConnection());
        await foundation.Database.UseTransactionAsync(tx.GetDbTransaction(), cancellationToken);
        var permission = InitialAdministrator.ManageIdentity;
        var allowed = await (from user in foundation.Users
                             join ur in foundation.UserRoles on user.Id equals ur.UserId
                             join rp in foundation.RolePermissions on ur.RoleId equals rp.RoleId
                             join p in foundation.Permissions on rp.PermissionId equals p.Id
                             where user.Id == actor && user.IsActive &&
                                 EF.Functions.Collate(p.Code, "Latin1_General_100_BIN2") == permission &&
                                 EF.Functions.DataLength(p.Code) == permission.Length * 2
                             select user.Id).AnyAsync(cancellationToken);
        if (!allowed) throw new UnauthorizedAccessException();
        var (result, target) = await operation();
        identity.SecurityAudit.Add(new SecurityAuditRecord { Id = Guid.NewGuid(), ActorUserId = actor,
            Action = action + ":" + target.ToString("N"), OccurredAtUtc = DateTimeOffset.UtcNow });
        await foundation.SaveChangesAsync(cancellationToken);
        await identity.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return result;
    }

    public Task<Guid> CreateUserAsync(Guid actor, string username, string password, Guid? employeeId, CancellationToken ct = default) =>
        ExecuteAsync(actor, "identity.user.created", async () =>
        {
            if (string.IsNullOrWhiteSpace(username) || username.Length > 200 || string.IsNullOrEmpty(password) || password.Length > 256 || employeeId == Guid.Empty)
                throw new ArgumentException("Invalid user input.");
            if (employeeId is not null && !await foundation.Employees.AnyAsync(e => e.Id == employeeId, ct))
                throw new ArgumentException("Unknown employee.");
            var id = Guid.NewGuid();
            var result = await users.CreateAsync(new LoginAccount { Id = id, UserName = username, LockoutEnabled = true }, password);
            if (!result.Succeeded) throw new ArgumentException("Username or password rejected by identity validation.");
            foundation.Users.Add(new UserRecord { Id = id, UserName = username, EmployeeId = employeeId, IsActive = true });
            return (id, id);
        }, ct);

    public Task<Guid> CreateRoleAsync(Guid actor, string name, CancellationToken ct = default) =>
        ExecuteAsync(actor, "identity.role.created", () =>
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 150) throw new ArgumentException("Invalid role name.");
            var id = Guid.NewGuid();
            foundation.Roles.Add(new RoleRecord { Id = id, Name = name });
            return Task.FromResult((id, id));
        }, ct);

    public Task<bool> GrantUserRoleAsync(Guid actor, Guid userId, Guid roleId, CancellationToken ct = default) =>
        ExecuteAsync(actor, "identity.user-role.granted", async () =>
        {
            if (!await foundation.Users.AnyAsync(x => x.Id == userId, ct) || !await foundation.Roles.AnyAsync(x => x.Id == roleId, ct) ||
                await foundation.UserRoles.AnyAsync(x => x.UserId == userId && x.RoleId == roleId, ct))
                throw new ArgumentException("Invalid or existing user-role grant.");
            foundation.UserRoles.Add(new UserRoleRecord { UserId = userId, RoleId = roleId });
            return (true, userId);
        }, ct);

    public Task<bool> GrantPermissionAsync(Guid actor, Guid roleId, string code, CancellationToken ct = default) =>
        ExecuteAsync(actor, "identity.role-permission.granted", async () =>
        {
            if (code is not (EmployeeAccessReader.ReadSelf or EmployeeAccessReader.ReadScoped or EmployeeAccessReader.ReadAll or InitialAdministrator.ManageIdentity) ||
                !await foundation.Roles.AnyAsync(x => x.Id == roleId, ct)) throw new ArgumentException("Invalid permission or role.");
            var permission = await foundation.Permissions.SingleOrDefaultAsync(p =>
                EF.Functions.Collate(p.Code, "Latin1_General_100_BIN2") == code && EF.Functions.DataLength(p.Code) == code.Length * 2, ct);
            if (permission is null)
            {
                permission = new PermissionRecord { Id = Guid.NewGuid(), Code = code };
                foundation.Permissions.Add(permission);
            }
            if (await foundation.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == permission.Id, ct))
                throw new ArgumentException("Permission already granted.");
            foundation.RolePermissions.Add(new RolePermissionRecord { RoleId = roleId, PermissionId = permission.Id });
            return (true, roleId);
        }, ct);

    public Task<bool> GrantScopeAsync(Guid actor, Guid userId, Guid unitId, DateOnly from, DateOnly? until, CancellationToken ct = default) =>
        ExecuteAsync(actor, "identity.user-scope.granted", async () =>
        {
            if (until is not null && until <= from || !await foundation.Users.AnyAsync(x => x.Id == userId, ct) ||
                !await foundation.OrgUnits.AnyAsync(x => x.Id == unitId, ct)) throw new ArgumentException("Invalid scope.");
            foundation.UserOrgScopes.Add(new UserOrgScopeRecord { Id = Guid.NewGuid(), UserId = userId, OrgUnitId = unitId,
                ValidFrom = from, ValidUntilExclusive = until });
            return (true, userId);
        }, ct);

    public Task<bool> DisableUserAsync(Guid actor, Guid userId, CancellationToken ct = default) =>
        ExecuteAsync(actor, "identity.user.disabled", async () =>
        {
            if (actor == userId) throw new ArgumentException("Cannot disable your own account.");
            var user = await foundation.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
            if (user is null || !user.IsActive) throw new ArgumentException("Unknown or inactive user.");
            user.IsActive = false;
            return (true, userId);
        }, ct);
}
