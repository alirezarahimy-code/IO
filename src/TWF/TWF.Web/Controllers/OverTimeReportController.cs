using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    public class OverTimeReportController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index(ReportModel model)
        {
            if (model == null)
            {
                model = new ReportModel();
            }

            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);
            List<PersonnelModel> list = new List<PersonnelModel>();

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                int titleId = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);
                var subTitles = adapter.GetSubordinateTitleIds(titleId, TwfWorkflowType.OverTime);
                foreach (var emp in adapter.GetEmployeesByTitleIds(subTitles))
                {
                    list.Add(new PersonnelModel(emp.BadgeNumber, emp.FirstName, emp.LastName));
                }
                ViewBag.Personnel = list;
                return View(model);
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();

            foreach (long overTimeSubUser in ConstOTController.GetOverTimeSubUsers(
                ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, HttpContext)))
            {
                Personnel.Model.UserInfo byID = personnelSystem.DB.UserInfos.GetByID(overTimeSubUser);
                if (byID != null)
                {
                    list.Add(new PersonnelModel(overTimeSubUser, byID.FirstName, byID.LastName));
                }
            }

            ViewBag.Personnel = list;
            return View(model);
        }

        public static bool HasAnyUsers(HttpContextBase httpContext)
        {
            try
            {
                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    int titleId = GlobalHelper.GetLoggedInUserInfoTitleID(httpContext);
                    var subTitles = DBInitializer.TestBackendAdapter.GetSubordinateTitleIds(titleId, TwfWorkflowType.OverTime);
                    return DBInitializer.TestBackendAdapter.GetEmployeesByTitleIds(subTitles).Count > 0;
                }

                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(httpContext);
                return ConstOTController.GetOverTimeSubUsers(
                    ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, httpContext)).Count > 0;
            }
            catch
            {
                return false;
            }
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Show(string[] selectedBadgeNumbers, string fromDateHijri, string toDateHijri, bool showAllDays = false)
        {
            if (selectedBadgeNumbers == null)
            {
                selectedBadgeNumbers = new string[0];
            }

            ViewBag.selectedBadgeNumbers = selectedBadgeNumbers;
            ViewBag.fromDateHijri = fromDateHijri;
            ViewBag.toDateHijri = toDateHijri;

            DateTime? fromDate = DailyLeaveController.ParseHijriDate(fromDateHijri);
            DateTime? toDate = DailyLeaveController.ParseHijriDate(toDateHijri);
            if (!fromDate.HasValue || !toDate.HasValue || toDate.Value < fromDate.Value)
            {
                ReportModel reportModel = new ReportModel
                {
                    ErrorMessage = PersianString.BadDateFormat
                };
                return RedirectToAction("Index", reportModel);
            }

            List<long> badgeList = new List<long>();
            foreach (string rawBadge in selectedBadgeNumbers)
            {
                if (long.TryParse(rawBadge, out long parsedBadge))
                {
                    badgeList.Add(parsedBadge);
                }
            }

            List<OverTimeForDay> list = new List<OverTimeForDay>();

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                foreach (long b in badgeList)
                {
                    var emp = adapter.GetEmployeeByBadge(b);
                    DateTime cur = fromDate.Value.Date;
                    while (cur <= toDate.Value.Date)
                    {
                        list.Add(new OverTimeForDay
                        {
                            BadgeNumber = b,
                            Name = emp != null ? emp.FullName : b.ToString(),
                            ForDate = JalaliDate.FromDateTime(cur).ToString(),
                            BeforeValueReal = "01:00",
                            BeforeValueLast = "01:00",
                            AfterValueReal = "02:00",
                            AfterValueLast = "02:00"
                        });
                        cur = cur.AddDays(1.0);
                    }
                }
                return View(list);
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            Attendance attendance = new Attendance(t5DBInstance, personnelSystem);

            foreach (ADayResult item in attendance.Calculate(badgeList, fromDate.Value, toDate.Value))
            {
                OverTimeForDay overTimeForDay = new OverTimeForDay
                {
                    BadgeNumber = item.BadgeNumber,
                    Name = item.UserInfo != null ? item.UserInfo.ToString() : item.BadgeNumber.ToString(),
                    ForDate = DateConverter.ToHijriDate(item.ForDate)
                };

                if (item.ProcessedSchedule != null)
                {
                    overTimeForDay.AfterValueLast = Misc.TimeSpanToString(item.ProcessedSchedule.OverTimeAfterLast);
                    overTimeForDay.AfterValueReal = (item.ProcessedSchedule.OverTimeAfterReal != default(TimeSpan))
                        ? Misc.TimeSpanToString(item.ProcessedSchedule.OverTimeAfterReal)
                        : "-";
                    overTimeForDay.BeforeValueReal = (item.ProcessedSchedule.OverTimeBeforeReal != default(TimeSpan))
                        ? Misc.TimeSpanToString(item.ProcessedSchedule.OverTimeBeforeReal)
                        : "-";
                    overTimeForDay.BeforeValueLast = Misc.TimeSpanToString(item.ProcessedSchedule.OverTimeBeforeLast);

                    if (showAllDays ||
                        item.ProcessedSchedule.OverTimeBeforeLast != default(TimeSpan) ||
                        item.ProcessedSchedule.OverTimeBeforeReal != default(TimeSpan) ||
                        item.ProcessedSchedule.OverTimeAfterLast != default(TimeSpan) ||
                        item.ProcessedSchedule.OverTimeAfterReal != default(TimeSpan))
                    {
                        list.Add(overTimeForDay);
                    }
                }
                else if (showAllDays)
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
            if (allBadgeNumbers == null)
            {
                allBadgeNumbers = new string[0];
            }

            DateTime? parsedDate = DailyLeaveController.ParseHijriDate(forDate);
            if (parsedDate.HasValue && (!DBInitializer.IsTestMode || DBInitializer.TestBackendAdapter == null))
            {
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);
                TimeSpan timeSpan = Misc.StringToTimeSpan(requredValue);

                if (!ConstOTController.GetOverTimeSubUsers(
                    ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, HttpContext)).Contains(badgeNumber))
                {
                    return View("Error", "", PersianString.AccessDenied);
                }
                if (actionType == PersianString.Cancel)
                {
                    timeSpan = new TimeSpan(0L);
                }
                t5DBInstance.EditedValueDA.InsertOrEdit(
                    new EditedValue(badgeNumber, EditValueType.OTBefore, parsedDate.Value, timeSpan),
                    out string _);
            }

            string query = $"fromDateHijri={fromDateHijri}&toDateHijri={toDateHijri}";
            foreach (string str in allBadgeNumbers)
            {
                query += "&selectedBadgeNumbers=" + str;
            }
            return Redirect($"~/OverTimeReport/Show?{query}");
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult ChangeAfter(long badgeNumber, string forDate, string requredValue, string actionType, string[] allBadgeNumbers, string fromDateHijri, string toDateHijri)
        {
            if (allBadgeNumbers == null)
            {
                allBadgeNumbers = new string[0];
            }

            DateTime? parsedDate = DailyLeaveController.ParseHijriDate(forDate);
            if (parsedDate.HasValue && (!DBInitializer.IsTestMode || DBInitializer.TestBackendAdapter == null))
            {
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);
                TimeSpan timeSpan = Misc.StringToTimeSpan(requredValue);

                if (!ConstOTController.GetOverTimeSubUsers(
                    ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, HttpContext)).Contains(badgeNumber))
                {
                    return View("Error", "", PersianString.AccessDenied);
                }
                if (actionType == PersianString.Cancel)
                {
                    timeSpan = new TimeSpan(0L);
                }
                t5DBInstance.EditedValueDA.InsertOrEdit(
                    new EditedValue(badgeNumber, EditValueType.OTAfter, parsedDate.Value, timeSpan),
                    out string _);
            }

            string query = $"fromDateHijri={fromDateHijri}&toDateHijri={toDateHijri}";
            foreach (string str in allBadgeNumbers)
            {
                query += "&selectedBadgeNumbers=" + str;
            }
            return Redirect($"~/OverTimeReport/Show?{query}");
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult AcceptBefore(long badgeNumber, string forDate, string requredValue)
        {
            DateTime? parsedDate = DailyLeaveController.ParseHijriDate(forDate);
            if (TimeSpan.TryParse(requredValue, out TimeSpan result) && parsedDate.HasValue && (!DBInitializer.IsTestMode || DBInitializer.TestBackendAdapter == null))
            {
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

                if (!ConstOTController.GetOverTimeSubUsers(
                    ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, HttpContext)).Contains(badgeNumber))
                {
                    return View("Error", "", PersianString.AccessDenied);
                }
                t5DBInstance.AllowValueDA.AddOrEdit(new AllowedValue(badgeNumber, parsedDate.Value, AllowType.OverTimeBefore, result));
            }
            return RedirectToAction("Index");
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult CancelBefore(long badgeNumber, string forDate)
        {
            DateTime? parsedDate = DailyLeaveController.ParseHijriDate(forDate);
            if (parsedDate.HasValue && (!DBInitializer.IsTestMode || DBInitializer.TestBackendAdapter == null))
            {
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

                if (!ConstOTController.GetOverTimeSubUsers(
                    ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, HttpContext)).Contains(badgeNumber))
                {
                    return View("Error", "", PersianString.AccessDenied);
                }
                t5DBInstance.AllowValueDA.Delete(new AllowedValue(badgeNumber, parsedDate.Value, AllowType.OverTimeBefore, default(TimeSpan)));
            }
            return RedirectToAction("Index");
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult AcceptAfter(long badgeNumber, string forDate, string requredValue)
        {
            DateTime? parsedDate = DailyLeaveController.ParseHijriDate(forDate);
            if (TimeSpan.TryParse(requredValue, out TimeSpan result) && parsedDate.HasValue && (!DBInitializer.IsTestMode || DBInitializer.TestBackendAdapter == null))
            {
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

                if (!ConstOTController.GetOverTimeSubUsers(
                    ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, HttpContext)).Contains(badgeNumber))
                {
                    return View("Error", "", PersianString.AccessDenied);
                }
                t5DBInstance.AllowValueDA.AddOrEdit(new AllowedValue(badgeNumber, parsedDate.Value, AllowType.OverTimeAfter, result));
            }
            return RedirectToAction("Index");
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult CancelAfter(long badgeNumber, string forDate)
        {
            DateTime? parsedDate = DailyLeaveController.ParseHijriDate(forDate);
            if (parsedDate.HasValue && (!DBInitializer.IsTestMode || DBInitializer.TestBackendAdapter == null))
            {
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

                if (!ConstOTController.GetOverTimeSubUsers(
                    ConstOTController.GetOwnedTitles(t5DBInstance, personnelSystem, loggedInBadgeNumber, HttpContext)).Contains(badgeNumber))
                {
                    return View("Error", "", PersianString.AccessDenied);
                }
                t5DBInstance.AllowValueDA.Delete(new AllowedValue(badgeNumber, parsedDate.Value, AllowType.OverTimeAfter, default(TimeSpan)));
            }
            return RedirectToAction("Index");
        }
    }
}
