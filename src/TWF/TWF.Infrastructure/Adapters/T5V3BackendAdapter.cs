using System;
using System.Collections.Generic;
using System.Linq;
using Personnel;
using T5V3.DB;
using T5V3.Model;
using TWF.Domain.Interfaces;
using TWF.Domain.Models;
using PersonnelDepartment = Personnel.Model.Department;
using PersonnelTitleChart = Personnel.Model.TitleChart;
using PersonnelUserInfo = Personnel.Model.UserInfo;
using T5Record = T5V3.Model.Record;
using T5RecordSource = T5V3.Model.RecordSource;

namespace TWF.Infrastructure.Adapters
{
    /// <summary>
    /// Production implementation of <see cref="IT5BackendAdapter"/> backed by <see cref="T5V3DBManager"/>
    /// and <see cref="PersonnelSystem"/>.
    /// </summary>
    public sealed class T5V3BackendAdapter : IT5BackendAdapter
    {
        private readonly string _personnelConnectionString;
        private readonly string _t5ConnectionString;

        public T5V3BackendAdapter(string personnelConnectionString, string t5ConnectionString)
        {
            _personnelConnectionString = personnelConnectionString ?? string.Empty;
            _t5ConnectionString = t5ConnectionString ?? string.Empty;
        }

        private PersonnelSystem CreatePersonnelSystem() => new PersonnelSystem(_personnelConnectionString);

        private T5V3DBManager CreateT5Db() => new T5V3DBManager(_t5ConnectionString);

        // --- Authentication & Password Management ---
        public bool VerifyAdminPassword(string password)
        {
            if (password == null)
                return false;

            var ps = CreatePersonnelSystem();
            long badge = ps.DB.UserInfos.CheckUserPass("admin", password, true);
            if (badge <= 0)
                return false;

            var t5 = CreateT5Db();
            return t5.UserPermissionDA.IsAnyMainManager(new List<long> { badge });
        }

        public long? AuthenticateEmployee(string userName, string password)
        {
            if (string.IsNullOrWhiteSpace(userName) || password == null)
                return null;

            var ps = CreatePersonnelSystem();
            long badge = ps.DB.UserInfos.CheckUserPass(userName, password, true);
            if (badge <= 0)
                return null;

            return badge;
        }

        public bool ChangeAdminPassword(string oldPassword, string newPassword)
        {
            var ps = CreatePersonnelSystem();
            long badge = ps.DB.UserInfos.CheckUserPass("admin", oldPassword ?? string.Empty, true);
            if (badge <= 0)
                return false;

            return ps.DB.UserInfos.EditPassword(badge, newPassword ?? string.Empty);
        }

        public bool ChangeEmployeePassword(long badgeNumber, string oldPassword, string newPassword)
        {
            var ps = CreatePersonnelSystem();
            PersonnelUserInfo user = ps.DB.UserInfos.GetByID(badgeNumber);
            if (user == null || user.Password != (oldPassword ?? string.Empty))
                return false;

            return ps.DB.UserInfos.EditPassword(badgeNumber, newPassword ?? string.Empty);
        }

        // --- Organizational Hierarchy & Personnel ---
        public IReadOnlyList<OrgTitleNodeDto> GetAllTitleNodes()
        {
            var ps = CreatePersonnelSystem();
            var result = new List<OrgTitleNodeDto>();
            foreach (KeyValuePair<int, int> pair in ps.DB.TitlesCharts.GetAllWithUppersOnly())
            {
                PersonnelTitleChart tc = ps.DB.TitlesCharts.GetByID(pair.Key);
                if (tc != null)
                {
                    result.Add(new OrgTitleNodeDto
                    {
                        TitleId = tc.TitleID,
                        TitleName = tc.TitleName ?? string.Empty,
                        DisplayName = tc.DisplayName ?? tc.TitleName ?? string.Empty,
                        UpperTitleId = tc.UpperTitleID,
                        IsObserver = tc.Observer.HasValue && tc.Observer.Value
                    });
                }
            }
            return result;
        }

        public OrgTitleNodeDto GetTitleNodeById(int titleId)
        {
            if (titleId <= 0)
                return null;

            var ps = CreatePersonnelSystem();
            PersonnelTitleChart tc = ps.DB.TitlesCharts.GetByID(titleId);
            if (tc == null)
                return null;

            return new OrgTitleNodeDto
            {
                TitleId = tc.TitleID,
                TitleName = tc.TitleName ?? string.Empty,
                DisplayName = tc.DisplayName ?? tc.TitleName ?? string.Empty,
                UpperTitleId = tc.UpperTitleID,
                IsObserver = tc.Observer.HasValue && tc.Observer.Value
            };
        }

        public int GetEmployeeCurrentTitleId(long badgeNumber)
        {
            var ps = CreatePersonnelSystem();
            return ps.DB.UserInfos.GetUserCurrentTitleID(badgeNumber);
        }

