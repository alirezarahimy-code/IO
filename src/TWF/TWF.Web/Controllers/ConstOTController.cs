using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten ConstOTController (Phase 4).
    /// - Fixes Persian month boundary calculation when pre=true (computes exact GetDaysInMonth of previous month instead of AddMonths(-1) on end date).
    /// - Fixes FirstOrDefault safety in GetOverTimeSubUsers.
    /// - Supports DBInitializer.IsTestMode.
    /// </summary>
    public class ConstOTController : Controller
    {
        private static void ComputeTargetMonthRange(bool pre, out DateTime fromDate, out DateTime toDate)
        {
            var pc = new PersianCalendar();
            int year = pc.GetYear(DateTime.Now);
            int month = pc.GetMonth(DateTime.Now);
            if (pre)
            {
                month--;
                if (month < 1)
                {
                    month = 12;
                    year--;
                }
            }
            int daysInMonth = pc.GetDaysInMonth(year, month);
            fromDate = pc.ToDateTime(year, month, 1, 0, 0, 0, 0);
            toDate = pc.ToDateTime(year, month, daysInMonth, 0, 0, 0, 0);
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index(bool pre = false)
        {
            base.ViewBag.Pre = pre;
            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            var list = new List<SumConstOT>();

            ComputeTargetMonthRange(pre, out DateTime fromDate, out DateTime toDate);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                List<int> ownedTitles = GetOwnedTitles(null, null, loggedInBadgeNumber, base.HttpContext);
                foreach (long subBadge in GetOverTimeSubUsers(ownedTitles))
                {
                    var emp = DBInitializer.TestBackendAdapter.GetEmployeeByBadge(subBadge);
                    var sumItem = new SumConstOT
                    {
                        BadgeNumber = subBadge,
                        Name = emp?.FullName ?? string.Empty
                    };

                    bool first = true;
                    foreach (OTConstDaily daily in GetAllDailyConstOT(subBadge, fromDate, toDate))
                    {
                        if (first)
                        {
                            sumItem.FirstDayValue = daily.ValueDaily;
                            first = false;
                        }
                        sumItem.SumValue += daily.ValueDaily;
                    }
                    list.Add(sumItem);
                }
                return View(list);
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<int> ownedTitlesReal = GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext);

            foreach (long overTimeSubUser in GetOverTimeSubUsers(ownedTitlesReal))
            {
                var sumConstOT = new SumConstOT
                {
                    BadgeNumber = overTimeSubUser,
                    Name = personnelSystem.DB.UserInfos.GetByID(overTimeSubUser)?.ToString() ?? string.Empty
                };

                bool first = true;
                foreach (OTConstDaily item in GetAllDailyConstOT(overTimeSubUser, fromDate, toDate))
                {
                    if (first)
                    {
                        sumConstOT.FirstDayValue = item.ValueDaily;
                        first = false;
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
            base.ViewBag.ExMessage = error ?? string.Empty;
            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);

            List<int> ownedTitles = DBInitializer.IsTestMode
                ? GetOwnedTitles(null, null, loggedInBadgeNumber, base.HttpContext)
                : GetOwnedTitles(DBInitializer.GetT5DBInstance(), DBInitializer.GetPersonnelSystem(), loggedInBadgeNumber, base.HttpContext);

            var model = new List<OTConstDaily>();
            if (GetOverTimeSubUsers(ownedTitles).Contains(badgeNumber))
            {
                ComputeTargetMonthRange(pre, out DateTime fromDate, out DateTime toDate);
                model = GetAllDailyConstOT(badgeNumber, fromDate, toDate);
            }
            return View(model);
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Edit(long badgeNumber, string forDate, string newValue)
        {
            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            TimeSpan duration = Misc.StringToTimeSpan(newValue);
            string error = string.Empty;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                List<int> ownedTitles = GetOwnedTitles(null, null, loggedInBadgeNumber, base.HttpContext);
                if (GetOverTimeSubUsers(ownedTitles).Contains(badgeNumber))
                {
                    DateTime? parsedDate = DailyLeaveController.ParseHijriDate(forDate);
                    if (parsedDate.HasValue)
                    {
                        DBInitializer.TestBackendAdapter.UpsertConstantOvertime(badgeNumber, parsedDate.Value.Date, (int)Math.Round(duration.TotalMinutes));
                    }
                }
                return RedirectToAction("Edit", new { badgeNumber, error });
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<int> ownedTitlesReal = GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext);

            if (GetOverTimeSubUsers(ownedTitlesReal).Contains(badgeNumber))
            {
                DateTime? targetDate = DailyLeaveController.ParseHijriDate(forDate);
                if (targetDate.HasValue)
                {
                    var pc = new PersianCalendar();
                    int y = pc.GetYear(targetDate.Value);
                    int m = pc.GetMonth(targetDate.Value);
                    DateTime fromDate = pc.ToDateTime(y, m, 1, 0, 0, 0, 0);
                    DateTime toDate = pc.ToDateTime(y, m, pc.GetDaysInMonth(y, m), 0, 0, 0, 0);

                    TimeSpan currentMonthTotal = TimeSpan.Zero;
                    foreach (OTConstDaily item in GetAllDailyConstOT(badgeNumber, fromDate, toDate))
                    {
                        if (item.ForDate != forDate)
                        {
                            currentMonthTotal += item.ValueDaily;
                        }
                    }

                    Rule activeRule = new RuleProcessor(
                        t5DBInstance.RuleDA.GetAll(),
                        t5DBInstance.DepartmentRuleDA.GetAll(),
                        t5DBInstance.UserRuleDA.GetAll())
                        .GetActiveRule(personnelSystem.DB.UserInfos.GetByID(badgeNumber), null, DateTime.Now);

                    if (activeRule != null)
                    {
                        if (activeRule.ConstOTMaxMonthly > TimeSpan.Zero && (currentMonthTotal + duration) > activeRule.ConstOTMaxMonthly)
                        {
                            error = PersianString.MonthlyOTConstLimit;
                        }
                        else if (activeRule.ConstOTMaxDaily > TimeSpan.Zero && duration > activeRule.ConstOTMaxDaily)
                        {
                            error = PersianString.DailyOTConstLimit;
                        }
                        else
                        {
                            var editedValue = new EditedValue
                            {
                                BadgeNumber = badgeNumber,
                                ForDate = targetDate.Value,
                                ValidDuration = duration,
                                ValueType = EditValueType.OTConst
                            };
                            t5DBInstance.EditedValueDA.InsertOrEdit(editedValue, out _);
                        }
                    }
                    else
                    {
                        error = PersianString.DefineUserRule;
                    }
                }
            }

            return RedirectToAction("Edit", new
            {
                badgeNumber,
                error
            });
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult EditAll(string[] badgeNumbers, string[] edittedValues, bool pre = false)
        {
            if (badgeNumbers == null || edittedValues == null)
            {
                return RedirectToAction("Index", new { pre });
            }

            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            ComputeTargetMonthRange(pre, out DateTime fromDate, out DateTime toDate);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                List<int> ownedTitles = GetOwnedTitles(null, null, loggedInBadgeNumber, base.HttpContext);
                var subUsers = GetOverTimeSubUsers(ownedTitles);
                for (int i = 0; i < badgeNumbers.Length && i < edittedValues.Length; i++)
                {
                    if (long.TryParse(badgeNumbers[i], out long badge) && subUsers.Contains(badge))
                    {
                        TimeSpan ts = Misc.StringToTimeSpan(edittedValues[i]);
                        DBInitializer.TestBackendAdapter.UpsertConstantOvertime(badge, fromDate, (int)Math.Round(ts.TotalMinutes));
                    }
                }
                return RedirectToAction("Index", new { pre });
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<int> ownedTitlesReal = GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, base.HttpContext);
            List<long> allowedBadges = GetOverTimeSubUsers(ownedTitlesReal);

            for (int i = 0; i < badgeNumbers.Length && i < edittedValues.Length; i++)
            {
                TimeSpan timeSpan = Misc.StringToTimeSpan(edittedValues[i]);
                if (long.TryParse(badgeNumbers[i], out long result) && allowedBadges.Contains(result))
                {
                    Rule activeRule = new RuleProcessor(
                        t5DBInstance.RuleDA.GetAll(),
                        t5DBInstance.DepartmentRuleDA.GetAll(),
                        t5DBInstance.UserRuleDA.GetAll())
                        .GetActiveRule(personnelSystem.DB.UserInfos.GetByID(result), null, DateTime.Now);

                    if (activeRule != null && (activeRule.ConstOTMaxMonthly <= TimeSpan.Zero || timeSpan <= activeRule.ConstOTMaxMonthly))
                    {
                        var editedValue = new EditedValue
                        {
                            BadgeNumber = result,
                            ForDate = fromDate,
                            ValidDuration = timeSpan,
                            ValueType = EditValueType.OTConst
                        };
                        t5DBInstance.EditedValueDA.InsertOrEdit(editedValue, out _);
                    }
                }
            }

            return RedirectToAction("Index", new { pre });
        }

        private List<OTConstDaily> GetAllDailyConstOT(long badgeNumber, DateTime fromDate, DateTime toDate)
        {
            var list = new List<OTConstDaily>();
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                DateTime cursor = fromDate.Date;
                while (cursor <= toDate.Date)
                {
                    int mins = DBInitializer.TestBackendAdapter.GetConstantOvertimeMinutes(badgeNumber, cursor, cursor);
                    list.Add(new OTConstDaily
                    {
                        BadgeNumber = badgeNumber,
                        ForDate = JalaliDate.FromDateTime(cursor).ToString(),
                        DayOfWeek = Misc.DayOfWeekToString(cursor.DayOfWeek),
                        ValueDaily = TimeSpan.FromMinutes(mins)
                    });
                    cursor = cursor.AddDays(1.0);
                }
                return list;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            List<EditedValue> all = t5DBInstance.EditedValueDA.GetAll(new List<long> { badgeNumber }, fromDate, toDate);

            DateTime current = fromDate;
            while (current <= toDate)
            {
                var oTConstDaily = new OTConstDaily
                {
                    BadgeNumber = badgeNumber,
                    ForDate = DateConverter.ToHijriDate(current),
                    DayOfWeek = Misc.DayOfWeekToString(current.DayOfWeek),
                    ValueDaily = TimeSpan.Zero
                };

                EditedValue matched = all.FirstOrDefault(ev => ev.ForDate == current && ev.ValueType == EditValueType.OTConst);
                if (matched != null)
                {
                    oTConstDaily.ValueDaily = matched.ValidDuration;
                }
                list.Add(oTConstDaily);
                current = current.AddDays(1.0);
            }
            return list;
        }

        public static List<int> GetOwnedTitles(T5V3DBManager db, PersonnelSystem ps, long badge, HttpContextBase httpContext)
        {
            var list = new List<int>();
            int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(httpContext);
            if (loggedInUserInfoTitleID != -1)
            {
                list.Add(loggedInUserInfoTitleID);
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                foreach (long principal in DBInitializer.TestBackendAdapter.GetPrincipalsWhereEmployeeIsDeputy(badge))
                {
                    int pTitle = DBInitializer.TestBackendAdapter.GetCurrentTitleIdForEmployee(principal);
                    if (pTitle != -1 && !list.Contains(pTitle))
                    {
                        list.Add(pTitle);
                    }
                }
                return list;
            }

            if (db != null && ps != null)
            {
                foreach (long item in db.PersonnelDeputyDA.IsDeputy(badge))
                {
                    int userCurrentTitleID = ps.DB.UserInfos.GetUserCurrentTitleID(item);
                    if (userCurrentTitleID != -1 && !list.Contains(userCurrentTitleID))
                    {
                        list.Add(userCurrentTitleID);
                    }
                }
            }
            return list;
        }

        public static List<long> GetOverTimeSubUsers(List<int> titleIDs)
        {
            if (titleIDs == null || titleIDs.Count == 0)
                return new List<long>();

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var subTitleIds = new List<int>();
                var allTitles = DBInitializer.TestBackendAdapter.GetAllTitleNodes();
                foreach (var title in allTitles)
                {
                    var wfSteps = DBInitializer.TestBackendAdapter.GetWorkflowSteps(title.TitleId, TwfWorkflowType.AllowOverTimes);
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
                bool flag = true;
                foreach (Workflow item2 in t5DBInstance.WorkflowDA.GetAll(item.TitleID, WorkflowOperations.AllowOverTimes))
                {
                    if (titleIDs.Contains(item2.AccepterTitleID))
                    {
                        subTitleIDs.Add(item.TitleID);
                    }
                    flag = false;
                }
                if (flag)
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
