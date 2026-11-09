using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Attendance;

/// <summary>Scoped, uniquely mapped raw events; not a live presence or exception determination.</summary>
public sealed class TeamAttendance(IoDbContext db, PermissionTransaction transaction, EmployeeAccessReader access)
{
    public const string Read = "attendance.read.team";
    public const string Scoped = "attendance.read.scoped";
    public const string All = "attendance.read.all";
    public sealed record EventView(Guid Id, Guid EmployeeId, string PersonnelNumber, string EmployeeName, string Source,
        DateTimeOffset OccurredAtUtc, string? SourceTimeZoneId, string? ReportedEventType);
    public sealed record EventPage(int Offset, int PageSize, IReadOnlyList<EventView> Items);

    // authorizationDate is supplied by the server, not by the client. Scope is evaluated at request time.
    public Task<EventPage> ListAsync(Guid actor, DateOnly authorizationDate, DateTimeOffset fromUtc, DateTimeOffset untilUtcExclusive, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Read, "attendance.team.viewed", async token =>
        {
            if (fromUtc.Offset != TimeSpan.Zero || untilUtcExclusive.Offset != TimeSpan.Zero || untilUtcExclusive <= fromUtc ||
                untilUtcExclusive - fromUtc > TimeSpan.FromDays(62) || offset is < 0 or > 100000) throw new ArgumentException("Invalid UTC interval or page.");
            var authorized=access.AuthorizedEmployees(actor,authorizationDate,"attendance.never-self",Scoped,All);
            var query=from raw in db.RawAttendanceEvents.AsNoTracking()
                      join source in db.EventSources on raw.SourceId equals source.Id
                      join mapping in db.SourceEmployeeMappings on raw.SourceId equals mapping.SourceId
                      join employee in authorized on mapping.EmployeeId equals employee.Id
                      where raw.SourceEmployeeIdentifier != null && raw.OccurredAtUtc != null &&
                            raw.OccurredAtUtc >= fromUtc && raw.OccurredAtUtc < untilUtcExclusive &&
                            EF.Functions.Collate(mapping.SourcePersonId,"Latin1_General_100_BIN2") == EF.Functions.Collate(raw.SourceEmployeeIdentifier!,"Latin1_General_100_BIN2") &&
                            EF.Functions.DataLength(mapping.SourcePersonId) == EF.Functions.DataLength(raw.SourceEmployeeIdentifier!) &&
                            mapping.ValidFromUtc <= raw.OccurredAtUtc && (mapping.ValidUntilUtcExclusive == null || raw.OccurredAtUtc < mapping.ValidUntilUtcExclusive) &&
                            db.SourceEmployeeMappings.Count(candidate=>candidate.SourceId==raw.SourceId &&
                                EF.Functions.Collate(candidate.SourcePersonId,"Latin1_General_100_BIN2") == EF.Functions.Collate(raw.SourceEmployeeIdentifier!,"Latin1_General_100_BIN2") &&
                                EF.Functions.DataLength(candidate.SourcePersonId)==EF.Functions.DataLength(raw.SourceEmployeeIdentifier!) &&
                                candidate.ValidFromUtc <= raw.OccurredAtUtc && (candidate.ValidUntilUtcExclusive == null || raw.OccurredAtUtc < candidate.ValidUntilUtcExclusive)) == 1
                      orderby raw.OccurredAtUtc,raw.Id
                      select new EventView(raw.Id,employee.Id,employee.PersonnelNumber,employee.DisplayName,source.Name,
                          raw.OccurredAtUtc!.Value,raw.SourceTimeZoneId,raw.ReportedEventType);
            var items=await query.Skip(offset).Take(50).ToListAsync(token);
            return (new EventPage(offset,50,items),actor);
        },ct);
}
