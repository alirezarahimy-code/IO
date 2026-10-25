using System;
using System.Collections.Generic;

namespace TWF.Domain.Models
{
    /// <summary>
    /// Represents a node in the T5 organizational chart (Personnel.DB.TitlesChart).
    /// </summary>
    public sealed class OrgTitleNodeDto
    {
        public int TitleId { get; set; }
        public string TitleName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int UpperTitleId { get; set; }
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public bool IsObserver { get; set; }
    }

    /// <summary>
    /// Represents an employee summary resolved through the T5 organizational hierarchy or deputy delegation.
    /// </summary>
    public sealed class EmployeeSummaryDto
    {
        public long BadgeNumber { get; set; }
        public string EmployeeNumber { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string CardNumber { get; set; } = string.Empty;
        public string NationalCode { get; set; } = string.Empty;
        public int TitleId { get; set; }
        public string TitleName { get; set; } = string.Empty;
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public int EmployeeTypeId { get; set; }
        public bool WebUserEnabled { get; set; } = true;
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// If this employee is visible to the manager via an active deputy delegation (PersonnelDeputy),
        /// indicates the principal manager's BadgeNumber; otherwise null.
        /// </summary>
        public long? ViaDeputyForBadgeNumber { get; set; }

        /// <summary>
        /// If visible via deputy delegation, indicates the principal manager's organizational title name.
        /// </summary>
        public string ViaDeputyForTitleName { get; set; } = string.Empty;

        public string FullName => $"{FirstName} {LastName}".Trim();

        public string DisplayNameWithBadge => $"{FullName} ({BadgeNumber})";
    }

    /// <summary>
    /// Represents a custom multi-step workflow rule in T5 (T5V3.Model.Workflow).
    /// </summary>
    public sealed class WorkflowStepConfigDto
    {
        public int WorkflowId { get; set; }
        public int TitleId { get; set; }
        public int AccepterTitleId { get; set; }
        public int Operation { get; set; } = -1; // -1 means all workflow operations
        public int OrderIndex { get; set; }
    }

    /// <summary>
    /// Represents an active or candidate deputy assignment combining T5 Deputy (Title-level)
    /// and PersonnelDeputy (User-level) tables.
    /// </summary>
    public sealed class DeputyAssignmentDto
    {
        public int Id { get; set; }
        public long PrincipalBadgeNumber { get; set; }
        public string PrincipalFullName { get; set; } = string.Empty;
        public int PrincipalTitleId { get; set; }
        public string PrincipalTitleName { get; set; } = string.Empty;
        public long DeputyBadgeNumber { get; set; }
        public string DeputyFullName { get; set; } = string.Empty;
        public int DeputyTitleId { get; set; }
        public string DeputyTitleName { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Represents a report definition and its title-based access permissions in T5 (Report &amp; ReportAccess).
    /// </summary>
    public sealed class ReportDefinitionDto
    {
        public int ReportId { get; set; }
        public string ReportName { get; set; } = string.Empty;
        public string ReportCategory { get; set; } = string.Empty;
        public string ReportFilePath { get; set; } = string.Empty;
        public List<int> AllowedTitleIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// Represents the active attendance rule resolved for an employee on a specific date
    /// (T5V3.Attendance.RuleProcessor.GetActiveRule).
    /// </summary>
    public sealed class ActiveAttendanceRuleDto
    {
        public int RuleId { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public bool AllowWebRequest { get; set; } = true;
        public bool RequireWorkflow { get; set; } = true;
        public bool AllowMobileApp { get; set; } = true;
        public bool RequireLocation { get; set; } = false;
        public bool RequirePhoto { get; set; } = false;
        public int LimitRecReqInDay { get; set; } = -1;
        public int LimitRecReqInMonth { get; set; } = -1;
        public int DailyRecordRequestLimit
        {
            get => LimitRecReqInDay;
            set => LimitRecReqInDay = value;
        }
        public int MonthlyRecordRequestLimit
        {
            get => LimitRecReqInMonth;
            set => LimitRecReqInMonth = value;
        }
        public int ConstOTMaxDailyMinutes { get; set; } = -1;
        public int ConstOTMaxMonthlyMinutes { get; set; } = -1;
        public int EachDayHoursMinutes { get; set; } = 440; // Default 7h 20m (440 mins)
    }
}
