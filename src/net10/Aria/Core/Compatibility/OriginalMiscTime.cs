using System;
using System.Globalization;

namespace Aria.Core.Compatibility
{
    /// <summary>
    /// Isolated pure time helpers for original TWF 4.28 Misc, never an HTTP
    /// authorization, overtime-write or calculated-report implementation.
    /// Only explicitly golden-tested inputs may be called equivalent to the
    /// original. Neither helper is wired to a production route yet.
    /// </summary>
    public static class OriginalMiscTime
    {
        public static string TimeSpanToString(TimeSpan value)
        {
            int totalHours = (int)value.TotalHours;
            return totalHours.ToString() + ":" + value.Minutes.ToString();
        }

        /// <summary>
        /// A safe subset of the original StringToTimeSpan parser for nonnegative
        /// durations. The original turns malformed fields into zero and accepts
        /// culture-dependent decimals; neither is safe on an untrusted POST.
        /// Reject instead of silently changing an overtime amount. Accept only
        /// ASCII digits, colon-separated hh:mm[:ss], or invariant decimal hours,
        /// capped at 9999 hours. Cumulative hours over 24 remain supported.
        /// </summary>
        public static bool TryParseSafeDuration(string text, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text) || text.Length > 32)
                return false;
            string input = text.Trim();
            if (input.IndexOf(':') >= 0)
            {
                string[] parts = input.Split(':');
                if (parts.Length != 2 && parts.Length != 3)
                    return false;
                int hours;
                int minutes;
                int seconds = 0;
                if (!TryAsciiInt(parts[0].Trim(), out hours) || hours > 9999 ||
                    !TryAsciiInt(parts[1].Trim(), out minutes) || minutes > 59 ||
                    (parts.Length == 3 &&
                     (!TryAsciiInt(parts[2].Trim(), out seconds) || seconds > 59)))
                    return false;
                value = new TimeSpan(hours, minutes, seconds);
                return true;
            }

            // Reject signs, thousands separators, exponents, and multiple dots;
            // CurrentCulture must never change the meaning of an hour amount.
            string[] decimalParts = input.Split('.');
            if (decimalParts.Length > 2 ||
                !AllAsciiDigits(decimalParts[0]) ||
                (decimalParts.Length == 2 && !AllAsciiDigits(decimalParts[1])))
                return false;
            double hoursValue;
            if (!double.TryParse(input, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out hoursValue) ||
                double.IsNaN(hoursValue) || double.IsInfinity(hoursValue) ||
                hoursValue > 9999)
                return false;
            value = TimeSpan.FromHours(hoursValue);
            return true;
        }

        private static bool TryAsciiInt(string text, out int value)
        {
            value = 0;
            return AllAsciiDigits(text) &&
                int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private static bool AllAsciiDigits(string text)
        {
            if (text.Length == 0) return false;
            foreach (char digit in text)
                if (digit < '0' || digit > '9') return false;
            return true;
        }
    }
}
