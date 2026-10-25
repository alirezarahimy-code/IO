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
using TWF.Application.Leaves;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten DailyLeaveController (Phase 4).
    /// - Fixes Edit(long id) GET missing ToDateHijri population.
    /// - Fixes tautological comparison bug (item.LeaveID == item.LeaveID) in AddRequest.
    /// - Fixes BUG-06: Replaces hardcoded DateTime.Parse("2020-03-21").DayOfYear leap-year check with PersianCalendar.
    /// - Enforces Limit1LeaveID single-day constraint and supports DBInitializer.IsTestMode.
    /// </summary>
    public class DailyLeaveController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(string forDate = null)
        {
            var dailyExitRequest = new DailyExitRequest();
            GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
            GlobalHelper.AddLeaveDescriptions(base.ViewBag);
            if (forDate != null)
            {
                dailyExitRequest.ForDateHijri = forDate;
                dailyExitRequest.ToDateHijri = forDate;
            }
            return View(dailyExitRequest);
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(DailyExitRequest request)
        {
            base.ViewBag.Message = RequestDaily(request);
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
                        return View(new DailyExitRequest
                        {
                            RequestID = dto.RequestId,
                            Descriptions = dto.Descriptions,
                            ForDateHijri = JalaliDate.FromDateTime(dto.ForDate).ToString(),
                            ToDateHijri = JalaliDate.FromDateTime(dto.ToTime).ToString(),
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
                    return View(new DailyExitRequest
                    {
                        RequestID = byID.RequestID,
                        Descriptions = byID.Descriptions,
                        ForDateHijri = DateConverter.ToHijriDate(byID.ForDate),
                        // Fixed legacy bug where ToDateHijri was omitted in DailyLeaveController.Edit GET
                        ToDateHijri = DateConverter.ToHijriDate(byID.ToTime),
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
        public ActionResult Edit(DailyExitRequest request)
        {
            base.ViewBag.Message = EditRequestDaily(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
            GlobalHelper.AddLeaveTypes(base.HttpContext, base.ViewBag);
            GlobalHelper.AddLeaveDescriptions(base.ViewBag);
            return View("Edit", request);
        }

        internal static DateTime? ParseHijriDate(string hijriStr)
        {
            if (string.IsNullOrWhiteSpace(hijriStr))
                return null;

            DateTime? converted = null;
            try
            {
                converted = DateConverter.ToChristian(hijriStr);
            }
            catch
            {
            }

            if (!converted.HasValue && JalaliDate.TryParse(hijriStr, out JalaliDate jDate))
            {
                converted = jDate.ToDateTime();
            }
            return converted;
        }

        public static string EditRequestDaily(long badgeNumber, DailyExitRequest request)
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

            DateTime? forDate = ParseHijriDate(request.ForDateHijri);
            DateTime? toDate = ParseHijriDate(request.ToDateHijri);
            if (!forDate.HasValue || !toDate.HasValue || toDate.Value.Date < forDate.Value.Date)
            {
                return PersianString.BadDateFormat;
            }

            int limit1LeaveId = (int)SettingsController.LoadValue("Limit1LeaveID", typeof(int), -1);
            if (limit1LeaveId > 0 && request.LeaveID == limit1LeaveId && toDate.Value.Date > forDate.Value.Date)
            {
                return PersianString.DailyLeaveLimit;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                if (adapter.GetApprovedDailyLeaves(badgeNumber, forDate.Value.Date, toDate.Value.Date).Count > 0)
                {
                    return PersianString.DailyLeaveExists;
                }

                var dto = adapter.GetRequestById(request.RequestID);
                if (dto == null)
                {
                    return PersianString.ErrorInInsertingData;
                }

                dto.LeaveTypeId = request.LeaveID;
                dto.LeaveDescriptionId = request.LeaveDescriptionID;
                dto.ForDate = forDate.Value.Date;
                dto.ToTime = toDate.Value.Date;
                dto.Descriptions = request.Descriptions;

                if (adapter.UpdateRequest(dto, out _))
                {
                    NotificationManager.NewRequestAdded(EditRequestController.MapDtoToT5Request(dto));
                    return PersianString.InsertSuccess;
                }
                return PersianString.ErrorInInsertingData;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            if (t5DBInstance.DailyLeaveDA.GetByID(badgeNumber, forDate.Value) != null)
            {
                return PersianString.DailyLeaveExists;
            }

            T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(request.RequestID);
            if (byID == null)
            {
                return PersianString.ErrorInInsertingData;
            }

            byID.LeaveID = request.LeaveID;
            byID.LeaveDescriptionID = request.LeaveDescriptionID;
            byID.ForDate = forDate.Value;
            byID.ToTime = toDate.Value;
            byID.Descriptions = request.Descriptions;

            if (t5DBInstance.RequestDA.Edit(byID, out _))
            {
                NotificationManager.NewRequestAdded(byID);
                return PersianString.InsertSuccess;
            }
            return PersianString.ErrorInInsertingData;
        }

        private string RequestDaily(DailyExitRequest request)
        {
            return AddRequest(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request, new AppExtraDataForRequest());
        }

        public static string AddRequest(long badgeNumber, DailyExitRequest request, AppExtraDataForRequest appExtraData)
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

            DateTime? forDate = ParseHijriDate(request.ForDateHijri);
            DateTime? toDate = ParseHijriDate(request.ToDateHijri);
            if (!forDate.HasValue || !toDate.HasValue || toDate.Value.Date < forDate.Value.Date)
            {
                return PersianString.BadDateFormat;
            }

            int limit1LeaveId = (int)SettingsController.LoadValue("Limit1LeaveID", typeof(int), -1);
            if (limit1LeaveId > 0 && request.LeaveID == limit1LeaveId && toDate.Value.Date > forDate.Value.Date)
            {
                return PersianString.DailyLeaveLimit;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null && DBInitializer.TestSettingsService != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                if (adapter.GetApprovedDailyLeaves(badgeNumber, forDate.Value.Date, toDate.Value.Date).Count > 0)
                {
                    return PersianString.DailyLeaveExists;
                }

                var pendingDaily = adapter.GetEmployeeRequests(badgeNumber, suspendedOnly: true)
                    .Where(r => r.IsDaily)
                    .ToList();

                if (pendingDaily.Any(r => r.ForDate.Date <= toDate.Value.Date && r.ToTime.Date >= forDate.Value.Date))
                {
                    return PersianString.RequestDailyForDateExists;
                }

                string timeCheck = EditRequestController.CheckRequestTime(forDate.Value);
                if (!string.IsNullOrEmpty(timeCheck))
                {
                    return timeCheck;
                }

                var leaveSvc = new LeaveBalanceService(adapter, DBInitializer.TestSettingsService);
                if (!leaveSvc.ValidateDailyLeaveBalance(badgeNumber, request.LeaveID, forDate.Value.Date, toDate.Value.Date, out string leaveErr))
                {
                    return leaveErr;
                }

                var dto = new WorkflowRequestItemDto
                {
                    BadgeNumber = badgeNumber,
                    CreatedAt = DateTime.Now,
                    ForDate = forDate.Value.Date,
                    FromTime = DateTime.Now.Date,
                    ToTime = toDate.Value.Date,
                    RecordTime = DateTime.Now.Date,
                    IsDaily = true,
                    IsSuspended = true,
                    LeaveTypeId = request.LeaveID,
                    LeaveDescriptionId = request.LeaveDescriptionID,
                    OverTimeAllowType = (int)AllowType.Early,
                    WorkflowType = TwfWorkflowType.Leave,
                    Descriptions = request.Descriptions,
                    Latitude = appExtraData?.latitude,
                    Longitude = appExtraData?.longitude,
                    ImageData = appExtraData?.ImageData
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

            if (t5DBInstance.DailyLeaveDA.GetByID(badgeNumber, forDate.Value) != null)
            {
                return PersianString.DailyLeaveExists;
            }

            List<T5V3.Model.Request> pendingDailyReal = t5DBInstance.RequestDA
                .GetAll(badgeNumber, true)
                .Where(r => r.IsDaily)
                .ToList();

            if (pendingDailyReal.Any(r => r.ForDate <= forDate.Value && r.ToTime >= forDate.Value) ||
                pendingDailyReal.Any(r => r.ForDate <= toDate.Value && r.ToTime >= toDate.Value))
            {
                return PersianString.RequestDailyForDateExists;
            }

            string text2 = EditRequestController.CheckRequestTime(forDate.Value);
            if (!string.IsNullOrEmpty(text2))
            {
                return text2;
            }

            var pc = new PersianCalendar();
            LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
            if (byID == null)
            {
                return PersianString.BadExitType;
            }

            int year = pc.GetYear(forDate.Value);
            int month = pc.GetMonth(forDate.Value);
            bool hasEnoughRemain = false;

            var attendance = new Attendance(t5DBInstance, personnelSystem);
            DateTime fromDate = pc.ToDateTime(year, 1, 1, 0, 0, 0, 0);
            DateTime toDate2 = pc.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0);
            List<ADayResult> list = attendance.Calculate(badgeNumber, fromDate, toDate2);
            List<LeaveType> all = t5DBInstance.LeaveTypeDA.GetAll();
            List<Schedule> all2 = t5DBInstance.ScheduleDA.GetAll();
            List<Paramtr> all3 = t5DBInstance.ParameterDA.GetAll();

            var remain = new Remain(
                list,
                all,
                t5DBInstance.LeavesAddedDA.GetAll(new List<long> { badgeNumber }, fromDate, toDate2),
                t5DBInstance.YearRemainDA.GetAll(badgeNumber),
                all2,
                all3);

            List<ADayRemain> source2 = remain.CalculateRemain(personnelSystem.DB.UserInfos.GetByID(badgeNumber), byID.LeaveTypeID, year);
            ADayRemain aDayRemain = source2.FirstOrDefault(rc => rc.ForDate == forDate.Value);
            if (aDayRemain != null && aDayRemain.Remain > 0.0 - byID.MaxMinus)
            {
                hasEnoughRemain = true;
            }

            bool exceedsLimit = false;
            int requestedAbsenceDays = 0;
            DateTime cursor = forDate.Value;
            while (cursor <= toDate.Value)
            {
                if (HomeController.CalculateDailyAbsence(badgeNumber, cursor, byID.AutoInHolidays))
                {
                    requestedAbsenceDays++;
                }
                cursor = cursor.AddDays(1.0);
            }

            double eachDayHours = byID.EachXHoursADay.TotalHours > 0 ? byID.EachXHoursADay.TotalHours : 8.0;
            for (int m = 1; m <= 12; m++)
            {
                double monthTakenAndPending = 0.0;
                int daysInMonth = pc.GetDaysInMonth(year, m);
                DateTime mStart = pc.ToDateTime(year, m, 1, 0, 0, 0, 0);
                DateTime mEnd = pc.ToDateTime(year, m, daysInMonth, 0, 0, 0, 0);

                foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(badgeNumber, mStart, mEnd))
                {
                    if (!item.IsSuspend || item.WorkflowType != WorkflowOperations.Leave)
                        continue;

                    LeaveType leaveType = all.FirstOrDefault(lt2 => lt2.LeaveTypeID == item.LeaveID);
                    bool matchesTarget =
                        item.LeaveID == byID.LeaveTypeID ||
                        (leaveType != null && leaveType.DecreaseFromLeaveID.HasValue && leaveType.DecreaseFromLeaveID.Value == byID.LeaveTypeID);

                    if (!matchesTarget)
                        continue;

                    // Fixed legacy bug where item.LeaveID == item.LeaveID was checked and hourly leaves were double-counted
                    if (item.IsDaily)
                    {
                        monthTakenAndPending += 1.0;
                    }
                    else
                    {
                        monthTakenAndPending += (item.ToTime - item.FromTime).TotalHours / eachDayHours;
                    }
                }

                for (int d = 1; d <= daysInMonth; d++)
                {
                    DateTime targetDay = pc.ToDateTime(year, m, d, 0, 0, 0, 0);
                    ADayResult aDayResult = list.FirstOrDefault(ya => ya.ForDate == targetDay);
                    if (aDayResult != null && aDayResult.IsDailyLeave &&
                        ((!aDayResult.IsHoliday && aDayResult.CurrentShift?.ActiveSchedule != null) || byID.GetInHolidays))
                    {
                        if (aDayResult.DailyLeaveValue.LeaveTypeID == byID.LeaveTypeID)
                        {
                            monthTakenAndPending += 1.0;
                        }
                    }
                }

                if (m == month && (byID.GetInType == LeaveGetInType.MonthFirst || byID.GetInType == LeaveGetInType.MonthLast))
                {
                    double monthEndRemain = source2.FirstOrDefault(rc => rc.ForDate == mEnd)?.Remain ?? 0.0;
                    if (byID.GetInType == LeaveGetInType.MonthLast)
                    {
                        monthEndRemain -= byID.MaxDailyInMonth;
                    }
                    if (monthEndRemain + byID.MaxMinus - requestedAbsenceDays < 0.0)
                    {
                        exceedsLimit = true;
                    }
                }
            }

            // Fixed BUG-06: Use PersianCalendar year comparison instead of hardcoded DateTime.Parse("2020-03-21").DayOfYear!
            int currentPersianYear = pc.GetYear(DateTime.Now);
            double effectiveRemain = (year < currentPersianYear)
                ? (source2.FirstOrDefault(rc => rc.ForDate.Date == forDate.Value.Date)?.Remain ?? 0.0)
                : (source2.FirstOrDefault(rc => rc.ForDate.Date == DateTime.Now.Date)?.Remain ?? 0.0);

            if (effectiveRemain + byID.MaxMinus - requestedAbsenceDays < 0.0)
            {
                exceedsLimit = true;
            }

            if (exceedsLimit)
            {
                return PersianString.DailyLeaveLimit;
            }
            if (!hasEnoughRemain)
            {
                return PersianString.RemainLeaveError;
            }

            var request2 = new T5V3.Model.Request
            {
                AllowValue = TimeSpan.Zero,
                BadgeNumber = badgeNumber,
                CreateTime = DateTime.Now,
                ForDate = forDate.Value,
                FromTime = DateTime.Now.Date,
                IsSuspend = true,
                IsDaily = true,
                LeaveID = request.LeaveID,
                LeaveDescriptionID = request.LeaveDescriptionID,
                OverTimeAllowType = AllowType.Early,
                RecordTime = DateTime.Now.Date,
                ToTime = toDate.Value,
                WorkflowType = WorkflowOperations.Leave,
                Descriptions = request.Descriptions
            };

            if (appExtraData != null)
            {
                request2.Longitude = appExtraData.longitude;
                request2.Latitude = appExtraData.latitude;
                request2.ImageData = appExtraData.ImageData;
            }

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
