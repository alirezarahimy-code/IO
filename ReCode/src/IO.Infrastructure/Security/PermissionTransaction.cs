using System.Data;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IO.Infrastructure.Security;

/// <summary>Shared unit of work for explicit, server-side permission checks and audit.</summary>
public sealed class PermissionTransaction(LoginIdentityDbContext identity, IoDbContext db)
{
    public async Task<T> ExecuteAsync<T>(Guid actor, string permission, string action,
        Func<CancellationToken, Task<(T Result, Guid Target)>> operation, CancellationToken ct = default)
    {
        await using var transaction = await identity.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.Database.CloseConnectionAsync();
        db.Database.SetDbConnection(identity.Database.GetDbConnection());
        await db.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        var authorized = await (from user in db.Users
                                join ur in db.UserRoles on user.Id equals ur.UserId
                                join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
                                join p in db.Permissions on rp.PermissionId equals p.Id
                                where user.Id == actor && user.IsActive &&
                                    EF.Functions.Collate(p.Code, "Latin1_General_100_BIN2") == permission &&
                                    EF.Functions.DataLength(p.Code) == permission.Length * 2
                                select user.Id).AnyAsync(ct);
        if (!authorized) throw new UnauthorizedAccessException();
        var (result, target) = await operation(ct);
        identity.SecurityAudit.Add(new SecurityAuditRecord { Id = Guid.NewGuid(), ActorUserId = actor,
            Action = action + ":" + target.ToString("N"), OccurredAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        await identity.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    /// <summary>Serializes cooperating SQL transactions for one logical resource.</summary>
    public async Task LockAsync(string resource, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=15000;
            IF @result < 0 THROW 51002, 'Unable to acquire application lock.', 1;
            """, ct);
    }
}
