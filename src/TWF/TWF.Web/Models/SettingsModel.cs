namespace MyTime3.Models
{
	public class SettingsModel
	{
		public bool AllowValidLate
		{
			get;
			set;
		}

		public int RequestRecordsMonthlyLimit
		{
			get;
			set;
		}

		public int RequestRecordsDailyLimit
		{
			get;
			set;
		}

		public int HoursOfDays
		{
			get;
			set;
		}

		public bool ViewHourlyDuty
		{
			get;
			set;
		}

		public bool ViewDailyDuty
		{
			get;
			set;
		}

		public bool ViewHourlyLeave
		{
			get;
			set;
		}

		public bool AllowValidHourlyFree
		{
			get;
			set;
		}

		public bool ViewDailyLeave
		{
			get;
			set;
		}

		public bool ViewOverTimeRequest
		{
			get;
			set;
		}

		public bool ViewRewardRequest
		{
			get;
			set;
		}

		public bool ViewConstOT
		{
			get;
			set;
		}

		public int LastDayForRequest
		{
			get;
			set;
		}

		public int MaxRequestAgeDays
		{
			get;
			set;
		}

		public bool RequestForUser
		{
			get;
			set;
		}

		public bool RequestRecord
		{
			get;
			set;
		}

		public bool ExportReport
		{
			get;
			set;
		}

		public bool ChangePassRestriction
		{
			get;
			set;
		}

		public bool ChangePassMonthly
		{
			get;
			set;
		}

		public bool ChangePassForce
		{
			get;
			set;
		}

		public int ChangePassLimit
		{
			get;
			set;
		}

		public string OverAllMessage
		{
			get;
			set;
		}
	}
}
