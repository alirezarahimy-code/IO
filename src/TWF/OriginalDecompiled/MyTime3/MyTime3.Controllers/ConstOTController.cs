using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class ConstOTController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index(bool pre = false)
		{
			base.ViewBag.Pre = pre;
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			List<SumConstOT> list = new List<SumConstOT>();
			List<int> ownedTitles = GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext);
			PersianCalendar persianCalendar = new PersianCalendar();
			DateTime dateTime = persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), persianCalendar.GetMonth(DateTime.Now), 1, 0, 0, 0, 0);
			DateTime dateTime2 = persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), persianCalendar.GetMonth(DateTime.Now), persianCalendar.GetDaysInMonth(persianCalendar.GetYear(DateTime.Now), persianCalendar.GetMonth(DateTime.Now)), 0, 0, 0, 0);
			if (pre)
			{
				dateTime = persianCalendar.AddMonths(dateTime, -1);
				dateTime2 = persianCalendar.AddMonths(dateTime2, -1);
			}
			foreach (long overTimeSubUser in GetOverTimeSubUsers(ownedTitles))
			{
				SumConstOT sumConstOT = new SumConstOT();
				sumConstOT.BadgeNumber = overTimeSubUser;
				sumConstOT.Name = personnelSystem.DB.UserInfos.GetByID(overTimeSubUser).ToString();
				bool flag = true;
				foreach (OTConstDaily item in GetAllDailyConstOT(overTimeSubUser, dateTime, dateTime2))
				{
					if (flag)
					{
						sumConstOT.FirstDayValue = item.ValueDaily;
						flag = false;
					}
					sumConstOT.SumValue += item.ValueDaily;
				}
				list.Add(sumConstOT);
			}
			return View(list);
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Edit(long badgeNumber, string error, bool pre = false)
		{
			base.ViewBag.Pre = pre;
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			List<int> ownedTitles = GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext);
			List<OTConstDaily> model = new List<OTConstDaily>();
			base.ViewBag.ExMessage = ((error != null) ? error : "");
			if (GetOverTimeSubUsers(ownedTitles).Contains(badgeNumber))
			{
				PersianCalendar persianCalendar = new PersianCalendar();
				int num = persianCalendar.GetYear(DateTime.Now);
				int num2 = persianCalendar.GetMonth(DateTime.Now);
				if (pre)
				{
					num2--;
					if (num2 < 1)
					{
						num2 = 12;
						num--;
					}
				}
				DateTime fromDate = persianCalendar.ToDateTime(num, num2, 1, 0, 0, 0, 0);
				DateTime toDate = persianCalendar.ToDateTime(num, num2, persianCalendar.GetDaysInMonth(num, num2), 0, 0, 0, 0);
				model = GetAllDailyConstOT(badgeNumber, fromDate, toDate);
			}
			return View(model);
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Edit(long badgeNumber, string forDate, string newValue)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			List<int> ownedTitles = GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext);
			TimeSpan timeSpan = default(TimeSpan);
			string error = "";
			timeSpan = Misc.StringToTimeSpan(newValue);
			if (GetOverTimeSubUsers(ownedTitles).Contains(badgeNumber))
			{
				PersianCalendar persianCalendar = new PersianCalendar();
				DateTime fromDate = persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), persianCalendar.GetMonth(DateTime.Now), 1, 0, 0, 0, 0);
				DateTime toDate = persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), persianCalendar.GetMonth(DateTime.Now), persianCalendar.GetDaysInMonth(persianCalendar.GetYear(DateTime.Now), persianCalendar.GetMonth(DateTime.Now)), 0, 0, 0, 0);
				TimeSpan t = default(TimeSpan);
				foreach (OTConstDaily item in GetAllDailyConstOT(badgeNumber, fromDate, toDate))
				{
					t += item.ValueDaily;
				}
				Rule activeRule = new RuleProcessor(t5DBInstance.RuleDA.GetAll(), t5DBInstance.DepartmentRuleDA.GetAll(), t5DBInstance.UserRuleDA.GetAll()).GetActiveRule(personnelSystem.DB.UserInfos.GetByID(badgeNumber), null, DateTime.Now);
				if (activeRule != null)
				{
					if (t + timeSpan > activeRule.ConstOTMaxMonthly)
					{
						error = PersianString.MonthlyOTConstLimit;
					}
					else if (timeSpan > activeRule.ConstOTMaxDaily)
					{
						error = PersianString.DailyOTConstLimit;
					}
					else
					{
						EditedValue editedValue = new EditedValue();
						editedValue.BadgeNumber = badgeNumber;
						editedValue.ForDate = DateConverter.ToChristian(forDate).Value;
						editedValue.ValidDuration = timeSpan;
						editedValue.ValueType = EditValueType.OTConst;
						string err = "";
						t5DBInstance.EditedValueDA.InsertOrEdit(editedValue, out err);
					}
				}
				else
				{
					error = PersianString.DefineUserRule;
				}
			}
			return RedirectToAction("Edit", (object)new
			{
				badgeNumber,
				error
			});
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult EditAll(string[] badgeNumbers, string[] edittedValues, bool pre = false)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			List<int> ownedTitles = GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext);
			for (int i = 0; i < badgeNumbers.Length; i++)
			{
				if (edittedValues.Length > i)
				{
					TimeSpan timeSpan = Misc.StringToTimeSpan(edittedValues[i]);
					if (long.TryParse(badgeNumbers[i], out long result) && GetOverTimeSubUsers(ownedTitles).Contains(result))
					{
						PersianCalendar persianCalendar = new PersianCalendar();
						DateTime dateTime = persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), persianCalendar.GetMonth(DateTime.Now), 1, 0, 0, 0, 0);
						DateTime dateTime2 = persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), persianCalendar.GetMonth(DateTime.Now), persianCalendar.GetDaysInMonth(persianCalendar.GetYear(DateTime.Now), persianCalendar.GetMonth(DateTime.Now)), 0, 0, 0, 0);
						if (pre)
						{
							dateTime = persianCalendar.AddMonths(dateTime, -1);
							dateTime2 = persianCalendar.AddMonths(dateTime2, -1);
						}
						TimeSpan t = default(TimeSpan);
						foreach (OTConstDaily item in GetAllDailyConstOT(result, dateTime, dateTime2))
						{
							t += item.ValueDaily;
						}
						Rule activeRule = new RuleProcessor(t5DBInstance.RuleDA.GetAll(), t5DBInstance.DepartmentRuleDA.GetAll(), t5DBInstance.UserRuleDA.GetAll()).GetActiveRule(personnelSystem.DB.UserInfos.GetByID(result), null, DateTime.Now);
						if (activeRule != null)
						{
							if (timeSpan > activeRule.ConstOTMaxMonthly)
							{
								string monthlyOTConstLimit = PersianString.MonthlyOTConstLimit;
							}
							else
							{
								EditedValue editedValue = new EditedValue();
								editedValue.BadgeNumber = result;
								editedValue.ForDate = dateTime;
								editedValue.ValidDuration = timeSpan;
								editedValue.ValueType = EditValueType.OTConst;
								string err = "";
								t5DBInstance.EditedValueDA.InsertOrEdit(editedValue, out err);
							}
						}
						else
						{
							string defineUserRule = PersianString.DefineUserRule;
						}
					}
				}
			}
			return RedirectToAction("Index", (object)new
			{
				pre
			});
		}

		private List<OTConstDaily> GetAllDailyConstOT(long badgeNumber, DateTime fromDate, DateTime toDate)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<OTConstDaily> list = new List<OTConstDaily>();
			List<EditedValue> all = t5DBInstance.EditedValueDA.GetAll(new List<long>
			{
				badgeNumber
			}, fromDate, toDate);
			while (fromDate <= toDate)
			{
				OTConstDaily oTConstDaily = new OTConstDaily();
				oTConstDaily.BadgeNumber = badgeNumber;
				oTConstDaily.ForDate = DateConverter.ToHijriDate(fromDate);
				oTConstDaily.DayOfWeek = Misc.DayOfWeekToString(fromDate.DayOfWeek);
				oTConstDaily.ValueDaily = default(TimeSpan);
				IEnumerable<EditedValue> source = all.Where(delegate(EditedValue ev)
				{
					if (ev.ForDate == fromDate)
					{
						return ev.ValueType == EditValueType.OTConst;
					}
					return false;
				});
				if (source.Count() > 0)
				{
					oTConstDaily.ValueDaily = source.First().ValidDuration;
				}
				list.Add(oTConstDaily);
				fromDate = fromDate.AddDays(1.0);
			}
			return list;
		}

		public static List<int> GetOwnedTitles(T5V3DBManager db, PersonnelSystem ps, long badge, HttpContextBase httpContext)
		{
			List<int> list = new List<int>();
			int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(httpContext);
			if (loggedInUserInfoTitleID != -1)
			{
				list.Add(loggedInUserInfoTitleID);
			}
			foreach (long item in db.PersonnelDeputyDA.IsDeputy(badge))
			{
				int userCurrentTitleID = ps.DB.UserInfos.GetUserCurrentTitleID(item);
				if (userCurrentTitleID != -1)
				{
					list.Add(userCurrentTitleID);
				}
			}
			return list;
		}

		public static List<long> GetOverTimeSubUsers(List<int> titleIDs)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			new List<long>();
			List<int> subTitleIDs = new List<int>();
			List<KeyValuePair<int, int>> allWithUppersOnly = personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly();
			foreach (Personnel.Model.TitleChart item in personnelSystem.DB.TitlesCharts.GetAll())
			{
				bool flag = true;
				foreach (Workflow item2 in t5DBInstance.WorkflowDA.GetAll(item.TitleID, WorkflowOperations.AllowOverTimes))
				{
					if (titleIDs.Contains(item2.AccepterTitleID))
					{
						subTitleIDs.Add(item.TitleID);
					}
					flag = false;
				}
				if (flag && titleIDs.Contains((from at in allWithUppersOnly
				where at.Key == item.TitleID
				select at.Value).First()))
				{
					subTitleIDs.Add(item.TitleID);
				}
			}
			return (from ut in personnelSystem.DB.UserInfos.GetAllUserInfoTitles()
			where subTitleIDs.Contains(ut.Value)
			select ut.Key).ToList();
		}
	}
}
