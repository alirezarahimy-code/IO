using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Text.Json;
using Aria.Web.Data;
using Aria.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TWF.Domain.Models;
using WebHost = Aria.Web.Program;

if ((args.Length != 1 && (args.Length != 3 || args[1] != "--original-personnel-schema")) ||
    !File.Exists(Path.Combine(args[0], "Aria.Web.csproj")) ||
    (args.Length == 3 && !File.Exists(args[2])))
    throw new ArgumentException("Pass Aria Web and optionally a same-run original EDMX fixture.");
string webRoot = Path.GetFullPath(args[0]);
string? originalSchemaFixture = args.Length == 3 ? args[2] : null;
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
    foreach (var (badge, name, webUser, former) in entries)
        data.Rows.Add(badge, name, webUser,
            former ? (object)new DateTime(2020, 1, 1) : DBNull.Value);
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
                ReadOnlyPersonnelDirectory.PersonSql.Contains("FROM [dbo].[UserInfo]", StringComparison.Ordinal) &&
                !ReadOnlyPersonnelDirectory.PersonSql.Contains("Password", StringComparison.OrdinalIgnoreCase),
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

Console.WriteLine($"Summary: {passed} Passed, {failed} Failed (Aria fake Personnel + Kestrel; original EDMX only with same-run fixture)");
return failed == 0 && passed == (originalSchemaFixture is null ? 8 : 9) ? 0 : 1;

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
