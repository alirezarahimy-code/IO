using System;
using System.Globalization;

namespace AriaAdmin.Core.Compatibility
{
    /// <summary>
    /// Narrow display-only compatibility with the three static report helpers
    /// in THIS product's T5Web [3.5.7]/App/bin/T5V3Web.dll. No engine calculation,
    /// report authorization, route, SQL or other product is invoked by this file.
    /// Consumers must still prove full report, culture and permission parity.
    /// </summary>
    public static class OriginalReportTime
    {
        public static string Format(TimeSpan value, string format)
        {
            if (value == TimeSpan.Zero)
                return string.Empty;

            // The original's hhh\:mm special case does not pad the minute.
            if (string.Equals(format, @"hhh\:mm", StringComparison.Ordinal))
            {
                int hours = (int)value.TotalHours;
                return hours.ToString(CultureInfo.InvariantCulture) + ":" +
                    value.Minutes.ToString(CultureInfo.InvariantCulture);
            }

            return value.ToString(format);
        }
    }
}
