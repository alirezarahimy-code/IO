namespace IO.Infrastructure.Storage;

// Device metadata is NOT proof of a verified adapter or live connection.
public sealed class DeviceRecord
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public Guid SourceId { get; set; }
    public string Number { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Model { get; set; } = null!;
    public string Profile { get; set; } = null!;
    public string? Host { get; set; }
    public int? Port { get; set; }
    public bool Enabled { get; set; }
    public string CreationFingerprint { get; set; } = null!;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] Version { get; set; } = null!;
}

// Separate record; no API serializes persistence entities or protected ciphertext.
public sealed class DeviceSecretRecord
{
    public Guid DeviceId { get; set; }
    public string ProtectedValue { get; set; } = null!;
}
