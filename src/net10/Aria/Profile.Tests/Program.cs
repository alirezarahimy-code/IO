using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Text.Json;
using Aria.Web.Data;
using Aria.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TWF.Domain.Models;
using WebHost = Aria.Web.Program;

if ((args.Length != 1 && (args.Length != 3 ||
        (args[1] != "--original-personnel-schema" && args[1] != "--original-aria-manager"))) ||
    !File.Exists(Path.Combine(args[0], "Aria.Web.csproj")) ||
    (args.Length == 3 && !File.Exists(args[2])))
    throw new ArgumentException("Pass Aria Web and optionally a same-run original schema OR manager fixture.");
string webRoot = Path.GetFullPath(args[0]);
string? originalSchemaFixture = args.Length == 3 && args[1] == "--original-personnel-schema" ? args[2] : null;
string? originalManagerFixture = args.Length == 3 && args[1] == "--original-aria-manager" ? args[2] : null;
var passed = 0;
var failed = 0;

async Task Run(string label, Func<Task> test)
{
    try { await test(); Console.WriteLine("[PASS] " + label); passed++; }
    catch (Exception error) { Console.Error.WriteLine("[FAIL] " + label + ": " + error); failed++; }
}
static void Require(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
}
static DataTable People(params (long Badge, string Name, bool WebUser, bool Former)[] entries)
{
    var data = new DataTable();
    data.Columns.Add("BadgeNumber", typeof(long));
    data.Columns.Add("UserName", typeof(string));
    data.Columns.Add("WebUser", typeof(bool));
    data.Columns.Add("FiredDate", typeof(DateTime));
    data.Columns.Add("ValidFrom", typeof(DateTime));
    data.Columns.Add("ValidTo", typeof(DateTime));
    foreach (var (badge, name, webUser, former) in entries)
        data.Rows.Add(badge, name, webUser,
            former ? (object)new DateTime(2020, 1, 1) : DBNull.Value,
            new DateTime(2000, 1, 1), new DateTime(2050, 1, 1));
    return data;
}
static DataTable Permissions(params (long Badge, int Type, int Level)[] entries)
{
    var data = new DataTable();
    data.Columns.Add("BadgeNumber", typeof(long));
    data.Columns.Add("AccessType", typeof(int));
    data.Columns.Add("AccessLevel", typeof(int));
    foreach (var (badge, type, level) in entries)
        data.Rows.Add(badge, type, level);
    return data;
}

await Run("AR-PROFILE-01: bound Personnel-only SELECT for verified account", async () =>
{
    var connection = new TableConnection("Personnel", People((101, "fixture-user", true, false)));
    var directory = new ReadOnlyPersonnelDirectory(() => connection);
    var result = await directory.RecheckAsync(new VerifiedAriaAccount("fixture-user", 101),
        CancellationToken.None);
    Require(result.State == PersonnelState.Active && result.Person?.AccountName == "fixture-user" &&
        result.Person.BadgeNumber == 101, "active read-only row should match BOTH verified values");
    var command = connection.Commands.Single();
    Require(connection.WasDisposed && command.WasDisposed &&
        command.CommandText == ReadOnlyPersonnelDirectory.PersonSql &&
        command.CommandType == CommandType.Text &&
        command.CommandText.Contains("SELECT TOP (2)", StringComparison.Ordinal) &&
        !command.CommandText.Contains("Password", StringComparison.OrdinalIgnoreCase) &&
        !command.CommandText.Contains("NationalNo", StringComparison.OrdinalIgnoreCase) &&
        command.Parameters.Count == 2 &&
        command.Parameters["@BadgeNumber"].DbType == DbType.Int64 &&
        command.Parameters["@BadgeNumber"].Value is long badge && badge == 101 &&
        command.Parameters["@UserName"].DbType == DbType.String &&
        command.Parameters["@UserName"].Size == 100 &&
        command.Parameters["@UserName"].Value is string login && login == "fixture-user",
        "only a fixed projection and bound account parameters, no credential columns");
});

await Run("AR-PROFILE-02: invalid account refuses to open Personnel", async () =>
{
    var opened = 0;
    var directory = new ReadOnlyPersonnelDirectory(() =>
    {
        opened++;
        return new TableConnection("Personnel", People());
    });
    var names = new[] { "", " ", " fixture-user", "fixture-user\r\n", new string('x', 101) };
    foreach (var name in names)
        Require((await directory.RecheckAsync(new VerifiedAriaAccount(name, 101),
            CancellationToken.None)).State == PersonnelState.Denied, "invalid login: " + name);
    foreach (long badge in new long[] { 0, -1 })
        Require((await directory.RecheckAsync(new VerifiedAriaAccount("fixture-user", badge),
            CancellationToken.None)).State == PersonnelState.Denied, "nonpositive badge");
    Require((await directory.RecheckAsync(null!, CancellationToken.None)).State == PersonnelState.Denied &&
        opened == 0, "bad identity must not create a connection");
    string maxOriginalName = new('u', 100);
    var bounded = new TableConnection("Personnel", People((101, maxOriginalName, true, false)));
    var accepted = await new ReadOnlyPersonnelDirectory(() => bounded)
        .RecheckAsync(new VerifiedAriaAccount(maxOriginalName, 101), CancellationToken.None);
    Require(accepted.State == PersonnelState.Active &&
        bounded.Commands.Single().Parameters["@UserName"].Size == 100,
        "EDMX nvarchar(100) boundary is accepted without truncating the parameter");
});