        public IReadOnlyDictionary<long, int> GetAllUserCurrentTitleMap()
        {
            var ps = CreatePersonnelSystem();
            var dict = new Dictionary<long, int>();
            foreach (var kv in ps.DB.UserInfos.GetAllUserInfoTitles())
            {
                dict[kv.Key] = kv.Value;
            }
            return dict;
        }

        public IReadOnlyDictionary<int, int> GetTitleUpperMap()
        {
            var ps = CreatePersonnelSystem();
            var dict = new Dictionary<int, int>();
            foreach (var kv in ps.DB.TitlesCharts.GetAllWithUppersOnly())
            {
                dict[kv.Key] = kv.Value;
            }
            return dict;
        }

        public EmployeeSummaryDto GetEmployeeByBadge(long badgeNumber)
        {
            var ps = CreatePersonnelSystem();
            PersonnelUserInfo user = ps.DB.UserInfos.GetByID(badgeNumber);
            if (user == null)
                return null;

            int titleId = ps.DB.UserInfos.GetUserCurrentTitleID(badgeNumber);
            return MapEmployee(user, titleId);
        }

        public IReadOnlyList<EmployeeSummaryDto> GetEmployeesByTitleIds(IEnumerable<int> titleIds)
        {
            var idList = titleIds?.Distinct().ToList() ?? new List<int>();
            if (idList.Count == 0)
                return Array.Empty<EmployeeSummaryDto>();

            var ps = CreatePersonnelSystem();
            var users = ps.DB.UserInfos.GetAllByTitle(idList);
            return users.Select(u => MapEmployee(u, ps.DB.UserInfos.GetUserCurrentTitleID(u.BadgeNumber))).ToList();
        }

        public IReadOnlyList<EmployeeSummaryDto> GetGuardAccessiblePersonnel(long observerBadgeNumber, int observerTitleId)
        {
            if (observerTitleId <= 0)
                return Array.Empty<EmployeeSummaryDto>();

            var ps = CreatePersonnelSystem();
            List<int> depts = ps.DB.TitlesCharts.GetAllGuardAccess(observerTitleId);
            return ps.DB.UserInfos
                .GetAll(depts)
                .Where(u => u.DeptID > 0)
                .Select(u => MapEmployee(u, ps.DB.UserInfos.GetUserCurrentTitleID(u.BadgeNumber)))
                .ToList();
        }

        // --- Multi-Step Workflow Configuration & Two-Layer Deputy ---
        public IReadOnlyList<WorkflowStepConfigDto> GetWorkflowStepsForTitle(int requesterTitleId)
        {
            if (requesterTitleId <= 0)
                return Array.Empty<WorkflowStepConfigDto>();

            var t5 = CreateT5Db();
            var steps = new List<WorkflowStepConfigDto>();
            foreach (WorkflowOperations op in Enum.GetValues(typeof(WorkflowOperations)))
            {
                foreach (Workflow w in t5.WorkflowDA.GetAll(requesterTitleId, op))
                {
                    steps.Add(new WorkflowStepConfigDto
                    {
                        TitleId = w.TitleID,
                        AccepterTitleId = w.AccepterTitleID,
                        OrderIndex = w.OrderIndex,
                        Operation = (int)w.Operation
                    });
                }
            }
            return steps.OrderBy(s => s.OrderIndex).ToList();
        }

        public IReadOnlyList<int> GetAllowedDeputyTitleIds(int principalTitleId)
        {
            if (principalTitleId <= 0)
                return Array.Empty<int>();

            var t5 = CreateT5Db();
            return t5.DeputyDA.GetAll(principalTitleId);
        }

        public IReadOnlyList<long> GetPrincipalBadgesForDeputy(long deputyBadgeNumber)
        {
            if (deputyBadgeNumber <= 0)
                return Array.Empty<long>();

            var t5 = CreateT5Db();
            return t5.PersonnelDeputyDA.IsDeputy(deputyBadgeNumber);
        }

        public IReadOnlyList<DeputyAssignmentDto> GetDeputyAssignmentsForPrincipal(long principalBadgeNumber)
        {
            var t5 = CreateT5Db();
            var ps = CreatePersonnelSystem();
            long deputyBadge = t5.PersonnelDeputyDA.GetDeputy(principalBadgeNumber);
            if (deputyBadge <= 0)
                return Array.Empty<DeputyAssignmentDto>();

            PersonnelUserInfo principal = ps.DB.UserInfos.GetByID(principalBadgeNumber);
            PersonnelUserInfo deputy = ps.DB.UserInfos.GetByID(deputyBadge);
            return new List<DeputyAssignmentDto>
            {
                new DeputyAssignmentDto
                {
                    PrincipalBadgeNumber = principalBadgeNumber,
                    PrincipalFullName = principal?.ToString() ?? string.Empty,
                    DeputyBadgeNumber = deputyBadge,
                    DeputyFullName = deputy?.ToString() ?? string.Empty,
                    IsActive = true
                }
            };
        }

