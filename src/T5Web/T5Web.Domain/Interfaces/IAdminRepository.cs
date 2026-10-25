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

        // ---- access levels ----
        OperationResult SetPermission(long badgeNumber, AdminFeature feature, AccessLevel level);

        // ---- workflow ----
        IReadOnlyList<WorkflowStep> GetWorkflow(int workflowType, int leaveTypeId);

        OperationResult SaveWorkflow(IReadOnlyList<WorkflowStep> steps, int workflowType, int leaveTypeId);

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
