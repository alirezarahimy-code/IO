using System.Data;
using AriaAdmin.Web.Data;
using AriaAdmin.Web.Security;
using T5Web.Domain;

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
        (string)personCommand.Parameters["@UserName"].Value == "fixture-user", "normalized username must be bound, never interpolated");
    Require(accessCommand.CommandText == ReadOnlySqlDirectory.AccessSql &&
        accessCommand.Parameters.Count == 1 && accessCommand.Parameters["@BadgeNumber"].DbType == DbType.Int64 &&
        (long)accessCommand.Parameters["@BadgeNumber"].Value == 101, "permissions must be bound to same person in T5");
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

await Run("AA-DIR-07: UPN and bound adversarial login never become SQL text", async () =>
{
    foreach (var name in new[] { @"DOMAIN\", "@domain", "\r\n", "" })
    {
        var personCalls = 0;
        var denied = await new ReadOnlySqlDirectory(
            () => { personCalls++; return new TableConnection("Personnel", People()); },
            () => new TableConnection("T5", Permissions()))
            .ResolveAsync(name, CancellationToken.None);
        Require(denied.State == DirectoryState.Denied && personCalls == 0, "reject malformed identity before DB");
    }
    const string adversarial = "fixture'; SELECT @x";
    var p = new TableConnection("Personnel", People((101, true, false)));
    var directory = new ReadOnlySqlDirectory(() => p,
        () => new TableConnection("T5", Permissions(Grant(101, AdminFeature.PersonnelView, AccessLevel.ReadOnly))));
    var allowed = await directory.ResolveAsync(adversarial + "@testdom", CancellationToken.None);
    Require(allowed.State == DirectoryState.Allowed && allowed.Session?.BadgeNumber == 101,
        "fixed fake rows make this a binding test ONLY, not a real identity check");
    Require(!p.Commands.Single().CommandText.Contains(adversarial) &&
        (string)p.Commands.Single().Parameters["@UserName"].Value == adversarial,
        "SQL command text must never concatenate untrusted Windows identity");
});

Console.WriteLine($"Summary: {passed} Passed, {failed} Failed");
return failed == 0 ? 0 : 1;
