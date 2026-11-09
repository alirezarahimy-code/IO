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
