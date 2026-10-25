using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Configuration;
using System.Web.Mvc;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.LeaveCalcs;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class RemainLeaveController : Controller
	{
		[HttpGet]
		public RemainLeave GetRemain(long badgeNumber, int leaveTypeID, DateTime d)
		{
			d = d.Date;
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			RemainLeave remainLeave = new RemainLeave();
			remainLeave.ToDate = d;
			remainLeave.BadgeNumber = badgeNumber;
			remainLeave.LeaveTypeID = leaveTypeID;
			remainLeave.DailyTaken = 0.0;
			remainLeave.HourlyTaken = new TimeSpan(0L);
			remainLeave.Requested = 0.0;
			Attendance attendance = new Attendance(t5DBInstance, personnelSystem);
			PersianCalendar pc = new PersianCalendar();
			int year = pc.GetYear(d);
			DateTime fromDate = pc.ToDateTime(year, 1, 1, 0, 0, 0, 0);
			DateTime toDate = pc.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);
			List<LeaveType> all = t5DBInstance.LeaveTypeDA.GetAll();
			List<Schedule> all2 = t5DBInstance.ScheduleDA.GetAll();
			List<Paramtr> all3 = t5DBInstance.ParameterDA.GetAll();
			Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(badgeNumber);
			remainLeave.Name = byID.ToString();
			List<ADayResult> list = attendance.Calculate(badgeNumber, fromDate, toDate);
			IEnumerable<ADayRemain> source = from rc in new Remain(list, all, t5DBInstance.LeavesAddedDA.GetAll(new List<long>
			{
				badgeNumber
			}, fromDate, toDate), t5DBInstance.YearRemainDA.GetAll(badgeNumber), all2, all3).CalculateRemain(byID, leaveTypeID, year)
			where rc.ForDate == d
			select rc;
			if (source.Count() > 0)
			{
				ADayRemain aDayRemain = source.First();
				remainLeave.Remain = aDayRemain.Remain;
			}
			IEnumerable<LeaveType> source2 = from lt in all
			where lt.LeaveTypeID == leaveTypeID
			select lt;
			LeaveType leaveType = source2.FirstOrDefault();
			int i;
			for (i = 1; i < 13; i++)
			{
				int num = pc.GetDaysInMonth(year, i) + 1;
				int j;
				for (j = 1; j < num; j++)
				{
					ADayResult aDayResult = (from ya in list
					where ya.ForDate == pc.ToDateTime(year, i, j, 0, 0, 0, 0)
					select ya).First();
					if (aDayResult.IsDailyLeave)
					{
						string symbol = aDayResult.DailyLeaveValue.DailyLeaveType.Symbol;
						if (aDayResult.DailyLeaveValue.LeaveTypeID == leaveTypeID && ((!aDayResult.IsHoliday && aDayResult.CurrentShift.ActiveSchedule != null) || leaveType.GetInHolidays))
						{
							RemainLeave remainLeave2 = remainLeave;
							double dailyTaken = remainLeave2.DailyTaken;
							remainLeave2.DailyTaken = dailyTaken + 1.0;
						}
					}
					if (aDayResult.ProcessedSchedule != null)
					{
						foreach (T5V3.Attendance.InShiftExit inShiftExit in aDayResult.ProcessedSchedule.InShiftExits)
						{
							if (inShiftExit.lvType != null)
							{
								if (inShiftExit.lvType.DecreaseFromLeaveID.HasValue && inShiftExit.lvType.DecreaseFromLeaveID.Value == leaveTypeID)
								{
									remainLeave.HourlyTaken += inShiftExit.ValidLen;
								}
								if (inShiftExit.lvType.LeaveTypeID == leaveTypeID)
								{
									remainLeave.HourlyTaken += inShiftExit.ValidLen;
								}
							}
						}
					}
				}
			}
			if (source2.Count() > 0)
			{
				remainLeave.MaxMinus = leaveType.MaxMinus;
				RemainLeave remainLeave3 = remainLeave;
				double dailyTaken2 = remainLeave.DailyTaken;
				TimeSpan timeSpan = remainLeave.HourlyTaken;
				double totalHours = timeSpan.TotalHours;
				timeSpan = leaveType.EachXHoursADay;
				remainLeave3.TotalTaken = dailyTaken2 + totalHours / timeSpan.TotalHours;
				remainLeave.LeaveTypeName = leaveType.LeaveName;
				{
					foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(badgeNumber, fromDate, toDate))
					{
						new MyTime3.Models.Request().RequestT5 = item;
						if (item.IsSuspend && item.WorkflowType == WorkflowOperations.Leave)
						{
							if (item.LeaveID == leaveTypeID)
							{
								RemainLeave remainLeave4 = remainLeave;
								double dailyTaken = remainLeave4.Requested;
								remainLeave4.Requested = dailyTaken + 1.0;
							}
							LeaveType leaveType2 = (from lt in all
							where lt.LeaveTypeID == item.LeaveID
							select lt).FirstOrDefault();
							if (leaveType2 != null)
							{
								if (!item.IsDaily)
								{
									if (leaveType2.DecreaseFromLeaveID.HasValue && leaveType2.DecreaseFromLeaveID.Value == leaveTypeID)
									{
										RemainLeave remainLeave5 = remainLeave;
										double requested = remainLeave5.Requested;
										timeSpan = item.ToTime - item.FromTime;
										double totalHours2 = timeSpan.TotalHours;
										timeSpan = leaveType.EachXHoursADay;
										remainLeave5.Requested = requested + totalHours2 / timeSpan.TotalHours;
									}
									if (leaveType2.LeaveTypeID == leaveTypeID)
									{
										RemainLeave remainLeave6 = remainLeave;
										double requested2 = remainLeave6.Requested;
										timeSpan = item.ToTime - item.FromTime;
										double totalHours3 = timeSpan.TotalHours;
										timeSpan = leaveType.EachXHoursADay;
										remainLeave6.Requested = requested2 + totalHours3 / timeSpan.TotalHours;
									}
								}
								else if (leaveType2.DecreaseFromLeaveID.HasValue && leaveType2.DecreaseFromLeaveID.Value == leaveTypeID)
								{
									RemainLeave remainLeave7 = remainLeave;
									double dailyTaken = remainLeave7.Requested;
									remainLeave7.Requested = dailyTaken + 1.0;
								}
							}
						}
					}
					return remainLeave;
				}
			}
			return remainLeave;
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public JsonResult RemainLeaveJ(long badgeNumber, int leaveID)
		{
			RemainLeave data = new RemainLeave();
			bool flag = false;
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			if (loggedInBadgeNumber != -1)
			{
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
				List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly());
				int titleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
				List<long>.Enumerator enumerator;
				if (titleID != -1)
				{
					enumerator = (from r in requestAccepters
					where r.Value == titleID
					select r.Key).ToList().GetEnumerator();
					try
					{
						while (enumerator.MoveNext())
						{
							long current = enumerator.Current;
							T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(current);
							if (byID.BadgeNumber == badgeNumber && byID.WorkflowType == WorkflowOperations.Leave)
							{
								flag = true;
							}
						}
					}
					finally
					{
						((IDisposable)enumerator).Dispose();
					}
				}
				enumerator = t5DBInstance.PersonnelDeputyDA.IsDeputy(loggedInBadgeNumber).GetEnumerator();
				try
				{
					while (enumerator.MoveNext())
					{
						long current2 = enumerator.Current;
						int deputyTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(current2);
						if (deputyTitleID != -1)
						{
							foreach (long item in (from r in requestAccepters
							where r.Value == deputyTitleID
							select r.Key).ToList())
							{
								T5V3.Model.Request byID2 = t5DBInstance.RequestDA.GetByID(item);
								if (byID2.BadgeNumber == badgeNumber && byID2.WorkflowType == WorkflowOperations.Leave)
								{
									flag = true;
								}
							}
						}
					}
				}
				finally
				{
					((IDisposable)enumerator).Dispose();
				}
				if (flag)
				{
					data = GetRemain(badgeNumber, leaveID, DateTime.Now.Date);
				}
			}
			return Json(data);
		}

		[AuthorizeFilter(Roles = "admin,user")]
		public RemainLeave RemainLeaveJj(long badgeNumber, int leaveID)
		{
			RemainLeave result = new RemainLeave();
			bool flag = false;
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			if (loggedInBadgeNumber != -1)
			{
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
				List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly());
				int titleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
				List<long>.Enumerator enumerator;
				if (titleID != -1)
				{
					enumerator = (from r in requestAccepters
					where r.Value == titleID
					select r.Key).ToList().GetEnumerator();
					try
					{
						while (enumerator.MoveNext())
						{
							long current = enumerator.Current;
							T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(current);
							if (byID.BadgeNumber == badgeNumber && byID.WorkflowType == WorkflowOperations.Leave)
							{
								flag = true;
							}
						}
					}
					finally
					{
						((IDisposable)enumerator).Dispose();
					}
				}
				enumerator = t5DBInstance.PersonnelDeputyDA.IsDeputy(loggedInBadgeNumber).GetEnumerator();
				try
				{
					while (enumerator.MoveNext())
					{
						long current2 = enumerator.Current;
						int deputyTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(current2);
						if (deputyTitleID != -1)
						{
							foreach (long item in (from r in requestAccepters
							where r.Value == deputyTitleID
							select r.Key).ToList())
							{
								T5V3.Model.Request byID2 = t5DBInstance.RequestDA.GetByID(item);
								if (byID2.BadgeNumber == badgeNumber && byID2.WorkflowType == WorkflowOperations.Leave)
								{
									flag = true;
								}
							}
						}
					}
				}
				finally
				{
					((IDisposable)enumerator).Dispose();
				}
				if (flag)
				{
					result = GetRemain(badgeNumber, leaveID, DateTime.Now.Date);
				}
			}
			return result;
		}

		[HttpPost]
		public JsonResult RemainLeave(string userName, string password, string badgeNumbers)
		{
			List<RemainLeave> list = new List<RemainLeave>();
			long num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				if (badgeNumbers == null || badgeNumbers == string.Empty)
				{
					badgeNumbers = num.ToString();
				}
				List<long> list2 = new List<long>();
				long result = 0L;
				string[] array = badgeNumbers.Split(new char[1]
				{
					';'
				}, StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < array.Length; i++)
				{
					if (long.TryParse(array[i], out result))
					{
						list2.Add(result);
					}
				}
				int result2 = 0;
				int.TryParse(WebConfigurationManager.get_AppSettings()["LeaveID"], out result2);
				foreach (long item in list2)
				{
					list.Add(GetRemain(item, result2, DateTime.Now.Date));
				}
			}
			return Json(list);
		}

		[HttpPost]
		public string GetForUser(string[] SelectedBadgeNumbers, bool isSelf = true, int? LeaveTypeID = default(int?), bool? year = default(bool?))
		{
			GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
			DateTime dateTime = DateTime.Now;
			if (year.HasValue && year.Value)
			{
				PersianCalendar persianCalendar = new PersianCalendar();
				dateTime = persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), 1, 1, 0, 0, 0, 0).AddDays(-1.0);
			}
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			List<int> ownedTitles = ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext);
			List<PersonnelModel> list = new List<PersonnelModel>();
			foreach (Personnel.Model.UserInfo item2 in personnelSystem.DB.UserInfos.GetAll(GetLeaveSubUsers(ownedTitles)))
			{
				PersonnelModel item = new PersonnelModel(item2.BadgeNumber, item2.FirstName, item2.LastName);
				list.Add(item);
			}
			base.ViewBag.Personnel = list;
			AddPersonnelToViewBag();
			List<RemainLeave> list2 = new List<RemainLeave>();
			if (LeaveTypeID.HasValue)
			{
				if (isSelf)
				{
					list2.Add(GetRemain(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), LeaveTypeID.Value, dateTime.Date));
				}
				else if (SelectedBadgeNumbers != null)
				{
					foreach (string s in SelectedBadgeNumbers)
					{
						list2.Add(GetRemain(long.Parse(s), LeaveTypeID.Value, dateTime.Date));
					}
				}
			}
			return RenderRazorViewToString("GetForUser", list2);
		}

		public string RenderRazorViewToString(string viewName, object model)
		{
			base.ViewData.Model = model;
			using (StringWriter stringWriter = new StringWriter())
			{
				ViewEngineResult viewEngineResult = ViewEngines.Engines.FindPartialView(base.ControllerContext, viewName);
				ViewContext viewContext = new ViewContext(base.ControllerContext, viewEngineResult.View, base.ViewData, base.TempData, stringWriter);
				viewEngineResult.View.Render(viewContext, stringWriter);
				viewEngineResult.ViewEngine.ReleaseView(base.ControllerContext, viewEngineResult.View);
				return stringWriter.GetStringBuilder().ToString();
			}
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index(RemainLeave leaveType = null)
		{
			GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
			DBInitializer.GetT5DBInstance();
			DBInitializer.GetPersonnelSystem();
			GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			AddPersonnelToViewBag();
			if ((int)SettingsController.LoadValue("HideLeaveTypes", typeof(int), -1) > 0)
			{
				for (int num = (int)(base.ViewBag.DailyLeaveTypes.Count - 1); num >= 0; num--)
				{
					if (base.ViewBag.DailyLeaveTypes[num].Value != ((int)SettingsController.LoadValue("HideLeaveTypes", typeof(int), -1)).ToString())
					{
						base.ViewBag.DailyLeaveTypes.RemoveAt(num);
					}
				}
			}
			if ((leaveType == null || leaveType.LeaveTypeID == 0) && ((base.ViewBag.DailyLeaveTypes.Count > 0) ? true : false))
			{
				leaveType = new RemainLeave();
				leaveType.LeaveTypeID = int.Parse((string)base.ViewBag.DailyLeaveTypes[0].Value);
			}
			List<RemainLeave> list = new List<RemainLeave>();
			if (leaveType != null)
			{
				list.Add(GetRemain(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), leaveType.LeaveTypeID, DateTime.Now));
			}
			return View(list);
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult ForPersonnel(long badgeNumber, int leaveTypeID)
		{
			RemainLeave model = new RemainLeave();
			bool flag = false;
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			if (loggedInBadgeNumber != -1)
			{
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
				List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly());
				int titleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
				List<long>.Enumerator enumerator;
				if (titleID != -1)
				{
					enumerator = (from r in requestAccepters
					where r.Value == titleID
					select r.Key).ToList().GetEnumerator();
					try
					{
						while (enumerator.MoveNext())
						{
							long current = enumerator.Current;
							T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(current);
							if (byID.BadgeNumber == badgeNumber && byID.WorkflowType == WorkflowOperations.Leave)
							{
								flag = true;
							}
						}
					}
					finally
					{
						((IDisposable)enumerator).Dispose();
					}
				}
				enumerator = t5DBInstance.PersonnelDeputyDA.IsDeputy(loggedInBadgeNumber).GetEnumerator();
				try
				{
					while (enumerator.MoveNext())
					{
						long current2 = enumerator.Current;
						int deputyTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(current2);
						if (deputyTitleID != -1)
						{
							foreach (long item in (from r in requestAccepters
							where r.Value == deputyTitleID
							select r.Key).ToList())
							{
								T5V3.Model.Request byID2 = t5DBInstance.RequestDA.GetByID(item);
								if (byID2.BadgeNumber == badgeNumber && byID2.WorkflowType == WorkflowOperations.Leave)
								{
									flag = true;
								}
							}
						}
					}
				}
				finally
				{
					((IDisposable)enumerator).Dispose();
				}
				if (flag)
				{
					model = GetRemain(badgeNumber, leaveTypeID, DateTime.Now.Date);
				}
			}
			return View(model);
		}

		private void AddPersonnelToViewBag()
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			List<PersonnelModel> list = new List<PersonnelModel>();
			int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
			List<Personnel.Model.TitleChart> source = new List<Personnel.Model.TitleChart>();
			if (loggedInUserInfoTitleID != -1)
			{
				source = personnelSystem.DB.TitlesCharts.GetAll(loggedInUserInfoTitleID);
			}
			List<Personnel.Model.UserInfo>.Enumerator enumerator = personnelSystem.DB.UserInfos.GetAllByTitle((from t in source
			select t.TitleID).ToList()).GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					Personnel.Model.UserInfo current = enumerator.Current;
					list.Add(new PersonnelModel(current.BadgeNumber, current.FirstName, current.LastName));
				}
			}
			finally
			{
				((IDisposable)enumerator).Dispose();
			}
			foreach (long item in t5DBInstance.PersonnelDeputyDA.IsDeputy(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext)))
			{
				int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
				if (userCurrentTitleID != -1)
				{
					source = personnelSystem.DB.TitlesCharts.GetAll(userCurrentTitleID);
					enumerator = personnelSystem.DB.UserInfos.GetAllByTitle((from t in source
					select t.TitleID).ToList()).GetEnumerator();
					try
					{
						while (enumerator.MoveNext())
						{
							Personnel.Model.UserInfo current3 = enumerator.Current;
							list.Add(new PersonnelModel(current3.BadgeNumber, current3.FirstName, current3.LastName));
						}
					}
					finally
					{
						((IDisposable)enumerator).Dispose();
					}
				}
			}
			base.ViewBag.Personnel = list;
		}

		public static List<long> GetLeaveSubUsers(List<int> titleIDs)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			new List<long>();
			List<int> subTitleIDs = new List<int>();
			List<KeyValuePair<int, int>> allWithUppersOnly = personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly();
			foreach (Personnel.Model.TitleChart item in personnelSystem.DB.TitlesCharts.GetAll())
			{
				bool flag = true;
				foreach (Workflow item2 in t5DBInstance.WorkflowDA.GetAll(item.TitleID, WorkflowOperations.Leave))
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
