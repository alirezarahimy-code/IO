using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Dapper;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// Schedules and shifts over [Schedule], [Shift], [ShiftDetail] and [UserShift].
    ///
    /// [Schedule].[SchName] exists in the schema but not on the engine's entity, so the
    /// name is read here and never written back (T16 marks that column display-only for
    /// the generic module screens; this store only reads).
    /// </summary>
    public sealed class SqlScheduleStore : IScheduleStore
    {
        private readonly Func<IDbConnection> _connectionFactory;

        public SqlScheduleStore(Func<IDbConnection> connectionFactory)
        {
            if (connectionFactory == null)
            {
                throw new ArgumentNullException(nameof(connectionFactory));
            }
            _connectionFactory = connectionFactory;
        }

        public IReadOnlyList<ScheduleRow> GetSchedules()
        {
            const string sql = @"
SELECT s.[ScheduleID] AS ScheduleId,
       ISNULL(s.[SchName], N'') AS Name,
       CONVERT(varchar(8), s.[StartTime], 108) AS StartTime,
       CONVERT(varchar(8), s.[EndTime], 108) AS EndTime,
       ISNULL(s.[WorkMins], 0) AS WorkMins,
       ISNULL(s.[RuleID], 0) AS RuleId,
       ISNULL(s.[Color], 0) AS Color,
       (SELECT COUNT(1) FROM [ShiftDetail] d WHERE d.[ScheduleID] = s.[ScheduleID]) AS ShiftCount
FROM [Schedule] s
ORDER BY s.[ScheduleID];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<ScheduleRow>(sql).ToList();
            }
        }

        public IReadOnlyList<ShiftRow> GetShifts()
        {
            const string sql = @"
SELECT sh.[ShiftID] AS ShiftId,
       ISNULL(sh.[ShiftName], N'') AS ShiftName,
       ISNULL(sh.[Period], 0) AS Period,
       ISNULL(sh.[Unit], 0) AS Unit,
       ISNULL(sh.[WeekEnds], N'') AS WeekEnds,
       (SELECT COUNT(1) FROM [ShiftDetail] d WHERE d.[ShiftID] = sh.[ShiftID]) AS DayCount
FROM [Shift] sh
ORDER BY sh.[ShiftID];

SELECT d.[ShiftID] AS ShiftId, ISNULL(d.[DayIndex], 0) AS DayIndex
FROM [ShiftDetail] d
ORDER BY d.[ShiftID], d.[DayIndex];";

            using (IDbConnection connection = Open())
            using (var multi = connection.QueryMultiple(sql))
            {
                List<ShiftRow> shifts = multi.Read<ShiftRow>().ToList();
                var days = multi.Read<ShiftDayRecord>().ToList();
                var byShift = days.GroupBy(d => d.ShiftId)
                                  .ToDictionary(g => g.Key, g => g.Select(d => d.DayIndex).OrderBy(i => i).ToList());
                foreach (ShiftRow shift in shifts)
                {
                    List<int> indexes;
                    if (byShift.TryGetValue(shift.ShiftId, out indexes))
                    {
                        shift.DayIndexes = indexes;
                    }
                }
                return shifts;
            }
        }

        public IReadOnlyList<UserShiftRow> GetAssignments(long? badgeNumber, int take)
        {
            const string sql = @"
SELECT TOP (@Take)
    us.[BadgeNumber] AS BadgeNumber,
    ISNULL(u.[FirstName], N'') + N' ' + ISNULL(u.[LastName], N'') AS PersonName,
    us.[ShiftID] AS ShiftId,
    ISNULL(sh.[ShiftName], N'') AS ShiftName,
    us.[FromDate] AS FromDate,
    us.[EndDate] AS EndDate
FROM [UserShift] us
LEFT JOIN [UserInfo] u ON u.[BadgeNumber] = us.[BadgeNumber]
LEFT JOIN [Shift] sh ON sh.[ShiftID] = us.[ShiftID]
WHERE (@BadgeNumber = 0 OR us.[BadgeNumber] = @BadgeNumber)
ORDER BY us.[FromDate] DESC, us.[BadgeNumber];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<UserShiftRow>(sql, new
                {
                    BadgeNumber = badgeNumber ?? 0,
                    Take = take <= 0 ? 200 : take
                }).ToList();
            }
        }

        public OperationResult AssignShift(long badgeNumber, int shiftId, DateTime fromDate, DateTime? endDate,
                                          out string detail)
        {
            detail = string.Empty;
            if (badgeNumber <= 0)
            {
                return OperationResult.Fail("شماره پرسنلی معتبر نیست.");
            }
            if (shiftId <= 0)
            {
                return OperationResult.Fail("شیفت انتخاب نشده است.");
            }

            string policyFailure;
            if (!SchedulePolicy.IsStartAcceptable(fromDate, DateTime.Today, out policyFailure))
            {
                return OperationResult.Fail(policyFailure);
            }
            if (endDate.HasValue && endDate.Value.Date < fromDate.Date)
            {
                return OperationResult.Fail("تاریخ پایان نمی‌تواند پیش از تاریخ شروع باشد.");
            }

            using (IDbConnection connection = Open())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    // Overlap check inside the transaction: two operators assigning the same
                    // person at the same time must not both win (BUG-22 in the legacy flow).
                    int overlapping = connection.ExecuteScalar<int>(@"
SELECT COUNT(1) FROM [UserShift]
WHERE [BadgeNumber] = @BadgeNumber
  AND [FromDate] <= ISNULL(@EndDate, '9999-12-31')
  AND ISNULL([EndDate], '9999-12-31') >= @FromDate;",
                        new
                        {
                            BadgeNumber = badgeNumber,
                            FromDate = fromDate.Date,
                            EndDate = endDate.HasValue ? (object)endDate.Value.Date : null
                        }, transaction);

                    if (overlapping > 0)
                    {
                        transaction.Rollback();
                        return OperationResult.Fail("بازه انتخابی با یک بازه فعال دیگر برای همین پرسنل هم‌پوشانی دارد.");
                    }

                    connection.Execute(@"
INSERT INTO [UserShift] ([BadgeNumber], [ShiftID], [FromDate], [EndDate])
VALUES (@BadgeNumber, @ShiftId, @FromDate, @EndDate);",
                        new
                        {
                            BadgeNumber = badgeNumber,
                            ShiftId = shiftId,
                            FromDate = fromDate.Date,
                            EndDate = endDate.HasValue ? (object)endDate.Value.Date : null
                        }, transaction);

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (InvalidOperationException)
                    {
                        // Already rolled back; nothing else to do.
                    }
                    return OperationResult.Fail("ثبت برنامه انجام نشد: " + ex.Message);
                }
            }

            detail = "badge=" + badgeNumber + "; shift=" + shiftId +
                     "; from=" + fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                     "; to=" + (endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "open");
            return OperationResult.Ok();
        }

        private IDbConnection Open()
        {
            IDbConnection connection = _connectionFactory();
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }
            return connection;
        }

        private sealed class ShiftDayRecord
        {
            public int ShiftId { get; set; }
            public int DayIndex { get; set; }
        }
    }
}
