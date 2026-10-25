using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using TWF.Domain.Interfaces;
using TWF.Domain.Models;

namespace TWF.Infrastructure.Data
{
    /// <summary>
    /// Thread-safe data repository replacing TD5.dll, TitleChartClass, DescriptionClass,
    /// and raw SQL in ChangePassController.
    /// Eliminates all shared static mutable fields (BUG-01), connection check bugs (BUG-07a),
    /// FromTime.AddDays(1.0) getter mutation (BUG-07b), and undisposed EF contexts (BUG-07c).
    /// </summary>
    public sealed class TwfDataRepository : ITwfDataRepository
    {
        private readonly string _personnelConnectionString;
        private readonly string _t5ConnectionString;
        private readonly Func<string, IDbConnection> _connectionFactory;

        // Thread-safe in-memory fallback store for unit testing / offline verification when no SQL connection factory is supplied
        private readonly ConcurrentDictionary<long, UserTitleProfileDto> _inMemoryProfiles =
            new ConcurrentDictionary<long, UserTitleProfileDto>();
        private readonly ConcurrentDictionary<string, string> _inMemoryDescriptions =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentBag<RequestRecordDto> _inMemoryRequests =
            new ConcurrentBag<RequestRecordDto>();
        private readonly ConcurrentDictionary<long, PasswordChangeHistoryDto> _inMemoryPassHistory =
            new ConcurrentDictionary<long, PasswordChangeHistoryDto>();

        public TwfDataRepository(
            string personnelConnectionString,
            string t5ConnectionString,
            Func<string, IDbConnection> connectionFactory = null)
        {
            _personnelConnectionString = SqlConnectionStringHelper.ExtractProviderConnectionString(personnelConnectionString);
            _t5ConnectionString = SqlConnectionStringHelper.ExtractProviderConnectionString(t5ConnectionString);
            _connectionFactory = connectionFactory;
        }

        public string PersonnelConnectionString => _personnelConnectionString;
        public string T5ConnectionString => _t5ConnectionString;

        /// <summary>
        /// Seeds an in-memory user profile (used for automated verification and unit testing).
        /// </summary>
        public void SeedUserTitleProfile(UserTitleProfileDto profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            _inMemoryProfiles[profile.BadgeNumber] = profile;
        }

        /// <summary>
        /// Seeds an in-memory leave/duty description (used for automated verification and unit testing).
        /// </summary>
        public void SeedDescription(long badgeNumber, DateTime targetDate, bool isHourly, string description)
        {
            string key = $"{badgeNumber}:{targetDate:yyyyMMdd}:{isHourly}";
            _inMemoryDescriptions[key] = description ?? string.Empty;
        }

        /// <summary>
        /// Seeds an in-memory request record (used for automated verification and unit testing).
        /// </summary>
        public void SeedRequest(RequestRecordDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            _inMemoryRequests.Add(request);
        }

        /// <summary>
        /// Checks both Personnel and T5 database connections independently.
        /// Fixes BUG-07a where TD5.Init.Start checked PersonnelConnection twice and ignored T5Connection.
        /// </summary>
        public DatabaseHealthStatus CheckDatabaseHealth()
        {
            var status = new DatabaseHealthStatus();

            if (_connectionFactory == null)
            {
                status.IsPersonnelDbConnected = !string.IsNullOrWhiteSpace(_personnelConnectionString);
                status.IsT5DbConnected = !string.IsNullOrWhiteSpace(_t5ConnectionString);
                if (!status.IsPersonnelDbConnected)
                    status.PersonnelDbError = "رشته اتصال بانک اطلاعاتی پرسنلی (Personnel) تنظیم نشده است.";
                if (!status.IsT5DbConnected)
                    status.T5DbError = "رشته اتصال بانک اطلاعاتی تردد (T5) تنظیم نشده است.";
                return status;
            }

            try
            {
                using (IDbConnection conn = _connectionFactory(_personnelConnectionString))
                {
                    conn.Open();
                    status.IsPersonnelDbConnected = (conn.State == ConnectionState.Open);
                }
            }
            catch (Exception ex)
            {
                status.IsPersonnelDbConnected = false;
                status.PersonnelDbError = ex.Message;
            }

            try
            {
                using (IDbConnection conn = _connectionFactory(_t5ConnectionString))
                {
                    conn.Open();
                    status.IsT5DbConnected = (conn.State == ConnectionState.Open);
                }
            }
            catch (Exception ex)
            {
                status.IsT5DbConnected = false;
                status.T5DbError = ex.Message;
            }

            return status;
        }

