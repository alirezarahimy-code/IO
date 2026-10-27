using System.Data;
using System.Data.Common;
using AriaAdmin.Web.Security;
using T5Web.Domain;
using T5Web.Domain.Models;

namespace AriaAdmin.Web.Data;

/// <summary>
/// Admin-only, provider-neutral read seam for the current T5Web rewrite's
/// UserInfo (Personnel catalog) and T5WebAccess (T5 catalog). NEVER registered
/// in the production DI container: the offline SQL provider, original Admin
/// authorization golden, schema and role checks have not been approved.
/// </summary>
internal sealed class ReadOnlySqlDirectory : IAdminDirectory
{
    // Explicit projections: never fetch UserInfo.Password or private contact data.
    // Fixed identifiers and bound values; no cross-catalog JOIN or write operation.
    internal const string PersonSql = """
        SELECT TOP (2) [BadgeNumber], [FirstName], [LastName], [FiredDate], [WebUser]
        FROM [UserInfo]
        WHERE [UserName] = @UserName;
        """;
    internal const string AccessSql = """
        SELECT [BadgeNumber], [Feature], [Level]
        FROM [T5WebAccess]
        WHERE [BadgeNumber] = @BadgeNumber;
        """;

    private readonly Func<DbConnection> _personnelFactory;
    private readonly Func<DbConnection> _t5Factory;

    internal ReadOnlySqlDirectory(Func<DbConnection> personnelFactory, Func<DbConnection> t5Factory)
    {
        _personnelFactory = personnelFactory ?? throw new ArgumentNullException(nameof(personnelFactory));
        _t5Factory = t5Factory ?? throw new ArgumentNullException(nameof(t5Factory));
    }

    public async Task<DirectoryResolution> ResolveAsync(string windowsName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string login = Normalize(windowsName);
        if (login.Length == 0) return DirectoryResolution.Denied();

        try
        {
            Person? person = await FindPersonAsync(login, cancellationToken);
            if (person is null || person.BadgeNumber <= 0 || !person.IsWebUser || person.IsFormer)
                return DirectoryResolution.Denied();

            var session = new AdminSession
            {
                BadgeNumber = person.BadgeNumber,
                DisplayName = (person.FirstName + " " + person.LastName).Trim(),
                IssuedUtc = DateTime.UtcNow
            };
            var rowCount = 0;
            using (DbConnection connection = OpenNew(_t5Factory))
            {
                await connection.OpenAsync(cancellationToken);
                CheckOpen(connection);
                using DbCommand command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = AccessSql;
                AddParameter(command, "@BadgeNumber", DbType.Int64, person.BadgeNumber);
                using DbDataReader rows = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken);
                while (await rows.ReadAsync(cancellationToken))
                {
                    // An unbounded permission result is not a valid identity snapshot.
                    if (++rowCount > 512) throw new DataException("Excess permission rows");
                    if (ReadLong(rows["BadgeNumber"]) != person.BadgeNumber)
                        throw new DataException("Cross-user permission row");
                    int feature = ReadInt(rows["Feature"]);
                    int level = ReadInt(rows["Level"]);
                    if (!Enum.IsDefined(typeof(AdminFeature), feature) ||
                        !Enum.IsDefined(typeof(AccessLevel), level))
                        throw new DataException("Unknown permission value");
                    // AdminSession.Grant in the current T5Web rewrite makes Deny
                    // win over Allow regardless of row ordering.
                    session.Grant((AdminFeature)feature, (AccessLevel)level);
                }
            }
            // A Deny-only row cannot turn a user into an authenticated operator.
            if (rowCount == 0 || !session.Levels.Values.Any(level => level != AccessLevel.Deny))
                return DirectoryResolution.Denied();
            return DirectoryResolution.Allowed(session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A failed/misconfigured DB is not an unknown user or an allow.
            // Exception messages may contain server names or connection strings.
            return DirectoryResolution.Unavailable();
        }
    }

    private async Task<Person?> FindPersonAsync(string login, CancellationToken cancellationToken)
    {
        using DbConnection connection = OpenNew(_personnelFactory);
        await connection.OpenAsync(cancellationToken);
        CheckOpen(connection);
        using DbCommand command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = PersonSql;
        DbParameter userName = command.CreateParameter();
        userName.ParameterName = "@UserName";
        userName.DbType = DbType.String;
        userName.Size = 128;
        userName.Value = login;
        command.Parameters.Add(userName);
        using DbDataReader rows = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken);
        if (!await rows.ReadAsync(cancellationToken)) return null;
        var person = new Person(
            ReadLong(rows["BadgeNumber"]),
            rows["FirstName"] is DBNull ? "" : rows.GetString(rows.GetOrdinal("FirstName")),
            rows["LastName"] is DBNull ? "" : rows.GetString(rows.GetOrdinal("LastName")),
            rows.GetBoolean(rows.GetOrdinal("WebUser")),
            !rows.IsDBNull(rows.GetOrdinal("FiredDate")));
        // The current rewrite picks an arbitrary TOP 1 on duplicate UserName.
        // This mandatory safety difference denies ambiguous identities instead.
        if (await rows.ReadAsync(cancellationToken)) return null;
        return person;
    }

    private static DbConnection OpenNew(Func<DbConnection> factory) =>
        factory() ?? throw new InvalidOperationException("Missing catalog factory result");

    private static void CheckOpen(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Catalog did not open");
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static long ReadLong(object value) => value switch
    {
        long number => number,
        int number => number,
        _ => throw new DataException("Invalid badge type")
    };

    private static int ReadInt(object value) => value switch
    {
        int number => number,
        short number => number,
        byte number => number,
        _ => throw new DataException("Invalid permission type")
    };

    private static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Any(char.IsControl)) return "";
        string name = raw.Trim();
        int slash = name.LastIndexOf('\\');
        if (slash >= 0) name = name[(slash + 1)..];
        int at = name.IndexOf('@');
        if (at >= 0)
        {
            if (at == 0 || at == name.Length - 1 || name.IndexOf('@', at + 1) >= 0) return "";
            name = name[..at];
        }
        name = name.Trim();
        // The SQL parameter cannot inject SQL, but a malformed/ambiguous
        // Windows name is still not a safe organizational identity.
        if (name.Length is 0 or > 128 || name.Any(char.IsControl) ||
            name.Contains('\\') || name.Contains('@') || name.Contains('/')) return "";
        return name;
    }

    private sealed record Person(long BadgeNumber, string FirstName, string LastName, bool IsWebUser, bool IsFormer);
}
