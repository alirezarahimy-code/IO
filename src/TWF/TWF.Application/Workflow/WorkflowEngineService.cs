using System;
using System.Collections.Generic;
using System.Linq;
using TWF.Application.Leaves;
using TWF.Application.Organization;
using TWF.Domain.Interfaces;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;
using TWF.Infrastructure.Localization;

namespace TWF.Application.Workflow
{
    /// <summary>
    /// Core multi-step workflow and approval engine for TWF.
    /// Replaces AcceptRequestController, EditRequestController, UndoController, and RequestController workflow logic,
    /// integrating T5 WorkflowDA, RequestAcceptDA, PersonnelDeputyDA, and operational table synchronization (InsertData / DeleteData).
    /// </summary>
    public sealed class WorkflowEngineService
    {
        private readonly IT5BackendAdapter _backend;
        private readonly IAppSettingsService _settingsService;
        private readonly T5OrganizationAccessService _orgService;
        private readonly LeaveBalanceService _leaveBalanceService;

        public WorkflowEngineService(
            IT5BackendAdapter backend,
            IAppSettingsService settingsService,
            T5OrganizationAccessService orgService,
            LeaveBalanceService leaveBalanceService)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _orgService = orgService ?? throw new ArgumentNullException(nameof(orgService));
            _leaveBalanceService = leaveBalanceService ?? throw new ArgumentNullException(nameof(leaveBalanceService));
        }

        /// <summary>
        /// Validates whether a request for <paramref name="forDate"/> is permitted under Web.config time rules:
        /// 1. RequestsLockOADate (global cutoff date).
        /// 2. MaxRequestAgeDays (maximum age in days from today).
        /// 3. LastDayForRequest (cutoff day of the current Persian month for submitting/approving previous month requests).
        /// </summary>
        public RequestTimeValidationResult CheckRequestTime(DateTime forDate, DateTime? referenceNow = null)
        {
            TwfAppSettings settings = _settingsService.GetSettings();
            DateTime now = (referenceNow ?? DateTime.Now).Date;
            DateTime target = forDate.Date;

            DateTime? lockDate = settings.GetRequestsLockDate();
            if (lockDate.HasValue && target <= lockDate.Value.Date)
            {
                return RequestTimeValidationResult.Invalid(PersianString.MaxRequestTime);
            }

            if (settings.MaxRequestAgeDays > 0)
            {
                double ageDays = (now - target).TotalDays;
                if (ageDays > settings.MaxRequestAgeDays)
                {
                    return RequestTimeValidationResult.Invalid(
                        string.Format(PersianString.MaxRequestTime, settings.MaxRequestAgeDays));
                }
            }

            if (settings.LastDayForRequest > 0)
            {
                int nowYear = JalaliDateHelper.GetPersianYear(now);
                int nowMonth = JalaliDateHelper.GetPersianMonth(now);
                int nowDay = JalaliDateHelper.GetPersianDayOfMonth(now);

                int targetYear = JalaliDateHelper.GetPersianYear(target);
                int targetMonth = JalaliDateHelper.GetPersianMonth(target);

                bool isPreviousPersianMonth = (targetYear < nowYear) ||
                                              (targetYear == nowYear && targetMonth < nowMonth);

                if (isPreviousPersianMonth && nowDay > settings.LastDayForRequest)
                {
                    return RequestTimeValidationResult.Invalid(
                        string.Format(PersianString.LastValidDayOfMonth, settings.LastDayForRequest));
                }
            }

            return RequestTimeValidationResult.Valid();
        }

        /// <summary>
        /// Submits a new workflow request after validating time restrictions, daily/monthly record limits, and leave quotas.
        /// If the requester's T5 rule does not require workflow (or they are at the top of the chart with no approvers),
        /// automatically finalizes the request and inserts operational data.
        /// </summary>
        public WorkflowActionResult SubmitRequest(
            WorkflowRequestItemDto request,
            long senderBadgeNumber,
            DateTime? referenceNow = null)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            RequestTimeValidationResult timeCheck = CheckRequestTime(request.ForDate, referenceNow);
            if (!timeCheck.IsValid)
            {
                return WorkflowActionResult.Failure(timeCheck.ErrorMessage);
            }

