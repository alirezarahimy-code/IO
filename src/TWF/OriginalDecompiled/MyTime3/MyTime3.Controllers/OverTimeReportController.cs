using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class OverTimeReportController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index(ReportModel model)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			List<PersonnelModel> list = new List<PersonnelModel>();
			new List<SumConstOT>();
			foreach (long overTimeSubUser in ConstOTController.GetOverTimeSubUsers(ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext)))
			{
				Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(overTimeSubUser);
				list.Add(new PersonnelModel(overTimeSubUser, byID.FirstName, byID.LastName));
			}
			base.ViewBag.Personnel = list;
			return View(model);
		}

		public static bool HasAnyUsers(HttpContextBase httpContext)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(httpContext);
			return ConstOTController.GetOverTimeSubUsers(ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, httpContext)).Count > 0;
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Show(string[] selectedBadgeNumbers, string fromDateHijri, string toDateHijri, bool showAllDays = false)
		{
			if (selectedBadgeNumbers == null)
			{
				selectedBadgeNumbers = new string[0];
			}
			base.ViewBag.selectedBadgeNumbers = selectedBadgeNumbers;
			base.ViewBag.fromDateHijri = fromDateHijri;
			base.ViewBag.toDateHijri = toDateHijri;
			List<OverTimeForDay> list = new List<OverTimeForDay>();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			bool flag = false;
			string errorMessage = "";
			DateTime? nullable = DateConverter.ToChristian(fromDateHijri);
			DateTime? nullable2 = DateConverter.ToChristian(toDateHijri);
			if (!nullable.HasValue || !nullable2.HasValue || nullable2.Value < nullable.Value)
			{
				flag = true;
				errorMessage = PersianString.BadDateFormat;
			}
			if (flag)
			{
				ReportModel reportModel = new ReportModel();
				reportModel.ErrorMessage = errorMessage;
				return RedirectToAction("Index", (object)reportModel);
			}
			Attendance attendance = new Attendance(t5DBInstance, personnelSystem);
			List<long> list2 = new List<long>();
			long result = 0L;
			string[] array = selectedBadgeNumbers;
			for (int i = 0; i < array.Length; i++)
			{
				if (long.TryParse(array[i], out result))
				{
					list2.Add(result);
				}
			}
			foreach (ADayResult item in attendance.Calculate(list2, nullable.Value, nullable2.Value))
			{
				OverTimeForDay overTimeForDay = new OverTimeForDay();
				overTimeForDay.BadgeNumber = item.BadgeNumber;
				overTimeForDay.Name = item.UserInfo.ToString();
				overTimeForDay.ForDate = DateConverter.ToHijriDate(item.ForDate);
				if (item.ProcessedSchedule != null)
				{
					overTimeForDay.AfterValueLast = Misc.TimeSpanToString(item.ProcessedSchedule.OverTimeAfterLast);
					if (item.ProcessedSchedule.OverTimeAfterReal != default(TimeSpan))
					{
						overTimeForDay.AfterValueReal = Misc.TimeSpanToString(item.ProcessedSchedule.OverTimeAfterReal);
					}
					else
					{
						overTimeForDay.AfterValueReal = "-";
					}
					if (item.ProcessedSchedule.OverTimeBeforeReal != default(TimeSpan))
					{
						overTimeForDay.BeforeValueReal = Misc.TimeSpanToString(item.ProcessedSchedule.OverTimeBeforeReal);
					}
					else
					{
						overTimeForDay.BeforeValueReal = "-";
					}
					overTimeForDay.BeforeValueLast = Misc.TimeSpanToString(item.ProcessedSchedule.OverTimeBeforeLast);
				}
				if (showAllDays || item.ProcessedSchedule.OverTimeBeforeLast != default(TimeSpan) || item.ProcessedSchedule.OverTimeBeforeReal != default(TimeSpan) || item.ProcessedSchedule.OverTimeAfterLast != default(TimeSpan) || item.ProcessedSchedule.OverTimeAfterReal != default(TimeSpan))
				{
					list.Add(overTimeForDay);
				}
			}
			return View(list);
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult ChangeBefore(long badgeNumber, string forDate, string requredValue, string actionType, string[] allBadgeNumbers, string fromDateHijri, string toDateHijri)
		{
			ReportModel obj = new ReportModel
			{
				ToDateHijri = toDateHijri,
				FromDateHijri = fromDateHijri,
				SelectedBadgeNumbers = allBadgeNumbers.ToList()
			};
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			DateTime? nullable = DateConverter.ToChristian(forDate);
			TimeSpan timeSpan = new TimeSpan(0L);
			timeSpan = Misc.StringToTimeSpan(requredValue);
			if (nullable.HasValue)
			{
				string err = "";
				if (!ConstOTController.GetOverTimeSubUsers(ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext)).Contains(badgeNumber))
				{
					return View("Error", "", PersianString.AccessDenied);
				}
				if (actionType == PersianString.Cancel)
				{
					timeSpan = new TimeSpan(0L);
				}
				t5DBInstance.EditedValueDA.InsertOrEdit(new EditedValue(badgeNumber, EditValueType.OTBefore, nullable.Value, timeSpan), out err);
			}
			string text = $"fromDateHijri={fromDateHijri}&toDateHijri={toDateHijri}";
			foreach (string str in allBadgeNumbers)
			{
				text = text + "&selectedBadgeNumbers=" + str;
			}
			return Redirect($"~/OverTimeReport/Show?{text}");
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult ChangeAfter(long badgeNumber, string forDate, string requredValue, string actionType, string[] allBadgeNumbers, string fromDateHijri, string toDateHijri)
		{
			ReportModel obj = new ReportModel
			{
				ToDateHijri = toDateHijri,
				FromDateHijri = fromDateHijri,
				SelectedBadgeNumbers = allBadgeNumbers.ToList()
			};
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			DateTime? nullable = DateConverter.ToChristian(forDate);
			TimeSpan timeSpan = new TimeSpan(0L);
			timeSpan = Misc.StringToTimeSpan(requredValue);
			if (nullable.HasValue)
			{
				string err = "";
				if (!ConstOTController.GetOverTimeSubUsers(ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext)).Contains(badgeNumber))
				{
					return View("Error", "", PersianString.AccessDenied);
				}
				if (actionType == PersianString.Cancel)
				{
					timeSpan = new TimeSpan(0L);
				}
				t5DBInstance.EditedValueDA.InsertOrEdit(new EditedValue(badgeNumber, EditValueType.OTAfter, nullable.Value, timeSpan), out err);
			}
			string text = $"fromDateHijri={fromDateHijri}&toDateHijri={toDateHijri}";
			foreach (string str in allBadgeNumbers)
			{
				text = text + "&selectedBadgeNumbers=" + str;
			}
			return Redirect($"~/OverTimeReport/Show?{text}");
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult AcceptBefore(long badgeNumber, string forDate, string requredValue)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			DateTime? nullable = DateConverter.ToChristian(forDate);
			TimeSpan result = new TimeSpan(0L);
			if (TimeSpan.TryParse(requredValue, out result) && nullable.HasValue)
			{
				if (!ConstOTController.GetOverTimeSubUsers(ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext)).Contains(badgeNumber))
				{
					return View("Error", "", PersianString.AccessDenied);
				}
				t5DBInstance.AllowValueDA.AddOrEdit(new AllowedValue(badgeNumber, nullable.Value, AllowType.OverTimeBefore, result));
			}
			return RedirectToAction("Index");
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult CancelBefore(long badgeNumber, string forDate)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			DateTime? nullable = DateConverter.ToChristian(forDate);
			if (nullable.HasValue)
			{
				if (!ConstOTController.GetOverTimeSubUsers(ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext)).Contains(badgeNumber))
				{
					return View("Error", "", PersianString.AccessDenied);
				}
				t5DBInstance.AllowValueDA.Delete(new AllowedValue(badgeNumber, nullable.Value, AllowType.OverTimeBefore, default(TimeSpan)));
			}
			return RedirectToAction("Index");
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult AcceptAfter(long badgeNumber, string forDate, string requredValue)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			DateTime? nullable = DateConverter.ToChristian(forDate);
			TimeSpan result = new TimeSpan(0L);
			if (TimeSpan.TryParse(requredValue, out result) && nullable.HasValue)
			{
				if (!ConstOTController.GetOverTimeSubUsers(ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext)).Contains(badgeNumber))
				{
					return View("Error", "", PersianString.AccessDenied);
				}
				t5DBInstance.AllowValueDA.AddOrEdit(new AllowedValue(badgeNumber, nullable.Value, AllowType.OverTimeAfter, result));
			}
			return RedirectToAction("Index");
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult CancelAfter(long badgeNumber, string forDate)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			DateTime? nullable = DateConverter.ToChristian(forDate);
			if (nullable.HasValue)
			{
				if (!ConstOTController.GetOverTimeSubUsers(ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext)).Contains(badgeNumber))
				{
					return View("Error", "", PersianString.AccessDenied);
				}
				t5DBInstance.AllowValueDA.Delete(new AllowedValue(badgeNumber, nullable.Value, AllowType.OverTimeAfter, default(TimeSpan)));
			}
			return RedirectToAction("Index");
		}
	}
}
