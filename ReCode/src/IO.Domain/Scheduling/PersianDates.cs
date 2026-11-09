using System.Globalization;

namespace IO.Domain.Scheduling;

/// <summary>Converts presentation dates; persisted business dates remain DateOnly.</summary>
public static class PersianDates
{
    public static DateOnly ToGregorian(int year, int month, int day)
    {
        var calendar = new PersianCalendar();
        return DateOnly.FromDateTime(calendar.ToDateTime(year, month, day, 0, 0, 0, 0));
    }

    public static (int Year, int Month, int Day) FromGregorian(DateOnly date)
    {
        var calendar = new PersianCalendar();
        var time = date.ToDateTime(TimeOnly.MinValue);
        return (calendar.GetYear(time), calendar.GetMonth(time), calendar.GetDayOfMonth(time));
    }
}
