using System.Globalization;
using System.Security.Cryptography;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Devices;

public sealed record DeviceCommunicationKey(int Value)
{
    public override string ToString() => "DeviceCommunicationKey [redacted]";
}

/// <summary>Private service-only credential access for an owned, unchanged, active connection claim.</summary>
public sealed class DeviceConnectionCredential(IoDbContext db, PermissionTransaction transaction, IDataProtectionProvider protection, TimeProvider clock)
{
    public Task<DeviceCommunicationKey?> LoadAsync(Guid service, DeviceConnections.Claim claim, bool allowDefaultCommunicationKey, CancellationToken ct = default) =>
        transaction.ExecuteAsync<DeviceCommunicationKey?>(service, DeviceConnections.Execute, "device-connection.credential-used", async token =>
        {
            await transaction.LockAsync("IO.DeviceConnections", token);
            var job = await db.DeviceConnections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == claim.Id, token);
            if (job is null || job.State != "running" || job.ClaimId != claim.Token || job.ServiceUserId != service || job.DeviceId != claim.DeviceId || job.ClaimedAtUtc < clock.GetUtcNow().AddMinutes(-2))
                throw new DomainConflictException("Claim no longer active.");
            var device = await db.Devices.AsNoTracking().SingleAsync(x => x.Id == job.DeviceId, token);
            if (claim.Profile!=device.Profile || claim.Host!=device.Host || claim.Port!=device.Port || claim.TimeoutSeconds!=job.TimeoutSeconds || !device.Enabled || !device.Version.SequenceEqual(job.ExpectedDeviceVersion) || !await transaction.HasPermissionAsync(job.RequestedByUserId, DeviceConnections.Test, token))
                return (null, job.Id);
            var ciphertext = await db.DeviceSecrets.Where(x => x.DeviceId == job.DeviceId).Select(x => x.ProtectedValue).SingleOrDefaultAsync(token);
            if (ciphertext is null) return (allowDefaultCommunicationKey ? new DeviceCommunicationKey(0) : null, job.Id);
            try
            {
                var text = protection.CreateProtector("IO.Devices.Credential.v1", job.DeviceId.ToString("N")).Unprotect(ciphertext);
                // ZK COM API takes an Int32 communication key, not an arbitrary password string.
                return (text.Length > 0 && text.All(c => c is >= '0' and <= '9') && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var key)
                    ? new DeviceCommunicationKey(key) : null, job.Id);
            }
            catch (CryptographicException) { return (null, job.Id); }
        }, ct);
}