await Run("AR-PROFILE-03: wrong badge/name, absent and duplicate rows are denied", async () =>
{
    foreach (var (rows, reason) in new[]
    {
        (People(), "missing"),
        (People((102, "fixture-user", true, false)), "wrong badge"),
        (People((101, "fixture-foreign", true, false)), "wrong name"),
        (People((101, "fixture-user", true, false), (101, "fixture-user", true, false)), "ambiguous")
    })
    {
        var connection = new TableConnection("Personnel", rows);
        var decision = await new ReadOnlyPersonnelDirectory(() => connection)
            .RecheckAsync(new VerifiedAriaAccount("fixture-user", 101), CancellationToken.None);
        Require(decision.State == PersonnelState.Denied && decision.Person == null &&
            connection.WasDisposed, "deny " + reason + " without leaking a connection");
    }
});

await Run("AR-PROFILE-04: disabled and former user cannot reuse a session", async () =>
{
    foreach (var (enabled, former) in new[] { (false, false), (true, true), (false, true) })
    {
        var decision = await new ReadOnlyPersonnelDirectory(() =>
            new TableConnection("Personnel", People((101, "fixture-user", enabled, former))))
            .RecheckAsync(new VerifiedAriaAccount("fixture-user", 101), CancellationToken.None);
        Require(decision.State == PersonnelState.Denied, "WebUser/FiredDate status must deny");
    }
});

await Run("AR-PROFILE-10: validity window follows own original true-flag golden; null fails closed", async () =>
{
    var now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    var clock = new ProfileClock(new DateTimeOffset(now));
    async Task<PersonnelState> At(DateTime? from, DateTime? until)
    {
        var rows = People((101, "fixture-user", true, false));
        rows.Rows[0]["ValidFrom"] = from.HasValue ? (object)from.Value : DBNull.Value;
        rows.Rows[0]["ValidTo"] = until.HasValue ? (object)until.Value : DBNull.Value;
        var connection = new TableConnection("Personnel", rows);
        var result = await new ReadOnlyPersonnelDirectory(() => connection, clock)
            .RecheckAsync(new VerifiedAriaAccount("fixture-user", 101), CancellationToken.None);
        Require(connection.WasDisposed && connection.Commands.Single().WasDisposed,
            "dated read is bounded and closes the connection");
        return result.State;
    }
    DateTime start = new(2000, 1, 1);
    DateTime end = new(2050, 1, 1);
    Require(await At(start, end) == PersonnelState.Active, "unexpired synthetic dates");
    Require(await At(now.AddDays(1), end) == PersonnelState.Denied, "future-dated account");
    Require(await At(start, now.AddDays(-1)) == PersonnelState.Denied, "expired account");
    Require(await At(null, end) == PersonnelState.Denied &&
            await At(start, null) == PersonnelState.Denied, "unknown dates fail closed");
    Require(await At(end, start) == PersonnelState.Denied, "inverted window fails closed");
    Require(await At(now, now) == PersonnelState.Active, "fixed-clock inclusive boundary (not original parity)");
    var broken = People((101, "fixture-user", true, false));
    broken.Columns.Remove("ValidFrom");
    var bad = new TableConnection("Personnel", broken);
    var unavailable = await new ReadOnlyPersonnelDirectory(() => bad, clock)
        .RecheckAsync(new VerifiedAriaAccount("fixture-user", 101), CancellationToken.None);
    Require(unavailable.State == PersonnelState.Unavailable && bad.WasDisposed,
        "missing SQL column never becomes access");
});

await Run("AR-PROFILE-05: driver/factory errors never turn into user access", async () =>
{
    foreach (bool failOnOpen in new[] { true, false })
    {
        var connection = new TableConnection("Personnel", People((101, "fixture-user", true, false)));
        if (failOnOpen)
            connection.OpenFailure = new InvalidOperationException("PRIVATE_CONNECTION_MARKER");
        else
            connection.ReadFailure = new InvalidOperationException("PRIVATE_CONNECTION_MARKER");
        var result = await new ReadOnlyPersonnelDirectory(() => connection)
            .RecheckAsync(new VerifiedAriaAccount("fixture-user", 101), CancellationToken.None);
        Require(result.State == PersonnelState.Unavailable && result.Person == null &&
            connection.WasDisposed, "generic unavailable response, disposed resource");
    }
    var noFactory = new ReadOnlyPersonnelDirectory(() => throw new InvalidOperationException("PRIVATE_CONNECTION_MARKER"));
    Require((await noFactory.RecheckAsync(new VerifiedAriaAccount("fixture-user", 101),
        CancellationToken.None)).State == PersonnelState.Unavailable, "factory failure must not become active");
});

