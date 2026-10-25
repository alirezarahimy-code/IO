using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TWF.Domain.Interfaces;
using TWF.Domain.Models;

namespace TWF.Infrastructure.Adapters
{
    /// <summary>
    /// Thread-safe, high-fidelity implementation of <see cref="IT5BackendAdapter"/>
    /// modeling the exact domain behavior of Personnel.dll (TitlesChart, UserInfos, UserInfoTitles)
    /// and T5V3.*.dll (WorkflowDA, DeputyDA, PersonnelDeputyDA, RequestDA, RequestAcceptDA,
    /// DailyLeaveDA, HourlyLeaveDA, EditedValueDA, AllowValueDA, RecordDA, LeaveTypeDA, ReportAccessDA).
    /// </summary>
    public sealed class InMemoryT5BackendAdapter : IT5BackendAdapter
    {
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        private string _adminPassword = "admin";
        private readonly Dictionary<long, (EmployeeSummaryDto Profile, string Password)> _employees =
            new Dictionary<long, (EmployeeSummaryDto, string)>();
        private readonly Dictionary<int, OrgTitleNodeDto> _titles =
            new Dictionary<int, OrgTitleNodeDto>();
        private readonly List<WorkflowStepConfigDto> _workflows =
            new List<WorkflowStepConfigDto>();
        private readonly Dictionary<int, HashSet<int>> _allowedDeputyTitlesByPrincipalTitle =
            new Dictionary<int, HashSet<int>>();
        private readonly List<DeputyAssignmentDto> _personnelDeputies =
            new List<DeputyAssignmentDto>();
        private readonly List<ReportDefinitionDto> _reports =
            new List<ReportDefinitionDto>();
        private readonly Dictionary<long, ActiveAttendanceRuleDto> _userRules =
            new Dictionary<long, ActiveAttendanceRuleDto>();
        private ActiveAttendanceRuleDto _defaultRule = new ActiveAttendanceRuleDto
        {
            RuleId = 1,
            RuleName = "آیین‌نامه عمومی تردد",
            AllowWebRequest = true,
            RequireWorkflow = true,
            AllowMobileApp = true,
            EachDayHoursMinutes = 440
        };

        private readonly Dictionary<int, LeaveTypeDefinitionDto> _leaveTypes =
            new Dictionary<int, LeaveTypeDefinitionDto>();
        private readonly Dictionary<(long Badge, int LeaveTypeId, int Year), int> _annualEntitlements =
            new Dictionary<(long, int, int), int>();
        private readonly List<DailyLeaveRecordDto> _dailyLeaves =
            new List<DailyLeaveRecordDto>();
        private readonly List<HourlyLeaveRecordDto> _hourlyLeaves =
            new List<HourlyLeaveRecordDto>();

        private readonly Dictionary<long, WorkflowRequestItemDto> _requests =
            new Dictionary<long, WorkflowRequestItemDto>();
        private readonly List<RequestAcceptStepDto> _requestAccepts =
            new List<RequestAcceptStepDto>();
        private readonly List<(long Badge, TwfWorkflowType Type, DateTime ForDate, long RequestId)> _operationalEntries =
            new List<(long, TwfWorkflowType, DateTime, long)>();
        private readonly Dictionary<(long Badge, DateTime FromDate, DateTime ToDate), int> _constantOvertime =
            new Dictionary<(long, DateTime, DateTime), int>();

        private long _nextRequestId = 1000;
        private int _nextAcceptId = 1;
        private int _nextDeputyId = 1;

        // --- Seed Helpers ---
        public void SetAdminPassword(string password)
        {
            _lock.EnterWriteLock();
            try { _adminPassword = password ?? string.Empty; }
            finally { _lock.ExitWriteLock(); }
        }

        public void SeedTitleNode(OrgTitleNodeDto node)
        {
            _lock.EnterWriteLock();
            try { _titles[node.TitleId] = node; }
            finally { _lock.ExitWriteLock(); }
        }

        public void SeedEmployee(EmployeeSummaryDto employee, string password = "123")
        {
            _lock.EnterWriteLock();
            try { _employees[employee.BadgeNumber] = (employee, password ?? string.Empty); }
            finally { _lock.ExitWriteLock(); }
        }

