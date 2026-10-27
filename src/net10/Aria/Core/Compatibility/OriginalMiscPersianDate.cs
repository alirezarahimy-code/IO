using System;
using System.Globalization;

namespace Aria.Core.Compatibility
{
    /// <summary>
    /// Bounded pure display adapter for this product's original TWF 4.28
    /// Misc.DayOfWeekToString / Misc.ToLongPersianDate. Labels start with the
    /// Aria rewrite's Persian resource, and must match the SHA-pinned ORIGINAL
    /// binary on all seven days and 12 mid-month dates in the same Windows run.
    /// Never infer full calendar/culture or HTTP parity from those samples.
    /// Not wired to MVC, a report, SQL or an unverified user input path.
    /// </summary>
    public static class OriginalMiscPersianDate
    {
        // Index 0 is unused. The original helper indexes a PersianCalendar
        // month and returns day + resource label + year in the current culture.
        private static readonly string[] MonthNames =
        {
            "", "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
        };

        public static string DayOfWeekToString(DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Sunday: return "یکشنبه";
                case DayOfWeek.Monday: return "دوشنبه";
                case DayOfWeek.Tuesday: return "سه شنبه";
                case DayOfWeek.Wednesday: return "چهارشنبه";
                case DayOfWeek.Thursday: return "پنج شنبه";
                case DayOfWeek.Friday: return "جمعه";
                case DayOfWeek.Saturday: return "شنبه";
                default: return string.Empty;
            }
        }

        public static string ToLongPersianDate(DateTime date)
        {
            var calendar = new PersianCalendar();
            int month = calendar.GetMonth(date);
            return calendar.GetDayOfMonth(date).ToString(CultureInfo.CurrentCulture) +
                " " + MonthNames[month] + " " +
                calendar.GetYear(date).ToString(CultureInfo.CurrentCulture);
        }
    }
}
