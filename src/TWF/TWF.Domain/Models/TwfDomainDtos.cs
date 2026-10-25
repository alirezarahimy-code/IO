using System;

namespace TWF.Domain.Models
{
    /// <summary>
    /// Workflow request types matching T5V3.Model.WorkflowType.
    /// </summary>
    public enum TwfWorkflowType
    {
        DailyLeave = 0,
        Leave = 0,
        HourlyLeave = 1,
        DailyDuty = 2,
        Duty = 2,
        HourlyDuty = 3,
        Record = 4,
        Late = 5,
        LateEarly = 5,
        Early = 6,
        OverTime = 7,
        AllowOverTimes = 7,
        RewardPenalty = 8,
        RewardAndPenalty = 8,
        ConstOT = 9
    }

    /// <summary>
    /// Request approval states matching T5V3.Model.RequestState.
    /// </summary>
    public enum TwfRequestState
    {
        UnKnown = 0,
        Accept = 1,
        Reject = 2,
        Waiting = 3,
        FirstAccept = 4
    }

    /// <summary>
    /// Immutable per-call user & organizational title profile.
    /// Replaces the thread-unsafe static MyTime3.Models.TitleChartClass.user (BUG-01).
    /// </summary>
    public sealed class UserTitleProfileDto
    {
        public long BadgeNumber { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string NationalCode { get; set; } = string.Empty;
        public int TitleId { get; set; }
        public string TitleName { get; set; } = string.Empty;
        public int UpperTitleId { get; set; }
        public string UpperTitleName { get; set; } = string.Empty;
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public bool IsObserver { get; set; }
    }

    /// <summary>
    /// Clean DTO for workflow requests stored in the T5 [Request] table.
    /// Replaces TD5.Controllers.RequestController (which had the destructive FromTime.AddDays(1.0) getter bug - BUG-07b).
    /// </summary>
    public sealed class RequestRecordDto
    {
        public int RequestId { get; set; }
        public long BadgeNumber { get; set; }
        public DateTime RequestDate { get; set; }
        public int WorkflowType { get; set; }
        public int SubType { get; set; }
        public DateTime FromTime { get; set; }
        public DateTime ToTime { get; set; }
        public int RequestDurationMinutes { get; set; }
        public int RequestState { get; set; }
        public string Description { get; set; } = string.Empty;
        public string AccepterDescription { get; set; } = string.Empty;
        public long SenderBadgeNumber { get; set; }
        public int SenderTitleId { get; set; }
    }

    /// <summary>
    /// Represents password change history stored in the Personnel [PassCheckProps] table.
    /// </summary>
    public sealed class PasswordChangeHistoryDto
    {
        public int Id { get; set; }
        public long BadgeNumber { get; set; }
        public DateTime LastChangeDate { get; set; }
        public bool HasChangedPassword { get; set; }
    }

    /// <summary>
    /// Health status of both Personnel and T5 databases.
    /// Fixes BUG-07a where TD5.Init.Start checked PersonnelConnection twice and ignored T5Connection.
    /// </summary>
    public sealed class DatabaseHealthStatus
    {
        public bool IsPersonnelDbConnected { get; set; }
        public bool IsT5DbConnected { get; set; }
        public string PersonnelDbError { get; set; } = string.Empty;
        public string T5DbError { get; set; } = string.Empty;
        public bool IsHealthy => IsPersonnelDbConnected && IsT5DbConnected;
    }
}
