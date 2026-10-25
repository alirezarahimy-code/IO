using System;
using System.Collections.Generic;

namespace MyTime3.Models
{
	public class ADayReport
	{
		public bool IsOdd
		{
			get
			{
				bool result = false;
				try
				{
					result = (Convert.ToInt32(HijriDate.Split(new char[1]
					{
						'/'
					}, StringSplitOptions.RemoveEmptyEntries)[2]) % 2 == 0);
					return result;
				}
				catch
				{
					return result;
				}
			}
		}

		public bool IsDailyLeave
		{
			get;
			set;
		}

		public string DailyLeaveName
		{
			get;
			set;
		}

		public string DayOfWeekName
		{
			get;
			set;
		}

		public string HijriDate
		{
			get;
			set;
		}

		public string MainInTime
		{
			get;
			set;
		}

		public bool WoIn
		{
			get;
			set;
		}

		public string SchInTime
		{
			get;
			set;
		}

		public string MainOutTime
		{
			get;
			set;
		}

		public string MainOutTimeDate
		{
			get;
			set;
		}

		public bool WoOut
		{
			get;
			set;
		}

		public string SchOutTime
		{
			get;
			set;
		}

		public string Late
		{
			get;
			set;
		}

		public bool HaveLate
		{
			get;
			set;
		}

		public string Early
		{
			get;
			set;
		}

		public bool HaveEarly
		{
			get;
			set;
		}

		public bool IsAbsent
		{
			get;
			set;
		}

		public bool WOShift
		{
			get;
			set;
		}

		public bool IsHoliday
		{
			get;
			set;
		}

		public List<InShiftExit> InShiftExits
		{
			get;
			set;
		}

		public bool IsWOInRequested
		{
			get;
			set;
		}

		public bool IsWOOutRequested
		{
			get;
			set;
		}

		public bool IsLateRequested
		{
			get;
			set;
		}

		public bool IsEarlyRequested
		{
			get;
			set;
		}

		public bool IsAbsentRequested
		{
			get;
			set;
		}

		public string LateRequestedDescriptions
		{
			get;
			set;
		}

		public string EarlyRequestedDescriptions
		{
			get;
			set;
		}

		public string AbsentRequestedDescriptions
		{
			get;
			set;
		}

		public ADayReport()
		{
			InShiftExits = new List<InShiftExit>();
		}
	}
}