await Run("AR-PROFILE-06: cancellation propagates and closes resources", async () =>
{
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    var factoryCalls = 0;
    var directory = new ReadOnlyPersonnelDirectory(() =>
    {
        factoryCalls++;
        return new TableConnection("Personnel", People());
    });
    try { _ = await directory.RecheckAsync(new VerifiedAriaAccount("fixture-user", 101), cancelled.Token);
        throw new Exception("ignored cancellation"); }
    catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
    Require(factoryCalls == 0, "already cancelled before opening Personnel");
    using var duringOpen = new CancellationTokenSource();
    var connection = new TableConnection("Personnel", People());
    connection.BeforeOpen = duringOpen.Cancel;
    try { _ = await new ReadOnlyPersonnelDirectory(() => connection)
            .RecheckAsync(new VerifiedAriaAccount("fixture-user", 101), duringOpen.Token);
        throw new Exception("swallowed in-flight cancellation"); }
    catch (OperationCanceledException) when (duringOpen.IsCancellationRequested) { }
    Require(connection.WasDisposed, "in-flight cancellation closes connection");
});

await Run("AR-PROFILE-07: adversarial login stays a parameter (fake rows are not authentication)", async () =>
{
    const string adversarial = "fixt'; DROP TABLE [UserInfo];--";
    var connection = new TableConnection("Personnel", People((101, adversarial, true, false)));
    var directory = new ReadOnlyPersonnelDirectory(() => connection);
    var result = await directory.RecheckAsync(new VerifiedAriaAccount(adversarial, 101), CancellationToken.None);
    Require(result.State == PersonnelState.Active, "fake rows can claim active irrespective of SQL WHERE");
    var command = connection.Commands.Single();
    Require(!command.CommandText.Contains(adversarial, StringComparison.Ordinal) &&
        command.Parameters["@UserName"].Value is string name && name == adversarial,
        "never concatenate untrusted login into SQL; NO SQL is executed by fake");
});

await Run("AR-PROFILE-08: test-only Kestrel rechecks one badge before placeholder Home", async () =>
{
    var connections = new ConcurrentBag<TableConnection>();
    await using var app = WebHost.CreateApp(Array.Empty<string>(), services =>
    {
        services.AddSingleton<IAriaSessionVerifier, ProfileTestSessionVerifier>();
        services.AddSingleton<IAriaPersonnelDirectory>(new ReadOnlyPersonnelDirectory(() =>
        {
            var connection = new TableConnection("Personnel",
                People((101, "fixture-user", true, false)));
            connections.Add(connection);
            return connection;
        }));
    }, webRoot);
    using var client = await StartWeb(app);
    using var valid = await Send(client, "/Home/Index", "fixture-user");
    Require(valid.StatusCode == HttpStatusCode.ServiceUnavailable &&
        (await valid.Content.ReadAsStringAsync()).Contains("داشبورد آریا", StringComparison.Ordinal),
        "active synthetic user still reaches only an unavailable dashboard");
    using var foreign = await Send(client, "/Home/Index", "fixture-foreign");
    Require(foreign.StatusCode == HttpStatusCode.Forbidden,
        "wrong verified badge cannot borrow person 101's session");
    using var adminRole = await Send(client, "/Home/Index", "fixture-admin-role");
    Require(adminRole.StatusCode == HttpStatusCode.Forbidden,
        "role='admin' cannot bypass missing T5 manager validation");
    using var anonymous = await Send(client, "/Home/Index", null);
    Require(anonymous.StatusCode == HttpStatusCode.Redirect, "anonymous does not query Personnel");
    Require(connections.Count == 2 && connections.All(connection => connection.WasDisposed) &&
        !valid.Headers.TryGetValues("Set-Cookie", out _) &&
        !foreign.Headers.TryGetValues("Set-Cookie", out _),
        "only verified non-admin test accounts recheck with fresh closed connections; no session cookie");
});

await Run("AR-ROLE-01: Aria original-pair manager lookup is fixed and badge-bound", async () =>
{
    var connection = new TableConnection("T5", Permissions((101, 0, 1)));
    var result = await new ReadOnlyAriaManagerDirectory(() => connection)
        .CheckAsync(101, CancellationToken.None);
    var command = connection.Commands.Single();
    Require(result == AriaManagerDecision.Manager && connection.WasDisposed && command.WasDisposed &&
        command.CommandType == CommandType.Text &&
        command.CommandText == ReadOnlyAriaManagerDirectory.ManagerSql &&
        command.CommandText.Contains("FROM [dbo].[UserPermission]", StringComparison.Ordinal) &&
        command.CommandText.Contains("[AccessType] = 0 AND [AccessLevel] = 1", StringComparison.Ordinal) &&
        !command.CommandText.Contains("Password", StringComparison.OrdinalIgnoreCase) &&
        command.Parameters.Count == 1 && command.Parameters["@BadgeNumber"].DbType == DbType.Int64 &&
        command.Parameters["@BadgeNumber"].Value is long badge && badge == 101,
        "single original-pair role, bounded lookup, no name/credential or second user secret");
});

await Run("AR-ROLE-02: no manager row is regular; mismatches and duplicates deny", async () =>
{
    foreach (var (rows, expected) in new[]
    {
        (Permissions(), AriaManagerDecision.Regular),
        (Permissions((102, 0, 1)), AriaManagerDecision.Denied),
        (Permissions((101, 0, 0)), AriaManagerDecision.Denied),
        (Permissions((101, 1, 1)), AriaManagerDecision.Denied),
        (Permissions((101, 0, 1), (101, 0, 1)), AriaManagerDecision.Denied)
    })
    {
        // The fake does NOT evaluate SQL WHERE; a returned nonmatching row is
        // suspicious and must fail closed rather than grant manager/regular.
        var connection = new TableConnection("T5", rows);
        var decision = await new ReadOnlyAriaManagerDirectory(() => connection)
            .CheckAsync(101, CancellationToken.None);
        Require(decision == expected && connection.WasDisposed, "manager matrix/malformed-row guard");
    }
});