        public DeputyAssignmentDto AssignPersonnelDeputy(long principalBadgeNumber, long deputyBadgeNumber)
        {
            var t5 = CreateT5Db();
            var ps = CreatePersonnelSystem();
            t5.PersonnelDeputyDA.UpdateDeputyBadgeNumber(principalBadgeNumber, deputyBadgeNumber);

            PersonnelUserInfo principal = ps.DB.UserInfos.GetByID(principalBadgeNumber);
            PersonnelUserInfo deputy = ps.DB.UserInfos.GetByID(deputyBadgeNumber);
            return new DeputyAssignmentDto
            {
                PrincipalBadgeNumber = principalBadgeNumber,
                PrincipalFullName = principal?.ToString() ?? string.Empty,
                DeputyBadgeNumber = deputyBadgeNumber,
                DeputyFullName = deputy?.ToString() ?? string.Empty,
                IsActive = true
            };
        }

        public bool RemovePersonnelDeputy(long principalBadgeNumber, long deputyBadgeNumber)
        {
            var t5 = CreateT5Db();
            return t5.PersonnelDeputyDA.DeleteDeputy(principalBadgeNumber);
        }

        // --- Report Access & Attendance Rules ---
        public IReadOnlyList<ReportDefinitionDto> GetAllReports()
        {
            var t5 = CreateT5Db();
            return t5.ReportDA
                .GetAll()
                .Select(r => new ReportDefinitionDto
                {
                    ReportId = r.ReportID,
                    ReportName = r.ReportName ?? string.Empty
                })
                .ToList();
        }

        public ActiveAttendanceRuleDto GetActiveRule(long badgeNumber, DateTime targetDate)
        {
            var t5 = CreateT5Db();
            var ps = CreatePersonnelSystem();

            Rule resolvedRule = null;
            UserRule userRule = t5.UserRuleDA
                .GetAll(new List<long> { badgeNumber })
                .FirstOrDefault(r => r.RuleType == RuleType.Personnal && r.FromDate <= targetDate && r.ToDate >= targetDate);

            if (userRule != null)
            {
                resolvedRule = t5.RuleDA.GetByID(userRule.RuleID);
            }
            else
            {
                List<DepartmentRule> allDeptRules = t5.DepartmentRuleDA.GetAll();
                List<PersonnelDepartment> allDepts = ps.DB.Departments.GetAll();
                PersonnelUserInfo user = ps.DB.UserInfos.GetByID(badgeNumber);
                if (user != null)
                {
                    int deptId = user.DeptID;
                    var visited = new HashSet<int>();
                    while (deptId > 0 && visited.Add(deptId))
                    {
                        int currentDeptId = deptId;
                        DepartmentRule deptRule = allDeptRules.FirstOrDefault(
                            r => r.DepartmentID == currentDeptId && r.FromDate <= targetDate && r.ToDate >= targetDate);
                        if (deptRule != null)
                        {
                            resolvedRule = t5.RuleDA.GetByID(deptRule.RuleID);
                            break;
                        }
                        PersonnelDepartment dept = allDepts.FirstOrDefault(d => d.DepartmentID == currentDeptId);
                        if (dept == null)
                            break;
                        deptId = dept.UpperDeptID;
                    }
                }
            }

            if (resolvedRule == null)
                return null;

            return new ActiveAttendanceRuleDto
            {
                RuleId = resolvedRule.RuleID,
                RuleName = resolvedRule.ParamName ?? string.Empty,
                LimitRecReqInDay = resolvedRule.LimitRecReqInDay,
                LimitRecReqInMonth = resolvedRule.LimitRecReqInMonth,
                ConstOTMaxDailyMinutes = (int)resolvedRule.ConstOTMaxDaily.TotalMinutes,
                ConstOTMaxMonthlyMinutes = (int)resolvedRule.ConstOTMaxMonthly.TotalMinutes
            };
        }

        // --- Leave Types & Leave Balances ---
        public IReadOnlyList<LeaveTypeDefinitionDto> GetAllLeaveTypes()
        {
            var t5 = CreateT5Db();
            return t5.LeaveTypeDA.GetAll().Select(MapLeaveType).ToList();
        }

        public LeaveTypeDefinitionDto GetLeaveTypeById(int leaveTypeId)
        {
            var t5 = CreateT5Db();
            LeaveType lt = t5.LeaveTypeDA.GetByID(leaveTypeId);
            return lt == null ? null : MapLeaveType(lt);
        }

        public int GetAnnualLeaveEntitlementMinutes(long badgeNumber, int leaveTypeId, int persianYear)
        {
            var t5 = CreateT5Db();
            LeaveType lt = t5.LeaveTypeDA.GetByID(leaveTypeId);
            double eachDayMinutes = (lt != null && lt.EachXHoursADay.TotalMinutes > 0)
                ? lt.EachXHoursADay.TotalMinutes
                : 440.0;

            YearLeaveRemain remain = t5.YearRemainDA
                .GetAll(badgeNumber)
                .FirstOrDefault(r => r.Year == persianYear && r.LeaveTypeID == leaveTypeId);

            if (remain == null)
                return (int)Math.Round((lt?.MaxDailyInYear ?? 26.0) * eachDayMinutes);

            return (int)Math.Round(remain.Value * eachDayMinutes);
        }