            TwfAppSettings settings = _settingsService.GetSettings();
            ActiveAttendanceRuleDto rule = _backend.GetActiveRule(request.BadgeNumber, request.ForDate);
            if (rule != null && !rule.AllowWebRequest)
            {
                return WorkflowActionResult.Failure(PersianString.AccessDenied);
            }

            // Enforce Record request daily & monthly limits
            if (request.WorkflowType == TwfWorkflowType.Record)
            {
                int dailyLimit = settings.RequestRecordsDailyLimit > 0
                    ? settings.RequestRecordsDailyLimit
                    : (rule?.LimitRecReqInDay ?? -1);
                int monthlyLimit = settings.RequestRecordsMonthlyLimit > 0
                    ? settings.RequestRecordsMonthlyLimit
                    : (rule?.LimitRecReqInMonth ?? -1);

                IReadOnlyList<WorkflowRequestItemDto> existingReqs = _backend.GetRequestsByEmployee(request.BadgeNumber)
                    .Where(r => r.WorkflowType == TwfWorkflowType.Record && r.RequestState != TwfRequestState.Reject)
                    .ToList();

                if (dailyLimit > 0)
                {
                    int sameDayCount = existingReqs.Count(r => r.ForDate.Date == request.ForDate.Date);
                    if (sameDayCount >= dailyLimit)
                        return WorkflowActionResult.Failure(PersianString.RequestRecordsDailyLimit);
                }

                if (monthlyLimit > 0)
                {
                    DateTime monthStart = JalaliDateHelper.GetPersianMonthStart(request.ForDate);
                    DateTime monthEnd = JalaliDateHelper.GetPersianMonthEnd(request.ForDate);
                    int sameMonthCount = existingReqs.Count(r => r.ForDate.Date >= monthStart.Date && r.ForDate.Date <= monthEnd.Date);
                    if (sameMonthCount >= monthlyLimit)
                        return WorkflowActionResult.Failure(PersianString.RequestRecordsMonthlyLimit);
                }
            }

            // Enforce Leave quota & Limit1LeaveID
            if (request.WorkflowType == TwfWorkflowType.DailyLeave || request.WorkflowType == TwfWorkflowType.HourlyLeave)
            {
                var (allowed, quotaError) = _leaveBalanceService.ValidateLeaveRequestQuota(request);
                if (!allowed)
                {
                    return WorkflowActionResult.Failure(quotaError);
                }
            }

            request.SenderBadgeNumber = senderBadgeNumber;
            request.SenderTitleId = _backend.GetEmployeeCurrentTitleId(senderBadgeNumber);
            request.RequestDate = referenceNow ?? DateTime.Now;
            request.IsSuspend = true;
            request.RequestState = TwfRequestState.Waiting;

            long requestId = _backend.AddRequest(request);
            WorkflowRequestItemDto saved = _backend.GetRequestById(requestId);

            bool requireWorkflow = rule?.RequireWorkflow ?? true;
            IReadOnlyList<KeyValuePair<int, int>> remainingSteps = _backend.GetRemainingRequestAccepters(requestId);

            if (!requireWorkflow || remainingSteps.Count == 0)
            {
                // Auto-finalize when no workflow steps exist for this title
                _backend.InsertApprovedOperationalData(saved, senderBadgeNumber);
                saved.IsSuspend = false;
                saved.RequestState = TwfRequestState.Accept;
                _backend.UpdateRequest(saved);
                return WorkflowActionResult.Success(requestId, TwfRequestState.Accept, isFinalized: true, operationalRecordInserted: true);
            }

