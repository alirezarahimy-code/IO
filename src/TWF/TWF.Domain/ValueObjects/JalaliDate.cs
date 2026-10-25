using System;
using System.Globalization;
using System.Text;

namespace TWF.Domain.ValueObjects
{
    /// <summary>
    /// Immutable value object representing a Solar Hijri (Jalali / Persian) date.
    /// Backed by <see cref="PersianCalendar"/> to eliminate all Gregorian DayOfYear=81
    /// leap-year bugs found in legacy MyTime3 (AcceptRequestController, DailyLeaveController, RemainLeaveController).
    /// </summary>
    public struct JalaliDate : IEquatable<JalaliDate>, IComparable<JalaliDate>
    {
        private static readonly PersianCalendar Calendar = new PersianCalendar();

        public int Year { get; }
        public int Month { get; }
        public int Day { get; }

        public JalaliDate(int year, int month, int day)
        {
            if (year < 1300 || year > 1500)
                throw new ArgumentOutOfRangeException(nameof(year), "سال شمسی باید بین ۱۳۰۰ تا ۱۵۰۰ باشد.");
            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), "ماه شمسی باید بین ۱ تا ۱۲ باشد.");

            int maxDays = Calendar.GetDaysInMonth(year, month);
            if (day < 1 || day > maxDays)
                throw new ArgumentOutOfRangeException(nameof(day), $"روز ماه {month} در سال {year} باید بین ۱ تا {maxDays} باشد.");

