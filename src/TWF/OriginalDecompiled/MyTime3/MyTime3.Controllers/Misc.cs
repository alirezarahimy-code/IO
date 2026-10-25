using MyTime3.Content;
using System;
using System.Globalization;

namespace MyTime3.Controllers
{
	public class Misc
	{
		public static string DayOfWeekToString(DayOfWeek dow)
		{
			string text = "";
			switch (dow)
			{
			case DayOfWeek.Friday:
				return PersianString.Friday;
			case DayOfWeek.Monday:
				return PersianString.Monday;
			case DayOfWeek.Saturday:
				return PersianString.Saturday;
			case DayOfWeek.Sunday:
				return PersianString.Sunday;
			case DayOfWeek.Thursday:
				return PersianString.Thirsday;
			case DayOfWeek.Tuesday:
				return PersianString.Tuesday;
			case DayOfWeek.Wednesday:
				return PersianString.Wednesday;
			default:
				return "";
			}
		}

		public static string ToLongPersianDate(DateTime d)
		{
			PersianCalendar persianCalendar = new PersianCalendar();
			string text = "";
			int num = persianCalendar.GetMonth(d);
			switch (num)
			{
			case 1:
				text = PersianString.Farvardin;
				break;
			case 2:
				text = PersianString.Ordibehesht;
				break;
			case 3:
				text = PersianString.Khordad;
				break;
			case 4:
				text = PersianString.Tir;
				break;
			case 5:
				text = PersianString.Mordad;
				break;
			case 6:
				text = PersianString.Shahrivar;
				break;
			case 7:
				text = PersianString.Mehr;
				break;
			case 8:
				text = PersianString.Aban;
				break;
			case 9:
				text = PersianString.Azar;
				break;
			case 10:
				text = PersianString.Dey;
				break;
			case 11:
				text = PersianString.Bahman;
				break;
			case 12:
				text = PersianString.Esfand;
				break;
			}
			num = persianCalendar.GetDayOfMonth(d);
			string arg = num.ToString();
			string arg2 = text;
			num = persianCalendar.GetYear(d);
			return $"{arg} {arg2} {num.ToString()}";
		}

		public static string TimeSpanToString(TimeSpan t)
		{
			int num = (int)t.TotalHours;
			string arg = num.ToString();
			num = t.Minutes;
			return $"{arg}:{num.ToString()}";
		}

		public static TimeSpan StringToTimeSpan(string s)
		{
			TimeSpan timeSpan = default(TimeSpan);
			double result = 0.0;
			if (!double.TryParse(s, out result))
			{
				int result2 = 0;
				int result3 = 0;
				int result4 = 0;
				string[] array = s.Split(new char[1]
				{
					':'
				}, StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < array.Length; i++)
				{
					array[i] = array[i].Trim();
				}
				if (array.Length != 0)
				{
					int.TryParse(array[0], out result2);
				}
				if (array.Length > 1)
				{
					int.TryParse(array[1], out result3);
				}
				if (array.Length > 2)
				{
					int.TryParse(array[2], out result4);
				}
				return new TimeSpan(result2, result3, result4);
			}
			return TimeSpan.FromHours(result);
		}
	}
}
