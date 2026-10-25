using System;
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
    /// Rewritten EarlyController (Phase 4).
    /// Supports both live T5V3DBManager and DBInitializer.IsTestMode.
    /// </summary>
    public class EarlyController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(string earlyDate = null)
        {
            if (earlyDate != null)
            {
                return View(new LateEarlyRequest
                {
                    ForDateStr = earlyDate
                });
            }
            return View();
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(LateEarlyRequest request)
        {
            base.ViewBag.Message = RequestEarly(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), request);
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
                        return View(new LateEarlyRequest
                        {
                            RequestID = dto.RequestId,
                            Descriptions = dto.Descriptions,
                            ForDateStr = JalaliDate.FromDateTime(dto.ForDate).ToString()
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
                    return View(new LateEarlyRequest
                    {
                        RequestID = byID.RequestID,
                        Descriptions = byID.Descriptions,
                        ForDateStr = DateConverter.ToHijriDate(byID.ForDate)
                    });
                }
                return View("Error", "", PersianString.AccessDenied);
            }
            return RedirectToAction("Add");
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Edit(LateEarlyRequest request)
        {
            base.ViewBag.Message = EditRequestEarly(request);
            return View("Edit", request);
        }

        public static string EditRequestEarly(LateEarlyRequest request)
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

            string checkTime = EditRequestController.CheckRequestTime(forDate.Value);
            if (!string.IsNullOrEmpty(checkTime))
            {
                return checkTime;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var dto = DBInitializer.TestBackendAdapter.GetRequestById(request.RequestID);
                if (dto == null)
                {
                    return PersianString.ErrorInInsertingData;
                }
                dto.ForDate = forDate.Value.Date;
                dto.Descriptions = request.Descriptions;
                if (DBInitializer.TestBackendAdapter.UpdateRequest(dto, out _))
                {
                    NotificationManager.NewRequestAdded(EditRequestController.MapDtoToT5Request(dto));
                    return PersianString.InsertSuccess;
                }
                return PersianString.ErrorInInsertingData;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(request.RequestID);
            if (byID == null)
            {
                return PersianString.ErrorInInsertingData;
            }

            byID.ForDate = forDate.Value;
            byID.Descriptions = request.Descriptions;
            if (t5DBInstance.RequestDA.Edit(byID, out _))
            {
                NotificationManager.NewRequestAdded(byID);
                return PersianString.InsertSuccess;
            }
            return PersianString.ErrorInInsertingData;
        }

        public static string RequestEarly(long badgeNumber, LateEarlyRequest request)
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

            string checkTime = EditRequestController.CheckRequestTime(forDate.Value);
            if (!string.IsNullOrEmpty(checkTime))
            {
                return checkTime;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                bool duplicate = adapter.GetEmployeeRequests(badgeNumber, suspendedOnly: true)
                    .Any(r =>
                        r.ForDate.Date == forDate.Value.Date &&
                        r.WorkflowType == TwfWorkflowType.LateEarly &&
                        r.OverTimeAllowType == (int)AllowType.Early);

                if (duplicate)
                {
                    return PersianString.RequestExists;
                }

                var dto = new WorkflowRequestItemDto
                {
                    BadgeNumber = badgeNumber,
                    CreatedAt = DateTime.Now,
                    ForDate = forDate.Value.Date,
                    FromTime = DateTime.Now.Date,
                    ToTime = DateTime.Now.Date,
                    RecordTime = DateTime.Now.Date,
                    IsDaily = false,
                    IsSuspended = true,
                    LeaveTypeId = -1,
                    LeaveDescriptionId = -1,
                    OverTimeAllowType = (int)AllowType.Early,
                    WorkflowType = TwfWorkflowType.LateEarly,
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
            bool exists = t5DBInstance.RequestDA
                .GetAll(badgeNumber, true)
                .Any(r =>
                    r.ForDate == forDate.Value &&
                    r.WorkflowType == WorkflowOperations.LateEarly &&
                    r.OverTimeAllowType == AllowType.Early);

            if (exists)
            {
                return PersianString.RequestExists;
            }

            var request2 = new T5V3.Model.Request
            {
                AllowValue = TimeSpan.Zero,
                BadgeNumber = badgeNumber,
                CreateTime = DateTime.Now,
                ForDate = forDate.Value,
                FromTime = DateTime.Now.Date,
                IsSuspend = true,
                IsDaily = false,
                LeaveID = -1,
                LeaveDescriptionID = -1,
                OverTimeAllowType = AllowType.Early,
                RecordTime = DateTime.Now.Date,
                ToTime = DateTime.Now.Date,
                WorkflowType = WorkflowOperations.LateEarly,
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