await Run("AR-ROLE-03: invalid badge cannot initiate a T5 connection", async () =>
{
    var calls = 0;
    var directory = new ReadOnlyAriaManagerDirectory(() =>
    {
        calls++;
        return new TableConnection("T5", Permissions());
    });
    Require(await directory.CheckAsync(0, CancellationToken.None) == AriaManagerDecision.Denied &&
            await directory.CheckAsync(-1, CancellationToken.None) == AriaManagerDecision.Denied &&
            calls == 0, "never infer managerial rights from zero/negative badges");
});

await Run("AR-ROLE-04: T5 failure never silently reduces privileged authorization to regular", async () =>
{
    var failing = new TableConnection("T5", Permissions((101, 0, 1)))
        { OpenFailure = new InvalidOperationException("PRIVATE_T5_MARKER") };
    Require(await new ReadOnlyAriaManagerDirectory(() => failing)
            .CheckAsync(101, CancellationToken.None) == AriaManagerDecision.Unavailable &&
        failing.WasDisposed, "broken T5 must not pretend no manager role");
    var missing = new ReadOnlyAriaManagerDirectory(() => throw new InvalidOperationException("PRIVATE_T5_MARKER"));
    Require(await missing.CheckAsync(101, CancellationToken.None) == AriaManagerDecision.Unavailable,
        "factory failure must not grant manager or a regular session");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try { _ = await missing.CheckAsync(101, cancelled.Token); throw new Exception("cancellation swallowed"); }
    catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
});

await Run("AR-CRED-01: one synthetic matched Personnel row binds SAME shared account/password", async () =>
{
    var connection = new TableConnection("Personnel", People((101, "fixture-user", true, false)));
    var result = await new ReadOnlyAriaCredentialDirectory(() => connection)
        .VerifyAsync("fixture-user", "fixture-secret", CancellationToken.None);
    var command = connection.Commands.Single();
    Require(result.State == AriaCredentialState.Verified &&
        result.Account == new VerifiedAriaAccount("fixture-user", 101) &&
        connection.WasDisposed && command.WasDisposed && command.CommandType == CommandType.Text &&
        command.CommandText == ReadOnlyAriaCredentialDirectory.CredentialSql &&
        command.CommandText.Contains("FROM [dbo].[UserInfo]", StringComparison.Ordinal) &&
        command.CommandText.Contains("[Password] = @Password", StringComparison.Ordinal) &&
        !command.CommandText.Contains("fixture-secret", StringComparison.Ordinal) &&
        !command.CommandText.Contains("SELECT *", StringComparison.OrdinalIgnoreCase) &&
        command.Parameters.Count == 2 &&
        command.Parameters["@UserName"].Value is string login && login == "fixture-user" &&
        command.Parameters["@UserName"].Size == 100 &&
        command.Parameters["@Password"].Value is string secret && secret == "fixture-secret" &&
        command.Parameters["@Password"].Size == 250,
        "only bound original Personnel column sizes; NO SQL/real password was checked by fake");
});

await Run("AR-CRED-02: no matched SQL row and invalid credential inputs deny early", async () =>
{
    var empty = new TableConnection("Personnel", People());
    var missing = await new ReadOnlyAriaCredentialDirectory(() => empty)
        .VerifyAsync("fixture-user", "fixture-wrong", CancellationToken.None);
    Require(missing.State == AriaCredentialState.Denied && empty.WasDisposed,
        "fake empty reader stands for SQL no-match, not actual password validation");
    var calls = 0;
    var directory = new ReadOnlyAriaCredentialDirectory(() =>
    {
        calls++;
        return new TableConnection("Personnel", People());
    });
    foreach (var (name, password) in new (string?, string?)[]
    {
        (null, "fixture"), ("", "fixture"), (" user", "fixture"),
        ("user\r", "fixture"), (new string('x', 101), "fixture"),
        ("fixture-user", null), ("fixture-user", ""),
        ("fixture-user", new string('x', 251)), ("fixture-user", "abc\nxyz")
    })
        Require((await directory.VerifyAsync(name, password, CancellationToken.None)).State ==
            AriaCredentialState.Denied, "bad credential form must not open a database");
    Require(calls == 0, "no connection for invalid inputs");
    var max = new TableConnection("Personnel", People((101, new string('n', 100), true, false)));
    Require((await new ReadOnlyAriaCredentialDirectory(() => max)
        .VerifyAsync(new string('n', 100), new string('p', 250), CancellationToken.None)).State ==
        AriaCredentialState.Verified &&
        max.Commands.Single().Parameters["@Password"].Size == 250,
        "original EDMX 100/250 maximum accepted without truncation (fake reader only)");
});

