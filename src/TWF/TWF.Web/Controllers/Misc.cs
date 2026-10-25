using System;
using System.Globalization;
using MyTime3.Content;

namespace MyTime3.Controllers
{
    public class Misc
    {
        public static string DayOfWeekToString(DayOfWeek dow)
        {
            switch (dow)
            {
                case DayOfWeek.Friday: return PersianString.Friday;
                case DayOfWeek.Monday: return PersianString.Monday;
                case DayOfWeek.Saturday: return PersianString.Saturday;
                case DayOfWeek.Sunday: return PersianString.Sunday;
                case DayOfWeek.Thursday: return PersianString.Thirsday;
                case DayOfWeek.Tuesday: return PersianString.Tuesday;
                case DayOfWeek.Wednesday: return PersianString.Wednesday;
                default: return string.Empty;
            }
        }

        public static string ToLongPersianDate(DateTime d)
        {
            var persianCalendar = new PersianCalendar();
            string monthName = string.Empty;
            int month = persianCalendar.GetMonth(d);
            switch (month)
            {
                case 1: monthName = PersianString.Farvardin; break;
                case 2: monthName = PersianString.Ordibehesht; break;
                case 3: monthName = PersianString.Khordad; break;
                case 4: monthName = PersianString.Tir; break;
                case 5: monthName = PersianString.Mordad; break;
                case 6: monthName = PersianString.Shahrivar; break;
                case 7: monthName = PersianString.Mehr; break;
                case 8: monthName = PersianString.Aban; break;
                case 9: monthName = PersianString.Azar; break;
                case 10: monthName = PersianString.Dey; break;
                case 11: monthName = PersianString.Bahman; break;
                case 12: monthName = PersianString.Esfand; break;
            }
            int day = persianCalendar.GetDayOfMonth(d);
            int year = persianCalendar.GetYear(d);
            return $"{day} {monthName} {year}";
        }

        public static string TimeSpanToString(TimeSpan t)
        {
            int totalHours = (int)t.TotalHours;
            int minutes = Math.Abs(t.Minutes);
            return $"{totalHours}:{minutes:00}";
        }

        public static TimeSpan StringToTimeSpan(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return default(TimeSpan);

            if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double hours) ||
                double.TryParse(s.Trim(), out hours))
            {
                return TimeSpan.FromHours(hours);
            }

            int h = 0, m = 0, sec = 0;
            string[] parts = s.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
            }
            if (parts.Length != 0)
                int.TryParse(parts[0], out h);
            if (parts.Length > 1)
                int.TryParse(parts[1], out m);
            if (parts.Length > 2)
                int.TryParse(parts[2], out sec);

            return new TimeSpan(h, m, sec);
        }
    }
}
