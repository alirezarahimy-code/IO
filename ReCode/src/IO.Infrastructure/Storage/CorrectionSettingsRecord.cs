namespace IO.Infrastructure.Storage;

// Append-only company setting. 0 means no age limit; only missing-punch submissions use it.
public sealed class CorrectionSettingsRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int Revision { get; set; }
    public int MaxRequestAgeDays { get; set; }
    public string Reason { get; set; } = null!;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
