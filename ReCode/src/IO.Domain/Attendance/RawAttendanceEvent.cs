namespace IO.Domain.Attendance;

/// <summary>
/// Immutable envelope for a received raw event. Missing or invalid source fields
/// can remain unresolved; the received payload must be preserved for review.
/// Processing state and corrections belong to separate records.
/// </summary>
public sealed class RawAttendanceEvent
{
    private readonly byte[] sourcePayload;
    public Guid Id { get; }
    public Guid SourceId { get; }
    public string? ExternalEventId { get; }
    public string? SourceEmployeeIdentifier { get; }
    public DateTimeOffset? OccurredAtUtc { get; }
    public DateTimeOffset ReceivedAtUtc { get; }
    public string? SourceTimeZoneId { get; }
    public string? ReportedEventType { get; }

    public RawAttendanceEvent(Guid id, Guid sourceId, string? externalEventId,
        string? sourceEmployeeIdentifier, DateTimeOffset? occurredAt,
        DateTimeOffset receivedAt, string? sourceTimeZoneId,
        string? reportedEventType, ReadOnlySpan<byte> sourcePayload)
    {
        if (id == Guid.Empty) throw new ArgumentException("Event ID is required.", nameof(id));
        if (sourceId == Guid.Empty) throw new ArgumentException("Source ID is required.", nameof(sourceId));
        if (externalEventId is not null && string.IsNullOrWhiteSpace(externalEventId))
            throw new ArgumentException("An external ID must be meaningful or null.", nameof(externalEventId));

        Id = id;
        SourceId = sourceId;
        ExternalEventId = externalEventId;
        SourceEmployeeIdentifier = sourceEmployeeIdentifier;
        OccurredAtUtc = occurredAt?.ToUniversalTime();
        ReceivedAtUtc = receivedAt.ToUniversalTime();
        SourceTimeZoneId = sourceTimeZoneId;
        ReportedEventType = reportedEventType;
        this.sourcePayload = sourcePayload.ToArray();
    }

    // Return a copy so that callers cannot mutate the stored raw bytes.
    public byte[] GetSourcePayload() => (byte[])sourcePayload.Clone();
}
