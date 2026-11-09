namespace IO.Infrastructure.Storage;

// Immutable, explicitly configured experimental pairing policy. Not a payroll rule.
public sealed class ObservedRuleVersionRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = null!;
    public string Algorithm { get; set; } = "paired-v1";
    public int BeforeMinutes { get; set; }
    public int AfterMinutes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
}

// Append-only snapshot; subsequent recomputations create new rows.
public sealed class DailyCalculationRunRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid RuleVersionId { get; set; }
    public Guid SourceId { get; set; }
    public Guid ScheduleAssignmentId { get; set; }
    public DateOnly WorkDate { get; set; }
    public DateTimeOffset WindowStartUtc { get; set; }
    public DateTimeOffset WindowEndUtc { get; set; }
    public string InputSnapshotJson { get; set; } = null!;
    public string AnomaliesJson { get; set; } = null!;
    public long? ObservedTicks { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
}
