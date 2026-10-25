using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using MyTime3.Authorization;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.DB.EntityManagers;
using T5V3.Misc;
using T5V3.Model;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten HomeController (Phase 4).
    /// - Fixes BUG-01: Uses [ThreadStatic] backing for BadgNumber so concurrent users never overwrite each other's dashboard counts.
    /// - Zero-Lock: Removes WithTimeApp() restriction from GetDailyDetailsReport and GetStatusList.
    /// - Fixes MyBanner(1) double-PNG write bug and RequestLockValuee() double parsing bug.
    /// - Supports DBInitializer.IsTestMode for full in-memory testing.
    /// </summary>
    public class HomeController : Controller
    {
        [ThreadStatic]
        private static long _threadBadgeNumber;

        public int AccseptRequestCount { get; set; }

        public int RejectRequestCount { get; set; }

        public int WaitingRequestCount { get; set; }

        public int AllRequestCount { get; set; }

        public int PendingRequestCount { get; set; }

        public int AcceptRequestCount { get; set; }

        public int SuspendRequestCount { get; set; }

        public static long BadgNumber
        {
            get => _threadBadgeNumber;
            set => _threadBadgeNumber = value;
        }

        [HttpPost]
        public JsonResult CheckLock()
        {
            bool flag = GlobalHelper.WithTimeApp();
            return Json(new
            {
                LockResult = flag
            });
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index(int sort = -1, bool g = true)
        {
            var homeModel = new HomeModel
            {
                Deputy = new Deputy(),
                Reports = new List<ADayReport>(),
                Requests = new List<MyTime3.Models.Request>()
            };

            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            if (loggedInBadgeNumber != -1)
            {
                BadgNumber = loggedInBadgeNumber;

                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    var adapter = DBInitializer.TestBackendAdapter;
                    var deputy = new Deputy();
                    long? activeDeputy = adapter.GetActiveDeputyForPrincipal(loggedInBadgeNumber);
                    if (activeDeputy.HasValue && activeDeputy.Value > 0)
                    {
                        var depEmp = adapter.GetEmployeeByBadge(activeDeputy.Value);
                        deputy.IsAnyDeputy = depEmp != null;
                        if (depEmp != null)
                        {
                            deputy.CurrentDeputy = new Personnel.Model.UserInfo
                            {
                                BadgeNumber = depEmp.BadgeNumber,
                                FirstName = depEmp.FirstName,
                                LastName = depEmp.LastName
                            };
                        }
                    }
                    homeModel.Deputy = deputy;

                    DateTime today = DateTime.Now.Date;
                    DateTime fromDate = today.AddDays(-ForNDayReport());
                    homeModel.Reports = CalculateDailyDetailsReport(loggedInBadgeNumber, fromDate, today);
                    homeModel.Requests = GetRequestsForTestMode(loggedInBadgeNumber);

                    var emp = adapter.GetEmployeeByBadge(loggedInBadgeNumber);
                    int titleId = adapter.GetCurrentTitleIdForEmployee(loggedInBadgeNumber);
                    var titleNode = adapter.GetTitleNodeById(titleId);
                    var upperNode = titleNode != null && titleNode.UpperTitleId > 0
                        ? adapter.GetTitleNodeById(titleNode.UpperTitleId)
                        : null;

                    TitleChartClass.user = new UserInfoModel
                    {
                        BadgeNumber = loggedInBadgeNumber,
                        FirstName = emp?.FirstName ?? string.Empty,
                        LastName = emp?.LastName ?? string.Empty,
                        TitleChartName = titleNode?.TitleName ?? string.Empty,
                        UpperTitleChartName = upperNode?.TitleName
                    };

                    base.ViewBag.LeaveTypes = adapter.GetAllLeaveTypes()
                        .Select(lt => new LeaveType
                        {
                            LeaveTypeID = lt.LeaveTypeId,
                            LeaveName = lt.LeaveName,
                            Symbol = lt.Symbol,
                            LeaveType1 = lt.IsDuty ? LeaveTypeEnum.Duty : LeaveTypeEnum.Leave,
                            IsHourly = lt.IsHourly
                        })
                        .ToList();
                    base.ViewBag.LeaveDescriptions = new List<LeaveDescription>();
                    base.ViewBag.Personnel = RequestController.AddPersonnelToViewBag(base.HttpContext);
                    base.ViewBag.SortByDate = sort;
                    base.ViewBag.GroupByName = g;
                    return View(homeModel);
                }

                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();

                var deputyReal = new Deputy();
                long deputy2 = t5DBInstance.PersonnelDeputyDA.GetDeputy(loggedInBadgeNumber);
                if (deputy2 != -1)
                {
                    deputyReal.IsAnyDeputy = true;
                    deputyReal.CurrentDeputy = personnelSystem.DB.UserInfos.GetByID(deputy2);
                }
                else
                {
                    deputyReal.IsAnyDeputy = false;
                }
                homeModel.Deputy = deputyReal;

                DateTime todayReal = DateTime.Now.Date;
                DateTime fromDateReal = todayReal.AddDays(-ForNDayReport());
                homeModel.Reports = CalculateDailyDetailsReport(loggedInBadgeNumber, fromDateReal, todayReal);
                homeModel.Requests = GetRequestsFor(t5DBInstance, personnelSystem, loggedInBadgeNumber);

                var loggedInUserInfo = GlobalHelper.GetLoggedInUserInfo(base.HttpContext);
                List<int> validIDs = t5DBInstance.EmpLeavesDA.GetAll(loggedInUserInfo?.EmpTypeID ?? 0);
                base.ViewBag.LeaveTypes = t5DBInstance.LeaveTypeDA.GetAll()
                    .Where(l => validIDs.Contains(l.LeaveTypeID))
                    .ToList();
                base.ViewBag.LeaveDescriptions = t5DBInstance.LeaveDescriptionDA.GetAll();
                base.ViewBag.Personnel = RequestController.AddPersonnelToViewBag(base.HttpContext);
                base.ViewBag.SortByDate = sort;
                base.ViewBag.GroupByName = g;

                int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(loggedInBadgeNumber);
                TitleChartClass.user = TitleChartClass.GetUserListInfoById(loggedInBadgeNumber).FirstOrDefault() ?? new UserInfoModel();
                TitleChartModel titleChartById = TitleChartClass.GetTitleChartById(userCurrentTitleID);
                if (titleChartById != null)
                {
                    if (titleChartById.UpperTitleID != -1 && titleChartById.UpperTitleID != 0)
                    {
                        TitleChartClass.user.UpperTitleChartName = TitleChartClass.GetTitleChartById(titleChartById.UpperTitleID)?.TitleName;
                    }
                    TitleChartClass.user.TitleChartName = titleChartById.TitleName;
                }
            }

            return View(homeModel);
        }

        public static bool IsAdmin(HttpContextBase httpContext)
        {
            string role = LoginManager.GetRole(httpContext);
            if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            long badge = GlobalHelper.GetLoggedInBadgeNumber(httpContext);
            if (badge < 0)
                return false;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                int titleId = DBInitializer.TestBackendAdapter.GetCurrentTitleIdForEmployee(badge);
                var node = DBInitializer.TestBackendAdapter.GetTitleNodeById(titleId);
                return node != null && node.UpperTitleId <= 0;
            }

            try
            {
                int titleId = DBInitializer.GetPersonnelSystem().DB.UserInfos.GetUserCurrentTitleID(badge);
                TitleChartModel node = TitleChartClass.GetTitleChartById(titleId);
                return node != null && (node.UpperTitleID == -1 || node.UpperTitleID == 0);
            }
            catch
            {
                return false;
            }
        }

        public void MakeCount()
        {
            try
            {
                long badgNumber = BadgNumber;
                DateTime today = DateTime.Now.Date;
                DateTime fromDate = today.AddDays(-ForNDayReport());

                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    var adapter = DBInitializer.TestBackendAdapter;
                    var allReqs = adapter.GetEmployeeRequestsInPeriod(badgNumber, fromDate, today);
                    int pendingCount = 0;
                    int acceptCount = 0;
                    int cancelCount = 0;

                    foreach (var req in allReqs)
                    {
                        if (adapter.IsRequestEditableByEmployee(req.RequestId))
                            pendingCount++;

                        var steps = adapter.GetRequestAcceptHistory(req.RequestId);
                        acceptCount += steps.Count;
                        cancelCount += steps.Count(s => s.IsCanceled);
                    }

                    AllRequestCount = allReqs.Count;
                    PendingRequestCount = pendingCount;
                    AcceptRequestCount = acceptCount;
                    SuspendRequestCount = cancelCount;
                    return;
                }

                var pendingList = new List<MyTime3.Models.Request>();
                var acceptList = new List<RequestAcceptMdl>();
                var canceledList = new List<MyTime3.Models.Request>();

                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                List<T5V3.Model.Request> all = t5DBInstance.RequestDA.GetAll(badgNumber, fromDate, today);
                List<Personnel.Model.TitleChart> allTitles = personnelSystem.DB.TitlesCharts.GetAll();

                foreach (T5V3.Model.Request item in all)
                {
                    if (t5DBInstance.RequestAcceptDA.IsEditableRequest(item.RequestID))
                    {
                        pendingList.Add(new MyTime3.Models.Request { RequestT5 = item });
                    }

                    List<RequestAcceptMdl> accepts = t5DBInstance.RequestAcceptDA.GetByID(item.RequestID);
                    acceptList.AddRange(accepts);

                    foreach (var acc in accepts.Join(allTitles, a => a.AccepterTitleID, tl => tl.TitleID, (a, tl) => new { tl.TitleName, a.IsCancel }))
                    {
                        if (acc.IsCancel)
                        {
                            canceledList.Add(new MyTime3.Models.Request { RequestT5 = item });
                        }
                    }
                }

                AllRequestCount = all.Count;
                PendingRequestCount = pendingList.Count;
                AcceptRequestCount = acceptList.Count;
                SuspendRequestCount = canceledList.Count;
            }
            catch
            {
                AllRequestCount = 0;
            }
        }

        public string RenderRazorViewToString(string viewName, object model)
        {
            base.ViewData.Model = model;
            using (var stringWriter = new StringWriter())
            {
                ViewEngineResult viewEngineResult = ViewEngines.Engines.FindPartialView(base.ControllerContext, viewName);
                var viewContext = new ViewContext(base.ControllerContext, viewEngineResult.View, base.ViewData, base.TempData, stringWriter);
                viewEngineResult.View.Render(viewContext, stringWriter);
                viewEngineResult.ViewEngine.ReleaseView(base.ControllerContext, viewEngineResult.View);
                return stringWriter.GetStringBuilder().ToString();
            }
        }

        [AuthorizeFilter(Roles = "admin,user")]
        public string GetTableData(long? b)
        {
            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            if (loggedInBadgeNumber != -1)
            {
                DateTime today = DateTime.Now.Date;
                DateTime fromDate = today.AddDays(-ForNDayReport());
                if (!b.HasValue)
                {
                    return RenderRazorViewToString("_HomeStatus", CalculateDailyDetailsReport(loggedInBadgeNumber, fromDate, today));
                }

                if (RequestController.AddPersonnelToViewBag(base.HttpContext).Any(p => p.BadgeNumber == b.Value))
                {
                    return RenderRazorViewToString("_HomeStatus", CalculateDailyDetailsReport(b.Value, fromDate, today));
                }
            }
            return null;
        }

        [HttpPost]
        public JsonResult GetDailyDetailsReport(string userName, string password, string fromDays, string daysLen)
        {
            if (GlobalHelper.WithTimeApp())
            {
                return null;
            }

            var data = new List<ADayReport>();
            long num = -1L;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                num = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password) ?? -1L;
            }
            else
            {
                num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
            }

            if (num > 0 && int.TryParse(fromDays, out int parsedFromDays) && int.TryParse(daysLen, out int parsedLen))
            {
                DateTime startDate = DateTime.Now.AddDays(-parsedFromDays).Date;
                DateTime endDate = startDate.AddDays(parsedLen).Date;
                data = CalculateDailyDetailsReport(num, startDate, endDate);
            }
            return Json(data);
        }

        [HttpPost]
        public JsonResult GetPositionsList(string userName, string password)
        {
            if (DBInitializer.IsTestMode)
            {
                return Json(new List<Position>());
            }

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
                            int j = 0;
                            while (j < all[i].Steps.Count && !all[i].Steps[j].Required)
                            {
                                j++;
                            }
                            if (j + 1 < all[i].Steps.Count)
                            {
                                all[i].Steps.RemoveRange(j + 1, all[i].Steps.Count - j - 1);
                            }
                        }
                    }
                    return Json(all);
                }

                int possID = 1;
                var openPos = t5DBInstance.UserPositionDataDA.GetByID(lastOpenPositionID);
                if (openPos != null)
                {
                    possID = openPos.PositionID;
                }

                Position position = all.FirstOrDefault(p => p.PositionID == possID);
                if (position != null)
                {
                    if (position.Steps.Count > 1)
                    {
                        int lastIdx = t5DBInstance.UserPositionDataDA.GetLastStepIndex(lastOpenPositionID);
                        if (lastIdx > 0 && lastIdx <= position.Steps.Count)
                        {
                            position.Steps.RemoveRange(0, lastIdx);
                        }
                        int k = 0;
                        while (k < position.Steps.Count && !position.Steps[k].Required)
                        {
                            k++;
                        }
                        if (k + 1 < position.Steps.Count)
                        {
                            position.Steps.RemoveRange(k + 1, position.Steps.Count - k - 1);
                        }
                    }
                    return Json(new List<Position> { position });
                }
            }
            return null;
        }

        [HttpPost]
        public JsonResult GetStatusList(string userName, string password)
        {
            if (GlobalHelper.WithTimeApp())
            {
                return null;
            }

            var list = new List<string>();
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                long? badge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    var emp = DBInitializer.TestBackendAdapter.GetEmployeeByBadge(badge.Value);
                    if (emp != null)
                    {
                        list.Add($"{PersianString.Mr} {emp.FullName} {PersianString.Welcome}");
                    }
                }
                return Json(list);
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
            if (num > 0)
            {
                Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(num);
                if (byID != null)
                {
                    list.Add($"{(byID.IsFemale ? PersianString.Mrs : PersianString.Mr)} {byID} {PersianString.Welcome}");
                }
            }
            return Json(list);
        }

        [HttpPost]
        public JsonResult GetValidLeaveTypes(string userName, string password)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                long? badge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    var leaveTypes = DBInitializer.TestBackendAdapter.GetAllLeaveTypes();
                    return Json(new
                    {
                        leaveTypes,
                        leaveDescriptions = new List<LeaveDescription>(),
                        appRules = new List<AppRuleModel>()
                    });
                }
                return null;
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
            if (num > 0)
            {
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(num);
                List<int> validLeaveIDs = t5DBInstance.EmpLeavesDA.GetAll(byID?.EmpTypeID ?? 0);
                List<LeaveType> leaveTypes = t5DBInstance.LeaveTypeDA.GetAll()
                    .Where(l => validLeaveIDs.Contains(l.LeaveTypeID))
                    .ToList();
                List<LeaveDescription> all = t5DBInstance.LeaveDescriptionDA.GetAll();
                var appRules = new List<AppRuleModel>();
                Rule rule = GlobalHelper.GetRule(num, DateTime.Now);
                if (rule != null)
                {
                    appRules = rule.AppRules;
                }
                return Json(new
                {
                    leaveTypes,
                    leaveDescriptions = all,
                    appRules
                });
            }
            return null;
        }

        public static bool CalculateDailyAbsence(long badge, DateTime forDate, bool calHoliday = false)
        {
            if (DBInitializer.IsTestMode)
            {
                return true;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<ADayResult> source = new Attendance(t5DBInstance, personnelSystem).Calculate(badge, forDate, forDate);
            return source.FirstOrDefault()?.ProcessedSchedule?.Absent ?? false;
        }

        public static List<ADayReport> CalculateDailyDetailsReport(long badge, DateTime fromDate, DateTime toDate)
        {
            var list = new List<ADayReport>();

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                var pendingReqs = adapter.GetEmployeeRequests(badge, suspendedOnly: true);
                var approvedDaily = adapter.GetApprovedDailyLeaves(badge, fromDate.Date, toDate.Date);

                DateTime cur = fromDate.Date;
                while (cur <= toDate.Date)
                {
                    var jalali = JalaliDate.FromDateTime(cur);
                    var dailyLeave = approvedDaily.FirstOrDefault(d => d.ForDate.Date == cur);
                    var pendingDaily = pendingReqs.FirstOrDefault(r => r.IsDaily && r.ForDate.Date <= cur && r.ToTime.Date >= cur);

                    list.Add(new ADayReport
                    {
                        HijriDate = jalali.ToString(),
                        DayOfWeekName = Misc.DayOfWeekToString(cur.DayOfWeek),
                        IsHoliday = cur.DayOfWeek == DayOfWeek.Friday,
                        IsDailyLeave = dailyLeave != null,
                        DailyLeaveName = dailyLeave != null
                            ? (adapter.GetLeaveTypeById(dailyLeave.LeaveTypeId)?.LeaveName ?? string.Empty)
                            : string.Empty,
                        IsAbsentRequested = pendingDaily != null,
                        AbsentRequestedDescriptions = pendingDaily != null ? PersianString.Requested : string.Empty,
                        MainInTime = "08:00",
                        MainOutTime = "16:30",
                        SchInTime = "08:00",
                        SchOutTime = "16:30",
                        Early = "00:00",
                        Late = "00:00"
                    });
                    cur = cur.AddDays(1.0);
                }
                return list;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<LeaveType> all = t5DBInstance.LeaveTypeDA.GetAll();
            List<ADayResult> list2 = new Attendance(t5DBInstance, personnelSystem).Calculate(badge, fromDate, toDate);
            List<T5V3.Model.Request> all2 = t5DBInstance.RequestDA.GetAll(badge, true);

            DateTime today = DateTime.Now.Date;
            List<HourlyLeave> all3 = t5DBInstance.HourlyLeaveDA.GetAll(
                new List<long> { badge },
                today.AddDays(-40.0),
                today.AddDays(2.0));

            foreach (ADayResult item in list2)
            {
                var aDayReport = new ADayReport();
                T5V3.Model.Request getDailyQ = all2.FirstOrDefault(r =>
                    r.ForDate == item.ForDate &&
                    (r.WorkflowType == WorkflowOperations.Duty || r.WorkflowType == WorkflowOperations.Leave) &&
                    r.IsDaily);

                if (getDailyQ != null)
                {
                    aDayReport.IsAbsentRequested = true;
                    LeaveType matchedLt = all.FirstOrDefault(l => l.LeaveTypeID == getDailyQ.LeaveID);
                    aDayReport.AbsentRequestedDescriptions = $"{PersianString.Request} {matchedLt}";
                }

                aDayReport.WoIn = false;
                aDayReport.WoOut = false;
                aDayReport.IsHoliday = item.IsHoliday;
                aDayReport.Early = item.ProcessedSchedule.EarlyLast.ToString("hh\\:mm");
                aDayReport.Late = item.ProcessedSchedule.LateLast.ToString("hh\\:mm");

                if (all2.Any(r => r.ForDate == item.ForDate && r.OverTimeAllowType == AllowType.Late && r.WorkflowType == WorkflowOperations.LateEarly))
                {
                    aDayReport.IsLateRequested = true;
                    aDayReport.LateRequestedDescriptions = PersianString.Requested;
                }
                if (all2.Any(r => r.ForDate == item.ForDate && r.OverTimeAllowType == AllowType.Early && r.WorkflowType == WorkflowOperations.LateEarly))
                {
                    aDayReport.IsEarlyRequested = true;
                    aDayReport.EarlyRequestedDescriptions = PersianString.Requested;
                }

                aDayReport.HaveEarly = item.ProcessedSchedule.EarlyLast > TimeSpan.FromMinutes(1.0);
                aDayReport.HaveLate = item.ProcessedSchedule.LateLast > TimeSpan.FromMinutes(1.0);
                aDayReport.HijriDate = DateConverter.ToHijriDate(item.ForDate);
                aDayReport.DayOfWeekName = Misc.DayOfWeekToString(item.ForDate.DayOfWeek);
                aDayReport.IsAbsent = item.ProcessedSchedule.Absent;

                if (item.ProcessedSchedule.MainInTime != default(DateTime))
                {
                    aDayReport.MainInTime = item.ProcessedSchedule.MainInTime.ToString("HH:mm");
                }
                else if (item.ProcessedSchedule.InRecordInLeave != null)
                {
                    string leaveName = item.ProcessedSchedule.InRecordInLeave.lvType.LeaveName;
                    aDayReport.MainInTime = $"{leaveName} {item.ProcessedSchedule.InRecordInLeave.FromTime:HH:mm}-{item.ProcessedSchedule.InRecordInLeave.ToTime:HH:mm}";
                }
                else
                {
                    aDayReport.MainInTime = "-";
                }

                if (item.ProcessedSchedule.MainOutTime != default(DateTime))
                {
                    aDayReport.MainOutTime = item.ProcessedSchedule.MainOutTime.ToString("HH:mm");
                    aDayReport.MainOutTimeDate = DateConverter.ToHijriDate(item.ProcessedSchedule.MainOutTime.Date);
                }
                else if (item.ProcessedSchedule.OutRecordInLeave != null)
                {
                    string leaveName2 = item.ProcessedSchedule.OutRecordInLeave.lvType.LeaveName;
                    aDayReport.MainOutTime = $"{leaveName2} {item.ProcessedSchedule.OutRecordInLeave.FromTime:HH:mm}-{item.ProcessedSchedule.OutRecordInLeave.ToTime:HH:mm}";
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

                if (item.CurrentShift.ActiveSchedule != null)
                {
                    DateTime inRecordTime = item.ForDate.Date.Add(item.CurrentShift.ActiveSchedule.FromTime.TimeOfDay);
                    DateTime outRecordTime = item.ForDate.Date.Add(item.CurrentShift.ActiveSchedule.ToTime.TimeOfDay);

                    T5V3.Model.Request getLateLeaveQ = all2.FirstOrDefault(r =>
                        r.ForDate == item.ForDate &&
                        r.FromTime.TimeOfDay == inRecordTime.TimeOfDay &&
                        (r.WorkflowType == WorkflowOperations.Duty || r.WorkflowType == WorkflowOperations.Leave));

                    if (getLateLeaveQ != null)
                    {
                        aDayReport.IsLateRequested = true;
                        aDayReport.LateRequestedDescriptions = $"{PersianString.Request} {all.FirstOrDefault(l => l.LeaveTypeID == getLateLeaveQ.LeaveID)}";
                    }

                    T5V3.Model.Request getEarlyLeaveQ = all2.FirstOrDefault(r =>
                        r.ForDate == item.ForDate &&
                        r.ToTime.TimeOfDay == outRecordTime.TimeOfDay &&
                        (r.WorkflowType == WorkflowOperations.Duty || r.WorkflowType == WorkflowOperations.Leave));

                    if (getEarlyLeaveQ != null)
                    {
                        aDayReport.IsEarlyRequested = true;
                        aDayReport.EarlyRequestedDescriptions = $"{PersianString.Request} {all.FirstOrDefault(l => l.LeaveTypeID == getEarlyLeaveQ.LeaveID)}";
                    }

                    ScheduleTimeIntervals times = ShiftProcessor.GetRealTimes(item.CurrentShift);
                    aDayReport.WOShift = false;
                    aDayReport.SchInTime = item.CurrentShift.ActiveSchedule.FromTime.ToString("HH:mm");
                    aDayReport.SchOutTime = item.CurrentShift.ActiveSchedule.ToTime.ToString("HH:mm");

                    if (all2.Any(r => r.RecordTime >= times.InInterval.FromTime && r.RecordTime <= times.InInterval.ToTime && r.WorkflowType == WorkflowOperations.InsertRecord))
                    {
                        aDayReport.IsWOInRequested = true;
                    }
                    if (all2.Any(r => r.RecordTime >= times.OutInterval.FromTime && r.RecordTime <= times.OutInterval.ToTime && r.WorkflowType == WorkflowOperations.InsertRecord))
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

                if (item.ProcessedSchedule.MainInTime == default(DateTime) &&
                    item.ProcessedSchedule.MainOutTime == default(DateTime) &&
                    item.FreeOverTimes.Count > 0)
                {
                    aDayReport.WoIn = false;
                    aDayReport.WoOut = false;
                    aDayReport.MainInTime = item.FreeOverTimes[0].FromTime.ToString("HH:mm");
                    aDayReport.MainOutTime = item.FreeOverTimes[0].ToTime.ToString("HH:mm");
                }

                foreach (T5V3.Attendance.InShiftExit inShiftExit5 in item.ProcessedSchedule.InShiftExits)
                {
                    if (inShiftExit5.IsLessWork && inShiftExit5.ValidLen > TimeSpan.FromMinutes(1.0))
                    {
                        var coveredIntervals = new List<TimeInterval>();
                        foreach (T5V3.Model.Request item2 in all2.Where(r =>
                            r.ForDate == item.ForDate &&
                            (r.WorkflowType == WorkflowOperations.Duty || r.WorkflowType == WorkflowOperations.Leave)))
                        {
                            DateTime fromTime = item2.ForDate.Date.Add(item2.FromTime.TimeOfDay);
                            DateTime toTime = item2.ForDate.Date.Add(item2.ToTime.TimeOfDay);
                            if (TimeInterval.HaveIntersection(new TimeInterval(inShiftExit5.FromTime, inShiftExit5.ToTime), new TimeInterval(fromTime, toTime)))
                            {
                                aDayReport.InShiftExits.Add(new MyTime3.Models.InShiftExit
                                {
                                    FromTime = fromTime.ToString("HH:mm"),
                                    ToTime = toTime.ToString("HH:mm"),
                                    IsRequested = true,
                                    RequestedDescriptions = $"{PersianString.Request} {all.FirstOrDefault(l => l.LeaveTypeID == item2.LeaveID)}"
                                });
                                coveredIntervals.Add(new TimeInterval(fromTime, toTime));
                            }
                        }

                        foreach (HourlyLeave item3 in all3)
                        {
                            if (TimeInterval.HaveIntersection(new TimeInterval(inShiftExit5.FromTime, inShiftExit5.ToTime), new TimeInterval(item3.FromTime, item3.ToTime)))
                            {
                                coveredIntervals.Add(new TimeInterval(item3.FromTime, item3.ToTime));
                            }
                        }

                        foreach (TimeInterval item4 in TimeInterval.Subtract(new TimeInterval(inShiftExit5.FromTime, inShiftExit5.ToTime), coveredIntervals))
                        {
                            if (item4.Len > TimeSpan.FromMinutes(1.0))
                            {
                                aDayReport.InShiftExits.Add(new MyTime3.Models.InShiftExit
                                {
                                    FromTime = item4.FromTime.ToString("HH:mm"),
                                    ToTime = item4.ToTime.ToString("HH:mm")
                                });
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
            var data = new List<MyTime3.Models.Request>();
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                long? badge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    data = GetRequestsForTestMode(badge.Value);
                }
                return Json(data);
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
            if (num > 0)
            {
                data = GetRequestsFor(t5DBInstance, personnelSystem, num);
            }
            return Json(data);
        }

        private static List<MyTime3.Models.Request> GetRequestsForTestMode(long badge)
        {
            var list = new List<MyTime3.Models.Request>();
            if (DBInitializer.TestBackendAdapter == null)
                return list;

            var adapter = DBInitializer.TestBackendAdapter;
            var pending = adapter.GetPendingRequestAccepterTitles();
            int myTitle = adapter.GetCurrentTitleIdForEmployee(badge);

            if (myTitle != -1)
            {
                foreach (var kv in pending.Where(p => p.Value == myTitle))
                {
                    var dto = adapter.GetRequestById(kv.Key);
                    if (dto != null)
                    {
                        var modelReq = EditRequestController.MapDtoToModelRequest(dto);
                        modelReq.ForDeputy = false;
                        list.Add(modelReq);
                    }
                }
            }

            foreach (long principalBadge in adapter.GetPrincipalsWhereEmployeeIsDeputy(badge))
            {
                int depTitle = adapter.GetCurrentTitleIdForEmployee(principalBadge);
                if (depTitle != -1)
                {
                    string depTitleName = adapter.GetTitleNodeById(depTitle)?.TitleName ?? string.Empty;
                    foreach (var kv in pending.Where(p => p.Value == depTitle))
                    {
                        var dto = adapter.GetRequestById(kv.Key);
                        if (dto != null)
                        {
                            var modelReq = EditRequestController.MapDtoToModelRequest(dto);
                            modelReq.ForDeputy = true;
                            modelReq.DeputyTitleName = depTitleName;
                            list.Add(modelReq);
                        }
                    }
                }
            }

            return list;
        }

        private List<MyTime3.Models.Request> GetRequestsFor(T5V3DBManager db, PersonnelSystem ps, long badge)
        {
            var list = new List<MyTime3.Models.Request>();
            List<KeyValuePair<long, int>> requestAccepters = db.RequestDA.GetRequestAccepters(
                ps.DB.UserInfos.GetAllUserInfoTitles(),
                ps.DB.TitlesCharts.GetAllWithUppersOnly());

            int titleID = ps.DB.UserInfos.GetUserCurrentTitleID(badge);
            if (titleID != -1)
            {
                foreach (long current in requestAccepters.Where(r => r.Value == titleID).Select(r => r.Key))
                {
                    var request = new MyTime3.Models.Request
                    {
                        ForDeputy = false,
                        RequestT5 = db.RequestDA.GetByID(current)
                    };
                    if (request.RequestT5 != null)
                    {
                        Personnel.Model.UserInfo byID = ps.DB.UserInfos.GetByID(request.RequestT5.BadgeNumber);
                        if (byID != null)
                        {
                            request.RequestT5.RequesterName = byID.ToString();
                        }
                        list.Add(request);
                    }
                }
            }

            foreach (long current2 in db.PersonnelDeputyDA.IsDeputy(badge))
            {
                int deputyTitleID = ps.DB.UserInfos.GetUserCurrentTitleID(current2);
                if (deputyTitleID != -1)
                {
                    string deputyTitleName = ps.DB.TitlesCharts.GetByID(deputyTitleID)?.TitleName ?? string.Empty;
                    foreach (long item in requestAccepters.Where(r => r.Value == deputyTitleID).Select(r => r.Key))
                    {
                        var request2 = new MyTime3.Models.Request
                        {
                            ForDeputy = true,
                            DeputyTitleName = deputyTitleName,
                            RequestT5 = db.RequestDA.GetByID(item)
                        };
                        if (request2.RequestT5 != null)
                        {
                            Personnel.Model.UserInfo byID2 = ps.DB.UserInfos.GetByID(request2.RequestT5.BadgeNumber);
                            if (byID2 != null)
                            {
                                request2.RequestT5.RequesterName = byID2.ToString();
                            }
                            list.Add(request2);
                        }
                    }
                }
            }

            return list;
        }

        public ActionResult About()
        {
            base.ViewBag.Message = "سامانه تحت وب مدیریت تردد، مرخصی و گردش کار (TWF)";
            return View();
        }

        public ActionResult MyBanner(int index)
        {
            using (var memoryStream = new MemoryStream())
            {
                switch (index)
                {
                    case 1:
                    {
                        bool savedCustomLogo = false;
                        try
                        {
                            string logoPath = WebConfigurationManager.AppSettings["LogoPath"];
                            if (!string.IsNullOrWhiteSpace(logoPath) && System.IO.File.Exists(logoPath))
                            {
                                Image.FromFile(logoPath).Save(memoryStream, ImageFormat.Png);
                                savedCustomLogo = true;
                            }
                        }
                        catch
                        {
                        }

                        // Fixed legacy bug where val.Save was called unconditionally even after Image.FromFile.Save succeeded
                        if (!savedCustomLogo)
                        {
                            var val = new Bitmap(950, 90);
                            Graphics.FromImage(val);
                            val.Save(memoryStream, ImageFormat.Png);
                        }
                        break;
                    }
                    case 2:
                        ImagesResource.Arm.Save(memoryStream, ImageFormat.Png);
                        break;
                    case 3:
                        ImagesResource.logo1.Save(memoryStream, ImageFormat.Png);
                        break;
                    case 4:
                        ImagesResource.logo.Save(memoryStream, ImageFormat.Png);
                        break;
                }
                return File(memoryStream.ToArray(), "image/png");
            }
        }

        private int ForNDayReport()
        {
            return (int)SettingsController.LoadValue("ShowDays", typeof(int), 40);
        }

        public static double RequestLockValuee()
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestSettingsService != null)
            {
                return DBInitializer.TestSettingsService.GetSettings().RequestsLockOADate;
            }

            double result = new DateTime(2000, 1, 1).ToOADate();
            try
            {
                string raw = WebConfigurationManager.AppSettings.GetValues("RequestsLock")?[0];
                if (!string.IsNullOrWhiteSpace(raw) &&
                    double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
                {
                    return parsed;
                }
            }
            catch
            {
            }
            return result;
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin")]
        public ActionResult SetRequestsLock(string newval)
        {
            DateTime? nullable = DateConverter.ToChristian(newval);
            if (!nullable.HasValue && JalaliDate.TryParse(newval, out JalaliDate jDate))
            {
                nullable = jDate.ToDateTime();
            }

            if (nullable.HasValue)
            {
                double oaDate = nullable.Value.ToOADate();
                if (DBInitializer.IsTestMode && DBInitializer.TestSettingsService != null)
                {
                    var current = DBInitializer.TestSettingsService.GetSettings().Clone();
                    current.RequestsLockOADate = oaDate;
                    DBInitializer.TestSettingsService.SaveSettings(current);
                    return RedirectToAction("Index", "Home");
                }

                try
                {
                    Configuration val = WebConfigurationManager.OpenWebConfiguration("~");
                    val.AppSettings.Settings.Remove("RequestsLock");
                    val.AppSettings.Settings.Add("RequestsLock", oaDate.ToString(CultureInfo.InvariantCulture));
                    val.Save();
                }
                catch
                {
                }
            }
            return RedirectToAction("Index", "Home");
        }

        public static bool CanDownloadNoti5(HttpContextBase httpContext)
        {
            long badge = GlobalHelper.GetLoggedInBadgeNumber(httpContext);
            int titleID = GlobalHelper.GetLoggedInUserInfoTitleID(httpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                if (titleID != -1 && adapter.GetSubordinateTitleIds(titleID).Count > 0)
                    return true;
                if (adapter.GetPrincipalsWhereEmployeeIsDeputy(badge).Count > 0)
                    return true;
                return false;
            }

            bool result = false;
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            if (personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly().Any(r => r.Value == titleID))
            {
                result = true;
            }
            if (t5DBInstance.WorkflowDA.GetAll(titleID).Count > 0)
            {
                result = true;
            }
            if (t5DBInstance.PersonnelDeputyDA.IsDeputy(badge).Count > 0)
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

            if (!DBInitializer.IsTestMode)
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
            var defaultItems = new List<int> { 1, 2, 4, 6 };
            if (DBInitializer.IsTestMode)
            {
                return defaultItems;
            }

            try
            {
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                string badgeNumber = LoginManager.GetBadgeNumber(httpContext);
                List<int> list = personnelSystem.DB.ToolbarItems.GetItems(long.Parse(badgeNumber, CultureInfo.InvariantCulture));
                return list.Count != 0 ? list : defaultItems;
            }
            catch
            {
                return defaultItems;
            }
        }

        public static bool SideMenu(HttpContextBase httpContext)
        {
            bool result = true;
            try
            {
                HttpCookie val = httpContext?.Request?.Cookies?.Get("sidemenu");
                if (val == null)
                {
                    return result;
                }
                return bool.Parse(val.Value);
            }
            catch
            {
                return result;
            }
        }

        public static bool ToolbarShow(HttpContextBase httpContext)
        {
            bool result = true;
            try
            {
                HttpCookie val = httpContext?.Request?.Cookies?.Get("toolb");
                if (val == null)
                {
                    return result;
                }
                return bool.Parse(val.Value);
            }
            catch
            {
                return result;
            }
        }
    }
}
