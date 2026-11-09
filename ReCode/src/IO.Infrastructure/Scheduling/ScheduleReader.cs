using IO.Domain.Scheduling;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Scheduling;

/// <summary>One employee/day, filtered by explicit schedule permissions in SQL.</summary>
public sealed class ScheduleReader(IoDbContext db, EmployeeAccessReader access)
{
    public const string ReadSelf = "schedule.read.self";
    public const string ReadScoped = "schedule.read.scoped";
    public const string ReadAll = "schedule.read.all";
    public sealed record ScheduleView(Guid EmployeeId, DateOnly WorkDate, int JalaliYear, int JalaliMonth, int JalaliDay,
        string Calendar, int? DayKind, string Shift, string TimeZoneId, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

    public Task<ScheduleView?> FindAsync(Guid actor, Guid employeeId, DateOnly workDate,
        DateOnly trustedAuthorizationDate, CancellationToken ct = default) =>
        actor == Guid.Empty || employeeId == Guid.Empty ? Task.FromResult<ScheduleView?>(null) :
        FindCoreAsync(actor, employeeId, false, workDate, trustedAuthorizationDate, ct);

    // The linked employee and its permission are checked in the same SQL query.
    // This endpoint never accepts a client-selected employee identifier.
    public Task<ScheduleView?> FindOwnAsync(Guid actor, DateOnly workDate, DateOnly trustedAuthorizationDate, CancellationToken ct = default) =>
        actor == Guid.Empty ? Task.FromResult<ScheduleView?>(null) :
        FindCoreAsync(actor, Guid.Empty, true, workDate, trustedAuthorizationDate, ct);

    private async Task<ScheduleView?> FindCoreAsync(Guid actor, Guid employeeId, bool ownOnly, DateOnly workDate,
        DateOnly trustedAuthorizationDate, CancellationToken ct)
    {
        var authorized = access.AuthorizedEmployees(actor, trustedAuthorizationDate, ReadSelf, ReadScoped, ReadAll);
        var query = from assignment in db.WorkScheduleAssignments.AsNoTracking()
                    join shift in db.ShiftDefinitions on assignment.ShiftId equals shift.Id
                    join site in db.Sites on shift.SiteId equals site.Id
                    join calendar in db.Calendars on assignment.CalendarId equals calendar.Id
                    where ((ownOnly && db.Users.Any(user => user.Id == actor && user.IsActive && user.EmployeeId == assignment.EmployeeId)) ||
                           (!ownOnly && assignment.EmployeeId == employeeId)) && assignment.ValidFrom <= workDate &&
                          (assignment.ValidUntilExclusive == null || workDate < assignment.ValidUntilExclusive) &&
                          authorized.Any(x => x.Id == assignment.EmployeeId)
                    select new
                    {
                        assignment.EmployeeId, Calendar = calendar.Name, Shift = shift.Name, site.TimeZoneId,
                        shift.StartsAt, shift.EndsAt, shift.EndDayOffset,
                        DayKind = db.CalendarDays.Where(day => day.CalendarId == calendar.Id && day.Date == workDate)
                            .Select(day => (int?)day.Kind).SingleOrDefault()
                    };
        // Multiple assignments indicate corruption; never select one arbitrarily.
        var item = await query.SingleOrDefaultAsync(ct);
        if (item is null) return null;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(item.TimeZoneId, out var zone))
            throw new InvalidOperationException("The site time zone is unavailable.");
        var interval = new FixedShift(item.StartsAt, item.EndsAt, item.EndDayOffset).Resolve(workDate, zone);
        var jalali = PersianDates.FromGregorian(workDate);
        return new ScheduleView(item.EmployeeId, workDate, jalali.Year, jalali.Month, jalali.Day, item.Calendar,
            item.DayKind, item.Shift, item.TimeZoneId, interval.StartUtc, interval.EndUtc);
    }
}
