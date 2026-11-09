using IO.Domain.Scheduling;
using IO.Domain.Attendance;
using IO.Domain.Organization;
using IO.Domain.Calculation;
using IO.Domain.Workflows;
using IO.Domain.Periods;

// Dependency-free executable checks. Run with `dotnet run --project tests/IO.Domain.Tests`.
var failures = 0;
var count = 0;
void Check(string name, Action test)
{
    count++;
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
}
void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

var employee = Guid.NewGuid();
var otherEmployee = Guid.NewGuid();
var unit = Guid.NewGuid();
var jan1 = new DateOnly(2026, 1, 1);
var feb1 = new DateOnly(2026, 2, 1);
var mar1 = new DateOnly(2026, 3, 1);
EmployeeOrgAssignment Assignment(Guid employeeId, DateOnly start, DateOnly? end) =>
    new(Guid.NewGuid(), employeeId, unit, start, end);

Check("assignment rejects empty identity", () =>
    Throws<ArgumentException>(() => new EmployeeOrgAssignment(Guid.Empty, employee, unit, jan1, feb1)));
Check("assignment rejects empty employee or unit", () =>
{
    Throws<ArgumentException>(() => Assignment(Guid.Empty, jan1, feb1));
    Throws<ArgumentException>(() => new EmployeeOrgAssignment(Guid.NewGuid(), employee, Guid.Empty, jan1, feb1));
});
Check("end before or equal to start rejected", () =>
{
    Throws<ArgumentException>(() => Assignment(employee, feb1, jan1));
    Throws<ArgumentException>(() => Assignment(employee, jan1, jan1));
});
Check("half-open interval boundaries", () =>
{
    var a = Assignment(employee, jan1, feb1);
    Assert(!a.Includes(jan1.AddDays(-1)), "Before start included");
    Assert(a.Includes(jan1), "Start excluded");
    Assert(a.Includes(feb1.AddDays(-1)), "Last day excluded");
    Assert(!a.Includes(feb1), "Exclusive end included");
});
Check("adjacent intervals do not overlap", () =>
{
    var a = Assignment(employee, jan1, feb1);
    var b = Assignment(employee, feb1, mar1);
    Assert(!a.Overlaps(b) && !b.Overlaps(a), "Adjacent intervals overlap");
});
Check("overlapping intervals are symmetric", () =>
{
    var a = Assignment(employee, jan1, mar1);
    var b = Assignment(employee, feb1, null);
    Assert(a.Overlaps(b) && b.Overlaps(a), "Overlap not detected");
});
Check("open ended interval overlaps later intervals", () =>
    Assert(Assignment(employee, jan1, null).Overlaps(Assignment(employee, mar1, null)), "Expected overlap"));
Check("different employees do not overlap", () =>
    Assert(!Assignment(employee, jan1, null).Overlaps(Assignment(otherEmployee, jan1, null)), "Unexpected overlap"));
Check("timeline finds historical assignment at boundary", () =>
{
    var timeline = new EmployeeAssignmentTimeline(employee);
    var first = Assignment(employee, jan1, feb1);
    var second = Assignment(employee, feb1, null);
    timeline.Add(first); timeline.Add(second);
    Assert(timeline.At(jan1.AddDays(-1)) is null, "Expected no assignment");
    Assert(timeline.At(feb1.AddDays(-1))?.Id == first.Id, "Wrong previous assignment");
    Assert(timeline.At(feb1)?.Id == second.Id, "Wrong new assignment");
});
Check("timeline rejects overlapping and foreign assignment", () =>
{
    var timeline = new EmployeeAssignmentTimeline(employee);
    timeline.Add(Assignment(employee, jan1, mar1));
    Throws<InvalidOperationException>(() => timeline.Add(Assignment(employee, feb1, null)));
    Throws<ArgumentException>(() => timeline.Add(Assignment(otherEmployee, mar1, null)));
    Assert(timeline.Assignments.Count == 1, "Rejected assignments were added");
});
Check("timeline rejects repeated ID even without overlap", () =>
{
    var timeline = new EmployeeAssignmentTimeline(employee);
    var id = Guid.NewGuid();
    timeline.Add(new EmployeeOrgAssignment(id, employee, unit, jan1, feb1));
    Throws<InvalidOperationException>(() => timeline.Add(new EmployeeOrgAssignment(id, employee, unit, feb1, mar1)));
});