        public IReadOnlyList<DailyLeaveRecordDto> GetApprovedDailyLeaves(long badgeNumber, DateTime fromDate, DateTime toDate)
        {
            var t5 = CreateT5Db();
            return t5.DailyLeaveDA
                .GetAll(new List<long> { badgeNumber }, fromDate, toDate)
                .Select(d => new DailyLeaveRecordDto
                {
                    BadgeNumber = d.BadgeNumber,
                    LeaveDate = d.LeaveDate,
                    LeaveTypeId = d.LeaveTypeID,
                    DetailDescriptionId = d.DetailDescriptionID,
                    Descriptions = d.Descriptions ?? string.Empty
                })
                .ToList();
        }

        public IReadOnlyList<HourlyLeaveRecordDto> GetApprovedHourlyLeaves(long badgeNumber, DateTime fromDate, DateTime toDate)
        {
            var t5 = CreateT5Db();
            return t5.HourlyLeaveDA
                .GetAll(new List<long> { badgeNumber }, fromDate, toDate)
                .Select(h => new HourlyLeaveRecordDto
                {
                    Id = h.ID,
                    BadgeNumber = h.BadgeNumber,
                    FromTime = h.FromTime,
                    ToTime = h.ToTime,
                    LeaveTypeId = h.LeaveTypeID,
                    Descriptions = h.Descriptions ?? string.Empty
                })
                .ToList();
        }

        // --- Workflow Requests & Operational Tables ---
        public WorkflowRequestItemDto GetRequestById(long requestId)
        {
            var t5 = CreateT5Db();
            var ps = CreatePersonnelSystem();
            Request req = t5.RequestDA.GetByID(requestId);
            return req == null ? null : MapRequest(req, t5, ps);
        }

        public IReadOnlyList<WorkflowRequestItemDto> GetAllRequests()
        {
            var t5 = CreateT5Db();
            var ps = CreatePersonnelSystem();
            var all = t5.RequestDA.GetAll(true).Concat(t5.RequestDA.GetAll(false));
            return all.Select(r => MapRequest(r, t5, ps)).ToList();
        }

        public IReadOnlyList<WorkflowRequestItemDto> GetRequestsByEmployee(long badgeNumber)
        {
            var t5 = CreateT5Db();
            var ps = CreatePersonnelSystem();
            var all = t5.RequestDA.GetAll(badgeNumber, true).Concat(t5.RequestDA.GetAll(badgeNumber, false));
            return all.Select(r => MapRequest(r, t5, ps)).ToList();
        }

        public long AddRequest(WorkflowRequestItemDto request)
        {
            if (request == null)
                return -1;

            var t5 = CreateT5Db();
            Request model = MapToT5Request(request);
            if (t5.RequestDA.AddNew(model, true, out _, out long newId))
                return newId;

            return -1;
        }

        public void UpdateRequest(WorkflowRequestItemDto request)
        {
            if (request == null)
                return;

            var t5 = CreateT5Db();
            Request model = MapToT5Request(request);
            t5.RequestDA.Edit(model, out _);
        }

        public bool DeleteRequest(long requestId)
        {
            var t5 = CreateT5Db();
            return t5.RequestDA.Delete(requestId, out _);
        }

        public IReadOnlyList<KeyValuePair<int, int>> GetRemainingRequestAccepters(long requestId)
        {
            var t5 = CreateT5Db();
            var ps = CreatePersonnelSystem();
            var pairs = t5.RequestDA.GetRequestAccepters(
                ps.DB.UserInfos.GetAllUserInfoTitles(),
                ps.DB.TitlesCharts.GetAllWithUppersOnly(),
                requestId);

            return pairs
                .Where(p => p.Value > 0)
                .Select((p, idx) => new KeyValuePair<int, int>(idx + 1, p.Value))
                .ToList();
        }

        public IReadOnlyList<RequestAcceptStepDto> GetRequestAcceptHistory(long requestId)
        {
            var t5 = CreateT5Db();
            var ps = CreatePersonnelSystem();
            return t5.RequestAcceptDA
                .GetByID(requestId)
                .Select(a =>
                {
                    PersonnelUserInfo u = ps.DB.UserInfos.GetByID(a.AccepterBadgeNumber);
                    PersonnelTitleChart tc = ps.DB.TitlesCharts.GetByID(a.AccepterTitleID);
                    return new RequestAcceptStepDto
                    {
                        RequestId = a.RequestID,
                        AccepterBadgeNumber = a.AccepterBadgeNumber,
                        AccepterFullName = u != null ? u.ToString() : string.Empty,
                        AccepterTitleId = a.AccepterTitleID,
                        AccepterTitleName = tc?.TitleName ?? string.Empty,
                        AcceptDate = a.AcceptDate,
                        IsCancel = a.IsCancel,
                        IsDeputy = a.IsDeputy
                    };
                })
                .ToList();
        }

