using IO.Infrastructure.Storage;
using IO.Infrastructure.Security;

namespace IO.Infrastructure.Reports;

/// <summary>Executive overview of one company inside an explicit UTC window; caller must hold the executive permission.</summary>
public sealed class ExecutiveOverview(IoDbContext db, PermissionTransaction transaction)
{
    public const string Read = "executive.dashboard.read";
    public sealed record CompanyItem(Guid Id, string Name);
    public sealed record Summary(Guid CompanyId, DateTimeOffset FromUtc, DateTimeOffset UntilUtcExclusive,
        int EmployeeCount, int ActiveLinkedAccountCount, int PendingCorrectionCount, int RawEventCount,
        int ApprovedCorrectionCount, int RejectedCorrectionCount, int CancelledCorrectionCount,
        int CorrectionDecisionCount, int CalculationRunCount, int AnomalyRunCount, long WorkedMinutes,
        DateOnly? LatestCalculationWorkDate);

    public Task<IReadOnlyList<CompanyItem>> CompaniesAsync(Guid actor, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Read, "executive.companies.viewed", async token =>
            (IReadOnlyList<CompanyItem>)await db.Companies.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new CompanyItem(x.Id, x.Name)).ToListAsync(token), ct);

    public Task<Summary> InspectAsync(Guid actor, Guid companyId, DateTimeOffset fromUtc, DateTimeOffset untilUtcExclusive, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Read, "executive.overview.viewed", async token =>
        {
            if (companyId==Guid.Empty || fromUtc.Offset!=TimeSpan.Zero || untilUtcExclusive.Offset!=TimeSpan.Zero ||
                untilUtcExclusive<=fromUtc || untilUtcExclusive-fromUtc>TimeSpan.FromDays(62) ||
                !await db.Companies.AnyAsync(x=>x.Id==companyId,token)) throw new ArgumentException("Invalid company or interval.");
            var fromDay=DateOnly.FromDateTime(fromUtc.UtcDateTime);
            var untilDay=DateOnly.FromDateTime(untilUtcExclusive.UtcDateTime);
            var employees=await db.Employees.CountAsync(x=>x.CompanyId==companyId,token);
            var accounts=await (from user in db.Users join employee in db.Employees on user.EmployeeId equals (Guid?)employee.Id
                                where user.IsActive && employee.CompanyId==companyId select user.Id).CountAsync(token);
            var pending=await db.CorrectionRequests.CountAsync(x=>x.CompanyId==companyId && x.State==0,token);
            var approved=await db.CorrectionRequests.CountAsync(x=>x.CompanyId==companyId && x.State==1,token);
            var rejected=await db.CorrectionRequests.CountAsync(x=>x.CompanyId==companyId && x.State==2,token);
            var cancelled=await db.CorrectionRequests.CountAsync(x=>x.CompanyId==companyId && x.State==3,token);
            var decisions=await (from decision in db.CorrectionDecisions join request in db.CorrectionRequests on decision.RequestId equals request.Id
                                where request.CompanyId==companyId && decision.AtUtc>=fromUtc && decision.AtUtc<untilUtcExclusive
                                select decision.Id).CountAsync(token);
            var events=await (from raw in db.RawAttendanceEvents join source in db.EventSources on raw.SourceId equals source.Id
                              join site in db.Sites on source.SiteId equals (Guid?)site.Id
                              where site.CompanyId==companyId && raw.OccurredAtUtc>=fromUtc && raw.OccurredAtUtc<untilUtcExclusive
                              select raw.Id).CountAsync(token);
            var runCount=await db.DailyCalculationRuns.CountAsync(x=>x.CompanyId==companyId && x.WorkDate>=fromDay && x.WorkDate<untilDay,token);
            var anomalyCount=await db.DailyCalculationRuns.CountAsync(x=>x.CompanyId==companyId && x.WorkDate>=fromDay && x.WorkDate<untilDay && x.AnomaliesJson!="[]",token);
            var ticks=await db.DailyCalculationRuns.SumAsync(x=>x.CompanyId==companyId && x.WorkDate>=fromDay && x.WorkDate<untilDay?x.ObservedTicks:null,token);
            var latest=await db.DailyCalculationRuns.MaxAsync(x=>x.CompanyId==companyId && x.WorkDate>=fromDay && x.WorkDate<untilDay?(DateOnly?)x.WorkDate:null,token);
            return (new Summary(companyId,fromUtc,untilUtcExclusive,employees,accounts,pending,events,
                approved,rejected,cancelled,decisions,runCount,anomalyCount,(long)(ticks??0)/600_000_000L,latest),companyId);
        },ct);
}