var offset = TimeSpan.FromHours(3.5);
var occurred = new DateTimeOffset(2026, 1, 1, 12, 0, 0, offset);
var received = new DateTimeOffset(2026, 1, 1, 12, 1, 0, offset);
RawAttendanceEvent Event(byte[] payload) => new(Guid.NewGuid(), Guid.NewGuid(), "evt-1", "123", occurred,
    received, "Asia/Tehran", "in", payload);
Check("raw event normalizes timestamps to UTC", () =>
{
    var e = Event([1, 2]);
    Assert(e.OccurredAtUtc == occurred.ToUniversalTime(), "Occurred instant changed");
    Assert(e.OccurredAtUtc?.Offset == TimeSpan.Zero, "Occurred offset is not UTC");
    Assert(e.ReceivedAtUtc == received.ToUniversalTime() && e.ReceivedAtUtc.Offset == TimeSpan.Zero, "Received instant changed");
});
Check("raw event copies input and output payloads", () =>
{
    byte[] data = [1, 2];
    var e = Event(data);
    data[0] = 9;
    var copy = e.GetSourcePayload();
    copy[1] = 9;
    Assert(e.GetSourcePayload().SequenceEqual(new byte[] { 1, 2 }), "Raw bytes were mutated");
});
Check("missing parsed fields remain unresolved", () =>
{
    var e = new RawAttendanceEvent(Guid.NewGuid(), Guid.NewGuid(), null, null, null, received, null, null, [0xff]);
    Assert(e.ExternalEventId is null && e.SourceEmployeeIdentifier is null && e.OccurredAtUtc is null && e.ReportedEventType is null,
        "Missing source data was guessed");
    Assert(e.GetSourcePayload().SequenceEqual(new byte[] { 0xff }), "Unparsed data was lost");
});
Check("blank external ID and missing internal IDs rejected", () =>
{
    Throws<ArgumentException>(() => new RawAttendanceEvent(Guid.NewGuid(), Guid.NewGuid(), "  ", null, null, received, null, null, []));
    Throws<ArgumentException>(() => new RawAttendanceEvent(Guid.Empty, Guid.NewGuid(), null, null, null, received, null, null, []));
    Throws<ArgumentException>(() => new RawAttendanceEvent(Guid.NewGuid(), Guid.Empty, null, null, null, received, null, null, []));
});

