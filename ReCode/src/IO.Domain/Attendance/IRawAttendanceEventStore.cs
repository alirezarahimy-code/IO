namespace IO.Domain.Attendance;

/// <summary>
/// Contract only; the SQL implementation and transaction semantics do not yet exist.
/// There are intentionally no update/delete operations on raw events.
/// </summary>
public interface IRawAttendanceEventStore
{
    Task<RawEventAppendResult> AppendAsync(RawAttendanceEvent attendanceEvent, CancellationToken cancellationToken = default);
}

/// <summary>Outcomes are not interchangeable; do not report a conflict as success.</summary>
public enum RawEventAppendResult
{
    Appended,
    AlreadyRecorded,
    ConflictingExternalIdentity,
    IdentityRequiresReview
}
