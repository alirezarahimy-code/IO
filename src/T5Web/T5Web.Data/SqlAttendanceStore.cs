using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// Attendance, requests and the monthly summary over the shared T5 database.
    ///
    /// Every column named here is verified against db/schema-manifest.json by
    /// tools/verify-sql-schema.py, and every filter value is a Dapper parameter. The
    /// date range is applied to [Record].[CheckTime], which the engine's own entity
    /// types as datetime (engine/column-types.tsv) - so this is not a guess.
    /// </summary>
    public sealed class SqlAttendanceStore : IAttendanceStore
    {
        private readonly Func<IDbConnection> _connectionFactory;
        private readonly PersonnelDirectory _personnel;

        public SqlAttendanceStore(Func<IDbConnection> connectionFactory, PersonnelDirectory personnel = null)
        {
            if (connectionFactory == null)
            {
                throw new ArgumentNullException(nameof(connectionFactory));
            }
            _connectionFactory = connectionFactory;
            // Records, requests and leave ledgers live in the engine catalog; names and
            // departments live in the Personnel catalog (docs/09). Never joined in SQL.
            _personnel = personnel ?? new PersonnelDirectory(connectionFactory);
        }

        private IDbConnection Open()
        {
            IDbConnection connection = _connectionFactory();
            connection.Open();
            return connection;
        }

        public IReadOnlyList<RecordRow> QueryRecords(RecordQuery query)
        {
            RecordQuery q = (query ?? new RecordQuery()).Clamped();

            const string sql = @"
SELECT TOP (@Take)
    r.[BadgeNumber]      AS BadgeNumber,
    r.[CheckTime]        AS CheckTime,
    r.[CheckType]        AS CheckType,
    r.[MachineNumber]    AS MachineNumber,
    r.[IsValid]          AS IsValid,
    r.[OriginalSCheckType] AS OriginalCheckType,
    r.[WorkCode]         AS WorkCode,
    r.[InsertUser]       AS InsertUser,
    r.[InsertTime]       AS InsertTime,
    CAST('' AS nvarchar(1)) AS PersonName,
    CAST('' AS nvarchar(1)) AS MachineName
FROM [Record] r
WHERE (@BadgeNumber IS NULL OR r.[BadgeNumber] = @BadgeNumber)
  AND (@MachineNumber IS NULL OR r.[MachineNumber] = @MachineNumber)
  AND (@FromDate IS NULL OR r.[CheckTime] >= @FromDate)
  AND (@ToDate IS NULL OR r.[CheckTime] < @ToDate)
  AND (@OnlyInvalid = 0 OR r.[IsValid] = 0)
  AND (@OnlyManual = 0 OR r.[InsertUser] > 0)
ORDER BY r.[CheckTime] DESC;";

            List<RecordRow> rows;
            using (IDbConnection connection = Open())
            {
                rows = connection.Query<RecordRow>(sql, new
                {
                    q.Take,
                    BadgeNumber = q.BadgeNumber,
                    MachineNumber = q.MachineNumber,
                    FromDate = q.FromDate,
                    ToDate = q.ToDate.HasValue ? q.ToDate.Value.Date.AddDays(1) : (DateTime?)null,
                    OnlyInvalid = q.OnlyInvalid ? 1 : 0,
                    OnlyManual = q.OnlyManual ? 1 : 0
                }).ToList();
            }

            if (rows.Count > 0)
            {
                var names = _personnel.Names(rows.Select(r => r.BadgeNumber));
                var machines = _personnel.MachineNames(rows.Select(r => r.MachineNumber));
                foreach (RecordRow row in rows)
                {
                    string name, machine;
                    row.PersonName = names.TryGetValue(row.BadgeNumber, out name) ? name : string.Empty;
                    row.MachineName = machines.TryGetValue(row.MachineNumber, out machine) ? machine : string.Empty;
                }
            }
            return rows;
        }

        public OperationResult SaveRecord(RecordRow row, bool isNew)
        {
            if (row == null)
            {
                return OperationResult.Fail("سطر تردد خالی است.");
            }
            if (row.BadgeNumber <= 0)
            {
                return OperationResult.Fail("شماره پرسنلی معتبر نیست.");
            }
            if (row.CheckTime.Year < 1990 || row.CheckTime.Year > 2200)
            {
                return OperationResult.Fail("زمان تردد معتبر نیست.");
            }
            if (row.CheckType < 0 || row.CheckType > 5)
            {
                return OperationResult.Fail("نوع تردد باید بین ۰ تا ۵ باشد.");
            }

            try
            {
                using (IDbConnection connection = Open())
                {
                    if (isNew)
                    {
                        const string exists = @"
SELECT COUNT(1) FROM [Record]
WHERE [BadgeNumber] = @BadgeNumber AND [CheckTime] = @CheckTime AND [CheckType] = @CheckType;";
                        if (connection.ExecuteScalar<int>(exists, new
                        {
                            row.BadgeNumber,
                            row.CheckTime,
                            row.CheckType
                        }) > 0)
                        {
                            return OperationResult.Fail("این تردد از قبل ثبت شده است (پرسنل، زمان و نوع تکراری است).");
                        }

                        const string insert = @"
INSERT INTO [Record]
    ([BadgeNumber], [CheckTime], [CheckType], [MachineNumber], [IsValid],
     [OriginalSCheckType], [WorkCode], [InsertUser], [InsertTime])
VALUES
    (@BadgeNumber, @CheckTime, @CheckType, @MachineNumber, @IsValid,
     @OriginalCheckType, @WorkCode, @InsertUser, @InsertTime);";
                        connection.Execute(insert, new
                        {
                            row.BadgeNumber,
                            row.CheckTime,
                            row.CheckType,
                            row.MachineNumber,
                            row.IsValid,
                            OriginalCheckType = row.CheckType,
                            row.WorkCode,
                            row.InsertUser,
                            InsertTime = DateTime.UtcNow
                        });
                        return OperationResult.Ok();
                    }

                    // Correcting a punch: the triple identifies the row, and the portal
                    // changes only what an administrator is allowed to change.
                    const string update = @"
UPDATE [Record]
SET [IsValid] = @IsValid,
    [CheckType] = @CheckType
WHERE [BadgeNumber] = @BadgeNumber AND [CheckTime] = @CheckTime AND [CheckType] = @OriginalCheckType;";

                    int affected = connection.Execute(update, new
                    {
                        row.IsValid,
                        row.CheckType,
                        row.BadgeNumber,
                        row.CheckTime,
                        row.OriginalCheckType
                    });
                    return affected == 1
                        ? OperationResult.Ok()
                        : OperationResult.Fail("تردد مورد نظر یافت نشد (ممکن است هم‌زمان تغییر کرده باشد).");
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("ثبت تردد ناموفق بود: " + FirstLine(ex.Message));
            }
        }

        public IReadOnlyList<RequestRow> QueryRequests(bool pendingOnly, long? badgeNumber, int take)
        {
            const string sql = @"
SELECT TOP (@Take)
    q.[RequestID]        AS RequestId,
    q.[BadgeNumber]      AS BadgeNumber,
    q.[LeaveID]          AS LeaveId,
    q.[LeaveDescriptionID] AS LeaveDescriptionId,
    q.[Descriptions]     AS DescriptionText,
    q.[ForDate]          AS ForDate,
    q.[FromTime]         AS FromTime,
    q.[ToTime]           AS ToTime,
    q.[CreateDate]       AS CreateTime,
    q.[IsDaily]          AS IsDaily,
    q.[IsSuspend]        AS IsSuspended,
    q.[WofkflowType]     AS WorkflowType,
    CAST('' AS nvarchar(1)) AS PersonName,
    ISNULL(lt.[LeaveName], N'') AS LeaveName,
    (SELECT COUNT(1) FROM [RequestAccept] a WHERE a.[RequestID] = q.[RequestID]) AS AcceptedCount,
    (SELECT COUNT(1) FROM [Workflow] w
      WHERE w.[WorkflowType] = q.[WofkflowType] AND w.[LeaveTypeID] = q.[LeaveID]) AS StepCount
FROM [Request] q
LEFT JOIN [LeaveType] lt ON lt.[LeaveID] = q.[LeaveID]
WHERE (@BadgeNumber IS NULL OR q.[BadgeNumber] = @BadgeNumber)
  AND (@PendingOnly = 0 OR (q.[IsSuspend] = 0
       AND (SELECT COUNT(1) FROM [Workflow] w
             WHERE w.[WorkflowType] = q.[WofkflowType] AND w.[LeaveTypeID] = q.[LeaveID])
           > (SELECT COUNT(1) FROM [RequestAccept] a WHERE a.[RequestID] = q.[RequestID])))
ORDER BY q.[RequestID] DESC;";

            List<RequestRow> rows;
            using (IDbConnection connection = Open())
            {
                rows = connection.Query<RequestRow>(sql, new
                {
                    Take = take <= 0 ? 100 : Math.Min(take, 500),
                    BadgeNumber = badgeNumber,
                    PendingOnly = pendingOnly ? 1 : 0
                }).ToList();
            }
            return FillRequestNames(rows);
        }

        public RequestRow GetRequest(long requestId)
        {
            const string sql = @"
SELECT TOP 1
    q.[RequestID]        AS RequestId,
    q.[BadgeNumber]      AS BadgeNumber,
    q.[LeaveID]          AS LeaveId,
    q.[LeaveDescriptionID] AS LeaveDescriptionId,
    q.[Descriptions]     AS DescriptionText,
    q.[ForDate]          AS ForDate,
    q.[FromTime]         AS FromTime,
    q.[ToTime]           AS ToTime,
    q.[CreateDate]       AS CreateTime,
    q.[IsDaily]          AS IsDaily,
    q.[IsSuspend]        AS IsSuspended,
    q.[WofkflowType]     AS WorkflowType,
    CAST('' AS nvarchar(1)) AS PersonName,
    ISNULL(lt.[LeaveName], N'') AS LeaveName,
    (SELECT COUNT(1) FROM [RequestAccept] a WHERE a.[RequestID] = q.[RequestID]) AS AcceptedCount,
    (SELECT COUNT(1) FROM [Workflow] w
      WHERE w.[WorkflowType] = q.[WofkflowType] AND w.[LeaveTypeID] = q.[LeaveID]) AS StepCount
FROM [Request] q
LEFT JOIN [LeaveType] lt ON lt.[LeaveID] = q.[LeaveID]
WHERE q.[RequestID] = @RequestId;";

            RequestRow row;
            using (IDbConnection connection = Open())
            {
                row = connection.QueryFirstOrDefault<RequestRow>(sql, new { RequestId = requestId });
            }
            if (row == null)
            {
                return null;
            }
            FillRequestNames(new List<RequestRow> { row });
            return row;
        }

        /// <summary>
        /// Completes the display-only fields the legacy query joined in from the Personnel
        /// catalog. A missing person yields an empty name, never a null row.
        /// </summary>
        private List<RequestRow> FillRequestNames(List<RequestRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return rows ?? new List<RequestRow>();
            }
            var names = _personnel.Names(rows.Select(r => r.BadgeNumber));
            foreach (RequestRow row in rows)
            {
                string name;
                row.PersonName = names.TryGetValue(row.BadgeNumber, out name) ? name : string.Empty;
            }
            return rows;
        }

        public OperationResult DecideRequest(long requestId, long accepterBadgeNumber, int accepterTitleId,
                                             bool cancel, bool isDeputy, out string detail)
        {
            detail = string.Empty;

            using (IDbConnection connection = Open())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    int suspended = connection.ExecuteScalar<int>(
                        @"SELECT ISNULL(MAX(CASE WHEN [IsSuspend] = 1 THEN 1 ELSE 0 END), 0)
                          FROM [Request] WHERE [RequestID] = @RequestId;",
                        new { RequestId = requestId }, transaction);

                    if (suspended == 1)
                    {
                        transaction.Rollback();
                        return OperationResult.Fail("این درخواست قبلاً لغو شده است.");
                    }

                    int accepted = connection.ExecuteScalar<int>(
                        @"SELECT COUNT(1) FROM [RequestAccept] WHERE [RequestID] = @RequestId;",
                        new { RequestId = requestId }, transaction);

                    if (accepted == 0 && requestId <= 0)
                    {
                        transaction.Rollback();
                        return OperationResult.Fail("درخواست یافت نشد.");
                    }

                    // One decision per title, ever. The legacy screen allowed a second
                    // approval by the same title, which double-counted a step.
                    int alreadyByTitle = connection.ExecuteScalar<int>(
                        @"SELECT COUNT(1) FROM [RequestAccept]
                          WHERE [RequestID] = @RequestId AND [AccepterTitleID] = @AccepterTitleID;",
                        new { RequestId = requestId, AccepterTitleID = accepterTitleId }, transaction);

                    if (alreadyByTitle > 0)
                    {
                        transaction.Rollback();
                        return OperationResult.Fail("این مرحله قبلاً توسط همین عنوان تأیید شده است.");
                    }

                    connection.Execute(
                        @"INSERT INTO [RequestAccept]
                              ([RequestID], [AccepterBadgeNumber], [AccepterTitleID], [AcceptDate], [IsCancel], [IsDeputy])
                          VALUES
                              (@RequestId, @AccepterBadgeNumber, @AccepterTitleID, SYSUTCDATETIME(), @IsCancel, @IsDeputy);",
                        new
                        {
                            RequestId = requestId,
                            AccepterBadgeNumber = accepterBadgeNumber,
                            AccepterTitleID = accepterTitleId,
                            IsCancel = cancel,
                            IsDeputy = isDeputy
                        }, transaction);

                    if (cancel)
                    {
                        connection.Execute(
                            @"UPDATE [Request] SET [IsSuspend] = 1 WHERE [RequestID] = @RequestId;",
                            new { RequestId = requestId }, transaction);
                    }

                    transaction.Commit();
                    detail = "request=" + requestId + "; acceptor=" + accepterBadgeNumber +
                             "; title=" + accepterTitleId + "; accepted_before=" + accepted +
                             (cancel ? "; cancelled" : string.Empty);
                    return OperationResult.Ok();
                }
                catch (Exception ex)
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception)
                    {
                        // A rollback failure must not mask the original error.
                    }
                    return OperationResult.Fail("ثبت تصمیم درخواست ناموفق بود: " + FirstLine(ex.Message));
                }
            }
        }

        public IReadOnlyList<AttendanceSummaryRow> GetMonthlySummary(int year, int month, long? badgeNumber, int take)
        {
            if (month < 1)
            {
                month = 1;
            }
            if (month > 12)
            {
                month = 12;
            }
            DateTime from = new DateTime(year, month, 1);
            DateTime to = from.AddMonths(1);

            // The Gregorian month is only the *storage* window; the Persian report
            // converts with PersianCalendar (see ReportsController), because a Jalali
            // month never aligns with a Gregorian one.
            //
            // The legacy screen drove this from the Personnel catalog and looked up the
            // engine numbers per person. Here the two are separated: the calendar
            // aggregates come from one grouped query on the engine catalog, the people
            // and department names from the Personnel catalog, and they are merged in
            // memory (docs/09).
            const string sql = @"
SELECT r.[BadgeNumber] AS BadgeNumber,
       COUNT(DISTINCT CASE WHEN r.[IsValid] = 1 THEN CAST(r.[CheckTime] AS date) END) AS PresentDays,
       SUM(CASE WHEN r.[CheckType] = 0 AND r.[IsValid] = 0 THEN 1 ELSE 0 END) AS LateCount,
       SUM(CASE WHEN r.[CheckType] = 1 AND r.[IsValid] = 0 THEN 1 ELSE 0 END) AS EarlyCount
FROM [Record] r
WHERE r.[CheckTime] >= @From AND r.[CheckTime] < @To
GROUP BY r.[BadgeNumber];";

            // A day whose punch count is odd has a missing check-in or check-out. It is
            // counted per badge in SQL so the result keeps the engine's own definition,
            // instead of approximating it from averages in memory.
            const string oddSql = @"
SELECT t.[BadgeNumber] AS BadgeNumber, COUNT(1) AS MissingPunchDays
FROM (SELECT r.[BadgeNumber] AS BadgeNumber, CAST(r.[CheckTime] AS date) AS Day, COUNT(1) AS Punches
      FROM [Record] r
      WHERE r.[CheckTime] >= @From AND r.[CheckTime] < @To
      GROUP BY r.[BadgeNumber], CAST(r.[CheckTime] AS date)
      HAVING COUNT(1) % 2 = 1) t
GROUP BY t.[BadgeNumber];";

            const string leaveSql = @"
SELECT y.[BadgeNumber] AS BadgeNumber,
       ISNULL(SUM(y.[DailyValue]), 0) AS LeaveBalanceDays,
       ISNULL((SELECT SUM(l.[DailyValue]) FROM [LeavesAdded] l
                WHERE l.[BadgeNumber] = y.[BadgeNumber]
                  AND l.[AssignDate] >= @From AND l.[AssignDate] < @To), 0) AS LeaveAddedDays
FROM [YearRemain] y
WHERE y.[Year] = @Year
GROUP BY y.[BadgeNumber];";

            var punches = new Dictionary<long, PunchAggregate>();
            var leave = new Dictionary<long, LeaveAggregate>();
            var oddDays = new Dictionary<long, int>();
            using (IDbConnection connection = Open())
            {
                foreach (var row in connection.Query<PunchAggregate>(sql, new { From = from, To = to }))
                {
                    punches[row.BadgeNumber] = row;
                }
                foreach (var row in connection.Query<LeaveAggregate>(leaveSql, new { From = from, To = to, Year = year }))
                {
                    leave[row.BadgeNumber] = row;
                }
                foreach (var row in connection.Query<OddDayAggregate>(oddSql, new { From = from, To = to }))
                {
                    oddDays[row.BadgeNumber] = row.MissingPunchDays;
                }
            }

            IReadOnlyList<PersonRef> people = _personnel.People(badgeNumber, take);
            var departments = _personnel.DepartmentNames();
            var rows = new List<AttendanceSummaryRow>(people.Count);
            foreach (PersonRef person in people)
            {
                PunchAggregate punch;
                LeaveAggregate paid;
                punches.TryGetValue(person.BadgeNumber, out punch);
                leave.TryGetValue(person.BadgeNumber, out paid);

                string department;
                int missing;
                oddDays.TryGetValue(person.BadgeNumber, out missing);

                rows.Add(new AttendanceSummaryRow
                {
                    BadgeNumber = person.BadgeNumber,
                    PersonName = person.PersonName ?? string.Empty,
                    DeptName = departments.TryGetValue(person.DepartmentId, out department) ? department : string.Empty,
                    PresentDays = punch == null ? 0 : punch.PresentDays,
                    LateCount = punch == null ? 0 : punch.LateCount,
                    EarlyCount = punch == null ? 0 : punch.EarlyCount,
                    MissingPunchDays = missing,
                    LeaveBalanceDays = paid == null ? 0 : paid.LeaveBalanceDays,
                    LeaveAddedDays = paid == null ? 0 : paid.LeaveAddedDays,
                    OvertimeHours = 0
                });
            }
            return rows;
        }

        private sealed class PunchAggregate
        {
            public long BadgeNumber { get; set; }
            public int PresentDays { get; set; }
            public int LateCount { get; set; }
            public int EarlyCount { get; set; }
        }

        private sealed class OddDayAggregate
        {
            public long BadgeNumber { get; set; }
            public int MissingPunchDays { get; set; }
        }

        private sealed class LeaveAggregate
        {
            public long BadgeNumber { get; set; }
            public double LeaveBalanceDays { get; set; }
            public double LeaveAddedDays { get; set; }
        }

        public IReadOnlyList<KeyValuePair<int, string>> GetCheckTypes()
        {
            // The engine stores the direction as a small int (0..5); these are the labels
            // the legacy grid used, kept so an operator's muscle memory still works.
            return new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(0, "ورود"),
                new KeyValuePair<int, string>(1, "خروج"),
                new KeyValuePair<int, string>(2, "شروع استراحت"),
                new KeyValuePair<int, string>(3, "پایان استراحت"),
                new KeyValuePair<int, string>(4, "اضافه‌کاری ورود"),
                new KeyValuePair<int, string>(5, "اضافه‌کاری خروج")
            };
        }

        private static string FirstLine(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }
            string first = message.Split('\n')[0].Trim();
            return first.Length > 200 ? first.Substring(0, 200) + "…" : first;
        }
    }
}
