namespace IO.Infrastructure.Storage;

/// <summary>Immutable experimental leave type, including an explicit day-to-minute conversion.</summary>
public sealed class LeaveTypeRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = null!;
    public string Symbol { get; set; } = null!;
    public int MinutesPerDay { get; set; }
    public bool CarryOverAllowed { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>Manually recorded experimental balance movement; never an approved leave request.</summary>
public sealed class LeaveLedgerEntryRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public int PersianYear { get; set; }
    public DateOnly EffectiveDate { get; set; }
    // 0 opening, 1 grant, 2 usage, 3 signed adjustment.
    public int Kind { get; set; }
    public int Minutes { get; set; }
    public string DocumentNumber { get; set; } = null!;
    public string Description { get; set; } = null!;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>Daily or hourly leave request moving through the same decision workflow as corrections.</summary>
public sealed class LeaveRequestRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid RequesterUserId { get; set; }
    public Guid LeaveTypeId { get; set; }
    // Idempotency key; a replayed operation with identical payload returns the same request.
    public Guid OperationId { get; set; }
    // 0 leave, 1 overtime (hourly only), 2 mission (daily or hourly).
    public int Category { get; set; }
    // 0 daily (one whole work day), 1 hourly (one window inside the work day).
    public int Kind { get; set; }
    public DateOnly WorkDate { get; set; }
    // Hourly only; both timestamps carry an explicit zero UTC offset.
    public DateTimeOffset? FromAtUtc { get; set; }
    public DateTimeOffset? UntilAtUtcExclusive { get; set; }
    public int Minutes { get; set; }
    public string Reason { get; set; } = null!;
    // 0 pending, 1 approved, 2 rejected, 3 cancelled.
    public int State { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public byte[] Version { get; set; } = null!;
}

/// <summary>Immutable record of the one decision (or requester cancellation) per leave request.</summary>
public sealed class LeaveDecisionRecord
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid ActorUserId { get; set; }
    public int Decision { get; set; } // 1 approve, 2 reject, 3 cancel
    public string? Reason { get; set; }
    public DateTimeOffset AtUtc { get; set; }
}
