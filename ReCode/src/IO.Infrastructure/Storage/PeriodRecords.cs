namespace IO.Infrastructure.Storage;

/// <summary>A historical period draft, not a lock or an approval.</summary>
public sealed class PeriodDraftRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly EndExclusive { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>Immutable diagnostic evidence, never a period approval or lock.</summary>
public sealed class PeriodDiagnosticRecord
{
    public Guid Id { get; set; }
    public Guid DraftId { get; set; }
    public long? InputRevision { get; set; }
    public string EvidenceJson { get; set; } = null!;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
