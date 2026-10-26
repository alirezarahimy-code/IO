using System;

namespace Aria.Core.Compatibility
{
    /// <summary>
    /// A deliberately narrow display adapter for TWF 4.28 MyTime3.Controllers.Misc.
    /// The TWF rewrite's TimeDurationHelper zero-pads its output (08:05); the
    /// original binary emits 8:5. Report/display consumers requiring original
    /// formatting must opt in explicitly; no legacy parser, SQL, engine, license
    /// or route is activated here. In particular, do not use this to parse input.
    /// </summary>
    public static class OriginalMiscTime
    {
        public static string TimeSpanToString(TimeSpan value)
        {
            int totalHours = (int)value.TotalHours;
            return totalHours.ToString() + ":" + value.Minutes.ToString();
        }
    }
}