await Run("AR-CRED-03: bad badge, ineligible date, disabled/former and duplicate all deny", async () =>
{
    foreach (var table in new[]
    {
        People((0, "fixture-user", true, false)),
        People((101, "fixture-other", true, false)),
        People((101, "fixture-user", false, false)),
        People((101, "fixture-user", true, true)),
        People((101, "fixture-user", true, false), (101, "fixture-user", true, false))
    })
    {
        var connection = new TableConnection("Personnel", table);
        var result = await new ReadOnlyAriaCredentialDirectory(() => connection)
            .VerifyAsync("fixture-user", "fixture-secret", CancellationToken.None);
        Require(result.State == AriaCredentialState.Denied && result.Account == null &&
            connection.WasDisposed, "malformed/ineligible returned fake row never issues verified badge");
    }
    var clock = new ProfileClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
    foreach (var (from, to) in new[]
    {
        ((object)new DateTime(2026, 11, 1), (object)new DateTime(2050, 1, 1)),
        ((object)new DateTime(2000, 1, 1), (object)new DateTime(2020, 1, 1)),
        ((object)DBNull.Value, (object)new DateTime(2050, 1, 1)),
        ((object)new DateTime(2000, 1, 1), (object)DBNull.Value)
    })
    {
        var data = People((101, "fixture-user", true, false));
        data.Rows[0]["ValidFrom"] = from;
        data.Rows[0]["ValidTo"] = to;
        var result = await new ReadOnlyAriaCredentialDirectory(() =>
            new TableConnection("Personnel", data), clock)
            .VerifyAsync("fixture-user", "fixture-secret", CancellationToken.None);
        Require(result.State == AriaCredentialState.Denied, "date/null status cannot become a new session");
    }
});

await Run("AR-CRED-04: credential driver errors and cancellations never leak a secret", async () =>
{
    var failing = new TableConnection("Personnel", People((101, "fixture-user", true, false)))
        { ReadFailure = new InvalidOperationException("PRIVATE_CREDENTIAL_MARKER") };
    Require((await new ReadOnlyAriaCredentialDirectory(() => failing)
        .VerifyAsync("fixture-user", "fixture-secret", CancellationToken.None)).State ==
        AriaCredentialState.Unavailable && failing.WasDisposed, "SQL failure is not bad-password or success");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try {
        _ = await new ReadOnlyAriaCredentialDirectory(() => throw new Exception("SHOULD_NOT_OPEN"))
            .VerifyAsync("fixture-user", "fixture-secret", cancelled.Token);
        throw new Exception("cancellation swallowed");
    } catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
});

await Run("AR-CRED-05: adversarial password stays a bound parameter, never SQL text", async () =>
{
    const string injection = "x'; DROP TABLE [dbo].[UserInfo];--";
    var connection = new TableConnection("Personnel", People());
    var result = await new ReadOnlyAriaCredentialDirectory(() => connection)
        .VerifyAsync("fixture-user", injection, CancellationToken.None);
    var command = connection.Commands.Single();
    Require(result.State == AriaCredentialState.Denied &&
        !command.CommandText.Contains(injection, StringComparison.Ordinal) &&
        command.Parameters["@Password"].Value is string bound && bound == injection &&
        connection.WasDisposed,
        "binding proof only: fake NEVER executed a SQL Server command or verified a password");
});

await Run("AR-ADMIT-01: same synthetic credential/person badge plus original role pair", async () =>
{
    var touched = new List<TableConnection>();
    TableConnection Open(string catalog, DataTable rows)
    {
        var connection = new TableConnection(catalog, rows);
        touched.Add(connection);
        return connection;
    }
    var service = new AriaSharedAccountAdmission(
        new ReadOnlyAriaCredentialDirectory(() => Open("Personnel", People((101, "fixture-user", true, false)))),
        new ReadOnlyPersonnelDirectory(() => Open("Personnel", People((101, "fixture-user", true, false)))),
        new ReadOnlyAriaManagerDirectory(() => Open("T5", Permissions((101, 0, 1)))));
    var result = await service.AuthenticateAsync("fixture-user", "fixture-secret", CancellationToken.None);
    Require(result.State == AriaAdmissionState.Verified && result.IsManager &&
        result.Account == new VerifiedAriaAccount("fixture-user", 101) &&
        touched.Count == 3 && touched.All(item => item.WasDisposed) &&
        touched.Select(item => item.Database).SequenceEqual(new[] { "Personnel", "Personnel", "T5" }),
        "single synthetic shared account; 3 separate closed reads, no cookie or second user password");
});

await Run("AR-ADMIT-02: same synthetic person without manager row is regular", async () =>
{
    var service = new AriaSharedAccountAdmission(
        new ReadOnlyAriaCredentialDirectory(() =>
            new TableConnection("Personnel", People((101, "fixture-user", true, false)))),
        new ReadOnlyPersonnelDirectory(() =>
            new TableConnection("Personnel", People((101, "fixture-user", true, false)))),
        new ReadOnlyAriaManagerDirectory(() => new TableConnection("T5", Permissions())));
    var result = await service.AuthenticateAsync("fixture-user", "fixture-secret", CancellationToken.None);
    Require(result.State == AriaAdmissionState.Verified && !result.IsManager &&
        result.Account?.BadgeNumber == 101, "a missing permission row never fabricates manager");
});

