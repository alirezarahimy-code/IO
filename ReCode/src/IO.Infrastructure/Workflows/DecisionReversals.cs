using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Workflows;

/// <summary>Append-only, expected-decision and client-operation keyed reversals; never rewrite a decision.</summary>
public sealed class DecisionReversals(IoDbContext db, PermissionTransaction transaction, EmployeeAccessReader access)
{
    public const string CorrectionPermission = "correction.reverse";
    public const string LeavePermission = "leave.request.reverse";
    private IQueryable<CorrectionRequestRecord> Corrections(Guid actor)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var employees = access.AuthorizedEmployees(actor, today, "correction.never-self", CorrectionWorkflow.DecideScoped, CorrectionWorkflow.DecideAll);
        // Reversal authority is not implicitly delegated. The currently assigned principal must act.
        return from r in db.CorrectionRequests
               join p in db.CorrectionPolicies on r.PolicyId equals p.Id
               where p.ApproverUserId == actor && r.RequesterUserId != actor && employees.Any(e => e.Id == r.EmployeeId) &&
                     !db.Users.Any(u => u.Id == actor && u.EmployeeId == r.EmployeeId)
               select r;
    }
    private IQueryable<LeaveRequestRecord> Leaves(Guid actor)
    {
        var employees = access.AuthorizedEmployees(actor, DateOnly.FromDateTime(DateTime.UtcNow), "leave.never-self", "leave.request.decide", "leave.never-all");
        return db.LeaveRequests.Where(r => r.RequesterUserId != actor && employees.Any(e => e.Id == r.EmployeeId) &&
            !db.Users.Any(u => u.Id == actor && u.EmployeeId == r.EmployeeId));
    }
    private static void Validate(Guid operation, Guid expected, string reason)
    {
        if (operation == Guid.Empty || expected == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 500 || reason.Any(char.IsControl))
            throw new ArgumentException("An operation id, expected decision id and nonempty reason (maximum 500 characters) are required.");
    }
    private async Task ProtectDraftAsync(Guid employee, DateOnly day, CancellationToken ct)
    {
        // There is no closed-period lifecycle yet. Conservatively refuse even draft overlap,
        // rather than silently altering a period that might already have been exported.
        // Serializable range read also serializes against concurrent draft creation.
        await transaction.LockAsync("IO.PeriodDraft." + employee.ToString("N"), ct);
        if (await db.PeriodDrafts.AnyAsync(p => p.EmployeeId == employee && p.Start <= day && day < p.EndExclusive, ct))
            throw new DomainConflictException("Period evidence already exists; reversal is blocked pending the controlled period-reopen workflow.");
    }
    private void Notify(Guid user, string category, string reason)
    {
        var text = "تصمیم درخواست شما بازگردانده شد؛ درخواست دوباره در انتظار بررسی است. دلیل: " + reason;
        db.Notifications.Add(new() { Id = Guid.NewGuid(), UserId = user, Category = category, Text = text.Length > 500 ? text[..500] : text, CreatedAtUtc = DateTimeOffset.UtcNow });
    }
    public Task<Guid> ReverseCorrectionAsync(Guid actor, Guid requestId, Guid expectedDecisionId, Guid operationId, string reason, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, CorrectionPermission, "correction.decision.reversed", async token =>
        {
            Validate(operationId, expectedDecisionId, reason);
            await transaction.LockAsync("IO.DecisionReversalOperation." + operationId.ToString("N"), token);
            await transaction.LockAsync("IO.Correction." + requestId.ToString("N"), token);
            var request = await Corrections(actor).SingleOrDefaultAsync(r => r.Id == requestId, token) ?? throw new UnauthorizedAccessException();
            var retry = await db.CorrectionDecisionReversals.AsNoTracking().SingleOrDefaultAsync(r => r.Id == operationId, token);
            if (retry is not null)
            {
                if (retry.ActorUserId != actor || retry.DecisionId != expectedDecisionId || !string.Equals(retry.Reason, reason, StringComparison.Ordinal) ||
                    !await db.CorrectionDecisions.AnyAsync(d => d.Id == expectedDecisionId && d.RequestId == requestId, token))
                    throw new DomainConflictException("Reversal operation id was used for another operation.");
                return (retry.Id, requestId);
            }
            var decision = await db.CorrectionDecisions.AsNoTracking().SingleOrDefaultAsync(d => d.Id == expectedDecisionId && d.RequestId == requestId, token);
            if (decision is null || request.State is not (1 or 2) || decision.Decision != request.State ||
                await db.CorrectionDecisionReversals.AnyAsync(r => r.DecisionId == expectedDecisionId, token) ||
                await db.CorrectionDecisions.AnyAsync(d => d.RequestId == requestId && d.Sequence > decision.Sequence, token))
                throw new DomainConflictException("Expected current approved or rejected decision; refresh history.");
            await ProtectDraftAsync(request.EmployeeId, request.WorkDate, token);
            db.CorrectionDecisionReversals.Add(new() { Id = operationId, DecisionId = decision.Id, ActorUserId = actor, Reason = reason, AtUtc = DateTimeOffset.UtcNow });
            // Persist evidence before the request transition; both saves remain in one permission transaction.
            await db.SaveChangesAsync(token);
            request.State = 0;
            Notify(request.RequesterUserId, "correction", reason);
            return (operationId, requestId);
        }, ct);

    public Task<Guid> ReverseLeaveAsync(Guid actor, Guid requestId, Guid expectedDecisionId, Guid operationId, string reason, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, LeavePermission, "leave.decision.reversed", async token =>
        {
            Validate(operationId, expectedDecisionId, reason);
            await transaction.LockAsync("IO.DecisionReversalOperation." + operationId.ToString("N"), token);
            await transaction.LockAsync("IO.LeaveRequest." + requestId.ToString("N"), token);
            var request = await Leaves(actor).SingleOrDefaultAsync(r => r.Id == requestId, token) ?? throw new UnauthorizedAccessException();
            var retry = await db.LeaveDecisionReversals.AsNoTracking().SingleOrDefaultAsync(r => r.Id == operationId, token);
            if (retry is not null)
            {
                if (retry.ActorUserId != actor || retry.DecisionId != expectedDecisionId || !string.Equals(retry.Reason, reason, StringComparison.Ordinal) ||
                    !await db.LeaveDecisions.AnyAsync(d => d.Id == expectedDecisionId && d.RequestId == requestId, token))
                    throw new DomainConflictException("Reversal operation id was used for another operation.");
                return (retry.Id, requestId);
            }
            var decision = await db.LeaveDecisions.AsNoTracking().SingleOrDefaultAsync(d => d.Id == expectedDecisionId && d.RequestId == requestId, token);
            if (decision is null || request.State is not (1 or 2) || decision.Decision != request.State ||
                await db.LeaveDecisionReversals.AnyAsync(r => r.DecisionId == expectedDecisionId, token) ||
                await db.LeaveDecisions.AnyAsync(d => d.RequestId == requestId && d.Sequence > decision.Sequence, token))
                throw new DomainConflictException("Expected current approved or rejected decision; refresh history.");
            await ProtectDraftAsync(request.EmployeeId, request.WorkDate, token);
            Guid? compensationId = null;
            if (decision.Decision == 1 && request.Category == 0)
            {
                var original = await db.LeaveLedgerEntries.AsNoTracking().SingleOrDefaultAsync(l => l.Id == decision.LedgerEntryId, token);
                if (original is null || original.Kind != 2 || original.Minutes != -request.Minutes || original.EmployeeId != request.EmployeeId ||
                    original.CompanyId != request.CompanyId || original.LeaveTypeId != request.LeaveTypeId || original.EffectiveDate != request.WorkDate)
                    throw new DomainConflictException("Original leave consumption cannot be reconciled; no refund was posted.");
                compensationId = Guid.NewGuid();
                db.LeaveLedgerEntries.Add(new() { Id = compensationId.Value, EmployeeId = original.EmployeeId, CompanyId = original.CompanyId,
                    LeaveTypeId = original.LeaveTypeId, PersianYear = original.PersianYear, EffectiveDate = original.EffectiveDate,
                    Kind = 3, Minutes = -original.Minutes, DocumentNumber = "REV-" + operationId.ToString("N"), Description = "بازگشت مصرف مرخصی؛ " + reason[..Math.Min(reason.Length, 450)],
                    CreatedByUserId = actor, CreatedAtUtc = DateTimeOffset.UtcNow });
            }
            db.LeaveDecisionReversals.Add(new() { Id = operationId, DecisionId = decision.Id, ActorUserId = actor,
                OriginalLedgerEntryId = decision.LedgerEntryId, CompensationLedgerEntryId = compensationId, Reason = reason, AtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(token);
            request.State = 0;
            Notify(request.RequesterUserId, "leave", reason);
            return (operationId, requestId);
        }, ct);

    public Task<object> ListCorrectionsAsync(Guid actor, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, CorrectionPermission, "correction.reversals.viewed", async token =>
        {
            if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var items = await (from r in Corrections(actor) join d in db.CorrectionDecisions on r.Id equals d.RequestId
                               join v in db.CorrectionDecisionReversals on d.Id equals v.DecisionId into reversals
                               from v in reversals.DefaultIfEmpty()
                               orderby d.AtUtc descending, d.Id
                               select new { RequestId = r.Id, r.EmployeeId, r.WorkDate, r.State, DecisionId = d.Id, d.Sequence, d.Decision,
                                   DecisionReason = d.Reason, d.AtUtc, ReversalId = (Guid?)v.Id, ReversalReason = v.Reason, ReversedAtUtc = (DateTimeOffset?)v.AtUtc,
                                   ReversedByUserId = (Guid?)v.ActorUserId, CanReverse = v == null && (r.State == 1 || r.State == 2) })
                .Skip(offset).Take(50).ToListAsync(token);
            return (new { offset, pageSize = 50, items }, actor);
        }, ct);
    public Task<object> ListLeavesAsync(Guid actor, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, LeavePermission, "leave.reversals.viewed", async token =>
        {
            if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var items = await (from r in Leaves(actor) join d in db.LeaveDecisions on r.Id equals d.RequestId
                               join v in db.LeaveDecisionReversals on d.Id equals v.DecisionId into reversals
                               from v in reversals.DefaultIfEmpty()
                               orderby d.AtUtc descending, d.Id
                               select new { RequestId = r.Id, r.EmployeeId, r.WorkDate, r.State, r.Category, r.Kind, r.Minutes, DecisionId = d.Id,
                                   d.Sequence, d.Decision, DecisionReason = d.Reason, d.AtUtc, d.LedgerEntryId, ReversalId = (Guid?)v.Id,
                                   ReversalReason = v.Reason, ReversedAtUtc = (DateTimeOffset?)v.AtUtc, ReversedByUserId = (Guid?)v.ActorUserId,
                                   CompensationLedgerEntryId = (Guid?)v.CompensationLedgerEntryId, CanReverse = v == null && (r.State == 1 || r.State == 2) })
                .Skip(offset).Take(50).ToListAsync(token);
            return (new { offset, pageSize = 50, items }, actor);
        }, ct);

    public Task<object> OwnCorrectionHistoryAsync(Guid actor, Guid requestId, int offset, CancellationToken ct = default) =>
        transaction.ExecuteEitherAsync<object>(actor, CorrectionWorkflow.ReadOwn, CorrectionWorkflow.Submit, "correction.own-history.viewed", async token =>
        {
            if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            if (!await db.CorrectionRequests.AnyAsync(r => r.Id == requestId && r.RequesterUserId == actor, token)) throw new UnauthorizedAccessException();
            var items = await (from d in db.CorrectionDecisions where d.RequestId == requestId
                               join v in db.CorrectionDecisionReversals on d.Id equals v.DecisionId into reversals
                               from v in reversals.DefaultIfEmpty() orderby d.Sequence
                               select new { d.Id, d.Sequence, d.Decision, d.ActorUserId, d.Reason, d.AtUtc, ReversalId = (Guid?)v.Id,
                                   ReversalReason = v.Reason, ReversedAtUtc = (DateTimeOffset?)v.AtUtc, ReversedByUserId = (Guid?)v.ActorUserId }).Skip(offset).Take(50).ToListAsync(token);
            return (new { offset, pageSize = 50, items }, requestId);
        }, ct);
    public Task<object> OwnLeaveHistoryAsync(Guid actor, Guid requestId, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, "leave.request.submit", "leave.own-history.viewed", async token =>
        {
            if (offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            if (!await db.LeaveRequests.AnyAsync(r => r.Id == requestId && r.RequesterUserId == actor, token)) throw new UnauthorizedAccessException();
            var items = await (from d in db.LeaveDecisions where d.RequestId == requestId
                               join v in db.LeaveDecisionReversals on d.Id equals v.DecisionId into reversals
                               from v in reversals.DefaultIfEmpty() orderby d.Sequence
                               select new { d.Id, d.Sequence, d.Decision, d.ActorUserId, d.Reason, d.AtUtc, d.LedgerEntryId, ReversalId = (Guid?)v.Id,
                                   ReversalReason = v.Reason, ReversedAtUtc = (DateTimeOffset?)v.AtUtc, ReversedByUserId = (Guid?)v.ActorUserId,
                                   CompensationLedgerEntryId = (Guid?)v.CompensationLedgerEntryId }).Skip(offset).Take(50).ToListAsync(token);
            return (new { offset, pageSize = 50, items }, requestId);
        }, ct);
}
