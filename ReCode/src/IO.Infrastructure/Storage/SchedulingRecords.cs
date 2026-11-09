namespace IO.Infrastructure.Storage;

// Calendar and shift definitions are added as new versions; operational update
// and deletion policies remain subject to later domain and audit decisions.
public sealed class CalendarRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = null!;
    public byte[] Version { get; set; } = null!;
}
public sealed class CalendarDayRecord
{
    public Guid Id { get; set; }
    public Guid CalendarId { get; set; }
    public DateOnly Date { get; set; }
    // 0 working, 1 holiday, 2 rest; explicitly supplied, never inferred.
    public int Kind { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
public sealed class ShiftDefinitionRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SiteId { get; set; }
    public string Name { get; set; } = null!;
    public TimeOnly StartsAt { get; set; }
    public TimeOnly EndsAt { get; set; }
    public int EndDayOffset { get; set; }
    public byte[] Version { get; set; } = null!;
}
public sealed class WorkScheduleAssignmentRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid CalendarId { get; set; }
    public Guid ShiftId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidUntilExclusive { get; set; }
    public byte[] Version { get; set; } = null!;
}
