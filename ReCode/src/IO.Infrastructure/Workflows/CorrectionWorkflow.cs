using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using IO.Infrastructure.Settings;
using IO.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Workflows;

/// <summary>Experimental one-step missing-punch workflow; not a leave or payroll workflow.</summary>
public sealed class CorrectionWorkflow(IoDbContext db, PermissionTransaction transaction, EmployeeAccessReader access)
{
    public const string Configure = "correction.configure";
    public const string Submit = "correction.submit.self";
    public const string ReadOwn = "correction.requests.read.self";
    public const string CancelOwn = "correction.cancel.self";
    public const string Decide = "correction.decide";
    public const string Approve = "correction.approve";
    public const string Reject = "correction.reject";
    public const string ReadDecisions = "correction.decisions.read.self";
    public const string ReadInbox = "correction.inbox.read";
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

    // Administrative catalog is separate from the requester's own eligible-policy list.
    public Task<object> ListConfiguredPoliciesAsync(Guid actor, Guid companyId, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Configure, "correction.configured-policies.viewed", async token =>
        {
            if (companyId == Guid.Empty || offset is < 0 or > 100000 || !await db.Companies.AnyAsync(x => x.Id == companyId, token))
                throw new ArgumentException("Invalid company or page.");
            var items = await (from policy in db.CorrectionPolicies.AsNoTracking()
                               join approver in db.Users on policy.ApproverUserId equals approver.Id
                               where policy.CompanyId == companyId
                               orderby policy.CreatedAtUtc, policy.Id
                               select new { policy.Id, policy.CompanyId, policy.Name, policy.ApproverUserId,
                                   ApproverName = approver.UserName, ApproverActive = approver.IsActive,
                                   policy.CreatedByUserId, policy.CreatedAtUtc }).Skip(offset).Take(50).ToListAsync(token);
            return (new { companyId, offset, pageSize = 50, items }, companyId);
        }, ct);

