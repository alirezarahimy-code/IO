using System;
using System.Collections.Generic;
using TWF.Domain.Models;

namespace TWF.Domain.Interfaces
{
    /// <summary>
    /// Thread-safe data access contract replacing TD5.dll, TitleChartClass, DescriptionClass,
    /// and raw SQL queries in controllers.
    /// </summary>
    public interface ITwfDataRepository
    {
        /// <summary>
        /// Checks connectivity to both Personnel and T5 databases (fixes BUG-07a in TD5.Init.Start).
        /// </summary>
        DatabaseHealthStatus CheckDatabaseHealth();

        /// <summary>
        /// Resolves the active user and organizational title profile for a badge number without static state (fixes BUG-01).
        /// </summary>
        UserTitleProfileDto GetUserTitleProfile(long badgeNumber);

        /// <summary>
        /// Resolves the leave or duty description for a user on a given date without mutating a static list (fixes BUG-01b).
        /// </summary>
        string GetLeaveOrDutyDescription(long badgeNumber, DateTime targetDate, bool isHourly);

        /// <summary>
        /// Queries workflow requests for a badge number and date range without mutating FromTime (fixes BUG-07b).
        /// </summary>
        IReadOnlyList<RequestRecordDto> GetRequests(
            long badgeNumber,
            DateTime fromDate,
            DateTime toDate,
            TwfWorkflowType? workflowType = null);

        /// <summary>
        /// Gets the password change history for a user from [PassCheckProps] (fixes BUG-07c).
        /// </summary>
        PasswordChangeHistoryDto GetPasswordChangeHistory(long badgeNumber);

        /// <summary>
        /// Records a successful password change for a user in [PassCheckProps] using parameterized SQL (fixes BUG-07c).
        /// </summary>
        void RecordPasswordChange(long badgeNumber, DateTime changedAt);
    }
}