            return WorkflowActionResult.Success(requestId, TwfRequestState.Waiting, isFinalized: false, operationalRecordInserted: false);
        }

        /// <summary>
        /// Returns all pending requests currently awaiting approval at the step corresponding to
        /// <paramref name="approverBadgeNumber"/>'s own TitleID or any principal TitleID for which they are an active deputy.
        /// </summary>
        public IReadOnlyList<WorkflowRequestItemDto> GetPendingRequestsForApprover(long approverBadgeNumber)
        {
            IReadOnlyDictionary<int, bool> effectiveTitles = _orgService.GetEffectiveApproverTitleIds(approverBadgeNumber);
            if (approverBadgeNumber != 0 && effectiveTitles.Count == 0)
                return Array.Empty<WorkflowRequestItemDto>();

            var pendingList = new List<WorkflowRequestItemDto>();
            foreach (WorkflowRequestItemDto req in _backend.GetAllRequests())
            {
                if (!req.IsSuspend)
                    continue;

                IReadOnlyList<KeyValuePair<int, int>> remaining = _backend.GetRemainingRequestAccepters(req.RequestId);
                if (remaining == null || remaining.Count == 0)
                    continue;

                int nextAccepterTitleId = remaining[0].Value;
                if (approverBadgeNumber == 0)
                {
                    req.ForDeputy = false;
                    pendingList.Add(req);
                }
                else if (effectiveTitles.TryGetValue(nextAccepterTitleId, out bool isDeputy))
                {
                    req.ForDeputy = isDeputy;
                    if (isDeputy)
                    {
                        req.DeputyPrincipalTitleId = nextAccepterTitleId;
                        req.DeputyPrincipalTitleName = _backend.GetTitleNodeById(nextAccepterTitleId)?.TitleName ?? string.Empty;
                    }
                    pendingList.Add(req);
                }
            }

            return pendingList
                .OrderByDescending(r => r.RequestDate)
                .ThenByDescending(r => r.RequestId)
                .ToList();
        }

        /// <summary>
        /// Approves the current workflow step of <paramref name="requestId"/> on behalf of <paramref name="approverBadgeNumber"/>.
        /// - If additional workflow steps remain after this step (AcceptByID1), records the intermediate approval in RequestAcceptDA
        ///   and sets RequestState = FirstAccept while keeping IsSuspend = true.
        /// - If this is the final workflow step (AcceptByID2), records the approval in RequestAcceptDA, inserts the operational record
        ///   into T5 (DailyLeaves, HourlyLeaves, EditedValue, AllowValue, or Record), and sets IsSuspend = false, RequestState = Accept.
        /// </summary>
        public WorkflowActionResult ApproveRequest(
            long requestId,
            long approverBadgeNumber,
            DateTime? referenceNow = null)
        {
            WorkflowRequestItemDto req = _backend.GetRequestById(requestId);
            if (req == null)
                return WorkflowActionResult.Failure("درخواست مورد نظر یافت نشد.", requestId);

            if (!req.IsSuspend)
                return WorkflowActionResult.Failure("این درخواست قبلاً تعیین تکلیف شده است.", requestId);

            RequestTimeValidationResult timeCheck = CheckRequestTime(req.ForDate, referenceNow);
            if (!timeCheck.IsValid)
                return WorkflowActionResult.Failure(timeCheck.ErrorMessage, requestId);

            if (req.WorkflowType == TwfWorkflowType.DailyLeave || req.WorkflowType == TwfWorkflowType.HourlyLeave)
            {
                var (allowed, quotaError) = _leaveBalanceService.ValidateLeaveRequestQuota(req, ignoreExistingRequestId: true);
                if (!allowed)
                    return WorkflowActionResult.Failure(quotaError, requestId);
            }

            IReadOnlyList<KeyValuePair<int, int>> remainingBefore = _backend.GetRemainingRequestAccepters(requestId);
            if (remainingBefore == null || remainingBefore.Count == 0)
                return WorkflowActionResult.Failure("مرحله تاییدی برای این درخواست باقی نمانده است.", requestId);

            int requiredTitleId = remainingBefore[0].Value;
            IReadOnlyDictionary<int, bool> effectiveTitles = _orgService.GetEffectiveApproverTitleIds(approverBadgeNumber);

            bool isDeputy = false;
            int actingTitleId = requiredTitleId;

            if (approverBadgeNumber != 0)
            {
                if (!effectiveTitles.TryGetValue(requiredTitleId, out isDeputy))
                {
                    return WorkflowActionResult.Failure(PersianString.AccessDenied, requestId);
                }
            }

            _backend.AddRequestAcceptStep(new RequestAcceptStepDto
            {
                RequestId = requestId,
                AccepterBadgeNumber = approverBadgeNumber,
                AccepterTitleId = actingTitleId,
                AcceptDate = referenceNow ?? DateTime.Now,
                IsCancel = false,
                IsDeputy = isDeputy
            });

            IReadOnlyList<KeyValuePair<int, int>> remainingAfter = _backend.GetRemainingRequestAccepters(requestId);
            if (remainingAfter.Count == 0)
            {
                // Final step approved (AcceptByID2 -> InsertData)
                _backend.InsertApprovedOperationalData(req, approverBadgeNumber);
                req.IsSuspend = false;
                req.RequestState = TwfRequestState.Accept;
                _backend.UpdateRequest(req);

                return WorkflowActionResult.Success(
                    requestId: requestId,
                    newState: TwfRequestState.Accept,
                    isFinalized: true,
                    operationalRecordInserted: true,
                    message: PersianString.AcceptRequest);
            }
            else
            {
                // Intermediate step approved (AcceptByID1)
                req.IsSuspend = true;
                req.RequestState = TwfRequestState.FirstAccept;
                _backend.UpdateRequest(req);

                return WorkflowActionResult.Success(
                    requestId: requestId,
                    newState: TwfRequestState.FirstAccept,
                    isFinalized: false,
                    operationalRecordInserted: false,
                    message: PersianString.AcceptedTitle);
            }
        }

        /// <summary>
        /// Rejects (cancels) a pending request at the current workflow step (CancelByID).
        /// Records a RequestAccept step with IsCancel = true and finalizes the request with RequestState = Reject
        /// without inserting any operational data.
        /// </summary>
        public WorkflowActionResult RejectRequest(
            long requestId,
            long approverBadgeNumber,
            DateTime? referenceNow = null)
        {
            WorkflowRequestItemDto req = _backend.GetRequestById(requestId);
            if (req == null)
                return WorkflowActionResult.Failure("درخواست مورد نظر یافت نشد.", requestId);

            if (!req.IsSuspend)
                return WorkflowActionResult.Failure("این درخواست قبلاً تعیین تکلیف شده است.", requestId);

            IReadOnlyList<KeyValuePair<int, int>> remainingBefore = _backend.GetRemainingRequestAccepters(requestId);
            if (remainingBefore == null || remainingBefore.Count == 0)
                return WorkflowActionResult.Failure("مرحله تاییدی برای این درخواست یافت نشد.", requestId);

            int requiredTitleId = remainingBefore[0].Value;
            IReadOnlyDictionary<int, bool> effectiveTitles = _orgService.GetEffectiveApproverTitleIds(approverBadgeNumber);

            bool isDeputy = false;
            if (approverBadgeNumber != 0 && !effectiveTitles.TryGetValue(requiredTitleId, out isDeputy))
            {
                return WorkflowActionResult.Failure(PersianString.AccessDenied, requestId);
            }

            _backend.AddRequestAcceptStep(new RequestAcceptStepDto
            {
                RequestId = requestId,
                AccepterBadgeNumber = approverBadgeNumber,
                AccepterTitleId = requiredTitleId,
                AcceptDate = referenceNow ?? DateTime.Now,
                IsCancel = true,
                IsDeputy = isDeputy
            });

            req.IsSuspend = false;
            req.RequestState = TwfRequestState.Reject;
            _backend.UpdateRequest(req);

            return WorkflowActionResult.Success(
                requestId: requestId,
                newState: TwfRequestState.Reject,
                isFinalized: true,
                operationalRecordInserted: false);
        }

        /// <summary>
        /// Reverses (Undoes) a manager's previous approval or rejection (UndoController.Undo + DeleteData):
        /// 1. If the request was finalized &amp; approved, deletes the operational record from T5 (DailyLeaves, HourlyLeaves, EditedValue, AllowValue, Record).
        /// 2. Removes the manager's RequestAccept entry.
        /// 3. Restores the request to pending state (IsSuspend = true).
        /// </summary>
        public WorkflowActionResult UndoManagerDecision(long requestId, long managerBadgeNumber)
        {
            WorkflowRequestItemDto req = _backend.GetRequestById(requestId);
            if (req == null)
                return WorkflowActionResult.Failure("درخواست مورد نظر یافت نشد.", requestId);

            TwfAppSettings settings = _settingsService.GetSettings();
            DateTime? lockDate = settings.GetRequestsLockDate();
            if (lockDate.HasValue && req.ForDate.Date <= lockDate.Value.Date)
                return WorkflowActionResult.Failure(PersianString.MaxRequestTime, requestId);

            IReadOnlyList<RequestAcceptStepDto> history = _backend.GetRequestAcceptHistory(requestId);
            if (managerBadgeNumber != 0 && !history.Any(h => h.AccepterBadgeNumber == managerBadgeNumber))
                return WorkflowActionResult.Failure(PersianString.AccessDenied, requestId);

            if (!req.IsSuspend && req.RequestState == TwfRequestState.Accept)
            {
                _backend.DeleteApprovedOperationalData(req, managerBadgeNumber);
            }

            _backend.DeleteRequestAcceptSteps(requestId, managerBadgeNumber == 0 ? (long?)null : managerBadgeNumber);

            IReadOnlyList<RequestAcceptStepDto> remainingHistory = _backend.GetRequestAcceptHistory(requestId);
            req.IsSuspend = true;
            req.RequestState = remainingHistory.Any(h => !h.IsCancel)
                ? TwfRequestState.FirstAccept
                : TwfRequestState.Waiting;

            const string undoSuffix = " (برگشت از تایید)";
            if (req.Descriptions == null || !req.Descriptions.EndsWith(undoSuffix, StringComparison.Ordinal))
            {
                req.Descriptions = (req.Descriptions ?? string.Empty) + undoSuffix;
            }

            _backend.UpdateRequest(req);

            return WorkflowActionResult.Success(
                requestId: requestId,
                newState: req.RequestState,
                isFinalized: false,
                operationalRecordInserted: false);
        }

        /// <summary>
        /// Allows a user to delete their own pending request before final approval (EditRequestController.Delete).
        /// </summary>
        public WorkflowActionResult DeletePendingRequest(long requestId, long actorBadgeNumber)
        {
            WorkflowRequestItemDto req = _backend.GetRequestById(requestId);
            if (req == null)
                return WorkflowActionResult.Failure("درخواست مورد نظر یافت نشد.", requestId);

            if (!req.IsSuspend)
                return WorkflowActionResult.Failure("درخواست‌های نهایی‌شده قابل حذف توسط کاربر نیستند.", requestId);

            if (actorBadgeNumber != 0 &&
                req.BadgeNumber != actorBadgeNumber &&
                req.SenderBadgeNumber != actorBadgeNumber)
            {
                return WorkflowActionResult.Failure(PersianString.AccessDenied, requestId);
            }

            _backend.DeleteRequest(requestId);
            return WorkflowActionResult.Success(requestId, TwfRequestState.UnKnown, isFinalized: true, operationalRecordInserted: false);
        }
    }
}
