using System;
using System.Collections.Generic;
using TWF.Domain.Models;

namespace TWF.Domain.Interfaces
{
    /// <summary>
    /// Unified adapter interface for T5 (T5V3.DB / T5V3.Attendance / T5V3.LeaveCalcs) and Personnel (Personnel.DB)
    /// operations required by TWF.
    /// Allows 100% deterministic unit/integration testing and eliminates direct static singleton coupling in controllers.
    /// </summary>
    public interface IT5BackendAdapter
    {
        // --- Authentication & Password Management (Personnel DB) ---
        bool VerifyAdminPassword(string password);
        long? AuthenticateEmployee(string userName, string password);
        bool ChangeAdminPassword(string oldPassword, string newPassword);
        bool ChangeEmployeePassword(long badgeNumber, string oldPassword, string newPassword);

        // --- Organizational Hierarchy & Personnel (Personnel DB: TitlesChart, UserInfos, UserInfoTitles) ---
        IReadOnlyList<OrgTitleNodeDto> GetAllTitleNodes();
        OrgTitleNodeDto GetTitleNodeById(int titleId);
        int GetEmployeeCurrentTitleId(long badgeNumber);
        int GetCurrentTitleIdForEmployee(long badgeNumber);
        IReadOnlyList<int> GetSubordinateTitleIds(int managerTitleId, TwfWorkflowType? operation = null);
        IReadOnlyDictionary<long, int> GetAllUserCurrentTitleMap();
        IReadOnlyDictionary<int, int> GetTitleUpperMap();
        EmployeeSummaryDto GetEmployeeByBadge(long badgeNumber);
        IReadOnlyList<EmployeeSummaryDto> GetEmployeesByTitleIds(IEnumerable<int> titleIds);
        IReadOnlyList<EmployeeSummaryDto> GetGuardAccessiblePersonnel(long observerBadgeNumber, int observerTitleId);

        // --- Multi-Step Workflow Configuration & Two-Layer Deputy (T5 DB: Workflow, Deputy, PersonnelDeputy) ---
        IReadOnlyList<WorkflowStepConfigDto> GetWorkflowStepsForTitle(int requesterTitleId);
        IReadOnlyList<WorkflowStepConfigDto> GetWorkflowSteps(int requesterTitleId, TwfWorkflowType operation);
        IReadOnlyList<int> GetAllowedDeputyTitleIds(int principalTitleId);
        IReadOnlyList<long> GetPrincipalBadgesForDeputy(long deputyBadgeNumber);
        IReadOnlyList<long> GetPrincipalsWhereEmployeeIsDeputy(long deputyBadgeNumber);
        long? GetActiveDeputyForPrincipal(long principalBadgeNumber);
        IReadOnlyList<DeputyAssignmentDto> GetDeputyAssignmentsForPrincipal(long principalBadgeNumber);
        DeputyAssignmentDto AssignPersonnelDeputy(long principalBadgeNumber, long deputyBadgeNumber);
        bool RemovePersonnelDeputy(long principalBadgeNumber, long deputyBadgeNumber);

        // --- Report Access & Attendance Rules (T5 DB: Report, ReportAccess, RuleProcessor) ---
        IReadOnlyList<ReportDefinitionDto> GetAllReports();
        ActiveAttendanceRuleDto GetActiveRule(long badgeNumber, DateTime targetDate);

        // --- Leave Types & Leave Balances (T5 DB: LeaveType, DailyLeaves, HourlyLeaves, LeaveCalcs) ---
        IReadOnlyList<LeaveTypeDefinitionDto> GetAllLeaveTypes();
        LeaveTypeDefinitionDto GetLeaveTypeById(int leaveTypeId);
        int GetAnnualLeaveEntitlementMinutes(long badgeNumber, int leaveTypeId, int persianYear);
        IReadOnlyList<DailyLeaveRecordDto> GetApprovedDailyLeaves(long badgeNumber, DateTime fromDate, DateTime toDate);
        IReadOnlyList<HourlyLeaveRecordDto> GetApprovedHourlyLeaves(long badgeNumber, DateTime fromDate, DateTime toDate);

        // --- Workflow Requests & Operational Tables (T5 DB: Request, RequestAccept, DailyLeaves, HourlyLeaves, EditedValue, AllowValue, Record) ---
        WorkflowRequestItemDto GetRequestById(long requestId);
        IReadOnlyList<WorkflowRequestItemDto> GetAllRequests();
        IReadOnlyList<WorkflowRequestItemDto> GetRequestsByEmployee(long badgeNumber);
        IReadOnlyList<WorkflowRequestItemDto> GetEmployeeRequests(long badgeNumber, bool suspendedOnly = false);
        IReadOnlyList<WorkflowRequestItemDto> GetEmployeeRequestsInPeriod(long badgeNumber, DateTime fromDate, DateTime toDate);
        bool IsRequestEditableByEmployee(long requestId);
        long AddRequest(WorkflowRequestItemDto request);
        bool CreateRequest(WorkflowRequestItemDto request, out string error, out long newRequestId);
        void UpdateRequest(WorkflowRequestItemDto request);
        bool UpdateRequest(WorkflowRequestItemDto request, out string error);
        bool DeleteRequest(long requestId);
        bool DeleteRequest(long requestId, out string error);

        IReadOnlyList<KeyValuePair<int, int>> GetRemainingRequestAccepters(long requestId);
        IReadOnlyList<KeyValuePair<int, int>> GetPendingRequestAccepterTitles(long requestId);
        IReadOnlyList<KeyValuePair<long, int>> GetPendingRequestAccepterTitles();
        IReadOnlyList<RequestAcceptStepDto> GetRequestAcceptHistory(long requestId);
        void AddRequestAcceptStep(RequestAcceptStepDto step);
        bool AddRequestAcceptStep(RequestAcceptStepDto step, out string error);
        bool DeleteRequestAcceptSteps(long requestId, long? accepterBadgeNumber = null);
        IReadOnlyList<KeyValuePair<long, bool>> GetRequestIdsForUndo(long accepterBadgeNumber, DateTime minDate);

        // --- Operational Table Sync (Called on Final Approval / Undo) ---
        void InsertApprovedOperationalData(WorkflowRequestItemDto request, long approverBadgeNumber);
        bool InsertOperationalDataForApprovedRequest(WorkflowRequestItemDto request, out string error);
        void DeleteApprovedOperationalData(WorkflowRequestItemDto request, long undoerBadgeNumber);
        int GetOperationalRecordsCount(long badgeNumber, TwfWorkflowType workflowType, DateTime forDate);
        bool UpsertConstantOvertime(long badgeNumber, DateTime fromDate, DateTime toDate, int minutes, out string error);
        bool UpsertConstantOvertime(long badgeNumber, DateTime forDate, int minutes);
        int GetConstantOvertimeMinutes(long badgeNumber, DateTime fromDate, DateTime toDate);
    }
}