        public void AddRequestAcceptStep(RequestAcceptStepDto step)
        {
            if (step == null)
                return;

            var t5 = CreateT5Db();
            var model = new RequestAcceptMdl
            {
                RequestID = step.RequestId,
                AccepterBadgeNumber = step.AccepterBadgeNumber,
                AccepterTitleID = step.AccepterTitleId,
                AcceptDate = step.AcceptDate == default(DateTime) ? DateTime.Now : step.AcceptDate,
                IsCancel = step.IsCancel,
                IsDeputy = step.IsDeputy
            };
            t5.RequestAcceptDA.AddNew(model, out _);
        }

        public bool DeleteRequestAcceptSteps(long requestId, long? accepterBadgeNumber = null)
        {
            var t5 = CreateT5Db();
            if (accepterBadgeNumber.HasValue)
            {
                return t5.RequestAcceptDA.Delete(requestId, accepterBadgeNumber.Value);
            }
            return true;
        }

        public IReadOnlyList<KeyValuePair<long, bool>> GetRequestIdsForUndo(long accepterBadgeNumber, DateTime minDate)
        {
            var t5 = CreateT5Db();
            return t5.RequestAcceptDA.GetRequestIDsForUndo(accepterBadgeNumber, minDate);
        }

        public void InsertApprovedOperationalData(WorkflowRequestItemDto request, long approverBadgeNumber)
        {
            if (request == null)
                return;

            var t5 = CreateT5Db();
            if (request.WorkflowType == TwfWorkflowType.DailyLeave || request.WorkflowType == TwfWorkflowType.DailyDuty)
            {
                DateTime current = request.ForDate.Date;
                DateTime end = request.ToTime.Date < current ? current : request.ToTime.Date;
                while (current <= end)
                {
                    var dl = new DailyLeave
                    {
                        BadgeNumber = request.BadgeNumber,
                        LeaveDate = current,
                        LeaveTypeID = request.LeaveTypeId,
                        DetailDescriptionID = request.LeaveDescriptionId,
                        Descriptions = request.Descriptions ?? string.Empty
                    };
                    t5.DailyLeaveDA.AddNew(dl, out _);
                    current = current.AddDays(1.0);
                }
            }
            else if (request.WorkflowType == TwfWorkflowType.HourlyLeave || request.WorkflowType == TwfWorkflowType.HourlyDuty)
            {
                var hl = new HourlyLeave
                {
                    BadgeNumber = request.BadgeNumber,
                    LeaveTypeID = request.LeaveTypeId,
                    FromTime = request.FromTime,
                    ToTime = request.ToTime,
                    Descriptions = request.Descriptions ?? string.Empty
                };
                t5.HourlyLeaveDA.AddNew(hl, false, out _, out _);
            }
            else if (request.WorkflowType == TwfWorkflowType.Late || request.WorkflowType == TwfWorkflowType.Early)
            {
                var ev = new EditedValue
                {
                    BadgeNumber = request.BadgeNumber,
                    ForDate = request.ForDate.Date,
                    ValidDuration = TimeSpan.Zero,
                    ValueType = request.WorkflowType == TwfWorkflowType.Early ? EditValueType.Early : EditValueType.Late
                };
                t5.EditedValueDA.InsertOrEdit(ev, out _);
            }
            else if (request.WorkflowType == TwfWorkflowType.RewardPenalty)
            {
                var ev = new EditedValue
                {
                    BadgeNumber = request.BadgeNumber,
                    ForDate = request.ForDate.Date,
                    ValidDuration = request.AllowValueDuration,
                    ValueType = (AllowType)request.OverTimeAllowType == AllowType.Early ? EditValueType.Penalty : EditValueType.Reward
                };
                t5.EditedValueDA.InsertOrEdit(ev, out _);
            }
            else if (request.WorkflowType == TwfWorkflowType.OverTime)
            {
                var av = new AllowedValue(
                    request.BadgeNumber,
                    request.ForDate.Date,
                    (AllowType)request.OverTimeAllowType,
                    request.AllowValueDuration);
                t5.AllowValueDA.AddOrEdit(av);
            }
            else if (request.WorkflowType == TwfWorkflowType.Record)
            {
                var rec = new T5Record
                {
                    BadgeNumber = request.BadgeNumber,
                    CheckTime = request.RecordTime,
                    CheckType = request.RecordCheckType,
                    IsValid = true,
                    InsertTime = DateTime.Now,
                    InsertUser = approverBadgeNumber,
                    SourceType = T5RecordSource.Manual
                };
                t5.RecordDA.Insert(rec, out _);
            }
        }

