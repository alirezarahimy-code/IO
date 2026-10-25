using System;
using System.Collections.Generic;
using T5Web.Domain.Models;

namespace T5Web.Domain.Interfaces
{
    /// <summary>
    /// Everything the portal needs from storage, expressed in domain terms.
    ///
    /// Two implementations exist:
    ///   * SqlAdminRepository   - the production one, Dapper over the shared T5 schema;
    ///   * InMemoryAdminRepository - used by the tests and by the offline demo.
    ///
    /// Deliberately synchronous: this portal runs on .NET Framework 4.7.2 / MVC 5
    /// where sync-over-async is the classic deadlock source. Dapper calls are short
    /// and index-backed; the connection is pooled.
    /// </summary>
    public interface IAdminRepository
    {
        // ---- session / authentication ----
        /// <summary>Loads a person by user name, including the stored password hash.</summary>
        PersonnelRow FindUserByUserName(string userName);

        /// <summary>Loads the effective permission rows for one person.</summary>
        IReadOnlyList<PermissionRow> GetPermissions(long badgeNumber);

        /// <summary>Loads the job title of a person (for display in the header).</summary>
        string GetTitleName(long badgeNumber);

        /// <summary>
        /// The active title id of a person ([UserInfoTitles] -> [TitlesChart]).
        ///
        /// Approval decisions are stamped with this id; it must never come from the
        /// request form, otherwise anybody could approve as the director.
        /// </summary>
        int GetTitleId(long badgeNumber);

        // ---- organisation ----
        IReadOnlyList<DepartmentNode> GetDepartments();

        IReadOnlyList<TitleNode> GetTitles();

        IReadOnlyList<GateRow> GetGates();

        /// <summary>Employment types used by the personnel editor ([EmpType]).</summary>
        IReadOnlyList<KeyValuePair<int, string>> GetEmpTypes();

        /// <summary>
        /// Reparents a department. The caller must have validated the cycle rule;
        /// storage only persists the new [UpperDeptID].
        /// </summary>
        OperationResult MoveDepartment(int departmentId, int newParentId);

        // ---- personnel ----
        IReadOnlyList<PersonnelRow> SearchPersonnel(string term, int? departmentId, bool includeFormer, int take);

        PersonnelRow GetPersonnel(long badgeNumber);

        OperationResult UpdatePersonnel(PersonnelRow row);

        // ---- devices ----
        IReadOnlyList<DeviceRow> GetDevices(int? gateId);

        OperationResult UpdateDevice(DeviceRow row);

        /// <summary>
        /// Queues one device command. The call is deliberately narrow: the closed set of
        /// commands is validated before this point, so the store only persists the intent
        /// and returns the new ticket id.
        /// </summary>
        OperationResult EnqueueDeviceCommand(DeviceCommandTicket ticket);

        /// <summary>The newest tickets, for the device page.</summary>
        IReadOnlyList<DeviceCommandTicket> GetDeviceCommands(int take);

        // ---- access levels ----
        OperationResult SetPermission(long badgeNumber, AdminFeature feature, AccessLevel level);

        // ---- workflow ----
        IReadOnlyList<WorkflowStep> GetWorkflow(int workflowType, int leaveTypeId);

        OperationResult SaveWorkflow(IReadOnlyList<WorkflowStep> steps, int workflowType, int leaveTypeId);

        /// <summary>
        /// How many requests are currently waiting in this chain.
        ///
        /// Rewriting a chain that has requests in flight strands them: their stored
        /// [RequestAccept] rows refer to steps that no longer exist, and the engine
        /// silently treats them as approved (legacy behaviour, BUG-21). The screen must
        /// therefore refuse - or demand an explicit purge - before saving.
        /// </summary>
        int CountInFlightRequests(int workflowType, int leaveTypeId);

        // ---- audit ----
        void AppendAudit(AuditEntry entry);

        IReadOnlyList<AuditEntry> GetAudit(int take, AdminFeature? feature);

        string GetLastAuditHash();

        // ---- diagnostics ----
        /// <summary>Number of rows per key table, used by the health page.</summary>
        IReadOnlyDictionary<string, int> GetTableCounts();

        bool CanConnect(out string error);
    }
}
