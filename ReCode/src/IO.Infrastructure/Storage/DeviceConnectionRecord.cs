namespace IO.Infrastructure.Storage;

// No credentials, adapter payloads, host exception strings or biometric data here.
public sealed class DeviceConnectionRecord
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public byte[] ExpectedDeviceVersion { get; set; } = null!;
    public int TimeoutSeconds { get; set; }
    public string State { get; set; } = "pending";
    public Guid? ServiceUserId { get; set; }
    public Guid? ClaimId { get; set; }
    public DateTimeOffset? ClaimedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public byte[] Version { get; set; } = null!;
}