        public void DeleteApprovedOperationalData(WorkflowRequestItemDto request, long undoerBadgeNumber)
        {
            if (request == null)
                return;

            var t5 = CreateT5Db();
            if (request.WorkflowType == TwfWorkflowType.DailyLeave || request.WorkflowType == TwfWorkflowType.DailyDuty)
            {
                DateTime current = request.ForDate.Date;
                DateTime end = request.ToTime.Date < current ? current : request.ToTime.Date;
                while (current <= end)
                {
                    DailyLeave existing = t5.DailyLeaveDA.GetByID(request.BadgeNumber, current);
                    if (existing != null)
                    {
                        t5.DailyLeaveDA.Delete(existing, out _);
                    }
                    current = current.AddDays(1.0);
                }
            }
            else if (request.WorkflowType == TwfWorkflowType.HourlyLeave || request.WorkflowType == TwfWorkflowType.HourlyDuty)
            {
                t5.HourlyLeaveDA.Delete(request.BadgeNumber, request.FromTime, request.ToTime);
            }
            else if (request.WorkflowType == TwfWorkflowType.Late || request.WorkflowType == TwfWorkflowType.Early)
            {
                var ev = new EditedValue
                {
                    BadgeNumber = request.BadgeNumber,
                    ForDate = request.ForDate.Date,
                    ValidDuration = TimeSpan.Zero,
                    ValueType = request.WorkflowType == TwfWorkflowType.Early ? EditValueType.Early : EditValueType.Late
                };
                t5.EditedValueDA.Delete(ev, out _);
            }
            else if (request.WorkflowType == TwfWorkflowType.RewardPenalty)
            {
                var ev = new EditedValue
                {
                    BadgeNumber = request.BadgeNumber,
                    ForDate = request.ForDate.Date,
                    ValidDuration = request.AllowValueDuration,
                    ValueType = (AllowType)request.OverTimeAllowType == AllowType.Early ? EditValueType.Penalty : EditValueType.Reward
                };
                t5.EditedValueDA.Delete(ev, out _);
            }
            else if (request.WorkflowType == TwfWorkflowType.OverTime)
            {
                var av = new AllowedValue(
                    request.BadgeNumber,
                    request.ForDate.Date,
                    (AllowType)request.OverTimeAllowType,
                    request.AllowValueDuration);
                t5.AllowValueDA.Delete(av);
            }
            else if (request.WorkflowType == TwfWorkflowType.Record)
            {
                var rec = new T5Record
                {
                    BadgeNumber = request.BadgeNumber,
                    CheckTime = request.RecordTime
                };
                t5.RecordDA.Delete(rec, out _);
            }
        }

        public int GetOperationalRecordsCount(long badgeNumber, TwfWorkflowType workflowType, DateTime forDate)
        {
            var t5 = CreateT5Db();
            DateTime from = forDate.Date;
            DateTime to = forDate.Date.AddDays(1.0).AddTicks(-1);
            return t5.RequestDA
                .GetAll(badgeNumber, from, to)
                .Count();
        }

        public int GetCurrentTitleIdForEmployee(long badgeNumber) => GetEmployeeCurrentTitleId(badgeNumber);

        public IReadOnlyList<int> GetSubordinateTitleIds(int managerTitleId, TwfWorkflowType? operation = null)
        {
            var t5 = CreateT5Db();
            var query = t5.WorkflowDA.GetAll(managerTitleId).AsEnumerable();
            if (operation.HasValue)
            {
                WorkflowOperations op = MapToT5Operation(operation.Value);
                query = query.Where(wf => wf.Operation == op);
            }
            return query.Select(wf => wf.TitleID).Distinct().ToList();
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
            var t5 = CreateT5Db();
            long dep = t5.PersonnelDeputyDA.GetDeputy(principalBadgeNumber);
            return dep > 0 ? (long?)dep : null;
        }

        public IReadOnlyList<WorkflowRequestItemDto> GetEmployeeRequests(long badgeNumber, bool suspendedOnly = false)
        {
            var t5 = CreateT5Db();
            var ps = CreatePersonnelSystem();
            var list = t5.RequestDA.GetAll(badgeNumber, suspendedOnly);
            return list.Select(r => MapRequest(r, t5, ps)).ToList();
        }

        public IReadOnlyList<WorkflowRequestItemDto> GetEmployeeRequestsInPeriod(long badgeNumber, DateTime fromDate, DateTime toDate)
        {
            var t5 = CreateT5Db();
            var ps = CreatePersonnelSystem();
            var list = t5.RequestDA.GetAll(badgeNumber, fromDate, toDate);
            return list.Select(r => MapRequest(r, t5, ps)).ToList();
        }

        public bool IsRequestEditableByEmployee(long requestId)
        {
            var t5 = CreateT5Db();
            return t5.RequestAcceptDA.IsEditableRequest(requestId);
        }

        public bool CreateRequest(WorkflowRequestItemDto request, out string error, out long newRequestId)
        {
            var t5 = CreateT5Db();
            var model = MapToT5Request(request);
            return t5.RequestDA.AddNew(model, true, out error, out newRequestId);
        }

