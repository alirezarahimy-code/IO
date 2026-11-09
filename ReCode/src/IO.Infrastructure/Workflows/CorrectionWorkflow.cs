using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Workflows;

/// <summary>Experimental one-step missing-punch workflow; not a leave or payroll workflow.</summary>
public sealed class CorrectionWorkflow(IoDbContext db, PermissionTransaction transaction, EmployeeAccessReader access)
{
    public const string Configure = "correction.configure";
    public const string Submit = "correction.submit.self";
    public const string Decide = "correction.decide";
    public const string DecideScoped = "correction.decide.scoped";
    public const string DecideAll = "correction.decide.all";
    private static void Reason(string? value, bool required)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || value is { Length: > 500 } || (value?.Any(char.IsControl) ?? false))
            throw new ArgumentException("Invalid reason.");
    }

    public Task<Guid> CreatePolicyAsync(Guid actor, Guid companyId, Guid approverUserId, string name, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Configure, "correction.policy.created", async token =>
        {
            if (companyId == Guid.Empty || approverUserId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl) ||
                !await db.Companies.AnyAsync(x => x.Id == companyId, token) || !await db.Users.AnyAsync(x => x.Id == approverUserId && x.IsActive, token))
                throw new ArgumentException("Invalid policy.");
            var id = Guid.NewGuid();
            db.CorrectionPolicies.Add(new() { Id = id, CompanyId = companyId, Name = name, ApproverUserId = approverUserId,
                CreatedByUserId = actor, CreatedAtUtc = DateTimeOffset.UtcNow });
            return (id, id);
        }, ct);

    public Task<Guid> SubmitAsync(Guid actor, Guid policyId, DateOnly workDate, DateTimeOffset proposedUtc, int kind, string reason, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Submit, "correction.request.created", async token =>
        {
            Reason(reason, true);
            if (policyId == Guid.Empty || proposedUtc.Offset != TimeSpan.Zero || kind is < 0 or > 1) throw new ArgumentException("Invalid proposed punch.");
            var employee = await (from user in db.Users join e in db.Employees on user.EmployeeId equals (Guid?)e.Id
                                  where user.Id == actor && user.IsActive select e).SingleOrDefaultAsync(token)
                ?? throw new ArgumentException("Active account must be linked to an employee.");
            var policy = await db.CorrectionPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == policyId && x.CompanyId == employee.CompanyId, token)
                ?? throw new ArgumentException("Unknown policy or different company.");
            if (policy.ApproverUserId == actor || await db.Users.AnyAsync(x => x.Id == policy.ApproverUserId && x.EmployeeId == employee.Id, token))
                throw new ArgumentException("Self-approval policy cannot be used.");
            if (!await db.Users.AnyAsync(x => x.Id == policy.ApproverUserId && x.IsActive, token))
                throw new DomainConflictException("Policy approver is inactive.");
            var id = Guid.NewGuid();
            db.CorrectionRequests.Add(new() { Id = id, CompanyId = employee.CompanyId, EmployeeId = employee.Id,
                RequesterUserId = actor, PolicyId = policyId, WorkDate = workDate, ProposedAtUtc = proposedUtc,
                PunchKind = kind, Reason = reason, State = 0, CreatedAtUtc = DateTimeOffset.UtcNow });
            return (id, id);
        }, ct);

    public Task<bool> DecideAsync(Guid actor, Guid requestId, bool approve, string? reason, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Decide, "correction.request.decided", async token =>
        {
            Reason(reason, !approve);
            if (requestId == Guid.Empty) throw new ArgumentException("Invalid request.");
            await transaction.LockAsync("IO.Correction." + requestId.ToString("N"), token);
            var authorized = access.AuthorizedEmployees(actor, DateOnly.FromDateTime(DateTime.UtcNow),
                "correction.never-self", DecideScoped, DecideAll);
            var request = await (from candidate in db.CorrectionRequests
                                 join policy in db.CorrectionPolicies on candidate.PolicyId equals policy.Id
                                 where candidate.Id == requestId && policy.ApproverUserId == actor && candidate.RequesterUserId != actor &&
                                       !db.Users.Any(x => x.Id == actor && x.EmployeeId == candidate.EmployeeId) &&
                                       authorized.Any(x => x.Id == candidate.EmployeeId)
                                 select candidate).AsTracking().SingleOrDefaultAsync(token)
                ?? throw new UnauthorizedAccessException();
            if (request.State != 0 || await db.CorrectionDecisions.AnyAsync(x => x.RequestId == requestId, token))
                throw new DomainConflictException("Request has already been decided or cancelled.");
            request.State = approve ? 1 : 2;
            db.CorrectionDecisions.Add(new() { Id = Guid.NewGuid(), RequestId = requestId, ActorUserId = actor,
                Decision = request.State, Reason = reason, AtUtc = DateTimeOffset.UtcNow });
            return (true, requestId);
        }, ct);

    public Task<bool> CancelAsync(Guid actor, Guid requestId, string reason, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Submit, "correction.request.cancelled", async token =>
        {
            Reason(reason, true);
            await transaction.LockAsync("IO.Correction." + requestId.ToString("N"), token);
            var request = await db.CorrectionRequests.SingleOrDefaultAsync(x => x.Id == requestId && x.RequesterUserId == actor, token)
                ?? throw new UnauthorizedAccessException();
            if (request.State != 0 || await db.CorrectionDecisions.AnyAsync(x => x.RequestId == requestId, token))
                throw new DomainConflictException("Request has already been decided or cancelled.");
            request.State = 3;
            db.CorrectionDecisions.Add(new() { Id = Guid.NewGuid(), RequestId = requestId, ActorUserId = actor,
                Decision = 3, Reason = reason, AtUtc = DateTimeOffset.UtcNow });
            return (true, requestId);
        }, ct);

    public Task<object> ListPoliciesAsync(Guid actor, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Submit, "correction.policies.viewed", async token =>
        {
            if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var items = await (from user in db.Users.AsNoTracking()
                               join employee in db.Employees on user.EmployeeId equals (Guid?)employee.Id
                               join policy in db.CorrectionPolicies on employee.CompanyId equals policy.CompanyId
                               where user.Id == actor && user.IsActive && policy.ApproverUserId != actor &&
                                     db.Users.Any(approver => approver.Id == policy.ApproverUserId && approver.IsActive && (approver.EmployeeId == null || approver.EmployeeId != employee.Id))
                               orderby policy.CreatedAtUtc, policy.Id
                               select new { policy.Id, policy.Name, policy.ApproverUserId }).Skip(offset).Take(50).ToListAsync(token);
            return (new { offset, pageSize = 50, items }, actor);
        }, ct);

    public Task<object> ListOwnAsync(Guid actor, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Submit, "correction.own.viewed", async token =>
        {
            if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var items = await (from request in db.CorrectionRequests.AsNoTracking()
                               join decision in db.CorrectionDecisions on request.Id equals decision.RequestId into decisions
                               from decision in decisions.DefaultIfEmpty()
                               where request.RequesterUserId == actor
                               orderby request.CreatedAtUtc descending, request.Id
                               select new { request.Id, request.EmployeeId, request.PolicyId, request.WorkDate, request.ProposedAtUtc,
                                   request.PunchKind, request.Reason, request.State, request.CreatedAtUtc,
                                   DecisionReason = decision == null ? null : decision.Reason,
                                   DecisionAtUtc = decision == null ? (DateTimeOffset?)null : decision.AtUtc })
                               .Skip(offset).Take(50).ToListAsync(token);
            return (new { offset, pageSize = 50, items }, actor);
        }, ct);

    public Task<object> InboxAsync(Guid actor, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Decide, "correction.inbox.viewed", async token =>
        {
            if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var authorized = access.AuthorizedEmployees(actor, DateOnly.FromDateTime(DateTime.UtcNow), "correction.never-self", DecideScoped, DecideAll);
            var items = await (from request in db.CorrectionRequests.AsNoTracking()
                               join policy in db.CorrectionPolicies on request.PolicyId equals policy.Id
                               where policy.ApproverUserId == actor && request.State == 0 && request.RequesterUserId != actor &&
                                     authorized.Any(x => x.Id == request.EmployeeId)
                               orderby request.CreatedAtUtc, request.Id
                               select new { request.Id, request.EmployeeId, request.WorkDate, request.ProposedAtUtc, request.PunchKind, request.Reason, request.CreatedAtUtc })
                               .Skip(offset).Take(50).ToListAsync(token);
            return (new { offset, pageSize = 50, items }, actor);
        }, ct);
}
