using System.Data;
using System.Data.Common;
using Aria.Web.Security;

namespace Aria.Web.Data;

/// <summary>
/// Aria-only, read-only recheck of a previously VERIFIED shared account in the
/// Personnel catalog. The current TWF rewrite's UserInfo/WebUser/FiredDate
/// fields are a starting point, NOT original TWF 4.28 authentication parity.
/// No SQL provider/factory is registered in publishable DI. In particular this
/// code cannot verify a password or confer a T5 managerial role.
/// </summary>
internal sealed class ReadOnlyPersonnelDirectory : IAriaPersonnelDirectory
{
    // Bind both values from the SAME verified account. Never retrieve Password,
    // NationalNo or contact data, and never interpolate caller input into SQL.
    internal const string PersonSql = """
        SELECT TOP (2) [BadgeNumber], [UserName], [WebUser], [FiredDate]
        FROM [UserInfo]
        WHERE [BadgeNumber] = @BadgeNumber AND [UserName] = @UserName;
        """;

    private readonly Func<DbConnection> _personnelFactory;

    internal ReadOnlyPersonnelDirectory(Func<DbConnection> personnelFactory) =>
        _personnelFactory = personnelFactory ?? throw new ArgumentNullException(nameof(personnelFactory));

    public async Task<PersonnelResolution> RecheckAsync(
        VerifiedAriaAccount verifiedAccount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Until the original product's domain/UPN normalization is measured,
        // reject ambiguous names instead of silently trusting an alias.
        if (verifiedAccount is null || verifiedAccount.BadgeNumber <= 0 ||
            string.IsNullOrWhiteSpace(verifiedAccount.AccountName) ||
            verifiedAccount.AccountName.Length > 128 ||
            verifiedAccount.AccountName != verifiedAccount.AccountName.Trim() ||
            verifiedAccount.AccountName.Any(char.IsControl))
            return PersonnelResolution.Denied();

        try
        {
            using DbConnection connection = _personnelFactory()
                ?? throw new InvalidOperationException("Missing Personnel connection");
            await connection.OpenAsync(cancellationToken);
            if (connection.State != ConnectionState.Open)
                throw new DataException("Personnel connection did not open");
            using DbCommand command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = PersonSql;
            AddParameter(command, "@BadgeNumber", DbType.Int64, verifiedAccount.BadgeNumber);
            var login = command.CreateParameter();
            login.ParameterName = "@UserName";
            login.DbType = DbType.String;
            login.Size = 128;
            login.Value = verifiedAccount.AccountName;
            command.Parameters.Add(login);
            using DbDataReader rows = await command.ExecuteReaderAsync(CommandBehavior.SingleResult,
                cancellationToken);
            if (!await rows.ReadAsync(cancellationToken))
                return PersonnelResolution.Denied();
            // The query caps rows at two; a second row means the identity is
            // ambiguous even if the current TWF rewrite would choose TOP 1.
            long badge = ReadBadge(rows["BadgeNumber"]);
            string userName = rows["UserName"] as string
                ?? throw new DataException("Missing Personnel user name");
            bool webUser = rows["WebUser"] is bool enabled
                ? enabled : throw new DataException("Invalid Personnel access flag");
            bool former = !rows.IsDBNull(rows.GetOrdinal("FiredDate"));
            bool duplicate = await rows.ReadAsync(cancellationToken);
            if (duplicate || badge != verifiedAccount.BadgeNumber ||
                !string.Equals(userName, verifiedAccount.AccountName,
                    StringComparison.OrdinalIgnoreCase) || !webUser || former)
                return PersonnelResolution.Denied();
            return PersonnelResolution.Active(new VerifiedAriaAccount(userName, badge));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Driver failures may include server names or connection strings.
            return PersonnelResolution.Unavailable();
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

    private static long ReadBadge(object value) => value switch
    {
        long badge => badge,
        int badge => badge,
        _ => throw new DataException("Invalid Personnel badge")
    };
}
