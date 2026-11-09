using System.Text.Json;
using IO.Infrastructure.Calculation;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Periods;

/// <summary>Immutable, historical diagnostics only. Never validates or approves a period.</summary>
public sealed class PeriodDiagnostics(IoDbContext db, PermissionTransaction transaction)
{
    public const string Manage = "period.diagnostic.manage";
    private const string Format = "diagnostic-v1";
    private sealed record RunView(Guid Id, DateOnly WorkDate, DateTimeOffset CreatedAtUtc, long? InputRevision, long? ObservedTicks, string AnomaliesJson, string? InputMonitorState);
    private sealed record EvidenceDay(DateOnly Date, Guid? RunId, long? InputRevision, long? ObservedTicks, string? AnomaliesJson, string? MonitorAtCapture = null);
    private sealed record Evidence(string Version, Guid DraftId, Guid EmployeeId, DateOnly Start, DateOnly EndExclusive,
        long? InputRevision, EvidenceDay[] Days, string? MonitorAtCapture = null);

    private async Task<List<RunView>> RunsAsync(PeriodDraftRecord draft, CancellationToken ct)
    {
        var rows = await db.DailyCalculationRuns.AsNoTracking().Where(x => x.EmployeeId == draft.EmployeeId &&
                x.WorkDate >= draft.Start && x.WorkDate < draft.EndExclusive)
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Select(x => new RunView(x.Id, x.WorkDate, x.CreatedAtUtc, x.InputRevision, x.ObservedTicks, x.AnomaliesJson, x.InputMonitorState))
            .Take(1001).ToListAsync(ct);
        if (rows.Count > 1000) throw new DomainConflictException("Too many runs in the diagnostic interval.");
        return rows;
    }

    private static Dictionary<DateOnly, RunView> Latest(IEnumerable<RunView> rows) =>
        rows.GroupBy(x => x.WorkDate).ToDictionary(x => x.Key, x => x.First());

    private Task<long?> RevisionAsync(CancellationToken ct) => db.CalculationInputRevisions.AsNoTracking()
        .Where(x => x.Id == 1).Select(x => (long?)x.Revision).SingleOrDefaultAsync(ct);

