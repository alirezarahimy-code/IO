namespace IO.Domain.Organization;

/// <summary>
/// One employee's primary organizational unit in a half-open date interval.
/// History is modeled as separate assignments rather than overwriting the past.
/// Dates are business dates, not UTC instants.
/// </summary>
public sealed class EmployeeOrgAssignment
{
    public Guid Id { get; }
    public Guid EmployeeId { get; }
    public Guid OrgUnitId { get; }
    public DateOnly ValidFrom { get; }
    public DateOnly? ValidUntilExclusive { get; }

    public EmployeeOrgAssignment(Guid id, Guid employeeId, Guid orgUnitId,
        DateOnly validFrom, DateOnly? validUntilExclusive)
    {
        if (id == Guid.Empty) throw new ArgumentException("Assignment ID is required.", nameof(id));
        if (employeeId == Guid.Empty) throw new ArgumentException("Employee ID is required.", nameof(employeeId));
        if (orgUnitId == Guid.Empty) throw new ArgumentException("Organizational unit ID is required.", nameof(orgUnitId));
        if (validUntilExclusive is not null && validUntilExclusive <= validFrom)
            throw new ArgumentException("The exclusive end must be after the start.", nameof(validUntilExclusive));

        Id = id;
        EmployeeId = employeeId;
        OrgUnitId = orgUnitId;
        ValidFrom = validFrom;
        ValidUntilExclusive = validUntilExclusive;
    }

    public bool Includes(DateOnly date) =>
        date >= ValidFrom && (ValidUntilExclusive is null || date < ValidUntilExclusive);

    public bool Overlaps(EmployeeOrgAssignment other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return EmployeeId == other.EmployeeId
            && (ValidUntilExclusive is null || other.ValidFrom < ValidUntilExclusive)
            && (other.ValidUntilExclusive is null || ValidFrom < other.ValidUntilExclusive);
    }
}