            Year = year;
            Month = month;
            Day = day;
        }

        public static JalaliDate FromDateTime(DateTime dateTime)
        {
            return new JalaliDate(
                Calendar.GetYear(dateTime),
                Calendar.GetMonth(dateTime),
                Calendar.GetDayOfMonth(dateTime));
        }

        public static bool TryParse(string input, out JalaliDate result)
        {
            if (JalaliDateHelper.TryParseJalali(input, out DateTime dt))
            {
                result = FromDateTime(dt);
                return true;
            }
            result = default(JalaliDate);
            return false;
        }

        public DateTime ToDateTime(int hour = 0, int minute = 0, int second = 0)
        {
            return Calendar.ToDateTime(Year, Month, Day, hour, minute, second, 0);
        }

        public bool IsLeapYear => Calendar.IsLeapYear(Year);

        public int DaysInMonth => Calendar.GetDaysInMonth(Year, Month);

        public int DaysInYear => Calendar.GetDaysInYear(Year);

        public string MonthName => JalaliDateHelper.GetMonthName(Month);

        public string Format(char separator = '/', bool usePersianDigits = false)
        {
            string formatted = $"{Year:0000}{separator}{Month:00}{separator}{Day:00}";
            return usePersianDigits ? JalaliDateHelper.ToPersianDigits(formatted) : formatted;
        }

        public override string ToString() => Format('/', false);

        public bool Equals(JalaliDate other) => Year == other.Year && Month == other.Month && Day == other.Day;

        public override bool Equals(object obj) => obj is JalaliDate other && Equals(other);

        public override int GetHashCode() => (Year * 10000) + (Month * 100) + Day;

        public int CompareTo(JalaliDate other)
        {
            int cmp = Year.CompareTo(other.Year);
            if (cmp != 0) return cmp;
            cmp = Month.CompareTo(other.Month);
            if (cmp != 0) return cmp;
            return Day.CompareTo(other.Day);
        }

        public static bool operator ==(JalaliDate left, JalaliDate right) => left.Equals(right);
        public static bool operator !=(JalaliDate left, JalaliDate right) => !left.Equals(right);
        public static bool operator <(JalaliDate left, JalaliDate right) => left.CompareTo(right) < 0;
        public static bool operator <=(JalaliDate left, JalaliDate right) => left.CompareTo(right) <= 0;
        public static bool operator >(JalaliDate left, JalaliDate right) => left.CompareTo(right) > 0;
        public static bool operator >=(JalaliDate left, JalaliDate right) => left.CompareTo(right) >= 0;
    }

    /// <summary>
    /// Thread-safe helper for Solar Hijri (Jalali) calendar conversions, leap-year calculations,
    /// and Persian/Arabic digit normalization.
    /// </summary>
    public static class JalaliDateHelper
    {
        private static readonly PersianCalendar Calendar = new PersianCalendar();

        private static readonly string[] PersianMonthNames =
        {
            "",
            "فروردین",
            "اردیبهشت",
            "خرداد",
            "تیر",
            "مرداد",
            "شهریور",
            "مهر",
            "آبان",
            "آذر",
            "دی",
            "بهمن",
            "اسفند"
        };

        /// <summary>
        /// Converts Persian (۰-۹) and Arabic (٠-٩) digits in a string to standard ASCII digits (0-9).
        /// </summary>
        public static string NormalizeDigits(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var sb = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c >= '\u06F0' && c <= '\u06F9') // Persian digits ۰..۹
                {
                    sb.Append((char)('0' + (c - '\u06F0')));
                }
                else if (c >= '\u0660' && c <= '\u0669') // Arabic digits ٠..٩
                {
                    sb.Append((char)('0' + (c - '\u0660')));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Converts ASCII digits (0-9) to Persian digits (۰-۹) for offline RTL tabular display.
        /// </summary>
        public static string ToPersianDigits(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var sb = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c >= '0' && c <= '9')
                {
                    sb.Append((char)('\u06F0' + (c - '0')));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public static bool IsLeapYear(int persianYear) => Calendar.IsLeapYear(persianYear);

        public static int GetDaysInMonth(int persianYear, int persianMonth) => Calendar.GetDaysInMonth(persianYear, persianMonth);

        public static int GetDaysInYear(int persianYear) => Calendar.GetDaysInYear(persianYear);

        public static int GetPersianYear(DateTime date) => Calendar.GetYear(date);

        public static int GetPersianMonth(DateTime date) => Calendar.GetMonth(date);

        public static int GetPersianDayOfMonth(DateTime date) => Calendar.GetDayOfMonth(date);

        /// <summary>
        /// Returns the exact Gregorian DateTime for 1 Farvardin (00:00:00) of the Persian year containing <paramref name="date"/>.
        /// Fixes BUG-06 (where legacy MyTime3 used DateTime.Parse("2020-03-21").DayOfYear == 81 and broke in non-leap Gregorian years).
        /// </summary>
        public static DateTime GetPersianYearStart(DateTime date)
        {
            int persianYear = Calendar.GetYear(date);
            return Calendar.ToDateTime(persianYear, 1, 1, 0, 0, 0, 0);
        }

        /// <summary>
        /// Returns the exact Gregorian DateTime for 1 Farvardin (00:00:00) of <paramref name="persianYear"/>.
        /// </summary>
        public static DateTime GetPersianYearStart(int persianYear)
        {
            return Calendar.ToDateTime(persianYear, 1, 1, 0, 0, 0, 0);
        }

        /// <summary>
        /// Returns the exact Gregorian DateTime for the last day of Esfand (29 or 30 Esfand at 23:59:59)
        /// of the Persian year containing <paramref name="date"/>.
        /// </summary>
        public static DateTime GetPersianYearEnd(DateTime date)
        {
            int persianYear = Calendar.GetYear(date);
            return GetPersianYearEnd(persianYear);
        }

        /// <summary>
        /// Returns the exact Gregorian DateTime for the last day of Esfand (29 or 30 Esfand at 23:59:59)
        /// of <paramref name="persianYear"/>.
        /// </summary>
        public static DateTime GetPersianYearEnd(int persianYear)
        {
            int lastDayOfEsfand = Calendar.GetDaysInMonth(persianYear, 12);
            return Calendar.ToDateTime(persianYear, 12, lastDayOfEsfand, 23, 59, 59, 0);
        }

        /// <summary>
        /// Returns the exact Gregorian DateTime for the first day of the Persian month containing <paramref name="date"/>.
        /// </summary>
        public static DateTime GetPersianMonthStart(DateTime date)
        {
            int year = Calendar.GetYear(date);
            int month = Calendar.GetMonth(date);
            return Calendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
        }

        /// <summary>
        /// Returns the exact Gregorian DateTime for the last day of the Persian month containing <paramref name="date"/>.
        /// </summary>
        public static DateTime GetPersianMonthEnd(DateTime date)
        {
            int year = Calendar.GetYear(date);
            int month = Calendar.GetMonth(date);
            int daysInMonth = Calendar.GetDaysInMonth(year, month);
            return Calendar.ToDateTime(year, month, daysInMonth, 23, 59, 59, 0);
        }

        /// <summary>
        /// Accurately checks whether two dates fall within the same Solar Hijri year.
        /// Fixes BUG-06 in DailyLeaveController and AcceptRequestController.
        /// </summary>
        public static bool IsSamePersianYear(DateTime first, DateTime second)
        {
            return Calendar.GetYear(first) == Calendar.GetYear(second);
        }

        /// <summary>
        /// Formats a <see cref="DateTime"/> as a Jalali date string (e.g. "1405/08/02").
        /// Equivalent to T5V3.Misc.DateToPDateString but thread-safe and predictable.
        /// </summary>
        public static string ToJalaliString(DateTime date, char separator = '/')
        {
            if (date <= DateTime.MinValue.AddDays(1))
                return string.Empty;

            int y = Calendar.GetYear(date);
            int m = Calendar.GetMonth(date);
            int d = Calendar.GetDayOfMonth(date);
            return $"{y:0000}{separator}{m:00}{separator}{d:00}";
        }

        /// <summary>
        /// Formats a <see cref="DateTime"/> as a Jalali date and 24-hour time string (e.g. "1405/08/02 14:30").
        /// </summary>
        public static string ToJalaliDateTimeString(DateTime date, bool includeSeconds = false)
        {
            if (date <= DateTime.MinValue.AddDays(1))
                return string.Empty;

            string datePart = ToJalaliString(date, '/');
            string timePart = includeSeconds
                ? date.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : date.ToString("HH:mm", CultureInfo.InvariantCulture);
            return $"{datePart} {timePart}";
        }

        /// <summary>
        /// Parses a Jalali date string (e.g. "1405/08/02", "۱۴۰۵-۰۸-۰۲", or "1405/08/02 08:30") into a Gregorian <see cref="DateTime"/>.
        /// </summary>
        public static bool TryParseJalali(string jalaliInput, out DateTime result)
        {
            result = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(jalaliInput))
                return false;

            string normalized = NormalizeDigits(jalaliInput).Trim();
            string datePart = normalized;
            string timePart = null;

            int spaceIdx = normalized.IndexOf(' ');
            if (spaceIdx > 0)
            {
                datePart = normalized.Substring(0, spaceIdx).Trim();
                timePart = normalized.Substring(spaceIdx + 1).Trim();
            }

            string[] parts = datePart.Split(new[] { '/', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                return false;

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int year) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int month) ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int day))
            {
                return false;
            }

            if (year < 100)
            {
                year += 1300;
            }

            if (year < 1300 || year > 1500 || month < 1 || month > 12)
                return false;

            int maxDays = Calendar.GetDaysInMonth(year, month);
            if (day < 1 || day > maxDays)
                return false;

            int hour = 0, minute = 0, second = 0;
            if (!string.IsNullOrEmpty(timePart))
            {
                string[] tParts = timePart.Split(':');
                if (tParts.Length < 2 || tParts.Length > 3)
                    return false;
                if (!int.TryParse(tParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hour) ||
                    !int.TryParse(tParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute))
                    return false;
                if (tParts.Length == 3 && !int.TryParse(tParts[2], NumberStyles.None, CultureInfo.InvariantCulture, out second))
                    return false;
                if (hour < 0 || hour > 23 || minute < 0 || minute > 59 || second < 0 || second > 59)
                    return false;
            }

            result = Calendar.ToDateTime(year, month, day, hour, minute, second, 0);
            return true;
        }

        /// <summary>
        /// Parses a Jalali date string or throws a descriptive <see cref="FormatException"/>.
        /// </summary>
        public static DateTime ParseJalali(string jalaliInput)
        {
            if (!TryParseJalali(jalaliInput, out DateTime dt))
                throw new FormatException($"تاریخ شمسی وارد شده معتبر نمی‌باشد: '{jalaliInput}'");
            return dt;
        }

        public static string GetMonthName(int month)
        {
            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month));
            return PersianMonthNames[month];
        }

        public static string GetDayOfWeekPersianName(DayOfWeek dayOfWeek)
        {
            switch (dayOfWeek)
            {
                case DayOfWeek.Saturday: return "شنبه";
                case DayOfWeek.Sunday: return "یکشنبه";
                case DayOfWeek.Monday: return "دوشنبه";
                case DayOfWeek.Tuesday: return "سه‌شنبه";
                case DayOfWeek.Wednesday: return "چهارشنبه";
                case DayOfWeek.Thursday: return "پنجشنبه";
                case DayOfWeek.Friday: return "جمعه";
                default: return string.Empty;
            }
        }
    }
}
