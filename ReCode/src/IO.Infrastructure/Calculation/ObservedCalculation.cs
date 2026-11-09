using System.Text;
using System.Text.Json;
using IO.Domain.Calculation;
using IO.Domain.Scheduling;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Calculation;

/// <summary>Provisional observed presence, never payroll or a period approval.</summary>
public sealed class ObservedCalculation(IoDbContext db, PermissionTransaction transaction)
{
    public const string Manage = "attendance.calculate";
    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) Resolve(TimeOnly start, TimeOnly end, int dayOffset, DateOnly date, TimeZoneInfo zone)
    {
        try { return new FixedShift(start, end, dayOffset).Resolve(date, zone); }
        catch (InvalidOperationException) { throw new DomainConflictException("Ambiguous or invalid shift boundary."); }
    }

    public Task<Guid> CreateRuleAsync(Guid actor, Guid companyId, string name, int beforeMinutes, int afterMinutes, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "calculation.rule.created", async token =>
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl) ||
                beforeMinutes is < 0 or > 720 || afterMinutes is < 0 or > 720 ||
                !await db.Companies.AnyAsync(x => x.Id == companyId, token)) throw new ArgumentException("Invalid calculation rule.");
            var id = Guid.NewGuid();
            db.ObservedRuleVersions.Add(new() { Id = id, CompanyId = companyId, Name = name, BeforeMinutes = beforeMinutes,
                AfterMinutes = afterMinutes, Algorithm = "paired-v1", CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = actor });
            return (id, id);
        }, ct);

    public Task<Guid> CalculateAsync(Guid actor, Guid employeeId, DateOnly workDate, Guid sourceId, Guid ruleId, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "calculation.run.created", async token =>
        {
            if (employeeId == Guid.Empty || sourceId == Guid.Empty || ruleId == Guid.Empty) throw new ArgumentException("Invalid identifier.");
            // Cooperating calculation writers serialize per employee/day; SQL serializable
            // reads protect the input snapshot from concurrent inserts during this run.
            await transaction.LockAsync("IO.Calc." + employeeId.ToString("N") + "." + workDate.ToString("yyyyMMdd"), token);
            var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == employeeId, token)
                ?? throw new ArgumentException("Unknown employee.");
            var rule = await db.ObservedRuleVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ruleId && x.CompanyId == employee.CompanyId, token)
                ?? throw new ArgumentException("Unknown rule or different company.");
            var source = await db.EventSources.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, token)
                ?? throw new ArgumentException("Unknown source.");
            var schedules = await (from assignment in db.WorkScheduleAssignments.AsNoTracking()
                                   join shift in db.ShiftDefinitions on assignment.ShiftId equals shift.Id
                                   join site in db.Sites on shift.SiteId equals site.Id
                                   where assignment.EmployeeId == employeeId && assignment.ValidFrom <= workDate &&
                                       (assignment.ValidUntilExclusive == null || workDate < assignment.ValidUntilExclusive)
                                   select new { assignment.Id, assignment.CalendarId, shift.SiteId, shift.StartsAt, shift.EndsAt, shift.EndDayOffset, site.TimeZoneId })
                .Take(2).ToListAsync(token);
            if (schedules.Count != 1 || source.SiteId != schedules[0].SiteId) throw new ArgumentException("Exactly one schedule at the source site is required.");
            var schedule = schedules[0];
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(schedule.TimeZoneId, out var zone)) throw new ArgumentException("Unavailable time zone.");
            var (shiftStart, shiftEnd) = Resolve(schedule.StartsAt, schedule.EndsAt, schedule.EndDayOffset, workDate, zone);
            var start = shiftStart.AddMinutes(-rule.BeforeMinutes);
            // Include a punch exactly at the configured latest boundary while retaining a half-open interval.
            var end = shiftEnd.AddMinutes(rule.AfterMinutes).AddTicks(1);
            var dayKind = await db.CalendarDays.AsNoTracking().Where(x => x.CalendarId == schedule.CalendarId && x.Date == workDate)
                .Select(x => (int?)x.Kind).SingleOrDefaultAsync(token);
            var anomalies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var adjacentDate in new[] { workDate.DayNumber > DateOnly.MinValue.DayNumber ? workDate.AddDays(-1) : (DateOnly?)null,
                                                   workDate.DayNumber < DateOnly.MaxValue.DayNumber ? workDate.AddDays(1) : (DateOnly?)null })
            {
                if (adjacentDate is null) continue;
                var adjacent = await (from assignment in db.WorkScheduleAssignments.AsNoTracking()
                                      join shift in db.ShiftDefinitions on assignment.ShiftId equals shift.Id
                                      join site in db.Sites on shift.SiteId equals site.Id
                                      where assignment.EmployeeId == employeeId && shift.SiteId == source.SiteId &&
                                          assignment.ValidFrom <= adjacentDate && (assignment.ValidUntilExclusive == null || adjacentDate < assignment.ValidUntilExclusive)
                                      select new { shift.StartsAt, shift.EndsAt, shift.EndDayOffset, site.TimeZoneId }).Take(2).ToListAsync(token);
                if (adjacent.Count > 1) { anomalies.Add("ambiguous-adjacent-schedule"); continue; }
                if (adjacent.Count == 0) continue;
                if (!TimeZoneInfo.TryFindSystemTimeZoneById(adjacent[0].TimeZoneId, out var adjacentZone)) throw new ArgumentException("Unavailable time zone.");
                var (otherStart, otherEnd) = Resolve(adjacent[0].StartsAt, adjacent[0].EndsAt, adjacent[0].EndDayOffset, adjacentDate.Value, adjacentZone);
                if (start < otherEnd.AddMinutes(rule.AfterMinutes).AddTicks(1) && otherStart.AddMinutes(-rule.BeforeMinutes) < end)
                    anomalies.Add("overlapping-work-windows");
            }
            if (dayKind is null) anomalies.Add("unknown-calendar-day");
            else if (dayKind != 0) anomalies.Add("nonworking-day-requires-review");
            if (await db.RawAttendanceEvents.AnyAsync(x => x.SourceId == sourceId && x.OccurredAtUtc == null, token))
                anomalies.Add("source-has-undated-events");
            if (await db.RawEventConflicts.AnyAsync(x => x.SourceId == sourceId, token)) anomalies.Add("source-has-conflicts");
            var rows = await db.RawAttendanceEvents.AsNoTracking().Where(x => x.SourceId == sourceId &&
                    x.OccurredAtUtc >= start && x.OccurredAtUtc < end)
                .OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).Take(501).ToListAsync(token);
            if (rows.Count > 500) throw new DomainConflictException("The provisional input window exceeds 500 events.");
            var snapshot = new List<object>(); var points = new List<AttendancePoint>();
            foreach (var row in rows)
            {
                var mappingIds = new List<(Guid Id, Guid EmployeeId)>();
                if (row.SourceEmployeeIdentifier is not null)
                {
                    var bytes = Encoding.Unicode.GetBytes(row.SourceEmployeeIdentifier);
                    var matches = await db.SourceEmployeeMappings.AsNoTracking().Where(x => x.SourceId == sourceId &&
                        EF.Property<byte[]>(x, "SourcePersonIdBytes") == bytes && EF.Property<int>(x, "SourcePersonIdByteLength") == bytes.Length &&
                        x.ValidFromUtc <= row.OccurredAtUtc && (x.ValidUntilUtcExclusive == null || row.OccurredAtUtc < x.ValidUntilUtcExclusive))
                        .Select(x => new { x.Id, x.EmployeeId }).Take(2).ToListAsync(token);
                    mappingIds.AddRange(matches.Select(x => (x.Id, x.EmployeeId)));
                }
                if (mappingIds.Count != 1) { anomalies.Add("unresolved-source-events"); snapshot.Add(new { row.Id, row.OccurredAtUtc, row.SourceEmployeeIdentifier, Mapping = "unresolved" }); continue; }
                if (mappingIds[0].EmployeeId != employeeId)
                {
                    snapshot.Add(new { row.Id, row.OccurredAtUtc, row.SourceEmployeeIdentifier, row.ExternalEventId, row.ReportedEventType,
                        MappingId = mappingIds[0].Id, MappedEmployeeId = mappingIds[0].EmployeeId });
                    continue;
                }
                snapshot.Add(new { row.Id, row.OccurredAtUtc, row.SourceEmployeeIdentifier, row.ExternalEventId, row.ReportedEventType, MappingId = mappingIds[0].Id });
                if (row.ExternalEventId is null) anomalies.Add("event-identity-requires-review");
                var kind = row.ReportedEventType switch { "in" => PunchKind.In, "out" => PunchKind.Out, _ => (PunchKind?)null };
                if (kind is null) anomalies.Add("unknown-event-type");
                else points.Add(new(row.Id, row.OccurredAtUtc!.Value, kind.Value));
            }
            // Only immutable, explicitly approved missing-punch requests for this employee/day
            // may contribute. A decision is verified separately; pending or inconsistent records
            // block a numeric result rather than being guessed away.
            var corrections = await (from request in db.CorrectionRequests.AsNoTracking()
                                     join policy in db.CorrectionPolicies on request.PolicyId equals policy.Id
                                     join decision in db.CorrectionDecisions on request.Id equals decision.RequestId into decisions
                                     from decision in decisions.DefaultIfEmpty()
                                     where request.EmployeeId == employeeId && request.WorkDate == workDate && (request.State == 0 || request.State == 1)
                                     orderby request.CreatedAtUtc, request.Id
                                     select new { request.Id, request.CompanyId, request.PolicyId, request.RequesterUserId, request.State,
                                         request.ProposedAtUtc, request.PunchKind, request.Reason, request.CreatedAtUtc,
                                         policy.ApproverUserId, DecisionId = decision == null ? (Guid?)null : decision.Id,
                                         Decision = decision == null ? (int?)null : decision.Decision,
                                         DecisionActor = decision == null ? (Guid?)null : decision.ActorUserId,
                                         DecisionAtUtc = decision == null ? (DateTimeOffset?)null : decision.AtUtc,
                                         DecisionReason = decision == null ? null : decision.Reason }).Take(501).ToListAsync(token);
            if (corrections.Count > 500) throw new DomainConflictException("The correction input exceeds 500 requests.");
            foreach (var correction in corrections)
            {
                if (correction.State == 0) { anomalies.Add("pending-correction"); continue; }
                if (correction.Decision != 1 || correction.DecisionId is null || correction.DecisionActor != correction.ApproverUserId ||
                    correction.DecisionActor == correction.RequesterUserId || correction.CompanyId != employee.CompanyId ||
                    correction.DecisionAtUtc < correction.CreatedAtUtc)
                { anomalies.Add("invalid-correction-decision"); continue; }
                if (correction.ProposedAtUtc < start || correction.ProposedAtUtc >= end)
                { anomalies.Add("correction-outside-window"); continue; }
                points.Add(new(correction.Id, correction.ProposedAtUtc, (PunchKind)correction.PunchKind));
            }
            var calculated = ObservedAttendance.Calculate(employeeId, workDate, ruleId, start, end, points, []);
            foreach (var anomaly in calculated.Anomalies) anomalies.Add(anomaly);
            var id = Guid.NewGuid();
            db.DailyCalculationRuns.Add(new() { Id = id, CompanyId = employee.CompanyId, EmployeeId = employeeId,
                RuleVersionId = ruleId, SourceId = sourceId, ScheduleAssignmentId = schedule.Id, WorkDate = workDate,
                WindowStartUtc = start, WindowEndUtc = end, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = actor,
                ObservedTicks = anomalies.Count == 0 ? calculated.ObservedPresence?.Ticks : null,
                InputSnapshotJson = JsonSerializer.Serialize(new { rule.Algorithm, rule.BeforeMinutes, rule.AfterMinutes,
                    schedule.Id, schedule.CalendarId, schedule.SiteId, schedule.TimeZoneId, ShiftStartUtc = shiftStart, ShiftEndUtc = shiftEnd,
                    DayKind = dayKind, Events = snapshot, Corrections = corrections }),
                AnomaliesJson = JsonSerializer.Serialize(anomalies.Order(StringComparer.Ordinal)) });
            return (id, id);
        }, ct);

    public Task<object> ListRulesAsync(Guid actor, Guid companyId, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Manage, "calculation.rules.viewed", async token =>
        {
            if (companyId == Guid.Empty || offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var items = await db.ObservedRuleVersions.AsNoTracking().Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(offset).Take(50).ToListAsync(token);
            return (new { companyId, offset, pageSize = 50, items }, companyId);
        }, ct);

    public Task<object> ListRunsAsync(Guid actor, Guid employeeId, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Manage, "calculation.runs.viewed", async token =>
        {
            if (employeeId == Guid.Empty || offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var items = await db.DailyCalculationRuns.AsNoTracking().Where(x => x.EmployeeId == employeeId)
                .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(offset).Take(50).ToListAsync(token);
            return (new { employeeId, offset, pageSize = 50, freshness = "unknown", scope = "single-selected-source", items }, employeeId);
        }, ct);
}
