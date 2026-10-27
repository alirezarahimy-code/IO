using System.Collections.Concurrent;
using System.Data;
using System.Net;
using AriaAdmin.Web.Data;
using AriaAdmin.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using T5Web.Domain;
using WebHost = AriaAdmin.Web.Program;

if (args.Length != 1 || !File.Exists(Path.Combine(args[0], "AriaAdmin.Web.csproj")))
    throw new ArgumentException("Pass only the local Admin Web project directory for synthetic Kestrel tests.");
string webRoot = Path.GetFullPath(args[0]);

var passed = 0;
var failed = 0;
async Task Run(string label, Func<Task> test)
{
    try { await test(); Console.WriteLine($"[PASS] {label}"); passed++; }
    catch (Exception error) { Console.Error.WriteLine($"[FAIL] {label}: {error}"); failed++; }
}
static void Require(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException(message);
}
static async Task<HttpClient> StartWeb(WebApplication app)
{
    app.Urls.Add("http://127.0.0.1:0"); // CI process only, no customer/browser endpoint.
    await app.StartAsync();
    string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?
        .Addresses.Single(value => value.StartsWith("http://127.0.0.1:", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("Synthetic Kestrel did not bind a local port");
    return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) };
}
static async Task<HttpResponseMessage> SendTestIdentity(HttpClient client, string path, string? identity)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    if (identity != null) request.Headers.TryAddWithoutValidation("X-Directory-Test-Identity", identity);
    return await client.SendAsync(request);
}
static DataTable People(params (long Badge, bool WebUser, bool Former)[] entries)
{
    var rows = new DataTable();
    rows.Columns.Add("BadgeNumber", typeof(long));
    rows.Columns.Add("FirstName", typeof(string));
    rows.Columns.Add("LastName", typeof(string));
    rows.Columns.Add("FiredDate", typeof(DateTime));
    rows.Columns.Add("WebUser", typeof(bool));
    foreach (var (badge, webUser, former) in entries)
        rows.Rows.Add(badge, "آزمایشی", "ساختگی", former ? (object)new DateTime(2020, 1, 1) : DBNull.Value, webUser);
    return rows;
}
static DataTable Permissions(params (long Badge, int Feature, int Level)[] entries)
{
    var rows = new DataTable();
    rows.Columns.Add("BadgeNumber", typeof(long));
    rows.Columns.Add("Feature", typeof(int));
    rows.Columns.Add("Level", typeof(int));
    foreach (var (badge, feature, level) in entries) rows.Rows.Add(badge, feature, level);
    return rows;
}
static (long, int, int) Grant(long badge, AdminFeature feature, AccessLevel level) =>
    (badge, (int)feature, (int)level);

await Run("AA-DIR-01: Personnel/T5 read-only commands bind separate values and close", async () =>
{
    var p = new TableConnection("Personnel", People((101, true, false)));
    var t = new TableConnection("T5", Permissions(
        Grant(101, AdminFeature.PersonnelView, AccessLevel.ReadOnly),
        Grant(101, AdminFeature.PortalAdmin, AccessLevel.Allow)));
    var directory = new ReadOnlySqlDirectory(() => p, () => t);
    var result = await directory.ResolveAsync(@"TESTDOM\fixture-user", CancellationToken.None);
    Require(result.State == DirectoryState.Allowed && result.Session?.BadgeNumber == 101 &&
        result.Session.LevelOf(AdminFeature.PortalAdmin) == AccessLevel.Allow, "verified current-rewrite grant expected");
    Require(p.Database == "Personnel" && t.Database == "T5" && p.WasDisposed && t.WasDisposed &&
        p.Commands.Single().WasDisposed && t.Commands.Single().WasDisposed, "catalogs/resources must be separate");
    var personCommand = p.Commands.Single();
    var accessCommand = t.Commands.Single();
    Require(personCommand.CommandText == ReadOnlySqlDirectory.PersonSql &&
        personCommand.CommandText.Contains("TOP (2)") && !personCommand.CommandText.Contains("Password") &&
        personCommand.CommandType == CommandType.Text, "limited current-T5Web person projection, no password");
    Require(personCommand.Parameters.Count == 1 && personCommand.Parameters["@UserName"].DbType == DbType.String &&
        personCommand.Parameters["@UserName"].Value is string boundUser && boundUser == "fixture-user",
        "normalized username must be bound, never interpolated");
    Require(accessCommand.CommandText == ReadOnlySqlDirectory.AccessSql &&
        accessCommand.Parameters.Count == 1 && accessCommand.Parameters["@BadgeNumber"].DbType == DbType.Int64 &&
        accessCommand.Parameters["@BadgeNumber"].Value is long boundBadge && boundBadge == 101,
        "permissions must be bound to same person in T5");
});

