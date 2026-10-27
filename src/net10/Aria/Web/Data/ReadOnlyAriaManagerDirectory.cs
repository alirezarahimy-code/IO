using System.Data;
using System.Data.Common;

namespace Aria.Web.Data;

/// <summary>
/// An UNWIRED Aria-only, read-only single-badge manager lookup. The pinned
/// TWF 4.28-packaged T5V3.DB original returned true only for synthetic
/// [dbo].[UserPermission] AccessType=0 + AccessLevel=1 in an isolated LocalDB
/// 2025 matrix. This is NOT SQL Server 2016, production role admission, or
/// independent evidence about the T5Web Admin original.
/// </summary>
internal sealed class ReadOnlyAriaManagerDirectory
{
    internal const string ManagerSql = """
        SELECT TOP (2) [BadgeNumber], [AccessType], [AccessLevel]
        FROM [dbo].[UserPermission]
        WHERE [BadgeNumber] = @BadgeNumber AND [AccessType] = 0 AND [AccessLevel] = 1;
        """;

    private readonly Func<DbConnection> _t5Factory;

    internal ReadOnlyAriaManagerDirectory(Func<DbConnection> t5Factory) =>
        _t5Factory = t5Factory ?? throw new ArgumentNullException(nameof(t5Factory));

    internal async Task<AriaManagerDecision> CheckAsync(long verifiedPersonnelBadge,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (verifiedPersonnelBadge <= 0) return AriaManagerDecision.Denied;
        try
        {
            using DbConnection connection = _t5Factory()
                ?? throw new InvalidOperationException("Missing T5 connection");
            await connection.OpenAsync(cancellationToken);
            if (connection.State != ConnectionState.Open)
                throw new DataException("T5 connection did not open");
            using DbCommand command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = ManagerSql;
            DbParameter badge = command.CreateParameter();
            badge.ParameterName = "@BadgeNumber";
            badge.DbType = DbType.Int64;
            badge.Value = verifiedPersonnelBadge;
            command.Parameters.Add(badge);
            using DbDataReader rows = await command.ExecuteReaderAsync(CommandBehavior.SingleResult,
                cancellationToken);
            if (!await rows.ReadAsync(cancellationToken)) return AriaManagerDecision.Regular;
            if (rows["BadgeNumber"] is not long found || found != verifiedPersonnelBadge ||
                rows["AccessType"] is not int type || type != 0 ||
                rows["AccessLevel"] is not int level || level != 1 ||
                await rows.ReadAsync(cancellationToken))
                return AriaManagerDecision.Denied;
            return AriaManagerDecision.Manager;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Connection details can contain private server/credential text.
            return AriaManagerDecision.Unavailable;
        }
    }
}

internal enum AriaManagerDecision { Unavailable, Denied, Regular, Manager }
