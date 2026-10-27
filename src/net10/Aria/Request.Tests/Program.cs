using System.Data;
using System.Text.Json;
using Aria.Web.Data;
using Aria.Web.Security;

// Snapshot-only on portable runners. Windows x86 golden also regenerates the
// selected TWF 4.28 T5V3.DB.dll EDMX contract IN THIS RUN. No query is executed
// against a database; all results below are tiny fabricated DataTable rows.
bool originalRuntime = args.Length == 2 && args[0] == "--original-request-runtime" &&
    File.Exists(args[1]);
bool originalSchema = args.Length == 1 && File.Exists(args[0]);
if (args.Length != 0 && !originalSchema && !originalRuntime)
    throw new ArgumentException("Pass no args, same-run original EDMX, or --original-request-runtime <CI temp file>.");
string schemaSnapshot = Path.Combine(AppContext.BaseDirectory, "original-request-schema.fixture.json");
using var snapshotDocument = JsonDocument.Parse(File.ReadAllText(schemaSnapshot));
using var fixtureDocument = JsonDocument.Parse(File.ReadAllText(
    originalSchema ? args[0] : schemaSnapshot));
var original = fixtureDocument.RootElement;
var snapshot = snapshotDocument.RootElement;
const string sha = "d8f0570b39635e21b883257cf4d6bba1d6011002921ba5227dcd9cdf567b1faf";
const string scope = "ORIGINAL_TWF_REQUEST_EDMX_ONLY_X86";
if (original.GetProperty("source_sha256").GetString() != sha ||
    original.GetProperty("scope").GetString() != scope ||
    original.GetProperty("table").GetString() != "dbo.Request" ||
    snapshot.GetProperty("source_sha256").GetString() != sha ||
    snapshot.GetProperty("scope").GetString() != scope ||
    snapshot.GetProperty("table").GetString() != "dbo.Request")
    throw new InvalidOperationException("Reject changed original/request metadata provenance.");
var columns = new Dictionary<string, string>
{
    ["RequestID"] = "bigint", ["BadgeNumber"] = "bigint", ["WofkflowType"] = "int",
    ["ForDate"] = "datetime", ["FromTime"] = "datetime", ["ToTime"] = "datetime",
    ["IsSuspend"] = "bit", ["IsDaily"] = "bit"
};
int passed = 0, failed = 0;
void Run(string label, Action test)
{
    try { test(); Console.WriteLine("[PASS] " + label); passed++; }
    catch (Exception failure)
    {
        // Never expose exception messages, which may contain provider strings.
        Console.Error.WriteLine("[FAIL] " + label + ": " + failure.GetType().Name);
        failed++;
    }
}
void Require(bool condition) { if (!condition) throw new InvalidOperationException("Synthetic test failure."); }
DateTime day = new(2026, 10, 27, 0, 0, 0, DateTimeKind.Unspecified);
VerifiedAriaAccount account = new("fixture-request", 101);
DataTable Rows(params (long Id, long Badge, int Workflow, DateTime Date, DateTime Start,
    DateTime End, bool Suspended, bool Daily)[] data)
{
    var rows = new DataTable();
    rows.Columns.Add("RequestID", typeof(long));
    rows.Columns.Add("BadgeNumber", typeof(long));
    rows.Columns.Add("WofkflowType", typeof(int));
    foreach (string name in new[] { "ForDate", "FromTime", "ToTime" })
    {
        var column = rows.Columns.Add(name, typeof(DateTime));
        column.DateTimeMode = DataSetDateTime.Unspecified;
    }
    rows.Columns.Add("IsSuspend", typeof(bool));
    rows.Columns.Add("IsDaily", typeof(bool));
    foreach (var row in data)
        rows.Rows.Add(row.Id, row.Badge, row.Workflow, row.Date, row.Start, row.End,
            row.Suspended, row.Daily);
    return rows;
}
var testRows = Rows((50, 101, 1, day, day.AddHours(9), day.AddHours(10), false, false),
    (51, 101, 3, day, day.AddHours(11), day.AddHours(12), true, true));

