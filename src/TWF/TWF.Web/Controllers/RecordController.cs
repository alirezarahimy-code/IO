using System;
using System.Globalization;
using System.Linq;
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
    /// Rewritten RecordController (Phase 4).
    /// - Fixes EditRequestRecord omitting byID.ForDate update when changing the date of a forgotten record request.
    /// - Fixes RequestRecordsDailyLimit / RequestRecordsMonthlyLimit handling when set to <= 0 (unlimited in Web.config).
    /// - Supports DBInitializer.IsTestMode.
    /// </summary>
    public class RecordController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(string recordDate = null, string recordTime = null)
        {
            if (recordDate != null || recordTime != null)
            {
                return View(new RecordRequest
                {
                    ForDateStr = recordDate,
                    RecordTimeStr = recordTime
                });
            }
            return View();
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(RecordRequest request)
        {
            base.ViewBag.Message = RequestForRecord(request, GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext));
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
                        return View(new RecordRequest
                        {
                            RequestID = dto.RequestId,
                            RecordTimeStr = dto.RecordTime.ToString("HH:mm"),
                            Descriptions = dto.Descriptions,
                            ForDateStr = JalaliDate.FromDateTime(dto.RecordTime).ToString()
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
                    return View(new RecordRequest
                    {
                        RequestID = byID.RequestID,
                        RecordTimeStr = byID.RecordTime.ToString("HH:mm"),
                        Descriptions = byID.Descriptions,
                        ForDateStr = DateConverter.ToHijriDate(byID.RecordTime)
                    });
                }
                return View("Error", "", PersianString.AccessDenied);
            }
            return RedirectToAction("Add");
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Edit(RecordRequest request)
        {
            base.ViewBag.Message = EditRequestRecord(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
            return View("Edit", request);
        }

        public static string EditRequestRecord(long badgeNumber, RecordRequest request)
        {
            if (request == null)
                return PersianString.ErrorInInsertingData;

            if (request.Descriptions == null)
            {
                request.Descriptions = string.Empty;
            }

            DateTime? forDate = DailyLeaveController.ParseHijriDate(request.ForDateStr);
            if (!forDate.HasValue)
            {
                return PersianString.BadDateFormat;
            }

            if (!DateTime.TryParse(request.RecordTimeStr, out DateTime parsedTime) || parsedTime == default(DateTime))
            {
                return PersianString.BadTimeFormat;
            }

            string checkTime = EditRequestController.CheckRequestTime(forDate.Value);
            if (!string.IsNullOrEmpty(checkTime))
            {
                return checkTime;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                var dto = adapter.GetRequestById(request.RequestID);
                if (dto == null)
                {
                    return PersianString.ErrorInInsertingData;
                }

                int dailyLimit = (int)SettingsController.LoadValue("RequestRecordsDailyLimit", typeof(int), 0);
                int monthlyLimit = (int)SettingsController.LoadValue("RequestRecordsMonthlyLimit", typeof(int), 0);

                if (dto.RecordTime.Date != forDate.Value.Date)
                {
                    if (dailyLimit > 0 && GetRecordRequestsCount(badgeNumber, forDate.Value.Date, forDate.Value.Date.AddHours(23.9)) >= dailyLimit)
                    {
                        return PersianString.RequestRecordsDailyLimit;
                    }
                    if (monthlyLimit > 0 && GetRecordRequestsCount(badgeNumber, MonthStart(forDate.Value), MonthEnd(forDate.Value)) >= monthlyLimit)
                    {
                        return PersianString.RequestRecordsMonthlyLimit;
                    }
                }

                dto.ForDate = forDate.Value.Date;
                dto.RecordTime = forDate.Value.Date.Add(parsedTime.TimeOfDay);
                dto.Descriptions = request.Descriptions;

                if (adapter.UpdateRequest(dto, out _))
                {
                    NotificationManager.NewRequestAdded(EditRequestController.MapDtoToT5Request(dto));
                    return PersianString.InsertSuccess;
                }
                return PersianString.ErrorInInsertingData;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(request.RequestID);
            if (byID == null)
            {
                return PersianString.ErrorInInsertingData;
            }

            int num = (int)SettingsController.LoadValue("RequestRecordsDailyLimit", typeof(int), 0);
            int num2 = (int)SettingsController.LoadValue("RequestRecordsMonthlyLimit", typeof(int), 0);
            Rule activeRule = new RuleProcessor(
                t5DBInstance.RuleDA.GetAll(),
                t5DBInstance.DepartmentRuleDA.GetAll(),
                t5DBInstance.UserRuleDA.GetAll())
                .GetActiveRule(personnelSystem.DB.UserInfos.GetByID(byID.BadgeNumber), null, DateTime.Now);

            if (activeRule != null && activeRule.RuleID != 0)
            {
                num = activeRule.LimitRecReqInDay;
                num2 = activeRule.LimitRecReqInMonth;
            }

            if (byID.RecordTime.Date != forDate.Value.Date)
            {
                if (num > 0 && GetRecordRequestsCount(badgeNumber, forDate.Value.Date, forDate.Value.AddHours(23.9)) >= num)
                {
                    return PersianString.RequestRecordsDailyLimit;
                }
                if (num2 > 0 && GetRecordRequestsCount(badgeNumber, MonthStart(forDate.Value), MonthEnd(forDate.Value)) >= num2)
                {
                    return PersianString.RequestRecordsMonthlyLimit;
                }
            }

            // Fixed legacy bug where byID.ForDate was not updated alongside byID.RecordTime
            byID.ForDate = forDate.Value.Date;
            byID.RecordTime = forDate.Value.Date.Add(parsedTime.TimeOfDay);
            byID.Descriptions = request.Descriptions;

            if (t5DBInstance.RequestDA.Edit(byID, out _))
            {
                NotificationManager.NewRequestAdded(byID);
                return PersianString.InsertSuccess;
            }
            return PersianString.ErrorInInsertingData;
        }

        private static int GetRecordRequestsCount(long badgeNumber, DateTime fromDate, DateTime toDate)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                int count = 0;
                foreach (var req in adapter.GetEmployeeRequestsInPeriod(badgeNumber, fromDate, toDate))
                {
                    if (req.WorkflowType == TwfWorkflowType.Record &&
                        adapter.GetRequestAcceptHistory(req.RequestId).All(a => !a.IsCanceled))
                    {
                        count++;
                    }
                }
                return count;
            }

            int num = 0;
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(badgeNumber, fromDate, toDate))
            {
                if (item.WorkflowType == WorkflowOperations.InsertRecord &&
                    t5DBInstance.RequestAcceptDA.GetByID(item.RequestID).All(re => !re.IsCancel))
                {
                    num++;
                }
            }
            return num;
        }

        private static DateTime MonthStart(DateTime d)
        {
            var persianCalendar = new PersianCalendar();
            return persianCalendar.ToDateTime(persianCalendar.GetYear(d), persianCalendar.GetMonth(d), 1, 0, 0, 0, 0);
        }

        private static DateTime MonthEnd(DateTime d)
        {
            var persianCalendar = new PersianCalendar();
            int year = persianCalendar.GetYear(d);
            int month = persianCalendar.GetMonth(d);
            return persianCalendar.ToDateTime(year, month, persianCalendar.GetDaysInMonth(year, month), 23, 59, 59, 0);
        }

        public static string RequestForRecord(RecordRequest request, long badgenumber)
        {
            if (request == null)
                return PersianString.ErrorInInsertingData;

            if (request.Descriptions == null)
            {
                request.Descriptions = string.Empty;
            }

            DateTime? forDate = DailyLeaveController.ParseHijriDate(request.ForDateStr);
            if (!forDate.HasValue)
            {
                return PersianString.BadDateFormat;
            }

            if (!DateTime.TryParse(request.RecordTimeStr, out DateTime parsedTime) || parsedTime == default(DateTime))
            {
                return PersianString.BadTimeFormat;
            }

            DateTime recTime = forDate.Value.Date.Add(parsedTime.TimeOfDay);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                bool exists = adapter.GetEmployeeRequests(badgenumber, suspendedOnly: true)
                    .Any(r => r.WorkflowType == TwfWorkflowType.Record && r.RecordTime == recTime);
                if (exists)
                {
                    return PersianString.RequestExists;
                }

                int dailyLimit = (int)SettingsController.LoadValue("RequestRecordsDailyLimit", typeof(int), 0);
                int monthlyLimit = (int)SettingsController.LoadValue("RequestRecordsMonthlyLimit", typeof(int), 0);

                if (dailyLimit > 0 && GetRecordRequestsCount(badgenumber, forDate.Value.Date, forDate.Value.Date.AddHours(23.9)) >= dailyLimit)
                {
                    return PersianString.RequestRecordsDailyLimit;
                }
                if (monthlyLimit > 0 && GetRecordRequestsCount(badgenumber, MonthStart(forDate.Value), MonthEnd(forDate.Value)) >= monthlyLimit)
                {
                    return PersianString.RequestRecordsMonthlyLimit;
                }

                string checkTime = EditRequestController.CheckRequestTime(forDate.Value);
                if (!string.IsNullOrEmpty(checkTime))
                {
                    return checkTime;
                }

                var dto = new WorkflowRequestItemDto
                {
                    BadgeNumber = badgenumber,
                    CreatedAt = DateTime.Now,
                    ForDate = forDate.Value.Date,
                    FromTime = DateTime.Now.Date,
                    ToTime = DateTime.Now.Date,
                    RecordTime = recTime,
                    IsDaily = false,
                    IsSuspended = true,
                    LeaveTypeId = -1,
                    LeaveDescriptionId = -1,
                    OverTimeAllowType = (int)AllowType.Early,
                    WorkflowType = TwfWorkflowType.Record,
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

            if (t5DBInstance.RequestDA.GetAll(badgenumber, true).Any(r => r.WorkflowType == WorkflowOperations.InsertRecord && r.RecordTime == recTime))
            {
                return PersianString.RequestExists;
            }

            int num = (int)SettingsController.LoadValue("RequestRecordsDailyLimit", typeof(int), 0);
            int num2 = (int)SettingsController.LoadValue("RequestRecordsMonthlyLimit", typeof(int), 0);
            Rule activeRule = new RuleProcessor(
                t5DBInstance.RuleDA.GetAll(),
                t5DBInstance.DepartmentRuleDA.GetAll(),
                t5DBInstance.UserRuleDA.GetAll())
                .GetActiveRule(personnelSystem.DB.UserInfos.GetByID(badgenumber), null, DateTime.Now);

            if (activeRule != null && activeRule.RuleID != 0)
            {
                num = activeRule.LimitRecReqInDay;
                num2 = activeRule.LimitRecReqInMonth;
            }

            if (num > 0 && GetRecordRequestsCount(badgenumber, forDate.Value.Date, forDate.Value.AddHours(23.9)) >= num)
            {
                return PersianString.RequestRecordsDailyLimit;
            }
            if (num2 > 0 && GetRecordRequestsCount(badgenumber, MonthStart(forDate.Value), MonthEnd(forDate.Value)) >= num2)
            {
                return PersianString.RequestRecordsMonthlyLimit;
            }

            string text2 = EditRequestController.CheckRequestTime(forDate.Value);
            if (!string.IsNullOrEmpty(text2))
            {
                return text2;
            }

            var request2 = new T5V3.Model.Request
            {
                AllowValue = TimeSpan.Zero,
                BadgeNumber = badgenumber,
                CreateTime = DateTime.Now,
                ForDate = forDate.Value,
                FromTime = DateTime.Now.Date,
                IsSuspend = true,
                IsDaily = false,
                LeaveID = -1,
                LeaveDescriptionID = -1,
                OverTimeAllowType = AllowType.Early,
                RecordTime = recTime,
                ToTime = DateTime.Now.Date,
                WorkflowType = WorkflowOperations.InsertRecord,
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
