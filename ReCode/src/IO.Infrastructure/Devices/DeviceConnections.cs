using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Devices;

/// <summary>Durable, independently authorized connection diagnostics, not a device protocol driver.</summary>
public sealed class DeviceConnections(IoDbContext db, PermissionTransaction transaction, TimeProvider clock)
{
    public const string Read = "devices.connection.read", Test = "devices.connection.test", Execute = "devices.connection.execute";
    public sealed record Claim(Guid Id, Guid Token, Guid DeviceId, string Profile, string? Host, int? Port, int TimeoutSeconds);
    public static readonly IReadOnlySet<string> Outcomes = new HashSet<string>(StringComparer.Ordinal)
        { "tcp-reachable", "unreachable", "timed-out", "unsupported", "endpoint-denied", "interrupted", "sdk-serial-matched", "sdk-identity-mismatch", "sdk-unavailable", "credential-unavailable", "bridge-failed" };

    public Task<Guid> RequestAsync(Guid actor, Guid operationId, Guid deviceId, byte[] expectedDeviceVersion, int timeoutSeconds, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Test, "device-connection.requested", async token =>
        {
            if (operationId == Guid.Empty || deviceId == Guid.Empty || expectedDeviceVersion is not { Length: 8 } || timeoutSeconds is < 1 or > 60)
                throw new ArgumentException("Device version and a timeout between 1 and 60 seconds required.");
            await transaction.LockAsync("IO.DeviceConnections", token);
            var old = await db.DeviceConnections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == operationId, token);
            if (old is not null)
            {
                if (old.RequestedByUserId != actor || old.DeviceId != deviceId || old.TimeoutSeconds != timeoutSeconds || !old.ExpectedDeviceVersion.SequenceEqual(expectedDeviceVersion))
                    throw new DomainConflictException("Changed connection test retry.");
                return (old.Id, old.Id);
            }
            var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deviceId, token) ?? throw new ArgumentException("Unknown device.");
            if (!device.Enabled || !device.Version.SequenceEqual(expectedDeviceVersion)) throw new DomainConflictException("Disabled or changed device; refresh.");
            if (await db.DeviceConnections.AnyAsync(x => x.DeviceId == deviceId && (x.State == "pending" || x.State == "running"), token))
                throw new DomainConflictException("This device already has an unfinished diagnostic.");
            if(await db.DeviceOperations.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device has an unfinished operation.");
            db.DeviceConnections.Add(new() { Id = operationId, DeviceId = deviceId, RequestedByUserId = actor,
                RequestedAtUtc = clock.GetUtcNow(), ExpectedDeviceVersion = expectedDeviceVersion.ToArray(), TimeoutSeconds = timeoutSeconds });
            return (operationId, operationId);
        }, ct);

    public Task<object> ListAsync(Guid actor, Guid? deviceId, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Read, "device-connection.viewed", async token =>
        {
            if (deviceId == Guid.Empty || offset is < 0 or > 100000) throw new ArgumentException("Invalid page or device.");
            var items = await db.DeviceConnections.AsNoTracking().Where(x => deviceId == null || x.DeviceId == deviceId)
                .OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.Id).Skip(offset).Take(50)
                .Select(x => new { x.Id, x.DeviceId, x.RequestedAtUtc, x.TimeoutSeconds, x.State, x.ClaimedAtUtc, x.FinishedAtUtc, x.Version }).ToListAsync(token);
            return (new { offset, pageSize = 50, items }, actor);
        }, ct);

    public Task<Guid> CancelAsync(Guid actor, Guid id, byte[] version, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Test, "device-connection.cancelled", async token =>
        {
            if (version is not { Length: 8 }) throw new ArgumentException("Version required.");
            await transaction.LockAsync("IO.DeviceConnections", token);
            var item = await db.DeviceConnections.AsTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ArgumentException("Unknown diagnostic.");
            if (!item.Version.SequenceEqual(version) || item.State != "pending") throw new DomainConflictException("Only a current pending diagnostic can be cancelled.");
            item.State = "cancelled"; item.FinishedAtUtc = clock.GetUtcNow();
            return (id, id);
        }, ct);

    // Service identities need an explicitly provisioned active foundation user with Execute;
    // no endpoint takes a caller-supplied service identity, and no role template grants Execute.
    public Task<Claim?> ClaimAsync(Guid service, CancellationToken ct = default) =>
        transaction.ExecuteAsync<Claim?>(service, Execute, "device-connection.claimed", async token =>
        {
            await transaction.LockAsync("IO.DeviceConnections", token);
            var now = clock.GetUtcNow();
            // A crash/abandoned claim is terminal: never silently repeat unknown work.
            var expired = await db.DeviceConnections.AsTracking().Where(x => x.State == "running" && x.ClaimedAtUtc < now.AddMinutes(-2)).ToListAsync(token);
            foreach (var item in expired) { item.State = "interrupted"; item.FinishedAtUtc = now; }
            var next = await db.DeviceConnections.AsTracking().Where(x => x.State == "pending")
                .OrderBy(x => x.RequestedAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(token);
            if (next is null) return (null, service);
            var device = await db.Devices.AsNoTracking().SingleAsync(x => x.Id == next.DeviceId, token);
            if (!await transaction.HasPermissionAsync(next.RequestedByUserId, Test, token))
            { next.State = "revoked"; next.FinishedAtUtc = now; return (null, next.Id); }
            if (!device.Enabled || !device.Version.SequenceEqual(next.ExpectedDeviceVersion))
            { next.State = "device-changed"; next.FinishedAtUtc = now; return (null, next.Id); }
            next.State = "running"; next.ServiceUserId = service; next.ClaimId = Guid.NewGuid(); next.ClaimedAtUtc = now;
            return (new Claim(next.Id, next.ClaimId.Value, device.Id, device.Profile, device.Host, device.Port, next.TimeoutSeconds), next.Id);
        }, ct);

    public Task<Guid> CompleteAsync(Guid service, Guid id, Guid claimId, string outcome, CancellationToken ct = default) =>
        transaction.ExecuteAsync(service, Execute, "device-connection.finished", async token =>
        {
            if (!Outcomes.Contains(outcome)) throw new ArgumentException("Unknown diagnostic result.");
            await transaction.LockAsync("IO.DeviceConnections", token);
            var item = await db.DeviceConnections.AsTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new ArgumentException("Unknown diagnostic.");
            if (item.ServiceUserId != service || item.ClaimId != claimId || item.State != "running" || item.ClaimedAtUtc < clock.GetUtcNow().AddMinutes(-2)) throw new DomainConflictException("Claim no longer owned by this service.");
            item.State = outcome; item.FinishedAtUtc = clock.GetUtcNow();
            return (id, id);
        }, ct);
}
