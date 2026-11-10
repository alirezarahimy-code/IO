using IO.Domain.Scheduling;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Leaves;

/// <summary>
/// Daily and hourly leave requests decided in the same server-side workflow style
/// as corrections: requester submits with an idempotent operation, scoped managers
/// decide, and an approval posts a usage movement into the leave ledger.
/// </summary>
public sealed class LeaveRequests(IoDbContext db, PermissionTransaction transaction, EmployeeAccessReader access)
{
    public const string Submit = "leave.request.submit";
    public const string Decide = "leave.request.decide";
    private const string Never = "leave.never-self";
    public sealed record RequestItem(Guid Id, Guid LeaveTypeId, int Kind, DateOnly WorkDate,
        DateTimeOffset? FromAtUtc, DateTimeOffset? UntilAtUtcExclusive, int Minutes, string Reason,
        int State, DateTimeOffset CreatedAtUtc, int? Decision, string? DecisionReason, DateTimeOffset? DecisionAtUtc);

    private static void Text(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length>max || value.Any(char.IsControl)) throw new ArgumentException("Invalid text.");
    }
    private async Task<(Guid Employee, Guid Company)> OwnEmployeeAsync(Guid actor, CancellationToken token)
    {
        var found = await (from user in db.Users.AsNoTracking() join emp in db.Employees.AsNoTracking() on user.EmployeeId equals (Guid?)emp.Id
                           where user.Id==actor && user.IsActive select new{emp.Id, emp.CompanyId}).SingleOrDefaultAsync(token);
        return found is null ? throw new UnauthorizedAccessException() : (found.Id, found.CompanyId);
    }
    private IQueryable<EmployeeRecord> VisibleEmployees(Guid actor, DateOnly today) =>
        access.AuthorizedEmployees(actor, today, Never, Decide, Never);

    public Task<Guid> SubmitAsync(Guid actor, Guid operationId, Guid leaveTypeId, int kind, DateOnly workDate,
        DateTimeOffset? fromAtUtc, DateTimeOffset? untilAtUtcExclusive, string reason, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Submit, "leave.request.submitted", async token =>
        {
            Text(reason,500);
            var (employee, company) = await OwnEmployeeAsync(actor, token);
            if (operationId==Guid.Empty || kind is not (0 or 1)) throw new ArgumentException("Invalid operation or kind.");
            var year=PersianDates.FromGregorian(workDate);
            if (year.Year is < 1300 or > 1500) throw new ArgumentException("Unsupported Persian year.");
            var type=await (from t in db.LeaveTypes.AsNoTracking() where t.Id==leaveTypeId && t.CompanyId==company select t)
                .SingleOrDefaultAsync(token) ?? throw new ArgumentException("Unknown leave type for company.");
            if (kind==1)
            {
                if (fromAtUtc is null || untilAtUtcExclusive is null || fromAtUtc.Value.Offset!=TimeSpan.Zero ||
                    untilAtUtcExclusive.Value.Offset!=TimeSpan.Zero || untilAtUtcExclusive<=fromAtUtc ||
                    DateOnly.FromDateTime(fromAtUtc.Value.UtcDateTime)!=workDate) throw new ArgumentException("Invalid hourly leave window.");
                var end = DateOnly.FromDateTime(untilAtUtcExclusive.Value.UtcDateTime);
                var windowMinutes = (int)(untilAtUtcExclusive.Value-fromAtUtc.Value).TotalMinutes;
                if (end!=workDate && end!=workDate.AddDays(1) || windowMinutes < 1 || windowMinutes > type.MinutesPerDay)
                    throw new ArgumentException("Invalid hourly leave window.");
            }
            var minutes = kind==0 ? type.MinutesPerDay : (int)(untilAtUtcExclusive!.Value-fromAtUtc!.Value).TotalMinutes;
            await transaction.LockAsync("IO.LeaveOperation."+operationId.ToString("N"),token);
            var existing=await db.LeaveRequests.AsNoTracking().SingleOrDefaultAsync(x=>x.OperationId==operationId,token);
            if (existing is not null)
            {
                if (existing.EmployeeId!=employee || existing.LeaveTypeId!=leaveTypeId || existing.Kind!=kind || existing.WorkDate!=workDate ||
                    existing.FromAtUtc!=fromAtUtc || existing.UntilAtUtcExclusive!=untilAtUtcExclusive ||
                    !string.Equals(existing.Reason,reason,StringComparison.Ordinal)) throw new DomainConflictException("Operation identifier already used for different leave request.");
                return (existing.Id, employee);
            }
            var id=Guid.NewGuid();
            db.LeaveRequests.Add(new LeaveRequestRecord{Id=id,CompanyId=company,EmployeeId=employee,RequesterUserId=actor,
                LeaveTypeId=leaveTypeId,OperationId=operationId,Kind=kind,WorkDate=workDate,FromAtUtc=fromAtUtc,
                UntilAtUtcExclusive=untilAtUtcExclusive,Minutes=minutes,Reason=reason,State=0,CreatedAtUtc=DateTimeOffset.UtcNow});
            return (id, employee);
        },ct);

    public Task<object> ListOwnAsync(Guid actor, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Submit, "leave.own-requests.viewed", async token =>
        {
            if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var (employee, _) = await OwnEmployeeAsync(actor, token);
            var items=await (from request in db.LeaveRequests.AsNoTracking()
                             join decision in db.LeaveDecisions.AsNoTracking() on request.Id equals decision.RequestId into decisions
                             from decision in decisions.DefaultIfEmpty()
                             where request.RequesterUserId==actor
                             orderby request.CreatedAtUtc descending, request.Id
                             select new RequestItem(request.Id,request.LeaveTypeId,request.Kind,request.WorkDate,request.FromAtUtc,
                                 request.UntilAtUtcExclusive,request.Minutes,request.Reason,request.State,request.CreatedAtUtc,
                                 (int?)decision.Decision,decision.Reason,(DateTimeOffset?)decision.AtUtc))
                .Skip(offset).Take(50).ToListAsync(token);
            return (new {offset,pageSize=50,items},employee);
        },ct);

    public Task<object> ListInboxAsync(Guid actor, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Decide, "leave.inbox.viewed", async token =>
        {
            if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var today=DateOnly.FromDateTime(DateTime.UtcNow);
            var visible=VisibleEmployees(actor,today);
            var items=await (from request in db.LeaveRequests.AsNoTracking()
                             join employee in visible on request.EmployeeId equals employee.Id
                             where request.State==0 && request.RequesterUserId!=actor
                             orderby request.CreatedAtUtc, request.Id
                             select new {request.Id,request.CompanyId,request.EmployeeId,request.Kind,request.WorkDate,
                                 request.FromAtUtc,request.UntilAtUtcExclusive,request.Minutes,request.Reason,request.CreatedAtUtc})
                .Skip(offset).Take(50).ToListAsync(token);
            return (new {offset,pageSize=50,items},actor);
        },ct);

    public Task<object> DecideAsync(Guid actor, Guid id, int decision, string? reason, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Decide, "leave.request.decided", async token =>
        {
            if (decision is not (1 or 2)) throw new ArgumentException("Invalid leave decision.");
            if (reason is not null) Text(reason,500);
            var today=DateOnly.FromDateTime(DateTime.UtcNow);
            var request=await db.LeaveRequests.SingleOrDefaultAsync(x=>x.Id==id,token) ?? throw new ArgumentException("Unknown leave request.");
            if (request.State!=0) throw new DomainConflictException("Leave request already decided.");
            if (request.RequesterUserId==actor) throw new ArgumentException("Self-approval is not allowed.");
            var visible=VisibleEmployees(actor,today);
            if (!await visible.AnyAsync(e=>e.Id==request.EmployeeId,token)) throw new UnauthorizedAccessException();
            request.State=decision;
            db.LeaveDecisions.Add(new LeaveDecisionRecord{Id=Guid.NewGuid(),RequestId=request.Id,ActorUserId=actor,
                Decision=decision,Reason=string.IsNullOrWhiteSpace(reason)?null:reason,AtUtc=DateTimeOffset.UtcNow});
            if (decision==1)
            {
                // Approval consumes ledger minutes in the same transaction; the
                // pending-state guard above prevents a second posting.
                db.LeaveLedgerEntries.Add(new LeaveLedgerEntryRecord{Id=Guid.NewGuid(),CompanyId=request.CompanyId,
                    EmployeeId=request.EmployeeId,LeaveTypeId=request.LeaveTypeId,
                    PersianYear=PersianDates.FromGregorian(request.WorkDate).Year,EffectiveDate=request.WorkDate,
                    Kind=2,Minutes=-request.Minutes,DocumentNumber="LEAVE-"+id.ToString("N")[..12],
                    Description="مصرف مرخصی بر پایهٔ درخواست تأییدشده",CreatedByUserId=actor,CreatedAtUtc=DateTimeOffset.UtcNow});
            }
            return (new {request.Id,state=request.State},request.EmployeeId);
        },ct);

    public Task<object> CancelOwnAsync(Guid actor, Guid id, string? reason, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Submit, "leave.request.cancelled", async token =>
        {
            if (reason is not null) Text(reason,500);
            var request=await db.LeaveRequests.SingleOrDefaultAsync(x=>x.Id==id && x.RequesterUserId==actor,token)
                ?? throw new ArgumentException("Unknown own leave request.");
            if (request.State!=0) throw new DomainConflictException("Leave request already decided.");
            request.State=3;
            db.LeaveDecisions.Add(new LeaveDecisionRecord{Id=Guid.NewGuid(),RequestId=request.Id,ActorUserId=actor,
                Decision=3,Reason=string.IsNullOrWhiteSpace(reason)?null:reason,AtUtc=DateTimeOffset.UtcNow});
            return (new {request.Id,state=request.State},request.EmployeeId);
        },ct);
}