        public bool UpdateRequest(WorkflowRequestItemDto request, out string error)
        {
            var t5 = CreateT5Db();
            var model = MapToT5Request(request);
            return t5.RequestDA.Edit(model, out error);
        }

        public bool DeleteRequest(long requestId, out string error)
        {
            var t5 = CreateT5Db();
            return t5.RequestDA.Delete(requestId, out error);
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
                if (rem != null && rem.Count > 0)
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
            var t5 = CreateT5Db();
            var model = new RequestAcceptMdl
            {
                RequestID = step.RequestId,
                AccepterBadgeNumber = step.AccepterBadgeNumber,
                AccepterTitleID = step.AccepterTitleId,
                AcceptDate = step.AcceptDate == default(DateTime) ? DateTime.Now : step.AcceptDate,
                IsCancel = step.IsCancel,
                IsDeputy = step.IsDeputy
            };
            return t5.RequestAcceptDA.AddNew(model, out error);
        }

        public bool InsertOperationalDataForApprovedRequest(WorkflowRequestItemDto request, out string error)
        {
            error = string.Empty;
            InsertApprovedOperationalData(request, request.SenderBadgeNumber);
            return true;
        }

        public bool UpsertConstantOvertime(long badgeNumber, DateTime fromDate, DateTime toDate, int minutes, out string error)
        {
            var t5 = CreateT5Db();
            var model = new EditedValue
            {
                BadgeNumber = badgeNumber,
                ForDate = fromDate,
                ValidDuration = TimeSpan.FromMinutes(minutes),
                ValueType = EditValueType.OTConst
            };
            return t5.EditedValueDA.InsertOrEdit(model, out error);
        }

        public bool UpsertConstantOvertime(long badgeNumber, DateTime forDate, int minutes)
        {
            return UpsertConstantOvertime(badgeNumber, forDate, forDate, minutes, out _);
        }

        public int GetConstantOvertimeMinutes(long badgeNumber, DateTime fromDate, DateTime toDate)
        {
            var t5 = CreateT5Db();
            var list = t5.EditedValueDA.GetAll(new List<long> { badgeNumber }, fromDate, toDate);
            var item = list.FirstOrDefault(ev => ev.ValueType == EditValueType.OTConst);
            return item != null ? (int)Math.Round(item.ValidDuration.TotalMinutes) : 0;
        }

        private static WorkflowOperations MapToT5Operation(TwfWorkflowType wf)
        {
            switch (wf)
            {
                case TwfWorkflowType.DailyLeave:
                case TwfWorkflowType.HourlyLeave:
                    return WorkflowOperations.Leave;
                case TwfWorkflowType.DailyDuty:
                case TwfWorkflowType.HourlyDuty:
                    return WorkflowOperations.Duty;
                case TwfWorkflowType.Record:
                    return WorkflowOperations.InsertRecord;
                case TwfWorkflowType.Late:
                case TwfWorkflowType.Early:
                    return WorkflowOperations.LateEarly;
                case TwfWorkflowType.OverTime:
                    return WorkflowOperations.AllowOverTimes;
                case TwfWorkflowType.RewardPenalty:
                    return WorkflowOperations.RewardAndPenalty;
                default:
                    return WorkflowOperations.Leave;
            }
        }

        // --- Private Mapping Helpers ---
        private static EmployeeSummaryDto MapEmployee(PersonnelUserInfo u, int titleId)
        {
            if (u == null)
                return null;

            return new EmployeeSummaryDto
            {
                BadgeNumber = u.BadgeNumber,
                EmployeeNumber = u.EmployeeNumber ?? string.Empty,
                FirstName = u.FirstName ?? string.Empty,
                LastName = u.LastName ?? string.Empty,
                DepartmentId = u.DeptID,
                EmployeeTypeId = u.EmpTypeID,
                TitleId = titleId,
                UserName = u.UserName ?? string.Empty,
                WebUserEnabled = u.WebUser
            };
        }

        private static LeaveTypeDefinitionDto MapLeaveType(LeaveType lt)
        {
            int dayMins = lt.EachXHoursADay.TotalMinutes > 0
                ? (int)Math.Round(lt.EachXHoursADay.TotalMinutes)
                : 440;

            return new LeaveTypeDefinitionDto
            {
                LeaveTypeId = lt.LeaveTypeID,
                LeaveName = lt.LeaveName ?? string.Empty,
                Symbol = lt.Symbol ?? string.Empty,
                IsDuty = lt.LeaveType1 == LeaveTypeEnum.Duty,
                IsHourly = lt.IsHourly,
                DecreaseFromLeaveId = lt.DecreaseFromLeaveID,
                GetInHolidays = lt.GetInHolidays,
                EachXHoursADayMinutes = dayMins,
                YearlyMaxMinutes = (int)Math.Round(lt.MaxDailyInYear * dayMins),
                MonthlyMaxMinutes = (int)Math.Round(lt.MaxDailyInMonth * dayMins),
                MaxMinusMinutes = (int)Math.Round(lt.MaxMinus * dayMins),
                IsPaid = lt.Payment
            };
        }

