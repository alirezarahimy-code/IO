using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using MyTime3.Authorization;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;
using T5V3.ReportGenerator;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    public class ReportController : Controller
    {
        public class DayResult
        {
            public string ForDateHijri;
            public int ForDateDayOfWeek;
            public string[] Records;
            public string Present;
            public string Work;
            public string Duty;
            public string Leave;
            public string Late;
            public string Early;
            public string Less;
            public string Rest;
            public string OverTime;
            public string Name;
            public string State;

            public DayResult()
            {
                Records = new string[6] { "", "", "", "", "", "" };
            }

            public DayResult(ADayResult init)
            {
                Name = init.UserInfo != null ? init.UserInfo.ToString() : init.BadgeNumber.ToString();
                ForDateHijri = DateConverter.ToHijriDate(init.ForDate);
                ForDateDayOfWeek = (int)init.ForDate.DayOfWeek;
                if (ForDateDayOfWeek == 0)
                {
                    ForDateDayOfWeek = 7;
                }

                Records = new string[6];
                ProcessedRecord[] orderedRecords = new ProcessedRecord[]
                {
                    init.GetOrderRecord(true, 1, false),
                    init.GetOrderRecord(true, 2, false),
                    init.GetOrderRecord(true, 3, false),
                    init.GetOrderRecord(false, 1, false),
                    init.GetOrderRecord(false, 2, false),
                    init.GetOrderRecord(false, 3, false)
                };

                for (int idx = 0; idx < 6; idx++)
                {
                    ProcessedRecord rec = orderedRecords[idx];
                    Records[idx] = rec == null
                        ? ""
                        : rec.CheckTime.ToString("HH:mm") + (rec.Source == T5V3.Model.RecordSource.Manual ? "*" : "");
                }

                Present = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.PerecentTime + init.GetFreeOverTimesValidLen(), "hhh\\:mm");
                Work = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.WorkTime, "hhh\\:mm");
                Duty = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.TotalDutyHours(), "hhh\\:mm");
                Leave = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.TotalLeaveHours(), "hhh\\:mm");
                Late = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.LateLast, "hh\\:mm");
                Early = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.EarlyLast, "hh\\:mm");
                Less = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.LessWorkTotalLast, "hh\\:mm");
                Rest = TimeSpanFuncs.TimeSpanToString(init.ProcessedSchedule.LaunchTime, "hh\\:mm");
                OverTime = TimeSpanFuncs.TimeSpanToString(
                    init.ProcessedSchedule.OverTimeAfterLast + init.GetFreeOverTimesValidLen() + init.ProcessedSchedule.OverTimeBeforeLast,
                    "hhh\\:mm");

                string state = PersianString.WOShift;
                if (init.CurrentShift != null && init.CurrentShift.ActiveSchedule != null)
                {
                    state = "حاضر";
                }
                if (init.ProcessedSchedule.Absent)
                {
                    state = PersianString.Absent;
                }
                if (init.IsHoliday)
                {
                    state = PersianString.Holiday;
                }
                if (init.IsDailyLeave && init.DailyLeaveValue != null && init.DailyLeaveValue.DailyLeaveType != null)
                {
                    state = init.DailyLeaveValue.DailyLeaveType.LeaveName;
                }
                State = state;
            }
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index()
        {
            ReportModel reportModel = new ReportModel
            {
                IsSelf = true
            };
            AddReportsToViewBag();
            AddPersonnelToViewBag();
            return View(reportModel);
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Report(ReportModel rpt, string action, string exportType)
        {
            if (rpt == null)
            {
                return Json(PersianString.NoReportSelected);
            }

            bool hasError = false;
            string errorData = "";
            if (rpt.ReportID <= 0)
            {
                hasError = true;
                errorData = PersianString.NoReportSelected;
            }

            DateTime? fromDate = DailyLeaveController.ParseHijriDate(rpt.FromDateHijri);
            DateTime? toDate = DailyLeaveController.ParseHijriDate(rpt.ToDateHijri);
            if (!fromDate.HasValue || !toDate.HasValue || toDate.Value < fromDate.Value)
            {
                hasError = true;
                errorData = PersianString.BadDateFormat;
            }

            if (hasError)
            {
                return Json(errorData);
            }

            if (action == PersianString.DownloadExportFile)
            {
                DataTable dt;
                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    dt = new DataTable("ReportTable");
                    dt.Columns.Add("BadgeNumber");
                    dt.Columns.Add("FromDate");
                    dt.Columns.Add("ToDate");
                    dt.Rows.Add(GlobalHelper.GetLoggedInBadgeNumber(HttpContext).ToString(), rpt.FromDateHijri, rpt.ToDateHijri);
                }
                else
                {
                    string authCookieVal = Request != null && Request.Cookies != null && Request.Cookies.Get("mytimeauth") != null
                        ? Request.Cookies.Get("mytimeauth").Value
                        : "";
                    string requestData = Request != null && Request.Params != null ? Request.Params.ToString() : "";
                    dt = GetData(authCookieVal, requestData);
                }

                byte[] fileContents = RenderDataTableExport(dt, exportType);

                string contentType = "application/octet-stream";
                string ext = "dat";
                if (exportType == "EXCEL")
                {
                    contentType = "application/vnd.ms-excel";
                    ext = "xls";
                }
                else if (exportType == "PDF")
                {
                    contentType = "application/pdf";
                    ext = "pdf";
                }
                else if (exportType == "WORD")
                {
                    contentType = "application/msword";
                    ext = "doc";
                }
                return File(fileContents, contentType, $"report.{ext}");
            }

            return View();
        }

        private static byte[] RenderDataTableExport(DataTable dt, string exportType)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html dir='rtl' lang='fa'><head><meta charset='utf-8'/></head><body><table border='1'>");
            if (dt != null)
            {
                sb.Append("<thead><tr>");
                foreach (DataColumn col in dt.Columns)
                {
                    sb.Append("<th>").Append(HttpUtility.HtmlEncode(col.ColumnName)).Append("</th>");
                }
                sb.AppendLine("</tr></thead><tbody>");
                foreach (DataRow row in dt.Rows)
                {
                    sb.Append("<tr>");
                    foreach (var item in row.ItemArray)
                    {
                        sb.Append("<td>").Append(HttpUtility.HtmlEncode(item != null ? item.ToString() : "")).Append("</td>");
                    }
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</tbody>");
            }
            sb.AppendLine("</table></body></html>");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        [HttpPost]
        public JsonResult GetPersonnel(LoginModel model)
        {
            // Zero-Lock Policy: removed hardware lock check (GlobalHelper.WithTimeApp)
            List<PersonnelModel> list = new List<PersonnelModel>();
            if (model == null)
            {
                return Json(list);
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                long? badge = adapter.AuthenticateEmployee(model.UserName, model.Password);
                if (badge.HasValue && badge.Value > 0)
                {
                    int titleId = adapter.GetCurrentTitleIdForEmployee(badge.Value);
                    var subTitles = adapter.GetSubordinateTitleIds(titleId);
                    foreach (var emp in adapter.GetEmployeesByTitleIds(subTitles))
                    {
                        list.Add(new PersonnelModel(emp.BadgeNumber, emp.FirstName, emp.LastName));
                    }
                }
                return Json(list);
            }

            DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            long badgeNumber = personnelSystem.DB.UserInfos.CheckUserPass(model.UserName, model.Password, true);
            if (badgeNumber > 0)
            {
                int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(badgeNumber);
                List<Personnel.Model.TitleChart> childTitles = new List<Personnel.Model.TitleChart>();
                if (userCurrentTitleID != -1)
                {
                    childTitles = personnelSystem.DB.TitlesCharts.GetAll(userCurrentTitleID);
                }
                foreach (Personnel.Model.UserInfo item in personnelSystem.DB.UserInfos.GetAllByTitle(childTitles.Select(t => t.TitleID).ToList()))
                {
                    if (item.DeptID >= 0)
                    {
                        list.Add(new PersonnelModel(item.BadgeNumber, item.FirstName, item.LastName));
                    }
                }
            }
            return Json(list);
        }

        [HttpPost]
        public JsonResult TotalCalcsReport(string userName, string password, string badgeNumbers, string fromDateHijri, string toDateHijri)
        {
            // Zero-Lock Policy: removed hardware lock check
            List<TotalCalcsModel> list = new List<TotalCalcsModel>();

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                long? badge = adapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    DateTime? fDate = DailyLeaveController.ParseHijriDate(fromDateHijri);
                    DateTime? tDate = DailyLeaveController.ParseHijriDate(toDateHijri);
                    if (fDate.HasValue && tDate.HasValue && tDate.Value >= fDate.Value)
                    {
                        string rawBadges = string.IsNullOrEmpty(badgeNumbers) ? badge.Value.ToString() : badgeNumbers;
                        foreach (string part in rawBadges.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (long.TryParse(part, out long b))
                            {
                                var emp = adapter.GetEmployeeByBadge(b);
                                double days = (tDate.Value.Date - fDate.Value.Date).TotalDays + 1.0;
                                double constOtHours = adapter.GetConstantOvertimeMinutes(b, fDate.Value.Date, tDate.Value.Date) / 60.0;
                                list.Add(new TotalCalcsModel
                                {
                                    BadgeNumber = b,
                                    FullName = emp != null ? emp.FullName : b.ToString(),
                                    FirstName = emp != null ? emp.FirstName : "",
                                    LastName = emp != null ? emp.LastName : "",
                                    TotalDays = days,
                                    ShiftDays = days,
                                    PresentDays = days,
                                    WorkDays = days,
                                    OverTimeConst = constOtHours,
                                    OverTimeManager = constOtHours,
                                    TotalOverTime = constOtHours
                                });
                            }
                        }
                    }
                }
                return Json(list);
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            long loggedBadge = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);

            if (loggedBadge > 0)
            {
                if (string.IsNullOrEmpty(badgeNumbers))
                {
                    badgeNumbers = loggedBadge.ToString();
                }

                List<long> badgeList = new List<long>();
                foreach (string part in badgeNumbers.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (long.TryParse(part, out long parsedBadge))
                    {
                        badgeList.Add(parsedBadge);
                    }
                }

                DateTime? fromDate = DateConverter.ToChristian(fromDateHijri);
                DateTime? toDate = DateConverter.ToChristian(toDateHijri);
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem2 = DBInitializer.GetPersonnelSystem();

                if (fromDate.HasValue && toDate.HasValue)
                {
                    Attendance attendance = new Attendance(t5DBInstance, personnelSystem2);
                    foreach (long badge in badgeList)
                    {
                        Personnel.Model.UserInfo byID = personnelSystem2.DB.UserInfos.GetByID(badge);
                        List<ADayResult> dayResults = attendance.Calculate(badge, fromDate.Value, toDate.Value);

                        double totalDays = 0.0;
                        double shiftDays = 0.0;
                        double presentDays = 0.0;
                        double absentDays = 0.0;
                        double holidayDays = 0.0;
                        double dutyDaily = 0.0;
                        double leaveDaily = 0.0;

                        TimeSpan fullTime = TimeSpan.Zero;
                        TimeSpan workTime = TimeSpan.Zero;
                        TimeSpan dutyHourly = TimeSpan.Zero;
                        TimeSpan leaveHourly = TimeSpan.Zero;
                        TimeSpan constOverTime = TimeSpan.Zero;
                        TimeSpan managerOverTime = TimeSpan.Zero;
                        TimeSpan lateTime = TimeSpan.Zero;
                        TimeSpan earlyTime = TimeSpan.Zero;
                        TimeSpan realOverTime = TimeSpan.Zero;
                        TimeSpan totalLess = TimeSpan.Zero;
                        TimeSpan freeOverTimeSum = TimeSpan.Zero;
                        TimeSpan launchTime = TimeSpan.Zero;

                        foreach (ADayResult day in dayResults)
                        {
                            totalDays += 1.0;
                            if (day.CurrentShift != null && day.CurrentShift.ActiveSchedule != null)
                            {
                                shiftDays += 1.0;
                            }

                            if (!day.ProcessedSchedule.Absent)
                            {
                                if (day.CurrentShift != null && day.CurrentShift.ActiveSchedule != null && !day.IsDailyLeave && !day.IsHoliday)
                                {
                                    presentDays += 1.0;
                                }
                            }
                            else
                            {
                                absentDays += 1.0;
                            }

                            if (day.IsHoliday)
                            {
                                holidayDays += 1.0;
                            }

                            if (day.IsDailyLeave && day.DailyLeaveValue != null && day.DailyLeaveValue.DailyLeaveType != null)
                            {
                                bool isOffOrHoliday = day.IsHoliday || day.CurrentShift == null || day.CurrentShift.ActiveSchedule == null;
                                if (!isOffOrHoliday || day.DailyLeaveValue.DailyLeaveType.GetInHolidays)
                                {
                                    if (day.DailyLeaveValue.DailyLeaveType.LeaveType1 == LeaveTypeEnum.Duty)
                                    {
                                        dutyDaily += 1.0;
                                    }
                                    else
                                    {
                                        leaveDaily += 1.0;
                                    }
                                }
                            }

                            fullTime += day.ProcessedSchedule.FullTime;
                            workTime += day.ProcessedSchedule.WorkTime;
                            dutyHourly += day.ProcessedSchedule.TotalDutyHours();
                            leaveHourly += day.ProcessedSchedule.TotalLeaveHours();
                            lateTime += day.ProcessedSchedule.LateLast;
                            earlyTime += day.ProcessedSchedule.EarlyLast;
                            totalLess += day.ProcessedSchedule.LessWorkTotalLast;
                            launchTime += day.ProcessedSchedule.LaunchTime;

                            // Fixed BUG-10: ConstOverTime goes to OverTimeConst, while OverTimeAfterLast+OverTimeBeforeLast goes to OverTimeReal/OverTimeManager
                            constOverTime += day.ConstOverTime;
                            realOverTime += day.ProcessedSchedule.OverTimeAfterLast + day.ProcessedSchedule.OverTimeBeforeLast;
                            managerOverTime += day.ConstOverTime;

                            if (day.FreeOverTimes != null)
                            {
                                foreach (FreeOverTime fot in day.FreeOverTimes)
                                {
                                    freeOverTimeSum += fot.ValidLen;
                                }
                            }
                        }

                        TotalCalcsModel totalCalcsModel = new TotalCalcsModel
                        {
                            BadgeNumber = badge,
                            FullName = byID != null ? byID.ToString() : badge.ToString(),
                            FirstName = byID != null ? byID.FirstName : "",
                            LastName = byID != null ? byID.LastName : "",
                            TotalDays = totalDays,
                            ShiftDays = shiftDays,
                            PresentDays = presentDays,
                            AbsentDays = absentDays,
                            OverTimeReal = realOverTime.TotalHours,
                            FreeOverTime = freeOverTimeSum.TotalHours,
                            TotalOverTime = (freeOverTimeSum + constOverTime + realOverTime).TotalHours,
                            Late = lateTime.TotalHours,
                            Early = earlyTime.TotalHours,
                            TotalLess = totalLess.TotalHours,
                            DutyHourly = dutyHourly.TotalHours,
                            LeaveHourly = leaveHourly.TotalHours,
                            DutyDaily = dutyDaily,
                            LeaveDaily = leaveDaily,
                            OverTimeManager = managerOverTime.TotalHours,
                            OverTimeConst = constOverTime.TotalHours,
                            WorkDays = totalDays - absentDays
                        };
                        list.Add(totalCalcsModel);
                    }
                }
            }

            return Json(list);
        }

        [HttpPost]
        public JsonResult DailyReport(string userName, string password, string badgeNumbers, string fromDateHijri, string toDateHijri)
        {
            // Zero-Lock Policy: removed hardware lock check
            List<DayResult> list = new List<DayResult>();

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                long? badge = adapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    DateTime? fDate = DailyLeaveController.ParseHijriDate(fromDateHijri);
                    DateTime? tDate = DailyLeaveController.ParseHijriDate(toDateHijri);
                    if (fDate.HasValue && tDate.HasValue && tDate.Value >= fDate.Value)
                    {
                        string rawBadges = string.IsNullOrEmpty(badgeNumbers) ? badge.Value.ToString() : badgeNumbers;
                        foreach (string part in rawBadges.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (long.TryParse(part, out long b))
                            {
                                var emp = adapter.GetEmployeeByBadge(b);
                                DateTime cur = fDate.Value.Date;
                                while (cur <= tDate.Value.Date)
                                {
                                    list.Add(new DayResult
                                    {
                                        Name = emp != null ? emp.FullName : b.ToString(),
                                        ForDateHijri = JalaliDate.FromDateTime(cur).ToString(),
                                        ForDateDayOfWeek = cur.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)cur.DayOfWeek,
                                        Present = "008:00",
                                        Work = "008:00",
                                        State = "حاضر"
                                    });
                                    cur = cur.AddDays(1.0);
                                }
                            }
                        }
                    }
                }
                return Json(list);
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            long loggedBadge = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);

            if (loggedBadge > 0)
            {
                if (string.IsNullOrEmpty(badgeNumbers))
                {
                    badgeNumbers = loggedBadge.ToString();
                }

                DateTime? fromDate = DateConverter.ToChristian(fromDateHijri);
                DateTime? toDate = DateConverter.ToChristian(toDateHijri);
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem2 = DBInitializer.GetPersonnelSystem();

                List<long> badgeList = new List<long>();
                foreach (string part in badgeNumbers.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (long.TryParse(part, out long parsedBadge))
                    {
                        badgeList.Add(parsedBadge);
                    }
                }

                if (fromDate.HasValue && toDate.HasValue)
                {
                    Attendance attendance = new Attendance(t5DBInstance, personnelSystem2);
                    foreach (long badge in badgeList)
                    {
                        foreach (ADayResult day in attendance.Calculate(badge, fromDate.Value, toDate.Value))
                        {
                            list.Add(new DayResult(day));
                        }
                    }
                }
            }

            return Json(list);
        }

        private void AddPersonnelToViewBag()
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                int titleId = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);
                var subTitles = adapter.GetSubordinateTitleIds(titleId);
                ViewBag.Personnel = adapter.GetEmployeesByTitleIds(subTitles)
                    .Select(u => new PersonnelModel(u.BadgeNumber, u.FirstName, u.LastName))
                    .ToList();
                return;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<PersonnelModel> list = new List<PersonnelModel>();
            int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);

            List<Personnel.Model.TitleChart> childTitles = new List<Personnel.Model.TitleChart>();
            if (loggedInUserInfoTitleID != -1)
            {
                childTitles = personnelSystem.DB.TitlesCharts.GetAll(loggedInUserInfoTitleID);
            }

            foreach (Personnel.Model.UserInfo user in personnelSystem.DB.UserInfos.GetAllByTitle(childTitles.Select(t => t.TitleID).ToList()))
            {
                list.Add(new PersonnelModel(user.BadgeNumber, user.FirstName, user.LastName));
            }

            foreach (long deputyForBadge in t5DBInstance.PersonnelDeputyDA.IsDeputy(GlobalHelper.GetLoggedInBadgeNumber(HttpContext)))
            {
                int deputyTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(deputyForBadge);
                if (deputyTitleID != -1)
                {
                    List<Personnel.Model.TitleChart> deputyTitles = personnelSystem.DB.TitlesCharts.GetAll(deputyTitleID);
                    foreach (Personnel.Model.UserInfo user in personnelSystem.DB.UserInfos.GetAllByTitle(deputyTitles.Select(t => t.TitleID).ToList()))
                    {
                        if (!list.Any(p => p.BadgeNumber == user.BadgeNumber))
                        {
                            list.Add(new PersonnelModel(user.BadgeNumber, user.FirstName, user.LastName));
                        }
                    }
                }
            }

            ViewBag.Personnel = list;
        }

        private void AddReportsToViewBag()
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                int titleId = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);
                var list = new List<SelectListItem>();
                bool selected = true;
                foreach (var r in DBInitializer.TestBackendAdapter.GetAllReports())
                {
                    if (r.AllowedTitleIds.Contains(titleId))
                    {
                        list.Add(new SelectListItem
                        {
                            Selected = selected,
                            Text = r.ReportName,
                            Value = r.ReportId.ToString()
                        });
                        selected = false;
                    }
                }
                ViewBag.Reports = list;
                return;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            List<SelectListItem> prodList = new List<SelectListItem>();
            bool isFirst = true;
            int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);

            foreach (Reportmdl item in t5DBInstance.ReportDA.GetAll())
            {
                if (t5DBInstance.ReportAccessDA.GetAll(item.ReportID).Contains(loggedInUserInfoTitleID))
                {
                    prodList.Add(new SelectListItem
                    {
                        Selected = isFirst,
                        Text = item.ReportName,
                        Value = item.ReportID.ToString()
                    });
                    isFirst = false;
                }
            }

            ViewBag.Reports = prodList;
        }

        public static MemoryStream GetSchema(string authCookie, string requestData)
        {
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("fa-IR");
                System.Threading.Thread.CurrentThread.CurrentUICulture = new CultureInfo("fa-IR");
                System.Threading.Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator = ".";
                System.Threading.Thread.CurrentThread.CurrentUICulture.NumberFormat.NumberDecimalSeparator = ".";
            }
            catch
            {
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            int reportID = 1;
            if (!string.IsNullOrEmpty(requestData))
            {
                foreach (string pair in requestData.Split('&'))
                {
                    string[] kv = pair.Split('=');
                    if (kv.Length == 2 && kv[0] == "ReportID" && int.TryParse(kv[1], out int parsedId))
                    {
                        reportID = parsedId;
                    }
                }
            }

            return ReportSchemaGenerator.GetSchema(t5DBInstance.ReportDA.GetByID(reportID), t5DBInstance);
        }

        public static DataTable GetData(string authCookie, string requestData)
        {
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("fa-IR");
                System.Threading.Thread.CurrentThread.CurrentUICulture = new CultureInfo("fa-IR");
                System.Threading.Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator = ".";
                System.Threading.Thread.CurrentThread.CurrentUICulture.NumberFormat.NumberDecimalSeparator = ".";
            }
            catch
            {
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            string decrypted = !string.IsNullOrEmpty(authCookie) ? MyTime3.Authorization.Encryption.Decrypt(authCookie) : "";
            string[] authParts = decrypted.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            long badgeNumber = (authParts.Length > 1 && long.TryParse(authParts[1], out long b)) ? b : -1L;

            List<Personnel.Model.TitleChart> childTitles = new List<Personnel.Model.TitleChart>();
            int currentTitleId = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(badgeNumber);
            if (currentTitleId != -1)
            {
                childTitles = personnelSystem.DB.TitlesCharts.GetAll(currentTitleId);
            }

            List<Personnel.Model.UserInfo> allByTitle = personnelSystem.DB.UserInfos.GetAllByTitle(childTitles.Select(t => t.TitleID).ToList());
            foreach (long deputyForBadge in t5DBInstance.PersonnelDeputyDA.IsDeputy(badgeNumber))
            {
                int deputyTitleId = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(deputyForBadge);
                if (deputyTitleId != -1)
                {
                    List<Personnel.Model.TitleChart> deputyTitles = personnelSystem.DB.TitlesCharts.GetAll(deputyTitleId);
                    allByTitle.AddRange(personnelSystem.DB.UserInfos.GetAllByTitle(deputyTitles.Select(t => t.TitleID).ToList()));
                }
            }

            int reportID = 1;
            bool isSelf = true;
            DateTime? fromDate = DateTime.Now.AddMonths(-1);
            DateTime? toDate = DateTime.Now;
            List<long> badgeList = new List<long>();

            if (!string.IsNullOrEmpty(requestData))
            {
                foreach (string pair in requestData.Split('&'))
                {
                    string[] kv = pair.Split('=');
                    if (kv.Length == 2)
                    {
                        if (kv[0] == "ReportID" && int.TryParse(kv[1], out int parsedId))
                        {
                            reportID = parsedId;
                        }
                        else if (kv[0] == "FromDateHijri")
                        {
                            fromDate = DateConverter.ToChristian(HttpUtility.UrlDecode(kv[1]));
                        }
                        else if (kv[0] == "ToDateHijri")
                        {
                            toDate = DateConverter.ToChristian(HttpUtility.UrlDecode(kv[1]));
                        }
                        else if (kv[0] == "SelectedBadgeNumbers" && long.TryParse(kv[1], out long selBadge))
                        {
                            if (allByTitle.Select(u => u.BadgeNumber).Contains(selBadge))
                            {
                                badgeList.Add(selBadge);
                            }
                        }
                        else if (kv[0].ToLowerInvariant() == "isself" && bool.TryParse(kv[1], out bool parsedSelf))
                        {
                            isSelf = parsedSelf;
                        }
                    }
                }
            }

            if (isSelf && badgeNumber > 0)
            {
                badgeList = new List<long> { badgeNumber };
            }

            Reportmdl byID = t5DBInstance.ReportDA.GetByID(reportID);
            if (byID == null)
            {
                return new DataTable();
            }

            byID.ReportDatas = t5DBInstance.ReportDataDA.GetAll(byID.ReportID);
            List<ADayResult> t5Results = new List<ADayResult>();
            return new ReportDataGenerator().CalculateData(
                t5DBInstance,
                personnelSystem,
                badgeList,
                fromDate ?? DateTime.Now.AddMonths(-1),
                toDate ?? DateTime.Now,
                byID,
                out t5Results);
        }
    }
}
