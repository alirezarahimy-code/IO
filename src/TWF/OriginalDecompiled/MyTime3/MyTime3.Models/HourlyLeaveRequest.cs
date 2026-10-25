using System;

namespace MyTime3.Models
{
	public class HourlyLeaveRequest
	{
		public DateTime ForDate
		{
			get;
			set;
		}

		public DateTime FromTime
		{
			get;
			set;
		}

		public DateTime ToTime
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
