using MyTime3.Authorization;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.DB.EntityManagers;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class HomeController : Controller
	{
		public int AccseptRequestCount
		{
			get;
			set;
		}

		public int RejectRequestCount
		{
			get;
			set;
		}

		public int WaitingRequestCount
		{
			get;
			set;
		}

		public int AllRequestCount
		{
			get;
			set;
		}

		public int PendingRequestCount
		{
			get;
			set;
		}

		public int AcceptRequestCount
		{
			get;
			set;
		}

		public int SuspendRequestCount
		{
			get;
			set;
		}

		public static long BadgNumber
		{
			get;
			set;
		}

		[HttpPost]
		public JsonResult CheckLock()
		{
			bool flag = false;
			flag = GlobalHelper.WithTimeApp();
			return Json(new
			{
				LockResult = flag
			});
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index(int sort = -1, bool g = true)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			HomeModel homeModel = new HomeModel();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			if (loggedInBadgeNumber != -1)
			{
				BadgNumber = loggedInBadgeNumber;
				Deputy deputy = new Deputy();
				long deputy2 = t5DBInstance.PersonnelDeputyDA.GetDeputy(loggedInBadgeNumber);
				if (deputy2 != -1)
				{
					deputy.IsAnyDeputy = true;
					deputy.CurrentDeputy = personnelSystem.DB.UserInfos.GetByID(deputy2);
				}
				else
				{
					deputy.IsAnyDeputy = false;
				}
				homeModel.Deputy = deputy;
				HomeModel homeModel2 = homeModel;
				long badge = loggedInBadgeNumber;
				DateTime dateTime = DateTime.Now;
				dateTime = dateTime.Date;
				DateTime fromDate = dateTime.AddDays((double)(-ForNDayReport()));
				dateTime = DateTime.Now;
				homeModel2.Reports = CalculateDailyDetailsReport(badge, fromDate, dateTime.Date);
				List<MyTime3.Models.Request> list = homeModel.Requests = GetRequestsFor(t5DBInstance, personnelSystem, loggedInBadgeNumber);
			}
			List<int> validIDs = t5DBInstance.EmpLeavesDA.GetAll(GlobalHelper.GetLoggedInUserInfo(base.HttpContext).EmpTypeID);
			base.ViewBag.LeaveTypes = (from l in t5DBInstance.LeaveTypeDA.GetAll()
			where validIDs.Contains(l.LeaveTypeID)
			select l).ToList();
			base.ViewBag.LeaveDescriptions = t5DBInstance.LeaveDescriptionDA.GetAll();
			base.ViewBag.Personnel = RequestController.AddPersonnelToViewBag(base.HttpContext);
			base.ViewBag.SortByDate = sort;
			base.ViewBag.GroupByName = g;
			int userCurrentTitleID = DBInitializer.GetPersonnelSystem().DB.UserInfos.GetUserCurrentTitleID(loggedInBadgeNumber);
			TitleChartClass.user = TitleChartClass.GetUserListInfoById(loggedInBadgeNumber).FirstOrDefault();
			TitleChartModel titleChartById = TitleChartClass.GetTitleChartById(userCurrentTitleID);
			if (titleChartById != null)
			{
				if (titleChartById.UpperTitleID != -1)
				{
					TitleChartClass.user.UpperTitleChartName = TitleChartClass.GetTitleChartById(titleChartById.UpperTitleID).TitleName;
				}
				TitleChartClass.user.TitleChartName = titleChartById.TitleName;
			}
			return View(homeModel);
		}

		public static bool IsAdmin(HttpContextBase httpContext)
		{
			if (TitleChartClass.GetTitleChartById(DBInitializer.GetPersonnelSystem().DB.UserInfos.GetUserCurrentTitleID(GlobalHelper.GetLoggedInBadgeNumber(httpContext))).UpperTitleID == -1)
			{
				return true;
			}
			return false;
		}

		public void MakeCount()
		{
			try
			{
				List<MyTime3.Models.Request> list = new List<MyTime3.Models.Request>();
				List<RequestAcceptMdl> list2 = new List<RequestAcceptMdl>();
				List<MyTime3.Models.Request> list3 = new List<MyTime3.Models.Request>();
				long badgNumber = BadgNumber;
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
				RequestDA requestDA = t5DBInstance.RequestDA;
				long badgeNumber = badgNumber;
				DateTime dateTime = DateTime.Now;
				dateTime = dateTime.Date;
				DateTime fromDate = dateTime.AddDays((double)(-ForNDayReport()));
				dateTime = DateTime.Now;
				List<T5V3.Model.Request> all = requestDA.GetAll(badgeNumber, fromDate, dateTime.Date);
				foreach (T5V3.Model.Request item in all)
				{
					if (t5DBInstance.RequestAcceptDA.IsEditableRequest(item.RequestID))
					{
						MyTime3.Models.Request request = new MyTime3.Models.Request();
						request.RequestT5 = item;
						list.Add(request);
					}
					list2.AddRange(t5DBInstance.RequestAcceptDA.GetByID(item.RequestID));
					List<Personnel.Model.TitleChart> all2 = personnelSystem.DB.TitlesCharts.GetAll();
					foreach (var item2 in from acc in t5DBInstance.RequestAcceptDA.GetByID(item.RequestID)
					join tl in all2 on acc.AccepterTitleID equals tl.TitleID
					select new
					{
						tl.TitleName,
						acc.IsCancel
					})
					{
						if (item2.IsCancel)
						{
							MyTime3.Models.Request request2 = new MyTime3.Models.Request();
							request2.RequestT5 = item;
							list3.Add(request2);
						}
					}
				}
				AllRequestCount = all.Count();
				PendingRequestCount = list.Count();
				AcceptRequestCount = list2.Count();
				SuspendRequestCount = list3.Count();
			}
			catch (Exception)
			{
				AllRequestCount = 0;
			}
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

		[AuthorizeFilter(Roles = "admin,user")]
		public string GetTableData(long? b)
		{
			DBInitializer.GetT5DBInstance();
			DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			if (loggedInBadgeNumber != -1)
			{
				DateTime dateTime;
				if (!b.HasValue)
				{
					long badge = loggedInBadgeNumber;
					dateTime = DateTime.Now;
					dateTime = dateTime.Date;
					DateTime fromDate = dateTime.AddDays((double)(-ForNDayReport()));
					dateTime = DateTime.Now;
					return RenderRazorViewToString("_HomeStatus", CalculateDailyDetailsReport(badge, fromDate, dateTime.Date));
				}
				if ((from p in RequestController.AddPersonnelToViewBag(base.HttpContext)
				where p.BadgeNumber == b.Value
				select p).Count() > 0)
				{
					long value = b.Value;
					dateTime = DateTime.Now;
					dateTime = dateTime.Date;
					DateTime fromDate2 = dateTime.AddDays((double)(-ForNDayReport()));
					dateTime = DateTime.Now;
					return RenderRazorViewToString("_HomeStatus", CalculateDailyDetailsReport(value, fromDate2, dateTime.Date));
				}
			}
			return null;
		}

		[HttpPost]
		public JsonResult GetDailyDetailsReport(string userName, string password, string fromDays, string daysLen)
		{
			if (!GlobalHelper.WithTimeApp())
			{
				return null;
			}
			List<ADayReport> data = new List<ADayReport>();
			long num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				int result = 0;
				int result2 = 0;
				if (int.TryParse(fromDays, out result) && int.TryParse(daysLen, out result2))
				{
					long badge = num;
					DateTime dateTime = DateTime.Now;
					dateTime = dateTime.AddDays((double)(-result));
					DateTime date = dateTime.Date;
					dateTime = DateTime.Now;
					dateTime = dateTime.AddDays((double)(-result));
					dateTime = dateTime.AddDays((double)result2);
					data = CalculateDailyDetailsReport(badge, date, dateTime.Date);
				}
			}
			return Json(data);
		}

		[HttpPost]
		public JsonResult GetPositionsList(string userName, string password)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			long num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				int lastOpenPositionID = t5DBInstance.UserPositionDataDA.GetLastOpenPositionID(num);
				List<Position> all = t5DBInstance.PositionDA.GetAll();
				if (lastOpenPositionID == -1)
				{
					for (int i = 0; i < all.Count; i++)
					{
						if (all[i].Steps.Count > 1)
						{
							int j;
							for (j = 0; j < all[i].Steps.Count && !all[i].Steps[j].Required; j++)
							{
							}
							all[i].Steps.RemoveRange(j + 1, all[i].Steps.Count - j - 1);
						}
					}
					return Json(all);
				}
				int possID = 1;
				if (t5DBInstance.UserPositionDataDA.GetByID(lastOpenPositionID) != null)
				{
					possID = t5DBInstance.UserPositionDataDA.GetByID(lastOpenPositionID).PositionID;
				}
				Position position = all.FirstOrDefault((Position p) => p.PositionID == possID);
				if (position != null)
				{
					if (position.Steps.Count > 1)
					{
						position.Steps.RemoveRange(0, t5DBInstance.UserPositionDataDA.GetLastStepIndex(lastOpenPositionID));
						int k;
						for (k = 0; k < position.Steps.Count && !position.Steps[k].Required; k++)
						{
						}
						position.Steps.RemoveRange(k + 1, position.Steps.Count - k - 1);
					}
					return Json(new List<Position>
					{
						position
					});
				}
			}
			return null;
		}

		[HttpPost]
		public JsonResult GetStatusList(string userName, string password)
		{
			if (!GlobalHelper.WithTimeApp())
			{
				return null;
			}
			List<string> list = new List<string>();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(num);
				list.Add($"{(byID.IsFemale ? PersianString.Mrs : PersianString.Mr)} {byID.ToString()} {PersianString.Welcome}");
			}
			return Json(list);
		}

		[HttpPost]
		public JsonResult GetValidLeaveTypes(string userName, string password)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(num);
				List<int> validLeaveIDs = t5DBInstance.EmpLeavesDA.GetAll(byID.EmpTypeID);
				List<LeaveType> leaveTypes = (from l in t5DBInstance.LeaveTypeDA.GetAll()
				where validLeaveIDs.Contains(l.LeaveTypeID)
				select l).ToList();
				List<LeaveDescription> all = t5DBInstance.LeaveDescriptionDA.GetAll();
				List<AppRuleModel> appRules = new List<AppRuleModel>();
				Rule rule = GlobalHelper.GetRule(num, DateTime.Now);
				if (rule != null)
				{
					appRules = rule.AppRules;
				}
				return Json(new
				{
					leaveTypes = leaveTypes,
					leaveDescriptions = all,
					appRules = appRules
				});
			}
			return null;
		}

		public static bool CalculateDailyAbsence(long badge, DateTime forDate, bool calHoliday = false)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			List<ADayResult> source = new Attendance(t5DBInstance, personnelSystem).Calculate(badge, forDate, forDate);
			bool absent = source.FirstOrDefault().ProcessedSchedule.Absent;
			return source.FirstOrDefault().ProcessedSchedule.Absent;
		}

		public static List<ADayReport> CalculateDailyDetailsReport(long badge, DateTime fromDate, DateTime toDate)
		{
			List<ADayReport> list = new List<ADayReport>();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			List<LeaveType> all = t5DBInstance.LeaveTypeDA.GetAll();
			List<ADayResult> list2 = new Attendance(t5DBInstance, personnelSystem).Calculate(badge, fromDate, toDate);
			List<T5V3.Model.Request> all2 = t5DBInstance.RequestDA.GetAll(badge, true);
			HourlyLeaveDA hourlyLeaveDA = t5DBInstance.HourlyLeaveDA;
			List<long> badgeNumbers = new List<long>
			{
				badge
			};
			DateTime d = DateTime.Now;
			d = d.Date;
			DateTime fromDate2 = d.AddDays(-40.0);
			d = DateTime.Now;
			d = d.Date;
			List<HourlyLeave> all3 = hourlyLeaveDA.GetAll(badgeNumbers, fromDate2, d.AddDays(2.0));
			foreach (ADayResult item in list2)
			{
				ADayReport aDayReport = new ADayReport();
				T5V3.Model.Request getDailyQ = all2.FirstOrDefault(delegate(T5V3.Model.Request r)
				{
					if (r.ForDate == item.ForDate && (r.WorkflowType == WorkflowOperations.Duty || r.WorkflowType == WorkflowOperations.Leave))
					{
						return r.IsDaily;
					}
					return false;
				});
				if (getDailyQ != null)
				{
					aDayReport.IsAbsentRequested = true;
					aDayReport.AbsentRequestedDescriptions = $"{PersianString.Request} {(from l in all
					where l.LeaveTypeID == getDailyQ.LeaveID
					select l).First().ToString()}";
				}
				aDayReport.WoIn = false;
				aDayReport.WoOut = false;
				aDayReport.IsHoliday = item.IsHoliday;
				ADayReport aDayReport2 = aDayReport;
				TimeSpan timeSpan = item.ProcessedSchedule.EarlyLast;
				aDayReport2.Early = timeSpan.ToString("hh\\:mm");
				ADayReport aDayReport3 = aDayReport;
				timeSpan = item.ProcessedSchedule.LateLast;
				aDayReport3.Late = timeSpan.ToString("hh\\:mm");
				if (all2.FirstOrDefault(delegate(T5V3.Model.Request r)
				{
					if (r.ForDate == item.ForDate && r.OverTimeAllowType == AllowType.Late)
					{
						return r.WorkflowType == WorkflowOperations.LateEarly;
					}
					return false;
				}) != null)
				{
					aDayReport.IsLateRequested = true;
					aDayReport.LateRequestedDescriptions = PersianString.Requested;
				}
				if (all2.FirstOrDefault(delegate(T5V3.Model.Request r)
				{
					if (r.ForDate == item.ForDate && r.OverTimeAllowType == AllowType.Early)
					{
						return r.WorkflowType == WorkflowOperations.LateEarly;
					}
					return false;
				}) != null)
				{
					aDayReport.IsEarlyRequested = true;
					aDayReport.EarlyRequestedDescriptions = PersianString.Requested;
				}
				aDayReport.HaveEarly = (item.ProcessedSchedule.EarlyLast > TimeSpan.FromMinutes(1.0));
				aDayReport.HaveLate = (item.ProcessedSchedule.LateLast > TimeSpan.FromMinutes(1.0));
				aDayReport.HijriDate = DateConverter.ToHijriDate(item.ForDate);
				ADayReport aDayReport4 = aDayReport;
				d = item.ForDate;
				aDayReport4.DayOfWeekName = Misc.DayOfWeekToString(d.DayOfWeek);
				aDayReport.IsAbsent = item.ProcessedSchedule.Absent;
				DateTime mainInTime = item.ProcessedSchedule.MainInTime;
				d = default(DateTime);
				if (mainInTime != d)
				{
					ADayReport aDayReport5 = aDayReport;
					d = item.ProcessedSchedule.MainInTime;
					aDayReport5.MainInTime = d.ToString("HH:mm");
				}
				else if (item.ProcessedSchedule.InRecordInLeave != null)
				{
					ADayReport aDayReport6 = aDayReport;
					string leaveName = item.ProcessedSchedule.InRecordInLeave.lvType.LeaveName;
					d = item.ProcessedSchedule.InRecordInLeave.FromTime;
					string arg = d.ToString("HH:mm");
					d = item.ProcessedSchedule.InRecordInLeave.ToTime;
					aDayReport6.MainInTime = string.Format("{0} {1}-{2}", leaveName, arg, d.ToString("HH:mm"));
				}
				else
				{
					aDayReport.MainInTime = "-";
				}
				DateTime mainOutTime = item.ProcessedSchedule.MainOutTime;
				d = default(DateTime);
				if (mainOutTime != d)
				{
					ADayReport aDayReport7 = aDayReport;
					d = item.ProcessedSchedule.MainOutTime;
					aDayReport7.MainOutTime = d.ToString("HH:mm");
					ADayReport aDayReport8 = aDayReport;
					d = item.ProcessedSchedule.MainOutTime;
					aDayReport8.MainOutTimeDate = DateConverter.ToHijriDate(d.Date);
				}
				else if (item.ProcessedSchedule.OutRecordInLeave != null)
				{
					ADayReport aDayReport9 = aDayReport;
					string leaveName2 = item.ProcessedSchedule.OutRecordInLeave.lvType.LeaveName;
					d = item.ProcessedSchedule.OutRecordInLeave.FromTime;
					string arg2 = d.ToString("HH:mm");
					d = item.ProcessedSchedule.OutRecordInLeave.ToTime;
					aDayReport9.MainOutTime = string.Format("{0} {1}-{2}", leaveName2, arg2, d.ToString("HH:mm"));
				}
				else
				{
					aDayReport.MainOutTime = "-";
				}
				if (!item.IsDailyLeave)
				{
					aDayReport.IsDailyLeave = false;
				}
				else
				{
					aDayReport.DailyLeaveName = item.DailyLeaveValue.DailyLeaveType.LeaveName;
					aDayReport.IsDailyLeave = true;
				}
				DateTime dateTime;
				if (item.CurrentShift.ActiveSchedule != null)
				{
					d = item.ForDate;
					d = d.Date;
					dateTime = item.CurrentShift.ActiveSchedule.FromTime;
					DateTime inRecordTime = d.Add(dateTime.TimeOfDay);
					d = item.ForDate;
					d = d.Date;
					dateTime = item.CurrentShift.ActiveSchedule.ToTime;
					DateTime outRecordTime = d.Add(dateTime.TimeOfDay);
					T5V3.Model.Request getLateLeaveQ = all2.FirstOrDefault(delegate(T5V3.Model.Request r)
					{
						if (r.ForDate == item.ForDate && r.FromTime.TimeOfDay == inRecordTime.TimeOfDay)
						{
							if (r.WorkflowType != WorkflowOperations.Duty)
							{
								return r.WorkflowType == WorkflowOperations.Leave;
							}
							return true;
						}
						return false;
					});
					if (getLateLeaveQ != null)
					{
						aDayReport.IsLateRequested = true;
						aDayReport.LateRequestedDescriptions = $"{PersianString.Request} {all.FirstOrDefault((LeaveType l) => l.LeaveTypeID == getLateLeaveQ.LeaveID).ToString()}";
					}
					T5V3.Model.Request getEarlyLeaveQ = all2.FirstOrDefault(delegate(T5V3.Model.Request r)
					{
						if (r.ForDate == item.ForDate && r.ToTime.TimeOfDay == outRecordTime.TimeOfDay)
						{
							if (r.WorkflowType != WorkflowOperations.Duty)
							{
								return r.WorkflowType == WorkflowOperations.Leave;
							}
							return true;
						}
						return false;
					});
					if (getEarlyLeaveQ != null)
					{
						aDayReport.IsEarlyRequested = true;
						aDayReport.EarlyRequestedDescriptions = $"{PersianString.Request} {all.FirstOrDefault((LeaveType l) => l.LeaveTypeID == getEarlyLeaveQ.LeaveID).ToString()}";
					}
					ScheduleTimeIntervals times = ShiftProcessor.GetRealTimes(item.CurrentShift);
					aDayReport.WOShift = false;
					ADayReport aDayReport10 = aDayReport;
					d = item.CurrentShift.ActiveSchedule.FromTime;
					aDayReport10.SchInTime = d.ToString("HH:mm");
					ADayReport aDayReport11 = aDayReport;
					d = item.CurrentShift.ActiveSchedule.ToTime;
					aDayReport11.SchOutTime = d.ToString("HH:mm");
					T5V3.Model.Request request = all2.FirstOrDefault(delegate(T5V3.Model.Request r)
					{
						if (r.RecordTime >= times.InInterval.FromTime && r.RecordTime <= times.InInterval.ToTime)
						{
							return r.WorkflowType == WorkflowOperations.InsertRecord;
						}
						return false;
					});
					T5V3.Model.Request request2 = all2.FirstOrDefault(delegate(T5V3.Model.Request r)
					{
						if (r.RecordTime >= times.OutInterval.FromTime && r.RecordTime <= times.OutInterval.ToTime)
						{
							return r.WorkflowType == WorkflowOperations.InsertRecord;
						}
						return false;
					});
					if (request != null)
					{
						aDayReport.IsWOInRequested = true;
					}
					if (request2 != null)
					{
						aDayReport.IsWOOutRequested = true;
					}
					if (!item.IsDailyLeave && !item.IsHoliday)
					{
						aDayReport.WoIn = item.ProcessedSchedule.WOIn;
						aDayReport.WoOut = item.ProcessedSchedule.WOOut;
					}
				}
				else
				{
					aDayReport.WOShift = true;
				}
				DateTime mainInTime2 = item.ProcessedSchedule.MainInTime;
				d = default(DateTime);
				if (mainInTime2 == d)
				{
					DateTime mainOutTime2 = item.ProcessedSchedule.MainOutTime;
					d = default(DateTime);
					if (mainOutTime2 == d && item.FreeOverTimes.Count > 0)
					{
						aDayReport.WoIn = false;
						aDayReport.WoOut = false;
						ADayReport aDayReport12 = aDayReport;
						d = item.FreeOverTimes[0].FromTime;
						aDayReport12.MainInTime = d.ToString("HH:mm");
						ADayReport aDayReport13 = aDayReport;
						d = item.FreeOverTimes[0].ToTime;
						aDayReport13.MainOutTime = d.ToString("HH:mm");
					}
				}
				foreach (T5V3.Attendance.InShiftExit inShiftExit5 in item.ProcessedSchedule.InShiftExits)
				{
					if (inShiftExit5.IsLessWork && inShiftExit5.ValidLen > TimeSpan.FromMinutes(1.0))
					{
						List<TimeInterval> list3 = new List<TimeInterval>();
						foreach (T5V3.Model.Request item2 in all2.Where(delegate(T5V3.Model.Request r)
						{
							if (r.ForDate == item.ForDate)
							{
								if (r.WorkflowType != WorkflowOperations.Duty)
								{
									return r.WorkflowType == WorkflowOperations.Leave;
								}
								return true;
							}
							return false;
						}))
						{
							d = item2.ForDate;
							d = d.Date;
							dateTime = item2.FromTime;
							DateTime fromTime = d.Add(dateTime.TimeOfDay);
							d = item2.ForDate;
							d = d.Date;
							dateTime = item2.ToTime;
							DateTime toTime = d.Add(dateTime.TimeOfDay);
							if (TimeInterval.HaveIntersection(new TimeInterval(inShiftExit5.FromTime, inShiftExit5.ToTime), new TimeInterval(fromTime, toTime)))
							{
								MyTime3.Models.InShiftExit inShiftExit = new MyTime3.Models.InShiftExit();
								inShiftExit.FromTime = fromTime.ToString("HH:mm");
								inShiftExit.ToTime = toTime.ToString("HH:mm");
								inShiftExit.IsRequested = true;
								inShiftExit.RequestedDescriptions = $"{PersianString.Request} {all.FirstOrDefault((LeaveType l) => l.LeaveTypeID == item2.LeaveID).ToString()}";
								aDayReport.InShiftExits.Add(inShiftExit);
								list3.Add(new TimeInterval(fromTime, toTime));
							}
						}
						foreach (HourlyLeave item3 in all3)
						{
							if (TimeInterval.HaveIntersection(new TimeInterval(inShiftExit5.FromTime, inShiftExit5.ToTime), new TimeInterval(item3.FromTime, item3.ToTime)))
							{
								list3.Add(new TimeInterval(item3.FromTime, item3.ToTime));
							}
						}
						foreach (TimeInterval item4 in TimeInterval.Subtract(new TimeInterval(inShiftExit5.FromTime, inShiftExit5.ToTime), list3))
						{
							if (item4.Len > TimeSpan.FromMinutes(1.0))
							{
								MyTime3.Models.InShiftExit inShiftExit2 = new MyTime3.Models.InShiftExit();
								MyTime3.Models.InShiftExit inShiftExit3 = inShiftExit2;
								d = item4.FromTime;
								inShiftExit3.FromTime = d.ToString("HH:mm");
								MyTime3.Models.InShiftExit inShiftExit4 = inShiftExit2;
								d = item4.ToTime;
								inShiftExit4.ToTime = d.ToString("HH:mm");
								aDayReport.InShiftExits.Add(inShiftExit2);
							}
						}
					}
				}
				list.Add(aDayReport);
			}
			return list;
		}

		[HttpPost]
		public JsonResult GetSuspendRequests(string userName, string password)
		{
			List<MyTime3.Models.Request> data = new List<MyTime3.Models.Request>();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				data = GetRequestsFor(t5DBInstance, personnelSystem, num);
			}
			return Json(data);
		}

		private List<MyTime3.Models.Request> GetRequestsFor(T5V3DBManager db, PersonnelSystem ps, long badge)
		{
			List<MyTime3.Models.Request> list = new List<MyTime3.Models.Request>();
			List<KeyValuePair<long, int>> requestAccepters = db.RequestDA.GetRequestAccepters(ps.DB.UserInfos.GetAllUserInfoTitles(), ps.DB.TitlesCharts.GetAllWithUppersOnly());
			int titleID = ps.DB.UserInfos.GetUserCurrentTitleID(badge);
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
						request.RequestT5 = db.RequestDA.GetByID(current);
						Personnel.Model.UserInfo byID = ps.DB.UserInfos.GetByID(request.RequestT5.BadgeNumber);
						if (byID != null)
						{
							request.RequestT5.RequesterName = byID.ToString();
						}
						list.Add(request);
					}
				}
				finally
				{
					((IDisposable)enumerator).Dispose();
				}
			}
			enumerator = db.PersonnelDeputyDA.IsDeputy(badge).GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					long current2 = enumerator.Current;
					int deputyTitleID = ps.DB.UserInfos.GetUserCurrentTitleID(current2);
					if (deputyTitleID != -1)
					{
						foreach (long item in (from r in requestAccepters
						where r.Value == deputyTitleID
						select r.Key).ToList())
						{
							MyTime3.Models.Request request2 = new MyTime3.Models.Request();
							request2.ForDeputy = true;
							request2.DeputyTitleName = ps.DB.TitlesCharts.GetByID(deputyTitleID).TitleName;
							request2.RequestT5 = db.RequestDA.GetByID(item);
							Personnel.Model.UserInfo byID2 = ps.DB.UserInfos.GetByID(request2.RequestT5.BadgeNumber);
							if (byID2 != null)
							{
								request2.RequestT5.RequesterName = byID2.ToString();
							}
							list.Add(request2);
						}
					}
				}
				return list;
			}
			finally
			{
				((IDisposable)enumerator).Dispose();
			}
		}

		public ActionResult About()
		{
			base.ViewBag.Message = "Your app description page.";
			return View();
		}

		public ActionResult MyBanner(int index)
		{
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_0016: Expected O, but got Unknown
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0033: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			//IL_005a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0070: Unknown result type (might be due to invalid IL or missing references)
			//IL_0086: Unknown result type (might be due to invalid IL or missing references)
			using (MemoryStream memoryStream = new MemoryStream())
			{
				switch (index)
				{
				case 1:
				{
					Bitmap val = new Bitmap(950, 90);
					Graphics.FromImage(val);
					try
					{
						Image.FromFile(WebConfigurationManager.get_AppSettings()["LogoPath"]).Save((Stream)memoryStream, ImageFormat.get_Png());
					}
					catch
					{
					}
					val.Save((Stream)memoryStream, ImageFormat.get_Png());
					break;
				}
				case 2:
					ImagesResource.Arm.Save((Stream)memoryStream, ImageFormat.get_Png());
					break;
				case 3:
					ImagesResource.logo1.Save((Stream)memoryStream, ImageFormat.get_Png());
					break;
				case 4:
					ImagesResource.logo.Save((Stream)memoryStream, ImageFormat.get_Png());
					break;
				}
				return File(memoryStream.ToArray(), "image/png");
			}
		}

		private int ForNDayReport()
		{
			int result = 40;
			try
			{
				result = int.Parse(WebConfigurationManager.get_AppSettings().GetValues("ShowDays")[0]);
				return result;
			}
			catch
			{
				return result;
			}
		}

		public static double RequestLockValuee()
		{
			double result = new DateTime(2000, 1, 1).ToOADate();
			try
			{
				result = (double)int.Parse(WebConfigurationManager.get_AppSettings().GetValues("RequestsLock")[0]);
				return result;
			}
			catch
			{
				return result;
			}
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin")]
		public ActionResult SetRequestsLock(string newval)
		{
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_001a: Unknown result type (might be due to invalid IL or missing references)
			//IL_001b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			//IL_0045: Unknown result type (might be due to invalid IL or missing references)
			//IL_004a: Unknown result type (might be due to invalid IL or missing references)
			DateTime? nullable = DateConverter.ToChristian(newval);
			if (nullable.HasValue)
			{
				try
				{
					Configuration val = WebConfigurationManager.OpenWebConfiguration("~");
					val.get_AppSettings().get_Settings().Remove("RequestsLock");
					val.Save();
				}
				catch
				{
				}
				try
				{
					Configuration val2 = WebConfigurationManager.OpenWebConfiguration("~");
					val2.get_AppSettings().get_Settings().Add("RequestsLock", nullable.Value.ToOADate().ToString());
					val2.Save();
				}
				catch (Exception ex)
				{
					string message = ex.Message;
				}
			}
			return RedirectToAction("Index", "Home");
		}

		public static bool CanDownloadNoti5(HttpContextBase httpContext)
		{
			bool result = false;
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			int titleID = GlobalHelper.GetLoggedInUserInfoTitleID(httpContext);
			if ((from r in personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly()
			where r.Value == titleID
			select r).Count() > 0)
			{
				result = true;
			}
			if (t5DBInstance.WorkflowDA.GetAll(titleID).Count() > 0)
			{
				result = true;
			}
			if (t5DBInstance.PersonnelDeputyDA.IsDeputy(GlobalHelper.GetLoggedInBadgeNumber(httpContext)).Count > 0)
			{
				result = true;
			}
			return result;
		}

		[HttpGet]
		public FileResult GetNoti5()
		{
			return File(Noti5.Noti5Setup, "application/octet-stream", "Noti5Setup.msi");
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult SetToolbar(List<int> ToolbarItems)
		{
			if (ToolbarItems == null)
			{
				ToolbarItems = new List<int>();
			}
			if (ToolbarItems != null)
			{
				PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
				long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
				if (loggedInBadgeNumber != -1)
				{
					personnelSystem.DB.ToolbarItems.SetItems(loggedInBadgeNumber, ToolbarItems);
				}
			}
			return RedirectToAction("Index");
		}

		public static List<int> GetToolbars(HttpContextBase httpContext)
		{
			List<int> list = new List<int>();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			try
			{
				string badgeNumber = LoginManager.GetBadgeNumber(httpContext);
				list = personnelSystem.DB.ToolbarItems.GetItems(long.Parse(badgeNumber));
				if (list.Count != 0)
				{
					return list;
				}
				list = new List<int>
				{
					1,
					2,
					4,
					6
				};
				return list;
			}
			catch
			{
				return list;
			}
		}

		public static bool SideMenu(HttpContextBase httpContext)
		{
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			bool result = true;
			try
			{
				HttpCookie val = httpContext.get_Request().get_Cookies().Get("sidemenu");
				if (val == null)
				{
					return result;
				}
				result = bool.Parse(val.get_Value());
				return result;
			}
			catch
			{
				return result;
			}
		}

		public static bool ToolbarShow(HttpContextBase httpContext)
		{
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			bool result = true;
			try
			{
				HttpCookie val = httpContext.get_Request().get_Cookies().Get("toolb");
				if (val == null)
				{
					return result;
				}
				result = bool.Parse(val.get_Value());
				return result;
			}
			catch
			{
				return result;
			}
		}
	}
}
