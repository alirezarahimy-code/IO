namespace IO.Infrastructure.Storage;

// Single-approver experimental policy. It is versioned by creating a new row.
public sealed class CorrectionPolicyRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = null!;
    public Guid ApproverUserId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

// One proposed missing punch; raw attendance events are never modified.
public sealed class CorrectionRequestRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid RequesterUserId { get; set; }
    public Guid PolicyId { get; set; }
    // Null means no recorded setting reference (unconfigured or predates this feature).
    public Guid? CorrectionSettingsId { get; set; }
    public DateOnly WorkDate { get; set; }
    public DateTimeOffset ProposedAtUtc { get; set; }
    public int PunchKind { get; set; }
    public string Reason { get; set; } = null!;
    // 0 pending, 1 approved, 2 rejected, 3 cancelled.
    public int State { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public byte[] Version { get; set; } = null!;
}

public sealed class CorrectionDecisionRecord
{
    public int Sequence { get; set; } = 1;
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid ActorUserId { get; set; }
    public int Decision { get; set; } // 1 approve, 2 reject, 3 cancel
    public string? Reason { get; set; }
    public DateTimeOffset AtUtc { get; set; }
}
