namespace IO.Infrastructure.Storage;

// Exact source identifiers are not global personnel numbers. Mappings use UTC
// half-open intervals and can only be written by the authorized service.
public sealed class SourceEmployeeMappingRecord
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string SourcePersonId { get; set; } = null!;
    public Guid EmployeeId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset? ValidUntilUtcExclusive { get; set; }
    public byte[] Version { get; set; } = null!;
}

// A conflicting receipt cannot enter the uniquely indexed raw stream. Keep its
// full envelope and payload separately without modifying the original event.
public sealed class RawEventConflictRecord
{
    public Guid Id { get; set; }
    public Guid OriginalEventId { get; set; }
    public Guid SourceId { get; set; }
    public string ExternalEventId { get; set; } = null!;
    public string? SourceEmployeeIdentifier { get; set; }
    public DateTimeOffset? OccurredAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public string? SourceTimeZoneId { get; set; }
    public string? ReportedEventType { get; set; }
    public byte[] SourcePayload { get; set; } = null!;
}

/// <summary>Append-only operator note; a note does not resolve a collision.</summary>
public sealed class RawConflictNoteRecord
{
    public Guid Id { get; set; }
    public Guid ConflictId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string Note { get; set; } = null!;
}

/// <summary>Audited change of an open mapping, preserving the old interval and new identity.</summary>
public sealed class SourceMappingChangeRecord
{
    public Guid Id { get; set; }
    public Guid PreviousMappingId { get; set; }
    public Guid NewMappingId { get; set; }
    public DateTimeOffset EffectiveAtUtc { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string Reason { get; set; } = null!;
}
