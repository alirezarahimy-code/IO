using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Devices;

/// <summary>Shared safety fence, not a grant or a generic native dispatch list.
/// Call under IO.DeviceConnections transaction lock; recovery reads remain possible.</summary>
internal static class DeviceWriteSafety
{
    internal static bool IsWrite(string kind)=>kind is "clock.set" or "device.restart" or "network.ip.set" or "settings.idle.set" or "settings.raw.set";
    internal static Task<bool> UnreviewedAsync(IoDbContext db,Guid device,CancellationToken ct)=>db.DeviceOperations.AnyAsync(x=>x.DeviceId==device&&
        (x.Kind=="clock.set"||x.Kind=="device.restart"||x.Kind=="network.ip.set"||x.Kind=="settings.idle.set"||x.Kind=="settings.raw.set")&&x.State=="unknown"&&x.ResolvedAtUtc==null,ct);
    internal static Task<bool> InFlightAsync(IoDbContext db,Guid device,CancellationToken ct)=>db.DeviceOperations.AnyAsync(x=>x.DeviceId==device&&
        (x.Kind=="clock.set"||x.Kind=="device.restart"||x.Kind=="network.ip.set"||x.Kind=="settings.idle.set"||x.Kind=="settings.raw.set")&&(x.State=="pending"||x.State=="running"),ct);
}
