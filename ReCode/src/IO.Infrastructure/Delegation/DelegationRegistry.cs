using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Delegation;

/// <summary>Experimental information-only registry; no workflow authority is conferred.</summary>
public sealed class DelegationRegistry(IoDbContext db, PermissionTransaction transaction)
{
    public const string ManageSelf = "delegation.manage.self";
    public const string Receive = "delegation.receive";

    private async Task<(Guid EmployeeId, Guid CompanyId)> LinkedEmployee(Guid actor, CancellationToken token)
    {
        var linked = await (from u in db.Users join e in db.Employees on u.EmployeeId equals (Guid?)e.Id
                            where u.Id == actor && u.IsActive select new { e.Id, e.CompanyId }).SingleOrDefaultAsync(token)
                     ?? throw new UnauthorizedAccessException();
        return (linked.Id, linked.CompanyId);
    }

    // Eligibility is explicit and independent of any workflow authorization.
    private IQueryable<UserRecord> Eligible(Guid company, Guid principalEmployee) =>
        from u in db.Users.AsNoTracking()
        join e in db.Employees on u.EmployeeId equals (Guid?)e.Id
        where u.IsActive && e.CompanyId == company && e.Id != principalEmployee &&
            (from ur in db.UserRoles join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
             join p in db.Permissions on rp.PermissionId equals p.Id
             where ur.UserId == u.Id && EF.Functions.Collate(p.Code, "Latin1_General_100_BIN2") == Receive &&
                   EF.Functions.DataLength(p.Code) == Receive.Length * 2 select ur.UserId).Any()
        select u;

    public Task<object> CandidatesAsync(Guid actor, int offset, CancellationToken ct=default) =>
        transaction.ExecuteAsync<object>(actor, ManageSelf, "delegation.candidates.viewed", async token =>
        {
            if(offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var (employee, company)=await LinkedEmployee(actor,token);
            var items=await (from u in Eligible(company,employee) join e in db.Employees on u.EmployeeId equals (Guid?)e.Id
                             orderby u.UserName,u.Id select new { u.Id, u.UserName, EmployeeName=e.DisplayName }).Skip(offset).Take(50).ToListAsync(token);
            return (new {offset,pageSize=50,items},actor);
        },ct);

    public Task<object> ListAsync(Guid actor, int offset, CancellationToken ct=default) =>
        transaction.ExecuteAsync<object>(actor, ManageSelf, "delegation.own.viewed", async token =>
        {
            if(offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var (employee,company)=await LinkedEmployee(actor,token);
            var items=await (from d in db.Delegations.AsNoTracking()
                             join u in db.Users on d.DelegateUserId equals u.Id
                             join r in db.DelegationRevocations on d.Id equals r.DelegationId into revocations
                             from r in revocations.DefaultIfEmpty()
                             where d.PrincipalUserId==actor && d.PrincipalEmployeeId==employee && d.CompanyId==company
                             orderby d.CreatedAtUtc descending,d.Id
                             select new {d.Id,d.DelegateUserId,DelegateName=u.UserName,d.ValidFrom,d.ValidUntilExclusive,d.CreatedAtUtc,
                                 RevokedAtUtc=r==null?(DateTimeOffset?)null:r.RevokedAtUtc,RevocationReason=r==null?null:r.Reason}).Skip(offset).Take(50).ToListAsync(token);
            return (new {offset,pageSize=50,items,asOfUtcDate=DateOnly.FromDateTime(DateTime.UtcNow),grantsAuthority=false},actor);
        },ct);

    public Task<Guid> AssignAsync(Guid actor,Guid operationId,Guid deputy,DateOnly from,DateOnly until,CancellationToken ct=default) =>
        transaction.ExecuteAsync(actor, ManageSelf, "delegation.assigned", async token =>
        {
            if(operationId==Guid.Empty || deputy==Guid.Empty || from>=until || until.DayNumber-from.DayNumber>366) throw new ArgumentException("Invalid interval or identity.");
            var (employee,company)=await LinkedEmployee(actor,token);
            await transaction.LockAsync("IO.DelegationOperation."+operationId.ToString("N"),token);
            var prior=await db.Delegations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==operationId,token);
            if(prior is not null)
            {
                if(prior.PrincipalUserId!=actor || prior.PrincipalEmployeeId!=employee || prior.CompanyId!=company || prior.DelegateUserId!=deputy || prior.ValidFrom!=from || prior.ValidUntilExclusive!=until)
                    throw new DomainConflictException("Operation id already used.");
                return (operationId,operationId);
            }
            if(from<DateOnly.FromDateTime(DateTime.UtcNow)) throw new ArgumentException("Cannot create past assignments.");
            var candidate=await Eligible(company,employee).SingleOrDefaultAsync(x=>x.Id==deputy,token)
                ?? throw new ArgumentException("Candidate not eligible.");
            await transaction.LockAsync("IO.DelegationPrincipal."+actor.ToString("N"),token);
            if(await db.Delegations.AnyAsync(x=>x.PrincipalUserId==actor && x.ValidFrom<until && from<x.ValidUntilExclusive &&
                    !db.DelegationRevocations.Any(r=>r.DelegationId==x.Id),token))
                throw new DomainConflictException("Overlapping assignment exists.");
            db.Delegations.Add(new DelegationRecord { Id=operationId,PrincipalUserId=actor,PrincipalEmployeeId=employee,CompanyId=company,
                DelegateUserId=deputy,DelegateEmployeeId=candidate.EmployeeId!.Value,ValidFrom=from,ValidUntilExclusive=until,CreatedAtUtc=DateTimeOffset.UtcNow });
            return (operationId,operationId);
        },ct);

    public Task<bool> RevokeAsync(Guid actor,Guid id,string reason,CancellationToken ct=default) =>
        transaction.ExecuteAsync(actor,ManageSelf,"delegation.revoked",async token=>
        {
            if(id==Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length>500 || reason.Any(char.IsControl)) throw new ArgumentException("Invalid reason.");
            var (employee,company)=await LinkedEmployee(actor,token);
            await transaction.LockAsync("IO.DelegationPrincipal."+actor.ToString("N"),token);
            var d=await db.Delegations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id && x.PrincipalUserId==actor && x.PrincipalEmployeeId==employee && x.CompanyId==company,token)
                ?? throw new UnauthorizedAccessException();
            var existing=await db.DelegationRevocations.AsNoTracking().SingleOrDefaultAsync(x=>x.DelegationId==d.Id,token);
            if(existing is not null)
            {
                if(existing.ActorUserId!=actor || !string.Equals(existing.Reason,reason,StringComparison.Ordinal)) throw new DomainConflictException("Already revoked with different reason.");
                return (true,id);
            }
            db.DelegationRevocations.Add(new DelegationRevocationRecord {DelegationId=id,ActorUserId=actor,RevokedAtUtc=DateTimeOffset.UtcNow,Reason=reason});
            return (true,id);
        },ct);
}
