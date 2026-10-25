using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Configuration;
using System.Web.Mvc;
using MyTime3.Models;
using Personnel;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.LeaveCalcs;
using T5V3.Model;
using TWF.Application.Leaves;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten RemainLeaveController (Phase 4).
    /// - Fixes double-counting bug in GetRemain where pending hourly leave requests of the same LeaveTypeID
    ///   were counted as BOTH +1.0 full day AND +hourly fraction.
    /// - Supports DBInitializer.IsTestMode via LeaveBalanceService.
    /// </summary>
    public class RemainLeaveController : Controller
    {
        [HttpGet]
        public RemainLeave GetRemain(long badgeNumber, int leaveTypeID, DateTime d)
        {
            d = d.Date;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null && DBInitializer.TestSettingsService != null)
            {
                var leaveSvc = new LeaveBalanceService(DBInitializer.TestBackendAdapter, DBInitializer.TestSettingsService);
                var summary = leaveSvc.CalculateLeaveBalance(badgeNumber, leaveTypeID, d);
                return new RemainLeave
                {
                    ToDate = d,
                    BadgeNumber = badgeNumber,
                    Name = summary.EmployeeFullName ?? string.Empty,
                    LeaveTypeID = leaveTypeID,
                    LeaveTypeName = summary.LeaveTypeName ?? string.Empty,
                    DailyTaken = summary.DailyTakenDays,
                    HourlyTaken = TimeSpan.FromMinutes(summary.HourlyTakenMinutes),
                    TotalTaken = summary.TotalTakenDays,
                    Requested = summary.PendingRequestedDays,
                    Remain = summary.RemainingDays,
                    MaxMinus = summary.MaxMinusDays
                };
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            var remainLeave = new RemainLeave
            {
                ToDate = d,
                BadgeNumber = badgeNumber,
                LeaveTypeID = leaveTypeID,
                DailyTaken = 0.0,
                HourlyTaken = TimeSpan.Zero,
                Requested = 0.0
            };

            var attendance = new Attendance(t5DBInstance, personnelSystem);
            var pc = new PersianCalendar();
            int year = pc.GetYear(d);
            DateTime fromDate = pc.ToDateTime(year, 1, 1, 0, 0, 0, 0);
            DateTime toDate = pc.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);

            List<LeaveType> allLeaveTypes = t5DBInstance.LeaveTypeDA.GetAll();
            List<Schedule> allSchedules = t5DBInstance.ScheduleDA.GetAll();
            List<Paramtr> allParameters = t5DBInstance.ParameterDA.GetAll();
            Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(badgeNumber);
            remainLeave.Name = byID?.ToString() ?? string.Empty;

            List<ADayResult> attendanceDays = attendance.Calculate(badgeNumber, fromDate, toDate);
            var remainCalc = new Remain(
                attendanceDays,
                allLeaveTypes,
                t5DBInstance.LeavesAddedDA.GetAll(new List<long> { badgeNumber }, fromDate, toDate),
                t5DBInstance.YearRemainDA.GetAll(badgeNumber),
                allSchedules,
                allParameters);

            ADayRemain matchedRemain = remainCalc
                .CalculateRemain(byID, leaveTypeID, year)
                .FirstOrDefault(rc => rc.ForDate == d);

            if (matchedRemain != null)
            {
                remainLeave.Remain = matchedRemain.Remain;
            }

            LeaveType leaveType = allLeaveTypes.FirstOrDefault(lt => lt.LeaveTypeID == leaveTypeID);
            for (int month = 1; month <= 12; month++)
            {
                int daysInMonth = pc.GetDaysInMonth(year, month);
                for (int day = 1; day <= daysInMonth; day++)
                {
                    DateTime targetDate = pc.ToDateTime(year, month, day, 0, 0, 0, 0);
                    ADayResult aDayResult = attendanceDays.FirstOrDefault(ya => ya.ForDate == targetDate);
                    if (aDayResult == null)
                        continue;

                    if (aDayResult.IsDailyLeave && leaveType != null)
                    {
                        if (aDayResult.DailyLeaveValue.LeaveTypeID == leaveTypeID &&
                            ((!aDayResult.IsHoliday && aDayResult.CurrentShift?.ActiveSchedule != null) || leaveType.GetInHolidays))
                        {
                            remainLeave.DailyTaken += 1.0;
                        }
                    }

                    if (aDayResult.ProcessedSchedule != null)
                    {
                        foreach (T5V3.Attendance.InShiftExit inShiftExit in aDayResult.ProcessedSchedule.InShiftExits)
                        {
                            if (inShiftExit.lvType != null)
                            {
                                if (inShiftExit.lvType.LeaveTypeID == leaveTypeID ||
                                    (inShiftExit.lvType.DecreaseFromLeaveID.HasValue && inShiftExit.lvType.DecreaseFromLeaveID.Value == leaveTypeID))
                                {
                                    remainLeave.HourlyTaken += inShiftExit.ValidLen;
                                }
                            }
                        }
                    }
                }
            }

            if (leaveType != null)
            {
                double eachDayHours = leaveType.EachXHoursADay.TotalHours > 0 ? leaveType.EachXHoursADay.TotalHours : 8.0;
                remainLeave.MaxMinus = leaveType.MaxMinus;
                remainLeave.TotalTaken = remainLeave.DailyTaken + (remainLeave.HourlyTaken.TotalHours / eachDayHours);
                remainLeave.LeaveTypeName = leaveType.LeaveName;

                foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(badgeNumber, fromDate, toDate))
                {
                    if (!item.IsSuspend || item.WorkflowType != WorkflowOperations.Leave)
                        continue;

                    LeaveType reqLeaveType = allLeaveTypes.FirstOrDefault(lt => lt.LeaveTypeID == item.LeaveID);
                    bool matchesLeaveOrLinked =
                        item.LeaveID == leaveTypeID ||
                        (reqLeaveType != null && reqLeaveType.DecreaseFromLeaveID.HasValue && reqLeaveType.DecreaseFromLeaveID.Value == leaveTypeID);

                    if (!matchesLeaveOrLinked)
                        continue;

                    // Fixed legacy double-counting bug: only add 1.0 day if item.IsDaily is true; otherwise add hourly fraction!
                    if (item.IsDaily)
                    {
                        double spanDays = Math.Max(1.0, (item.ToTime.Date - item.ForDate.Date).TotalDays + 1.0);
                        remainLeave.Requested += spanDays;
                    }
                    else
                    {
                        TimeSpan duration = item.ToTime >= item.FromTime
                            ? (item.ToTime - item.FromTime)
                            : (item.ToTime.AddHours(24.0) - item.FromTime);
                        remainLeave.Requested += duration.TotalHours / eachDayHours;
                    }
                }
            }

            return remainLeave;
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public JsonResult RemainLeaveJ(long badgeNumber, int leaveID)
        {
            RemainLeave data = RemainLeaveJj(badgeNumber, leaveID);
            return Json(data);
        }

        [AuthorizeFilter(Roles = "admin,user")]
        public RemainLeave RemainLeaveJj(long badgeNumber, int leaveID)
        {
            var result = new RemainLeave();
            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            if (loggedInBadgeNumber == -1)
                return result;

            if (loggedInBadgeNumber == badgeNumber || IsAuthorizedToViewLeaveBalance(loggedInBadgeNumber, badgeNumber))
            {
                result = GetRemain(badgeNumber, leaveID, DateTime.Now.Date);
            }
            return result;
        }

        private bool IsAuthorizedToViewLeaveBalance(long managerBadge, long employeeBadge)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var pending = DBInitializer.TestBackendAdapter.GetPendingRequestAccepterTitles();
                var ownedTitles = ConstOTController.GetOwnedTitles(null, null, managerBadge, base.HttpContext);
                foreach (var kv in pending.Where(p => ownedTitles.Contains(p.Value)))
                {
                    var req = DBInitializer.TestBackendAdapter.GetRequestById(kv.Key);
                    if (req != null && req.BadgeNumber == employeeBadge)
                        return true;
                }
                return false;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(
                personnelSystem.DB.UserInfos.GetAllUserInfoTitles(),
                personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly());

            List<int> ownedTitlesReal = ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, managerBadge, base.HttpContext);
            foreach (long reqId in requestAccepters.Where(r => ownedTitlesReal.Contains(r.Value)).Select(r => r.Key))
            {
                T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(reqId);
                if (byID != null && byID.BadgeNumber == employeeBadge && byID.WorkflowType == WorkflowOperations.Leave)
                {
                    return true;
                }
            }
            return false;
        }

        [HttpPost]
        public JsonResult RemainLeave(string userName, string password, string badgeNumbers)
        {
            var list = new List<RemainLeave>();
            long num = -1L;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                num = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password) ?? -1L;
            }
            else
            {
                num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
            }

            if (num > 0)
            {
                if (string.IsNullOrEmpty(badgeNumbers))
                {
                    badgeNumbers = num.ToString(CultureInfo.InvariantCulture);
                }

                var targetBadges = new List<long>();
                foreach (string part in badgeNumbers.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (long.TryParse(part, out long parsed))
                    {
                        targetBadges.Add(parsed);
                    }
                }

                int defaultLeaveId = (int)SettingsController.LoadValue("LeaveID", typeof(int), 1);
                foreach (long item in targetBadges)
                {
                    list.Add(GetRemain(item, defaultLeaveId, DateTime.Now.Date));
                }
            }

            return Json(list);
        }

        [HttpPost]
        public string GetForUser(string[] SelectedBadgeNumbers, bool isSelf = true, int? LeaveTypeID = null, bool? year = null)
        {
            GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
            DateTime dateTime = DateTime.Now;
            if (year.HasValue && year.Value)
            {
                var persianCalendar = new PersianCalendar();
                dateTime = persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), 1, 1, 0, 0, 0, 0).AddDays(-1.0);
            }

            AddPersonnelToViewBag();

            var list2 = new List<RemainLeave>();
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
                        if (long.TryParse(s, out long parsedBadge))
                        {
                            list2.Add(GetRemain(parsedBadge, LeaveTypeID.Value, dateTime.Date));
                        }
                    }
                }
            }

            return RenderRazorViewToString("GetForUser", list2);
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

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index(RemainLeave leaveType = null)
        {
            GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
            AddPersonnelToViewBag();

            int hideLeaveType = (int)SettingsController.LoadValue("HideLeaveTypes", typeof(int), -1);
            var dailyTypes = base.ViewBag.DailyLeaveTypes as List<SelectListItem>;
            if (hideLeaveType > 0 && dailyTypes != null)
            {
                for (int i = dailyTypes.Count - 1; i >= 0; i--)
                {
                    if (dailyTypes[i].Value != hideLeaveType.ToString(CultureInfo.InvariantCulture))
                    {
                        dailyTypes.RemoveAt(i);
                    }
                }
            }

            if ((leaveType == null || leaveType.LeaveTypeID == 0) && dailyTypes != null && dailyTypes.Count > 0)
            {
                leaveType = new RemainLeave
                {
                    LeaveTypeID = int.Parse(dailyTypes[0].Value, CultureInfo.InvariantCulture)
                };
            }

            var list = new List<RemainLeave>();
            if (leaveType != null && leaveType.LeaveTypeID > 0)
            {
                list.Add(GetRemain(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), leaveType.LeaveTypeID, DateTime.Now));
            }
            return View(list);
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult ForPersonnel(long badgeNumber, int leaveTypeID)
        {
            RemainLeave model = RemainLeaveJj(badgeNumber, leaveTypeID);
            return View(model);
        }

        private void AddPersonnelToViewBag()
        {
            long loggedInBadge = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var personnelList = RequestController.AddPersonnelToViewBag(base.HttpContext)
                    .Where(p => p.BadgeNumber != loggedInBadge)
                    .ToList();
                base.ViewBag.Personnel = personnelList;
                return;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<int> ownedTitles = ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadge, base.HttpContext);
            var list = new List<PersonnelModel>();

            foreach (Personnel.Model.UserInfo item in personnelSystem.DB.UserInfos.GetAll(GetLeaveSubUsers(ownedTitles)))
            {
                list.Add(new PersonnelModel(item.BadgeNumber, item.FirstName, item.LastName));
            }
            base.ViewBag.Personnel = list;
        }

        public static List<long> GetLeaveSubUsers(List<int> titleIDs)
        {
            if (titleIDs == null || titleIDs.Count == 0)
                return new List<long>();

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var subTitleIds = new List<int>();
                var allTitles = DBInitializer.TestBackendAdapter.GetAllTitleNodes();
                foreach (var title in allTitles)
                {
                    var wfSteps = DBInitializer.TestBackendAdapter.GetWorkflowSteps(title.TitleId, TWF.Domain.Models.TwfWorkflowType.Leave);
                    if (wfSteps.Count > 0)
                    {
                        if (wfSteps.Any(w => titleIDs.Contains(w.AccepterTitleId)))
                        {
                            subTitleIds.Add(title.TitleId);
                        }
                    }
                    else if (titleIDs.Contains(title.UpperTitleId))
                    {
                        subTitleIds.Add(title.TitleId);
                    }
                }
                return DBInitializer.TestBackendAdapter
                    .GetEmployeesByTitleIds(subTitleIds)
                    .Select(e => e.BadgeNumber)
                    .ToList();
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            var subTitleIDs = new List<int>();
            List<KeyValuePair<int, int>> allWithUppersOnly = personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly();

            foreach (Personnel.Model.TitleChart item in personnelSystem.DB.TitlesCharts.GetAll())
            {
                bool noCustomWorkflow = true;
                foreach (Workflow item2 in t5DBInstance.WorkflowDA.GetAll(item.TitleID, WorkflowOperations.Leave))
                {
                    if (titleIDs.Contains(item2.AccepterTitleID))
                    {
                        subTitleIDs.Add(item.TitleID);
                    }
                    noCustomWorkflow = false;
                }

                if (noCustomWorkflow)
                {
                    var upperEntry = allWithUppersOnly.FirstOrDefault(at => at.Key == item.TitleID);
                    if (titleIDs.Contains(upperEntry.Value))
                    {
                        subTitleIDs.Add(item.TitleID);
                    }
                }
            }

            return personnelSystem.DB.UserInfos
                .GetAllUserInfoTitles()
                .Where(ut => subTitleIDs.Contains(ut.Value))
                .Select(ut => ut.Key)
                .ToList();
        }
    }
}
