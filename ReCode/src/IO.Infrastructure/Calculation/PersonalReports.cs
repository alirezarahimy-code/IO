using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Calculation;

/// <summary>Self-only historical diagnostic results; never approved work time.</summary>
public sealed class PersonalReports(IoDbContext db, PermissionTransaction transaction)
{
    public const string ReadSelf = "attendance.results.read.self";
    public sealed record RunView(Guid Id, DateOnly WorkDate, DateTimeOffset CreatedAtUtc, long? ObservedTicks, string AnomaliesJson);
    public sealed record DayView(DateOnly Date, RunView? LatestRun);
    public sealed record Report(DateOnly Start, DateOnly EndExclusive, string Freshness, bool Approvable, IReadOnlyList<DayView> Days);

    public Task<Report> InspectAsync(Guid actor, DateOnly start, DateOnly endExclusive, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, ReadSelf, "calculation.own-report.viewed", async token =>
        {
            int length=endExclusive.DayNumber-start.DayNumber;
            if (length is < 1 or > 62) throw new ArgumentException("Period must contain 1 to 62 days.");
            var employee = await db.Users.Where(u=>u.Id==actor && u.IsActive).Select(u=>u.EmployeeId).SingleAsync(token);
            if (employee is null) throw new UnauthorizedAccessException();
            var runs=await db.DailyCalculationRuns.AsNoTracking().Where(x=>x.EmployeeId==employee && x.WorkDate>=start && x.WorkDate<endExclusive)
                .OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id)
                .Select(x=>new RunView(x.Id,x.WorkDate,x.CreatedAtUtc,x.ObservedTicks,x.AnomaliesJson))
                .Take(1001).ToListAsync(token);
            if (runs.Count>1000) throw new DomainConflictException("Too many runs for this diagnostic.");
            var latest=runs.GroupBy(x=>x.WorkDate).ToDictionary(x=>x.Key,x=>x.First());
            var days=Enumerable.Range(0,length).Select(i=>{var day=start.AddDays(i);return new DayView(day,latest.GetValueOrDefault(day));}).ToArray();
            return (new Report(start,endExclusive,"unknown",false,days),employee.Value);
        },ct);
}
