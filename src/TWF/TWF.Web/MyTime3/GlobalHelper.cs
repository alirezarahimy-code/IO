using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using MyTime3.Authorization;
using MyTime3.Content;
using MyTime3.Controllers;
using Personnel;
using T5V3.DB;
using T5V3.Model;

namespace MyTime3
{
    /// <summary>
    /// Rewritten MyTime3.GlobalHelper (Phase 3).
    /// - Enforces Zero-Lock (LockIsOK = true, PersonnelCount = int.MaxValue, FulLogoSize = true, WithTimeApp() = false).
    /// - Supports both production T5V3/Personnel databases and in-memory test mode via DBInitializer.
    /// </summary>
    public static class GlobalHelper
    {
        public static bool LockIsOK { get; set; } = true;

        public static int PersonnelCount { get; set; } = int.MaxValue;

        public static bool FulLogoSize { get; set; } = true;

        public static string TimeApp { get; set; } = string.Empty;

        public static bool WithTimeApp()
        {
            // Zero-Lock: Never expire or restrict the application by dongle date
            return false;
        }

        public static string GetLoggedInUserName(HttpContextBase httpContext)
        {
            long badge = GetLoggedInBadgeNumber(httpContext);
            if (badge <= 0)
                return string.Empty;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var emp = DBInitializer.TestBackendAdapter.GetEmployeeByBadge(badge);
                return emp != null ? emp.FullName : string.Empty;
            }

            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            var user = personnelSystem?.DB?.UserInfos?.GetByID(badge);
            return user != null ? user.ToString() : string.Empty;
        }

        public static long GetLoggedInBadgeNumber(HttpContextBase httpContext)
        {
            string badgeNumber = LoginManager.GetBadgeNumber(httpContext);
            if (long.TryParse(badgeNumber, out long parsed) && parsed > 0)
            {
                return parsed;
            }
            return -1L;
        }

        public static Personnel.Model.UserInfo GetLoggedInUserInfo(HttpContextBase httpContext)
        {
            long badge = GetLoggedInBadgeNumber(httpContext);
            if (badge <= 0)
                return null;

            return GetUserInfo(badge);
        }

        public static Personnel.Model.UserInfo GetUserInfo(long badgeNumber)
        {
            if (badgeNumber <= 0)
                return null;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var emp = DBInitializer.TestBackendAdapter.GetEmployeeByBadge(badgeNumber);
                if (emp == null)
                    return null;

                return new Personnel.Model.UserInfo
                {
                    BadgeNumber = emp.BadgeNumber,
                    EmployeeNumber = emp.EmployeeNumber,
                    FirstName = emp.FirstName,
                    LastName = emp.LastName,
                    DeptID = emp.DepartmentId,
                    EmpTypeID = emp.EmployeeTypeId,
                    UserName = emp.UserName,
                    WebUser = emp.WebUserEnabled
                };
            }

