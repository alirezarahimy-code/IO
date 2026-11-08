namespace IO.Infrastructure.Storage;

// Persistence records are not API contracts. Business authorization, input
// validation and lifecycle policies belong to their respective later phases.
public sealed class OrganizationRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public byte[] Version { get; set; } = null!;
}
public sealed class CompanyRecord
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = null!;
    public byte[] Version { get; set; } = null!;
}
public sealed class SiteRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = null!;
    public string TimeZoneId { get; set; } = null!;
    public byte[] Version { get; set; } = null!;
}
public sealed class OrgUnitRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = null!;
    public byte[] Version { get; set; } = null!;
}
public sealed class EmployeeRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string PersonnelNumber { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public byte[] Version { get; set; } = null!;
}
public sealed class EmployeeAssignmentRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid OrgUnitId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidUntilExclusive { get; set; }
    public byte[] Version { get; set; } = null!;
}
public sealed class UserRecord
{
    public Guid Id { get; set; }
    public Guid? EmployeeId { get; set; }
    public string UserName { get; set; } = null!;
    // No password storage or default accounts are implemented in this phase.
    public bool IsActive { get; set; }
    public byte[] Version { get; set; } = null!;
}
public sealed class RoleRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public byte[] Version { get; set; } = null!;
}
public sealed class PermissionRecord
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public byte[] Version { get; set; } = null!;
}
public sealed class UserRoleRecord
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}
public sealed class RolePermissionRecord
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}
public sealed class UserOrgScopeRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid OrgUnitId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidUntilExclusive { get; set; }
    public byte[] Version { get; set; } = null!;
}
public sealed class EventSourceRecord
{
    public Guid Id { get; set; }
    public Guid? SiteId { get; set; }
    public string Name { get; set; } = null!;
    // Source type and device protocols are deliberately not guessed.
    public byte[] Version { get; set; } = null!;
}
public sealed class RawAttendanceEventRecord
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string? ExternalEventId { get; set; }
    public string? SourceEmployeeIdentifier { get; set; }
    public DateTimeOffset? OccurredAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public string? SourceTimeZoneId { get; set; }
    public string? ReportedEventType { get; set; }
    public byte[] SourcePayload { get; set; } = null!;
}
