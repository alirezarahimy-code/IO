using System;
using System.Collections.Generic;
using System.Linq;
using TWF.Domain.Interfaces;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;

namespace TWF.Application.Leaves
{
    /// <summary>
    /// Service for calculating annual and monthly leave balances (RemainLeave) and enforcing leave quotas.
    /// Replaces MyTime3.Controllers.RemainLeaveController.GetRemain and fixes BUG-06
    /// (by using exact PersianCalendar year and month boundaries instead of DayOfYear == 81).
    /// </summary>
    public sealed class LeaveBalanceService
    {
        private readonly IT5BackendAdapter _backend;
        private readonly IAppSettingsService _settingsService;

        public LeaveBalanceService(IT5BackendAdapter backend, IAppSettingsService settingsService)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        /// <summary>
        /// Calculates the complete leave balance summary for an employee and leave type as of <paramref name="asOfDate"/>.
        /// Properly accounts for DecreaseFromLeaveID (linked leave types), GetInHolidays, EachXHoursADay,
        /// and pending (IsSuspend) requests in the same Solar Hijri year.
        /// </summary>
        public LeaveBalanceSummaryDto GetLeaveBalance(long badgeNumber, int leaveTypeId, DateTime asOfDate)
        {
            TwfAppSettings settings = _settingsService.GetSettings();
            IReadOnlyList<LeaveTypeDefinitionDto> allLeaveTypes = _backend.GetAllLeaveTypes();
            var leaveTypeMap = allLeaveTypes.ToDictionary(lt => lt.LeaveTypeId, lt => lt);

            if (!leaveTypeMap.TryGetValue(leaveTypeId, out LeaveTypeDefinitionDto requestedLeaveType))
            {
                requestedLeaveType = new LeaveTypeDefinitionDto
                {
                    LeaveTypeId = leaveTypeId,
                    LeaveName = "مرخصی استحقاقی",
                    EachXHoursADayMinutes = settings.HoursOfDays > 24 ? settings.HoursOfDays : settings.HoursOfDays * 60
                };
            }

            // If this leave type deducts from a parent leave type (DecreaseFromLeaveID), evaluate against the root quota type
            int quotaLeaveTypeId = requestedLeaveType.DecreaseFromLeaveId ?? requestedLeaveType.LeaveTypeId;
            if (!leaveTypeMap.TryGetValue(quotaLeaveTypeId, out LeaveTypeDefinitionDto quotaLeaveType))
            {
                quotaLeaveType = requestedLeaveType;
            }

            // Identify all leave type IDs that deduct from this quota pool
            var linkedLeaveTypeIds = new HashSet<int> { quotaLeaveTypeId };
            foreach (LeaveTypeDefinitionDto lt in allLeaveTypes)
            {
                if (lt.DecreaseFromLeaveId.HasValue && lt.DecreaseFromLeaveId.Value == quotaLeaveTypeId)
                {
                    linkedLeaveTypeIds.Add(lt.LeaveTypeId);
                }
            }

            int dayMinutes = quotaLeaveType.EachXHoursADayMinutes > 0
                ? quotaLeaveType.EachXHoursADayMinutes
                : (settings.HoursOfDays > 24 ? settings.HoursOfDays : settings.HoursOfDays * 60);

            // BUG-06 Fix: Exact Solar Hijri year start & end via PersianCalendar
            int persianYear = JalaliDateHelper.GetPersianYear(asOfDate);
            DateTime yearStart = JalaliDateHelper.GetPersianYearStart(persianYear);
            DateTime yearEnd = JalaliDateHelper.GetPersianYearEnd(persianYear);
            DateTime effectiveEnd = asOfDate.Date > yearEnd.Date ? yearEnd : asOfDate;

            int annualEntitlementMinutes = _backend.GetAnnualLeaveEntitlementMinutes(badgeNumber, quotaLeaveTypeId, persianYear);

            // 1. Approved Daily Leaves in [yearStart .. effectiveEnd]
            IReadOnlyList<DailyLeaveRecordDto> dailyLeaves = _backend.GetApprovedDailyLeaves(badgeNumber, yearStart, effectiveEnd);
            int dailyTakenDays = 0;
            foreach (DailyLeaveRecordDto dl in dailyLeaves)
            {
                if (!linkedLeaveTypeIds.Contains(dl.LeaveTypeId))
                    continue;

                leaveTypeMap.TryGetValue(dl.LeaveTypeId, out LeaveTypeDefinitionDto dlType);
                bool countInHolidays = dlType?.GetInHolidays ?? quotaLeaveType.GetInHolidays;
                if (dl.IsHoliday && !countInHolidays)
                    continue;

                dailyTakenDays++;
            }

            int dailyTakenMinutes = dailyTakenDays * dayMinutes;

            // 2. Approved Hourly Leaves in [yearStart .. effectiveEnd]
            IReadOnlyList<HourlyLeaveRecordDto> hourlyLeaves = _backend.GetApprovedHourlyLeaves(badgeNumber, yearStart, effectiveEnd);
            int hourlyTakenMinutes = 0;
            foreach (HourlyLeaveRecordDto hl in hourlyLeaves)
            {
                if (!linkedLeaveTypeIds.Contains(hl.LeaveTypeId))
                    continue;

                hourlyTakenMinutes += hl.ValidDurationMinutes;
            }

            // 3. Pending (IsSuspend == true) Leave Requests in the same Solar Hijri year
            IReadOnlyList<WorkflowRequestItemDto> userRequests = _backend.GetRequestsByEmployee(badgeNumber);
            int pendingMinutes = 0;
            foreach (WorkflowRequestItemDto req in userRequests)
            {
                if (!req.IsSuspend)
                    continue;
                if (req.WorkflowType != TwfWorkflowType.DailyLeave && req.WorkflowType != TwfWorkflowType.HourlyLeave)
                    continue;
                if (!linkedLeaveTypeIds.Contains(req.LeaveTypeId))
                    continue;
                if (!JalaliDateHelper.IsSamePersianYear(req.ForDate, asOfDate))
                    continue;

                pendingMinutes += CalculateRequestLeaveDurationMinutes(req, dayMinutes);
            }

            int totalTakenMinutes = dailyTakenMinutes + hourlyTakenMinutes;
            int remainingMinutes = annualEntitlementMinutes - totalTakenMinutes;

            EmployeeSummaryDto emp = _backend.GetEmployeeByBadge(badgeNumber);

            return new LeaveBalanceSummaryDto
            {
                BadgeNumber = badgeNumber,
                EmployeeName = emp?.FullName ?? string.Empty,
                LeaveTypeId = quotaLeaveTypeId,
                LeaveTypeName = quotaLeaveType.LeaveName,
                PersianYear = persianYear,
                YearStartDate = yearStart,
                AsOfDate = effectiveEnd,
                EachDayHoursMinutes = dayMinutes,
                AnnualEntitlementMinutes = annualEntitlementMinutes,
                DailyTakenDays = dailyTakenDays,
                DailyTakenMinutes = dailyTakenMinutes,
                HourlyTakenMinutes = hourlyTakenMinutes,
                TotalTakenMinutes = totalTakenMinutes,
                PendingRequestedMinutes = pendingMinutes,
                RemainingMinutes = remainingMinutes,
                MaxMinusMinutes = quotaLeaveType.MaxMinusMinutes
            };
        }

