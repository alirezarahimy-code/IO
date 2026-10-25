using System;
using System.Collections.Generic;
using System.Linq;
using TWF.Domain.Interfaces;
using TWF.Domain.Models;

namespace TWF.Application.Organization
{
    /// <summary>
    /// Unified service for T5 organizational hierarchy (TitlesChart), workflow-aware subordinate discovery (WorkflowDA),
    /// two-layer deputy delegation (DeputyDA + PersonnelDeputyDA), Guard/Observer access (TitlesChart.Observer),
    /// and title-based report permissions (ReportAccessDA).
    /// </summary>
    public sealed class T5OrganizationAccessService
    {
        private readonly IT5BackendAdapter _backend;

        public T5OrganizationAccessService(IT5BackendAdapter backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        /// <summary>
        /// Determines whether the user is a top-level manager in the organizational chart (or system admin),
        /// which grants access to organization-wide views such as /ShowUsersRequest (matching HomeController.IsAdmin).
        /// </summary>
        public bool IsTopLevelManager(long badgeNumber)
        {
            if (badgeNumber == 0)
                return true;

            int titleId = _backend.GetEmployeeCurrentTitleId(badgeNumber);
            if (titleId <= 0)
                return false;

            OrgTitleNodeDto node = _backend.GetTitleNodeById(titleId);
            if (node == null)
                return false;

            if (node.UpperTitleId <= 0 || node.UpperTitleId == node.TitleId)
                return true;

            OrgTitleNodeDto parent = _backend.GetTitleNodeById(node.UpperTitleId);
            return parent == null;
        }

        /// <summary>
        /// Determines whether the user holds a Guard/Observer title (TitlesChart.Observer == true)
        /// for real-time PersonnelStatus attendance monitoring.
        /// </summary>
        public bool IsGuardObserver(long badgeNumber)
        {
            if (badgeNumber == 0)
                return true;

            int titleId = _backend.GetEmployeeCurrentTitleId(badgeNumber);
            if (titleId <= 0)
                return false;

            OrgTitleNodeDto node = _backend.GetTitleNodeById(titleId);
            return node != null && node.IsObserver;
        }

        /// <summary>
        /// Resolves all subordinate TitleIDs for a manager's TitleID by combining:
        /// 1. Titles where T5 WorkflowDA explicitly includes <paramref name="managerTitleId"/> as an AccepterTitleID.
        /// 2. Titles that have NO custom WorkflowDA rules and are descendants of <paramref name="managerTitleId"/> in TitlesChart.
        /// Includes cycle protection for malformed TitlesChart trees.
        /// </summary>
        public IReadOnlyList<int> GetSubordinateTitleIdsForTitle(int managerTitleId)
        {
            if (managerTitleId <= 0)
                return Array.Empty<int>();

            IReadOnlyList<OrgTitleNodeDto> allTitles = _backend.GetAllTitleNodes();
            var titleMap = allTitles.ToDictionary(t => t.TitleId, t => t);
            var resultTitleIds = new HashSet<int>();

            foreach (OrgTitleNodeDto tc in allTitles)
            {
                if (tc.TitleId == managerTitleId)
                    continue;

                IReadOnlyList<WorkflowStepConfigDto> wfSteps = _backend.GetWorkflowStepsForTitle(tc.TitleId);
                if (wfSteps != null && wfSteps.Count > 0)
                {
                    if (wfSteps.Any(w => w.AccepterTitleId == managerTitleId))
                    {
                        resultTitleIds.Add(tc.TitleId);
                    }
                }
                else
                {
                    if (IsDescendantInTitleChart(tc.TitleId, managerTitleId, titleMap))
                    {
                        resultTitleIds.Add(tc.TitleId);
                    }
                }
            }

            return resultTitleIds.OrderBy(id => id).ToList();
        }

        /// <summary>
        /// Resolves all employees accessible to <paramref name="managerBadgeNumber"/> across:
        /// 1. Direct/Workflow subordinate titles of the manager's own TitleID.
        /// 2. Subordinate titles of any principal manager for whom <paramref name="managerBadgeNumber"/> is an active deputy (PersonnelDeputyDA.IsDeputy).
        /// 3. The manager's own employee record (when <paramref name="includeSelf"/> is true).
        /// </summary>
        public IReadOnlyList<EmployeeSummaryDto> GetAccessiblePersonnel(
            long managerBadgeNumber,
            bool includeSelf = true,
            bool includeDeputySubordinates = true)
        {
            if (managerBadgeNumber == 0)
            {
                IReadOnlyList<OrgTitleNodeDto> allTitles = _backend.GetAllTitleNodes();
                return _backend.GetEmployeesByTitleIds(allTitles.Select(t => t.TitleId));
            }

            var resultByBadge = new Dictionary<long, EmployeeSummaryDto>();

            if (includeSelf)
            {
                EmployeeSummaryDto self = _backend.GetEmployeeByBadge(managerBadgeNumber);
                if (self != null)
                {
                    resultByBadge[self.BadgeNumber] = self;
                }
            }

            int managerTitleId = _backend.GetEmployeeCurrentTitleId(managerBadgeNumber);
            if (managerTitleId > 0)
            {
                IReadOnlyList<int> directSubTitleIds = GetSubordinateTitleIdsForTitle(managerTitleId);
                foreach (EmployeeSummaryDto emp in _backend.GetEmployeesByTitleIds(directSubTitleIds))
                {
                    if (!resultByBadge.ContainsKey(emp.BadgeNumber))
                    {
                        resultByBadge[emp.BadgeNumber] = emp;
                    }
                }
            }

            if (includeDeputySubordinates)
            {
                IReadOnlyList<long> principalBadges = _backend.GetPrincipalBadgesForDeputy(managerBadgeNumber);
                foreach (long principalBadge in principalBadges)
                {
                    int principalTitleId = _backend.GetEmployeeCurrentTitleId(principalBadge);
                    if (principalTitleId <= 0)
                        continue;

                    OrgTitleNodeDto principalTitleNode = _backend.GetTitleNodeById(principalTitleId);
                    IReadOnlyList<int> deputySubTitleIds = GetSubordinateTitleIdsForTitle(principalTitleId);

                    foreach (EmployeeSummaryDto emp in _backend.GetEmployeesByTitleIds(deputySubTitleIds))
                    {
                        if (!resultByBadge.ContainsKey(emp.BadgeNumber))
                        {
                            emp.ViaDeputyForBadgeNumber = principalBadge;
                            emp.ViaDeputyForTitleName = principalTitleNode?.TitleName ?? string.Empty;
                            resultByBadge[emp.BadgeNumber] = emp;
                        }
                    }
                }
            }

            return resultByBadge.Values
                .OrderBy(e => e.LastName)
                .ThenBy(e => e.FirstName)
                .ToList();
        }

        /// <summary>
        /// Resolves all TitleIDs for which <paramref name="approverBadgeNumber"/> is currently authorized to approve requests
        /// (their own TitleID + the TitleIDs of all principals who delegated active deputy authority to them).
        /// </summary>
        public IReadOnlyDictionary<int, bool> GetEffectiveApproverTitleIds(long approverBadgeNumber)
        {
            // Key: TitleID, Value: isDeputy
            var map = new Dictionary<int, bool>();
            int ownTitleId = _backend.GetEmployeeCurrentTitleId(approverBadgeNumber);
            if (ownTitleId > 0)
            {
                map[ownTitleId] = false;
            }

            IReadOnlyList<long> principalBadges = _backend.GetPrincipalBadgesForDeputy(approverBadgeNumber);
            foreach (long principalBadge in principalBadges)
            {
                int principalTitleId = _backend.GetEmployeeCurrentTitleId(principalBadge);
                if (principalTitleId > 0 && !map.ContainsKey(principalTitleId))
                {
                    map[principalTitleId] = true;
                }
            }

            return map;
        }

        /// <summary>
        /// Two-Layer Deputy System (Layer 1: T5 Deputy table -> Layer 2: Candidate employees):
        /// Returns all employees eligible to be selected as a deputy by <paramref name="principalBadgeNumber"/>.
        /// </summary>
        public IReadOnlyList<EmployeeSummaryDto> GetCandidateDeputyEmployees(long principalBadgeNumber)
        {
            int principalTitleId = _backend.GetEmployeeCurrentTitleId(principalBadgeNumber);
            if (principalTitleId <= 0)
                return Array.Empty<EmployeeSummaryDto>();

            IReadOnlyList<int> allowedDeputyTitleIds = _backend.GetAllowedDeputyTitleIds(principalTitleId);
            if (allowedDeputyTitleIds == null || allowedDeputyTitleIds.Count == 0)
            {
                // Fallback when no explicit Deputy rows exist in T5: allow direct/workflow subordinate titles
                allowedDeputyTitleIds = GetSubordinateTitleIdsForTitle(principalTitleId);
            }

            return _backend.GetEmployeesByTitleIds(allowedDeputyTitleIds)
                .Where(e => e.BadgeNumber != principalBadgeNumber)
                .ToList();
        }

        /// <summary>
        /// Assigns an active deputy for <paramref name="principalBadgeNumber"/> after verifying Layer-1 title eligibility.
        /// </summary>
        public DeputyAssignmentDto AssignDeputy(long principalBadgeNumber, long deputyBadgeNumber)
        {
            if (principalBadgeNumber == deputyBadgeNumber)
                throw new InvalidOperationException("مدیر نمی‌تواند خودش را به عنوان جانشین انتخاب کند.");

            IReadOnlyList<EmployeeSummaryDto> candidates = GetCandidateDeputyEmployees(principalBadgeNumber);
            if (!candidates.Any(c => c.BadgeNumber == deputyBadgeNumber))
                throw new InvalidOperationException("کاربر انتخاب‌شده در لیست سمت‌های مجاز جانشینی (T5 Deputy) این سمت قرار ندارد.");

            return _backend.AssignPersonnelDeputy(principalBadgeNumber, deputyBadgeNumber);
        }

        public bool RemoveDeputy(long principalBadgeNumber, long deputyBadgeNumber)
        {
            return _backend.RemovePersonnelDeputy(principalBadgeNumber, deputyBadgeNumber);
        }

        public IReadOnlyList<DeputyAssignmentDto> GetActiveDeputiesForPrincipal(long principalBadgeNumber)
        {
            return _backend.GetDeputyAssignmentsForPrincipal(principalBadgeNumber);
        }

        /// <summary>
        /// Returns personnel accessible to a Guard/Observer user (TitlesChart.Observer).
        /// </summary>
        public IReadOnlyList<EmployeeSummaryDto> GetGuardAccessiblePersonnel(long observerBadgeNumber)
        {
            int titleId = _backend.GetEmployeeCurrentTitleId(observerBadgeNumber);
            if (observerBadgeNumber == 0)
            {
                IReadOnlyList<OrgTitleNodeDto> allTitles = _backend.GetAllTitleNodes();
                return _backend.GetEmployeesByTitleIds(allTitles.Select(t => t.TitleId));
            }
            return _backend.GetGuardAccessiblePersonnel(observerBadgeNumber, titleId);
        }

        /// <summary>
        /// Returns all T5 reports that <paramref name="badgeNumber"/> is authorized to view based on ReportAccessDA.
        /// </summary>
        public IReadOnlyList<ReportDefinitionDto> GetAccessibleReports(long badgeNumber)
        {
            IReadOnlyList<ReportDefinitionDto> allReports = _backend.GetAllReports();
            if (badgeNumber == 0)
                return allReports;

            int userTitleId = _backend.GetEmployeeCurrentTitleId(badgeNumber);
            if (userTitleId <= 0)
                return Array.Empty<ReportDefinitionDto>();

            return allReports
                .Where(r => r.AllowedTitleIds != null && r.AllowedTitleIds.Contains(userTitleId))
                .ToList();
        }

        /// <summary>
        /// Resolves the active T5 attendance rule for an employee on a specific date.
        /// </summary>
        public ActiveAttendanceRuleDto GetActiveRule(long badgeNumber, DateTime targetDate)
        {
            return _backend.GetActiveRule(badgeNumber, targetDate);
        }

        private static bool IsDescendantInTitleChart(
            int candidateTitleId,
            int ancestorTitleId,
            IReadOnlyDictionary<int, OrgTitleNodeDto> titleMap)
        {
            var visited = new HashSet<int>();
            int currentId = candidateTitleId;

            while (currentId > 0 && visited.Add(currentId))
            {
                if (!titleMap.TryGetValue(currentId, out OrgTitleNodeDto node))
                    return false;

                if (node.UpperTitleId == ancestorTitleId)
                    return true;

                if (node.UpperTitleId <= 0 || node.UpperTitleId == currentId)
                    return false;

                currentId = node.UpperTitleId;
            }

            return false;
        }
    }
}
