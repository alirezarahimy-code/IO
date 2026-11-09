using System.Data;
using System.Globalization;
using IO.Infrastructure.Storage;
using IO.Infrastructure.Organization;
using IO.Infrastructure.Attendance;
using IO.Infrastructure.Workflows;
using IO.Infrastructure.Periods;
using IO.Infrastructure.Calculation;
using IO.Infrastructure.Scheduling;
using IO.Infrastructure.Reports;
using IO.Infrastructure.Leaves;
using IO.Infrastructure.Delegation;
using IO.Infrastructure.Settings;
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
        await RequireManagementAsync(actor, cancellationToken);
        var (result, target) = await operation();
        identity.SecurityAudit.Add(new SecurityAuditRecord { Id = Guid.NewGuid(), ActorUserId = actor,
            Action = action + ":" + target.ToString("N"), OccurredAtUtc = DateTimeOffset.UtcNow });
        await foundation.SaveChangesAsync(cancellationToken);
        await identity.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return result;
    }

    private async Task RequireManagementAsync(Guid actor, CancellationToken cancellationToken)
    {
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
    }

    /// <summary>Returns one bounded page of non-credential administration data.</summary>
    public async Task<object> ListAsync(Guid actor, string kind, int offset, CancellationToken ct = default)
    {
        if (offset < 0 || offset > 1_000_000) throw new ArgumentException("Invalid page offset.");
        await using var tx = await identity.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await foundation.Database.CloseConnectionAsync();
        foundation.Database.SetDbConnection(identity.Database.GetDbConnection());
        await foundation.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
        await RequireManagementAsync(actor, ct);
        const int pageSize = 100;
        object page = kind switch
        {
            "audit" => await identity.SecurityAudit.AsNoTracking().OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id)
                .Skip(offset).Take(pageSize).Select(x => new { x.Id, x.ActorUserId, x.Action, x.OccurredAtUtc }).ToListAsync(ct),
            "org-units" => await foundation.OrgUnits.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(pageSize)
                .Select(x => new { x.Id, x.CompanyId, x.Name }).ToListAsync(ct),
            "users" => await foundation.Users.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(pageSize)
                .Select(x => new { x.Id, x.UserName, x.EmployeeId, x.IsActive }).ToListAsync(ct),
            "roles" => await foundation.Roles.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(pageSize)
                .Select(x => new { x.Id, x.Name }).ToListAsync(ct),
            "permissions" => await foundation.Permissions.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(pageSize)
                .Select(x => new { x.Id, x.Code }).ToListAsync(ct),
            "user-roles" => await foundation.UserRoles.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.RoleId)
                .Skip(offset).Take(pageSize).Select(x => new { x.UserId, x.RoleId }).ToListAsync(ct),
            "role-permissions" => await foundation.RolePermissions.AsNoTracking().OrderBy(x => x.RoleId).ThenBy(x => x.PermissionId)
                .Skip(offset).Take(pageSize).Select(x => new { x.RoleId, x.PermissionId }).ToListAsync(ct),
            "user-scopes" => await foundation.UserOrgScopes.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(pageSize)
                .Select(x => new { x.Id, x.UserId, x.OrgUnitId, x.ValidFrom, x.ValidUntilExclusive }).ToListAsync(ct),
            _ => throw new ArgumentException("Unknown catalog type.")
        };
        if (kind == "audit")
        {
            identity.SecurityAudit.Add(new SecurityAuditRecord { Id = Guid.NewGuid(), ActorUserId = actor,
                Action = "identity.audit.viewed:" + actor.ToString("N"), OccurredAtUtc = DateTimeOffset.UtcNow });
            await identity.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new { kind, offset, pageSize, items = page };
    }

    public Task<Guid> CreateUserAsync(Guid actor, string username, string password, Guid? employeeId, CancellationToken ct = default) =>
        ExecuteAsync(actor, "identity.user.created", async () =>
        {
            if (string.IsNullOrWhiteSpace(username) || username.Length > 200 || string.IsNullOrEmpty(password) || password.Length > 256 || employeeId == Guid.Empty)
                throw new ArgumentException("Invalid user input.");
            if (employeeId is not null && !await foundation.Employees.AnyAsync(e => e.Id == employeeId, ct))
                throw new ArgumentException("Unknown employee.");
            var id = Guid.NewGuid();
            var login = new LoginAccount { Id = id, UserName = username, LockoutEnabled = true };
            var result = await users.CreateAsync(login, password);
            if (!result.Succeeded) throw new ArgumentException("Username or password rejected by identity validation.");
            var claimResult = await users.AddClaimAsync(login, new System.Security.Claims.Claim(IdentityServices.MustChangePasswordClaim, "1"));
            if (!claimResult.Succeeded) throw new InvalidOperationException("Unable to enforce initial password change.");
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
        ExecuteAsync(actor, "role-grant." + roleId.ToString("N"), async () =>
        {
            if (!await foundation.Users.AnyAsync(x => x.Id == userId, ct) || !await foundation.Roles.AnyAsync(x => x.Id == roleId, ct) ||
                await foundation.UserRoles.AnyAsync(x => x.UserId == userId && x.RoleId == roleId, ct))
                throw new ArgumentException("Invalid or existing user-role grant.");
            foundation.UserRoles.Add(new UserRoleRecord { UserId = userId, RoleId = roleId });
            return (true, userId);
        }, ct);

    public Task<bool> GrantPermissionAsync(Guid actor, Guid roleId, string code, CancellationToken ct = default) =>
        ExecuteAsync(actor, "permission-grant." + code, async () =>
        {
            if (code is not (EmployeeAccessReader.ReadSelf or EmployeeAccessReader.ReadScoped or EmployeeAccessReader.ReadAll or InitialAdministrator.ManageIdentity or OrganizationManagement.Manage or AttendanceIngestion.Manage or PersonalAttendance.ReadSelf or TeamAttendance.Read or TeamAttendance.Scoped or TeamAttendance.All or PersonalReports.ReadSelf or ExecutiveOverview.Read or LeaveLedger.Manage or LeaveLedger.ReadSelf or DelegationRegistry.ManageSelf or DelegationRegistry.Receive or CorrectionSettings.Manage or ObservedCalculation.Manage or CorrectionWorkflow.Configure or CorrectionWorkflow.Submit or CorrectionWorkflow.ReadOwn or CorrectionWorkflow.ReadInbox or CorrectionWorkflow.Decide or CorrectionWorkflow.DecideScoped or CorrectionWorkflow.DecideAll or PeriodPreflight.Read or PeriodDrafts.Manage or PeriodDiagnostics.Manage or ScheduleManagement.Manage or ScheduleReader.ReadSelf or ScheduleReader.ReadScoped or ScheduleReader.ReadAll) ||
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

    public Task<Guid> GrantScopeAsync(Guid actor, Guid userId, Guid unitId, DateOnly from, DateOnly? until, CancellationToken ct = default) =>
        ExecuteAsync(actor, "scope-grant." + unitId.ToString("N") + "." + from.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "." +
            (until?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "open"), async () =>
        {
            if (until is not null && until <= from || !await foundation.Users.AnyAsync(x => x.Id == userId, ct) ||
                !await foundation.OrgUnits.AnyAsync(x => x.Id == unitId, ct)) throw new ArgumentException("Invalid scope.");
            var scopeId = Guid.NewGuid();
            foundation.UserOrgScopes.Add(new UserOrgScopeRecord { Id = scopeId, UserId = userId, OrgUnitId = unitId,
                ValidFrom = from, ValidUntilExclusive = until });
            return (scopeId, userId);
        }, ct);

    public Task<bool> RevokeUserRoleAsync(Guid actor, Guid userId, Guid roleId, CancellationToken ct = default) =>
        ExecuteAsync(actor, "role-revoke." + roleId.ToString("N"), async () =>
        {
            // Never remove the current actor's own management path through this API.
            if (actor == userId) throw new ArgumentException("Cannot revoke your own role.");
            var grant = await foundation.UserRoles.SingleOrDefaultAsync(x => x.UserId == userId && x.RoleId == roleId, ct)
                ?? throw new ArgumentException("Unknown user-role grant.");
            foundation.UserRoles.Remove(grant);
            return (true, userId);
        }, ct);

    public Task<bool> RevokePermissionAsync(Guid actor, Guid roleId, string code, CancellationToken ct = default) =>
        ExecuteAsync(actor, "permission-revoke." + code, async () =>
        {
            if (code is not (EmployeeAccessReader.ReadSelf or EmployeeAccessReader.ReadScoped or EmployeeAccessReader.ReadAll or InitialAdministrator.ManageIdentity or OrganizationManagement.Manage or AttendanceIngestion.Manage or PersonalAttendance.ReadSelf or TeamAttendance.Read or TeamAttendance.Scoped or TeamAttendance.All or PersonalReports.ReadSelf or ExecutiveOverview.Read or LeaveLedger.Manage or LeaveLedger.ReadSelf or DelegationRegistry.ManageSelf or DelegationRegistry.Receive or CorrectionSettings.Manage or ObservedCalculation.Manage or CorrectionWorkflow.Configure or CorrectionWorkflow.Submit or CorrectionWorkflow.ReadOwn or CorrectionWorkflow.ReadInbox or CorrectionWorkflow.Decide or CorrectionWorkflow.DecideScoped or CorrectionWorkflow.DecideAll or PeriodPreflight.Read or PeriodDrafts.Manage or PeriodDiagnostics.Manage or ScheduleManagement.Manage or ScheduleReader.ReadSelf or ScheduleReader.ReadScoped or ScheduleReader.ReadAll) ||
                await foundation.UserRoles.AnyAsync(x => x.UserId == actor && x.RoleId == roleId, ct))
                throw new ArgumentException("Invalid permission or role used by the actor.");
            var permissionId = await foundation.Permissions.Where(p =>
                EF.Functions.Collate(p.Code, "Latin1_General_100_BIN2") == code && EF.Functions.DataLength(p.Code) == code.Length * 2)
                .Select(p => p.Id).SingleOrDefaultAsync(ct);
            var grant = await foundation.RolePermissions.SingleOrDefaultAsync(x => x.RoleId == roleId && x.PermissionId == permissionId, ct)
                ?? throw new ArgumentException("Unknown role-permission grant.");
            foundation.RolePermissions.Remove(grant);
            return (true, roleId);
        }, ct);

    public Task<bool> RevokeScopeAsync(Guid actor, Guid scopeId, CancellationToken ct = default) =>
        ExecuteAsync(actor, "scope-revoke." + scopeId.ToString("N"), async () =>
        {
            var scope = await foundation.UserOrgScopes.SingleOrDefaultAsync(x => x.Id == scopeId, ct)
                ?? throw new ArgumentException("Unknown scope.");
            foundation.UserOrgScopes.Remove(scope);
            return (true, scope.UserId);
        }, ct);

    public Task<bool> DisableUserAsync(Guid actor, Guid userId, CancellationToken ct = default) =>
        ExecuteAsync(actor, "identity.user.disabled", async () =>
        {
            if (actor == userId) throw new ArgumentException("Cannot disable your own account.");
            var user = await foundation.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
            if (user is null || !user.IsActive) throw new ArgumentException("Unknown or inactive user.");
            await RotateStampIfLoginExistsAsync(userId);
            user.IsActive = false;
            return (true, userId);
        }, ct);

    public Task<bool> EnableUserAsync(Guid actor, Guid userId, CancellationToken ct = default) =>
        ExecuteAsync(actor, "identity.user.enabled", async () =>
        {
            var user = await foundation.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
            if (user is null || user.IsActive) throw new ArgumentException("Unknown or already active user.");
            await RotateStampIfLoginExistsAsync(userId);
            user.IsActive = true;
            return (true, userId);
        }, ct);

    private async Task RotateStampIfLoginExistsAsync(Guid userId)
    {
        // The stamp must rotate even when a disabled account is later enabled:
        // otherwise a cookie that was never presented during disablement revives.
        var login = await users.FindByIdAsync(userId.ToString());
        if (login is not null && !(await users.UpdateSecurityStampAsync(login)).Succeeded)
            throw new InvalidOperationException("Unable to invalidate account sessions.");
    }
}