    public Task<Guid> SubmitAsync(Guid actor, Guid policyId, DateOnly workDate, DateTimeOffset proposedUtc, int kind, string reason, CancellationToken ct = default, Guid? operationId = null) =>
        transaction.ExecuteAsync(actor, Submit, "correction.request.created", async token =>
        {
            Reason(reason, true);
            if (policyId == Guid.Empty || operationId == Guid.Empty || proposedUtc.Offset != TimeSpan.Zero || kind is < 0 or > 1) throw new ArgumentException("Invalid proposed punch or operation id.");
            var employee = await (from user in db.Users join e in db.Employees on user.EmployeeId equals (Guid?)e.Id
                                  where user.Id == actor && user.IsActive select e).SingleOrDefaultAsync(token)
                ?? throw new ArgumentException("Active account must be linked to an employee.");
            // A client operation id lets an uncertain retry resolve to the original immutable request.
            var id = operationId ?? Guid.NewGuid();
            await transaction.LockAsync("IO.CorrectionOperation." + id.ToString("N"), token);
            var existing = await db.CorrectionRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
            if (existing is not null)
            {
                if (existing.RequesterUserId != actor || existing.EmployeeId != employee.Id || existing.CompanyId != employee.CompanyId ||
                    existing.PolicyId != policyId || existing.WorkDate != workDate || existing.ProposedAtUtc != proposedUtc || existing.PunchKind != kind ||
                    !string.Equals(existing.Reason, reason, StringComparison.Ordinal))
                    throw new DomainConflictException("Operation id already used for another request.");
                return (id, id);
            }
            // Serialize with setting changes: the active company version at submission time is authoritative.
            await transaction.LockAsync(CorrectionSettings.LockName(employee.CompanyId), token);
            var setting = await db.CorrectionSettings.AsNoTracking().Where(x => x.CompanyId == employee.CompanyId)
                .OrderByDescending(x => x.Revision).Select(x => new { x.Id, x.MaxRequestAgeDays, x.DailyRequestLimit, x.MonthlyRequestLimit }).FirstOrDefaultAsync(token);
            int maxAge = setting?.MaxRequestAgeDays ?? 0;
            if (maxAge > 0 && workDate < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-maxAge))
                throw new ArgumentException("Work date exceeds configured maximum request age.");
            // The company lock serializes these counts with submissions and setting changes.
            var counted = db.CorrectionRequests.AsNoTracking().Where(x => x.CompanyId == employee.CompanyId && x.EmployeeId == employee.Id && (x.State == 0 || x.State == 1));
            if (setting is { DailyRequestLimit: > 0 } && await counted.CountAsync(x => x.WorkDate == workDate, token) >= setting.DailyRequestLimit)
                throw new DomainConflictException("Daily request limit reached.");
            if (setting is { MonthlyRequestLimit: > 0 })
            {
                var (year, month, _) = PersianDates.FromGregorian(workDate);
                if (year is < 1300 or > 1500) throw new ArgumentException("Unsupported Persian year for monthly limit.");
                var first = PersianDates.ToGregorian(year, month, 1);
                var next = month == 12 ? PersianDates.ToGregorian(year + 1, 1, 1) : PersianDates.ToGregorian(year, month + 1, 1);
                if (await counted.CountAsync(x => x.WorkDate >= first && x.WorkDate < next, token) >= setting.MonthlyRequestLimit)
                    throw new DomainConflictException("Monthly request limit reached.");
            }
            var policy = await db.CorrectionPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == policyId && x.CompanyId == employee.CompanyId, token)
                ?? throw new ArgumentException("Unknown policy or different company.");
            if (policy.ApproverUserId == actor || await db.Users.AnyAsync(x => x.Id == policy.ApproverUserId && x.EmployeeId == employee.Id, token))
                throw new ArgumentException("Self-approval policy cannot be used.");
            if (!await db.Users.AnyAsync(x => x.Id == policy.ApproverUserId && x.IsActive, token))
                throw new DomainConflictException("Policy approver is inactive.");
            db.CorrectionRequests.Add(new() { Id = id, CompanyId = employee.CompanyId, EmployeeId = employee.Id,
                RequesterUserId = actor, PolicyId = policyId, CorrectionSettingsId = setting?.Id, WorkDate = workDate, ProposedAtUtc = proposedUtc,
                PunchKind = kind, Reason = reason, State = 0, CreatedAtUtc = DateTimeOffset.UtcNow });
            return (id, id);
        }, ct);

    public Task<bool> DecideAsync(Guid actor, Guid requestId, bool approve, string? reason, CancellationToken ct = default) =>
        transaction.ExecuteEitherAsync(actor, approve ? Approve : Reject, Decide, "correction.request.decided", async token =>
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
        transaction.ExecuteEitherAsync(actor, CancelOwn, Submit, "correction.request.cancelled", async token =>
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

    public Task<object> ListOwnAsync(Guid actor, int offset, CancellationToken ct = default,
        DateOnly? fromDate = null, DateOnly? toDate = null, int? state = null) =>
        transaction.ExecuteEitherAsync<object>(actor, ReadOwn, Submit, "correction.own.viewed", async token =>
        {
            if (offset is < 0 or > 100000 || fromDate.HasValue != toDate.HasValue ||
                (fromDate.HasValue && fromDate > toDate) || state is < 0 or > 3)
                throw new ArgumentException("Invalid page or filter.");
            var ownRequests = db.CorrectionRequests.AsNoTracking().Where(x => x.RequesterUserId == actor);
            if (fromDate.HasValue && toDate.HasValue)
            {
                var from = fromDate.Value; var to = toDate.Value;
                ownRequests = ownRequests.Where(x => x.WorkDate >= from && x.WorkDate <= to);
            }
            if (state.HasValue)
            {
                var requestedState = state.Value;
                ownRequests = ownRequests.Where(x => x.State == requestedState);
            }
            var items = await (from request in ownRequests
                               join policy in db.CorrectionPolicies on request.PolicyId equals policy.Id
                               join approver in db.Users on policy.ApproverUserId equals approver.Id
                               join approverEmployee in db.Employees on approver.EmployeeId equals (Guid?)approverEmployee.Id into approverEmployees
                               from approverEmployee in approverEmployees.DefaultIfEmpty()
                               join decision in db.CorrectionDecisions on request.Id equals decision.RequestId into decisions
                               from decision in decisions.DefaultIfEmpty()
                               join decisionActor in db.Users on (Guid?)decision.ActorUserId equals (Guid?)decisionActor.Id into decisionActors
                               from decisionActor in decisionActors.DefaultIfEmpty()
                               join decisionEmployee in db.Employees on decisionActor.EmployeeId equals (Guid?)decisionEmployee.Id into decisionEmployees
                               from decisionEmployee in decisionEmployees.DefaultIfEmpty()
                               join settings in db.CorrectionSettings on request.CorrectionSettingsId equals (Guid?)settings.Id into settingsVersions
                               from settings in settingsVersions.DefaultIfEmpty()
                               orderby request.CreatedAtUtc descending, request.Id
                               select new { request.Id, request.EmployeeId, request.PolicyId, request.WorkDate, request.ProposedAtUtc,
                                   request.PunchKind, request.Reason, request.State, request.CreatedAtUtc,
                                   PolicyName = policy.Name, policy.ApproverUserId,
                                   ApproverDisplayName = approverEmployee == null ? null : approverEmployee.DisplayName,
                                   request.CorrectionSettingsId, SettingsRevision = settings == null ? (int?)null : settings.Revision,
                                   DecisionKind = decision == null ? (int?)null : decision.Decision,
                                   DecisionActorUserId = decision == null ? (Guid?)null : decision.ActorUserId,
                                   DecisionActorDisplayName = decisionEmployee == null ? null : decisionEmployee.DisplayName,
                                   DecisionReason = decision == null ? null : decision.Reason,
                                   DecisionAtUtc = decision == null ? (DateTimeOffset?)null : decision.AtUtc })
                               .Skip(offset).Take(50).ToListAsync(token);
            var canCancel = await transaction.HasPermissionAsync(actor, CancelOwn, token) ||
                            await transaction.HasPermissionAsync(actor, Submit, token);
            return (new { offset, pageSize = 50, fromDate, toDate, state, items, canCancel }, actor);
        }, ct);

    public Task<object> ListDecisionsAsync(Guid actor, int offset, CancellationToken ct = default) =>
        transaction.ExecuteEitherAsync<object>(actor, ReadDecisions, Decide, "correction.decisions.viewed", async token =>
        {
            if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var authorized = access.AuthorizedEmployees(actor, DateOnly.FromDateTime(DateTime.UtcNow), "correction.never-self", DecideScoped, DecideAll);
            var items = await (from decision in db.CorrectionDecisions.AsNoTracking()
                               join request in db.CorrectionRequests on decision.RequestId equals request.Id
                               join policy in db.CorrectionPolicies on request.PolicyId equals policy.Id
                               where decision.ActorUserId == actor && policy.ApproverUserId == actor &&
                                     decision.Decision != 3 && request.RequesterUserId != actor && authorized.Any(x => x.Id == request.EmployeeId) &&
                                     !db.Users.Any(x => x.Id == actor && x.EmployeeId == request.EmployeeId)
                               orderby decision.AtUtc descending, decision.Id
                               select new { decision.RequestId, request.EmployeeId, request.WorkDate, request.ProposedAtUtc, request.PunchKind,
                                   RequestReason = request.Reason, decision.Decision, DecisionReason = decision.Reason, decision.AtUtc })
                               .Skip(offset).Take(50).ToListAsync(token);
            return (new { offset, pageSize = 50, items }, actor);
        }, ct);

    // Only employees with a pending request actually assigned to this actor and
    // in the actor's current scope are exposed; this is not a personnel catalog.
    public Task<object> InboxEmployeesAsync(Guid actor, int offset, CancellationToken ct = default) =>
        transaction.ExecuteEitherAsync<object>(actor, ReadInbox, Decide, "correction.inbox-employees.viewed", async token =>
        {
            if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var authorized = access.AuthorizedEmployees(actor, DateOnly.FromDateTime(DateTime.UtcNow), "correction.never-self", DecideScoped, DecideAll);
            var query = (from request in db.CorrectionRequests.AsNoTracking()
                         join policy in db.CorrectionPolicies on request.PolicyId equals policy.Id
                         join employee in authorized on request.EmployeeId equals employee.Id
                         where policy.ApproverUserId == actor && request.State == 0 && request.RequesterUserId != actor &&
                               !db.Users.Any(x => x.Id == actor && x.EmployeeId == request.EmployeeId)
                         select new { employee.Id, employee.DisplayName, employee.PersonnelNumber }).Distinct();
            var items = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.PersonnelNumber).ThenBy(x => x.Id)
                .Skip(offset).Take(50).ToListAsync(token);
            return (new { offset, pageSize = 50, items }, actor);
        }, ct);

    public Task<object> InboxAsync(Guid actor, int offset, CancellationToken ct = default, Guid? employeeId = null, string? sort = null) =>
        transaction.ExecuteEitherAsync<object>(actor, ReadInbox, Decide, "correction.inbox.viewed", async token =>
        {
            sort ??= "created";
            if (offset is < 0 or > 100000 || employeeId == Guid.Empty || sort is not ("created" or "work-asc" or "work-desc"))
                throw new ArgumentException("Invalid page, employee filter or sort.");
            var authorized = access.AuthorizedEmployees(actor, DateOnly.FromDateTime(DateTime.UtcNow), "correction.never-self", DecideScoped, DecideAll);
            var requests = db.CorrectionRequests.AsNoTracking();
            if (employeeId.HasValue) requests = requests.Where(x => x.EmployeeId == employeeId.Value);
            var query = from request in requests
                        join policy in db.CorrectionPolicies on request.PolicyId equals policy.Id
                        join employee in authorized on request.EmployeeId equals employee.Id
                        where policy.ApproverUserId == actor && request.State == 0 && request.RequesterUserId != actor &&
                              !db.Users.Any(x => x.Id == actor && x.EmployeeId == request.EmployeeId)
                        select new { request.Id, request.EmployeeId, EmployeeName = employee.DisplayName, employee.PersonnelNumber,
                            request.WorkDate, request.ProposedAtUtc, request.PunchKind, request.Reason, request.CreatedAtUtc };
            var ordered = sort switch
            {
                "work-asc" => query.OrderBy(x => x.WorkDate).ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
                "work-desc" => query.OrderByDescending(x => x.WorkDate).ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
                _ => query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            };
            var items = await ordered.Skip(offset).Take(50).ToListAsync(token);
            var legacyDecision = await transaction.HasPermissionAsync(actor, Decide, token);
            var canApprove = legacyDecision || await transaction.HasPermissionAsync(actor, Approve, token);
            var canReject = legacyDecision || await transaction.HasPermissionAsync(actor, Reject, token);
            var canDecide = canApprove || canReject;
            return (new { offset, pageSize = 50, employeeId, sort, items, canDecide, canApprove, canReject }, actor);
        }, ct);
}
