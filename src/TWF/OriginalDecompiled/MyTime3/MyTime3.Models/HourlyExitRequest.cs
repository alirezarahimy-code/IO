namespace MyTime3.Models
{
	public class HourlyExitRequest
	{
		public long RequestID
		{
			get;
			set;
		}

		public int LeaveID
		{
			get;
			set;
		}

		public int LeaveDescriptionID
		{
			get;
			set;
		}

		public string Descriptions
		{
			get;
			set;
		}

		public string ForDateHijri
		{
			get;
			set;
		}

		public string FromTimeStr
		{
			get;
			set;
		}

		public string ToTimeStr
		{
			get;
			set;
		}
	}
}
