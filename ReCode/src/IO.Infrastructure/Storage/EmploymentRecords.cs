namespace IO.Infrastructure.Storage;

// Codes are provided by the organization; no legal/employment rules are inferred.
public sealed class EmploymentProfileRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public string StatusCode { get; set; } = null!;
    public string? Title { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidUntilExclusive { get; set; }
    public byte[] Version { get; set; } = null!;
}

public sealed class EmployeeSiteAssignmentRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid SiteId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidUntilExclusive { get; set; }
    public byte[] Version { get; set; } = null!;
}