    public Task<Guid> RecordAsync(Guid actor, Guid draftId, CancellationToken ct = default) =>
        transaction.ExecuteAsync(actor, Manage, "period.diagnostic.recorded", async token =>
        {
            if (draftId == Guid.Empty) throw new ArgumentException("Invalid draft.");
            await transaction.LockAsync("IO.PeriodDiagnostic." + draftId.ToString("N"), token);
            var draft = await db.PeriodDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == draftId, token)
                ?? throw new ArgumentException("Unknown draft.");
            var latest = Latest(await RunsAsync(draft, token));
            var monitor = await CalculationMonitorHealth.InspectAsync(db, token);
            var revision = await RevisionAsync(token) ?? throw new DomainConflictException("Calculation input revision unavailable.");
            var days = Enumerable.Range(0, draft.EndExclusive.DayNumber - draft.Start.DayNumber).Select(i =>
            {
                var date = draft.Start.AddDays(i); var run = latest.GetValueOrDefault(date);
                return new EvidenceDay(date, run?.Id, run?.InputRevision, run?.ObservedTicks, run?.AnomaliesJson, run?.InputMonitorState);
            }).ToArray();
            var evidence = new Evidence(Format, draft.Id, draft.EmployeeId, draft.Start, draft.EndExclusive, revision, days, monitor);
            var id = Guid.NewGuid();
            db.PeriodDiagnostics.Add(new() { Id = id, DraftId = draftId, InputRevision = revision,
                EvidenceJson = JsonSerializer.Serialize(evidence), CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = actor });
            return (id, id);
        }, ct);

    public Task<object> ListAsync(Guid actor, Guid draftId, int offset, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Manage, "period.diagnostics.viewed", async token =>
        {
            if (draftId == Guid.Empty || offset is < 0 or > 100000) throw new ArgumentException("Invalid page.");
            var items = await db.PeriodDiagnostics.AsNoTracking().Where(x => x.DraftId == draftId)
                .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(offset).Take(50)
                .Select(x => new { x.Id, x.DraftId, x.InputRevision, x.CreatedAtUtc, x.CreatedByUserId }).ToListAsync(token);
            return (new { draftId, offset, pageSize = 50, status = "historical-diagnostic-only", approvable = false, items }, draftId);
        }, ct);

    public Task<object> AssessAsync(Guid actor, Guid diagnosticId, CancellationToken ct = default) =>
        transaction.ExecuteAsync<object>(actor, Manage, "period.diagnostic.assessed", async token =>
        {
            if (diagnosticId == Guid.Empty) throw new ArgumentException("Invalid diagnostic.");
            var record = await db.PeriodDiagnostics.AsNoTracking().SingleOrDefaultAsync(x => x.Id == diagnosticId, token)
                ?? throw new ArgumentException("Unknown diagnostic.");
            var draft = await db.PeriodDrafts.AsNoTracking().SingleAsync(x => x.Id == record.DraftId, token);
            Evidence? evidence;
            try { evidence = JsonSerializer.Deserialize<Evidence>(record.EvidenceJson); }
            catch (JsonException) { evidence = null; }
            var length = draft.EndExclusive.DayNumber - draft.Start.DayNumber;
            var valid = evidence is not null && evidence.Version == Format && evidence.DraftId == draft.Id &&
                evidence.EmployeeId == draft.EmployeeId && evidence.Start == draft.Start && evidence.EndExclusive == draft.EndExclusive &&
                evidence.InputRevision == record.InputRevision && evidence.Days is not null && evidence.Days.Length == length &&
                evidence.Days.Select((day, i) => day is not null && day.Date == draft.Start.AddDays(i)).All(x => x);
            if (!valid)
                return (new { diagnosticId, status = "unrecognized-evidence", freshness = "unknown", approvable = false }, diagnosticId);
            var runs = await RunsAsync(draft, token);
            var latest = Latest(runs);
            // Verify every recorded ID and captured value against its immutable run;
            // malformed or externally fabricated evidence must not look consistent.
            var byId = runs.ToDictionary(x => x.Id);
            var days = evidence!.Days!;
            valid = days.All(day => day.RunId is null
                ? day.InputRevision is null && day.ObservedTicks is null && day.AnomaliesJson is null && day.MonitorAtCapture is null
                : byId.TryGetValue(day.RunId.Value, out var run) && run.WorkDate == day.Date &&
                  run.InputRevision == day.InputRevision && run.ObservedTicks == day.ObservedTicks && run.InputMonitorState == day.MonitorAtCapture &&
                  string.Equals(run.AnomaliesJson, day.AnomaliesJson, StringComparison.Ordinal));
            if (!valid)
                return (new { diagnosticId, status = "unrecognized-evidence", freshness = "unknown", approvable = false }, diagnosticId);
            var changedDays = days.Count(day => latest.GetValueOrDefault(day.Date)?.Id != day.RunId);
            var current = await RevisionAsync(token);
            var monitor = await CalculationMonitorHealth.InspectAsync(db, token);
            var databaseChange = CalculationInputRevisionStatus.Evaluate(record.InputRevision, current, evidence.MonitorAtCapture, monitor);
            var status = changedDays > 0 ? "latest-results-changed" :
                databaseChange == "no-monitored-database-change-detected" ? "no-monitored-diagnostic-change-detected" : databaseChange;
            var assessedDays = days.Select(day => new { day.Date, day.RunId, day.InputRevision, day.ObservedTicks, day.AnomaliesJson, day.MonitorAtCapture,
                databaseChange = day.RunId is null ? "no-run" : CalculationInputRevisionStatus.Evaluate(day.InputRevision, current, day.MonitorAtCapture, monitor) }).ToArray();
            return (new { diagnosticId, draftId = draft.Id, status, databaseChange, changedDays, monitorScope = "database-wide",
                record.CreatedAtUtc, record.CreatedByUserId, record.InputRevision, monitorAtCapture = evidence.MonitorAtCapture, currentRevision = current, currentMonitor = monitor,
                missingDays = days.Count(x => x.RunId is null),
                daysWithAnomaliesOrNoValue = days.Count(x => x.RunId is not null && (x.ObservedTicks is null || x.AnomaliesJson != "[]")),
                runsWithChangeOrUnknown = assessedDays.Count(x => x.RunId is not null && x.databaseChange != "no-monitored-database-change-detected"),
                days = assessedDays, freshness = "unknown", approvable = false,
                warning = "Diagnostic only; trigger metadata checks do not detect disabled intervals or skipped writes. No detected difference does not prove freshness or authorize approval." }, diagnosticId);
        }, ct);
}
