using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Devices;

/// <summary>Shared safety fence, not a grant or a generic native dispatch list.
/// Call under IO.DeviceConnections transaction lock; recovery reads remain possible.</summary>
internal static class DeviceWriteSafety
{
    internal static bool IsWrite(string kind)=>kind is "clock.set" or "device.restart" or "network.ip.set" or "settings.idle.set" or "settings.raw.set" or "settings.key.set" or "settings.profile.set";
    internal static async Task<bool> UnreviewedAsync(IoDbContext db,Guid device,CancellationToken ct,Guid? ignoredBiometric=null,Guid? ignoredErase=null)=>await db.DeviceAttendanceErases.IgnoreQueryFilters().AnyAsync(x=>x.DeviceId==device&&x.State=="unknown"&&x.Id!=ignoredErase&&!db.DeviceAttendanceErases.IgnoreQueryFilters().Any(r=>r.RecoveryOfId==x.Id&&r.State=="released"),ct)||await db.DeviceBiometricTransfers.IgnoreQueryFilters().AnyAsync(x=>x.TargetDeviceId==device&&x.State=="unknown"&&x.Id!=ignoredBiometric&&!db.DeviceBiometricRecoveries.IgnoreQueryFilters().Any(r=>r.TransferId==x.Id&&r.ApprovedAtUtc!=null),ct)||await db.RemoteEnrollments.AnyAsync(x=>x.DeviceId==device&&x.State=="unknown"&&!db.RemoteEnrollmentRecoveries.Any(r=>r.EnrollmentId==x.Id&&r.ApprovedAtUtc!=null),ct)||await db.DeviceOperations.AnyAsync(x=>x.DeviceId==device&&
        (x.Kind=="clock.set"||x.Kind=="device.restart"||x.Kind=="network.ip.set"||x.Kind=="settings.idle.set"||x.Kind=="settings.raw.set"||x.Kind=="settings.key.set"||x.Kind=="settings.profile.set")&&x.State=="unknown"&&x.ResolvedAtUtc==null,ct);
    internal static async Task<bool> InFlightAsync(IoDbContext db,Guid device,CancellationToken ct)=>await db.DeviceAttendanceErases.IgnoreQueryFilters().AnyAsync(x=>x.DeviceId==device&&(x.State=="proposed"||x.State=="pending"||x.State=="running"),ct)||await db.DeviceBiometricTransfers.IgnoreQueryFilters().AnyAsync(x=>(x.SourceDeviceId==device||x.TargetDeviceId==device)&&(x.State=="pending"||x.State=="running"),ct)||await db.RemoteEnrollments.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceOperations.AnyAsync(x=>x.DeviceId==device&&
        (x.Kind=="clock.set"||x.Kind=="device.restart"||x.Kind=="network.ip.set"||x.Kind=="settings.idle.set"||x.Kind=="settings.raw.set"||x.Kind=="settings.key.set"||x.Kind=="settings.profile.set")&&(x.State=="pending"||x.State=="running"),ct);
}
