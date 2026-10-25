namespace MyTime3.Models
{
	public class DailyExitRequest
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

		public string ToDateHijri
		{
			get;
			set;
		}
	}
}
