using System;
using System.Globalization;

namespace TWF.Domain.ValueObjects
{
    /// <summary>
    /// Helper for parsing and formatting 24-hour time values and multi-day cumulative durations (e.g. "142:30").
    /// Compatible with T5V3.Misc.TimeSpanToString / StringToTimeSpan while handling Persian digits and validation safely.
    /// </summary>
    public static class TimeDurationHelper
    {
        /// <summary>
        /// Formats a <see cref="TimeSpan"/> into total hours and minutes ("HH:mm" or "HHH:mm"),
        /// preserving negative signs if the duration is negative.
        /// </summary>
        public static string FormatTimeSpan(TimeSpan span)
        {
            bool isNegative = span.Ticks < 0;
            TimeSpan abs = span.Duration();
            long totalHours = (long)Math.Floor(abs.TotalHours);
            int minutes = abs.Minutes;
            string formatted = $"{totalHours:00}:{minutes:00}";
            return isNegative ? "-" + formatted : formatted;
        }

        /// <summary>
        /// Formats total minutes into "HH:mm" (or "HHH:mm" when > 99 hours).
        /// </summary>
        public static string MinutesToTimeString(int totalMinutes)
        {
            bool isNegative = totalMinutes < 0;
            int absMinutes = Math.Abs(totalMinutes);
            int hours = absMinutes / 60;
            int minutes = absMinutes % 60;
            string formatted = $"{hours:00}:{minutes:00}";
            return isNegative ? "-" + formatted : formatted;
        }

        /// <summary>
        /// Parses a time or cumulative duration string (e.g. "08:30", "145:15", "۰۸:۳۰") into total minutes.
        /// </summary>
        public static bool TryParseDurationMinutes(string input, out int totalMinutes, bool allowOver24Hours = true)
        {
            totalMinutes = 0;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string normalized = JalaliDateHelper.NormalizeDigits(input).Trim();
            bool isNegative = false;
            if (normalized.StartsWith("-", StringComparison.Ordinal))
            {
                isNegative = true;
                normalized = normalized.Substring(1).Trim();
            }

            string[] parts = normalized.Split(':');
            if (parts.Length < 2 || parts.Length > 3)
                return false;

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hours) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes))
            {
                return false;
            }

            if (hours < 0 || (!allowOver24Hours && hours > 23) || minutes < 0 || minutes > 59)
                return false;

            totalMinutes = (hours * 60) + minutes;
            if (isNegative)
                totalMinutes = -totalMinutes;

            return true;
        }

        /// <summary>
        /// Parses a time or cumulative duration string into a <see cref="TimeSpan"/>.
        /// </summary>
        public static bool TryParseTimeSpan(string input, out TimeSpan result, bool allowOver24Hours = true)
        {
            result = TimeSpan.Zero;
            if (!TryParseDurationMinutes(input, out int minutes, allowOver24Hours))
                return false;

            result = TimeSpan.FromMinutes(minutes);
            return true;
        }

        /// <summary>
        /// Parses a 24-hour clock time ("00:00" to "23:59") and combines it with the date portion of <paramref name="baseDate"/>.
        /// </summary>
        public static bool TryCombineDateAndTime(DateTime baseDate, string timeInput, out DateTime combined)
        {
            combined = baseDate.Date;
            if (!TryParseDurationMinutes(timeInput, out int totalMinutes, allowOver24Hours: false) || totalMinutes < 0)
                return false;

            combined = baseDate.Date.AddMinutes(totalMinutes);
            return true;
        }
    }
}