await Run("AA-DIR-02: disabled, former, zero and duplicate users are denied before T5", async () =>
{
    var sets = new[] {
        People((101, false, false)), People((101, true, true)), People((0, true, false)),
        People((101, true, false), (102, true, false)), People()
    };
    foreach (var people in sets)
    {
        var p = new TableConnection("Personnel", people);
        var t5Opened = 0;
        var directory = new ReadOnlySqlDirectory(() => p,
            () => { t5Opened++; return new TableConnection("T5", Permissions()); });
        var result = await directory.ResolveAsync("fixture-user", CancellationToken.None);
        Require(result.State == DirectoryState.Denied && result.Session == null && t5Opened == 0 && p.WasDisposed,
            "unqualified/ambiguous identity must not read permissions");
    }
});

await Run("AA-DIR-03: absent or Deny-winning permissions cannot admit an operator", async () =>
{
    var cases = new[] {
        Permissions(), Permissions(Grant(101, AdminFeature.PersonnelView, AccessLevel.Deny)),
        Permissions(Grant(101, AdminFeature.PersonnelView, AccessLevel.Deny),
                    Grant(101, AdminFeature.PersonnelView, AccessLevel.Allow))
    };
    foreach (var permissions in cases)
    {
        var directory = new ReadOnlySqlDirectory(
            () => new TableConnection("Personnel", People((101, true, false))),
            () => new TableConnection("T5", permissions));
        var result = await directory.ResolveAsync("fixture-user", CancellationToken.None);
        Require(result.State == DirectoryState.Denied && result.Session == null, "deny or empty must not become an authenticated session");
    }
});

await Run("AA-DIR-04: unknown or cross-user permissions fail unavailable, not allowed", async () =>
{
    var cases = new[] {
        Permissions((101, 9999, (int)AccessLevel.Allow)),
        Permissions((101, (int)AdminFeature.PortalAdmin, 9999)),
        Permissions(Grant(102, AdminFeature.PortalAdmin, AccessLevel.Allow))
    };
    foreach (var permissions in cases)
    {
        var directory = new ReadOnlySqlDirectory(
            () => new TableConnection("Personnel", People((101, true, false))),
            () => new TableConnection("T5", permissions));
        var result = await directory.ResolveAsync("fixture-user", CancellationToken.None);
        Require(result.State == DirectoryState.Unavailable && result.Session == null, "corrupt schema/permission cannot be coerced into success");
    }
});

await Run("AA-DIR-05: missing factories and provider/read failures do not disclose details", async () =>
{
    try { _ = new ReadOnlySqlDirectory(null!, () => new TableConnection("T5", Permissions())); throw new Exception("accepted missing factory"); }
    catch (ArgumentNullException) { /* expected */ }
    var p = new TableConnection("Personnel", People((101, true, false)))
        { OpenFailure = new InvalidOperationException("PRIVATE_CONNECTION_MARKER") };
    var unavailable = await new ReadOnlySqlDirectory(() => p, () => throw new Exception("T5 should not open"))
        .ResolveAsync("fixture-user", CancellationToken.None);
    Require(unavailable.State == DirectoryState.Unavailable && unavailable.Session == null && p.WasDisposed &&
        !unavailable.ToString().Contains("PRIVATE_CONNECTION_MARKER"), "provider exception must not leak");
    var t = new TableConnection("T5", Permissions(Grant(101, AdminFeature.PortalAdmin, AccessLevel.Allow)))
        { ReadFailure = new InvalidOperationException("PRIVATE_CONNECTION_MARKER") };
    unavailable = await new ReadOnlySqlDirectory(
        () => new TableConnection("Personnel", People((101, true, false))), () => t)
        .ResolveAsync("fixture-user", CancellationToken.None);
    Require(unavailable.State == DirectoryState.Unavailable && unavailable.Session == null && t.WasDisposed &&
        !unavailable.ToString().Contains("PRIVATE_CONNECTION_MARKER"), "read failure must not grant or disclose");
});

