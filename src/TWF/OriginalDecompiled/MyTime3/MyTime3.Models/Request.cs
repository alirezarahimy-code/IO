using MyTime3.Content;
using MyTime3.Controllers;
using System;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Models
{
	public class Request
	{
		public T5V3.Model.Request RequestT5
		{
			get;
			set;
		}

		public string Name => RequestT5.RequesterName;

		public long RequestID => RequestT5.RequestID;

		public long BadgeNumber => RequestT5.BadgeNumber;

		public string Descriptions => RequestT5.Descriptions;

		public string RequestTime => DateConverter.ToHijriDateWithTimeMins(RequestT5.CreateTime);

		public bool ForDeputy
		{
			get;
			set;
		}

		public string DeputyTitleName
		{
			get;
			set;
		}

		public string AcceptedTitles
		{
			get;
			set;
		}

		public string CanceledTitle
		{
			get;
			set;
		}

		public string WaitForTitle
		{
			get;
			set;
		}

		public bool IsCanceled
		{
			get;
			set;
		}

		public double Longitude
		{
			get
			{
				if (!RequestT5.Longitude.HasValue)
				{
					return 0.0;
				}
				return RequestT5.Longitude.Value;
			}
		}

		public double Latitude
		{
			get
			{
				if (!RequestT5.Latitude.HasValue)
				{
					return 0.0;
				}
				return RequestT5.Latitude.Value;
			}
		}

		public string ImageData
		{
			get
			{
				try
				{
					return Convert.ToBase64String(RequestT5.ImageData);
				}
				catch
				{
				}
				return "";
			}
		}

		public string WorkflowName
		{
			get
			{
				string result = "";
				if (RequestT5.WorkflowType == WorkflowOperations.Leave)
				{
					result = PersianString.Leave;
				}
				else if (RequestT5.WorkflowType == WorkflowOperations.Duty)
				{
					result = PersianString.Duty;
				}
				else if (RequestT5.WorkflowType == WorkflowOperations.InsertRecord)
				{
					result = PersianString.InsertRecord;
				}
				else if (RequestT5.WorkflowType == WorkflowOperations.LateEarly)
				{
					result = PersianString.LateEarly;
				}
				else if (RequestT5.WorkflowType == WorkflowOperations.EditOverTimes)
				{
					result = PersianString.EditOverTime;
				}
				else if (RequestT5.WorkflowType == WorkflowOperations.AllowOverTimes)
				{
					result = PersianString.OverTimeAccess;
				}
				else if (RequestT5.WorkflowType == WorkflowOperations.RewardAndPenalty)
				{
					result = PersianString.RewardAndPenalty;
				}
				return result;
			}
		}

		public string TypeName
		{
			get
			{
				string result = "";
				if (RequestT5.WorkflowType == WorkflowOperations.Leave || RequestT5.WorkflowType == WorkflowOperations.Duty)
				{
					result = DBInitializer.GetT5DBInstance().LeaveTypeDA.GetByID(RequestT5.LeaveID).LeaveName;
				}
				else if (RequestT5.WorkflowType == WorkflowOperations.EditOverTimes)
				{
					if (RequestT5.OverTimeAllowType == AllowType.OverTimeAfter)
					{
						result = PersianString.OverTimeAfter;
					}
					else if (RequestT5.OverTimeAllowType == AllowType.OverTimeBefore)
					{
						result = PersianString.OverTimeBefore;
					}
				}
				else if (RequestT5.WorkflowType == WorkflowOperations.LateEarly)
				{
					if (RequestT5.OverTimeAllowType == AllowType.Late)
					{
						result = PersianString.Late;
					}
					else if (RequestT5.OverTimeAllowType == AllowType.Early)
					{
						result = PersianString.Early;
					}
				}
				else if (RequestT5.WorkflowType == WorkflowOperations.AllowOverTimes)
				{
					if (RequestT5.OverTimeAllowType == AllowType.FreeOverTime)
					{
						result = PersianString.FreeOverTime;
					}
					else if (RequestT5.OverTimeAllowType == AllowType.OverTimeAfter)
					{
						result = PersianString.OverTimeAfter;
					}
					else if (RequestT5.OverTimeAllowType == AllowType.OverTimeBefore)
					{
						result = PersianString.OverTimeBefore;
					}
				}
				else if (RequestT5.WorkflowType == WorkflowOperations.RewardAndPenalty)
				{
					if (RequestT5.OverTimeAllowType == AllowType.Early)
					{
						result = PersianString.Penalty;
					}
					else if (RequestT5.OverTimeAllowType == AllowType.Late)
					{
						result = PersianString.Reward;
					}
				}
				return result;
			}
		}

		public string From
		{
			get
			{
				string text = "";
				if (RequestT5.WorkflowType != WorkflowOperations.Leave && RequestT5.WorkflowType != WorkflowOperations.Duty)
				{
					if (RequestT5.WorkflowType != WorkflowOperations.InsertRecord)
					{
						return DateConverter.ToHijriDate(RequestT5.ForDate);
					}
					return DateConverter.ToHijriDateWithTimeMins(RequestT5.RecordTime);
				}
				if (!RequestT5.IsDaily)
				{
					DateTime dateTime = RequestT5.ForDate;
					dateTime = dateTime.Date;
					return DateConverter.ToHijriDateWithTimeMins(dateTime.Add(RequestT5.FromTime.TimeOfDay));
				}
				return DateConverter.ToHijriDate(RequestT5.ForDate);
			}
		}

		public string To
		{
			get
			{
				string result = "";
				if (RequestT5.WorkflowType == WorkflowOperations.Leave || RequestT5.WorkflowType == WorkflowOperations.Duty)
				{
					if (RequestT5.IsDaily)
					{
						result = DateConverter.ToHijriDate(RequestT5.ToTime);
					}
					else
					{
						DateTime dateTime = RequestT5.ForDate;
						dateTime = dateTime.Date;
						result = DateConverter.ToHijriDateWithTimeMins(dateTime.Add(RequestT5.ToTime.TimeOfDay));
					}
				}
				if (RequestT5.WorkflowType == WorkflowOperations.AllowOverTimes)
				{
					result = RequestT5.AllowValue.ToString();
				}
				if (RequestT5.WorkflowType == WorkflowOperations.RewardAndPenalty)
				{
					result = TimeSpanFuncs.TimeSpanToString(RequestT5.AllowValue, "hhh\\:mm");
				}
				return result;
			}
		}
	}
}
