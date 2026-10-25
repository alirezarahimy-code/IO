using System;
using T5V3.Misc;

namespace MyTime3.Models
{
	public class RemainLeave
	{
		public int LeaveTypeID
		{
			get;
			set;
		}

		public string LeaveTypeName
		{
			get;
			set;
		}

		public double MaxMinus
		{
			get;
			set;
		}

		public long BadgeNumber
		{
			get;
			set;
		}

		public string Name
		{
			get;
			set;
		}

		public DateTime ToDate
		{
			get;
			set;
		}

		public double YearRemain
		{
			get;
			set;
		}

		public double DailyTaken
		{
			get;
			set;
		}

		public TimeSpan HourlyTaken
		{
			get;
			set;
		}

		public double TotalTaken
		{
			get;
			set;
		}

		public double Remain
		{
			get;
			set;
		}

		public double Requested
		{
			get;
			set;
		}

		public string HourlyTakenStr
		{
			get
			{
				string text = TimeSpanFuncs.TimeSpanToString(HourlyTaken, "hhh\\:mm");
				if (text == "")
				{
					text = "0:0";
				}
				return text;
			}
		}
	}
}