        /// <summary>
        /// Validates whether a leave request (Daily or Hourly) complies with:
        /// 1. Same Solar Hijri year boundary (start and end dates of a multi-day leave must be in the same Persian year).
        /// 2. Limit1LeaveId (at most 1 day per Persian month for the restricted leave type).
        /// 3. Remaining annual leave balance + MaxMinus allowance.
        /// </summary>
        public (bool IsAllowed, string ErrorMessage) ValidateLeaveRequestQuota(
            WorkflowRequestItemDto request,
            bool ignoreExistingRequestId = false)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (request.WorkflowType != TwfWorkflowType.DailyLeave && request.WorkflowType != TwfWorkflowType.HourlyLeave)
                return (true, string.Empty);

            TwfAppSettings settings = _settingsService.GetSettings();

            // Check 1: For multi-day daily leaves, ForDate and ToTime must be in the same Solar Hijri year (BUG-06 Fix)
            if (request.IsDaily && request.ToTime.Date > request.ForDate.Date)
            {
                if (!JalaliDateHelper.IsSamePersianYear(request.ForDate, request.ToTime))
                {
                    return (false, "بازه مرخصی روزانه نمی‌تواند در دو سال شمسی مختلف قرار گیرد. لطفاً برای هر سال درخواست جداگانه ثبت نمایید.");
                }
            }

