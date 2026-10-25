using System;
using System.Collections.Generic;
using T5V3.Model;

namespace MyTime3.Models
{
	public class PerosnnelStatusModel
	{
		public class Row
		{
			public long BadgeNumber
			{
				get;
				set;
			}

			public string Name
			{
				get;
				set;
			}

			public string State
			{
				get;
				set;
			}

			public List<Record> Records
			{
				get;
				set;
			}

			public Row()
			{
				Records = new List<Record>();
			}
		}

		public int RType
		{
			get;
			set;
		}

		public DateTime ForDate
		{
			get;
			set;
		}

		public List<Row> Rows
		{
			get;
			set;
		}

		public PerosnnelStatusModel()
		{
			RType = 1;
			ForDate = DateTime.Now;
			Rows = new List<Row>();
		}
	}
}