await Run("AR-ADMIT-03: no matched credential row never queries Personnel recheck or T5", async () =>
{
    var calls = 0;
    var service = new AriaSharedAccountAdmission(
        new ReadOnlyAriaCredentialDirectory(() => new TableConnection("Personnel", People())),
        new ReadOnlyPersonnelDirectory(() => { calls++; return new TableConnection("Personnel", People()); }),
        new ReadOnlyAriaManagerDirectory(() => { calls++; return new TableConnection("T5", Permissions()); }));
    var result = await service.AuthenticateAsync("fixture-user", "fixture-wrong", CancellationToken.None);
    Require(result.State == AriaAdmissionState.Denied && result.Account == null &&
        !result.IsManager && calls == 0,
        "no fake matched SQL row: no role query, no session and no fallback user");
});

await Run("AR-ADMIT-04: stale/mismatched Personnel badge prevents T5 privilege lookup", async () =>
{
    var rolesOpened = 0;
    var service = new AriaSharedAccountAdmission(
        new ReadOnlyAriaCredentialDirectory(() =>
            new TableConnection("Personnel", People((101, "fixture-user", true, false)))),
        new ReadOnlyPersonnelDirectory(() =>
            new TableConnection("Personnel", People((102, "fixture-user", true, false)))),
        new ReadOnlyAriaManagerDirectory(() => {
            rolesOpened++;
            return new TableConnection("T5", Permissions((101, 0, 1)));
        }));
    var result = await service.AuthenticateAsync("fixture-user", "fixture-secret", CancellationToken.None);
    Require(result.State == AriaAdmissionState.Denied && !result.IsManager &&
        result.Account == null && rolesOpened == 0,
        "another person's T5 manager row must not rescue a changed Personnel badge");
});

await Run("AR-ADMIT-05: T5 outage/ambiguous manager cannot fall back to regular", async () =>
{
    AriaSharedAccountAdmission Create(Func<TableConnection> roleFactory) => new(
        new ReadOnlyAriaCredentialDirectory(() =>
            new TableConnection("Personnel", People((101, "fixture-user", true, false)))),
        new ReadOnlyPersonnelDirectory(() =>
            new TableConnection("Personnel", People((101, "fixture-user", true, false)))),
        new ReadOnlyAriaManagerDirectory(roleFactory));
    var outage = await Create(() => new TableConnection("T5", Permissions())
        { OpenFailure = new InvalidOperationException("PRIVATE_T5_MARKER") })
        .AuthenticateAsync("fixture-user", "fixture-secret", CancellationToken.None);
    var duplicate = await Create(() =>
        new TableConnection("T5", Permissions((101, 0, 1), (101, 0, 1))))
        .AuthenticateAsync("fixture-user", "fixture-secret", CancellationToken.None);
    Require(outage.State == AriaAdmissionState.Unavailable &&
        duplicate.State == AriaAdmissionState.Denied &&
        !outage.IsManager && !duplicate.IsManager &&
        outage.Account == null && duplicate.Account == null,
        "never grant a partial account or mistake unavailable T5 for no privilege");
});

await Run("AR-SESSION-01: fresh protected single-account ticket needs separate registry", async () =>
{
    var registry = new SyntheticSessionRegistry();
    var clock = new SyntheticSessionClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
    var manager = new FreshAriaSessionManager(new EphemeralDataProtectionProvider(), registry, clock);
    string? token = await manager.IssueAsync(new VerifiedAriaAccount("fixture-user", 101), CancellationToken.None);
    Require(token != null && token.Length < 4096 &&
        !token.Contains("fixture-user", StringComparison.Ordinal) &&
        !token.Contains("admin", StringComparison.OrdinalIgnoreCase) && registry.Count == 1,
        "only a protected, bounded opaque test token; no roles or plaintext account");
    var verified = await manager.VerifyAsync(token, CancellationToken.None);
    Require(verified.State == FreshAriaSessionState.Verified &&
        verified.Account == new VerifiedAriaAccount("fixture-user", 101) &&
        verified.SessionId is not null,
        "same synthetic name/badge only after authenticated crypto AND active registry");
});

await Run("AR-SESSION-02: tamper/legacy/oversize tokens fail before registry", async () =>
{
    var registry = new SyntheticSessionRegistry();
    var manager = new FreshAriaSessionManager(new EphemeralDataProtectionProvider(), registry,
        new SyntheticSessionClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero)));
    string? token = await manager.IssueAsync(new VerifiedAriaAccount("fixture-user", 101), CancellationToken.None);
    Require(token != null, "test token setup");
    string tampered = (token![0] == 'A' ? "B" : "A") + token[1..];
    foreach (string invalid in new[] { "legacy-mytimeauth-DES", "broken.not-a-ticket", new string('x', 4097), tampered })
    {
        var decision = await manager.VerifyAsync(invalid, CancellationToken.None);
        Require(decision.State == FreshAriaSessionState.Denied && decision.Account == null &&
            decision.SessionId == null, "never accept legacy/altered/oversized bearer token");
    }
    Require(registry.FindCalls == 0, "invalid unprotected bytes never reach persistence");
});

