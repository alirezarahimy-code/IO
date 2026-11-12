namespace IO.Infrastructure.Storage;

public sealed class DeviceOperationRecord
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string Kind { get; set; } = null!;
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public byte[] ExpectedDeviceVersion { get; set; } = null!;
    public int TimeoutSeconds { get; set; }
    public string TimeZoneId { get; set; } = null!;
    public DateTimeOffset? TargetUtc { get; set; }
    public string? NetworkPlanJson { get; set; }
    public string State { get; set; } = "pending";
    public Guid? ServiceUserId { get; set; }
    public Guid? ClaimId { get; set; }
    public DateTimeOffset? ClaimedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string? ResultJson { get; set; }
    public Guid? ResolutionInfoId { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public byte[] Version { get; set; } = null!;
}
