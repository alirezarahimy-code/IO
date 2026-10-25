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
    /// Rewritten OverTimeController (Phase 4).
    /// - Enforces CheckRequestTime cutoff in RequestOverTime.
    /// - Preserves updated Descriptions when modifying an existing overtime request for the same day/type.
    /// - Supports DBInitializer.IsTestMode.
    /// </summary>
    public class OverTimeController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(string forDate = null, int valueType = -1, string validValue = null)
        {
            var overTimeRequest = new OverTimeRequest
            {
                ValidValue = "00:00"
            };
            GlobalHelper.AddOverTimeTypes(base.HttpContext, base.ViewBag);
            if (forDate != null)
            {
                overTimeRequest.ForDateHijri = forDate;
            }
            if (validValue != null)
            {
                overTimeRequest.ValidValue = validValue;
            }
            if (valueType != -1)
            {
                overTimeRequest.OverTimeType = valueType;
            }
            return View(overTimeRequest);
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(OverTimeRequest request)
        {
            GlobalHelper.AddOverTimeTypes(base.HttpContext, base.ViewBag);
            base.ViewBag.Message = RequestOverTime(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
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
                        TimeSpan allowTs = TimeSpan.FromMinutes(dto.AllowDurationMinutes);
                        return RedirectToAction("Add", new
                        {
                            forDate = JalaliDate.FromDateTime(dto.ForDate).ToString(),
                            valueType = dto.OverTimeAllowType,
                            validValue = allowTs.ToString()
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
                    return RedirectToAction("Add", new
                    {
                        forDate = DateConverter.ToHijriDate(byID.ForDate),
                        valueType = (int)byID.OverTimeAllowType,
                        validValue = byID.AllowValue.ToString()
                    });
                }
                return View("Error", "", PersianString.AccessDenied);
            }
            return RedirectToAction("Add");
        }

        public static string RequestOverTime(long badgeNumber, OverTimeRequest request)
        {
            if (request == null)
                return PersianString.ErrorInInsertingData;

            DateTime? forDate = DailyLeaveController.ParseHijriDate(request.ForDateHijri);
            if (!forDate.HasValue)
            {
                return PersianString.BadDateFormat;
            }

            if (!TimeSpan.TryParse(request.ValidValue, out TimeSpan duration))
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
                var existing = adapter.GetEmployeeRequests(badgeNumber, suspendedOnly: true)
                    .FirstOrDefault(r =>
                        r.ForDate.Date == forDate.Value.Date &&
                        r.WorkflowType == TwfWorkflowType.AllowOverTimes &&
                        r.OverTimeAllowType == request.OverTimeType);

                if (existing == null)
                {
                    var dto = new WorkflowRequestItemDto
                    {
                        BadgeNumber = badgeNumber,
                        CreatedAt = DateTime.Now,
                        ForDate = forDate.Value.Date,
                        FromTime = DateTime.Now.Date,
                        ToTime = DateTime.Now.Date,
                        RecordTime = DateTime.Now.Date,
                        IsDaily = true,
                        IsSuspended = true,
                        AllowDurationMinutes = (int)Math.Round(duration.TotalMinutes),
                        OverTimeAllowType = request.OverTimeType,
                        WorkflowType = TwfWorkflowType.AllowOverTimes,
                        Descriptions = request.Descriptions ?? string.Empty
                    };

                    if (adapter.CreateRequest(dto, out _, out long newReqId))
                    {
                        dto.RequestId = newReqId;
                        NotificationManager.NewRequestAdded(EditRequestController.MapDtoToT5Request(dto));
                        return PersianString.InsertSuccess;
                    }
                    return PersianString.ErrorInInsertingData;
                }
                else
                {
                    existing.AllowDurationMinutes = (int)Math.Round(duration.TotalMinutes);
                    existing.CreatedAt = DateTime.Now;
                    if (!string.IsNullOrEmpty(request.Descriptions))
                    {
                        existing.Descriptions = request.Descriptions;
                    }
                    if (adapter.UpdateRequest(existing, out _))
                    {
                        NotificationManager.NewRequestAdded(EditRequestController.MapDtoToT5Request(existing));
                        return PersianString.InsertSuccess;
                    }
                    return PersianString.ErrorInInsertingData;
                }
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            var matching = t5DBInstance.RequestDA
                .GetAll(badgeNumber, true)
                .Where(r =>
                    r.ForDate == forDate.Value &&
                    r.WorkflowType == WorkflowOperations.AllowOverTimes &&
                    r.OverTimeAllowType == (AllowType)request.OverTimeType)
                .ToList();

            if (matching.Count == 0)
            {
                var request2 = new T5V3.Model.Request
                {
                    AllowValue = duration,
                    BadgeNumber = badgeNumber,
                    CreateTime = DateTime.Now,
                    ForDate = forDate.Value,
                    FromTime = DateTime.Now.Date,
                    IsSuspend = true,
                    IsDaily = true,
                    LeaveID = 0,
                    LeaveDescriptionID = 0,
                    OverTimeAllowType = (AllowType)request.OverTimeType,
                    RecordTime = DateTime.Now.Date,
                    ToTime = DateTime.Now.Date,
                    WorkflowType = WorkflowOperations.AllowOverTimes,
                    Descriptions = request.Descriptions ?? string.Empty
                };

                if (t5DBInstance.RequestDA.AddNew(request2, true, out _, out long newID))
                {
                    request2.RequestID = newID;
                    NotificationManager.NewRequestAdded(request2);
                    return PersianString.InsertSuccess;
                }
                return PersianString.ErrorInInsertingData;
            }
            else
            {
                T5V3.Model.Request existingReq = matching[0];
                existingReq.AllowValue = duration;
                existingReq.CreateTime = DateTime.Now;
                if (request.Descriptions != null)
                {
                    existingReq.Descriptions = request.Descriptions;
                }

                if (t5DBInstance.RequestDA.Edit(existingReq, out _))
                {
                    NotificationManager.NewRequestAdded(existingReq);
                    return PersianString.InsertSuccess;
                }
                return PersianString.ErrorInInsertingData;
            }
        }
    }
}
