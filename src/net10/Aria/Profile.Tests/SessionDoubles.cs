using System.Collections.Concurrent;
using Aria.Web.Security;

// Test assembly ONLY. NEVER register an in-memory session registry in Web.
internal sealed class SyntheticSessionRegistry : IAriaSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, AriaSessionRecord> _records = new();
    public bool FailCreate { get; set; }
    public bool FailFind { get; set; }
    public bool FailRevoke { get; set; }
    public int FindCalls;
    public int Count => _records.Count;

    public Task<bool> TryCreateAsync(AriaSessionRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailCreate) throw new InvalidOperationException("SYNTHETIC_STORE_OUTAGE");
        return Task.FromResult(_records.TryAdd(record.Id, record));
    }

    public Task<AriaSessionRecord?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref FindCalls);
        if (FailFind) throw new InvalidOperationException("SYNTHETIC_STORE_OUTAGE");
        _records.TryGetValue(id, out var result);
        return Task.FromResult(result);
    }

    public Task<bool> TryRevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailRevoke) throw new InvalidOperationException("SYNTHETIC_STORE_OUTAGE");
        while (_records.TryGetValue(id, out var current))
        {
            if (current.Revoked) return Task.FromResult(false);
            if (_records.TryUpdate(id, current with { Revoked = true }, current))
                return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public void Replace(Guid id, VerifiedAriaAccount account)
    {
        if (!_records.TryGetValue(id, out var current))
            throw new InvalidOperationException("Synthetic session missing");
        _records[id] = current with { Account = account };
    }
}

internal sealed class SyntheticSessionClock(DateTimeOffset initial) : TimeProvider
{
    private long _utcTicks = initial.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public void Advance(TimeSpan interval) => Interlocked.Add(ref _utcTicks, interval.Ticks);
}
