using MyTime3.Content;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class NotificationManager
	{
		public static void NewRequestAdded(Request request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			IEnumerable<KeyValuePair<long, int>> source = from r in t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly())
			where r.Key == request.RequestID
			select r;
			if (source.Count() > 0)
			{
				int titlesID = source.First().Value;
				foreach (long item in from u in personnelSystem.DB.UserInfos.GetAllUserInfoTitles()
				where u.Value == titlesID
				select u.Key)
				{
					Notification notification = new Notification();
					notification.IsView = false;
					Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(request.BadgeNumber);
					if (byID != null)
					{
						request.RequesterName = byID.ToString();
					}
					notification.Descriptions = $"{PersianString.BadgeNumber}:{request.BadgeNumber.ToString()}\r\n{PersianString.Name}:{request.RequesterName}\r\n{PersianString.RequestTime}:{DateConverter.ToHijriDateWithTimeMins(request.CreateTime)}\r\n{PersianString.ForDate}:{DateConverter.ToHijriDate(request.ForDate)}";
					string str = "";
					string str2 = "";
					DateTime dateTime;
					if (request.WorkflowType == WorkflowOperations.AllowOverTimes)
					{
						notification.Title = PersianString.RequestOverTime;
					}
					else if (request.WorkflowType == WorkflowOperations.Duty)
					{
						LeaveType byID2 = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
						if (byID2 != null)
						{
							str2 = $"\r\n{PersianString.Type}:{byID2.ToString()}";
						}
						if (request.IsDaily)
						{
							notification.Title = PersianString.RequestDailyDuty;
						}
						else
						{
							notification.Title = PersianString.RequestHourlyDuty;
							object[] obj = new object[4]
							{
								PersianString.From,
								null,
								null,
								null
							};
							dateTime = request.FromTime;
							obj[1] = dateTime.ToString("HH:mm");
							obj[2] = PersianString.To;
							dateTime = request.ToTime;
							obj[3] = dateTime.ToString("HH:mm");
							str = string.Format("\r\n{0}:{1}\r\n{2}:{3}", obj);
						}
					}
					else if (request.WorkflowType == WorkflowOperations.Leave)
					{
						LeaveType byID3 = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
						if (byID3 != null)
						{
							str2 = $"\r\n{PersianString.Type}:{byID3.ToString()}";
						}
						if (request.IsDaily)
						{
							notification.Title = PersianString.RequestDailyLeave;
						}
						else
						{
							notification.Title = PersianString.RequestHourlyLeave;
							object[] obj2 = new object[4]
							{
								PersianString.From,
								null,
								null,
								null
							};
							dateTime = request.FromTime;
							obj2[1] = dateTime.ToString("HH:mm");
							obj2[2] = PersianString.To;
							dateTime = request.ToTime;
							obj2[3] = dateTime.ToString("HH:mm");
							str = string.Format("\r\n{0}:{1}\r\n{2}:{3}", obj2);
						}
					}
					else if (request.WorkflowType == WorkflowOperations.InsertRecord)
					{
						notification.Title = PersianString.RequestRecord;
					}
					else if (request.WorkflowType == WorkflowOperations.LateEarly)
					{
						if (request.OverTimeAllowType == AllowType.Late)
						{
							notification.Title = PersianString.RequestLate;
						}
						else
						{
							notification.Title = PersianString.RequestEarly;
						}
					}
					Notification notification2 = notification;
					notification2.Descriptions += str2;
					Notification notification3 = notification;
					notification3.Descriptions += str;
					notification.ForBadgeNumber = item;
				}
			}
		}
	}
}
