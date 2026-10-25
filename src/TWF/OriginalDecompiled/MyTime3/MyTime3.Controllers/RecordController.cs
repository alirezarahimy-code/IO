using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using System;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class RecordController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(string recordDate = null, string recordTime = null)
		{
			if (recordDate != null || recordTime != null)
			{
				RecordRequest recordRequest = new RecordRequest();
				recordRequest.ForDateStr = recordDate;
				recordRequest.RecordTimeStr = recordTime;
				return View(recordRequest);
			}
			return View();
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(RecordRequest request)
		{
			base.ViewBag.Message = RequestForRecord(request, GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext));
			return View("Add", request);
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Edit(long id)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(id);
			if (byID != null)
			{
				if (byID.BadgeNumber == GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext) && t5DBInstance.RequestAcceptDA.IsEditableRequest(id))
				{
					RecordRequest recordRequest = new RecordRequest();
					recordRequest.RequestID = byID.RequestID;
					recordRequest.RecordTimeStr = byID.RecordTime.ToString("HH:mm");
					recordRequest.Descriptions = byID.Descriptions;
					recordRequest.ForDateStr = DateConverter.ToHijriDate(byID.RecordTime);
					return View(recordRequest);
				}
				return View("Error", "", PersianString.AccessDenied);
			}
			return RedirectToAction("Add");
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Edit(RecordRequest request)
		{
			base.ViewBag.Message = EditRequestRecord(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
			return View("Edit", request);
		}

		public static string EditRequestRecord(long badgeNumber, RecordRequest request)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			string text = "";
			if (request.Descriptions == null)
			{
				request.Descriptions = "";
			}
			DateTime? nullable = DateConverter.ToChristian(request.ForDateStr);
			if (nullable.HasValue)
			{
				DateTime result = default(DateTime);
				DateTime.TryParse(request.RecordTimeStr, out result);
				DateTime d = result;
				DateTime d2 = default(DateTime);
				if (d != d2)
				{
					T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(request.RequestID);
					if (byID != null)
					{
						int num = (int)SettingsController.LoadValue("RequestRecordsDailyLimit", typeof(int), 0);
						int num2 = (int)SettingsController.LoadValue("RequestRecordsMonthlyLimit", typeof(int), 0);
						Rule activeRule = new RuleProcessor(t5DBInstance.RuleDA.GetAll(), t5DBInstance.DepartmentRuleDA.GetAll(), t5DBInstance.UserRuleDA.GetAll()).GetActiveRule(personnelSystem.DB.UserInfos.GetByID(byID.BadgeNumber), null, DateTime.Now);
						if (activeRule != null && activeRule.RuleID != 0)
						{
							num = activeRule.LimitRecReqInDay;
							num2 = activeRule.LimitRecReqInMonth;
						}
						d2 = byID.RecordTime;
						DateTime date = d2.Date;
						d2 = nullable.Value;
						if (!(date == d2.Date))
						{
							d2 = nullable.Value;
							DateTime date2 = d2.Date;
							d2 = nullable.Value;
							if (GetRecordRequestsCount(badgeNumber, date2, d2.AddHours(23.9)) >= num)
							{
								text = PersianString.RequestRecordsDailyLimit;
								goto IL_0256;
							}
						}
						d2 = byID.RecordTime;
						DateTime date3 = d2.Date;
						d2 = nullable.Value;
						if (date3 == d2.Date || GetRecordRequestsCount(badgeNumber, MonthStart(nullable.Value), MonthEnd(nullable.Value)) < num2)
						{
							T5V3.Model.Request request2 = byID;
							d2 = nullable.Value;
							d2 = d2.Date;
							request2.RecordTime = d2.Add(result.TimeOfDay);
							byID.Descriptions = request.Descriptions;
							if (t5DBInstance.RequestDA.Edit(byID, out string _))
							{
								text = PersianString.InsertSuccess;
								NotificationManager.NewRequestAdded(byID);
							}
							else
							{
								text = PersianString.ErrorInInsertingData;
							}
						}
						else
						{
							text = PersianString.RequestRecordsMonthlyLimit;
						}
					}
					else
					{
						text = PersianString.ErrorInInsertingData;
					}
				}
				else
				{
					text = PersianString.BadTimeFormat;
				}
			}
			else
			{
				text = PersianString.BadDateFormat;
			}
			goto IL_0256;
			IL_0256:
			return text;
		}

		private static int GetRecordRequestsCount(long badgeNumber, DateTime fromDate, DateTime toDate)
		{
			int num = 0;
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(badgeNumber, fromDate, toDate))
			{
				if (item.WorkflowType == WorkflowOperations.InsertRecord && (from re in t5DBInstance.RequestAcceptDA.GetByID(item.RequestID)
				where re.IsCancel
				select re).Count() == 0)
				{
					num++;
				}
			}
			return num;
		}

		private static DateTime MonthStart(DateTime d)
		{
			PersianCalendar persianCalendar = new PersianCalendar();
			return persianCalendar.ToDateTime(persianCalendar.GetYear(d), persianCalendar.GetMonth(d), 1, 0, 0, 0, 0);
		}

		private static DateTime MonthEnd(DateTime d)
		{
			PersianCalendar persianCalendar = new PersianCalendar();
			return persianCalendar.ToDateTime(persianCalendar.GetYear(d), persianCalendar.GetMonth(d), persianCalendar.GetDaysInMonth(persianCalendar.GetYear(d), persianCalendar.GetMonth(d)), 23, 59, 59, 0);
		}

		public static string RequestForRecord(RecordRequest request, long badgenumber)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			if (request.Descriptions == null)
			{
				request.Descriptions = "";
			}
			string text = "";
			DateTime? nullable = DateConverter.ToChristian(request.ForDateStr);
			if (nullable.HasValue)
			{
				DateTime result = default(DateTime);
				DateTime.TryParse(request.RecordTimeStr, out result);
				DateTime d = result;
				DateTime d2 = default(DateTime);
				if (d != d2)
				{
					d2 = nullable.Value;
					d2 = d2.Date;
					DateTime recTime = d2.Add(result.TimeOfDay);
					if (t5DBInstance.RequestDA.GetAll(badgenumber, true).Where(delegate(T5V3.Model.Request r)
					{
						if (r.WorkflowType == WorkflowOperations.InsertRecord)
						{
							return r.RecordTime == recTime;
						}
						return false;
					}).Count() == 0)
					{
						int num = (int)SettingsController.LoadValue("RequestRecordsDailyLimit", typeof(int), 0);
						int num2 = (int)SettingsController.LoadValue("RequestRecordsMonthlyLimit", typeof(int), 0);
						Rule activeRule = new RuleProcessor(t5DBInstance.RuleDA.GetAll(), t5DBInstance.DepartmentRuleDA.GetAll(), t5DBInstance.UserRuleDA.GetAll()).GetActiveRule(personnelSystem.DB.UserInfos.GetByID(badgenumber), null, DateTime.Now);
						if (activeRule != null && activeRule.RuleID != 0)
						{
							num = activeRule.LimitRecReqInDay;
							num2 = activeRule.LimitRecReqInMonth;
						}
						d2 = nullable.Value;
						DateTime date = d2.Date;
						d2 = nullable.Value;
						if (GetRecordRequestsCount(badgenumber, date, d2.AddHours(23.9)) < num)
						{
							if (GetRecordRequestsCount(badgenumber, MonthStart(nullable.Value), MonthEnd(nullable.Value)) < num2)
							{
								string text2 = EditRequestController.CheckRequestTime(nullable.Value);
								if (text2 == "")
								{
									T5V3.Model.Request request2 = new T5V3.Model.Request();
									request2.AllowValue = new TimeSpan(0L);
									request2.BadgeNumber = badgenumber;
									request2.CreateTime = DateTime.Now;
									request2.ForDate = nullable.Value;
									T5V3.Model.Request request3 = request2;
									d2 = DateTime.Now;
									request3.FromTime = d2.Date;
									request2.IsSuspend = true;
									request2.IsDaily = false;
									request2.LeaveID = -1;
									request2.LeaveDescriptionID = -1;
									request2.OverTimeAllowType = AllowType.Early;
									T5V3.Model.Request request4 = request2;
									d2 = nullable.Value;
									d2 = d2.Date;
									request4.RecordTime = d2.Add(result.TimeOfDay);
									T5V3.Model.Request request5 = request2;
									d2 = DateTime.Now;
									request5.ToTime = d2.Date;
									request2.WorkflowType = WorkflowOperations.InsertRecord;
									request2.Descriptions = request.Descriptions;
									if (t5DBInstance.RequestDA.AddNew(request2, true, out string _, out long newID))
									{
										text = PersianString.InsertSuccess;
										request2.RequestID = newID;
										NotificationManager.NewRequestAdded(request2);
									}
									else
									{
										text = PersianString.ErrorInInsertingData;
									}
								}
								else
								{
									text = text2;
								}
							}
							else
							{
								text = PersianString.RequestRecordsMonthlyLimit;
							}
						}
						else
						{
							text = PersianString.RequestRecordsDailyLimit;
						}
					}
					else
					{
						text = PersianString.RequestExists;
					}
				}
				else
				{
					text = PersianString.BadTimeFormat;
				}
			}
			else
			{
				text = PersianString.BadDateFormat;
			}
			return text;
		}
	}
}
