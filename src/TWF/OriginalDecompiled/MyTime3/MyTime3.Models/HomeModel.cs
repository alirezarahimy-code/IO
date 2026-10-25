using System.Collections.Generic;

namespace MyTime3.Models
{
	public class HomeModel
	{
		public List<ADayReport> Reports
		{
			get;
			set;
		}

		public List<Request> Requests
		{
			get;
			set;
		}

		public Deputy Deputy
		{
			get;
			set;
		}

		public HomeModel()
		{
			Requests = new List<Request>();
			Reports = new List<ADayReport>();
			Deputy = new Deputy();
		}
	}
}
