namespace IO.Domain.Organization;

/// <summary>
/// Validates one employee's primary unit assignments in memory. The persistence
/// implementation must also serialize concurrent writes for the same employee.
/// </summary>
public sealed class EmployeeAssignmentTimeline
{
    private readonly List<EmployeeOrgAssignment> assignments = [];
    public Guid EmployeeId { get; }

    public EmployeeAssignmentTimeline(Guid employeeId)
    {
        if (employeeId == Guid.Empty) throw new ArgumentException("Employee ID is required.", nameof(employeeId));
        EmployeeId = employeeId;
    }

    public IReadOnlyList<EmployeeOrgAssignment> Assignments => assignments.AsReadOnly();

    public void Add(EmployeeOrgAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (assignment.EmployeeId != EmployeeId)
            throw new ArgumentException("The assignment belongs to a different employee.", nameof(assignment));
        if (assignments.Any(existing => existing.Id == assignment.Id))
            throw new InvalidOperationException("The assignment ID already exists.");
        if (assignments.Any(existing => existing.Overlaps(assignment)))
            throw new InvalidOperationException("Primary organizational assignments cannot overlap.");
        assignments.Add(assignment);
    }

    public EmployeeOrgAssignment? At(DateOnly date) => assignments.SingleOrDefault(x => x.Includes(date));
}
