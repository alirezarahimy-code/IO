using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using MyTime3.Authorization;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten EditRequestController (Phase 4).
    /// - Fixes NullReferenceExceptions when RequestDA.GetByID or UserInfos.GetByID returns null.
    /// - Enforces RequestsLock cutoff alongside MaxRequestAgeDays and LastDayForRequest in CheckRequestTime.
    /// - Supports DBInitializer.IsTestMode for full in-memory unit/integration testing.
    /// </summary>
    public class EditRequestController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index()
        {
            long badgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            var list = new List<MyTime3.Models.Request>();

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                foreach (var dto in adapter.GetEmployeeRequests(badgeNumber, suspendedOnly: true))
                {
                    if (adapter.IsRequestEditableByEmployee(dto.RequestId))
                    {
                        list.Add(MapDtoToModelRequest(dto));
                    }
                }

                if ((bool)SettingsController.LoadValue("RequestForUsers", typeof(bool), true))
                {
                    foreach (PersonnelModel item in RequestController.AddPersonnelToViewBag(base.HttpContext))
                    {
                        foreach (var dto in adapter.GetEmployeeRequests(item.BadgeNumber, suspendedOnly: true))
                        {
                            if (adapter.IsRequestEditableByEmployee(dto.RequestId) &&
                                list.All(r => r.RequestID != dto.RequestId))
                            {
                                list.Add(MapDtoToModelRequest(dto));
                            }
                        }
                    }
                }

                return View(list);
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();

            if (badgeNumber > 0)
            {
                foreach (T5V3.Model.Request current in t5DBInstance.RequestDA.GetAll(badgeNumber, true))
                {
                    if (t5DBInstance.RequestAcceptDA.IsEditableRequest(current.RequestID))
                    {
                        list.Add(new MyTime3.Models.Request { RequestT5 = current });
                    }
                }
            }

            if ((bool)SettingsController.LoadValue("RequestForUsers", typeof(bool), true))
            {
                foreach (PersonnelModel item in RequestController.AddPersonnelToViewBag(base.HttpContext))
                {
                    foreach (T5V3.Model.Request current in t5DBInstance.RequestDA.GetAll(item.BadgeNumber, true))
                    {
                        if (t5DBInstance.RequestAcceptDA.IsEditableRequest(current.RequestID) &&
                            list.All(r => r.RequestID != current.RequestID))
                        {
                            list.Add(new MyTime3.Models.Request { RequestT5 = current });
                        }
                    }
                }
            }

            foreach (MyTime3.Models.Request item2 in list)
            {
                var user = personnelSystem.DB.UserInfos.GetByID(item2.BadgeNumber);
                if (user != null && item2.RequestT5 != null)
                {
                    item2.RequestT5.RequesterName = user.ToString();
                }
            }

            return View(list);
        }

        [HttpPost]
        public JsonResult GetRequests(string userName, string password)
        {
            var list = new List<MyTime3.Models.Request>();

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                long? badge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    foreach (var dto in DBInitializer.TestBackendAdapter.GetEmployeeRequests(badge.Value, suspendedOnly: true))
                    {
                        if (DBInitializer.TestBackendAdapter.IsRequestEditableByEmployee(dto.RequestId))
                        {
                            list.Add(MapDtoToModelRequest(dto));
                        }
                    }
                }
                return Json(list);
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
            if (num > 0)
            {
                foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(num, true))
                {
                    if (t5DBInstance.RequestAcceptDA.IsEditableRequest(item.RequestID))
                    {
                        list.Add(new MyTime3.Models.Request { RequestT5 = item });
                    }
                }
            }
            return Json(list);
        }

        [HttpPost]
        public JsonResult CancelRequest(string userName, string password, long requestID)
        {
            string message = string.Empty;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                long? badge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    var req = DBInitializer.TestBackendAdapter.GetRequestById(requestID);
                    if (req == null || req.BadgeNumber != badge.Value || !DBInitializer.TestBackendAdapter.IsRequestEditableByEmployee(requestID))
                    {
                        message = "-2";
                    }
                    else
                    {
                        message = DBInitializer.TestBackendAdapter.DeleteRequest(requestID, out _) ? "1" : "-1";
                    }
                }
                return Json(new { Message = message });
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
            if (num > 0)
            {
                T5V3.Model.Request existing = t5DBInstance.RequestDA.GetByID(requestID);
                if (existing == null || existing.BadgeNumber != num || !t5DBInstance.RequestAcceptDA.IsEditableRequest(requestID))
                {
                    message = "-2";
                }
                else
                {
                    message = t5DBInstance.RequestDA.Delete(requestID, out _) ? "1" : "-1";
                }
            }
            return Json(new { Message = message });
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult EditByID(long requestID)
        {
            long loggedInBadge = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            var delegatedBadges = new List<long>();
            if ((bool)SettingsController.LoadValue("RequestForUsers", typeof(bool), true))
            {
                delegatedBadges = RequestController.AddPersonnelToViewBag(base.HttpContext)
                    .Select(r => r.BadgeNumber)
                    .ToList();
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var dto = DBInitializer.TestBackendAdapter.GetRequestById(requestID);
                if (dto != null &&
                    (dto.BadgeNumber == loggedInBadge || delegatedBadges.Contains(dto.BadgeNumber)) &&
                    DBInitializer.TestBackendAdapter.IsRequestEditableByEmployee(requestID))
                {
                    string checkErr = CheckRequestTime(dto.ForDate);
                    if (string.IsNullOrEmpty(checkErr))
                    {
                        GeneralRequest generalReq = MapT5RequestToGeneralRequest(MapDtoToT5Request(dto));
                        return RedirectToAction("New", "Request", generalReq);
                    }
                    return View("Error", "", checkErr);
                }
                return View("Error", "", PersianString.AccessDenied);
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(requestID);
            if (byID != null &&
                (byID.BadgeNumber == loggedInBadge || delegatedBadges.Contains(byID.BadgeNumber)) &&
                t5DBInstance.RequestAcceptDA.IsEditableRequest(requestID))
            {
                string text = CheckRequestTime(byID.ForDate);
                if (string.IsNullOrEmpty(text))
                {
                    GeneralRequest generalRequest = MapT5RequestToGeneralRequest(byID);
                    return RedirectToAction("New", "Request", generalRequest);
                }
                return View("Error", "", text);
            }
            return View("Error", "", PersianString.AccessDenied);
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Delete(long requestID)
        {
            long loggedInBadge = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
            var delegatedBadges = new List<long>();
            if ((bool)SettingsController.LoadValue("RequestForUsers", typeof(bool), true))
            {
                delegatedBadges = RequestController.AddPersonnelToViewBag(base.HttpContext)
                    .Select(r => r.BadgeNumber)
                    .ToList();
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var dto = DBInitializer.TestBackendAdapter.GetRequestById(requestID);
                if (dto != null &&
                    (dto.BadgeNumber == loggedInBadge || delegatedBadges.Contains(dto.BadgeNumber)) &&
                    DBInitializer.TestBackendAdapter.IsRequestEditableByEmployee(requestID))
                {
                    if (DBInitializer.TestBackendAdapter.DeleteRequest(requestID, out string err))
                    {
                        return RedirectToAction("Index");
                    }
                    return View("Error", "", err);
                }
                return View("Error", "", PersianString.AccessDenied);
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(requestID);
            if (byID != null &&
                (byID.BadgeNumber == loggedInBadge || delegatedBadges.Contains(byID.BadgeNumber)) &&
                t5DBInstance.RequestAcceptDA.IsEditableRequest(requestID))
            {
                if (t5DBInstance.RequestDA.Delete(requestID, out string err))
                {
                    return RedirectToAction("Index");
                }
                return View("Error", "", err);
            }
            return View("Error", "", PersianString.AccessDenied);
        }

        public static string CheckRequestTime(DateTime forDate)
        {
            string result = string.Empty;

            // Check global RequestsLock OADate cutoff if configured
            double lockOaDate = HomeController.RequestLockValuee();
            if (lockOaDate > 0.0)
            {
                try
                {
                    DateTime lockCutoff = DateTime.FromOADate(lockOaDate).Date;
                    if (lockCutoff > new DateTime(2000, 1, 2) && forDate.Date <= lockCutoff)
                    {
                        return PersianString.RequestsLockInfo;
                    }
                }
                catch
                {
                }
            }

            int maxAgeDays = (int)SettingsController.LoadValue("MaxRequestAgeDays", typeof(int), 0);
            int lastDayOfMonth = (int)SettingsController.LoadValue("LastDayForRequest", typeof(int), 0);

            if (maxAgeDays > 0)
            {
                if (forDate.Date.AddDays(maxAgeDays) <= DateTime.Now.Date)
                {
                    result = string.Format(PersianString.MaxRequestTime, maxAgeDays.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (lastDayOfMonth > 0)
            {
                var pc = new PersianCalendar();
                DateTime today = DateTime.Now.Date;
                if (pc.GetDayOfMonth(today) >= lastDayOfMonth && forDate.Date < today)
                {
                    int currentMonth = pc.GetMonth(today);
                    int targetMonth = pc.GetMonth(forDate.Date);
                    int currentYear = pc.GetYear(today);
                    int targetYear = pc.GetYear(forDate.Date);

                    bool isPreviousPeriod =
                        targetYear < currentYear ||
                        (targetYear == currentYear && targetMonth < currentMonth);

                    if (isPreviousPeriod)
                    {
                        result = string.Format(PersianString.LastValidDayOfMonth, lastDayOfMonth.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            return result;
        }

        internal static GeneralRequest MapT5RequestToGeneralRequest(T5V3.Model.Request byID)
        {
            string forDateHijri = JalaliDate.FromDateTime(byID.ForDate).ToString();
            string toDateHijri = JalaliDate.FromDateTime(byID.ToTime).ToString();

            var generalRequest = new GeneralRequest
            {
                IsNew = false,
                RequestID = byID.RequestID,
                LeaveTypeID = byID.LeaveID,
                ForDateHijri = forDateHijri,
                FromTime = byID.FromTime.ToString("HH:mm"),
                ToTime = byID.ToTime.ToString("HH:mm"),
                ForTime = byID.RecordTime.ToString("HH:mm"),
                FromDateHijri = forDateHijri,
                ToDateHijri = toDateHijri,
                Descriptions = byID.Descriptions,
                LeaveDescriptionsID = byID.LeaveDescriptionID,
                OvertimeValue = $"{(int)byID.AllowValue.TotalHours:00}:{byID.AllowValue.Minutes:00}"
            };

            if (byID.WorkflowType == WorkflowOperations.AllowOverTimes)
            {
                generalRequest.RequestType = 7;
                if (byID.OverTimeAllowType == AllowType.OverTimeBefore)
                    generalRequest.LeaveTypeID = -2;
                else if (byID.OverTimeAllowType == AllowType.OverTimeAfter)
                    generalRequest.LeaveTypeID = -1;
                else if (byID.OverTimeAllowType == AllowType.FreeOverTime)
                    generalRequest.LeaveTypeID = -3;
            }
            else if (byID.WorkflowType == WorkflowOperations.Leave)
            {
                generalRequest.RequestType = byID.IsDaily ? 0 : 1;
            }
            else if (byID.WorkflowType == WorkflowOperations.Duty)
            {
                generalRequest.RequestType = byID.IsDaily ? 2 : 3;
            }
            else if (byID.WorkflowType == WorkflowOperations.InsertRecord)
            {
                generalRequest.RequestType = 4;
            }
            else if (byID.WorkflowType == WorkflowOperations.LateEarly)
            {
                generalRequest.RequestType = byID.OverTimeAllowType == AllowType.Early ? 5 : 6;
            }

            return generalRequest;
        }

        public static T5V3.Model.Request MapDtoToT5Request(WorkflowRequestItemDto dto)
        {
            if (dto == null)
                return null;

            return new T5V3.Model.Request
            {
                RequestID = dto.RequestId,
                BadgeNumber = dto.BadgeNumber,
                RequesterName = dto.RequesterFullName ?? string.Empty,
                CreateTime = dto.CreatedAt,
                ForDate = dto.ForDate,
                FromTime = dto.FromTime,
                ToTime = dto.ToTime,
                RecordTime = dto.RecordTime,
                IsDaily = dto.IsDaily,
                IsSuspend = dto.IsSuspended,
                LeaveID = dto.LeaveTypeId,
                LeaveDescriptionID = dto.LeaveDescriptionId,
                AllowValue = TimeSpan.FromMinutes(dto.AllowDurationMinutes),
                OverTimeAllowType = (AllowType)dto.OverTimeAllowType,
                WorkflowType = MapTwfWorkflowToT5(dto.WorkflowType),
                Descriptions = dto.Descriptions ?? string.Empty,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                ImageData = dto.ImageData
            };
        }

        public static MyTime3.Models.Request MapDtoToModelRequest(WorkflowRequestItemDto dto)
        {
            return new MyTime3.Models.Request
            {
                RequestT5 = MapDtoToT5Request(dto)
            };
        }

        internal static WorkflowOperations MapTwfWorkflowToT5(TwfWorkflowType wf)
        {
            switch (wf)
            {
                case TwfWorkflowType.Leave: return WorkflowOperations.Leave;
                case TwfWorkflowType.Duty: return WorkflowOperations.Duty;
                case TwfWorkflowType.Record: return WorkflowOperations.InsertRecord;
                case TwfWorkflowType.LateEarly: return WorkflowOperations.LateEarly;
                case TwfWorkflowType.AllowOverTimes: return WorkflowOperations.AllowOverTimes;
                case TwfWorkflowType.RewardAndPenalty: return WorkflowOperations.RewardAndPenalty;
                default: return WorkflowOperations.Leave;
            }
        }

        internal static TwfWorkflowType MapT5WorkflowToTwf(WorkflowOperations wf)
        {
            switch (wf)
            {
                case WorkflowOperations.Leave: return TwfWorkflowType.Leave;
                case WorkflowOperations.Duty: return TwfWorkflowType.Duty;
                case WorkflowOperations.InsertRecord: return TwfWorkflowType.Record;
                case WorkflowOperations.LateEarly: return TwfWorkflowType.LateEarly;
                case WorkflowOperations.AllowOverTimes: return TwfWorkflowType.AllowOverTimes;
                case WorkflowOperations.RewardAndPenalty: return TwfWorkflowType.RewardAndPenalty;
                default: return TwfWorkflowType.Leave;
            }
        }
    }
}