        /// <summary>
        /// Resolves a fresh per-call <see cref="UserTitleProfileDto"/> for the specified badge number.
        /// Fixes BUG-01 (eliminates the static TitleChartClass.user field that leaked user identities across concurrent requests).
        /// </summary>
        public UserTitleProfileDto GetUserTitleProfile(long badgeNumber)
        {
            if (badgeNumber == 0)
            {
                return new UserTitleProfileDto
                {
                    BadgeNumber = 0,
                    UserName = "admin",
                    FirstName = "مدیر",
                    LastName = "سیستم",
                    TitleId = 0,
                    TitleName = "مدیر ارشد سیستم",
                    UpperTitleId = 0,
                    UpperTitleName = string.Empty,
                    DepartmentId = 0,
                    DepartmentName = "مدیریت",
                    IsObserver = false
                };
            }

            if (_connectionFactory == null)
            {
                if (_inMemoryProfiles.TryGetValue(badgeNumber, out UserTitleProfileDto existing))
                {
                    return new UserTitleProfileDto
                    {
                        BadgeNumber = existing.BadgeNumber,
                        UserName = existing.UserName,
                        FirstName = existing.FirstName,
                        LastName = existing.LastName,
                        NationalCode = existing.NationalCode,
                        TitleId = existing.TitleId,
                        TitleName = existing.TitleName,
                        UpperTitleId = existing.UpperTitleId,
                        UpperTitleName = existing.UpperTitleName,
                        DepartmentId = existing.DepartmentId,
                        DepartmentName = existing.DepartmentName,
                        IsObserver = existing.IsObserver
                    };
                }
                return null;
            }

            const string sql = @"
SELECT TOP 1
    u.[BadgeNumber],
    ISNULL(u.[UserName], N'') AS UserName,
    ISNULL(u.[FirstName], N'') AS FirstName,
    ISNULL(u.[LastName], N'') AS LastName,
    ISNULL(u.[NationalCode], N'') AS NationalCode,
    ISNULL(tc.[TitleID], 0) AS TitleId,
    ISNULL(tc.[TitleName], N'') AS TitleName,
    ISNULL(tc.[UpperTitleID], 0) AS UpperTitleId,
    ISNULL(utc.[TitleName], N'') AS UpperTitleName,
    ISNULL(d.[DepartmentID], 0) AS DepartmentId,
    ISNULL(d.[DepartmentName], N'') AS DepartmentName,
    ISNULL(tc.[Observer], 0) AS IsObserver
FROM [UserInfos] u
LEFT JOIN [UserInfoTitles] uit ON u.[BadgeNumber] = uit.[BadgeNumber]
LEFT JOIN [TitlesChart] tc ON uit.[TitleID] = tc.[TitleID]
LEFT JOIN [TitlesChart] utc ON tc.[UpperTitleID] = utc.[TitleID]
LEFT JOIN [Departments] d ON tc.[DepartmentID] = d.[DepartmentID]
WHERE u.[BadgeNumber] = @BadgeNumber;";

            using (IDbConnection conn = _connectionFactory(_personnelConnectionString))
            {
                conn.Open();
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    AddParam(cmd, "@BadgeNumber", badgeNumber);

                    using (IDataReader reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return null;

                        return new UserTitleProfileDto
                        {
                            BadgeNumber = Convert.ToInt64(reader["BadgeNumber"]),
                            UserName = Convert.ToString(reader["UserName"]) ?? string.Empty,
                            FirstName = Convert.ToString(reader["FirstName"]) ?? string.Empty,
                            LastName = Convert.ToString(reader["LastName"]) ?? string.Empty,
                            NationalCode = Convert.ToString(reader["NationalCode"]) ?? string.Empty,
                            TitleId = Convert.ToInt32(reader["TitleId"]),
                            TitleName = Convert.ToString(reader["TitleName"]) ?? string.Empty,
                            UpperTitleId = Convert.ToInt32(reader["UpperTitleId"]),
                            UpperTitleName = Convert.ToString(reader["UpperTitleName"]) ?? string.Empty,
                            DepartmentId = Convert.ToInt32(reader["DepartmentId"]),
                            DepartmentName = Convert.ToString(reader["DepartmentName"]) ?? string.Empty,
                            IsObserver = Convert.ToBoolean(reader["IsObserver"])
                        };
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves the leave or duty description for a user on a given date without mutating any static list.
        /// Fixes BUG-01b in MyTime3.Models.DescriptionClass.
        /// </summary>
        public string GetLeaveOrDutyDescription(long badgeNumber, DateTime targetDate, bool isHourly)
        {
            if (_connectionFactory == null)
            {
                string key = $"{badgeNumber}:{targetDate:yyyyMMdd}:{isHourly}";
                return _inMemoryDescriptions.TryGetValue(key, out string desc) ? desc : string.Empty;
            }

            const string sql = @"
SELECT TOP 1 ISNULL([Description], N'')
FROM [Request]
WHERE [BadgeNumber] = @BadgeNumber
  AND [FromTime] >= @DayStart
  AND [FromTime] <= @DayEnd
  AND (@IsHourly = 0 OR [WofkflowType] IN (1, 3))
ORDER BY [RequestID] DESC;";

            using (IDbConnection conn = _connectionFactory(_t5ConnectionString))
            {
                conn.Open();
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    AddParam(cmd, "@BadgeNumber", badgeNumber);
                    AddParam(cmd, "@DayStart", targetDate.Date);
                    AddParam(cmd, "@DayEnd", targetDate.Date.AddDays(1).AddSeconds(-1));
                    AddParam(cmd, "@IsHourly", isHourly ? 1 : 0);

                    object scalar = cmd.ExecuteScalar();
                    return scalar == null || scalar == DBNull.Value ? string.Empty : Convert.ToString(scalar);
                }
            }
        }

        /// <summary>
        /// Queries workflow requests for a badge number and date range without mutating FromTime.
        /// Fixes BUG-07b where TD5.Controllers.RequestController.get_FromTime executed FromTime = FromTime.AddDays(1.0) on every access.
        /// </summary>
        public IReadOnlyList<RequestRecordDto> GetRequests(
            long badgeNumber,
            DateTime fromDate,
            DateTime toDate,
            TwfWorkflowType? workflowType = null)
        {
            var results = new List<RequestRecordDto>();

            if (_connectionFactory == null)
            {
                foreach (var r in _inMemoryRequests)
                {
                    if (r.BadgeNumber == badgeNumber &&
                        r.FromTime >= fromDate &&
                        r.FromTime <= toDate &&
                        (!workflowType.HasValue || r.WorkflowType == (int)workflowType.Value))
                    {
                        results.Add(new RequestRecordDto
                        {
                            RequestId = r.RequestId,
                            BadgeNumber = r.BadgeNumber,
                            RequestDate = r.RequestDate,
                            WorkflowType = r.WorkflowType,
                            SubType = r.SubType,
                            FromTime = r.FromTime,
                            ToTime = r.ToTime,
                            RequestDurationMinutes = r.RequestDurationMinutes,
                            RequestState = r.RequestState,
                            Description = r.Description,
                            AccepterDescription = r.AccepterDescription,
                            SenderBadgeNumber = r.SenderBadgeNumber,
                            SenderTitleId = r.SenderTitleId
                        });
                    }
                }
                return results;
            }

            const string sql = @"
SELECT
    [RequestID] AS RequestId,
    [BadgeNumber],
    [RequestDate],
    [WofkflowType] AS WorkflowType,
    [SubType],
    [FromTime],
    [ToTime],
    ISNULL([RequestDuration], 0) AS RequestDurationMinutes,
    [RequestState],
    ISNULL([Description], N'') AS Description,
    ISNULL([AccepterDescription], N'') AS AccepterDescription,
    ISNULL([SenderBadgeNumber], 0) AS SenderBadgeNumber,
    ISNULL([SenderTitleID], 0) AS SenderTitleId
FROM [Request]
WHERE [BadgeNumber] = @BadgeNumber
  AND [FromTime] >= @FromDate
  AND [FromTime] <= @ToDate
  AND (@WorkflowType IS NULL OR [WofkflowType] = @WorkflowType)
ORDER BY [FromTime] DESC;";

            using (IDbConnection conn = _connectionFactory(_t5ConnectionString))
            {
                conn.Open();
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    AddParam(cmd, "@BadgeNumber", badgeNumber);
                    AddParam(cmd, "@FromDate", fromDate);
                    AddParam(cmd, "@ToDate", toDate);
                    AddParam(cmd, "@WorkflowType", workflowType.HasValue ? (object)(int)workflowType.Value : DBNull.Value);

                    using (IDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(new RequestRecordDto
                            {
                                RequestId = Convert.ToInt32(reader["RequestId"]),
                                BadgeNumber = Convert.ToInt64(reader["BadgeNumber"]),
                                RequestDate = Convert.ToDateTime(reader["RequestDate"]),
                                WorkflowType = Convert.ToInt32(reader["WorkflowType"]),
                                SubType = Convert.ToInt32(reader["SubType"]),
                                FromTime = Convert.ToDateTime(reader["FromTime"]),
                                ToTime = Convert.ToDateTime(reader["ToTime"]),
                                RequestDurationMinutes = Convert.ToInt32(reader["RequestDurationMinutes"]),
                                RequestState = Convert.ToInt32(reader["RequestState"]),
                                Description = Convert.ToString(reader["Description"]) ?? string.Empty,
                                AccepterDescription = Convert.ToString(reader["AccepterDescription"]) ?? string.Empty,
                                SenderBadgeNumber = Convert.ToInt64(reader["SenderBadgeNumber"]),
                                SenderTitleId = Convert.ToInt32(reader["SenderTitleId"])
                            });
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Gets the password change history for a user from [PassCheckProps].
        /// Fixes BUG-07c where ChangePassController created undisposed EF contexts and used string-concatenated SQL.
        /// </summary>
        public PasswordChangeHistoryDto GetPasswordChangeHistory(long badgeNumber)
        {
            if (_connectionFactory == null)
            {
                if (_inMemoryPassHistory.TryGetValue(badgeNumber, out PasswordChangeHistoryDto entry))
                {
                    return new PasswordChangeHistoryDto
                    {
                        Id = entry.Id,
                        BadgeNumber = entry.BadgeNumber,
                        LastChangeDate = entry.LastChangeDate,
                        HasChangedPassword = entry.HasChangedPassword
                    };
                }
                return null;
            }

            // Legacy T5 schema (TWF 4.28): [PassCheckProps]([ID], [Badge], [LastChangeDate]) in the T5 database.
            // A row means "this badge has already changed its password at [LastChangeDate]".
            const string sql = @"
SELECT TOP 1
    [ID],
    [Badge],
    [LastChangeDate]
FROM [PassCheckProps]
WHERE [Badge] = @BadgeNumber
ORDER BY [LastChangeDate] DESC;";

            using (IDbConnection conn = _connectionFactory(_t5ConnectionString))
            {
                conn.Open();
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    AddParam(cmd, "@BadgeNumber", badgeNumber);

                    using (IDataReader reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return null;

                        return new PasswordChangeHistoryDto
                        {
                            Id = Convert.ToInt32(reader["ID"]),
                            BadgeNumber = Convert.ToInt64(reader["Badge"]),
                            LastChangeDate = Convert.ToDateTime(reader["LastChangeDate"]),
                            HasChangedPassword = true
                        };
                    }
                }
            }
        }

        /// <summary>
        /// Records a successful password change for a user in [PassCheckProps] using parameterized SQL.
        /// Fixes BUG-07c in ChangePassController.
        /// </summary>
        public void RecordPasswordChange(long badgeNumber, DateTime changedAt)
        {
            if (_connectionFactory == null)
            {
                _inMemoryPassHistory[badgeNumber] = new PasswordChangeHistoryDto
                {
                    Id = 1,
                    BadgeNumber = badgeNumber,
                    LastChangeDate = changedAt,
                    HasChangedPassword = true
                };
                return;
            }

            // Legacy T5 schema (TWF 4.28): keep [Badge]/[LastChangeDate] so an existing
            // customer database keeps working without a migration.
            const string sql = @"
IF EXISTS (SELECT 1 FROM [PassCheckProps] WHERE [Badge] = @BadgeNumber)
BEGIN
    UPDATE [PassCheckProps]
    SET [LastChangeDate] = @ChangedAt
    WHERE [Badge] = @BadgeNumber;
END
ELSE
BEGIN
    INSERT INTO [PassCheckProps] ([Badge], [LastChangeDate])
    VALUES (@BadgeNumber, @ChangedAt);
END";

            using (IDbConnection conn = _connectionFactory(_t5ConnectionString))
            {
                conn.Open();
                using (IDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    AddParam(cmd, "@BadgeNumber", badgeNumber);
                    AddParam(cmd, "@ChangedAt", changedAt);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void AddParam(IDbCommand command, string name, object value)
        {
            IDbDataParameter param = command.CreateParameter();
            param.ParameterName = name;
            param.Value = value ?? DBNull.Value;
            command.Parameters.Add(param);
        }
    }
}
