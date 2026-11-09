using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Attendance;

/// <summary>Unambiguously mapped raw events for the current account only, not calculated attendance.</summary>
public sealed class PersonalAttendance(IoDbContext db, PermissionTransaction transaction)
{
    public const string ReadSelf = "attendance.read.self";
    public sealed record EventView(Guid Id, string Source, DateTimeOffset OccurredAtUtc, string? SourceTimeZoneId, string? ReportedEventType);
    public sealed record EventPage(int Offset, int PageSize, IReadOnlyList<EventView> Items);

    public Task<EventPage> ListAsync(Guid actor, DateTimeOffset fromUtc, DateTimeOffset untilUtcExclusive, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, ReadSelf, "attendance.own.viewed", async token =>
        {
            if (fromUtc.Offset != TimeSpan.Zero || untilUtcExclusive.Offset != TimeSpan.Zero ||
                untilUtcExclusive <= fromUtc || untilUtcExclusive - fromUtc > TimeSpan.FromDays(62) || offset is < 0 or > 100000)
                throw new ArgumentException("Invalid UTC interval or page.");
            // This lookup is within the same serializable transaction as authorization and the read.
            // There is intentionally no client-selected employee or implicit global access.
            var employee = await db.Users.Where(u => u.Id == actor && u.IsActive).Select(u => u.EmployeeId).SingleAsync(token);
            if (employee is null) throw new UnauthorizedAccessException();
            var query = from raw in db.RawAttendanceEvents.AsNoTracking()
                        join source in db.EventSources on raw.SourceId equals source.Id
                        where raw.SourceEmployeeIdentifier != null && raw.OccurredAtUtc != null &&
                            raw.OccurredAtUtc >= fromUtc && raw.OccurredAtUtc < untilUtcExclusive &&
                            db.SourceEmployeeMappings.Count(mapping => mapping.SourceId == raw.SourceId &&
                                EF.Functions.Collate(mapping.SourcePersonId, "Latin1_General_100_BIN2") == EF.Functions.Collate(raw.SourceEmployeeIdentifier!, "Latin1_General_100_BIN2") &&
                                EF.Functions.DataLength(mapping.SourcePersonId) == EF.Functions.DataLength(raw.SourceEmployeeIdentifier!) &&
                                mapping.ValidFromUtc <= raw.OccurredAtUtc &&
                                (mapping.ValidUntilUtcExclusive == null || raw.OccurredAtUtc < mapping.ValidUntilUtcExclusive)) == 1 &&
                            db.SourceEmployeeMappings.Any(mapping => mapping.SourceId == raw.SourceId && mapping.EmployeeId == employee &&
                                EF.Functions.Collate(mapping.SourcePersonId, "Latin1_General_100_BIN2") == EF.Functions.Collate(raw.SourceEmployeeIdentifier!, "Latin1_General_100_BIN2") &&
                                EF.Functions.DataLength(mapping.SourcePersonId) == EF.Functions.DataLength(raw.SourceEmployeeIdentifier!) &&
                                mapping.ValidFromUtc <= raw.OccurredAtUtc &&
                                (mapping.ValidUntilUtcExclusive == null || raw.OccurredAtUtc < mapping.ValidUntilUtcExclusive))
                        orderby raw.OccurredAtUtc, raw.Id
                        select new EventView(raw.Id, source.Name, raw.OccurredAtUtc!.Value, raw.SourceTimeZoneId, raw.ReportedEventType);
            var items = await query.Skip(offset).Take(50).ToListAsync(token);
            return (new EventPage(offset, 50, items), employee.Value);
        }, ct);
}
