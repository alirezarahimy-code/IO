using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
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
    /// Rewritten RewardController (Phase 4).
    /// - Adds missing [AuthorizeFilter(Roles = "admin,user")] attribute on Add GET/POST actions.
    /// - Supports DBInitializer.IsTestMode.
    /// </summary>
    public class RewardController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(string forDate = null, int valueType = -1, string validValue = null)
        {
            var rewardPenaltyRequest = new RewardPenaltyRequest
            {
                ValidValue = "00:00",
                ForDateHijri = forDate ?? JalaliDate.FromDateTime(DateTime.Now).ToString()
            };

            GlobalHelper.AddRewardPenaltyTypes(base.HttpContext, base.ViewBag);
            if (validValue != null)
            {
                rewardPenaltyRequest.ValidValue = validValue;
            }
            if (valueType != -1)
            {
                rewardPenaltyRequest.RewardPenaltyType = valueType;
            }

            base.ViewBag.SubUsers = BuildSubUsersSelectList();
            return View(rewardPenaltyRequest);
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Add(RewardPenaltyRequest request)
        {
            GlobalHelper.AddRewardPenaltyTypes(base.HttpContext, base.ViewBag);
            base.ViewBag.SubUsers = BuildSubUsersSelectList();
            base.ViewBag.Message = RequestReward(request);
            return View(request);
        }

        private List<SelectListItem> BuildSubUsersSelectList()
        {
            var list = new List<SelectListItem>();
            int managerTitleId = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                var subTitles = adapter.GetSubordinateTitleIds(managerTitleId, TwfWorkflowType.RewardAndPenalty);
                foreach (var emp in adapter.GetEmployeesByTitleIds(subTitles))
                {
                    list.Add(new SelectListItem
                    {
                        Text = emp.FullName,
                        Value = emp.BadgeNumber.ToString(CultureInfo.InvariantCulture)
                    });
                }
                return list;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<int> titleIDs = t5DBInstance.WorkflowDA
                .GetAll(managerTitleId)
                .Where(wf => wf.Operation == WorkflowOperations.RewardAndPenalty && wf.OrderIndex == 1)
                .Select(wf => wf.TitleID)
                .ToList();

            foreach (Personnel.Model.UserInfo item in personnelSystem.DB.UserInfos.GetAllByTitle(titleIDs))
            {
                list.Add(new SelectListItem
                {
                    Text = item.ToString(),
                    Value = item.BadgeNumber.ToString(CultureInfo.InvariantCulture)
                });
            }
            return list;
        }

        public string RequestReward(RewardPenaltyRequest request)
        {
            if (request == null)
                return PersianString.ErrorInInsertingData;

            if (request.Descriptions == null)
            {
                request.Descriptions = string.Empty;
            }
            if (request.ForBadgeNumber == 0L)
            {
                return PersianString.BadDescriptions;
            }

            DateTime? forDate = DailyLeaveController.ParseHijriDate(request.ForDateHijri);
            if (!forDate.HasValue)
            {
                return PersianString.BadDateFormat;
            }

            if (!TimeSpan.TryParse(request.ValidValue, out TimeSpan duration))
            {
                return PersianString.BadTimeFormat;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                int empTitleId = adapter.GetCurrentTitleIdForEmployee(request.ForBadgeNumber);
                var wfSteps = adapter.GetWorkflowSteps(empTitleId, TwfWorkflowType.RewardAndPenalty);
                var dto = new WorkflowRequestItemDto
                {
                    BadgeNumber = request.ForBadgeNumber,
                    CreatedAt = DateTime.Now,
                    ForDate = forDate.Value.Date,
                    FromTime = DateTime.Now.Date,
                    ToTime = forDate.Value.Date,
                    RecordTime = DateTime.Now.Date,
                    IsDaily = true,
                    IsSuspended = wfSteps.Count > 1,
                    LeaveTypeId = 1,
                    LeaveDescriptionId = 1,
                    AllowDurationMinutes = (int)Math.Round(duration.TotalMinutes),
                    OverTimeAllowType = request.RewardPenaltyType == 6 ? (int)AllowType.Early : (int)AllowType.Late,
                    WorkflowType = TwfWorkflowType.RewardAndPenalty,
                    Descriptions = request.Descriptions
                };

                if (wfSteps.Count <= 1)
                {
                    return adapter.InsertOperationalDataForApprovedRequest(dto, out _)
                        ? PersianString.InsertSuccess
                        : PersianString.ErrorInInsertingData;
                }

                if (adapter.CreateRequest(dto, out _, out long newReqId))
                {
                    dto.RequestId = newReqId;
                    adapter.AddRequestAcceptStep(new RequestAcceptStepDto
                    {
                        RequestId = newReqId,
                        AccepterBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext),
                        AccepterTitleId = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext),
                        AcceptedAt = DateTime.Now,
                        IsCanceled = false
                    }, out _);
                    NotificationManager.NewRequestAdded(EditRequestController.MapDtoToT5Request(dto));
                    return PersianString.InsertSuccess;
                }
                return PersianString.ErrorInInsertingData;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();

            if (t5DBInstance.WorkflowDA.GetAll(personnelSystem.DB.UserInfos.GetUserCurrentTitleID(request.ForBadgeNumber), WorkflowOperations.RewardAndPenalty).Count == 1)
            {
                var editedValue = new EditedValue
                {
                    BadgeNumber = request.ForBadgeNumber,
                    ForDate = forDate.Value,
                    ValidDuration = duration,
                    ValueType = (EditValueType)request.RewardPenaltyType
                };
                return t5DBInstance.EditedValueDA.InsertOrEdit(editedValue, out _)
                    ? PersianString.InsertSuccess
                    : PersianString.ErrorInInsertingData;
            }

            var request2 = new T5V3.Model.Request
            {
                AllowValue = duration,
                BadgeNumber = request.ForBadgeNumber,
                CreateTime = DateTime.Now,
                ForDate = forDate.Value,
                FromTime = DateTime.Now.Date,
                IsSuspend = true,
                IsDaily = true,
                LeaveID = 1,
                LeaveDescriptionID = 1,
                OverTimeAllowType = request.RewardPenaltyType == 6 ? AllowType.Early : AllowType.Late,
                RecordTime = DateTime.Now.Date,
                ToTime = forDate.Value,
                WorkflowType = WorkflowOperations.RewardAndPenalty,
                Descriptions = request.Descriptions
            };

            if (t5DBInstance.RequestDA.AddNew(request2, true, out string err2, out long newID))
            {
                var requestAcceptMdl = new RequestAcceptMdl
                {
                    AcceptDate = DateTime.Now,
                    AccepterBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext),
                    AccepterTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext),
                    IsCancel = false,
                    RequestID = newID
                };
                t5DBInstance.RequestAcceptDA.AddNew(requestAcceptMdl, out err2);
                request2.RequestID = newID;
                NotificationManager.NewRequestAdded(request2);
                return PersianString.InsertSuccess;
            }
            return PersianString.ErrorInInsertingData;
        }
    }
}
