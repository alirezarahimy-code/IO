using System.Collections.Generic;
using T5V3.Model;

namespace MyTime3.Models
{
	public class LocationReportModel : ReportModel
	{
		public List<UserPosition> data
		{
			get;
			set;
		}
	}
}
