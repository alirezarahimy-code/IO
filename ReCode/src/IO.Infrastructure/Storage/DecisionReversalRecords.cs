namespace IO.Infrastructure.Storage;

// Client operation Id is the retry key; decision and compensation evidence are immutable.
public sealed class CorrectionDecisionReversalRecord
{
    public Guid Id { get; set; }
    public Guid DecisionId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Reason { get; set; } = null!;
    public DateTimeOffset AtUtc { get; set; }
}
public sealed class LeaveDecisionReversalRecord
{
    public Guid Id { get; set; }
    public Guid DecisionId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? OriginalLedgerEntryId { get; set; }
    public Guid? CompensationLedgerEntryId { get; set; }
    public string Reason { get; set; } = null!;
    public DateTimeOffset AtUtc { get; set; }
}