        public void SeedWorkflowStep(WorkflowStepConfigDto step)
        {
            _lock.EnterWriteLock();
            try { _workflows.Add(step); }
            finally { _lock.ExitWriteLock(); }
        }

        public void SeedAllowedDeputyTitle(int principalTitleId, int allowedDeputyTitleId)
        {
            _lock.EnterWriteLock();
            try
            {
                if (!_allowedDeputyTitlesByPrincipalTitle.TryGetValue(principalTitleId, out HashSet<int> set))
                {
                    set = new HashSet<int>();
                    _allowedDeputyTitlesByPrincipalTitle[principalTitleId] = set;
                }
                set.Add(allowedDeputyTitleId);
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void SeedReport(ReportDefinitionDto report)
        {
            _lock.EnterWriteLock();
            try { _reports.Add(report); }
            finally { _lock.ExitWriteLock(); }
        }

        public void SeedLeaveType(LeaveTypeDefinitionDto leaveType)
        {
            _lock.EnterWriteLock();
            try { _leaveTypes[leaveType.LeaveTypeId] = leaveType; }
            finally { _lock.ExitWriteLock(); }
        }

        public void SeedAnnualLeaveEntitlement(long badgeNumber, int leaveTypeId, int persianYear, int entitlementMinutes)
        {
            _lock.EnterWriteLock();
            try { _annualEntitlements[(badgeNumber, leaveTypeId, persianYear)] = entitlementMinutes; }
            finally { _lock.ExitWriteLock(); }
        }

        public void SeedApprovedDailyLeave(DailyLeaveRecordDto dailyLeave)
        {
            _lock.EnterWriteLock();
            try { _dailyLeaves.Add(dailyLeave); }
            finally { _lock.ExitWriteLock(); }
        }

        public void SeedApprovedHourlyLeave(HourlyLeaveRecordDto hourlyLeave)
        {
            _lock.EnterWriteLock();
            try { _hourlyLeaves.Add(hourlyLeave); }
            finally { _lock.ExitWriteLock(); }
        }

        public void SeedUserRule(long badgeNumber, ActiveAttendanceRuleDto rule)
        {
            _lock.EnterWriteLock();
            try { _userRules[badgeNumber] = rule; }
            finally { _lock.ExitWriteLock(); }
        }

        // --- Authentication & Password Management ---
        public bool VerifyAdminPassword(string password)
        {
            _lock.EnterReadLock();
            try { return string.Equals(_adminPassword, password ?? string.Empty, StringComparison.Ordinal); }
            finally { _lock.ExitReadLock(); }
        }

        public long? AuthenticateEmployee(string userName, string password)
        {
            _lock.EnterReadLock();
            try
            {
                foreach (var kv in _employees.Values)
                {
                    if ((string.Equals(kv.Profile.UserName, userName, StringComparison.OrdinalIgnoreCase) ||
                         kv.Profile.BadgeNumber.ToString() == userName) &&
                        string.Equals(kv.Password, password ?? string.Empty, StringComparison.Ordinal))
                    {
                        return kv.Profile.BadgeNumber;
                    }
                }
                return null;
            }
            finally { _lock.ExitReadLock(); }
        }

        public bool ChangeAdminPassword(string oldPassword, string newPassword)
        {
            _lock.EnterWriteLock();
            try
            {
                if (!string.Equals(_adminPassword, oldPassword ?? string.Empty, StringComparison.Ordinal))
                    return false;
                _adminPassword = newPassword ?? string.Empty;
                return true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool ChangeEmployeePassword(long badgeNumber, string oldPassword, string newPassword)
        {
            _lock.EnterWriteLock();
            try
            {
                if (!_employees.TryGetValue(badgeNumber, out var entry))
                    return false;
                if (!string.Equals(entry.Password, oldPassword ?? string.Empty, StringComparison.Ordinal))
                    return false;
                _employees[badgeNumber] = (entry.Profile, newPassword ?? string.Empty);
                return true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        // --- Organizational Hierarchy & Personnel ---
        public IReadOnlyList<OrgTitleNodeDto> GetAllTitleNodes()
        {
            _lock.EnterReadLock();
            try { return _titles.Values.ToList(); }
            finally { _lock.ExitReadLock(); }
        }

        public OrgTitleNodeDto GetTitleNodeById(int titleId)
        {
            _lock.EnterReadLock();
            try { return _titles.TryGetValue(titleId, out var node) ? node : null; }
            finally { _lock.ExitReadLock(); }
        }

        public int GetEmployeeCurrentTitleId(long badgeNumber)
        {
            _lock.EnterReadLock();
            try { return _employees.TryGetValue(badgeNumber, out var entry) ? entry.Profile.TitleId : 0; }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyDictionary<long, int> GetAllUserCurrentTitleMap()
        {
            _lock.EnterReadLock();
            try { return _employees.ToDictionary(kv => kv.Key, kv => kv.Value.Profile.TitleId); }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyDictionary<int, int> GetTitleUpperMap()
        {
            _lock.EnterReadLock();
            try { return _titles.ToDictionary(kv => kv.Key, kv => kv.Value.UpperTitleId); }
            finally { _lock.ExitReadLock(); }
        }

        public EmployeeSummaryDto GetEmployeeByBadge(long badgeNumber)
        {
            _lock.EnterReadLock();
            try { return _employees.TryGetValue(badgeNumber, out var entry) ? CloneEmployee(entry.Profile) : null; }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyList<EmployeeSummaryDto> GetEmployeesByTitleIds(IEnumerable<int> titleIds)
        {
            if (titleIds == null) return Array.Empty<EmployeeSummaryDto>();
            var set = new HashSet<int>(titleIds);
            _lock.EnterReadLock();
            try
            {
                return _employees.Values
                    .Where(e => e.Profile.IsActive && set.Contains(e.Profile.TitleId))
                    .Select(e => CloneEmployee(e.Profile))
                    .OrderBy(e => e.LastName)
                    .ThenBy(e => e.FirstName)
                    .ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyList<EmployeeSummaryDto> GetGuardAccessiblePersonnel(long observerBadgeNumber, int observerTitleId)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_titles.TryGetValue(observerTitleId, out var title) || !title.IsObserver)
                    return Array.Empty<EmployeeSummaryDto>();

                return _employees.Values
                    .Where(e => e.Profile.IsActive)
                    .Select(e => CloneEmployee(e.Profile))
                    .OrderBy(e => e.BadgeNumber)
                    .ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        // --- Multi-Step Workflow Configuration & Two-Layer Deputy ---
        public IReadOnlyList<WorkflowStepConfigDto> GetWorkflowStepsForTitle(int requesterTitleId)
        {
            _lock.EnterReadLock();
            try
            {
                return _workflows
                    .Where(w => w.TitleId == requesterTitleId)
                    .OrderBy(w => w.OrderIndex)
                    .ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyList<int> GetAllowedDeputyTitleIds(int principalTitleId)
        {
            _lock.EnterReadLock();
            try
            {
                if (_allowedDeputyTitlesByPrincipalTitle.TryGetValue(principalTitleId, out var set))
                    return set.OrderBy(id => id).ToList();
                return Array.Empty<int>();
            }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyList<long> GetPrincipalBadgesForDeputy(long deputyBadgeNumber)
        {
            _lock.EnterReadLock();
            try
            {
                return _personnelDeputies
                    .Where(d => d.IsActive && d.DeputyBadgeNumber == deputyBadgeNumber)
                    .Select(d => d.PrincipalBadgeNumber)
                    .Distinct()
                    .ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyList<DeputyAssignmentDto> GetDeputyAssignmentsForPrincipal(long principalBadgeNumber)
        {
            _lock.EnterReadLock();
            try
            {
                return _personnelDeputies
                    .Where(d => d.IsActive && d.PrincipalBadgeNumber == principalBadgeNumber)
                    .ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        public DeputyAssignmentDto AssignPersonnelDeputy(long principalBadgeNumber, long deputyBadgeNumber)
        {
            _lock.EnterWriteLock();
            try
            {
                var existing = _personnelDeputies.FirstOrDefault(
                    d => d.IsActive &&
                         d.PrincipalBadgeNumber == principalBadgeNumber &&
                         d.DeputyBadgeNumber == deputyBadgeNumber);
                if (existing != null)
                    return existing;

                _employees.TryGetValue(principalBadgeNumber, out var principal);
                _employees.TryGetValue(deputyBadgeNumber, out var deputy);

                var dto = new DeputyAssignmentDto
                {
                    Id = _nextDeputyId++,
                    PrincipalBadgeNumber = principalBadgeNumber,
                    PrincipalFullName = principal.Profile?.FullName ?? string.Empty,
                    PrincipalTitleId = principal.Profile?.TitleId ?? 0,
                    PrincipalTitleName = principal.Profile?.TitleName ?? string.Empty,
                    DeputyBadgeNumber = deputyBadgeNumber,
                    DeputyFullName = deputy.Profile?.FullName ?? string.Empty,
                    DeputyTitleId = deputy.Profile?.TitleId ?? 0,
                    DeputyTitleName = deputy.Profile?.TitleName ?? string.Empty,
                    AssignedAt = DateTime.Now,
                    IsActive = true
                };
                _personnelDeputies.Add(dto);
                return dto;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool RemovePersonnelDeputy(long principalBadgeNumber, long deputyBadgeNumber)
        {
            _lock.EnterWriteLock();
            try
            {
                int removed = _personnelDeputies.RemoveAll(
                    d => d.PrincipalBadgeNumber == principalBadgeNumber &&
                         d.DeputyBadgeNumber == deputyBadgeNumber);
                return removed > 0;
            }
            finally { _lock.ExitWriteLock(); }
        }

        // --- Report Access & Attendance Rules ---
        public IReadOnlyList<ReportDefinitionDto> GetAllReports()
        {
            _lock.EnterReadLock();
            try { return _reports.ToList(); }
            finally { _lock.ExitReadLock(); }
        }

        public ActiveAttendanceRuleDto GetActiveRule(long badgeNumber, DateTime targetDate)
        {
            _lock.EnterReadLock();
            try
            {
                if (_userRules.TryGetValue(badgeNumber, out var rule))
                    return rule;
                return _defaultRule;
            }
            finally { _lock.ExitReadLock(); }
        }

        // --- Leave Types & Leave Balances ---
        public IReadOnlyList<LeaveTypeDefinitionDto> GetAllLeaveTypes()
        {
            _lock.EnterReadLock();
            try { return _leaveTypes.Values.OrderBy(l => l.LeaveTypeId).ToList(); }
            finally { _lock.ExitReadLock(); }
        }

        public LeaveTypeDefinitionDto GetLeaveTypeById(int leaveTypeId)
        {
            _lock.EnterReadLock();
            try { return _leaveTypes.TryGetValue(leaveTypeId, out var lt) ? lt : null; }
            finally { _lock.ExitReadLock(); }
        }

        public int GetAnnualLeaveEntitlementMinutes(long badgeNumber, int leaveTypeId, int persianYear)
        {
            _lock.EnterReadLock();
            try
            {
                if (_annualEntitlements.TryGetValue((badgeNumber, leaveTypeId, persianYear), out int mins))
                    return mins;
                if (_leaveTypes.TryGetValue(leaveTypeId, out var lt))
                    return lt.YearlyMaxMinutes;
                return 26 * 440;
            }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyList<DailyLeaveRecordDto> GetApprovedDailyLeaves(long badgeNumber, DateTime fromDate, DateTime toDate)
        {
            _lock.EnterReadLock();
            try
            {
                return _dailyLeaves
                    .Where(dl => dl.BadgeNumber == badgeNumber &&
                                 dl.LeaveDate.Date >= fromDate.Date &&
                                 dl.LeaveDate.Date <= toDate.Date)
                    .OrderBy(dl => dl.LeaveDate)
                    .ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyList<HourlyLeaveRecordDto> GetApprovedHourlyLeaves(long badgeNumber, DateTime fromDate, DateTime toDate)
        {
            _lock.EnterReadLock();
            try
            {
                return _hourlyLeaves
                    .Where(hl => hl.BadgeNumber == badgeNumber &&
                                 hl.FromTime.Date >= fromDate.Date &&
                                 hl.FromTime.Date <= toDate.Date)
                    .OrderBy(hl => hl.FromTime)
                    .ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        // --- Workflow Requests & Operational Tables ---
        public WorkflowRequestItemDto GetRequestById(long requestId)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_requests.TryGetValue(requestId, out var req))
                    return null;
                return EnrichRequest(req);
            }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyList<WorkflowRequestItemDto> GetAllRequests()
        {
            _lock.EnterReadLock();
            try { return _requests.Values.Select(EnrichRequest).OrderByDescending(r => r.RequestId).ToList(); }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyList<WorkflowRequestItemDto> GetRequestsByEmployee(long badgeNumber)
        {
            _lock.EnterReadLock();
            try
            {
                return _requests.Values
                    .Where(r => r.BadgeNumber == badgeNumber)
                    .Select(EnrichRequest)
                    .OrderByDescending(r => r.RequestId)
                    .ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        public long AddRequest(WorkflowRequestItemDto request)
        {
            _lock.EnterWriteLock();
            try
            {
                long id = _nextRequestId++;
                request.RequestId = id;
                if (request.RequesterTitleId == 0 && _employees.TryGetValue(request.BadgeNumber, out var emp))
                {
                    request.RequesterTitleId = emp.Profile.TitleId;
                    request.RequesterTitleName = emp.Profile.TitleName;
                    request.RequesterFullName = emp.Profile.FullName;
                }
                _requests[id] = CloneRequest(request);
                return id;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void UpdateRequest(WorkflowRequestItemDto request)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_requests.ContainsKey(request.RequestId))
                {
                    _requests[request.RequestId] = CloneRequest(request);
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool DeleteRequest(long requestId)
        {
            _lock.EnterWriteLock();
            try
            {
                _requestAccepts.RemoveAll(a => a.RequestId == requestId);
                return _requests.Remove(requestId);
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Replicates T5V3.DB.EntityManagers.RequestDA.GetRequestAccepters:
        /// Resolves the ordered list of remaining (OrderIndex, AccepterTitleID) steps for a request.
        /// Uses WorkflowDA steps if defined for the requester's TitleID; otherwise falls back to TitleChart.UpperTitleID.
        /// Subtracts steps already approved in RequestAcceptDA.
        /// </summary>
        public IReadOnlyList<KeyValuePair<int, int>> GetRemainingRequestAccepters(long requestId)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_requests.TryGetValue(requestId, out var req))
                    return Array.Empty<KeyValuePair<int, int>>();

                if (!req.IsSuspend)
                    return Array.Empty<KeyValuePair<int, int>>();

                int requesterTitleId = req.RequesterTitleId;
                if (requesterTitleId == 0 && _employees.TryGetValue(req.BadgeNumber, out var emp))
                    requesterTitleId = emp.Profile.TitleId;

                var configuredSteps = _workflows
                    .Where(w => w.TitleId == requesterTitleId &&
                                (w.Operation == -1 || w.Operation == (int)req.WorkflowType))
                    .OrderBy(w => w.OrderIndex)
                    .Select(w => new KeyValuePair<int, int>(w.OrderIndex, w.AccepterTitleId))
                    .ToList();

                if (configuredSteps.Count == 0)
                {
                    if (_titles.TryGetValue(requesterTitleId, out var titleNode) &&
                        titleNode.UpperTitleId > 0 &&
                        titleNode.UpperTitleId != requesterTitleId)
                    {
                        configuredSteps.Add(new KeyValuePair<int, int>(1, titleNode.UpperTitleId));
                    }
                }

                var alreadyAcceptedTitleIds = new HashSet<int>(
                    _requestAccepts
                        .Where(a => a.RequestId == requestId && !a.IsCancel)
                        .Select(a => a.AccepterTitleId));

                return configuredSteps
                    .Where(step => !alreadyAcceptedTitleIds.Contains(step.Value))
                    .ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyList<RequestAcceptStepDto> GetRequestAcceptHistory(long requestId)
        {
            _lock.EnterReadLock();
            try
            {
                return _requestAccepts
                    .Where(a => a.RequestId == requestId)
                    .OrderBy(a => a.Id)
                    .ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        public void AddRequestAcceptStep(RequestAcceptStepDto step)
        {
            _lock.EnterWriteLock();
            try
            {
                step.Id = _nextAcceptId++;
                if (string.IsNullOrEmpty(step.AccepterFullName) && _employees.TryGetValue(step.AccepterBadgeNumber, out var emp))
                    step.AccepterFullName = emp.Profile.FullName;
                if (string.IsNullOrEmpty(step.AccepterTitleName) && _titles.TryGetValue(step.AccepterTitleId, out var t))
                    step.AccepterTitleName = t.TitleName;

                _requestAccepts.Add(step);
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool DeleteRequestAcceptSteps(long requestId, long? accepterBadgeNumber = null)
        {
            _lock.EnterWriteLock();
            try
            {
                int removed = _requestAccepts.RemoveAll(
                    a => a.RequestId == requestId &&
                         (!accepterBadgeNumber.HasValue || a.AccepterBadgeNumber == accepterBadgeNumber.Value));
                return removed > 0;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public IReadOnlyList<KeyValuePair<long, bool>> GetRequestIdsForUndo(long accepterBadgeNumber, DateTime minDate)
        {
            _lock.EnterReadLock();
            try
            {
                return _requestAccepts
                    .Where(a => a.AccepterBadgeNumber == accepterBadgeNumber && a.AcceptDate.Date >= minDate.Date)
                    .OrderByDescending(a => a.Id)
                    .Select(a => new KeyValuePair<long, bool>(a.RequestId, a.IsCancel))
                    .ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        public void InsertApprovedOperationalData(WorkflowRequestItemDto request, long approverBadgeNumber)
        {
            _lock.EnterWriteLock();
            try
            {
                if (request.WorkflowType == TwfWorkflowType.DailyLeave ||
                    (request.WorkflowType == TwfWorkflowType.DailyDuty && request.IsDaily))
                {
                    DateTime start = request.ForDate.Date;
                    DateTime end = request.ToTime.Date >= start ? request.ToTime.Date : start;
                    for (DateTime d = start; d <= end; d = d.AddDays(1))
                    {
                        _dailyLeaves.Add(new DailyLeaveRecordDto
                        {
                            Id = _dailyLeaves.Count + 1,
                            BadgeNumber = request.BadgeNumber,
                            LeaveDate = d,
                            LeaveTypeId = request.LeaveTypeId,
                            DetailDescriptionId = request.LeaveDescriptionId,
                            Descriptions = request.Descriptions,
                            DocumentNumber = request.RequestId.ToString()
                        });
                        _operationalEntries.Add((request.BadgeNumber, request.WorkflowType, d, request.RequestId));
                    }
                }
                else if (request.WorkflowType == TwfWorkflowType.HourlyLeave ||
                         request.WorkflowType == TwfWorkflowType.HourlyDuty)
                {
                    DateTime fromTime = request.ForDate.Date.Add(request.FromTime.TimeOfDay);
                    DateTime toTime = request.ForDate.Date.Add(request.ToTime.TimeOfDay);
                    if (request.ToTime.TimeOfDay < request.FromTime.TimeOfDay)
                    {
                        // Overnight shift support (matching AcceptRequestController.InsertData)
                        toTime = toTime.AddDays(1);
                    }

                    _hourlyLeaves.Add(new HourlyLeaveRecordDto
                    {
                        Id = _hourlyLeaves.Count + 1,
                        BadgeNumber = request.BadgeNumber,
                        FromTime = fromTime,
                        ToTime = toTime,
                        LeaveTypeId = request.LeaveTypeId,
                        DetailDescriptionId = request.LeaveDescriptionId,
                        Descriptions = request.Descriptions
                    });
                    _operationalEntries.Add((request.BadgeNumber, request.WorkflowType, request.ForDate.Date, request.RequestId));
                }
                else
                {
                    // Record, Late, Early, OverTime, RewardPenalty, ConstOT
                    _operationalEntries.Add((request.BadgeNumber, request.WorkflowType, request.ForDate.Date, request.RequestId));
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void DeleteApprovedOperationalData(WorkflowRequestItemDto request, long undoerBadgeNumber)
        {
            _lock.EnterWriteLock();
            try
            {
                string docNo = request.RequestId.ToString();
                _dailyLeaves.RemoveAll(dl => dl.BadgeNumber == request.BadgeNumber &&
                                             (dl.DocumentNumber == docNo ||
                                              (dl.LeaveDate.Date >= request.ForDate.Date && dl.LeaveDate.Date <= request.ToTime.Date && dl.LeaveTypeId == request.LeaveTypeId)));

                DateTime fromTime = request.ForDate.Date.Add(request.FromTime.TimeOfDay);
                _hourlyLeaves.RemoveAll(hl => hl.BadgeNumber == request.BadgeNumber &&
                                              hl.FromTime == fromTime &&
                                              hl.LeaveTypeId == request.LeaveTypeId);

                _operationalEntries.RemoveAll(e => e.RequestId == request.RequestId);
            }
            finally { _lock.ExitWriteLock(); }
        }

        public int GetOperationalRecordsCount(long badgeNumber, TwfWorkflowType workflowType, DateTime forDate)
        {
            _lock.EnterReadLock();
            try
            {
                return _operationalEntries.Count(
                    e => e.Badge == badgeNumber &&
                         e.Type == workflowType &&
                         e.ForDate.Date == forDate.Date);
            }
            finally { _lock.ExitReadLock(); }
        }

        public int GetCurrentTitleIdForEmployee(long badgeNumber) => GetEmployeeCurrentTitleId(badgeNumber);

        public IReadOnlyList<int> GetSubordinateTitleIds(int managerTitleId, TwfWorkflowType? operation = null)
        {
            _lock.EnterReadLock();
            try
            {
                var set = new HashSet<int>();
                foreach (var wf in _workflows)
                {
                    if (wf.AccepterTitleId == managerTitleId)
                    {
                        if (!operation.HasValue || wf.Operation == (int)operation.Value)
                        {
                            set.Add(wf.TitleId);
                        }
                    }
                }
                foreach (var t in _titles.Values)
                {
                    if (t.UpperTitleId == managerTitleId)
                    {
                        set.Add(t.TitleId);
                    }
                }
                return set.ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyList<WorkflowStepConfigDto> GetWorkflowSteps(int requesterTitleId, TwfWorkflowType operation)
        {
            return GetWorkflowStepsForTitle(requesterTitleId)
                .Where(s => s.Operation == (int)operation)
                .OrderBy(s => s.OrderIndex)
                .ToList();
        }

        public IReadOnlyList<long> GetPrincipalsWhereEmployeeIsDeputy(long deputyBadgeNumber) =>
            GetPrincipalBadgesForDeputy(deputyBadgeNumber);

        public long? GetActiveDeputyForPrincipal(long principalBadgeNumber)
        {
            var assignments = GetDeputyAssignmentsForPrincipal(principalBadgeNumber);
            var active = assignments.FirstOrDefault(a => a.IsActive);
            return active != null ? (long?)active.DeputyBadgeNumber : null;
        }

        public IReadOnlyList<WorkflowRequestItemDto> GetEmployeeRequests(long badgeNumber, bool suspendedOnly = false)
        {
            var all = GetRequestsByEmployee(badgeNumber);
            if (!suspendedOnly)
                return all;
            return all.Where(r => r.IsSuspend).ToList();
        }

        public IReadOnlyList<WorkflowRequestItemDto> GetEmployeeRequestsInPeriod(long badgeNumber, DateTime fromDate, DateTime toDate)
        {
            return GetRequestsByEmployee(badgeNumber)
                .Where(r => r.ForDate.Date >= fromDate.Date && r.ForDate.Date <= toDate.Date)
                .ToList();
        }

        public bool IsRequestEditableByEmployee(long requestId)
        {
            var req = GetRequestById(requestId);
            if (req == null || !req.IsSuspend)
                return false;
            var history = GetRequestAcceptHistory(requestId);
            return history.Count == 0;
        }

        public bool CreateRequest(WorkflowRequestItemDto request, out string error, out long newRequestId)
        {
            error = string.Empty;
            newRequestId = AddRequest(request);
            return newRequestId > 0;
        }

        public bool UpdateRequest(WorkflowRequestItemDto request, out string error)
        {
            error = string.Empty;
            UpdateRequest(request);
            return true;
        }

        public bool DeleteRequest(long requestId, out string error)
        {
            error = string.Empty;
            return DeleteRequest(requestId);
        }

        public IReadOnlyList<KeyValuePair<int, int>> GetPendingRequestAccepterTitles(long requestId)
        {
            var rem = GetRemainingRequestAccepters(requestId);
            if (rem == null || rem.Count == 0)
            {
                return new List<KeyValuePair<int, int>> { new KeyValuePair<int, int>(0, 0) };
            }
            int minOrder = rem.Min(kv => kv.Key);
            return rem.Where(kv => kv.Key == minOrder).ToList();
        }

        public IReadOnlyList<KeyValuePair<long, int>> GetPendingRequestAccepterTitles()
        {
            var result = new List<KeyValuePair<long, int>>();
            foreach (var req in GetAllRequests())
            {
                if (!req.IsSuspend)
                    continue;
                var rem = GetRemainingRequestAccepters(req.RequestId);
                if (rem.Count > 0)
                {
                    int minOrder = rem.Min(kv => kv.Key);
                    foreach (var kv in rem.Where(kv => kv.Key == minOrder))
                    {
                        result.Add(new KeyValuePair<long, int>(req.RequestId, kv.Value));
                    }
                }
            }
            return result;
        }

        public bool AddRequestAcceptStep(RequestAcceptStepDto step, out string error)
        {
            error = string.Empty;
            AddRequestAcceptStep(step);
            return true;
        }

        public bool InsertOperationalDataForApprovedRequest(WorkflowRequestItemDto request, out string error)
        {
            error = string.Empty;
            InsertApprovedOperationalData(request, request.SenderBadgeNumber);
            return true;
        }

        public bool UpsertConstantOvertime(long badgeNumber, DateTime fromDate, DateTime toDate, int minutes, out string error)
        {
            error = string.Empty;
            _lock.EnterWriteLock();
            try
            {
                _constantOvertime[(badgeNumber, fromDate.Date, toDate.Date)] = minutes;
                return true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool UpsertConstantOvertime(long badgeNumber, DateTime forDate, int minutes)
        {
            return UpsertConstantOvertime(badgeNumber, forDate.Date, forDate.Date, minutes, out _);
        }

        public int GetConstantOvertimeMinutes(long badgeNumber, DateTime fromDate, DateTime toDate)
        {
            _lock.EnterReadLock();
            try
            {
                return _constantOvertime.TryGetValue((badgeNumber, fromDate.Date, toDate.Date), out int mins) ? mins : 0;
            }
            finally { _lock.ExitReadLock(); }
        }

        private WorkflowRequestItemDto EnrichRequest(WorkflowRequestItemDto src)
        {
            var copy = CloneRequest(src);
            copy.AcceptedSteps = GetRequestAcceptHistory(src.RequestId).ToList();
            copy.RemainingAccepters = GetRemainingRequestAccepters(src.RequestId).ToList();
            return copy;
        }

        private static EmployeeSummaryDto CloneEmployee(EmployeeSummaryDto src)
        {
            if (src == null) return null;
            return new EmployeeSummaryDto
            {
                BadgeNumber = src.BadgeNumber,
                UserName = src.UserName,
                FirstName = src.FirstName,
                LastName = src.LastName,
                CardNumber = src.CardNumber,
                NationalCode = src.NationalCode,
                TitleId = src.TitleId,
                TitleName = src.TitleName,
                DepartmentId = src.DepartmentId,
                DepartmentName = src.DepartmentName,
                IsActive = src.IsActive,
                ViaDeputyForBadgeNumber = src.ViaDeputyForBadgeNumber,
                ViaDeputyForTitleName = src.ViaDeputyForTitleName
            };
        }

        private static WorkflowRequestItemDto CloneRequest(WorkflowRequestItemDto src)
        {
            return new WorkflowRequestItemDto
            {
                RequestId = src.RequestId,
                BadgeNumber = src.BadgeNumber,
                RequesterFullName = src.RequesterFullName,
                RequesterTitleId = src.RequesterTitleId,
                RequesterTitleName = src.RequesterTitleName,
                WorkflowType = src.WorkflowType,
                IsDaily = src.IsDaily,
                LeaveTypeId = src.LeaveTypeId,
                LeaveTypeName = src.LeaveTypeName,
                LeaveDescriptionId = src.LeaveDescriptionId,
                OverTimeAllowType = src.OverTimeAllowType,
                RequestDate = src.RequestDate,
                ForDate = src.ForDate,
                FromTime = src.FromTime,
                ToTime = src.ToTime,
                RecordTime = src.RecordTime,
                RecordCheckType = src.RecordCheckType,
                AllowValueDuration = src.AllowValueDuration,
                Descriptions = src.Descriptions,
                IsSuspend = src.IsSuspend,
                RequestState = src.RequestState,
                SenderBadgeNumber = src.SenderBadgeNumber,
                SenderTitleId = src.SenderTitleId,
                ImageData = src.ImageData,
                ForDeputy = src.ForDeputy,
                DeputyPrincipalTitleId = src.DeputyPrincipalTitleId,
                DeputyPrincipalTitleName = src.DeputyPrincipalTitleName
            };
        }
    }
}
