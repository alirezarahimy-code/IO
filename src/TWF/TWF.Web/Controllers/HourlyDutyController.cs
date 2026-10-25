using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using MyTime3.Content;
using MyTime3.Models;
using T5V3.Attendance;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten HourlyDutyController (Phase 4).
    /// - Fixes critical bug in EditRequestHourly where ValidHourlyFree=true caused the method to skip RequestDA.Edit and return "".
    /// - Supports DBInitializer.IsTestMode.
    /// </summary>
    public class HourlyDutyController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(string forDate = null, string fromTime = null, string toTime = null)
        {
            GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
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
            GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
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
                        GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
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
                    GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
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
            GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
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

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                if (adapter.GetApprovedDailyLeaves(badgeNumber, forDate.Value.Date, forDate.Value.Date).Count > 0)
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
                dto.FromTime = forDate.Value.Date.Add(fromTime.TimeOfDay);
                dto.ToTime = forDate.Value.Date.Add(toTime.TimeOfDay);
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

            LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
            if (byID == null)
            {
                return PersianString.BadExitType;
            }

            var persianCalendar = new PersianCalendar();
            int year = persianCalendar.GetYear(forDate.Value);
            int month = persianCalendar.GetMonth(forDate.Value);
            DateTime fromDate = persianCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
            DateTime toDate = persianCalendar.ToDateTime(year, month, persianCalendar.GetDaysInMonth(year, month), 23, 59, 0, 0);

            TimeSpan timeSpan = TimeSpan.Zero;
            foreach (T5V3.Model.Request item in t5DBInstance.RequestDA
                .GetAll(badgeNumber, fromDate, toDate)
                .Where(r => r.RequestID != request.RequestID && r.LeaveID == request.LeaveID && r.IsSuspend && r.WorkflowType == WorkflowOperations.Leave))
            {
                if (item.FromTime > item.ToTime)
                {
                    timeSpan += item.ToTime.AddHours(24.0) - item.FromTime;
                }
                else
                {
                    timeSpan += item.ToTime - item.FromTime;
                }
            }

            foreach (HourlyLeave item2 in t5DBInstance.HourlyLeaveDA
                .GetAll(new List<long> { badgeNumber }, fromDate, toDate)
                .Where(r => r.LeaveTypeID == request.LeaveID))
            {
                if (item2.FromTime > item2.ToTime)
                {
                    timeSpan += item2.ToTime.AddHours(24.0) - item2.FromTime;
                }
                else
                {
                    timeSpan += item2.ToTime - item2.FromTime;
                }
            }

            timeSpan += toTime - fromTime;
            if (byID.MaxHoursInAMonth > TimeSpan.Zero && timeSpan > byID.MaxHoursInAMonth)
            {
                return string.Format(PersianString.HourlyLeavePerMonthLimit, byID.MaxHoursInAMonth.TotalHours.ToString("0.0", CultureInfo.InvariantCulture));
            }

            // Fixed critical legacy bug: Legacy code wrapped RequestDA.Edit inside if (!ValidHourlyFree) with no else branch!
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
                !DateTime.TryParse(request.ToTimeStr, out DateTime toTime) ||
                fromTime >= toTime)
            {
                return PersianString.BadTimeFormat;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                string checkTime = EditRequestController.CheckRequestTime(forDate.Value);
                if (!string.IsNullOrEmpty(checkTime))
                {
                    return checkTime;
                }

                DateTime fullFrom = forDate.Value.Date.Add(fromTime.TimeOfDay);
                DateTime fullTo = forDate.Value.Date.Add(toTime.TimeOfDay);
                var existingHourly = adapter.GetEmployeeRequests(badgeNumber, suspendedOnly: false)
                    .Where(r => !r.IsDaily && r.ForDate.Date == forDate.Value.Date)
                    .ToList();

                foreach (var ex in existingHourly)
                {
                    DateTime exFrom = ex.ForDate.Date.Add(ex.FromTime.TimeOfDay);
                    DateTime exTo = ex.ForDate.Date.Add(ex.ToTime.TimeOfDay);
                    if (fullFrom < exTo && exFrom < fullTo)
                    {
                        return PersianString.RequestDailyForDateExists;
                    }
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
                    WorkflowType = TwfWorkflowType.HourlyDuty,
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
            LeaveType byID = t5DBInstance.LeaveTypeDA.GetByID(request.LeaveID);
            if (byID == null)
            {
                return PersianString.BadExitType;
            }

            var sameDayHourly = t5DBInstance.RequestDA
                .GetAll(badgeNumber, true)
                .Where(r => r.ForDate == forDate.Value && !r.IsDaily)
                .Concat(t5DBInstance.RequestDA.GetAll(badgeNumber, false).Where(r => r.ForDate == forDate.Value && !r.IsDaily))
                .ToList();

            var reqInterval = new TimeInterval(
                forDate.Value.Date.Add(fromTime.TimeOfDay),
                forDate.Value.Date.Add(toTime.TimeOfDay));

            if (sameDayHourly.Any(item =>
                TimeInterval.HaveIntersection(
                    reqInterval,
                    new TimeInterval(item.ForDate.Date.Add(item.FromTime.TimeOfDay), item.ForDate.Date.Add(item.ToTime.TimeOfDay)))))
            {
                string checkErr = EditRequestController.CheckRequestTime(forDate.Value);
                return !string.IsNullOrEmpty(checkErr) ? checkErr : PersianString.RequestDailyForDateExists;
            }

            string text2 = EditRequestController.CheckRequestTime(forDate.Value);
            if (!string.IsNullOrEmpty(text2))
            {
                return text2;
            }

            var persianCalendar = new PersianCalendar();
            int year = persianCalendar.GetYear(forDate.Value);
            int month = persianCalendar.GetMonth(forDate.Value);
            DateTime fromDate = persianCalendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
            DateTime toDate = persianCalendar.ToDateTime(year, month, persianCalendar.GetDaysInMonth(year, month), 23, 59, 0, 0);

            TimeSpan timeSpan = TimeSpan.Zero;
            foreach (T5V3.Model.Request item2 in t5DBInstance.RequestDA
                .GetAll(badgeNumber, fromDate, toDate)
                .Where(r => r.LeaveID == request.LeaveID && r.IsSuspend && r.WorkflowType == WorkflowOperations.Duty))
            {
                if (item2.FromTime > item2.ToTime)
                {
                    timeSpan += item2.ToTime.AddHours(24.0) - item2.FromTime;
                }
                else
                {
                    timeSpan += item2.ToTime - item2.FromTime;
                }
            }

            foreach (HourlyLeave item3 in t5DBInstance.HourlyLeaveDA
                .GetAll(new List<long> { badgeNumber }, fromDate, toDate)
                .Where(r => r.LeaveTypeID == request.LeaveID))
            {
                if (item3.FromTime > item3.ToTime)
                {
                    timeSpan += item3.ToTime.AddHours(24.0) - item3.FromTime;
                }
                else
                {
                    timeSpan += item3.ToTime - item3.FromTime;
                }
            }

            timeSpan += toTime - fromTime;
            if (byID.MaxHoursInAMonth > TimeSpan.Zero && timeSpan > byID.MaxHoursInAMonth)
            {
                return string.Format(PersianString.HourlyLeavePerMonthLimit, byID.MaxHoursInAMonth.TotalHours.ToString("0.0", CultureInfo.InvariantCulture));
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
                WorkflowType = WorkflowOperations.Duty,
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
