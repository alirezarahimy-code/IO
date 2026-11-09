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

    public Task<object> ListAsync(Guid actor, string kind, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Manage, "org.catalog.viewed", async token =>
        {
            if (offset is < 0 or > 1_000_000) throw new ArgumentException("Invalid offset.");
            const int size = 100;
            object items = kind switch
            {
                "organizations" => await db.Organizations.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(size).Select(x => new { x.Id, x.Name }).ToListAsync(token),
                "companies" => await db.Companies.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(size).Select(x => new { x.Id, x.OrganizationId, x.Name }).ToListAsync(token),
                "sites" => await db.Sites.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(size).Select(x => new { x.Id, x.CompanyId, x.Name, x.TimeZoneId }).ToListAsync(token),
                "units" => await db.OrgUnits.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(size).Select(x => new { x.Id, x.CompanyId, x.ParentId, x.Name, x.Version }).ToListAsync(token),
                "employees" => await db.Employees.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(size).Select(x => new { x.Id, x.CompanyId, x.PersonnelNumber, x.DisplayName, x.Version }).ToListAsync(token),
                "assignments" => await db.EmployeeAssignments.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(size).Select(x => new { x.Id, x.EmployeeId, x.CompanyId, x.OrgUnitId, x.ValidFrom, x.ValidUntilExclusive }).ToListAsync(token),
                _ => throw new ArgumentException("Unknown catalog type.")
            };
            return (new { kind, offset, pageSize = size, items }, actor);
        }, ct);

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

    public Task<bool> RenameEmployeeAsync(Guid actor, Guid employeeId, string displayName, byte[] expectedVersion, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "org.employee.renamed", async token =>
        {
            ValidateName(displayName, 200);
            if (employeeId == Guid.Empty || expectedVersion is not { Length: 8 }) throw new ArgumentException("Invalid employee or rowversion.");
            await transaction.LockAsync("IO.EmployeeRecord." + employeeId.ToString("N"), token);
            var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == employeeId, token) ?? throw new ArgumentException("Unknown employee.");
            if (!employee.Version.SequenceEqual(expectedVersion)) throw new DomainConflictException("Employee has changed.");
            employee.DisplayName = displayName;
            return (true, employeeId);
        }, ct);

    public Task<bool> ReparentUnitAsync(Guid actor, Guid companyId, Guid unitId, Guid? parentId, byte[] expectedVersion, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "org.unit.reparented", async token =>
        {
            if (companyId == Guid.Empty || unitId == Guid.Empty || expectedVersion is not { Length: 8 })
                throw new ArgumentException("Invalid company, unit or rowversion.");
            // All reparenting within the company shares this lock, so opposing
            // concurrent moves cannot each validate an outdated hierarchy.
            await transaction.LockAsync("IO.OrgTree." + companyId.ToString("N"), token);
            var unit = await db.OrgUnits.SingleOrDefaultAsync(x => x.Id == unitId && x.CompanyId == companyId, token)
                ?? throw new ArgumentException("Unknown unit or different company.");
            if (!unit.Version.SequenceEqual(expectedVersion)) throw new DomainConflictException("Unit has changed.");
            var cursor = parentId;
            var seen = new HashSet<Guid>();
            while (cursor is not null)
            {
                if (cursor == unitId || !seen.Add(cursor.Value)) throw new DomainConflictException("Organizational cycle detected.");
                var parent = await db.OrgUnits.AsNoTracking().Where(x => x.Id == cursor && x.CompanyId == companyId)
                    .Select(x => new { x.ParentId }).SingleOrDefaultAsync(token)
                    ?? throw new ArgumentException("Unknown parent or different company.");
                cursor = parent.ParentId;
            }
            unit.ParentId = parentId;
            return (true, unitId);
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
                throw new DomainConflictException("Overlapping primary assignment.");
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
                throw new DomainConflictException("Transfer requires an open assignment and a later effective date.");
            if (!await db.OrgUnits.AnyAsync(x => x.Id == newUnitId && x.CompanyId == original.CompanyId, token))
                throw new ArgumentException("Unknown unit or different company.");
            if (await db.EmployeeAssignments.AnyAsync(x => x.EmployeeId == employeeId && x.Id != assignmentId &&
                (x.ValidUntilExclusive == null || effective < x.ValidUntilExclusive), token))
                throw new DomainConflictException("Transfer overlaps an existing assignment.");
            original.ValidUntilExclusive = effective;
            var newId = Guid.NewGuid();
            db.EmployeeAssignments.Add(new() { Id = newId, EmployeeId = employeeId, CompanyId = original.CompanyId,
                OrgUnitId = newUnitId, ValidFrom = effective });
            return (newId, newId);
        }, ct);
}
