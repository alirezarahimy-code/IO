using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using MyTime3.Content;
using MyTime3.Models;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten DailyDutyController (Phase 4).
    /// Supports both live T5V3DBManager and DBInitializer.IsTestMode.
    /// </summary>
    public class DailyDutyController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(string forDate = null)
        {
            var dailyExitRequest = new DailyExitRequest();
            GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
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
                    GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
                    GlobalHelper.AddLeaveDescriptions(base.ViewBag);
                    return View(new DailyExitRequest
                    {
                        RequestID = byID.RequestID,
                        Descriptions = byID.Descriptions,
                        ForDateHijri = DateConverter.ToHijriDate(byID.ForDate),
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
            GlobalHelper.AddDutyTypes(base.HttpContext, base.ViewBag);
            GlobalHelper.AddLeaveDescriptions(base.ViewBag);
            return View("Edit", request);
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

            DateTime? forDate = DailyLeaveController.ParseHijriDate(request.ForDateHijri);
            DateTime? toDate = DailyLeaveController.ParseHijriDate(request.ToDateHijri);
            if (!forDate.HasValue || !toDate.HasValue || toDate.Value.Date < forDate.Value.Date)
            {
                return PersianString.BadDateFormat;
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
            return AddRequest(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
        }

        public static string AddRequest(long badgeNumber, DailyExitRequest request)
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
            DateTime? toDate = DailyLeaveController.ParseHijriDate(request.ToDateHijri);
            if (!forDate.HasValue || !toDate.HasValue || toDate.Value.Date < forDate.Value.Date)
            {
                return PersianString.BadDateFormat;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
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

                string checkTime = EditRequestController.CheckRequestTime(forDate.Value);
                if (!string.IsNullOrEmpty(checkTime))
                {
                    return checkTime;
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
                    WorkflowType = TwfWorkflowType.Duty,
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
            if (t5DBInstance.DailyLeaveDA.GetByID(badgeNumber, forDate.Value) != null)
            {
                return PersianString.DailyLeaveExists;
            }

            List<T5V3.Model.Request> source = t5DBInstance.RequestDA
                .GetAll(badgeNumber, true)
                .Where(r => r.IsDaily)
                .ToList();

            if (source.Any(r => r.ForDate <= forDate.Value && r.ToTime >= forDate.Value) ||
                source.Any(r => r.ForDate <= toDate.Value && r.ToTime >= toDate.Value))
            {
                return PersianString.RequestDailyForDateExists;
            }

            string text2 = EditRequestController.CheckRequestTime(forDate.Value);
            if (!string.IsNullOrEmpty(text2))
            {
                return text2;
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
