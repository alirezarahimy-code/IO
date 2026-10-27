using System.Text.Json;
using Aria.Core.Compatibility;

// Never synthesize an "original" expected result here. This executable needs
// a same-run Windows Framework x86 JSON produced directly by the pinned TWF 4.28
// T5V3.Attendance.dll harness. No SQL, network, IIS, production rows or secrets.
if (args.Length != 1 || !File.Exists(args[0]))
    throw new ArgumentException("Pass the same-run original attendance interval fixture.");
using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
var root = document.RootElement;
const string sha = "2a2abd09745ec0a80dff0343b0e1f97e1ed52b8806f2ec61e64da1aa8100d3b7";
if (root.GetProperty("source_sha256").GetString() != sha ||
    root.GetProperty("scope").GetString() != "SYNTHETIC_PURE_ORIGINAL_X86")
    throw new InvalidOperationException("Refusing a non-original/non-synthetic attendance fixture.");
var observed = root.GetProperty("cases");
if (observed.ValueKind != JsonValueKind.Object || observed.EnumerateObject().Count() != 8)
    throw new InvalidOperationException("The original must provide exactly eight fixed cases.");
int passed = 0, failed = 0;
void Run(string name, Action test)
{
    try { test(); Console.WriteLine("[PASS] " + name); passed++; }
    catch (Exception exception)
    {
        // The fixture has only synthetic dates and boolean values. Don't print
        // user-supplied file paths or real row values if an unexpected file is passed.
        Console.Error.WriteLine("[FAIL] " + name + ": " + exception.GetType().Name);
        failed++;
    }
}
void Compare(string name, DateTime a, DateTime b, DateTime c, DateTime d)
{
    Run("AR-ATT-" + (passed + failed + 1).ToString("D2") + ": pinned original " + name, () =>
    {
        bool expected = observed.GetProperty(name).GetBoolean();
        bool actual = OriginalAttendanceInterval.HaveIntersection(a, b, c, d);
        if (actual != expected) throw new InvalidOperationException("Interval golden mismatch.");
    });
}
DateTime day = new(2026, 10, 27, 0, 0, 0, DateTimeKind.Unspecified);
DateTime next = day.AddDays(1);
Compare("overlap", day.AddHours(9), day.AddHours(10), day.AddHours(9.5), day.AddHours(10.5));
Compare("disjoint", day.AddHours(9), day.AddHours(10), day.AddHours(11), day.AddHours(12));
Compare("touch", day.AddHours(9), day.AddHours(10), day.AddHours(10), day.AddHours(11));
Compare("same", day.AddHours(9), day.AddHours(10), day.AddHours(9), day.AddHours(10));
Compare("contained", day.AddHours(9), day.AddHours(12), day.AddHours(10), day.AddHours(11));
Compare("zero_inside", day.AddHours(9), day.AddHours(12), day.AddHours(10), day.AddHours(10));
Compare("zero_touch", day.AddHours(9), day.AddHours(10), day.AddHours(10), day.AddHours(10));
Compare("overnight", day.AddHours(23.5), next.AddHours(0.5),
    next.AddHours(0.25), next.AddHours(1));
Run("AR-ATT-09: inverted interval rejected (port-only safety, not original parity)", () =>
{
    try
    {
        OriginalAttendanceInterval.HaveIntersection(day.AddHours(10), day.AddHours(9),
            day.AddHours(9), day.AddHours(11));
        throw new InvalidOperationException("Inversion was accepted.");
    }
    catch (ArgumentOutOfRangeException) { }
});
Run("AR-ATT-10: mixed clock kinds rejected (port-only safety, not original parity)", () =>
{
    try
    {
        OriginalAttendanceInterval.HaveIntersection(day, day.AddHours(1),
            DateTime.SpecifyKind(day, DateTimeKind.Utc),
            DateTime.SpecifyKind(day.AddHours(1), DateTimeKind.Utc));
        throw new InvalidOperationException("Mixed clock kinds were accepted.");
    }
    catch (ArgumentException) { }
});
Run("AR-ATT-11: valid interval symmetry (port-only property, not original parity)", () =>
{
    bool left = OriginalAttendanceInterval.HaveIntersection(day, day.AddHours(10),
        day.AddHours(10), day.AddHours(11));
    bool right = OriginalAttendanceInterval.HaveIntersection(day.AddHours(10), day.AddHours(11),
        day, day.AddHours(10));
    if (left != right) throw new InvalidOperationException("Asymmetric closed intervals.");
});
Console.WriteLine($"Summary: {passed} Passed, {failed} Failed; original 8 bounded pure samples, port-only safety 3; no attendance workflow parity");
return failed == 0 && passed == 11 ? 0 : 1;