await Run("AR-SESSION-03: revocation makes manually replayed test token unusable", async () =>
{
    var registry = new SyntheticSessionRegistry();
    var manager = new FreshAriaSessionManager(new EphemeralDataProtectionProvider(), registry,
        new SyntheticSessionClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero)));
    var account = new VerifiedAriaAccount("fixture-user", 101);
    string? first = await manager.IssueAsync(account, CancellationToken.None);
    string? second = await manager.IssueAsync(account, CancellationToken.None);
    Require(first != null && second != null && first != second, "unique synthetic session IDs");
    Require(await manager.RevokeAsync(first, CancellationToken.None) == FreshAriaRevokeState.Revoked &&
        (await manager.VerifyAsync(first, CancellationToken.None)).State == FreshAriaSessionState.Denied &&
        await manager.RevokeAsync(first, CancellationToken.None) == FreshAriaRevokeState.Invalid &&
        (await manager.VerifyAsync(second, CancellationToken.None)).State == FreshAriaSessionState.Verified,
        "store-backed revocation vs original DES cookie replay (test-only, no browser/IIS)");
});

await Run("AR-SESSION-04: future issue, exact expiry and another key ring cannot verify", async () =>
{
    var registry = new SyntheticSessionRegistry();
    var keys = new EphemeralDataProtectionProvider();
    var clock = new SyntheticSessionClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
    var manager = new FreshAriaSessionManager(keys, registry, clock);
    string? token = await manager.IssueAsync(new VerifiedAriaAccount("fixture-user", 101), CancellationToken.None);
    Require(token != null, "session issue fixture");
    clock.Advance(TimeSpan.FromTicks(-1));
    Require((await manager.VerifyAsync(token, CancellationToken.None)).State == FreshAriaSessionState.Denied,
        "future-issued token not valid");
    clock.Advance(TimeSpan.FromTicks(1));
    Require((await new FreshAriaSessionManager(new EphemeralDataProtectionProvider(), registry, clock)
        .VerifyAsync(token, CancellationToken.None)).State == FreshAriaSessionState.Denied,
        "unknown key ring cannot silently accept old protected ticket");
    clock.Advance(TimeSpan.FromMinutes(20));
    Require((await manager.VerifyAsync(token, CancellationToken.None)).State == FreshAriaSessionState.Denied,
        "exact 20-minute expiry is denied; shorter current session must be tested separately");
});

await Run("AR-SESSION-05: corrupt identity/storage outage never produces verified access", async () =>
{
    var registry = new SyntheticSessionRegistry();
    var clock = new SyntheticSessionClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
    var manager = new FreshAriaSessionManager(new EphemeralDataProtectionProvider(), registry, clock);
    string? token = await manager.IssueAsync(new VerifiedAriaAccount("fixture-user", 101), CancellationToken.None);
    var initial = await manager.VerifyAsync(token, CancellationToken.None);
    Require(initial.SessionId is not null && initial.State == FreshAriaSessionState.Verified,
        "synthetic store setup");
    registry.Replace(initial.SessionId!.Value, new VerifiedAriaAccount("fixture-other", 102));
    Require((await manager.VerifyAsync(token, CancellationToken.None)).State == FreshAriaSessionState.Denied,
        "wrong current badge/name never inherits a protected ticket");
    registry.FailFind = true;
    Require((await manager.VerifyAsync(token, CancellationToken.None)).State == FreshAriaSessionState.Unavailable &&
        await manager.RevokeAsync(token, CancellationToken.None) == FreshAriaRevokeState.Unavailable,
        "registry outage is not anonymous, ordinary user or successful logout");
    registry.FailCreate = true;
    Require(await manager.IssueAsync(new VerifiedAriaAccount("fixture-user", 101), CancellationToken.None) == null,
        "no ticket from failed persistent write");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try { _ = await manager.VerifyAsync(token, cancelled.Token); throw new Exception("ignored cancellation"); }
    catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
});

await Run("AR-SESSION-06: 100 isolated in-memory tickets are NOT operational load", async () =>
{
    var registry = new SyntheticSessionRegistry();
    var manager = new FreshAriaSessionManager(new EphemeralDataProtectionProvider(), registry,
        new SyntheticSessionClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero)));
    var tasks = Enumerable.Range(1, 100).Select(async number =>
    {
        var account = new VerifiedAriaAccount("fixture-" + number, number);
        string token = await manager.IssueAsync(account, CancellationToken.None)
            ?? throw new Exception("test-only issue failed");
        var before = await manager.VerifyAsync(token, CancellationToken.None);
        var revoked = await manager.RevokeAsync(token, CancellationToken.None);
        var after = await manager.VerifyAsync(token, CancellationToken.None);
        return before.State == FreshAriaSessionState.Verified && before.Account == account &&
            revoked == FreshAriaRevokeState.Revoked && after.State == FreshAriaSessionState.Denied;
    });
    bool[] outcomes = await Task.WhenAll(tasks);
    Require(outcomes.All(value => value) && registry.Count == 100,
        "100 test-only in-process tokens; NOT 100 IIS users, SQL sessions or operational load");
});

