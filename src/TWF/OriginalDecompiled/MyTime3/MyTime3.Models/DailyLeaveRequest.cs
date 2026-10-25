using System;

namespace MyTime3.Models
{
	public class DailyLeaveRequest
	{
		public DateTime ForDate
		{
			get;
			set;
		}

		public string Descriptions
		{
			get;
			set;
		}

		public string HijriDate
		{
			get;
			set;
		}

		public int LeaveTypeID
		{
			get;
			set;
		}
	}
}
