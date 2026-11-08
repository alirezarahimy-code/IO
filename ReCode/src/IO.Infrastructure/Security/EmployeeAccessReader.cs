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

        // Permission and scope checks are part of the same SQL statement as the
        // projection, so an unauthorized target is never materialized here.
        var codes = from ur in db.UserRoles
                    join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
                    join p in db.Permissions on rp.PermissionId equals p.Id
                    where ur.UserId == actorId
                    select p.Code;

        var query = from employee in db.Employees.AsNoTracking()
                    where employee.Id == employeeId && db.Users.Any(user =>
                        user.Id == actorId && user.IsActive &&
                        // Permission codes are protocol identifiers, not human names.
                        // Binary collation plus byte length avoids SQL's case and
                        // trailing-space comparison rules granting a different code.
                        (codes.Any(code => EF.Functions.Collate(code, "Latin1_General_100_BIN2") == ReadAll &&
                                           EF.Functions.DataLength(code) == ReadAll.Length * 2) ||
                         (user.EmployeeId == employee.Id &&
                          codes.Any(code => EF.Functions.Collate(code, "Latin1_General_100_BIN2") == ReadSelf &&
                                            EF.Functions.DataLength(code) == ReadSelf.Length * 2)) ||
                         (codes.Any(code => EF.Functions.Collate(code, "Latin1_General_100_BIN2") == ReadScoped &&
                                           EF.Functions.DataLength(code) == ReadScoped.Length * 2) &&
                          db.UserOrgScopes.Any(scope =>
                              scope.UserId == user.Id &&
                              scope.ValidFrom <= businessDate &&
                              (scope.ValidUntilExclusive == null || businessDate < scope.ValidUntilExclusive) &&
                              db.EmployeeAssignments.Any(assignment =>
                                  assignment.EmployeeId == employee.Id &&
                                  assignment.OrgUnitId == scope.OrgUnitId &&
                                  assignment.ValidFrom <= businessDate &&
                                  (assignment.ValidUntilExclusive == null || businessDate < assignment.ValidUntilExclusive))))))
                    select new EmployeeView(employee.Id, employee.CompanyId, employee.PersonnelNumber, employee.DisplayName);
        return query.SingleOrDefaultAsync(cancellationToken);
    }
}
