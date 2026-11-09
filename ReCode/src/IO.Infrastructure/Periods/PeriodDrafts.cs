using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Periods;

/// <summary>Only diagnostic draft registration; no lock, review, approval or payroll effect.</summary>
public sealed class PeriodDrafts(IoDbContext db, PermissionTransaction transaction)
{
    public const string Manage = "period.draft.manage";

    public Task<Guid> CreateAsync(Guid actor, Guid employeeId, DateOnly start, DateOnly endExclusive, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "period.draft.registered", async token =>
        {
            var length = endExclusive.DayNumber - start.DayNumber;
            if (employeeId == Guid.Empty || length is < 1 or > 62) throw new ArgumentException("Draft must contain 1 to 62 days.");
            await transaction.LockAsync("IO.PeriodDraft." + employeeId.ToString("N"), token);
            var company = await db.Employees.Where(x => x.Id == employeeId).Select(x => (Guid?)x.CompanyId).SingleOrDefaultAsync(token)
                ?? throw new ArgumentException("Unknown employee.");
            // Retried identical registrations are idempotent. Overlapping drafts are
            // deliberately allowed: none is a closed period or blocks any input.
            var existing = await db.PeriodDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.EmployeeId == employeeId &&
                x.Start == start && x.EndExclusive == endExclusive, token);
            if (existing is not null) return (existing.Id, existing.Id);
            var id = Guid.NewGuid();
            db.PeriodDrafts.Add(new() { Id = id, EmployeeId = employeeId, CompanyId = company, Start = start,
                EndExclusive = endExclusive, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = actor });
            return (id, id);
        }, ct);

    public Task<object> ListAsync(Guid actor, Guid employeeId, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Manage, "period.drafts.viewed", async token =>
        {
            if (employeeId == Guid.Empty || offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var items = await db.PeriodDrafts.AsNoTracking().Where(x => x.EmployeeId == employeeId)
                .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(offset).Take(50).ToListAsync(token);
            return (new { employeeId, offset, pageSize = 50, status = "diagnostic-draft-only", approvable = false, items }, employeeId);
        }, ct);
}