        private static WorkflowRequestItemDto MapRequest(Request r, T5V3DBManager t5, PersonnelSystem ps)
        {
            PersonnelUserInfo user = ps.DB.UserInfos.GetByID(r.BadgeNumber);
            int titleId = ps.DB.UserInfos.GetUserCurrentTitleID(r.BadgeNumber);

            TwfWorkflowType wfType;
            switch (r.WorkflowType)
            {
                case WorkflowOperations.Leave:
                    wfType = r.IsDaily ? TwfWorkflowType.DailyLeave : TwfWorkflowType.HourlyLeave;
                    break;
                case WorkflowOperations.Duty:
                    wfType = r.IsDaily ? TwfWorkflowType.DailyDuty : TwfWorkflowType.HourlyDuty;
                    break;
                case WorkflowOperations.InsertRecord:
                    wfType = TwfWorkflowType.Record;
                    break;
                case WorkflowOperations.LateEarly:
                    wfType = r.OverTimeAllowType == AllowType.Early ? TwfWorkflowType.Early : TwfWorkflowType.Late;
                    break;
                case WorkflowOperations.AllowOverTimes:
                case WorkflowOperations.EditOverTimes:
                    wfType = TwfWorkflowType.OverTime;
                    break;
                case WorkflowOperations.RewardAndPenalty:
                    wfType = TwfWorkflowType.RewardPenalty;
                    break;
                default:
                    wfType = TwfWorkflowType.DailyLeave;
                    break;
            }

            List<RequestAcceptMdl> accepts = t5.RequestAcceptDA.GetByID(r.RequestID);
            TwfRequestState state;
            if (accepts.Any(a => a.IsCancel))
            {
                state = TwfRequestState.Reject;
            }
            else if (!r.IsSuspend)
            {
                state = TwfRequestState.Accept;
            }
            else if (accepts.Count > 0)
            {
                state = TwfRequestState.FirstAccept;
            }
            else
            {
                state = TwfRequestState.Waiting;
            }

            return new WorkflowRequestItemDto
            {
                RequestId = r.RequestID,
                BadgeNumber = r.BadgeNumber,
                RequesterFullName = user != null ? user.ToString() : (r.RequesterName ?? string.Empty),
                RequesterTitleId = titleId,
                WorkflowType = wfType,
                RequestState = state,
                IsDaily = r.IsDaily,
                RequestDate = r.CreateTime,
                ForDate = r.ForDate,
                FromTime = r.FromTime,
                ToTime = r.ToTime,
                RecordTime = r.RecordTime,
                LeaveTypeId = r.LeaveID,
                LeaveDescriptionId = r.LeaveDescriptionID,
                OverTimeAllowType = (int)r.OverTimeAllowType,
                AllowValueDuration = r.AllowValue,
                Descriptions = r.Descriptions ?? string.Empty,
                IsSuspend = r.IsSuspend
            };
        }

        private static Request MapToT5Request(WorkflowRequestItemDto dto)
        {
            WorkflowOperations op;
            switch (dto.WorkflowType)
            {
                case TwfWorkflowType.DailyLeave:
                case TwfWorkflowType.HourlyLeave:
                    op = WorkflowOperations.Leave;
                    break;
                case TwfWorkflowType.DailyDuty:
                case TwfWorkflowType.HourlyDuty:
                    op = WorkflowOperations.Duty;
                    break;
                case TwfWorkflowType.Record:
                    op = WorkflowOperations.InsertRecord;
                    break;
                case TwfWorkflowType.Late:
                case TwfWorkflowType.Early:
                    op = WorkflowOperations.LateEarly;
                    break;
                case TwfWorkflowType.OverTime:
                    op = WorkflowOperations.AllowOverTimes;
                    break;
                case TwfWorkflowType.RewardPenalty:
                    op = WorkflowOperations.RewardAndPenalty;
                    break;
                default:
                    op = WorkflowOperations.Leave;
                    break;
            }

            return new Request
            {
                RequestID = dto.RequestId,
                BadgeNumber = dto.BadgeNumber,
                RequesterName = dto.RequesterFullName,
                WorkflowType = op,
                IsDaily = dto.IsDaily,
                CreateTime = dto.RequestDate == default(DateTime) ? DateTime.Now : dto.RequestDate,
                ForDate = dto.ForDate,
                FromTime = dto.FromTime,
                ToTime = dto.ToTime,
                RecordTime = dto.RecordTime,
                LeaveID = dto.LeaveTypeId,
                LeaveDescriptionID = dto.LeaveDescriptionId,
                OverTimeAllowType = (AllowType)dto.OverTimeAllowType,
                AllowValue = dto.AllowValueDuration,
                Descriptions = dto.Descriptions,
                IsSuspend = dto.IsSuspend
            };
        }
    }
}
