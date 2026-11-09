using IO.Infrastructure.Security;
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
                .Select(x => new { x.Id, x.WorkDate, x.CreatedAtUtc, x.ObservedTicks, x.AnomaliesJson })
                .Take(1001).ToListAsync(token);
            if (runs.Count > 1000) throw new DomainConflictException("Too many historical runs for this diagnostic.");
            var latest = runs.GroupBy(x => x.WorkDate).ToDictionary(x => x.Key, x => x.First());
            var days = Enumerable.Range(0, length).Select(i =>
            {
                var day = start.AddDays(i);
                return new { date = day, latestRun = latest.GetValueOrDefault(day) };
            }).ToArray();
            return (new { employeeId, start, endExclusive, freshness = "unknown", approvable = false,
                warning = "Historical diagnostic only; no freshness, policy coverage or payroll validation.", days }, employeeId);
        }, ct);
}
