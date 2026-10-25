using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.LeaveCalcs;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class AcceptRequestController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index(long forBadgeNumberFilter = -1L, bool fromHome = true, int sort = -1, bool g = true)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<MyTime3.Models.Request> list = new List<MyTime3.Models.Request>();
			base.ViewBag.SortByDate = sort;
			base.ViewBag.GroupByName = g;
			if (forBadgeNumberFilter != -1)
			{
				base.ViewBag.requestBadgeFilter = forBadgeNumberFilter;
				if (fromHome)
				{
					base.ViewBag.PrePageLink = "/Home/Index";
				}
				else
				{
					base.ViewBag.PrePageLink = "/AcceptRequest/Index";
				}
			}
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
						MyTime3.Models.Request request = new MyTime3.Models.Request();
						request.ForDeputy = false;
						request.RequestT5 = t5DBInstance.RequestDA.GetByID(current);
						Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(request.RequestT5.BadgeNumber);
						if (byID != null)
						{
							request.RequestT5.RequesterName = byID.ToString();
						}
						if (forBadgeNumberFilter == -1 || forBadgeNumberFilter == byID.BadgeNumber)
						{
							list.Add(request);
						}
					}
				}
				finally
				{
					((IDisposable)enumerator).Dispose();
				}
			}
			enumerator = t5DBInstance.PersonnelDeputyDA.IsDeputy(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext)).GetEnumerator();
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
							MyTime3.Models.Request request2 = new MyTime3.Models.Request();
							request2.ForDeputy = true;
							request2.DeputyTitleName = personnelSystem.DB.TitlesCharts.GetByID(deputyTitleID).TitleName;
							request2.RequestT5 = t5DBInstance.RequestDA.GetByID(item);
							Personnel.Model.UserInfo byID2 = personnelSystem.DB.UserInfos.GetByID(request2.RequestT5.BadgeNumber);
							if (byID2 != null)
							{
								request2.RequestT5.RequesterName = byID2.ToString();
							}
							if (forBadgeNumberFilter == -1 || forBadgeNumberFilter == byID2.BadgeNumber)
							{
								list.Add(request2);
							}
						}
					}
				}
			}
			finally
			{
				((IDisposable)enumerator).Dispose();
			}
			return View(list);
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Accept(long requestID, bool? returnToHome = default(bool?))
		{
			string text = "";
			DateTime dateTime = DateTime.Now;
			int year = dateTime.Year;
			DateTime t = new DateTime(year, 3, 19);
			T5V3.Model.Request byID = DBInitializer.GetT5DBInstance().RequestDA.GetByID(requestID);
			dateTime = byID.ForDate;
			int year2 = dateTime.Year;
			dateTime = DateTime.Now;
			text = ((year2 >= dateTime.Year && !(byID.ForDate < t)) ? AcceptByID2(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), requestID) : AcceptByID(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), requestID));
			if (text != "0")
			{
				return View("Error", "", text);
			}
			if (returnToHome.HasValue)
			{
				return RedirectToAction("Index", "Home");
			}
			return RedirectToAction("Index");
		}

		[HttpPost]
		public JsonResult AcceptRequest(string userName, string password, long requestID)
		{
			string result = "";
			long num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				result = AcceptByID2(num, requestID);
			}
			return Json(new
			{
				result
			});
		}

		[HttpPost]
		public JsonResult CancelRequest(string userName, string password, long requestID)
		{
			string result = "";
			long num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				result = CancelByID(num, requestID);
			}
			return Json(new
			{
				result
			});
		}

		private string AcceptByID2(long badgeNumber, long requestID)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), requestID);
			T5V3.Model.Request req = t5DBInstance.RequestDA.GetByID(requestID);
			List<long>.Enumerator enumerator;
			KeyValuePair<long, int> keyValuePair;
			if (req.LeaveID == -1)
			{
				int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(badgeNumber);
				List<long> list = t5DBInstance.PersonnelDeputyDA.IsDeputy(badgeNumber);
				List<int> list2 = new List<int>();
				enumerator = list.GetEnumerator();
				try
				{
					while (enumerator.MoveNext())
					{
						long current = enumerator.Current;
						int userCurrentTitleID2 = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(current);
						if (userCurrentTitleID2 != -1)
						{
							list2.Add(userCurrentTitleID2);
						}
					}
				}
				finally
				{
					((IDisposable)enumerator).Dispose();
				}
				if ((userCurrentTitleID != -1 || list2.Count > 0) && requestAccepters.Count == 1)
				{
					keyValuePair = requestAccepters[0];
					if (keyValuePair.Value != userCurrentTitleID)
					{
						List<int> list3 = list2;
						keyValuePair = requestAccepters[0];
						if (!list3.Contains(keyValuePair.Value))
						{
							goto IL_020a;
						}
					}
					RequestAcceptMdl requestAcceptMdl = new RequestAcceptMdl();
					requestAcceptMdl.AcceptDate = DateTime.Now;
					requestAcceptMdl.AccepterBadgeNumber = badgeNumber;
					requestAcceptMdl.IsCancel = false;
					requestAcceptMdl.RequestID = requestID;
					List<int> list4 = list2;
					keyValuePair = requestAccepters[0];
					if (list4.Contains(keyValuePair.Value))
					{
						RequestAcceptMdl requestAcceptMdl2 = requestAcceptMdl;
						keyValuePair = requestAccepters[0];
						requestAcceptMdl2.AccepterTitleID = keyValuePair.Value;
						requestAcceptMdl.IsDeputy = true;
					}
					else
					{
						requestAcceptMdl.AccepterTitleID = userCurrentTitleID;
						requestAcceptMdl.IsDeputy = false;
					}
					string err = "";
					if (t5DBInstance.RequestAcceptDA.AddNew(requestAcceptMdl, out err))
					{
						List<KeyValuePair<long, int>> requestAccepters2 = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), requestID);
						if (requestAccepters2.Count == 1)
						{
							keyValuePair = requestAccepters2[0];
							if (keyValuePair.Value == 0 && !InsertData(requestID, out string err2))
							{
								return err2;
							}
						}
					}
				}
				goto IL_020a;
			}
			string text = EditRequestController.CheckRequestTime(req.ForDate);
			if (!(text == ""))
			{
				return text;
			}
			PersianCalendar pc = new PersianCalendar();
			LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(req.LeaveID);
			int year = pc.GetYear(req.ForDate);
			int month = pc.GetMonth(req.ForDate);
			bool flag = false;
			Attendance attendance = new Attendance(t5DBInstance, personnelSystem);
			DateTime fromDate = pc.ToDateTime(year, 1, 1, 0, 0, 0, 0);
			DateTime toDate = pc.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);
			List<ADayResult> list5 = attendance.Calculate(req.BadgeNumber, fromDate, toDate);
			List<LeaveType> all = t5DBInstance.LeaveTypeDA.GetAll();
			List<Schedule> all2 = t5DBInstance.ScheduleDA.GetAll();
			List<Paramtr> all3 = t5DBInstance.ParameterDA.GetAll();
			Remain remain = new Remain(list5, all, t5DBInstance.LeavesAddedDA.GetAll(new List<long>
			{
				req.BadgeNumber
			}, fromDate, toDate), t5DBInstance.YearRemainDA.GetAll(req.BadgeNumber), all2, all3);
			PersianCalendar persianCalendar = new PersianCalendar();
			RemainLeaveController remainLeaveController = new RemainLeaveController();
			DateTime dateTime = persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), 1, 1, 0, 0, 0, 0);
			DateTime dateTime2 = dateTime.AddDays(-1.0);
			remainLeaveController.GetRemain(badgeNumber, req.LeaveID, dateTime2.Date);
			List<ADayRemain> source = remain.CalculateRemain(personnelSystem.DB.UserInfos.GetByID(req.BadgeNumber), byID.LeaveTypeID, year);
			ADayRemain aDayRemain = (from rc in source
			where rc.ForDate == req.ForDate
			select rc).FirstOrDefault();
			if (aDayRemain != null && aDayRemain.Remain > 0.0 - byID.MaxMinus)
			{
				flag = true;
			}
			bool flag2 = false;
			double num = 0.0;
			int num2 = 0;
			DateTime dateTime3 = req.ForDate;
			while (dateTime3 <= req.ToTime)
			{
				if (HomeController.CalculateDailyAbsence(badgeNumber, dateTime3, byID.AutoInHolidays))
				{
					num2++;
				}
				dateTime3 = dateTime3.AddDays(1.0);
			}
			int i;
			for (i = 1; i < 13; i++)
			{
				double num3 = 0.0;
				foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(badgeNumber, pc.ToDateTime(year, i, 1, 0, 0, 0, 0), pc.ToDateTime(year, i, pc.GetDaysInMonth(year, i), 0, 0, 0, 0)))
				{
					new MyTime3.Models.Request().RequestT5 = item;
					if (item.IsSuspend && item.WorkflowType == WorkflowOperations.Leave)
					{
						if (item.LeaveID == item.LeaveID)
						{
							num3 += 1.0;
						}
						LeaveType leaveType = (from lt2 in all
						where lt2.LeaveTypeID == item.LeaveID
						select lt2).FirstOrDefault();
						if (leaveType != null)
						{
							if (!item.IsDaily)
							{
								TimeSpan timeSpan;
								if (leaveType.DecreaseFromLeaveID.HasValue && leaveType.DecreaseFromLeaveID.Value == item.LeaveID)
								{
									double num4 = num3;
									timeSpan = item.ToTime - item.FromTime;
									double totalHours = timeSpan.TotalHours;
									timeSpan = byID.EachXHoursADay;
									num3 = num4 + totalHours / timeSpan.TotalHours;
								}
								if (leaveType.LeaveTypeID == item.LeaveID)
								{
									double num5 = num3;
									timeSpan = item.ToTime - item.FromTime;
									double totalHours2 = timeSpan.TotalHours;
									timeSpan = byID.EachXHoursADay;
									num3 = num5 + totalHours2 / timeSpan.TotalHours;
								}
							}
							else if (leaveType.DecreaseFromLeaveID.HasValue && leaveType.DecreaseFromLeaveID.Value == byID.LeaveTypeID)
							{
								num3 += 1.0;
							}
						}
					}
				}
				TimeSpan t = new TimeSpan(0L);
				int daysCount = pc.GetDaysInMonth(year, i) + 1;
				int j;
				for (j = 1; j < daysCount; j++)
				{
					ADayResult aDayResult = (from ya in list5
					where ya.ForDate == pc.ToDateTime(year, i, j, 0, 0, 0, 0)
					select ya).First();
					if (aDayResult.IsDailyLeave && ((!aDayResult.IsHoliday && aDayResult.CurrentShift.ActiveSchedule != null) || byID.GetInHolidays))
					{
						string symbol = aDayResult.DailyLeaveValue.DailyLeaveType.Symbol;
						if (aDayResult.DailyLeaveValue.LeaveTypeID == byID.LeaveTypeID)
						{
							num3 += 1.0;
						}
					}
					if (aDayResult.ProcessedSchedule != null)
					{
						foreach (T5V3.Attendance.InShiftExit inShiftExit in aDayResult.ProcessedSchedule.InShiftExits)
						{
							if (inShiftExit.lvType != null)
							{
								if (inShiftExit.lvType.DecreaseFromLeaveID.HasValue && inShiftExit.lvType.DecreaseFromLeaveID.Value == byID.LeaveTypeID)
								{
									t += inShiftExit.ValidLen;
								}
								if (inShiftExit.lvType.LeaveTypeID == byID.LeaveTypeID)
								{
									t += inShiftExit.ValidLen;
								}
							}
						}
					}
				}
				double remain2 = source.FirstOrDefault((ADayRemain rc) => rc.ForDate == pc.ToDateTime(year, i, daysCount - 1, 0, 0, 0, 0)).Remain;
				num += num3;
				if (i == month && (byID.GetInType == LeaveGetInType.MonthFirst || byID.GetInType == LeaveGetInType.MonthLast) && remain2 + byID.MaxMinus - (double)num2 < 0.0)
				{
					try
					{
						flag2 = true;
					}
					catch
					{
					}
				}
			}
			double num6 = 0.0;
			dateTime = req.ToTime;
			int dayOfYear = dateTime.DayOfYear;
			dateTime = DateTime.Parse("2020-03-21");
			bool num7 = dayOfYear < dateTime.DayOfYear;
			dateTime = DateTime.Now;
			int dayOfYear2 = dateTime.DayOfYear;
			dateTime = DateTime.Parse("2020-03-21");
			bool num8 = dayOfYear2 > dateTime.DayOfYear;
			dateTime = req.ForDate;
			int dayOfYear3 = dateTime.DayOfYear;
			dateTime = DateTime.Parse("2020-03-21");
			num6 = ((!(num7 & (num8 & (dayOfYear3 < dateTime.DayOfYear)))) ? source.FirstOrDefault(delegate(ADayRemain rc)
			{
				DateTime dateTime4 = rc.ForDate;
				DateTime date2 = dateTime4.Date;
				dateTime4 = DateTime.Now;
				return date2 == dateTime4.Date;
			}).Remain : source.FirstOrDefault(delegate(ADayRemain rc)
			{
				DateTime forDate = rc.ForDate;
				DateTime date = forDate.Date;
				forDate = req.ForDate;
				return date == forDate.Date;
			}).Remain);
			if (num6 + byID.MaxMinus - (double)num2 < 0.0)
			{
				try
				{
					flag2 = true;
				}
				catch
				{
				}
			}
			if (flag2)
			{
				return PersianString.DailyLeaveLimit;
			}
			if (!flag)
			{
				return PersianString.RemainLeaveError;
			}
			int userCurrentTitleID3 = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(badgeNumber);
			List<long> list6 = t5DBInstance.PersonnelDeputyDA.IsDeputy(badgeNumber);
			List<int> list7 = new List<int>();
			enumerator = list6.GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					long current3 = enumerator.Current;
					int userCurrentTitleID4 = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(current3);
					if (userCurrentTitleID4 != -1)
					{
						list7.Add(userCurrentTitleID4);
					}
				}
			}
			finally
			{
				((IDisposable)enumerator).Dispose();
			}
			if ((userCurrentTitleID3 != -1 || list7.Count > 0) && requestAccepters.Count == 1)
			{
				keyValuePair = requestAccepters[0];
				if (keyValuePair.Value != userCurrentTitleID3)
				{
					List<int> list8 = list7;
					keyValuePair = requestAccepters[0];
					if (!list8.Contains(keyValuePair.Value))
					{
						goto IL_0bc6;
					}
				}
				RequestAcceptMdl requestAcceptMdl3 = new RequestAcceptMdl
				{
					AcceptDate = DateTime.Now,
					AccepterBadgeNumber = badgeNumber,
					IsCancel = false,
					RequestID = requestID
				};
				List<int> list9 = list7;
				keyValuePair = requestAccepters[0];
				if (list9.Contains(keyValuePair.Value))
				{
					RequestAcceptMdl requestAcceptMdl4 = requestAcceptMdl3;
					keyValuePair = requestAccepters[0];
					requestAcceptMdl4.AccepterTitleID = keyValuePair.Value;
					requestAcceptMdl3.IsDeputy = true;
				}
				else
				{
					requestAcceptMdl3.AccepterTitleID = userCurrentTitleID3;
					requestAcceptMdl3.IsDeputy = false;
				}
				string err3 = "";
				if (t5DBInstance.RequestAcceptDA.AddNew(requestAcceptMdl3, out err3))
				{
					List<KeyValuePair<long, int>> requestAccepters3 = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), requestID);
					if (requestAccepters3.Count == 1)
					{
						keyValuePair = requestAccepters3[0];
						if (keyValuePair.Value == 0 && !InsertData(requestID, out string err4))
						{
							return err4;
						}
					}
				}
			}
			goto IL_0bc6;
			IL_0bc6:
			return "0";
			IL_020a:
			return "0";
		}

		private string AcceptByID1(long badgeNumber, long requestID)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), requestID);
			T5V3.Model.Request req = t5DBInstance.RequestDA.GetByID(requestID);
			string text = EditRequestController.CheckRequestTime(req.ForDate);
			if (!(text == ""))
			{
				return text;
			}
			PersianCalendar pc = new PersianCalendar();
			LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(req.LeaveID);
			int year = pc.GetYear(req.ForDate);
			int month = pc.GetMonth(req.ForDate);
			bool flag = false;
			Attendance attendance = new Attendance(t5DBInstance, personnelSystem);
			DateTime fromDate = pc.ToDateTime(year, 1, 1, 0, 0, 0, 0);
			DateTime toDate = pc.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);
			List<ADayResult> list = attendance.Calculate(req.BadgeNumber, fromDate, toDate);
			List<LeaveType> all = t5DBInstance.LeaveTypeDA.GetAll();
			List<Schedule> all2 = t5DBInstance.ScheduleDA.GetAll();
			List<Paramtr> all3 = t5DBInstance.ParameterDA.GetAll();
			Remain remain = new Remain(list, all, t5DBInstance.LeavesAddedDA.GetAll(new List<long>
			{
				req.BadgeNumber
			}, fromDate, toDate), t5DBInstance.YearRemainDA.GetAll(req.BadgeNumber), all2, all3);
			PersianCalendar persianCalendar = new PersianCalendar();
			RemainLeaveController remainLeaveController = new RemainLeaveController();
			DateTime dateTime = persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), 1, 1, 0, 0, 0, 0);
			DateTime dateTime2 = dateTime.AddDays(-1.0);
			remainLeaveController.GetRemain(badgeNumber, req.LeaveID, dateTime2.Date);
			List<ADayRemain> source = remain.CalculateRemain(personnelSystem.DB.UserInfos.GetByID(req.BadgeNumber), byID.LeaveTypeID, year);
			ADayRemain aDayRemain = (from rc in source
			where rc.ForDate == req.ForDate
			select rc).FirstOrDefault();
			if (aDayRemain != null && aDayRemain.Remain > 0.0 - byID.MaxMinus)
			{
				flag = true;
			}
			bool flag2 = false;
			double num = 0.0;
			int num2 = 0;
			DateTime dateTime3 = req.ForDate;
			while (dateTime3 <= req.ToTime)
			{
				if (HomeController.CalculateDailyAbsence(badgeNumber, dateTime3, byID.AutoInHolidays))
				{
					num2++;
				}
				dateTime3 = dateTime3.AddDays(1.0);
			}
			int i;
			for (i = 1; i < 13; i++)
			{
				double num3 = 0.0;
				foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(badgeNumber, pc.ToDateTime(year, i, 1, 0, 0, 0, 0), pc.ToDateTime(year, i, pc.GetDaysInMonth(year, i), 0, 0, 0, 0)))
				{
					new MyTime3.Models.Request().RequestT5 = item;
					if (item.IsSuspend && item.WorkflowType == WorkflowOperations.Leave)
					{
						if (item.LeaveID == item.LeaveID)
						{
							num3 += 1.0;
						}
						LeaveType leaveType = (from lt2 in all
						where lt2.LeaveTypeID == item.LeaveID
						select lt2).FirstOrDefault();
						if (leaveType != null)
						{
							if (!item.IsDaily)
							{
								TimeSpan timeSpan;
								if (leaveType.DecreaseFromLeaveID.HasValue && leaveType.DecreaseFromLeaveID.Value == item.LeaveID)
								{
									double num4 = num3;
									timeSpan = item.ToTime - item.FromTime;
									double totalHours = timeSpan.TotalHours;
									timeSpan = byID.EachXHoursADay;
									num3 = num4 + totalHours / timeSpan.TotalHours;
								}
								if (leaveType.LeaveTypeID == item.LeaveID)
								{
									double num5 = num3;
									timeSpan = item.ToTime - item.FromTime;
									double totalHours2 = timeSpan.TotalHours;
									timeSpan = byID.EachXHoursADay;
									num3 = num5 + totalHours2 / timeSpan.TotalHours;
								}
							}
							else if (leaveType.DecreaseFromLeaveID.HasValue && leaveType.DecreaseFromLeaveID.Value == byID.LeaveTypeID)
							{
								num3 += 1.0;
							}
						}
					}
				}
				TimeSpan t = new TimeSpan(0L);
				int daysCount = pc.GetDaysInMonth(year, i) + 1;
				int j;
				for (j = 1; j < daysCount; j++)
				{
					ADayResult aDayResult = (from ya in list
					where ya.ForDate == pc.ToDateTime(year, i, j, 0, 0, 0, 0)
					select ya).First();
					if (aDayResult.IsDailyLeave && ((!aDayResult.IsHoliday && aDayResult.CurrentShift.ActiveSchedule != null) || byID.GetInHolidays))
					{
						string symbol = aDayResult.DailyLeaveValue.DailyLeaveType.Symbol;
						if (aDayResult.DailyLeaveValue.LeaveTypeID == byID.LeaveTypeID)
						{
							num3 += 1.0;
						}
					}
					if (aDayResult.ProcessedSchedule != null)
					{
						foreach (T5V3.Attendance.InShiftExit inShiftExit in aDayResult.ProcessedSchedule.InShiftExits)
						{
							if (inShiftExit.lvType != null)
							{
								if (inShiftExit.lvType.DecreaseFromLeaveID.HasValue && inShiftExit.lvType.DecreaseFromLeaveID.Value == byID.LeaveTypeID)
								{
									t += inShiftExit.ValidLen;
								}
								if (inShiftExit.lvType.LeaveTypeID == byID.LeaveTypeID)
								{
									t += inShiftExit.ValidLen;
								}
							}
						}
					}
				}
				double remain2 = source.FirstOrDefault((ADayRemain rc) => rc.ForDate == pc.ToDateTime(year, i, daysCount - 1, 0, 0, 0, 0)).Remain;
				num += num3;
				if (i == month && (byID.GetInType == LeaveGetInType.MonthFirst || byID.GetInType == LeaveGetInType.MonthLast) && remain2 + byID.MaxMinus - (double)num2 < 0.0)
				{
					try
					{
						flag2 = true;
					}
					catch
					{
					}
				}
			}
			double num6 = 0.0;
			dateTime = req.ToTime;
			int dayOfYear = dateTime.DayOfYear;
			dateTime = DateTime.Parse("2020-03-21");
			bool num7 = dayOfYear < dateTime.DayOfYear;
			dateTime = DateTime.Now;
			int dayOfYear2 = dateTime.DayOfYear;
			dateTime = DateTime.Parse("2020-03-21");
			bool num8 = dayOfYear2 > dateTime.DayOfYear;
			dateTime = req.ForDate;
			int dayOfYear3 = dateTime.DayOfYear;
			dateTime = DateTime.Parse("2020-03-21");
			num6 = ((!(num7 & (num8 & (dayOfYear3 < dateTime.DayOfYear)))) ? source.FirstOrDefault(delegate(ADayRemain rc)
			{
				DateTime dateTime4 = rc.ForDate;
				DateTime date2 = dateTime4.Date;
				dateTime4 = DateTime.Now;
				return date2 == dateTime4.Date;
			}).Remain : source.FirstOrDefault(delegate(ADayRemain rc)
			{
				DateTime forDate = rc.ForDate;
				DateTime date = forDate.Date;
				forDate = req.ForDate;
				return date == forDate.Date;
			}).Remain);
			if (num6 + byID.MaxMinus - (double)num2 < 0.0)
			{
				try
				{
					flag2 = true;
				}
				catch
				{
				}
			}
			if (flag2)
			{
				return PersianString.DailyLeaveLimit;
			}
			if (!flag)
			{
				return PersianString.RemainLeaveError;
			}
			int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(badgeNumber);
			List<long> list2 = t5DBInstance.PersonnelDeputyDA.IsDeputy(badgeNumber);
			List<int> list3 = new List<int>();
			foreach (long item2 in list2)
			{
				int userCurrentTitleID2 = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item2);
				if (userCurrentTitleID2 != -1)
				{
					list3.Add(userCurrentTitleID2);
				}
			}
			if ((userCurrentTitleID != -1 || list3.Count > 0) && requestAccepters.Count == 1)
			{
				KeyValuePair<long, int> keyValuePair = requestAccepters[0];
				if (keyValuePair.Value != userCurrentTitleID)
				{
					List<int> list4 = list3;
					keyValuePair = requestAccepters[0];
					if (!list4.Contains(keyValuePair.Value))
					{
						goto IL_0a07;
					}
				}
				RequestAcceptMdl requestAcceptMdl = new RequestAcceptMdl
				{
					AcceptDate = DateTime.Now,
					AccepterBadgeNumber = badgeNumber,
					IsCancel = false,
					RequestID = requestID
				};
				List<int> list5 = list3;
				keyValuePair = requestAccepters[0];
				if (list5.Contains(keyValuePair.Value))
				{
					RequestAcceptMdl requestAcceptMdl2 = requestAcceptMdl;
					keyValuePair = requestAccepters[0];
					requestAcceptMdl2.AccepterTitleID = keyValuePair.Value;
					requestAcceptMdl.IsDeputy = true;
				}
				else
				{
					requestAcceptMdl.AccepterTitleID = userCurrentTitleID;
					requestAcceptMdl.IsDeputy = false;
				}
				string err = "";
				if (t5DBInstance.RequestAcceptDA.AddNew(requestAcceptMdl, out err))
				{
					List<KeyValuePair<long, int>> requestAccepters2 = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), requestID);
					if (requestAccepters2.Count == 1)
					{
						keyValuePair = requestAccepters2[0];
						if (keyValuePair.Value == 0 && !InsertData(requestID, out string err2))
						{
							return err2;
						}
					}
				}
			}
			goto IL_0a07;
			IL_0a07:
			return "0";
		}

		private string AcceptByID(long badgeNumber, long requestID)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), requestID);
			int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(badgeNumber);
			List<long> list = t5DBInstance.PersonnelDeputyDA.IsDeputy(badgeNumber);
			List<int> list2 = new List<int>();
			foreach (long item in list)
			{
				int userCurrentTitleID2 = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
				if (userCurrentTitleID2 != -1)
				{
					list2.Add(userCurrentTitleID2);
				}
			}
			if ((userCurrentTitleID != -1 || list2.Count > 0) && requestAccepters.Count == 1)
			{
				KeyValuePair<long, int> keyValuePair = requestAccepters[0];
				if (keyValuePair.Value != userCurrentTitleID)
				{
					List<int> list3 = list2;
					keyValuePair = requestAccepters[0];
					if (!list3.Contains(keyValuePair.Value))
					{
						goto IL_01dd;
					}
				}
				RequestAcceptMdl requestAcceptMdl = new RequestAcceptMdl();
				requestAcceptMdl.AcceptDate = DateTime.Now;
				requestAcceptMdl.AccepterBadgeNumber = badgeNumber;
				requestAcceptMdl.IsCancel = false;
				requestAcceptMdl.RequestID = requestID;
				List<int> list4 = list2;
				keyValuePair = requestAccepters[0];
				if (list4.Contains(keyValuePair.Value))
				{
					RequestAcceptMdl requestAcceptMdl2 = requestAcceptMdl;
					keyValuePair = requestAccepters[0];
					requestAcceptMdl2.AccepterTitleID = keyValuePair.Value;
					requestAcceptMdl.IsDeputy = true;
				}
				else
				{
					requestAcceptMdl.AccepterTitleID = userCurrentTitleID;
					requestAcceptMdl.IsDeputy = false;
				}
				string err = "";
				if (t5DBInstance.RequestAcceptDA.AddNew(requestAcceptMdl, out err))
				{
					List<KeyValuePair<long, int>> requestAccepters2 = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), requestID);
					if (requestAccepters2.Count == 1)
					{
						keyValuePair = requestAccepters2[0];
						if (keyValuePair.Value == 0 && !InsertData(requestID, out string err2))
						{
							return err2;
						}
					}
				}
			}
			goto IL_01dd;
			IL_01dd:
			return "0";
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Accept(long requestID, string editedValue, bool? returnToHome = default(bool?), long forBadgeNumberFilter = -1L)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			TimeSpan result = default(TimeSpan);
			if (TimeSpan.TryParse(editedValue, out result))
			{
				List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), requestID);
				int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
				List<long> list = t5DBInstance.PersonnelDeputyDA.IsDeputy(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext));
				List<int> list2 = new List<int>();
				foreach (long item in list)
				{
					int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
					if (userCurrentTitleID != -1)
					{
						list2.Add(userCurrentTitleID);
					}
				}
				if ((loggedInUserInfoTitleID != -1 || list2.Count > 0) && requestAccepters.Count == 1)
				{
					KeyValuePair<long, int> keyValuePair = requestAccepters[0];
					if (keyValuePair.Value != loggedInUserInfoTitleID)
					{
						List<int> list3 = list2;
						keyValuePair = requestAccepters[0];
						if (!list3.Contains(keyValuePair.Value))
						{
							goto IL_023d;
						}
					}
					RequestAcceptMdl requestAcceptMdl = new RequestAcceptMdl();
					requestAcceptMdl.AcceptDate = DateTime.Now;
					requestAcceptMdl.AccepterBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
					requestAcceptMdl.IsCancel = false;
					requestAcceptMdl.RequestID = requestID;
					List<int> list4 = list2;
					keyValuePair = requestAccepters[0];
					if (list4.Contains(keyValuePair.Value))
					{
						RequestAcceptMdl requestAcceptMdl2 = requestAcceptMdl;
						keyValuePair = requestAccepters[0];
						requestAcceptMdl2.AccepterTitleID = keyValuePair.Value;
						requestAcceptMdl.IsDeputy = true;
					}
					else
					{
						requestAcceptMdl.AccepterTitleID = loggedInUserInfoTitleID;
						requestAcceptMdl.IsDeputy = false;
					}
					string err = "";
					if (t5DBInstance.RequestAcceptDA.AddNew(requestAcceptMdl, out err))
					{
						T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(requestID);
						byID.AllowValue = result;
						t5DBInstance.RequestDA.Edit(byID, out err);
						List<KeyValuePair<long, int>> requestAccepters2 = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), requestID);
						if (requestAccepters2.Count == 1)
						{
							keyValuePair = requestAccepters2[0];
							if (keyValuePair.Value == 0 && !InsertData(requestID, out string err2))
							{
								return View("Error", "", err2);
							}
						}
					}
				}
			}
			goto IL_023d;
			IL_023d:
			if (returnToHome.HasValue)
			{
				return RedirectToAction("Index", "Home");
			}
			if (forBadgeNumberFilter != -1)
			{
				return RedirectToAction("Index", (object)new
				{
					forBadgeNumberFilter = forBadgeNumberFilter,
					fromHome = false
				});
			}
			return RedirectToAction("Index");
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult AcceptAll(string action, string action2, string[] allOverIDs, string[] requestsChecked, string[] overValues, bool returnToHome = false, long forBadgeNumberFilter = -1L)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
			List<long> list = t5DBInstance.PersonnelDeputyDA.IsDeputy(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext));
			List<int> list2 = new List<int>();
			foreach (long item in list)
			{
				int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
				if (userCurrentTitleID != -1)
				{
					list2.Add(userCurrentTitleID);
				}
			}
			if (requestsChecked != null)
			{
				List<T5V3.Model.Request> list3 = new List<T5V3.Model.Request>();
				for (int i = 0; i < requestsChecked.Length; i++)
				{
					if (long.TryParse(requestsChecked[i], out long result))
					{
						list3.Add(t5DBInstance.RequestDA.GetByID(result));
					}
				}
				if (action2 == "0" || action == "تایید انتخاب شده ها")
				{
					foreach (T5V3.Model.Request item2 in from r in list3
					orderby r.ForDate
					select r)
					{
						DateTime dateTime = DateTime.Now;
						int year = dateTime.Year;
						DateTime t = new DateTime(year, 3, 19);
						dateTime = item2.ForDate;
						int year2 = dateTime.Year;
						dateTime = DateTime.Now;
						if (year2 < dateTime.Year || item2.ForDate < t)
						{
							AcceptByID(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), item2.RequestID);
						}
						else
						{
							AcceptByID2(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), item2.RequestID);
						}
					}
				}
				else
				{
					foreach (T5V3.Model.Request item3 in from r in list3
					orderby r.ForDate
					select r)
					{
						CancelByID(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), item3.RequestID);
					}
				}
			}
			if (returnToHome)
			{
				return RedirectToAction("Index", "Home");
			}
			if (forBadgeNumberFilter != -1)
			{
				return RedirectToAction("Index", (object)new
				{
					forBadgeNumberFilter = forBadgeNumberFilter,
					fromHome = false
				});
			}
			return RedirectToAction("Index");
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Cancel(long requestID, bool? returnToHome = default(bool?))
		{
			string text = CancelByID(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), requestID);
			if (text != "0")
			{
				return View("Error", "", text);
			}
			if (returnToHome.HasValue)
			{
				return RedirectToAction("Index", "Home");
			}
			return RedirectToAction("Index");
		}

		private string CancelByID(long badgeNumber, long requestID)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), requestID);
			int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(badgeNumber);
			List<long> list = t5DBInstance.PersonnelDeputyDA.IsDeputy(badgeNumber);
			List<int> list2 = new List<int>();
			foreach (long item in list)
			{
				int userCurrentTitleID2 = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
				if (userCurrentTitleID2 != -1)
				{
					list2.Add(userCurrentTitleID2);
				}
			}
			if ((userCurrentTitleID != -1 || list2.Count > 0) && requestAccepters.Count == 1)
			{
				KeyValuePair<long, int> keyValuePair = requestAccepters[0];
				if (keyValuePair.Value != userCurrentTitleID)
				{
					List<int> list3 = list2;
					keyValuePair = requestAccepters[0];
					if (!list3.Contains(keyValuePair.Value))
					{
						goto IL_01b8;
					}
				}
				RequestAcceptMdl requestAcceptMdl = new RequestAcceptMdl();
				requestAcceptMdl.AcceptDate = DateTime.Now;
				requestAcceptMdl.AccepterBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
				requestAcceptMdl.IsCancel = true;
				requestAcceptMdl.RequestID = requestID;
				List<int> list4 = list2;
				keyValuePair = requestAccepters[0];
				if (list4.Contains(keyValuePair.Value))
				{
					RequestAcceptMdl requestAcceptMdl2 = requestAcceptMdl;
					keyValuePair = requestAccepters[0];
					requestAcceptMdl2.AccepterTitleID = keyValuePair.Value;
					requestAcceptMdl.IsDeputy = true;
				}
				else
				{
					requestAcceptMdl.AccepterTitleID = userCurrentTitleID;
					requestAcceptMdl.IsDeputy = false;
				}
				string err = "";
				if (t5DBInstance.RequestAcceptDA.AddNew(requestAcceptMdl, out err))
				{
					T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(requestID);
					byID.IsSuspend = false;
					if (!t5DBInstance.RequestDA.Edit(byID, out err))
					{
						return err;
					}
				}
			}
			goto IL_01b8;
			IL_01b8:
			return "0";
		}

		[HttpPost]
		public JsonResult AcceptAllRequests(string userName, string password, string requestIDs)
		{
			bool flag = true;
			long num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				string[] array = requestIDs.Split(new char[1]
				{
					';'
				}, StringSplitOptions.RemoveEmptyEntries);
				foreach (string s in array)
				{
					if (AcceptByID2(num, long.Parse(s)) != "0")
					{
						flag = false;
					}
				}
			}
			return Json(new
			{
				result = (flag ? "0" : "-1")
			});
		}

		[HttpPost]
		public JsonResult CancelAllRequests(string userName, string password, string requestIDs)
		{
			bool flag = true;
			long num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				string[] array = requestIDs.Split(new char[1]
				{
					';'
				}, StringSplitOptions.RemoveEmptyEntries);
				foreach (string s in array)
				{
					if (CancelByID(num, long.Parse(s)) != "0")
					{
						flag = false;
					}
				}
			}
			return Json(new
			{
				result = (flag ? "0" : "-1")
			});
		}

		private bool InsertData(long requestID, out string err)
		{
			DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			err = "";
			bool flag = false;
			T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(requestID);
			t5DBInstance.LeaveTypeDA.GetByID(byID.LeaveID);
			DateTime dateTime;
			if (byID.WorkflowType == WorkflowOperations.Duty || byID.WorkflowType == WorkflowOperations.Leave)
			{
				if (byID.IsDaily)
				{
					dateTime = byID.ForDate;
					DateTime dateTime2 = dateTime.Date;
					while (true)
					{
						DateTime t = dateTime2;
						dateTime = byID.ToTime;
						if (!(t <= dateTime.Date))
						{
							break;
						}
						DailyLeave dailyLeave = new DailyLeave();
						dailyLeave.BadgeNumber = byID.BadgeNumber;
						dailyLeave.Descriptions = byID.Descriptions;
						dailyLeave.DetailDescriptionID = byID.LeaveDescriptionID;
						dailyLeave.LeaveDate = dateTime2;
						dailyLeave.DocumentNumber = byID.RequestID.ToString();
						dailyLeave.LeaveTypeID = byID.LeaveID;
						flag = t5DBInstance.DailyLeaveDA.AddNew(dailyLeave, out err);
						dateTime2 = dateTime2.AddDays(1.0);
					}
				}
				else
				{
					HourlyLeave hourlyLeave = new HourlyLeave();
					hourlyLeave.BadgeNumber = byID.BadgeNumber;
					hourlyLeave.Descriptions = byID.Descriptions;
					hourlyLeave.LeaveTypeID = byID.LeaveID;
					hourlyLeave.DetailDescriptions = new LeaveDescription();
					hourlyLeave.DetailDescriptions.ID = byID.LeaveDescriptionID;
					HourlyLeave hourlyLeave2 = hourlyLeave;
					dateTime = byID.ForDate;
					dateTime = dateTime.Date;
					DateTime dateTime3 = byID.FromTime;
					hourlyLeave2.FromTime = dateTime.Add(dateTime3.TimeOfDay);
					dateTime = byID.ToTime;
					TimeSpan timeOfDay = dateTime.TimeOfDay;
					dateTime = byID.FromTime;
					if (timeOfDay < dateTime.TimeOfDay)
					{
						HourlyLeave hourlyLeave3 = hourlyLeave;
						dateTime = byID.ForDate;
						dateTime = dateTime.Date;
						dateTime3 = byID.ToTime;
						dateTime = dateTime.Add(dateTime3.TimeOfDay);
						hourlyLeave3.ToTime = dateTime.AddDays(1.0);
					}
					else
					{
						HourlyLeave hourlyLeave4 = hourlyLeave;
						dateTime = byID.ForDate;
						dateTime = dateTime.Date;
						dateTime3 = byID.ToTime;
						hourlyLeave4.ToTime = dateTime.Add(dateTime3.TimeOfDay);
					}
					flag = t5DBInstance.HourlyLeaveDA.AddNew(hourlyLeave, true, out err, out int _);
				}
			}
			if (byID.WorkflowType == WorkflowOperations.LateEarly)
			{
				EditedValue editedValue = new EditedValue();
				editedValue.BadgeNumber = byID.BadgeNumber;
				EditedValue editedValue2 = editedValue;
				dateTime = byID.ForDate;
				editedValue2.ForDate = dateTime.Date;
				editedValue.ValidDuration = new TimeSpan(0L);
				editedValue.ValueType = ((byID.OverTimeAllowType == AllowType.Early) ? EditValueType.Early : EditValueType.Late);
				flag = t5DBInstance.EditedValueDA.InsertOrEdit(editedValue, out err);
			}
			if (byID.WorkflowType == WorkflowOperations.RewardAndPenalty)
			{
				EditedValue editedValue3 = new EditedValue();
				editedValue3.BadgeNumber = byID.BadgeNumber;
				EditedValue editedValue4 = editedValue3;
				dateTime = byID.ForDate;
				editedValue4.ForDate = dateTime.Date;
				editedValue3.ValidDuration = byID.AllowValue;
				editedValue3.ValueType = ((byID.OverTimeAllowType == AllowType.Early) ? EditValueType.Penalty : EditValueType.Reward);
				flag = t5DBInstance.EditedValueDA.InsertOrEdit(editedValue3, out err);
			}
			if (byID.WorkflowType == WorkflowOperations.AllowOverTimes)
			{
				long badgeNumber = byID.BadgeNumber;
				dateTime = byID.ForDate;
				AllowedValue allowValue = new AllowedValue(badgeNumber, dateTime.Date, byID.OverTimeAllowType, byID.AllowValue);
				flag = t5DBInstance.AllowValueDA.AddOrEdit(allowValue);
			}
			if (byID.WorkflowType == WorkflowOperations.InsertRecord)
			{
				T5V3.Model.Record record = new T5V3.Model.Record();
				record.BadgeNumber = byID.BadgeNumber;
				record.CheckTime = byID.RecordTime;
				record.CheckType = 0;
				record.InsertTime = DateTime.Now;
				record.InsertUser = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
				record.IsValid = true;
				record.MachineNumber = -1;
				record.OriginalSCheckType = 0;
				record.SourceType = T5V3.Model.RecordSource.Manual;
				record.VerifyMode = T5V3.Model.RecordVerifyStyle.FP_OR_PW_OR_RF;
				record.WorkCode = 0;
				flag = t5DBInstance.RecordDA.Insert(record, out err);
			}
			if (flag)
			{
				byID.IsSuspend = false;
				flag = t5DBInstance.RequestDA.Edit(byID, out err);
			}
			return flag;
		}
	}
}
