using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Personnel;
using Personnel.Model;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    public class LeavesReportController : Controller
    {
        public class LV
        {
            public long BadgeNumber { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public string FromTo { get; set; }
            public string Descriptions { get; set; }
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index()
        {
            List<LV> list = new List<LV>();
            if (isObs(HttpContext))
            {
                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    var adapter = DBInitializer.TestBackendAdapter;
                    long badge = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);
                    int titleId = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);
                    DateTime today = DateTime.Now.Date;
                    string todayJalali = JalaliDate.FromDateTime(today).ToString();

                    foreach (var emp in adapter.GetGuardAccessiblePersonnel(badge, titleId))
                    {
                        foreach (var dl in adapter.GetApprovedDailyLeaves(emp.BadgeNumber, today, today))
                        {
                            var lt = adapter.GetLeaveTypeById(dl.LeaveTypeId);
                            list.Add(new LV
                            {
                                BadgeNumber = emp.BadgeNumber,
                                Name = emp.FullName,
                                Type = lt != null ? lt.LeaveName : "مرخصی",
                                FromTo = todayJalali,
                                Descriptions = dl.Descriptions ?? ""
                            });
                        }
                        foreach (var hl in adapter.GetApprovedHourlyLeaves(emp.BadgeNumber, today, today.AddDays(1)))
                        {
                            var lt = adapter.GetLeaveTypeById(hl.LeaveTypeId);
                            list.Add(new LV
                            {
                                BadgeNumber = emp.BadgeNumber,
                                Name = emp.FullName,
                                Type = lt != null ? lt.LeaveName : "مرخصی ساعتی",
                                FromTo = hl.FromTime.ToString("HH:mm") + " - " + hl.ToTime.ToString("HH:mm"),
                                Descriptions = hl.Descriptions ?? ""
                            });
                        }
                    }
                    return View(list);
                }

                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                personnelSystem.DB.Departments.GetAll();
                GlobalHelper.GetLoggedInUserInfo(HttpContext);
                int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);
                List<int> allGuardAccess = personnelSystem.DB.TitlesCharts.GetAllGuardAccess(loggedInUserInfoTitleID);
                List<Personnel.Model.UserInfo> guardUsers = personnelSystem.DB.UserInfos.GetAll(allGuardAccess)
                    .Where(u => u.DeptID > 0)
                    .ToList();

                Attendance attendance = new Attendance(t5DBInstance, personnelSystem);
                DateTime todayDate = DateTime.Now.Date;
                DateTime tomorrowDate = todayDate.AddDays(1.0);

                foreach (Personnel.Model.UserInfo userInfo in guardUsers)
                {
                    long badgeNumber = userInfo.BadgeNumber;
                    foreach (ADayResult dayResult in attendance.Calculate(badgeNumber, todayDate, tomorrowDate))
                    {
                        if (dayResult.ForDate == todayDate)
                        {
                            if (dayResult.IsDailyLeave)
                            {
                                if (dayResult.DailyLeaveValue.DailyLeaveType.LeaveType1 == LeaveTypeEnum.Duty ||
                                    dayResult.DailyLeaveValue.DailyLeaveType.LeaveType1 == LeaveTypeEnum.Leave)
                                {
                                    list.Add(new LV
                                    {
                                        BadgeNumber = badgeNumber,
                                        Name = userInfo.ToString(),
                                        Type = dayResult.DailyLeaveValue.DailyLeaveType.LeaveName,
                                        FromTo = DateConverter.ToHijriDate(todayDate),
                                        Descriptions = DescriptionClass.GetDescriptionInDate(badgeNumber, todayDate)
                                    });
                                }
                            }
                            else if (dayResult.ProcessedSchedule != null)
                            {
                                foreach (InShiftExit inShiftExit in dayResult.ProcessedSchedule.InShiftExits)
                                {
                                    if (!inShiftExit.IsLessWork && inShiftExit.lvType != null)
                                    {
                                        list.Add(new LV
                                        {
                                            Name = userInfo.ToString(),
                                            BadgeNumber = badgeNumber,
                                            Type = inShiftExit.lvType.LeaveName,
                                            FromTo = inShiftExit.FromTime.ToString("HH:mm") + " - " + inShiftExit.ToTime.ToString("HH:mm"),
                                            Descriptions = DescriptionClass.GetDescriptionInDate(badgeNumber, inShiftExit.FromTime)
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return View(list);
        }

        public static bool isObs(HttpContextBase con)
        {
            try
            {
                int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(con);
                if (loggedInUserInfoTitleID > 0)
                {
                    if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                    {
                        var node = DBInitializer.TestBackendAdapter.GetTitleNodeById(loggedInUserInfoTitleID);
                        return node != null && node.IsObserver;
                    }

                    PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                    // Fixed legacy bug: guard against null when TitlesCharts.GetByID returns null
                    Personnel.Model.TitleChart title = personnelSystem.DB.TitlesCharts.GetByID(loggedInUserInfoTitleID);
                    bool? observer = title != null ? title.Observer : null;
                    if (observer.HasValue && observer.Value)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }
    }
}
