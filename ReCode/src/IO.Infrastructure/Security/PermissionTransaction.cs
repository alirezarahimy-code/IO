using System.Data;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;

namespace IO.Infrastructure.Security;

/// <summary>Shared unit of work for explicit, server-side permission checks and audit.</summary>
public sealed class PermissionTransaction(LoginIdentityDbContext identity, IoDbContext db)
{
    public Task<T> ExecuteAsync<T>(Guid actor, string permission, string action,
        Func<CancellationToken, Task<(T Result, Guid Target)>> operation, CancellationToken ct = default) =>
        ExecuteEitherAsync(actor, permission, permission, action, operation, ct);

    // Only callers with server-defined permission constants may use this for
    // backwards-compatible access during a permission split.
    public async Task<T> ExecuteEitherAsync<T>(Guid actor, string permission, string alternative, string action,
        Func<CancellationToken, Task<(T Result, Guid Target)>> operation, CancellationToken ct = default)
    {
        try
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
                                        ((EF.Functions.Collate(p.Code, "Latin1_General_100_BIN2") == permission &&
                                          EF.Functions.DataLength(p.Code) == permission.Length * 2) ||
                                         (EF.Functions.Collate(p.Code, "Latin1_General_100_BIN2") == alternative &&
                                          EF.Functions.DataLength(p.Code) == alternative.Length * 2))
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
        catch (DbUpdateConcurrencyException)
        {
            // The write matched no rowversion; no audit or partial data may commit.
            throw new DomainConflictException("The record changed; refresh and retry.");
        }
        catch (Exception error) when (IsRetryableConcurrency(error))
        {
            // SQL Server chose this transaction as the deadlock victim or the
            // application lock was not obtained. The transaction was rolled back;
            // callers may retry deliberately, never infer that it committed.
            throw new DomainConflictException("Concurrent operation conflicted; retry the complete request.");
        }
    }

    // Must be used inside the same Execute transaction as the protected operation.
    public Task<bool> HasPermissionAsync(Guid actor, string permission, CancellationToken ct) =>
        (from user in db.Users
         join ur in db.UserRoles on user.Id equals ur.UserId
         join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
         join p in db.Permissions on rp.PermissionId equals p.Id
         where user.Id == actor && user.IsActive &&
               EF.Functions.Collate(p.Code, "Latin1_General_100_BIN2") == permission &&
               EF.Functions.DataLength(p.Code) == permission.Length * 2
         select user.Id).AnyAsync(ct);

    private static bool IsRetryableConcurrency(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is SqlException sql && sql.Number is 1205 or 51002) return true;
        return false;
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
