using System;

namespace Aria.Core.Compatibility
{
    /// <summary>
    /// Bounded pure-time translation of TWF 4.28-packaged
    /// T5V3.Attendance.TimeInterval.HaveIntersection, NOT the native attendance
    /// engine or a decision on a real leave/duty request. The original method
    /// must be re-executed against the same synthetic fixtures in the Windows
    /// x86 golden job; no T5Web/T5V3 desktop dependency is loaded here.
    /// </summary>
    public static class OriginalAttendanceInterval
    {
        /// <summary>
        /// Closed interval intersection, including shared endpoints and
        /// zero-length intervals (as observed only on the golden fixtures).
        /// Invalid or mixed-clock inputs are rejected instead of granting or
        /// denying a real request on an unproven interval.
        /// </summary>
        public static bool HaveIntersection(DateTime firstStart, DateTime firstEnd,
            DateTime secondStart, DateTime secondEnd)
        {
            if (firstEnd < firstStart || secondEnd < secondStart)
                throw new ArgumentOutOfRangeException("interval", "Start must not exceed end.");
            if (firstStart.Kind != firstEnd.Kind || firstStart.Kind != secondStart.Kind ||
                firstStart.Kind != secondEnd.Kind)
                throw new ArgumentException("All interval endpoints must use the same clock kind.",
                    "interval");
            return firstStart <= secondEnd && secondStart <= firstEnd;
        }

        /// <summary>
        /// Closed point containment for the seven original-pinned bounded
        /// samples, including start/end and a zero-length point. This does not
        /// normalize overnight or calendar dates and is NOT a leave decision.
        /// </summary>
        public static bool IsIn(DateTime start, DateTime end, DateTime moment)
        {
            if (end < start)
                throw new ArgumentOutOfRangeException("interval", "Start must not exceed end.");
            if (start.Kind != end.Kind || start.Kind != moment.Kind)
                throw new ArgumentException("All timestamps must use the same clock kind.",
                    "interval");
            return moment >= start && moment <= end;
        }
    }
}
