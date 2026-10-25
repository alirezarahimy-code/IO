using Personnel.Model;
using System.Collections.Generic;

namespace MyTime3.Models
{
	public class Deputy
	{
		public bool IsAnyDeputy
		{
			get;
			set;
		}

		public UserInfo CurrentDeputy
		{
			get;
			set;
		}

		public List<UserInfo> ValidList
		{
			get;
			set;
		}

		public Deputy()
		{
			ValidList = new List<UserInfo>();
			IsAnyDeputy = false;
		}
	}
}
