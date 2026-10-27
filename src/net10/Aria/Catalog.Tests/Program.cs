using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Aria.Core.Data;

// Only an in-process ADO.NET double. No driver, DB, real identities or secrets.
var passed = 0;
var failed = 0;

async Task Run(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine($"[PASS] {name}");
        passed++;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"[FAIL] {name}: {error}");
        failed++;
    }
}

static void Require(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
}

await Run("AR-CAT-01: both new catalog connections open and dispose", async () =>
{
    StubConnection? personnel = null, t5 = null;
    var probe = new DualCatalogConnectionProbe(
        () => personnel = new StubConnection("Personnel"),
        () => t5 = new StubConnection("T5"));
    var result = await probe.CheckAsync(CancellationToken.None);
    Require(result.IsHealthy && result.IsPersonnelDbConnected && result.IsT5DbConnected, "both connections must open");
    Require(personnel is { WasOpened: true, WasDisposed: true } && t5 is { WasOpened: true, WasDisposed: true }, "both resources must close");
    Require(!ReferenceEquals(personnel, t5) && personnel?.Database == "Personnel" && t5?.Database == "T5", "two named factories, not one shared connection");
    Require(result.PersonnelDbError == "" && result.T5DbError == "", "no fabricated error");
});

await Run("AR-CAT-02: missing factory cannot report healthy", () =>
{
    try { _ = new DualCatalogConnectionProbe(null!, () => new StubConnection("T5")); throw new Exception("accepted missing Personnel factory"); }
    catch (ArgumentNullException) { /* expected */ }
    try { _ = new DualCatalogConnectionProbe(() => new StubConnection("Personnel"), null!); throw new Exception("accepted missing T5 factory"); }
    catch (ArgumentNullException) { /* expected */ }
    return Task.CompletedTask;
});

await Run("AR-CAT-03: Personnel failure is isolated and sanitized", async () =>
{
    StubConnection? personnel = null, t5 = null;
    var probe = new DualCatalogConnectionProbe(
        () => personnel = new StubConnection("Personnel", failure: new InvalidOperationException("PRIVATE_CONNECTION_MARKER")),
        () => t5 = new StubConnection("T5"));
    var result = await probe.CheckAsync(CancellationToken.None);
    Require(!result.IsHealthy && !result.IsPersonnelDbConnected && result.IsT5DbConnected, "Personnel failure must not mask T5 check");
    Require(personnel is { WasDisposed: true } && t5 is { WasOpened: true, WasDisposed: true }, "always dispose and independently check T5");
    Require(result.PersonnelDbError != "" && !result.PersonnelDbError.Contains("PRIVATE_CONNECTION_MARKER"), "never expose provider error details");
});

await Run("AR-CAT-04: T5 factory failure is fail-closed and sanitized", async () =>
{
    StubConnection? personnel = null;
    var probe = new DualCatalogConnectionProbe(
        () => personnel = new StubConnection("Personnel"),
        () => throw new InvalidOperationException("PRIVATE_CONNECTION_MARKER"));
    var result = await probe.CheckAsync(CancellationToken.None);
    Require(!result.IsHealthy && result.IsPersonnelDbConnected && !result.IsT5DbConnected, "T5 failure must not produce green status");
    Require(personnel is { WasOpened: true, WasDisposed: true }, "Personnel connection must close");
    Require(result.T5DbError != "" && !result.T5DbError.Contains("PRIVATE_CONNECTION_MARKER"), "never expose provider error details");
});

await Run("AR-CAT-05: a driver that stays closed cannot report success", async () =>
{
    var probe = new DualCatalogConnectionProbe(
        () => new StubConnection("Personnel", staysClosed: true),
        () => new StubConnection("T5"));
    var result = await probe.CheckAsync(CancellationToken.None);
    Require(!result.IsHealthy && !result.IsPersonnelDbConnected && result.IsT5DbConnected, "OpenAsync returning is not proof of open state");
});

await Run("AR-CAT-06: cancellation propagates, closes and skips second catalog", async () =>
{
    using var cancel = new CancellationTokenSource();
    StubConnection? personnel = null;
    var t5Calls = 0;
    var probe = new DualCatalogConnectionProbe(
        () => personnel = new StubConnection("Personnel", onOpen: cancel.Cancel),
        () => { t5Calls++; return new StubConnection("T5"); });
    try { _ = await probe.CheckAsync(cancel.Token); throw new Exception("cancellation was swallowed"); }
    catch (OperationCanceledException) when (cancel.IsCancellationRequested) { /* expected */ }
    Require(t5Calls == 0 && personnel is { WasDisposed: true }, "cancellation must not open T5 or leak Personnel");
});

await Run("AR-CAT-07: 100 overlapping synthetic probes use fresh connections", async () =>
{
    var personnel = new ConcurrentBag<StubConnection>();
    var t5 = new ConcurrentBag<StubConnection>();
    var probe = new DualCatalogConnectionProbe(
        () => { var conn = new StubConnection("Personnel"); personnel.Add(conn); return conn; },
        () => { var conn = new StubConnection("T5"); t5.Add(conn); return conn; });
    var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => probe.CheckAsync(CancellationToken.None)));
    Require(results.Length == 100 && results.All(item => item.IsHealthy), "all synthetic probes must open both catalogs");
    Require(personnel.Count == 100 && t5.Count == 100 && personnel.All(c => c.WasDisposed) && t5.All(c => c.WasDisposed), "no shared/leaked connection");
});

Console.WriteLine($"Summary: {passed} Passed, {failed} Failed");
return failed == 0 ? 0 : 1;

internal sealed class StubConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;
    private readonly Exception? _failure;
    private readonly bool _staysClosed;
    private readonly Action? _onOpen;

    public StubConnection(string database, Exception? failure = null, bool staysClosed = false, Action? onOpen = null)
    {
        Database = database;
        _failure = failure;
        _staysClosed = staysClosed;
        _onOpen = onOpen;
    }

    public bool WasOpened { get; private set; }
    public bool WasDisposed { get; private set; }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database { get; }
    public override string DataSource => "synthetic";
    public override string ServerVersion => "synthetic";
    public override ConnectionState State => _state;

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        WasOpened = true;
        _onOpen?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (_failure != null) throw _failure;
        if (!_staysClosed) _state = ConnectionState.Open;
        return Task.CompletedTask;
    }

    public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
    public override void Close() => _state = ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        Close();
        base.Dispose(disposing);
    }
}
