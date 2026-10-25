using MyTime3.Content;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using System.Xml.Serialization;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class NotifyController : Controller
	{
		[HttpGet]
		public string Index(string userName, string password)
		{
			string result = "";
			if (userName != null && password != null)
			{
				PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password);
				if (num > 0)
				{
					List<long> list = new List<long>();
					list.Add(num);
					list.AddRange(t5DBInstance.PersonnelDeputyDA.IsDeputy(num));
					List<int> titleIDs = new List<int>();
					foreach (long item in list)
					{
						titleIDs.Add(personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item));
					}
					List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly());
					List<Notification> list2 = new List<Notification>();
					foreach (long item2 in from r in requestAccepters
					where titleIDs.Contains(r.Value)
					select r.Key into r
					orderby r descending
					select r)
					{
						Request byID = t5DBInstance.RequestDA.GetByID(item2);
						Notification notification = new Notification();
						notification.IsView = false;
						Personnel.Model.UserInfo byID2 = personnelSystem.DB.UserInfos.GetByID(byID.BadgeNumber);
						if (byID2 != null)
						{
							byID.RequesterName = byID2.ToString();
						}
						notification.Descriptions = $"{PersianString.BadgeNumber}:{byID.BadgeNumber.ToString()}  {PersianString.Name}:{byID.RequesterName}\r\n{PersianString.RequestTime}:{DateConverter.ToHijriDateWithTimeMins(byID.CreateTime)}\r\n{PersianString.ForDate}:{DateConverter.ToHijriDate(byID.ForDate)}";
						string str = "";
						string str2 = "";
						DateTime dateTime;
						if (byID.WorkflowType == WorkflowOperations.AllowOverTimes)
						{
							notification.Title = PersianString.RequestOverTime;
						}
						else if (byID.WorkflowType == WorkflowOperations.Duty)
						{
							LeaveType byID3 = t5DBInstance.LeaveTypeDA.GetByID(byID.LeaveID);
							if (byID3 != null)
							{
								str2 = $"\r\n{PersianString.Type}:{byID3.ToString()}";
							}
							if (byID.IsDaily)
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
								dateTime = byID.FromTime;
								obj[1] = dateTime.ToString("HH:mm");
								obj[2] = PersianString.To;
								dateTime = byID.ToTime;
								obj[3] = dateTime.ToString("HH:mm");
								str = string.Format("\r\n{0}:{1}\r\n{2}:{3}", obj);
							}
						}
						else if (byID.WorkflowType == WorkflowOperations.Leave)
						{
							LeaveType byID4 = t5DBInstance.LeaveTypeDA.GetByID(byID.LeaveID);
							if (byID4 != null)
							{
								str2 = $"\r\n{PersianString.Type}:{byID4.ToString()}";
							}
							if (byID.IsDaily)
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
								dateTime = byID.FromTime;
								obj2[1] = dateTime.ToString("HH:mm");
								obj2[2] = PersianString.To;
								dateTime = byID.ToTime;
								obj2[3] = dateTime.ToString("HH:mm");
								str = string.Format("\r\n{0}:{1}\r\n{2}:{3}", obj2);
							}
						}
						else if (byID.WorkflowType == WorkflowOperations.InsertRecord)
						{
							notification.Title = PersianString.RequestRecord;
						}
						else if (byID.WorkflowType == WorkflowOperations.LateEarly)
						{
							if (byID.OverTimeAllowType == AllowType.Late)
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
						notification.ForBadgeNumber = num;
						list2.Add(notification);
					}
					XmlSerializer xmlSerializer = new XmlSerializer(typeof(List<Notification>));
					MemoryStream memoryStream = new MemoryStream();
					xmlSerializer.Serialize(memoryStream, list2);
					memoryStream.Position = 0L;
					result = new StreamReader(memoryStream).ReadToEnd();
				}
			}
			return result;
		}

		[HttpGet]
		public void SetViewed(string userName, string password, int notifyID)
		{
			if (userName != null && password != null)
			{
				PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password);
				if (num > 0)
				{
					List<long> list = new List<long>();
					list.Add(num);
					list.AddRange(t5DBInstance.PersonnelDeputyDA.IsDeputy(num));
				}
			}
		}
	}
}