            // Check 2: Limit1LeaveID (at most 1 day per Persian month)
            if (settings.Limit1LeaveId > 0 && request.LeaveTypeId == settings.Limit1LeaveId)
            {
                DateTime monthStart = JalaliDateHelper.GetPersianMonthStart(request.ForDate);
                DateTime monthEnd = JalaliDateHelper.GetPersianMonthEnd(request.ForDate);

                int requestDays = request.IsDaily
                    ? Math.Max(1, (int)(request.ToTime.Date - request.ForDate.Date).TotalDays + 1)
                    : 1;

                if (requestDays > 1)
                {
                    return (false, TWF.Infrastructure.Localization.PersianString.DailyLeaveLimit);
                }

                IReadOnlyList<DailyLeaveRecordDto> monthlyApproved =
                    _backend.GetApprovedDailyLeaves(request.BadgeNumber, monthStart, monthEnd);
                if (monthlyApproved.Any(dl => dl.LeaveTypeId == settings.Limit1LeaveId))
                {
                    return (false, TWF.Infrastructure.Localization.PersianString.DailyLeaveLimit);
                }
            }

            // Check 3: Annual leave quota + MaxMinus
            DateTime yearEnd = JalaliDateHelper.GetPersianYearEnd(request.ForDate);
            LeaveBalanceSummaryDto balance = GetLeaveBalance(request.BadgeNumber, request.LeaveTypeId, yearEnd);
            int requestMinutes = CalculateRequestLeaveDurationMinutes(request, balance.EachDayHoursMinutes);

            int alreadyCountedPending = 0;
            if (ignoreExistingRequestId && request.RequestId > 0)
            {
                alreadyCountedPending = requestMinutes;
            }

            int projectedRemaining = balance.RemainingMinutes - (balance.PendingRequestedMinutes - alreadyCountedPending) - requestMinutes;
            int minAllowedRemaining = -Math.Abs(balance.MaxMinusMinutes);

            if (projectedRemaining < minAllowedRemaining)
            {
                return (false, TWF.Infrastructure.Localization.PersianString.RemainLeaveError);
            }

            return (true, string.Empty);
        }

        public LeaveBalanceSummaryDto CalculateLeaveBalance(long badgeNumber, int leaveTypeId, DateTime asOfDate)
        {
            return GetLeaveBalance(badgeNumber, leaveTypeId, asOfDate);
        }

        public bool ValidateDailyLeaveBalance(
            long badgeNumber,
            int leaveTypeId,
            DateTime fromDate,
            DateTime toDate,
            out string errorMessage,
            long? excludeRequestId = null)
        {
            var req = new WorkflowRequestItemDto
            {
                RequestId = excludeRequestId ?? 0,
                BadgeNumber = badgeNumber,
                LeaveTypeId = leaveTypeId,
                ForDate = fromDate.Date,
                ToTime = toDate.Date,
                IsDaily = true,
                WorkflowType = TwfWorkflowType.DailyLeave
            };
            var res = ValidateLeaveRequestQuota(req, ignoreExistingRequestId: excludeRequestId.HasValue);
            errorMessage = res.ErrorMessage;
            return res.IsAllowed;
        }

        public bool ValidateHourlyLeaveBalance(
            long badgeNumber,
            int leaveTypeId,
            DateTime forDate,
            DateTime fromTime,
            DateTime toTime,
            out string errorMessage,
            long? excludeRequestId = null)
        {
            var req = new WorkflowRequestItemDto
            {
                RequestId = excludeRequestId ?? 0,
                BadgeNumber = badgeNumber,
                LeaveTypeId = leaveTypeId,
                ForDate = forDate.Date,
                FromTime = fromTime,
                ToTime = toTime,
                IsDaily = false,
                WorkflowType = TwfWorkflowType.HourlyLeave
            };
            var res = ValidateLeaveRequestQuota(req, ignoreExistingRequestId: excludeRequestId.HasValue);
            errorMessage = res.ErrorMessage;
            return res.IsAllowed;
        }

        public static int CalculateRequestLeaveDurationMinutes(WorkflowRequestItemDto request, int dayMinutes)
        {
            if (request.IsDaily || request.WorkflowType == TwfWorkflowType.DailyLeave)
            {
                DateTime start = request.ForDate.Date;
                DateTime end = request.ToTime.Date >= start ? request.ToTime.Date : start;
                int days = (int)(end - start).TotalDays + 1;
                return Math.Max(1, days) * dayMinutes;
            }
            else
            {
                DateTime from = request.ForDate.Date.Add(request.FromTime.TimeOfDay);
                DateTime to = request.ForDate.Date.Add(request.ToTime.TimeOfDay);
                if (request.ToTime.TimeOfDay < request.FromTime.TimeOfDay)
                {
                    to = to.AddDays(1);
                }
                return Math.Max(0, (int)Math.Round((to - from).TotalMinutes));
            }
        }
    }
}
