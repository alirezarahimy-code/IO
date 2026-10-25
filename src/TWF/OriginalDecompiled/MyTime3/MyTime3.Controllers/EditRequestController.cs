using MyTime3.Authorization;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class EditRequestController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index()
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<MyTime3.Models.Request> list = new List<MyTime3.Models.Request>();
			List<T5V3.Model.Request>.Enumerator enumerator = t5DBInstance.RequestDA.GetAll(Convert.ToInt64(LoginManager.GetBadgeNumber(base.HttpContext)), true).GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					T5V3.Model.Request current = enumerator.Current;
					if (t5DBInstance.RequestAcceptDA.IsEditableRequest(current.RequestID))
					{
						MyTime3.Models.Request request = new MyTime3.Models.Request();
						request.RequestT5 = current;
						list.Add(request);
					}
				}
			}
			finally
			{
				((IDisposable)enumerator).Dispose();
			}
			if ((bool)SettingsController.LoadValue("RequestForUsers", typeof(bool), true))
			{
				foreach (PersonnelModel item in RequestController.AddPersonnelToViewBag(base.HttpContext))
				{
					enumerator = t5DBInstance.RequestDA.GetAll(item.BadgeNumber, true).GetEnumerator();
					try
					{
						while (enumerator.MoveNext())
						{
							T5V3.Model.Request current3 = enumerator.Current;
							if (t5DBInstance.RequestAcceptDA.IsEditableRequest(current3.RequestID))
							{
								MyTime3.Models.Request request2 = new MyTime3.Models.Request();
								request2.RequestT5 = current3;
								if (!(from r in list
								select r.RequestID).ToList().Contains(request2.RequestID))
								{
									list.Add(request2);
								}
							}
						}
					}
					finally
					{
						((IDisposable)enumerator).Dispose();
					}
				}
			}
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			foreach (MyTime3.Models.Request item2 in list)
			{
				item2.RequestT5.RequesterName = personnelSystem.DB.UserInfos.GetByID(item2.BadgeNumber).ToString();
			}
			return View(list);
		}

		[HttpPost]
		public JsonResult GetRequests(string userName, string password)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			List<MyTime3.Models.Request> list = new List<MyTime3.Models.Request>();
			long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(num, true))
				{
					if (t5DBInstance.RequestAcceptDA.IsEditableRequest(item.RequestID))
					{
						MyTime3.Models.Request request = new MyTime3.Models.Request();
						request.RequestT5 = item;
						list.Add(request);
					}
				}
			}
			return Json(list);
		}

		[HttpPost]
		public JsonResult CancelRequest(string userName, string password, long requestID)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			string message = "";
			long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				message = ((t5DBInstance.RequestDA.GetByID(requestID).BadgeNumber != num || !t5DBInstance.RequestAcceptDA.IsEditableRequest(requestID)) ? "-2" : ((!t5DBInstance.RequestDA.Delete(requestID, out string _)) ? "-1" : "1"));
			}
			return Json(new
			{
				Message = message
			});
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult EditByID(long requestID)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<long> list = new List<long>();
			if ((bool)SettingsController.LoadValue("RequestForUsers", typeof(bool), true))
			{
				list = (from r in RequestController.AddPersonnelToViewBag(base.HttpContext)
				select r.BadgeNumber).ToList();
			}
			if ((t5DBInstance.RequestDA.GetByID(requestID).BadgeNumber == GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext) || list.Contains(t5DBInstance.RequestDA.GetByID(requestID).BadgeNumber)) && t5DBInstance.RequestAcceptDA.IsEditableRequest(requestID))
			{
				T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(requestID);
				string text = CheckRequestTime(byID.ForDate);
				if (text == "")
				{
					GeneralRequest generalRequest = new GeneralRequest();
					generalRequest.IsNew = false;
					generalRequest.RequestID = byID.RequestID;
					generalRequest.LeaveTypeID = byID.LeaveID;
					generalRequest.ForDateHijri = DateConverter.ToHijriDate(byID.ForDate);
					GeneralRequest generalRequest2 = generalRequest;
					DateTime dateTime = byID.FromTime;
					generalRequest2.FromTime = dateTime.ToString("HH:mm");
					GeneralRequest generalRequest3 = generalRequest;
					dateTime = byID.ToTime;
					generalRequest3.ToTime = dateTime.ToString("HH:mm");
					GeneralRequest generalRequest4 = generalRequest;
					dateTime = byID.RecordTime;
					generalRequest4.ForTime = dateTime.ToString("HH:mm");
					generalRequest.FromDateHijri = DateConverter.ToHijriDate(byID.ForDate);
					generalRequest.ToDateHijri = DateConverter.ToHijriDate(byID.ToTime);
					generalRequest.Descriptions = byID.Descriptions;
					generalRequest.LeaveDescriptionsID = byID.LeaveDescriptionID;
					generalRequest.OvertimeValue = TimeSpanFuncs.TimeSpanToString(byID.AllowValue, "hhh\\:mm");
					if (byID.WorkflowType == WorkflowOperations.AllowOverTimes)
					{
						generalRequest.RequestType = 7;
						if (byID.OverTimeAllowType == AllowType.OverTimeBefore)
						{
							generalRequest.LeaveTypeID = -2;
						}
						if (byID.OverTimeAllowType == AllowType.OverTimeAfter)
						{
							generalRequest.LeaveTypeID = -1;
						}
						if (byID.OverTimeAllowType == AllowType.FreeOverTime)
						{
							generalRequest.LeaveTypeID = -3;
						}
					}
					if (byID.WorkflowType == WorkflowOperations.Leave)
					{
						if (byID.IsDaily)
						{
							generalRequest.RequestType = 0;
						}
						else
						{
							generalRequest.RequestType = 1;
						}
					}
					if (byID.WorkflowType == WorkflowOperations.Duty)
					{
						if (byID.IsDaily)
						{
							generalRequest.RequestType = 2;
						}
						else
						{
							generalRequest.RequestType = 3;
						}
					}
					if (byID.WorkflowType == WorkflowOperations.InsertRecord)
					{
						generalRequest.RequestType = 4;
					}
					if (byID.WorkflowType == WorkflowOperations.LateEarly)
					{
						if (byID.OverTimeAllowType == AllowType.Early)
						{
							generalRequest.RequestType = 5;
						}
						else if (byID.OverTimeAllowType == AllowType.Late)
						{
							generalRequest.RequestType = 6;
						}
					}
					return RedirectToAction("New", "Request", (object)generalRequest);
				}
				return View("Error", "", text);
			}
			return View("Error", "", PersianString.AccessDenied);
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Delete(long requestID)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<long> list = new List<long>();
			if ((bool)SettingsController.LoadValue("RequestForUsers", typeof(bool), true))
			{
				list = (from r in RequestController.AddPersonnelToViewBag(base.HttpContext)
				select r.BadgeNumber).ToList();
			}
			if ((t5DBInstance.RequestDA.GetByID(requestID).BadgeNumber == GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext) || list.Contains(t5DBInstance.RequestDA.GetByID(requestID).BadgeNumber)) && t5DBInstance.RequestAcceptDA.IsEditableRequest(requestID))
			{
				if (t5DBInstance.RequestDA.Delete(requestID, out string err))
				{
					return RedirectToAction("Index");
				}
				return View("Error", "", err);
			}
			return View("Error", "", PersianString.AccessDenied);
		}

		public static string CheckRequestTime(DateTime forDate)
		{
			string result = "";
			int num = (int)SettingsController.LoadValue("MaxRequestAgeDays", typeof(int), 0);
			int num2 = (int)SettingsController.LoadValue("LastDayForRequest", typeof(int), 0);
			DateTime dateTime;
			if (num != 0)
			{
				dateTime = forDate.Date;
				DateTime t = dateTime.AddDays((double)num);
				dateTime = DateTime.Now;
				if (t <= dateTime.Date)
				{
					result = string.Format(PersianString.MaxRequestTime, num.ToString());
				}
			}
			if (num2 != 0)
			{
				PersianCalendar persianCalendar = new PersianCalendar();
				if (persianCalendar.GetDayOfMonth(DateTime.Now) >= num2)
				{
					DateTime date = forDate.Date;
					dateTime = DateTime.Now;
					if (date < dateTime.Date)
					{
						PersianCalendar persianCalendar2 = persianCalendar;
						dateTime = DateTime.Now;
						if (persianCalendar2.GetMonth(dateTime.Date) <= persianCalendar.GetMonth(forDate.Date))
						{
							PersianCalendar persianCalendar3 = persianCalendar;
							dateTime = DateTime.Now;
							if (persianCalendar3.GetMonth(dateTime.Date) != 1 || persianCalendar.GetMonth(forDate.Date) != 12)
							{
								goto IL_0116;
							}
						}
						result = string.Format(PersianString.LastValidDayOfMonth, num2.ToString());
					}
				}
			}
			goto IL_0116;
			IL_0116:
			return result;
		}
	}
}
