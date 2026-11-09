using IO.Domain.Scheduling;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Scheduling;

/// <summary>Provisional fixed schedules for individual employees only.</summary>
public sealed class ScheduleManagement(IoDbContext db, PermissionTransaction transaction)
{
    public const string Manage = "schedule.manage";
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl))
            throw new ArgumentException("Invalid name.");
    }

    public Task<Guid> CreateCalendarAsync(Guid actor, Guid companyId, string name, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "schedule.calendar.created", async token =>
        {
            ValidateName(name);
            if (!await db.Companies.AnyAsync(x => x.Id == companyId, token)) throw new ArgumentException("Unknown company.");
            var id = Guid.NewGuid(); db.Calendars.Add(new() { Id = id, CompanyId = companyId, Name = name });
            return (id, id);
        }, ct);

    public Task<Guid> AddCalendarDayAsync(Guid actor, Guid calendarId, DateOnly date, int kind, string? note, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "schedule.calendar-day.created", async token =>
        {
            if (kind is < 0 or > 2 || note is { Length: > 500 }) throw new ArgumentException("Invalid calendar day.");
            await transaction.LockAsync("IO.Calendar." + calendarId.ToString("N"), token);
            if (!await db.Calendars.AnyAsync(x => x.Id == calendarId, token)) throw new ArgumentException("Unknown calendar.");
            if (await db.WorkScheduleAssignments.AnyAsync(x => x.CalendarId == calendarId, token))
                throw new InvalidOperationException("An assigned calendar cannot be modified; create a new version.");
            if (await db.CalendarDays.AnyAsync(x => x.CalendarId == calendarId && x.Date == date, token))
                throw new InvalidOperationException("Calendar day already exists; create a new calendar version instead of overwriting.");
            var id = Guid.NewGuid();
            db.CalendarDays.Add(new() { Id = id, CalendarId = calendarId, Date = date, Kind = kind, Note = note, RecordedAtUtc = DateTimeOffset.UtcNow });
            return (id, id);
        }, ct);

    public Task<Guid> CreateShiftAsync(Guid actor, Guid companyId, Guid siteId, string name,
        TimeOnly start, TimeOnly end, int endDayOffset, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "schedule.shift.created", async token =>
        {
            ValidateName(name); _ = new FixedShift(start, end, endDayOffset);
            var site = await db.Sites.SingleOrDefaultAsync(x => x.Id == siteId && x.CompanyId == companyId, token)
                ?? throw new ArgumentException("Unknown site or different company.");
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(site.TimeZoneId, out _)) throw new ArgumentException("Unknown site time zone.");
            var id = Guid.NewGuid();
            db.ShiftDefinitions.Add(new() { Id = id, CompanyId = companyId, SiteId = siteId, Name = name,
                StartsAt = start, EndsAt = end, EndDayOffset = endDayOffset });
            return (id, id);
        }, ct);

    public Task<Guid> AssignAsync(Guid actor, Guid employeeId, Guid calendarId, Guid shiftId,
        DateOnly from, DateOnly? until, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "schedule.assignment.created", async token =>
        {
            if (employeeId == Guid.Empty || calendarId == Guid.Empty || shiftId == Guid.Empty ||
                (until is not null && until <= from)) throw new ArgumentException("Invalid schedule interval or identifier.");
            await transaction.LockAsync("IO.WorkSchedules." + employeeId.ToString("N"), token);
            await transaction.LockAsync("IO.Calendar." + calendarId.ToString("N"), token);
            var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == employeeId, token) ?? throw new ArgumentException("Unknown employee.");
            if (!await db.Calendars.AnyAsync(x => x.Id == calendarId && x.CompanyId == employee.CompanyId, token) ||
                !await db.ShiftDefinitions.AnyAsync(x => x.Id == shiftId && x.CompanyId == employee.CompanyId, token))
                throw new ArgumentException("Unknown calendar or shift, or different company.");
            if (await db.WorkScheduleAssignments.AnyAsync(x => x.EmployeeId == employeeId &&
                (until == null || x.ValidFrom < until) && (x.ValidUntilExclusive == null || from < x.ValidUntilExclusive), token))
                throw new InvalidOperationException("Overlapping work schedule assignment.");
            var id = Guid.NewGuid();
            db.WorkScheduleAssignments.Add(new() { Id = id, CompanyId = employee.CompanyId, EmployeeId = employeeId,
                CalendarId = calendarId, ShiftId = shiftId, ValidFrom = from, ValidUntilExclusive = until });
            return (id, id);
        }, ct);
}
