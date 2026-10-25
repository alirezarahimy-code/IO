using System.Collections.Generic;

namespace MyTime3.Models
{
	public class ReportModel
	{
		public string FromDateHijri
		{
			get;
			set;
		}

		public string ToDateHijri
		{
			get;
			set;
		}

		public bool IsSelf
		{
			get;
			set;
		}

		public List<string> SelectedBadgeNumbers
		{
			get;
			set;
		}

		public int ReportID
		{
			get;
			set;
		}

		public string ErrorMessage
		{
			get;
			set;
		}

		public int Page
		{
			get;
			set;
		}
	}
}
