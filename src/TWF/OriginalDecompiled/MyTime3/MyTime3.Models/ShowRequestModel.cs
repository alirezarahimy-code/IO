using System.Collections.Generic;

namespace MyTime3.Models
{
	public class ShowRequestModel
	{
		public string fromDate
		{
			get;
			set;
		}

		public string toDate
		{
			get;
			set;
		}

		public List<Request> Requests
		{
			get;
			set;
		}

		public ShowRequestModel()
		{
			Requests = new List<Request>();
		}
	}
}
