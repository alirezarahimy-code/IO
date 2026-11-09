namespace IO.Domain.Scheduling;

/// <summary>
/// A fixed local wall-clock shift. End-day offset is explicit; it is never
/// inferred by comparing clock times. No attendance or payroll formula is implied.
/// </summary>
public sealed class FixedShift
{
    public TimeOnly StartsAt { get; }
    public TimeOnly EndsAt { get; }
    public int EndDayOffset { get; }

    public FixedShift(TimeOnly startsAt, TimeOnly endsAt, int endDayOffset)
    {
        if (endDayOffset is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(endDayOffset));
        var length = endsAt.ToTimeSpan() - startsAt.ToTimeSpan() + TimeSpan.FromDays(endDayOffset);
        if (length <= TimeSpan.Zero || length > TimeSpan.FromDays(1))
            throw new ArgumentException("A fixed shift must be longer than zero and at most 24 wall-clock hours.");
        StartsAt = startsAt;
        EndsAt = endsAt;
        EndDayOffset = endDayOffset;
    }

    public (DateTimeOffset StartUtc, DateTimeOffset EndUtc) Resolve(DateOnly workDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var start = workDate.ToDateTime(StartsAt, DateTimeKind.Unspecified);
        var end = workDate.AddDays(EndDayOffset).ToDateTime(EndsAt, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(start) || timeZone.IsInvalidTime(end) ||
            timeZone.IsAmbiguousTime(start) || timeZone.IsAmbiguousTime(end))
            throw new InvalidOperationException("A shift boundary is ambiguous or invalid in the selected time zone.");
        var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, timeZone), TimeSpan.Zero);
        var endUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(end, timeZone), TimeSpan.Zero);
        if (endUtc <= startUtc) throw new InvalidOperationException("The resolved shift must have positive elapsed time.");
        return (startUtc, endUtc);
    }
}
