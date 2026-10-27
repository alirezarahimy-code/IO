using System.Data;
using System.Data.Common;
using Aria.Web.Security;

namespace Aria.Web.Data;

/// <summary>
/// UNWIRED, provider-agnostic, read-only bounded day snapshot from the Aria
/// T5 catalog. Only the 8 non-sensitive column names/types of dbo.Request are
/// checked against the pinned TWF 4.28 T5V3.DB.dll EDMX. No original GetAll,
/// filtering, workflow, authorization, attendance or SQL Server 2016 parity is
/// claimed. The published host does NOT supply a connection factory or route.
/// </summary>
internal sealed class ReadOnlyAriaRequestDirectory
{
    // The original SSDL spells WofkflowType this way. Never "fix" it silently.
    // No description, geo-coordinates, image, accepter, or password columns.
    internal const string RequestSql = """
        SELECT TOP (101) [RequestID], [BadgeNumber], [WofkflowType], [ForDate],
            [FromTime], [ToTime], [IsSuspend], [IsDaily]
        FROM [dbo].[Request]
        WHERE [BadgeNumber] = @BadgeNumber AND [ForDate] >= @DayStart
            AND [ForDate] < @DayEnd
        ORDER BY [ForDate], [RequestID];
        """;

    private readonly Func<DbConnection> _t5Factory;

    internal ReadOnlyAriaRequestDirectory(Func<DbConnection> t5Factory) =>
        _t5Factory = t5Factory ?? throw new ArgumentNullException(nameof(t5Factory));

    internal async Task<AriaRequestSnapshot> ListDayAsync(
        VerifiedAriaAccount account, DateTime day, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // SQL datetime has no timezone and starts at 1753-01-01. Reject
        // unverified aliases and unsupported time domains without opening SQL.
        if (account == null || account.BadgeNumber <= 0 ||
            string.IsNullOrWhiteSpace(account.AccountName) || account.AccountName.Length > 100 ||
            account.AccountName != account.AccountName.Trim() ||
            account.AccountName.Any(char.IsControl) ||
            day.Kind != DateTimeKind.Unspecified || day.TimeOfDay != TimeSpan.Zero ||
            day < new DateTime(1753, 1, 1) || day >= new DateTime(9999, 12, 31))
            return AriaRequestSnapshot.Denied();

        try
        {
            using DbConnection connection = _t5Factory()
                ?? throw new InvalidOperationException("Missing T5 connection");
            await connection.OpenAsync(cancellationToken);
            if (connection.State != ConnectionState.Open)
                throw new DataException("T5 connection did not open");
            using DbCommand command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandTimeout = 5;
            command.CommandText = RequestSql;
            AddParameter(command, "@BadgeNumber", DbType.Int64, account.BadgeNumber);
            AddParameter(command, "@DayStart", DbType.DateTime, day);
            AddParameter(command, "@DayEnd", DbType.DateTime, day.AddDays(1));
            using DbDataReader rows = await command.ExecuteReaderAsync(CommandBehavior.SingleResult,
                cancellationToken);
            var snapshot = new List<AriaRequestHeader>();
            var identifiers = new HashSet<long>();
            while (await rows.ReadAsync(cancellationToken))
            {
                // Never return a truncated list of a person's requests or
                // a partial result after a malformed/foreign row.
                if (snapshot.Count >= 100 ||
                    rows["RequestID"] is not long id || id <= 0 || !identifiers.Add(id) ||
                    rows["BadgeNumber"] is not long badge || badge != account.BadgeNumber ||
                    rows["WofkflowType"] is not int workflow ||
                    rows["ForDate"] is not DateTime forDate ||
                    forDate.Kind != DateTimeKind.Unspecified ||
                    forDate < day || forDate >= day.AddDays(1) ||
                    rows["FromTime"] is not DateTime start || start.Kind != DateTimeKind.Unspecified ||
                    rows["ToTime"] is not DateTime end || end.Kind != DateTimeKind.Unspecified ||
                    rows["IsSuspend"] is not bool suspended || rows["IsDaily"] is not bool daily)
                    return AriaRequestSnapshot.Unavailable();
                snapshot.Add(new AriaRequestHeader(id, badge, workflow, forDate,
                    start, end, suspended, daily));
            }
            return AriaRequestSnapshot.Available(snapshot.AsReadOnly());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Driver messages can contain private SQL endpoints/credentials.
            return AriaRequestSnapshot.Unavailable();
        }
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

internal enum AriaRequestSnapshotState { Unavailable, Denied, Available }

internal sealed record AriaRequestHeader(long RequestId, long BadgeNumber, int WorkflowTypeValue,
    DateTime ForDate, DateTime FromTime, DateTime ToTime, bool IsSuspend, bool IsDaily);

internal sealed record AriaRequestSnapshot(AriaRequestSnapshotState State,
    IReadOnlyList<AriaRequestHeader> Headers)
{
    internal static AriaRequestSnapshot Unavailable() =>
        new(AriaRequestSnapshotState.Unavailable, Array.Empty<AriaRequestHeader>());
    internal static AriaRequestSnapshot Denied() =>
        new(AriaRequestSnapshotState.Denied, Array.Empty<AriaRequestHeader>());
    internal static AriaRequestSnapshot Available(IReadOnlyList<AriaRequestHeader> headers) =>
        new(AriaRequestSnapshotState.Available, headers);
}