Run("AR-REQUEST-01: original-only typed EDMX fields match read-only bounded projection", () =>
{
    foreach (var doc in new[] { original, snapshot })
    {
        JsonElement fields = doc.GetProperty("columns");
        Require(fields.ValueKind == JsonValueKind.Object &&
            fields.EnumerateObject().Count() == columns.Count &&
            columns.All(kv => fields.GetProperty(kv.Key).GetString() == kv.Value));
    }
    string sql = ReadOnlyAriaRequestDirectory.RequestSql;
    Require(sql.Contains("SELECT TOP (101)", StringComparison.Ordinal) &&
        sql.Contains("FROM [dbo].[Request]", StringComparison.Ordinal) &&
        sql.Contains("[WofkflowType]", StringComparison.Ordinal) &&
        sql.Contains("[BadgeNumber] = @BadgeNumber", StringComparison.Ordinal) &&
        sql.Contains("[ForDate] >= @DayStart", StringComparison.Ordinal) &&
        sql.Contains("[ForDate] < @DayEnd", StringComparison.Ordinal) &&
        columns.Keys.All(key => sql.Contains("[" + key + "]", StringComparison.Ordinal)) &&
        !sql.Contains("SELECT *", StringComparison.Ordinal) &&
        !sql.Contains("[Descriptions]", StringComparison.Ordinal) &&
        !sql.Contains("[Latitude]", StringComparison.Ordinal) &&
        !sql.Contains("[Longitude]", StringComparison.Ordinal) &&
        !sql.Contains("[ImageData]", StringComparison.Ordinal) &&
        !sql.Contains("UPDATE ", StringComparison.Ordinal) &&
        !sql.Contains("DELETE ", StringComparison.Ordinal) &&
        !sql.Contains("INSERT ", StringComparison.Ordinal));
});
Run("AR-REQUEST-02: verified same-badge day gets only typed header DTOs", () =>
{
    var connection = new TableConnection("T5", testRows);
    var output = new ReadOnlyAriaRequestDirectory(() => connection)
        .ListDayAsync(account, day, CancellationToken.None).GetAwaiter().GetResult();
    Require(output.State == AriaRequestSnapshotState.Available && output.Headers.Count == 2 &&
        output.Headers[0].RequestId == 50 && output.Headers[1].IsSuspend &&
        output.Headers[1].IsDaily && connection.WasDisposed);
    TableCommand command = connection.Commands.Single();
    Require(command.WasDisposed && command.CommandText == ReadOnlyAriaRequestDirectory.RequestSql &&
        command.CommandType == CommandType.Text && command.CommandTimeout == 5 &&
        command.Parameters.Count == 3 &&
        command.Parameters["@BadgeNumber"].DbType == DbType.Int64 &&
        command.Parameters["@BadgeNumber"].Value is long badge && badge == 101 &&
        command.Parameters["@DayStart"].DbType == DbType.DateTime &&
        command.Parameters["@DayStart"].Value is DateTime from && from == day &&
        command.Parameters["@DayEnd"].DbType == DbType.DateTime &&
        command.Parameters["@DayEnd"].Value is DateTime to && to == day.AddDays(1));
});
Run("AR-REQUEST-03: bad account/unsupported local day never opens a T5 connection", () =>
{
    int opened = 0;
    var directory = new ReadOnlyAriaRequestDirectory(() =>
    {
        opened++;
        return new TableConnection("T5", Rows());
    });
    foreach (var identity in new[] {
        new VerifiedAriaAccount("", 101), new VerifiedAriaAccount(" fixture-request", 101),
        new VerifiedAriaAccount("fixture-request", 0),
        new VerifiedAriaAccount(new string('x', 101), 101) })
        Require(directory.ListDayAsync(identity, day, CancellationToken.None).GetAwaiter().GetResult().State ==
            AriaRequestSnapshotState.Denied);
    foreach (DateTime date in new[] { new DateTime(1752, 12, 31),
        new DateTime(9999, 12, 31), day.AddMinutes(1), DateTime.SpecifyKind(day, DateTimeKind.Utc) })
        Require(directory.ListDayAsync(account, date, CancellationToken.None).GetAwaiter().GetResult().State ==
            AriaRequestSnapshotState.Denied);
    Require(opened == 0);
});
Run("AR-REQUEST-04: duplicate/foreign/day-shift/malformed rows fail closed, no partial list", () =>
{
    foreach (DataTable rows in new[] {
        Rows((50,101,1,day,day,day,false,false), (50,101,1,day,day,day,false,false)),
        Rows((50,101,1,day,day,day,false,false), (51,102,1,day,day,day,false,false)),
        Rows((50,101,1,day,day,day,false,false), (51,101,1,day.AddDays(1),day,day,false,false))
    })
    {
        var connection = new TableConnection("T5", rows);
        var output = new ReadOnlyAriaRequestDirectory(() => connection)
            .ListDayAsync(account, day, CancellationToken.None).GetAwaiter().GetResult();
        Require(output.State == AriaRequestSnapshotState.Unavailable &&
            output.Headers.Count == 0 && connection.WasDisposed);
    }
    var malformed = Rows((50, 101, 1, day, day, day, false, false));
    malformed.Rows[0]["ForDate"] = DBNull.Value;
    var bad = new TableConnection("T5", malformed);
    Require(new ReadOnlyAriaRequestDirectory(() => bad)
        .ListDayAsync(account, day, CancellationToken.None).GetAwaiter().GetResult().State ==
        AriaRequestSnapshotState.Unavailable);
});
Run("AR-REQUEST-05: 100 rows bounded; 101 or empty never imply workflow completion", () =>
{
    var hundred = Rows(Enumerable.Range(1, 100).Select(i =>
        ((long)i, 101L, 1, day, day, day, false, false)).ToArray());
    var admitted = new ReadOnlyAriaRequestDirectory(() => new TableConnection("T5", hundred))
        .ListDayAsync(account, day, CancellationToken.None).GetAwaiter().GetResult();
    Require(admitted.State == AriaRequestSnapshotState.Available && admitted.Headers.Count == 100);
    hundred.Rows.Add(101L, 101L, 1, day, day, day, false, false);
    var tooMany = new ReadOnlyAriaRequestDirectory(() => new TableConnection("T5", hundred))
        .ListDayAsync(account, day, CancellationToken.None).GetAwaiter().GetResult();
    Require(tooMany.State == AriaRequestSnapshotState.Unavailable && tooMany.Headers.Count == 0);
    var none = new ReadOnlyAriaRequestDirectory(() => new TableConnection("T5", Rows()))
        .ListDayAsync(account, day, CancellationToken.None).GetAwaiter().GetResult();
    Require(none.State == AriaRequestSnapshotState.Available && none.Headers.Count == 0);
});
Run("AR-REQUEST-06: provider errors are unavailable; caller cancellation propagates", () =>
{
    foreach (bool onOpen in new[] { true, false })
    {
        var connection = new TableConnection("T5", testRows);
        if (onOpen) connection.OpenFailure = new InvalidOperationException("PRIVATE_DB_MARKER");
        else connection.ReadFailure = new InvalidOperationException("PRIVATE_DB_MARKER");
        var output = new ReadOnlyAriaRequestDirectory(() => connection)
            .ListDayAsync(account, day, CancellationToken.None).GetAwaiter().GetResult();
        Require(output.State == AriaRequestSnapshotState.Unavailable && connection.WasDisposed);
    }
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    int opened = 0;
    try
    {
        new ReadOnlyAriaRequestDirectory(() =>
        {
            opened++;
            return new TableConnection("T5", testRows);
        }).ListDayAsync(account, day, cancelled.Token).GetAwaiter().GetResult();
        throw new InvalidOperationException("Cancelled request was admitted.");
    }
    catch (OperationCanceledException) { Require(opened == 0); }
});
if (originalRuntime)
{
    // The other project in this same Windows CI job executed TWF 4.28's
    // original RequestDA.GetAll on five synthetic LocalDB rows. The NEW host
    // STILL has no SQL provider. These assertions explicitly document where
    // our conservative badge/day boundary diverges from the original.
    using var executed = JsonDocument.Parse(File.ReadAllText(args[1]));
    JsonElement runtime = executed.RootElement;
    Run("AR-REQUEST-07: executable original filters vs unwired port-only bounds", () =>
    {
        Require(runtime.GetProperty("source_sha256").GetString() == sha &&
            runtime.GetProperty("scope").GetString() ==
                "ORIGINAL_ARIA_REQUEST_LOCALDB_SYNTHETIC_ONLY");
        static bool EqualIds(JsonElement element, params long[] expected) =>
            element.ValueKind == JsonValueKind.Array &&
            element.EnumerateArray().Select(x => x.GetInt64()).SequenceEqual(expected);
        Require(EqualIds(runtime.GetProperty("all_false"), 810001, 810003, 810004, 810005) &&
            EqualIds(runtime.GetProperty("all_true"), 810002) &&
            EqualIds(runtime.GetProperty("absent")) &&
            EqualIds(runtime.GetProperty("range_inclusive_probe"), 810001, 810002, 810003) &&
            EqualIds(runtime.GetProperty("exact_midnight"), 810001) &&
            EqualIds(runtime.GetProperty("flag_false"), 810001, 810003, 810004) &&
            EqualIds(runtime.GetProperty("flag_true"), 810002));
        string sql = ReadOnlyAriaRequestDirectory.RequestSql;
        Require(sql.Contains("[BadgeNumber] = @BadgeNumber", StringComparison.Ordinal) &&
            sql.Contains("[ForDate] < @DayEnd", StringComparison.Ordinal) &&
            !sql.Contains("[IsSuspend] = @", StringComparison.Ordinal) &&
            !sql.Contains("[ImageData]", StringComparison.Ordinal));
        // Fake reader is deliberately supplied *filtered* rows: it NEVER
        // executes the SQL or proves day-filter parity. It must retain both
        // raw suspension values so callers cannot mistake them for approvals.
        var scoped = Rows((810001, 900101, 0, day, day, day, false, false),
            (810002, 900101, 0, day.AddHours(12), day, day, true, true));
        var originalBadgeFixture = new VerifiedAriaAccount("fixture-request", 900101);
        var output = new ReadOnlyAriaRequestDirectory(() => new TableConnection("T5", scoped))
            .ListDayAsync(originalBadgeFixture, day, CancellationToken.None).GetAwaiter().GetResult();
        Require(output.State == AriaRequestSnapshotState.Available &&
            output.Headers.Select(row => row.RequestId).SequenceEqual(new long[] { 810001, 810002 }) &&
            !output.Headers[0].IsSuspend && output.Headers[1].IsSuspend);
    });
}
Console.WriteLine($"Summary: {passed} Passed, {failed} Failed; " +
    $"{(originalRuntime ? "SAME-RUN original RequestDA LocalDB and fake port contrast" :
        originalSchema ? "same-run original EDMX" : "tracked original EDMX snapshot")} + synthetic fake ADO; " +
    "NO net10 SQL Server/driver, original HTTP or request write");
return failed == 0 && passed == (originalRuntime ? 7 : 6) ? 0 : 1;
