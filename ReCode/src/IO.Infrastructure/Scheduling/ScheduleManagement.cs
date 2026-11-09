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

    public Task<object> ListAsync(Guid actor, string kind, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Manage, "schedule.catalog.viewed", async token =>
        {
            if (offset is < 0 or > 1_000_000) throw new ArgumentException("Invalid offset.");
            const int size = 100;
            object items = kind switch
            {
                "calendars" => await db.Calendars.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(size).Select(x => new { x.Id, x.CompanyId, x.Name }).ToListAsync(token),
                "days" => await db.CalendarDays.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(size).Select(x => new { x.Id, x.CalendarId, x.Date, x.Kind, x.Note, x.RecordedAtUtc }).ToListAsync(token),
                "shifts" => await db.ShiftDefinitions.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(size).Select(x => new { x.Id, x.CompanyId, x.SiteId, x.Name, x.StartsAt, x.EndsAt, x.EndDayOffset }).ToListAsync(token),
                "assignments" => await db.WorkScheduleAssignments.AsNoTracking().OrderBy(x => x.Id).Skip(offset).Take(size).Select(x => new { x.Id, x.EmployeeId, x.CompanyId, x.CalendarId, x.ShiftId, x.ValidFrom, x.ValidUntilExclusive }).ToListAsync(token),
                _ => throw new ArgumentException("Unknown catalog type.")
            };
            return (new { kind, offset, pageSize = size, items }, actor);
        }, ct);

    public Task<Guid> CreateCalendarAsync(Guid actor, Guid companyId, string name, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "schedule.calendar.created", async token =>
        {
            ValidateName(name);
            if (!await db.Companies.AnyAsync(x => x.Id == companyId, token)) throw new ArgumentException("Unknown company.");
            var id = Guid.NewGuid(); db.Calendars.Add(new() { Id = id, CompanyId = companyId, Name = name });
            return (id, id);
        }, ct);

    public Task<Guid> ForkCalendarAsync(Guid actor, Guid sourceId, string name, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "schedule.calendar.fork." + sourceId.ToString("N"), async token =>
        {
            ValidateName(name);
            if (sourceId == Guid.Empty) throw new ArgumentException("Invalid calendar.");
            await transaction.LockAsync("IO.Calendar." + sourceId.ToString("N"), token);
            var source = await db.Calendars.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, token)
                ?? throw new ArgumentException("Unknown calendar.");
            var days = await db.CalendarDays.AsNoTracking().Where(x => x.CalendarId == sourceId)
                .OrderBy(x => x.Date).Take(10001).ToListAsync(token);
            if (days.Count > 10000) throw new DomainConflictException("Calendar exceeds the supported copy size.");
            var id = Guid.NewGuid();
            db.Calendars.Add(new() { Id = id, CompanyId = source.CompanyId, Name = name });
            var now = DateTimeOffset.UtcNow;
            foreach (var day in days)
                db.CalendarDays.Add(new() { Id = Guid.NewGuid(), CalendarId = id, Date = day.Date,
                    Kind = day.Kind, Note = day.Note, RecordedAtUtc = now });
            return (id, id);
        }, ct);

    public Task<bool> ReviseCalendarDayAsync(Guid actor, Guid calendarId, DateOnly date, int kind, string? note, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "schedule.calendar-day.revised", async token =>
        {
            if (kind is < 0 or > 2 || note is { Length: > 500 }) throw new ArgumentException("Invalid calendar day.");
            await transaction.LockAsync("IO.Calendar." + calendarId.ToString("N"), token);
            if (await db.WorkScheduleAssignments.AnyAsync(x => x.CalendarId == calendarId, token))
                throw new DomainConflictException("An assigned calendar cannot be modified; create a new version.");
            var day = await db.CalendarDays.SingleOrDefaultAsync(x => x.CalendarId == calendarId && x.Date == date, token)
                ?? throw new ArgumentException("Unknown calendar day.");
            day.Kind = kind; day.Note = note; day.RecordedAtUtc = DateTimeOffset.UtcNow;
            return (true, day.Id);
        }, ct);

    public Task<Guid> AddCalendarDayAsync(Guid actor, Guid calendarId, DateOnly date, int kind, string? note, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "schedule.calendar-day.created", async token =>
        {
            if (kind is < 0 or > 2 || note is { Length: > 500 }) throw new ArgumentException("Invalid calendar day.");
            await transaction.LockAsync("IO.Calendar." + calendarId.ToString("N"), token);
            if (!await db.Calendars.AnyAsync(x => x.Id == calendarId, token)) throw new ArgumentException("Unknown calendar.");
            if (await db.WorkScheduleAssignments.AnyAsync(x => x.CalendarId == calendarId, token))
                throw new DomainConflictException("An assigned calendar cannot be modified; create a new version.");
            if (await db.CalendarDays.AnyAsync(x => x.CalendarId == calendarId && x.Date == date, token))
                throw new DomainConflictException("Calendar day already exists; create a new calendar version instead of overwriting.");
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

    public Task<Guid> RescheduleAsync(Guid actor, Guid employeeId, Guid assignmentId, Guid calendarId, Guid shiftId,
        DateOnly effective, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "schedule.replace." + assignmentId.ToString("N"), async token =>
        {
            if (employeeId == Guid.Empty || calendarId == Guid.Empty || shiftId == Guid.Empty) throw new ArgumentException("Invalid identifier.");
            await transaction.LockAsync("IO.WorkSchedules." + employeeId.ToString("N"), token);
            await transaction.LockAsync("IO.Calendar." + calendarId.ToString("N"), token);
            var original = await db.WorkScheduleAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId && x.EmployeeId == employeeId, token)
                ?? throw new ArgumentException("Unknown schedule assignment.");
            if (original.ValidUntilExclusive is not null || effective <= original.ValidFrom)
                throw new DomainConflictException("Replacement requires an open schedule and a later effective date.");
            if (!await db.Calendars.AnyAsync(x => x.Id == calendarId && x.CompanyId == original.CompanyId, token) ||
                !await db.ShiftDefinitions.AnyAsync(x => x.Id == shiftId && x.CompanyId == original.CompanyId, token))
                throw new ArgumentException("Unknown calendar or shift, or different company.");
            if (await db.WorkScheduleAssignments.AnyAsync(x => x.EmployeeId == employeeId && x.Id != assignmentId &&
                (x.ValidUntilExclusive == null || effective < x.ValidUntilExclusive), token))
                throw new DomainConflictException("Replacement overlaps an existing schedule.");
            original.ValidUntilExclusive = effective;
            var id = Guid.NewGuid();
            db.WorkScheduleAssignments.Add(new() { Id = id, EmployeeId = employeeId, CompanyId = original.CompanyId,
                CalendarId = calendarId, ShiftId = shiftId, ValidFrom = effective });
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
                throw new DomainConflictException("Overlapping work schedule assignment.");
            var id = Guid.NewGuid();
            db.WorkScheduleAssignments.Add(new() { Id = id, CompanyId = employee.CompanyId, EmployeeId = employeeId,
                CalendarId = calendarId, ShiftId = shiftId, ValidFrom = from, ValidUntilExclusive = until });
            return (id, id);
        }, ct);
}
