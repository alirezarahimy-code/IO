namespace IO.Infrastructure.Storage;

// Grouping metadata only. Tombstones preserve operation identity; no door controls.
public sealed class DeviceGroupRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = null!;
    public string CreationFingerprint { get; set; } = null!;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public byte[]? DeletedFromVersion { get; set; }
    public byte[] Version { get; set; } = null!;
}
