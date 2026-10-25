using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.LeaveCalcs;
using T5V3.Misc;
using T5V3.Model;
using TD5.Controllers;
using TD5.Model;
using TWF.Application.Leaves;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten HourlyLeaveController (Phase 4).
    /// - Fixes Edit POST calling GlobalHelper.AddDutyTypes instead of GlobalHelper.AddLeaveTypes.
    /// - Fixes EditRequestHourly returning unformatted PersianString.MaxHourlyInDayLimit.
    /// - Fixes double-counting of hourly leaves in AddRequestTest and linked leave balance check in AddRequest.
    /// - Supports DBInitializer.IsTestMode.
    /// </summary>
    public class HourlyLeaveController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(string forDate = null, string fromTime = null, string toTime = null)
        {
            GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
            GlobalHelper.AddLeaveDescriptions(base.ViewBag);
            if (forDate != null || fromTime != null || toTime != null)
            {
                return View(new HourlyExitRequest
                {
                    ForDateHijri = forDate,
                    FromTimeStr = fromTime,
                    ToTimeStr = toTime
                });
            }
            return View();
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(HourlyExitRequest request)
        {
            base.ViewBag.Message = RequestHourly(request);
            GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
            GlobalHelper.AddLeaveDescriptions(base.ViewBag);
            return View("Add", request);
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Edit(long id)
        {
            long loggedInBadge = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var dto = DBInitializer.TestBackendAdapter.GetRequestById(id);
                if (dto != null)
                {
                    if (dto.BadgeNumber == loggedInBadge && DBInitializer.TestBackendAdapter.IsRequestEditableByEmployee(id))
                    {
                        GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
                        GlobalHelper.AddLeaveDescriptions(base.ViewBag);
                        return View(new HourlyExitRequest
                        {
                            RequestID = dto.RequestId,
                            FromTimeStr = dto.FromTime.ToString("HH:mm"),
                            ToTimeStr = dto.ToTime.ToString("HH:mm"),
                            Descriptions = dto.Descriptions,
                            ForDateHijri = JalaliDate.FromDateTime(dto.ForDate).ToString(),
                            LeaveDescriptionID = dto.LeaveDescriptionId,
                            LeaveID = dto.LeaveTypeId
                        });
                    }
                    return View("Error", "", PersianString.AccessDenied);
                }
                return RedirectToAction("Add");
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(id);
            if (byID != null)
            {
                if (byID.BadgeNumber == loggedInBadge && t5DBInstance.RequestAcceptDA.IsEditableRequest(id))
                {
                    GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
                    GlobalHelper.AddLeaveDescriptions(base.ViewBag);
                    return View(new HourlyExitRequest
                    {
                        RequestID = byID.RequestID,
                        FromTimeStr = byID.FromTime.ToString("HH:mm"),
                        ToTimeStr = byID.ToTime.ToString("HH:mm"),
                        Descriptions = byID.Descriptions,
                        ForDateHijri = DateConverter.ToHijriDate(byID.ForDate),
                        LeaveDescriptionID = byID.LeaveDescriptionID,
                        LeaveID = byID.LeaveID
                    });
                }
                return View("Error", "", PersianString.AccessDenied);
            }
            return RedirectToAction("Add");
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Edit(HourlyExitRequest request)
        {
            base.ViewBag.Message = EditRequestHourly(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
            // Fixed legacy bug: Legacy code called GlobalHelper.AddDutyTypes instead of GlobalHelper.AddLeaveTypes!
            GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
            GlobalHelper.AddLeaveDescriptions(base.ViewBag);
            return View("Edit", request);
        }

        public static string EditRequestHourly(long badgeNumber, HourlyExitRequest request)
        {
            if (request == null)
                return PersianString.ErrorInInsertingData;

            if (request.Descriptions == null)
            {
                request.Descriptions = string.Empty;
            }
            if (request.LeaveDescriptionID == 0)
            {
                return PersianString.BadDescriptions;
            }
            if (request.LeaveID == 0)
            {
                return PersianString.BadExitType;
            }

            DateTime? forDate = DailyLeaveController.ParseHijriDate(request.ForDateHijri);
            if (!forDate.HasValue)
            {
                return PersianString.BadDateFormat;
            }

            if (!DateTime.TryParse(request.FromTimeStr, out DateTime fromTime) ||
                !DateTime.TryParse(request.ToTimeStr, out DateTime toTime) ||
                fromTime >= toTime)
            {
                return PersianString.BadTimeFormat;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null && DBInitializer.TestSettingsService != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                if (adapter.GetApprovedDailyLeaves(badgeNumber, forDate.Value.Date, forDate.Value.Date).Count > 0)
                {
                    return PersianString.DailyLeaveExists;
                }

                var leaveSvc = new LeaveBalanceService(adapter, DBInitializer.TestSettingsService);
                DateTime fullFrom = forDate.Value.Date.Add(fromTime.TimeOfDay);
                DateTime fullTo = forDate.Value.Date.Add(toTime.TimeOfDay);
                if (!leaveSvc.ValidateHourlyLeaveBalance(badgeNumber, request.LeaveID, forDate.Value.Date, fullFrom, fullTo, out string leaveErr))
                {
                    return leaveErr;
                }

                var dto = adapter.GetRequestById(request.RequestID);
                if (dto == null)
                {
                    return PersianString.ErrorInInsertingData;
                }

                dto.LeaveTypeId = request.LeaveID;
                dto.LeaveDescriptionId = request.LeaveDescriptionID;
                dto.ForDate = forDate.Value.Date;
                dto.FromTime = fullFrom;
                dto.ToTime = fullTo;
                dto.Descriptions = request.Descriptions;

                if (adapter.UpdateRequest(dto, out _))
                {
                    NotificationManager.NewRequestAdded(EditRequestController.MapDtoToT5Request(dto));
                    return PersianString.InsertSuccess;
                }
                return PersianString.ErrorInInsertingData;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
            if (byID == null)
            {
                return PersianString.BadExitType;
            }

            if (toTime - fromTime > byID.MaxHoursInADay && byID.MaxHoursInADay > TimeSpan.Zero)
            {
                return string.Format(PersianString.MaxHourlyInDayLimit, byID.MaxHoursInADay.ToString());
            }

            if (t5DBInstance.DailyLeaveDA.GetByID(badgeNumber, forDate.Value) != null)
            {
                return PersianString.DailyLeaveExists;
            }

            var persianCalendar = new PersianCalendar();
            int year = persianCalendar.GetYear(forDate.Value);
            int month = persianCalendar.GetMonth(forDate.Value);
            DateTime monthStart = persianCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
            DateTime monthEnd = (month >= 12)
                ? persianCalendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0)
                : persianCalendar.ToDateTime(year, month + 1, 1, 0, 0, 0, 0);

            TimeSpan monthTotal = TimeSpan.Zero;
            foreach (TD5.Model.Request item in TD5.Controllers.RequestController.GetRequest("*")
                .BadgeNumber(badgeNumber)
                .FromTime(monthStart)
                .ToTime(monthEnd)
                .LeaveId(request.LeaveID)
                .IsSuspend(false)
                .WorkflowType(1)
                .Tolist())
            {
                if (item.RequestID == request.RequestID)
                    continue;

                if (item.FromTime > item.ToTime)
                {
                    monthTotal += item.ToTime.AddHours(24.0) - item.FromTime;
                }
                else
                {
                    monthTotal += item.ToTime - item.FromTime;
                }
            }

            bool validHourlyFree = (bool)SettingsController.LoadValue("ValidHourlyFree", typeof(bool), true);
            int hoursOfDays = (int)SettingsController.LoadValue("HoursOfDays", typeof(int), 8);
            if (validHourlyFree)
            {
                int dailyLeaveCount = t5DBInstance.RequestDA
                    .GetAll(badgeNumber, monthStart, monthEnd)
                    .Count(r => r.IsDaily && r.WorkflowType == WorkflowOperations.Leave);
                monthTotal += new TimeSpan(0, dailyLeaveCount * hoursOfDays, 0, 0);
            }

            monthTotal += toTime - fromTime;
            if (byID.MaxHoursInAMonth > TimeSpan.Zero && monthTotal > byID.MaxHoursInAMonth)
            {
                return string.Format(PersianString.HourlyLeavePerMonthLimit, byID.MaxHoursInAMonth.TotalHours.ToString("0.0", CultureInfo.InvariantCulture));
            }

            T5V3.Model.Request byID2 = t5DBInstance.RequestDA.GetByID(request.RequestID);
            if (byID2 == null)
            {
                return PersianString.ErrorInInsertingData;
            }

            byID2.LeaveID = request.LeaveID;
            byID2.LeaveDescriptionID = request.LeaveDescriptionID;
            byID2.ForDate = forDate.Value;
            byID2.Descriptions = request.Descriptions;
            byID2.FromTime = fromTime;
            byID2.ToTime = toTime;

            if (t5DBInstance.RequestDA.Edit(byID2, out _))
            {
                NotificationManager.NewRequestAdded(byID2);
                return PersianString.InsertSuccess;
            }
            return PersianString.ErrorInInsertingData;
        }

        public static bool AddRequestTest(long badgeNumber, DailyExitRequest request, AppExtraDataForRequest appExtraData)
        {
            if (request == null || request.LeaveDescriptionID == 0 || request.LeaveID == 0)
                return false;

            string res = DailyLeaveController.AddRequest(badgeNumber, request, appExtraData);
            return res == PersianString.InsertSuccess;
        }

        private string RequestHourly(HourlyExitRequest request)
        {
            return AddRequest(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
        }

        public static string AddRequest(long badgeNumber, HourlyExitRequest request)
        {
            if (request == null)
                return PersianString.ErrorInInsertingData;

            if (request.Descriptions == null)
            {
                request.Descriptions = string.Empty;
            }
            if (request.LeaveDescriptionID == 0)
            {
                return PersianString.BadDescriptions;
            }
            if (request.LeaveID == 0)
            {
                return PersianString.BadExitType;
            }

            DateTime? forDate = DailyLeaveController.ParseHijriDate(request.ForDateHijri);
            if (!forDate.HasValue)
            {
                return PersianString.BadDateFormat;
            }

            if (!DateTime.TryParse(request.FromTimeStr, out DateTime fromTime) ||
                !DateTime.TryParse(request.ToTimeStr, out DateTime toTime))
            {
                return PersianString.BadTimeFormat;
            }

            if (toTime < fromTime)
            {
                toTime = toTime.AddDays(1.0);
            }
            if (toTime <= fromTime)
            {
                return PersianString.BadTimeFormat;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null && DBInitializer.TestSettingsService != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                if (adapter.GetApprovedDailyLeaves(badgeNumber, forDate.Value.Date, forDate.Value.Date).Count > 0)
                {
                    return PersianString.DailyLeaveExists;
                }

                string checkTime = EditRequestController.CheckRequestTime(forDate.Value);
                if (!string.IsNullOrEmpty(checkTime))
                {
                    return checkTime;
                }

                DateTime fullFrom = forDate.Value.Date.Add(fromTime.TimeOfDay);
                DateTime fullTo = toTime.TimeOfDay < fromTime.TimeOfDay
                    ? forDate.Value.Date.AddDays(1.0).Add(toTime.TimeOfDay)
                    : forDate.Value.Date.Add(toTime.TimeOfDay);

                var existingHourly = adapter.GetEmployeeRequests(badgeNumber, suspendedOnly: false)
                    .Where(r => !r.IsDaily && r.ForDate.Date == forDate.Value.Date)
                    .ToList();

                foreach (var ex in existingHourly)
                {
                    DateTime exFrom = ex.ForDate.Date.Add(ex.FromTime.TimeOfDay);
                    DateTime exTo = ex.ToTime.TimeOfDay < ex.FromTime.TimeOfDay
                        ? ex.ForDate.Date.AddDays(1.0).Add(ex.ToTime.TimeOfDay)
                        : ex.ForDate.Date.Add(ex.ToTime.TimeOfDay);

                    if (fullFrom < exTo && exFrom < fullTo)
                    {
                        return PersianString.RequestDailyForDateExists;
                    }
                }

                var leaveSvc = new LeaveBalanceService(adapter, DBInitializer.TestSettingsService);
                if (!leaveSvc.ValidateHourlyLeaveBalance(badgeNumber, request.LeaveID, forDate.Value.Date, fullFrom, fullTo, out string leaveErr))
                {
                    return leaveErr;
                }

                var dto = new WorkflowRequestItemDto
                {
                    BadgeNumber = badgeNumber,
                    CreatedAt = DateTime.Now,
                    ForDate = forDate.Value.Date,
                    FromTime = fullFrom,
                    ToTime = fullTo,
                    RecordTime = DateTime.Now.Date,
                    IsDaily = false,
                    IsSuspended = true,
                    LeaveTypeId = request.LeaveID,
                    LeaveDescriptionId = request.LeaveDescriptionID,
                    OverTimeAllowType = (int)AllowType.Early,
                    WorkflowType = TwfWorkflowType.HourlyLeave,
                    Descriptions = request.Descriptions
                };

                if (adapter.CreateRequest(dto, out _, out long newReqId))
                {
                    dto.RequestId = newReqId;
                    NotificationManager.NewRequestAdded(EditRequestController.MapDtoToT5Request(dto));
                    return PersianString.InsertSuccess;
                }
                return PersianString.ErrorInInsertingData;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
            if (byID == null)
            {
                return PersianString.BadExitType;
            }

            if (byID.MaxHoursInADay > TimeSpan.Zero && (toTime - fromTime) >= byID.MaxHoursInADay)
            {
                return string.Format(PersianString.MaxHourlyInDayLimit, byID.MaxHoursInADay.ToString());
            }

            var sameDayHourly = t5DBInstance.RequestDA
                .GetAll(badgeNumber, true)
                .Where(r => r.ForDate == forDate.Value && !r.IsDaily)
                .Concat(t5DBInstance.RequestDA.GetAll(badgeNumber, false).Where(r => r.ForDate == forDate.Value && !r.IsDaily))
                .ToList();

            DateTime reqStart = forDate.Value.Date.Add(fromTime.TimeOfDay);
            DateTime reqEnd = toTime.TimeOfDay < fromTime.TimeOfDay
                ? forDate.Value.Date.Add(toTime.TimeOfDay).AddDays(1.0)
                : forDate.Value.Date.Add(toTime.TimeOfDay);
            var reqInterval = new TimeInterval(reqStart, reqEnd);

            bool hasOverlap = false;
            foreach (T5V3.Model.Request item in sameDayHourly)
            {
                var itemInterval = new TimeInterval(
                    item.ForDate.Date.Add(item.FromTime.TimeOfDay),
                    item.ForDate.Date.Add(item.ToTime.TimeOfDay));
                if (TimeInterval.HaveIntersection(reqInterval, itemInterval))
                {
                    hasOverlap = true;
                    break;
                }
            }

            string text2 = EditRequestController.CheckRequestTime(forDate.Value);
            if (!string.IsNullOrEmpty(text2))
            {
                return text2;
            }
            if (hasOverlap)
            {
                return PersianString.RequestDailyForDateExists;
            }

            bool insufficientLinkedRemain = false;
            if (byID.DecreaseFromLeaveID.HasValue)
            {
                var persianCalendar = new PersianCalendar();
                LeaveType parentLeave = t5DBInstance.LeaveTypeDA.GetByID(byID.DecreaseFromLeaveID.Value);
                if (parentLeave != null)
                {
                    int year = persianCalendar.GetYear(forDate.Value);
                    var attendance = new Attendance(t5DBInstance, personnelSystem);
                    DateTime fromDate = persianCalendar.ToDateTime(year, 1, 1, 0, 0, 0, 0);
                    DateTime toDate = persianCalendar.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);

                    ADayRemain aDayRemain = new Remain(
                        attendance.Calculate(badgeNumber, fromDate, toDate),
                        t5DBInstance.LeaveTypeDA.GetAll(),
                        t5DBInstance.LeavesAddedDA.GetAll(new List<long> { badgeNumber }, fromDate, toDate),
                        t5DBInstance.YearRemainDA.GetAll(badgeNumber),
                        t5DBInstance.ScheduleDA.GetAll(),
                        t5DBInstance.ParameterDA.GetAll())
                        .CalculateRemain(personnelSystem.DB.UserInfos.GetByID(badgeNumber), parentLeave.LeaveTypeID, year)
                        .FirstOrDefault(rc => rc.ForDate == forDate.Value);

                    double parentDayHours = parentLeave.EachXHoursADay.TotalHours > 0 ? parentLeave.EachXHoursADay.TotalHours : 8.0;
                    double requestedDaysFraction = (toTime - fromTime).TotalHours / parentDayHours;
                    if (aDayRemain != null && (aDayRemain.Remain - requestedDaysFraction) < (0.0 - parentLeave.MaxMinus))
                    {
                        insufficientLinkedRemain = true;
                    }
                }
            }

            var persianCalendar2 = new PersianCalendar();
            int year2 = persianCalendar2.GetYear(forDate.Value);
            int month = persianCalendar2.GetMonth(forDate.Value);
            DateTime monthStart2 = persianCalendar2.ToDateTime(year2, month, 1, 0, 0, 0, 0);
            DateTime monthEnd2 = (month >= 12)
                ? persianCalendar2.ToDateTime(year2 + 1, 1, 1, 0, 0, 0, 0)
                : persianCalendar2.ToDateTime(year2, month + 1, 1, 0, 0, 0, 0);

            TimeSpan monthTotal2 = TimeSpan.Zero;
            foreach (TD5.Model.Request item2 in TD5.Controllers.RequestController.GetRequest("*")
                .BadgeNumber(badgeNumber)
                .FromTime(monthStart2)
                .ToTime(monthEnd2)
                .LeaveId(request.LeaveID)
                .IsSuspend(false)
                .WorkflowType(1)
                .Tolist())
            {
                if (item2.FromTime > item2.ToTime)
                {
                    monthTotal2 += item2.ToTime.AddHours(24.0) - item2.FromTime;
                }
                else
                {
                    monthTotal2 += item2.ToTime - item2.FromTime;
                }
            }

            bool validHourlyFree = (bool)SettingsController.LoadValue("ValidHourlyFree", typeof(bool), true);
            int hoursOfDays = (int)SettingsController.LoadValue("HoursOfDays", typeof(int), 8);
            if (validHourlyFree)
            {
                int dailyLeaveCount = t5DBInstance.RequestDA
                    .GetAll(badgeNumber, monthStart2, monthEnd2)
                    .Count(r => r.IsDaily && r.WorkflowType == WorkflowOperations.Leave);
                monthTotal2 += new TimeSpan(0, dailyLeaveCount * hoursOfDays, 0, 0);
            }

            monthTotal2 += toTime - fromTime;
            if (byID.MaxHoursInAMonth > TimeSpan.Zero && monthTotal2 > byID.MaxHoursInAMonth)
            {
                return string.Format(PersianString.HourlyLeavePerMonthLimit, byID.MaxHoursInAMonth.TotalHours.ToString("0.0", CultureInfo.InvariantCulture));
            }
            if (insufficientLinkedRemain)
            {
                return PersianString.RemainLeaveError;
            }

            var request2 = new T5V3.Model.Request
            {
                AllowValue = TimeSpan.Zero,
                BadgeNumber = badgeNumber,
                CreateTime = DateTime.Now,
                ForDate = forDate.Value,
                FromTime = fromTime,
                IsSuspend = true,
                IsDaily = false,
                LeaveID = request.LeaveID,
                LeaveDescriptionID = request.LeaveDescriptionID,
                OverTimeAllowType = AllowType.Early,
                RecordTime = DateTime.Now.Date,
                ToTime = toTime,
                WorkflowType = WorkflowOperations.Leave,
                Descriptions = request.Descriptions
            };

            if (t5DBInstance.RequestDA.AddNew(request2, true, out _, out long newID))
            {
                request2.RequestID = newID;
                NotificationManager.NewRequestAdded(request2);
                return PersianString.InsertSuccess;
            }
            return PersianString.ErrorInInsertingData;
        }
    }
}