Check("Persian dates round-trip around Nowruz", () =>
{
    var nowruz = PersianDates.ToGregorian(1403, 1, 1);
    Assert(nowruz == new DateOnly(2024, 3, 20), "Incorrect Nowruz conversion");
    Assert(PersianDates.FromGregorian(nowruz) == (1403, 1, 1), "Nowruz did not round trip");
    Assert(PersianDates.FromGregorian(nowruz.AddDays(-1)) == (1402, 12, 29), "Wrong year boundary");
});
Check("Persian leap day and invalid dates", () =>
{
    var leapDay = PersianDates.ToGregorian(1399, 12, 30);
    Assert(PersianDates.FromGregorian(leapDay) == (1399, 12, 30), "Leap day did not round trip");
    Throws<ArgumentOutOfRangeException>(() => PersianDates.ToGregorian(1402, 12, 30));
    Throws<ArgumentOutOfRangeException>(() => PersianDates.ToGregorian(1403, 13, 1));
});
Check("fixed shift requires explicit positive bounded interval", () =>
{
    Throws<ArgumentException>(() => new FixedShift(new TimeOnly(22, 0), new TimeOnly(6, 0), 0));
    Throws<ArgumentException>(() => new FixedShift(new TimeOnly(8, 0), new TimeOnly(8, 0), 0));
    Throws<ArgumentException>(() => new FixedShift(new TimeOnly(8, 0), new TimeOnly(9, 0), 1));
    Throws<ArgumentOutOfRangeException>(() => new FixedShift(new TimeOnly(8, 0), new TimeOnly(9, 0), 2));
});
Check("overnight shift retains work date and resolves UTC", () =>
{
    var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    var shift = new FixedShift(new TimeOnly(22, 0), new TimeOnly(6, 0), 1);
    var (start, end) = shift.Resolve(new DateOnly(2026, 1, 1), zone);
    Assert(start == new DateTimeOffset(2026, 1, 1, 18, 30, 0, TimeSpan.Zero), "Unexpected start UTC");
    Assert(end == new DateTimeOffset(2026, 1, 2, 2, 30, 0, TimeSpan.Zero), "Unexpected end UTC");
});
Check("invalid and ambiguous DST boundaries fail closed", () =>
{
    var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    var invalid = new FixedShift(new TimeOnly(2, 30), new TimeOnly(4, 0), 0);
    var ambiguous = new FixedShift(new TimeOnly(1, 30), new TimeOnly(4, 0), 0);
    Throws<InvalidOperationException>(() => invalid.Resolve(new DateOnly(2026, 3, 8), zone));
    Throws<InvalidOperationException>(() => ambiguous.Resolve(new DateOnly(2026, 11, 1), zone));
});

var owner = Guid.NewGuid(); var approver1 = Guid.NewGuid(); var approver2 = Guid.NewGuid();
var calculationEmployee = Guid.NewGuid(); var ruleVersion = Guid.NewGuid();
var workDate = new DateOnly(2026, 1, 1);
var windowStart = new DateTimeOffset(2026, 1, 1, 18, 0, 0, TimeSpan.Zero);
var windowEnd = windowStart.AddHours(12);
var entry = new AttendancePoint(Guid.NewGuid(), windowStart.AddHours(1), PunchKind.In);
var exit = new AttendancePoint(Guid.NewGuid(), windowStart.AddHours(9), PunchKind.Out);
AttendanceResult Compute(IEnumerable<AttendancePoint> points, IEnumerable<ApprovedCorrection> changes) =>
    ObservedAttendance.Calculate(calculationEmployee, workDate, ruleVersion, windowStart, windowEnd, points, changes);
Check("observed time pairs overnight punches without treating them as payroll", () =>
{
    var result = Compute([exit, entry], []);
    Assert(result.ObservedPresence == TimeSpan.FromHours(8) && result.RuleVersion == ruleVersion && result.Anomalies.Count == 0, "Wrong elapsed time");
});
Check("incomplete and simultaneous punches fail closed", () =>
{
    var missing = Compute([entry], []);
    Assert(missing.ObservedPresence is null && missing.Anomalies.Contains("unpaired-punch"), "Incomplete pair accepted");
    var same = Compute([entry, new(Guid.NewGuid(), entry.AtUtc, PunchKind.Out)], []);
    Assert(same.ObservedPresence is null && same.Anomalies.Contains("simultaneous-punches"), "Simultaneous pair accepted");
    Throws<ArgumentException>(() => Compute([entry, entry], []));
    Throws<ArgumentException>(() => Compute([new(Guid.NewGuid(), windowEnd, PunchKind.In)], []));
});
var hash = ApprovedCorrection.ComputePayloadHash([], [exit]);
var correctionRequest = new ApprovalRequest(Guid.NewGuid(), calculationEmployee, owner, RequestKind.AttendanceCorrection,
    new[] { new[] { approver1 }, new[] { approver2 } }, hash);
