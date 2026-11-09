using IO.Domain.Organization;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Organization;

/// <summary>
/// Narrow, global organization management. Provisional D10 policy: one primary
/// unit per employee at a time, with half-open business-date intervals. All
/// cooperating writers must use the same transaction and employee lock.
/// </summary>
public sealed class OrganizationManagement(IoDbContext db, PermissionTransaction transaction)
{
    public const string Manage = "organization.manage";

    private static void ValidateName(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(char.IsControl))
            throw new ArgumentException("Invalid name or identifier.");
    }

    public Task<Guid> CreateOrganizationAsync(Guid actor, string name, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "org.organization.created", _ =>
        {
            ValidateName(name, 200);
            var id = Guid.NewGuid(); db.Organizations.Add(new() { Id = id, Name = name });
            return Task.FromResult((id, id));
        }, ct);

    public Task<Guid> CreateCompanyAsync(Guid actor, Guid organizationId, string name, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "org.company.created", async token =>
        {
            ValidateName(name, 200);
            if (!await db.Organizations.AnyAsync(x => x.Id == organizationId, token)) throw new ArgumentException("Unknown organization.");
            var id = Guid.NewGuid(); db.Companies.Add(new() { Id = id, OrganizationId = organizationId, Name = name });
            return (id, id);
        }, ct);

    public Task<Guid> CreateSiteAsync(Guid actor, Guid companyId, string name, string timeZoneId, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "org.site.created", async token =>
        {
            ValidateName(name, 200); ValidateName(timeZoneId, 100);
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _) || !await db.Companies.AnyAsync(x => x.Id == companyId, token))
                throw new ArgumentException("Unknown time zone or company.");
            var id = Guid.NewGuid(); db.Sites.Add(new() { Id = id, CompanyId = companyId, Name = name, TimeZoneId = timeZoneId });
            return (id, id);
        }, ct);

    public Task<Guid> CreateUnitAsync(Guid actor, Guid companyId, Guid? parentId, string name, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "org.unit.created", async token =>
        {
            ValidateName(name, 200);
            if (!await db.Companies.AnyAsync(x => x.Id == companyId, token) ||
                (parentId is not null && !await db.OrgUnits.AnyAsync(x => x.Id == parentId && x.CompanyId == companyId, token)))
                throw new ArgumentException("Unknown company or parent in another company.");
            var id = Guid.NewGuid(); db.OrgUnits.Add(new() { Id = id, CompanyId = companyId, ParentId = parentId, Name = name });
            return (id, id);
        }, ct);

    public Task<Guid> CreateEmployeeAsync(Guid actor, Guid companyId, string personnelNumber, string displayName, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "org.employee.created", async token =>
        {
            ValidateName(personnelNumber, 80); ValidateName(displayName, 200);
            if (!await db.Companies.AnyAsync(x => x.Id == companyId, token)) throw new ArgumentException("Unknown company.");
            var id = Guid.NewGuid();
            db.Employees.Add(new() { Id = id, CompanyId = companyId, PersonnelNumber = personnelNumber, DisplayName = displayName });
            return (id, id);
        }, ct);

    public Task<Guid> AddAssignmentAsync(Guid actor, Guid employeeId, Guid unitId, DateOnly from, DateOnly? until, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "org.assignment.created", async token =>
        {
            var id = Guid.NewGuid();
            _ = new EmployeeOrgAssignment(id, employeeId, unitId, from, until);
            await transaction.LockAsync("IO.EmployeeAssignments." + employeeId.ToString("N"), token);
            var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == employeeId, token) ?? throw new ArgumentException("Unknown employee.");
            if (!await db.OrgUnits.AnyAsync(x => x.Id == unitId && x.CompanyId == employee.CompanyId, token))
                throw new ArgumentException("Unknown unit or different company.");
            if (await db.EmployeeAssignments.AnyAsync(x => x.EmployeeId == employeeId &&
                (until == null || x.ValidFrom < until) && (x.ValidUntilExclusive == null || from < x.ValidUntilExclusive), token))
                throw new InvalidOperationException("Overlapping primary assignment.");
            db.EmployeeAssignments.Add(new() { Id = id, EmployeeId = employeeId, CompanyId = employee.CompanyId,
                OrgUnitId = unitId, ValidFrom = from, ValidUntilExclusive = until });
            return (id, id);
        }, ct);

    public Task<Guid> TransferAsync(Guid actor, Guid employeeId, Guid assignmentId, Guid newUnitId, DateOnly effective, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "org.transfer." + assignmentId.ToString("N"), async token =>
        {
            if (employeeId == Guid.Empty) throw new ArgumentException("Invalid employee.");
            await transaction.LockAsync("IO.EmployeeAssignments." + employeeId.ToString("N"), token);
            var original = await db.EmployeeAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId && x.EmployeeId == employeeId, token)
                ?? throw new ArgumentException("Unknown employee assignment.");
            if (original.ValidUntilExclusive is not null || effective <= original.ValidFrom)
                throw new InvalidOperationException("Transfer requires an open assignment and a later effective date.");
            if (!await db.OrgUnits.AnyAsync(x => x.Id == newUnitId && x.CompanyId == original.CompanyId, token))
                throw new ArgumentException("Unknown unit or different company.");
            if (await db.EmployeeAssignments.AnyAsync(x => x.EmployeeId == employeeId && x.Id != assignmentId &&
                (x.ValidUntilExclusive == null || effective < x.ValidUntilExclusive), token))
                throw new InvalidOperationException("Transfer overlaps an existing assignment.");
            original.ValidUntilExclusive = effective;
            var newId = Guid.NewGuid();
            db.EmployeeAssignments.Add(new() { Id = newId, EmployeeId = employeeId, CompanyId = original.CompanyId,
                OrgUnitId = newUnitId, ValidFrom = effective });
            return (newId, newId);
        }, ct);
}
