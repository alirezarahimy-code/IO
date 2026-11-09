using IO.Domain.Calculation;
using IO.Domain.Workflows;

namespace IO.Domain.Periods;

public enum PeriodState { Open, Reviewed, Approved }
public sealed record PeriodAction(Guid ActorId, string Action, DateTimeOffset AtUtc, string? Reason, Guid? RunId);

/// <summary>In-memory period lifecycle. Persistence, locking and scope enforcement are external requirements.</summary>
public sealed class AttendancePeriod
{
    private readonly List<PeriodAction> history = [];
    public Guid Id { get; }
    public Guid EmployeeId { get; }
    public DateOnly Start { get; }
    public DateOnly EndExclusive { get; }
    public PeriodState State { get; private set; }
    public Guid? ReviewedRunId { get; private set; }
    public IReadOnlyList<PeriodAction> History => history.AsReadOnly();
    public AttendancePeriod(Guid id, Guid employeeId, DateOnly start, DateOnly endExclusive)
    {
        if (id == Guid.Empty || employeeId == Guid.Empty || endExclusive <= start) throw new ArgumentException("Invalid period.");
        Id = id; EmployeeId = employeeId; Start = start; EndExclusive = endExclusive;
    }
    private void Validate(Guid actor, DateTimeOffset time)
    {
        if (actor == Guid.Empty || time.Offset != TimeSpan.Zero) throw new ArgumentException("Actor and UTC time required.");
        if (history.Count > 0 && time < history[^1].AtUtc) throw new InvalidOperationException("Action time precedes history.");
    }
    public void Review(Guid actor, DateTimeOffset time, Guid runId, IReadOnlyList<AttendanceResult> results)
    {
        Validate(actor, time);
        if (State != PeriodState.Open || runId == Guid.Empty || results is null || results.Count == 0 ||
            results.Count != EndExclusive.DayNumber - Start.DayNumber ||
            results.Select(x => x.WorkDate).Distinct().Count() != results.Count ||
            results.Any(x => x.EmployeeId != EmployeeId || x.WorkDate < Start || x.WorkDate >= EndExclusive ||
                x.Anomalies.Count != 0 || x.ObservedPresence is null))
            throw new InvalidOperationException("Period must be open with complete anomaly-free results for its employee.");
        ReviewedRunId = runId; State = PeriodState.Reviewed; history.Add(new(actor, "reviewed", time, null, runId));
    }
    public void Approve(Guid actor, DateTimeOffset time, Guid expectedRunId)
    {
        Validate(actor, time);
        if (State != PeriodState.Reviewed || ReviewedRunId != expectedRunId) throw new InvalidOperationException("Review changed or is absent.");
        State = PeriodState.Approved; history.Add(new(actor, "approved", time, null, expectedRunId));
    }
    public void Reopen(Guid actor, DateTimeOffset time, string reason)
    {
        Validate(actor, time); ApprovalRequest.ValidateReason(reason);
        if (State == PeriodState.Open) throw new InvalidOperationException("Period is already open.");
        history.Add(new(actor, "reopened", time, reason, ReviewedRunId));
        ReviewedRunId = null; State = PeriodState.Open;
    }
}