            return DBInitializer.GetPersonnelSystem().DB.UserInfos.GetByID(badgeNumber);
        }

        public static int GetLoggedInUserInfoTitleID(HttpContextBase httpContext)
        {
            long badge = GetLoggedInBadgeNumber(httpContext);
            if (badge <= 0)
                return -1;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                return DBInitializer.TestBackendAdapter.GetEmployeeCurrentTitleId(badge);
            }

            return DBInitializer.GetPersonnelSystem().DB.UserInfos.GetUserCurrentTitleID(badge);
        }

        public static void AddLeaveTypes(HttpContextBase httpContext, dynamic ViewBag)
        {
            var dailyList = new List<SelectListItem>();
            var hourlyList = new List<SelectListItem>();
            bool selected = true;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                foreach (var lt in DBInitializer.TestBackendAdapter.GetAllLeaveTypes().Where(l => !l.IsDuty))
                {
                    var item = new SelectListItem
                    {
                        Text = lt.LeaveName,
                        Value = lt.LeaveTypeId.ToString(),
                        Selected = selected
                    };
                    selected = false;
                    if (lt.IsHourly)
                        hourlyList.Add(item);
                    else
                        dailyList.Add(item);
                }
                ViewBag.DailyLeaveTypes = dailyList;
                ViewBag.HourlyLeaveTypes = hourlyList;
                return;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            var userInfo = GetLoggedInUserInfo(httpContext);
            List<int> allowed = userInfo != null ? t5DBInstance.EmpLeavesDA.GetAll(userInfo.EmpTypeID) : new List<int>();
            foreach (LeaveType item in t5DBInstance.LeaveTypeDA.GetAll().Where(lt => lt.LeaveType1 == LeaveTypeEnum.Leave))
            {
                if (allowed.Contains(item.LeaveTypeID))
                {
                    var selectItem = new SelectListItem
                    {
                        Text = item.LeaveName,
                        Value = item.LeaveTypeID.ToString(),
                        Selected = selected
                    };
                    selected = false;
                    if (item.IsHourly)
                        hourlyList.Add(selectItem);
                    else
                        dailyList.Add(selectItem);
                }
            }
            ViewBag.DailyLeaveTypes = dailyList;
            ViewBag.HourlyLeaveTypes = hourlyList;
        }

        public static void AddOverTimeTypes(HttpContextBase httpContext, dynamic ViewBag)
        {
            var list = new List<SelectListItem>
            {
                new SelectListItem { Text = PersianString.OverTimeAfter, Value = "1", Selected = false },
                new SelectListItem { Text = PersianString.OverTimeBefore, Value = "0", Selected = false },
                new SelectListItem { Text = PersianString.FreeOverTime, Value = "2", Selected = false }
            };
            ViewBag.OverTimeTypes = list;
        }

        public static void AddRewardPenaltyTypes(HttpContextBase httpContext, dynamic ViewBag)
        {
            var list = new List<SelectListItem>
            {
                new SelectListItem { Text = PersianString.Reward, Value = "7", Selected = false },
                new SelectListItem { Text = PersianString.Penalty, Value = "6", Selected = false }
            };
            ViewBag.RewardPenaltyTypes = list;
        }

        public static void AddDutyTypes(HttpContextBase httpContext, dynamic ViewBag)
        {
            var dailyList = new List<SelectListItem>();
            var hourlyList = new List<SelectListItem>();
            bool selected = true;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                foreach (var lt in DBInitializer.TestBackendAdapter.GetAllLeaveTypes().Where(l => l.IsDuty))
                {
                    var item = new SelectListItem
                    {
                        Text = lt.LeaveName,
                        Value = lt.LeaveTypeId.ToString(),
                        Selected = selected
                    };
                    selected = false;
                    if (lt.IsHourly)
                        hourlyList.Add(item);
                    else
                        dailyList.Add(item);
                }
                ViewBag.DailyDutyTypes = dailyList;
                ViewBag.HourlyDutyTypes = hourlyList;
                return;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            var userInfo = GetLoggedInUserInfo(httpContext);
            List<int> allowed = userInfo != null ? t5DBInstance.EmpLeavesDA.GetAll(userInfo.EmpTypeID) : new List<int>();
            foreach (LeaveType item in t5DBInstance.LeaveTypeDA.GetAll().Where(lt => lt.LeaveType1 == LeaveTypeEnum.Duty))
            {
                if (allowed.Contains(item.LeaveTypeID))
                {
                    var selectItem = new SelectListItem
                    {
                        Text = item.LeaveName,
                        Value = item.LeaveTypeID.ToString(),
                        Selected = selected
                    };
                    selected = false;
                    if (item.IsHourly)
                        hourlyList.Add(selectItem);
                    else
                        dailyList.Add(selectItem);
                }
            }
            ViewBag.DailyDutyTypes = dailyList;
            ViewBag.HourlyDutyTypes = hourlyList;
        }

        public static void AddLeaveDescriptions(dynamic ViewBag)
        {
            var dutyDescriptions = new List<SelectListItem>();
            var leaveDescriptions = new List<SelectListItem>();
            bool selected = true;

            if (DBInitializer.IsTestMode)
            {
                dutyDescriptions.Add(new SelectListItem { Text = "مأموریت اداری", Value = "1", Selected = true });
                leaveDescriptions.Add(new SelectListItem { Text = "مرخصی شخصی", Value = "1", Selected = true });
                ViewBag.DutyDescriptions = dutyDescriptions;
                ViewBag.LeaveDescriptions = leaveDescriptions;
                return;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            foreach (LeaveDescription item in t5DBInstance.LeaveDescriptionDA.GetAll())
            {
                var selectItem = new SelectListItem
                {
                    Text = item.Description,
                    Value = item.ID.ToString(),
                    Selected = selected
                };
                selected = false;
                if (item.ForLeaveType == LeaveTypeEnum.Duty)
                    dutyDescriptions.Add(selectItem);
                else
                    leaveDescriptions.Add(selectItem);
            }
            ViewBag.DutyDescriptions = dutyDescriptions;
            ViewBag.LeaveDescriptions = leaveDescriptions;
        }

        public static Rule GetRule(long badgeNumber, DateTime forDate)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var activeRule = DBInitializer.TestBackendAdapter.GetActiveRule(badgeNumber, forDate);
                if (activeRule == null)
                    return null;

                return new Rule
                {
                    RuleID = activeRule.RuleId,
                    ParamName = activeRule.RuleName,
                    LimitRecReqInDay = activeRule.DailyRecordRequestLimit,
                    LimitRecReqInMonth = activeRule.MonthlyRecordRequestLimit
                };
            }

            Rule result = null;
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            UserRule userRule = t5DBInstance.UserRuleDA.GetAll(new List<long> { badgeNumber })
                .FirstOrDefault(r => r.RuleType == RuleType.Personnal && r.FromDate <= forDate && r.ToDate >= forDate);

            if (userRule != null)
            {
                result = t5DBInstance.RuleDA.GetByID(userRule.RuleID);
            }
            else
            {
                List<DepartmentRule> all = t5DBInstance.DepartmentRuleDA.GetAll();
                List<Personnel.Model.Department> allDepts = personnelSystem.DB.Departments.GetAll();
                var user = personnelSystem.DB.UserInfos.GetByID(badgeNumber);
                if (user != null)
                {
                    int deptID = user.DeptID;
                    var visited = new HashSet<int>();
                    while (deptID > 0 && visited.Add(deptID))
                    {
                        int currentDeptId = deptID;
                        DepartmentRule departmentRule = all.FirstOrDefault(r => r.DepartmentID == currentDeptId && r.FromDate <= forDate && r.ToDate >= forDate);
                        if (departmentRule != null)
                        {
                            result = t5DBInstance.RuleDA.GetByID(departmentRule.RuleID);
                            break;
                        }
                        Personnel.Model.Department department = allDepts.FirstOrDefault(d => d.DepartmentID == currentDeptId);
                        if (department == null)
                            break;
                        deptID = department.UpperDeptID;
                    }
                }
            }
            return result;
        }
    }
}
