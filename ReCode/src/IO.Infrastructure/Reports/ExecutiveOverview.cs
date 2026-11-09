using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Reports;

/// <summary>Global company-level diagnostic counts, not final attendance or period approvals.</summary>
public sealed class ExecutiveOverview(IoDbContext db, PermissionTransaction transaction)
{
    public const string Read = "executive.dashboard.read";
    public sealed record Summary(Guid CompanyId, DateTimeOffset FromUtc, DateTimeOffset UntilUtcExclusive,
        int EmployeeCount, int ActiveLinkedAccountCount, int PendingCorrectionCount, int RawEventCount);
    public Task<Summary> InspectAsync(Guid actor, Guid companyId, DateTimeOffset fromUtc, DateTimeOffset untilUtcExclusive, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Read, "executive.overview.viewed", async token =>
        {
            if (companyId==Guid.Empty || fromUtc.Offset!=TimeSpan.Zero || untilUtcExclusive.Offset!=TimeSpan.Zero ||
                untilUtcExclusive<=fromUtc || untilUtcExclusive-fromUtc>TimeSpan.FromDays(62) ||
                !await db.Companies.AnyAsync(x=>x.Id==companyId,token)) throw new ArgumentException("Invalid company or interval.");
            var employees=await db.Employees.CountAsync(x=>x.CompanyId==companyId,token);
            var accounts=await (from user in db.Users join employee in db.Employees on user.EmployeeId equals (Guid?)employee.Id
                                where user.IsActive && employee.CompanyId==companyId select user.Id).CountAsync(token);
            var pending=await db.CorrectionRequests.CountAsync(x=>x.CompanyId==companyId && x.State==0,token);
            var events=await (from raw in db.RawAttendanceEvents join source in db.EventSources on raw.SourceId equals source.Id
                              join site in db.Sites on source.SiteId equals (Guid?)site.Id
                              where site.CompanyId==companyId && raw.OccurredAtUtc>=fromUtc && raw.OccurredAtUtc<untilUtcExclusive
                              select raw.Id).CountAsync(token);
            return (new Summary(companyId,fromUtc,untilUtcExclusive,employees,accounts,pending,events),companyId);
        },ct);
}
