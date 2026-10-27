using System.Data;
using System.Data.Common;
using Aria.Web.Security;

namespace Aria.Web.Data;

/// <summary>
/// UNWIRED single-shared-password boundary for Aria ONLY. The original
/// TWF 4.28 CheckUserPass performed a Personnel SQL equality and returned a
/// badge on a two-catalog synthetic LocalDB fixture. This port has NO net10 SQL
/// provider, POST, HTTP session, original SQL2016/HTTP parity or production DI.
/// Never use its presence as a claim of operational authentication.
/// </summary>
internal sealed class ReadOnlyAriaCredentialDirectory
{
    internal const string CredentialSql = """
        SELECT TOP (2) [BadgeNumber], [UserName], [WebUser], [FiredDate], [ValidFrom], [ValidTo]
        FROM [dbo].[UserInfo]
        WHERE [UserName] = @UserName AND [Password] = @Password;
        """;

    private readonly Func<DbConnection> _personnelFactory;
    private readonly TimeProvider _clock;

    internal ReadOnlyAriaCredentialDirectory(Func<DbConnection> personnelFactory, TimeProvider? clock = null)
    {
        _personnelFactory = personnelFactory ?? throw new ArgumentNullException(nameof(personnelFactory));
        _clock = clock ?? TimeProvider.System;
    }

    internal async Task<AriaCredentialResolution> VerifyAsync(string? account, string? password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The SHA-pinned original EDMX says UserName nvarchar(100), Password
        // nvarchar(250). Never truncate a submitted credential or normalize its
        // password silently. Other original collation behaviour still requires
        // a real SQL Server 2016 fixture before activating login.
        if (string.IsNullOrWhiteSpace(account) || account.Length > 100 ||
            account != account.Trim() || account.Any(char.IsControl) ||
            string.IsNullOrEmpty(password) || password.Length > 250 ||
            password.Any(char.IsControl))
            return AriaCredentialResolution.Denied();
        try
        {
            using DbConnection connection = _personnelFactory()
                ?? throw new InvalidOperationException("Missing Personnel connection");
            await connection.OpenAsync(cancellationToken);
            if (connection.State != ConnectionState.Open)
                throw new DataException("Personnel connection did not open");
            using DbCommand command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = CredentialSql;
            Add(command, "@UserName", account, 100);
            Add(command, "@Password", password, 250);
            using DbDataReader rows = await command.ExecuteReaderAsync(CommandBehavior.SingleResult,
                cancellationToken);
            if (!await rows.ReadAsync(cancellationToken)) return AriaCredentialResolution.Denied();
            if (rows["BadgeNumber"] is not long badge || badge <= 0 ||
                rows["UserName"] is not string foundName ||
                !string.Equals(foundName, account, StringComparison.OrdinalIgnoreCase) ||
                rows["WebUser"] is not bool webUser || !webUser ||
                !rows.IsDBNull(rows.GetOrdinal("FiredDate")) ||
                rows["ValidFrom"] is not DateTime from ||
                rows["ValidTo"] is not DateTime until)
                return AriaCredentialResolution.Denied();
            // Original true-flag rejects future/expired dates. The new policy
            // ALSO rejects former/non-web/null rows even when the sampled
            // original admitted them; no simulated row grants production access.
            DateTime now = _clock.GetLocalNow().DateTime;
            if (from > now || until < now || from > until ||
                await rows.ReadAsync(cancellationToken))
                return AriaCredentialResolution.Denied();
            return AriaCredentialResolution.Verified(new VerifiedAriaAccount(foundName, badge));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Never log a password, connection string, SQL error detail, or row.
            return AriaCredentialResolution.Unavailable();
        }
    }

    private static void Add(DbCommand command, string name, string value, int maxSize)
    {
        DbParameter item = command.CreateParameter();
        item.ParameterName = name;
        item.DbType = DbType.String;
        item.Size = maxSize;
        item.Value = value;
        command.Parameters.Add(item);
    }
}

internal enum AriaCredentialState { Unavailable, Denied, Verified }

internal sealed record AriaCredentialResolution(AriaCredentialState State, VerifiedAriaAccount? Account)
{
    internal static AriaCredentialResolution Unavailable() => new(AriaCredentialState.Unavailable, null);
    internal static AriaCredentialResolution Denied() => new(AriaCredentialState.Denied, null);
    internal static AriaCredentialResolution Verified(VerifiedAriaAccount account) =>
        new(AriaCredentialState.Verified, account);
}