if (originalSchemaFixture != null)
{
    await Run("AR-PROFILE-09: exact pinned ORIGINAL EDMX username/table contract (metadata only)", async () =>
    {
        using var document = JsonDocument.Parse(File.ReadAllText(originalSchemaFixture));
        var schema = document.RootElement;
        Require(schema.GetProperty("source_sha256").GetString() ==
                    "468e43ea66b9c591384ddcd033918e6f563a85b4caa2b6e6c4e8b85a4484050f" &&
                schema.GetProperty("scope").GetString() ==
                    "EMBEDDED_EDMX_METADATA_ONLY_NOT_SQL_OR_PASSWORD_GOLDEN" &&
                schema.GetProperty("provider_metadata").GetString() == "System.Data.SqlClient" &&
                schema.GetProperty("table").GetString() == "dbo.UserInfo" &&
                schema.GetProperty("entity_set").GetString() == "UserInfoes" &&
                schema.GetProperty("columns").GetProperty("UserName")
                    .GetProperty("max_length").GetInt32() == 100 &&
                schema.GetProperty("columns").GetProperty("BadgeNumber")
                    .GetProperty("store_type").GetString() == "bigint" &&
                schema.GetProperty("columns").GetProperty("WebUser")
                    .GetProperty("store_type").GetString() == "bit" &&
                schema.GetProperty("columns").GetProperty("Password")
                    .GetProperty("max_length").GetInt32() == 250 &&
                ReadOnlyPersonnelDirectory.PersonSql.Contains("FROM [dbo].[UserInfo]", StringComparison.Ordinal) &&
                !ReadOnlyPersonnelDirectory.PersonSql.Contains("Password", StringComparison.OrdinalIgnoreCase) &&
                ReadOnlyAriaCredentialDirectory.CredentialSql.Contains(
                    "[Password] = @Password", StringComparison.Ordinal),
                "port projection/table must stay within its own original Personnel mapping");
        string longestUser = new('u', 100);
        var connection = new TableConnection("Personnel", People((101, longestUser, true, false)));
        var decision = await new ReadOnlyPersonnelDirectory(() => connection)
            .RecheckAsync(new VerifiedAriaAccount(longestUser, 101), CancellationToken.None);
        Require(decision.State == PersonnelState.Active &&
            connection.Commands.Single().Parameters["@UserName"].Size == 100 &&
            connection.WasDisposed,
            "the original nvarchar(100) limit must be applied in compiled net10 code");
        // This fake reader does not execute a SQL WHERE or check a password;
        // this test is only a schema-bound adapter constraint.
    });
}

if (originalManagerFixture != null)
{
    await Run("AR-ROLE-05: same-run pinned ORIGINAL Aria manager pair to compiled adapter", async () =>
    {
        using var document = JsonDocument.Parse(File.ReadAllText(originalManagerFixture));
        var original = document.RootElement;
        Require(original.GetProperty("source_sha256").GetString() ==
                    "d8f0570b39635e21b883257cf4d6bba1d6011002921ba5227dcd9cdf567b1faf" &&
                original.GetProperty("scope").GetString() == "SYNTHETIC_LOCALDB_2025_ONLY" &&
                original.GetProperty("table").GetString() == "dbo.UserPermission" &&
                original.GetProperty("access_type").GetInt32() == 0 &&
                original.GetProperty("access_level").GetInt32() == 1 &&
                ReadOnlyAriaManagerDirectory.ManagerSql.Contains(
                    "[AccessType] = 0 AND [AccessLevel] = 1", StringComparison.Ordinal),
                "same-run original manager matrix 0/1, never Admin original or SQL2016");
        var connection = new TableConnection("T5", Permissions((910011, 0, 1)));
        Require(await new ReadOnlyAriaManagerDirectory(() => connection)
            .CheckAsync(910011, CancellationToken.None) == AriaManagerDecision.Manager &&
            connection.WasDisposed, "compiled adapter uses original fixture badge (fake reader, not real SQL)");
    });
}

Console.WriteLine($"Summary: {passed} Passed, {failed} Failed (Aria fake Personnel/T5 + Kestrel; original fixtures only with same-run flags)");
return failed == 0 && passed == (originalSchemaFixture is null && originalManagerFixture is null ? 29 : 30) ? 0 : 1;

static async Task<HttpClient> StartWeb(WebApplication app)
{
    app.Urls.Add("http://127.0.0.1:0");
    await app.StartAsync();
    string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?
        .Addresses.Single(value => value.StartsWith("http://127.0.0.1:", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("Synthetic Kestrel did not bind a loopback port");
    return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) };
}
static async Task<HttpResponseMessage> Send(HttpClient client, string path, string? name)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    if (name != null) request.Headers.TryAddWithoutValidation("X-Aria-Profile-Test", name);
    return await client.SendAsync(request);
}

internal sealed class ProfileClock(DateTimeOffset fixedUtc) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => fixedUtc;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class ProfileTestSessionVerifier : IAriaSessionVerifier
{
    public Task<SessionResolution> VerifyAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.Headers.TryGetValue("X-Aria-Profile-Test", out var header))
            return Task.FromResult(SessionResolution.Anonymous());
        string requested = header.ToString();
        long badge = requested == "fixture-foreign" ? 102 : 101;
        const string login = "fixture-user";
        var now = DateTime.UtcNow;
        var session = new UserSessionIdentity(badge, login, "کاربر", "آزمایشی",
            requested == "fixture-admin-role" ? "admin" : "user", 11, "سمت آزمایشی", 0, "",
            10, "واحد آزمایشی", false, false, now.AddMinutes(-5), now.AddMinutes(10));
        return Task.FromResult(SessionResolution.Verified(new VerifiedAriaAccount(login, badge), session));
    }
}
