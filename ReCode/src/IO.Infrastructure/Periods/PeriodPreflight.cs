using IO.Infrastructure.Security;
using IO.Infrastructure.Calculation;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Periods;

/// <summary>Diagnostic coverage only; historical snapshots are never fresh or approvable by this report.</summary>
public sealed class PeriodPreflight(IoDbContext db, PermissionTransaction transaction)
{
    public const string Read = "period.preflight";

    public Task<object> InspectAsync(Guid actor, Guid employeeId, DateOnly start, DateOnly endExclusive, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Read, "period.preflight.viewed", async token =>
        {
            var length = endExclusive.DayNumber - start.DayNumber;
            if (employeeId == Guid.Empty || length is < 1 or > 62) throw new ArgumentException("Period must contain 1 to 62 days.");
            if (!await db.Employees.AnyAsync(x => x.Id == employeeId, token)) throw new ArgumentException("Unknown employee.");
            var runs = await db.DailyCalculationRuns.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.WorkDate >= start && x.WorkDate < endExclusive)
                .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.WorkDate, x.CreatedAtUtc, x.ObservedTicks, x.AnomaliesJson, x.InputRevision })
                .Take(1001).ToListAsync(token);
            if (runs.Count > 1000) throw new DomainConflictException("Too many historical runs for this diagnostic.");
            var revision = await db.CalculationInputRevisions.AsNoTracking().Where(x => x.Id == 1)
                .Select(x => (long?)x.Revision).SingleOrDefaultAsync(token);
            var latest = runs.GroupBy(x => x.WorkDate).ToDictionary(x => x.Key, x => x.First());
            var days = Enumerable.Range(0, length).Select(i =>
            {
                var day = start.AddDays(i);
                var run = latest.GetValueOrDefault(day);
                return new { date = day, latestRun = run, databaseChange = run is null ? "no-run" : CalculationInputRevisionStatus.Evaluate(run.InputRevision, revision) };
            }).ToArray();
            return (new { employeeId, start, endExclusive, currentRevision = revision, monitorScope = "database-wide", freshness = "unknown", approvable = false,
                warning = "Historical diagnostic only; no freshness, policy coverage or payroll validation.", days }, employeeId);
        }, ct);
}