Check("multi-step correction requires designated approvers and approved content", () =>
{
    Throws<InvalidOperationException>(() => new ApprovedCorrection(correctionRequest, [], [exit]));
    correctionRequest.Submit(owner, windowStart);
    Throws<InvalidOperationException>(() => correctionRequest.Approve(approver2, windowStart));
    Throws<InvalidOperationException>(() => correctionRequest.Approve(approver1, windowStart.AddSeconds(-1)));
    correctionRequest.Approve(approver1, windowStart);
    Throws<InvalidOperationException>(() => new ApprovedCorrection(correctionRequest, [], [exit]));
    correctionRequest.Approve(approver2, windowStart);
    Assert(correctionRequest.State == RequestState.Approved && correctionRequest.History.Count == 3, "Wrong state or history");
    Throws<InvalidOperationException>(() => new ApprovedCorrection(correctionRequest, [], [entry]));
});
Check("approved correction feeds calculation without mutating raw points", () =>
{
    var raw = new[] { entry };
    var before = Compute(raw, []);
    var corrected = Compute(raw, [new ApprovedCorrection(correctionRequest, [], [exit])]);
    Assert(raw.Length == 1 && before.ObservedPresence is null && corrected.ObservedPresence == TimeSpan.FromHours(8) &&
        corrected.AppliedRequestIds.Single() == correctionRequest.Id && corrected.InputIds.Single() == entry.Id, "History or source was altered");
    Throws<ArgumentException>(() => Compute(raw, [new ApprovedCorrection(correctionRequest, [], [exit]), new ApprovedCorrection(correctionRequest, [], [exit])]));
});
Check("request rejection and cancellation require reason and cannot be approved later", () =>
{
    var self = new ApprovalRequest(Guid.NewGuid(), calculationEmployee, owner, RequestKind.Leave, new[] { new[] { owner } });
    self.Submit(owner, windowStart);
    Throws<InvalidOperationException>(() => self.Approve(owner, windowStart));
    var rejected = new ApprovalRequest(Guid.NewGuid(), calculationEmployee, owner, RequestKind.Leave, new[] { new[] { approver1 } });
    rejected.Submit(owner, windowStart);
    Throws<ArgumentException>(() => rejected.Reject(approver1, windowStart, ""));
    rejected.Reject(approver1, windowStart, "test rejection");
    Throws<InvalidOperationException>(() => rejected.Approve(approver1, windowStart));
    var cancelled = new ApprovalRequest(Guid.NewGuid(), calculationEmployee, owner, RequestKind.Mission, new[] { new[] { approver1 } });
    cancelled.Submit(owner, windowStart); cancelled.Cancel(owner, windowStart, "test cancellation");
    Throws<InvalidOperationException>(() => cancelled.Approve(approver1, windowStart));
});
Check("period blocks anomalies and missing days, binds review to run and preserves reopen history", () =>
{
    var period = new AttendancePeriod(Guid.NewGuid(), calculationEmployee, workDate, workDate.AddDays(1));
    var bad = Compute([entry], []); var good = Compute([entry, exit], []); var run = Guid.NewGuid();
    Throws<InvalidOperationException>(() => period.Review(approver1, windowStart, run, [bad]));
    Throws<InvalidOperationException>(() => period.Review(approver1, windowStart, run, []));
    period.Review(approver1, windowStart, run, [good]);
    Throws<InvalidOperationException>(() => period.Approve(approver2, windowStart, Guid.NewGuid()));
    period.Approve(approver2, windowStart, run);
    Throws<ArgumentException>(() => period.Reopen(approver1, windowStart, ""));
    period.Reopen(approver1, windowStart, "test reopen");
    Assert(period.State == PeriodState.Open && period.ReviewedRunId is null && period.History.Count == 3 &&
        period.History.Last().RunId == run, "Approval history lost");
});

Console.WriteLine($"{count - failures}/{count} checks passed.");
return failures == 0 ? 0 : 1;