await Run("AA-DIR-06: request cancellation propagates and disposes the person connection", async () =>
{
    using var cancel = new CancellationTokenSource();
    var p = new TableConnection("Personnel", People((101, true, false)))
        { BeforeOpen = cancel.Cancel };
    var t5Calls = 0;
    var directory = new ReadOnlySqlDirectory(() => p,
        () => { t5Calls++; return new TableConnection("T5", Permissions()); });
    try { _ = await directory.ResolveAsync("fixture-user", cancel.Token); throw new Exception("cancellation swallowed"); }
    catch (OperationCanceledException) when (cancel.IsCancellationRequested) { /* expected */ }
    Require(p.WasDisposed && t5Calls == 0, "do not open T5 on cancelled request");
});

await Run("AA-DIR-07: legacy name shapes and bound adversarial account never become SQL text", async () =>
{
    foreach (var name in new[] { @"DOMAIN\", "@domain", "fixture@one@two", "fixture@", "\r\n", "" })
    {
        var personCalls = 0;
        var denied = await new ReadOnlySqlDirectory(
            () => { personCalls++; return new TableConnection("Personnel", People()); },
            () => new TableConnection("T5", Permissions()))
            .ResolveAsync(name, CancellationToken.None);
        Require(denied.State == DirectoryState.Denied && personCalls == 0, "reject malformed identity before DB");
    }
    var pUpn = new TableConnection("Personnel", People((101, true, false)));
    var upnDirectory = new ReadOnlySqlDirectory(() => pUpn,
        () => new TableConnection("T5", Permissions(Grant(101, AdminFeature.PersonnelView, AccessLevel.ReadOnly))));
    var upn = await upnDirectory.ResolveAsync("fixture-user@testdom", CancellationToken.None);
    Require(upn.State == DirectoryState.Allowed &&
        pUpn.Commands.Single().Parameters["@UserName"].Value is string boundUpn && boundUpn == "fixture-user",
        "well-formed UPN must bind only its local name (current T5Web rewrite rule)");

    const string adversarial = "fixture'; DROP TABLE [UserInfo];--";
    var p = new TableConnection("Personnel", People((101, true, false)));
    var directory = new ReadOnlySqlDirectory(() => p,
        () => new TableConnection("T5", Permissions(Grant(101, AdminFeature.PersonnelView, AccessLevel.ReadOnly))));
    var allowed = await directory.ResolveAsync(adversarial, CancellationToken.None);
    Require(allowed.State == DirectoryState.Allowed && allowed.Session?.BadgeNumber == 101,
        "fixed fake rows make this a binding test ONLY, not a real identity check");
    Require(!p.Commands.Single().CommandText.Contains(adversarial) &&
        p.Commands.Single().Parameters["@UserName"].Value is string boundAdversarial && boundAdversarial == adversarial,
        "SQL command text must never concatenate an account name");
});

await Run("AA-DIR-08: test-only SQL seam reaches HTTP gate but never fake operational data", async () =>
{
    var personnel = new ConcurrentBag<TableConnection>();
    var t5 = new ConcurrentBag<TableConnection>();
    await using var app = WebHost.CreateApp(Array.Empty<string>(), services =>
    {
        services.AddSingleton<IAdminIdentitySource, DirectoryTestIdentitySource>();
        services.AddSingleton<IAdminDirectory>(new ReadOnlySqlDirectory(
            () => { var connection = new TableConnection("Personnel", People((101, true, false))); personnel.Add(connection); return connection; },
            () => { var connection = new TableConnection("T5", Permissions(
                Grant(101, AdminFeature.PersonnelView, AccessLevel.ReadOnly),
                Grant(101, AdminFeature.PortalAdmin, AccessLevel.Allow))); t5.Add(connection); return connection; }));
    }, webRoot);
    using var client = await StartWeb(app);
    using var diagnostics = await SendTestIdentity(client, "/Diagnostics/Index", @"TESTDOM\fixture-user");
    Require(diagnostics.StatusCode == HttpStatusCode.ServiceUnavailable, "PortalAdmin is recognized, but no real diagnostics data exists");
    string body = await diagnostics.Content.ReadAsStringAsync();
    Require(!body.Contains("UserInfo", StringComparison.Ordinal) && !body.Contains("fixture-user", StringComparison.Ordinal),
        "do not publish identity or table name");
    Require(diagnostics.Headers.TryGetValues("Cache-Control", out var cache) && cache.Contains("no-store") &&
        !diagnostics.Headers.TryGetValues("Set-Cookie", out _), "no cache or synthetic session cookie");
    using var dashboard = await SendTestIdentity(client, "/Dashboard/Index", @"TESTDOM\fixture-user");
    Require(dashboard.StatusCode == HttpStatusCode.ServiceUnavailable, "authorized shell still has no operational dashboard");
    Require(personnel.Count == 2 && t5.Count == 2 &&
        personnel.All(connection => connection.WasDisposed) && t5.All(connection => connection.WasDisposed),
        "two HTTP requests must not share or leak fake DB connections");
});

await Run("AA-DIR-09: test-only SQL seam denies disabled and fails closed on driver errors", async () =>
{
    var disabledT5Calls = 0;
    await using (var deniedApp = WebHost.CreateApp(Array.Empty<string>(), services =>
    {
        services.AddSingleton<IAdminIdentitySource, DirectoryTestIdentitySource>();
        services.AddSingleton<IAdminDirectory>(new ReadOnlySqlDirectory(
            () => new TableConnection("Personnel", People((101, false, false))),
            () => { disabledT5Calls++; return new TableConnection("T5", Permissions()); }));
    }, webRoot))
    {
        using var client = await StartWeb(deniedApp);
        using var anonymous = await SendTestIdentity(client, "/Diagnostics/Index", null);
        Require(anonymous.StatusCode == HttpStatusCode.Unauthorized, "anonymous cannot even read the person table");
        using var denied = await SendTestIdentity(client, "/Diagnostics/Index", "fixture-user");
        Require(denied.StatusCode == HttpStatusCode.Forbidden && disabledT5Calls == 0,
            "WebUser disabled must never reach the permissions catalog");
    }
    await using (var failedApp = WebHost.CreateApp(Array.Empty<string>(), services =>
    {
        services.AddSingleton<IAdminIdentitySource, DirectoryTestIdentitySource>();
        services.AddSingleton<IAdminDirectory>(new ReadOnlySqlDirectory(
            () => new TableConnection("Personnel", People((101, true, false))),
            () => new TableConnection("T5", Permissions(Grant(101, AdminFeature.PortalAdmin, AccessLevel.Allow)))
                { OpenFailure = new InvalidOperationException("PRIVATE_CONNECTION_MARKER") }));
    }, webRoot))
    {
        using var client = await StartWeb(failedApp);
        using var unavailable = await SendTestIdentity(client, "/Diagnostics/Index", "fixture-user");
        Require(unavailable.StatusCode == HttpStatusCode.ServiceUnavailable, "driver failure must not become an allow or 403");
        string body = await unavailable.Content.ReadAsStringAsync();
        Require(!body.Contains("PRIVATE_CONNECTION_MARKER", StringComparison.Ordinal) &&
            !body.Contains("fixture-user", StringComparison.Ordinal), "driver/identity details never reach HTTP body");
    }
});

Console.WriteLine($"Summary: {passed} Passed, {failed} Failed");
return failed == 0 ? 0 : 1;

// This header source is in Directory.Tests ONLY. Published Web DI still uses
// UnavailableSharedAccountSource + UnavailableAdminDirectory (no test header path).
internal sealed class DirectoryTestIdentitySource : IAdminIdentitySource
{
    public Task<AccountResolution> ResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(context.Request.Headers.TryGetValue("X-Directory-Test-Identity", out var value) &&
            !string.IsNullOrWhiteSpace(value.ToString())
                ? AccountResolution.Verified(value.ToString())
                : AccountResolution.Anonymous());
}
