using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Security;

/// <summary>
/// Reads a minimal employee projection only when the active actor has a matching
/// explicit permission. This is a building block, not an authentication system:
/// actorId and businessDate must come from trusted server-side sources, never a
/// client request. Exact-unit scope is provisional until D09/D10 are decided.
/// </summary>
public sealed class EmployeeAccessReader(IoDbContext db)
{
    public const string ReadSelf = "employees.read.self";
    public const string ReadScoped = "employees.read.scoped";
    public const string ReadAll = "employees.read.all";

    public sealed record EmployeeView(Guid Id, Guid CompanyId, string PersonnelNumber, string DisplayName);

    public Task<EmployeeView?> FindAuthorizedAsync(Guid actorId, Guid employeeId, DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty || employeeId == Guid.Empty)
            return Task.FromResult<EmployeeView?>(null);

        return AuthorizedEmployees(actorId, businessDate, ReadSelf, ReadScoped, ReadAll)
            .Where(employee => employee.Id == employeeId)
            .Select(employee => new EmployeeView(employee.Id, employee.CompanyId, employee.PersonnelNumber, employee.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);
    }

    // Internal so callers must select fixed server-defined permission codes, not
    // client input. Composable as a subquery in the protected data read.
    internal IQueryable<EmployeeRecord> AuthorizedEmployees(Guid actorId, DateOnly businessDate, string readSelf, string readScoped, string readAll)
    {
        // Permission and scope checks are part of the same SQL statement as the
        // projection, so an unauthorized target is never materialized here.
        var codes = from ur in db.UserRoles
                    join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
                    join p in db.Permissions on rp.PermissionId equals p.Id
                    where ur.UserId == actorId
                    select p.Code;

        var query = from employee in db.Employees.AsNoTracking()
                    where actorId != Guid.Empty && db.Users.Any(user =>
                        user.Id == actorId && user.IsActive &&
                        // Permission codes are protocol identifiers, not human names.
                        // Binary collation plus byte length avoids SQL's case and
                        // trailing-space comparison rules granting a different code.
                        (codes.Any(code => EF.Functions.Collate(code, "Latin1_General_100_BIN2") == readAll &&
                                           EF.Functions.DataLength(code) == readAll.Length * 2) ||
                         (user.EmployeeId == employee.Id &&
                          codes.Any(code => EF.Functions.Collate(code, "Latin1_General_100_BIN2") == readSelf &&
                                            EF.Functions.DataLength(code) == readSelf.Length * 2)) ||
                         (codes.Any(code => EF.Functions.Collate(code, "Latin1_General_100_BIN2") == readScoped &&
                                           EF.Functions.DataLength(code) == readScoped.Length * 2) &&
                          db.UserOrgScopes.Any(scope =>
                              scope.UserId == user.Id &&
                              scope.ValidFrom <= businessDate &&
                              (scope.ValidUntilExclusive == null || businessDate < scope.ValidUntilExclusive) &&
                              db.EmployeeAssignments.Any(assignment =>
                                  assignment.EmployeeId == employee.Id &&
                                  assignment.OrgUnitId == scope.OrgUnitId &&
                                  assignment.ValidFrom <= businessDate &&
                                  (assignment.ValidUntilExclusive == null || businessDate < assignment.ValidUntilExclusive))))))
                    select employee;
        return query;
    }
}
