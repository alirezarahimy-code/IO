using IO.Domain.Attendance;
using IO.Domain.Organization;

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

Console.WriteLine($"{count - failures}/{count} checks passed.");
return failures == 0 ? 0 : 1;
