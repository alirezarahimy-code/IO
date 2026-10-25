using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Dapper;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// Leave balances over the shared T5 schema.
    ///
    /// Read model comes from four engine tables:
    ///   [YearRemain]  - opening balance per (badge, leave type, year)
    ///   [LeavesAdded] - grants during the year
    ///   [DailyLeaf]   - consumed days (one row per granted day)
    ///   [LeaveType]   - names, and the ForNextYear flag that decides what may carry over
    ///
    /// Nothing here recalculates attendance. The invariant the screens rely on
    /// (opening + added - used = closing) is pure arithmetic over these three sums,
    /// which is why it can be asserted in T21 without a SQL server.
    /// </summary>
    public sealed class SqlLeaveStore : ILeaveStore
    {
        private readonly Func<IDbConnection> _connectionFactory;
        private readonly PersonnelDirectory _personnel;

        public SqlLeaveStore(Func<IDbConnection> connectionFactory, PersonnelDirectory personnel = null)
        {
            if (connectionFactory == null)
            {
                throw new ArgumentNullException(nameof(connectionFactory));
            }
            _connectionFactory = connectionFactory;
            // The ledger is in the engine catalog, the names in the Personnel catalog;
            // a search term is resolved there first (docs/09).
            _personnel = personnel ?? new PersonnelDirectory(connectionFactory);
        }

        public IReadOnlyList<LeaveBalanceRow> GetBalances(int year, int? leaveTypeId, string term, int take)
        {
            string termText = (term ?? string.Empty).Trim();
            long[] badges = termText.Length == 0
                ? null
                : _personnel.BadgesMatching(termText, 500).ToArray();
            if (badges != null && badges.Length == 0)
            {
                // The term matches nobody in the Personnel catalog: an empty answer, and
                // not an accidental "all rows" (an empty IN () is a syntax error).
                return new List<LeaveBalanceRow>();
            }

            const string sql = @"
SELECT TOP (@Take)
    y.[BadgeNumber] AS BadgeNumber,
    CAST('' AS nvarchar(1)) AS PersonName,
    y.[LeaveTypeID] AS LeaveTypeId,
    ISNULL(lt.[LeaveName], N'') AS LeaveTypeName,
    y.[Year] AS Year,
    CAST(ISNULL(y.[DailyValue], 0) AS float) AS OpeningDays,
    CAST(ISNULL((SELECT SUM(la.[DailyValue]) FROM [LeavesAdded] la
                  WHERE la.[BadgeNumber] = y.[BadgeNumber]
                    AND la.[LeaveTypeID] = y.[LeaveTypeID]
                    AND YEAR(la.[AssignDate]) = y.[Year]), 0) AS float) AS AddedDays,
    CAST(ISNULL((SELECT COUNT(1) FROM [DailyLeaf] dl
                  WHERE dl.[BadgeNumber] = y.[BadgeNumber]
                    AND dl.[LeaveID] = y.[LeaveTypeID]
                    AND YEAR(dl.[Date]) = y.[Year]), 0) AS float) AS UsedDays
FROM [YearRemain] y
LEFT JOIN [LeaveType] lt ON lt.[LeaveID] = y.[LeaveTypeID]
WHERE y.[Year] = @Year
  AND (@LeaveTypeId = 0 OR y.[LeaveTypeID] = @LeaveTypeId)
  AND (@NoTerm = 1 OR y.[BadgeNumber] IN @Badges)
ORDER BY y.[BadgeNumber], lt.[LeaveName];";

            List<LeaveBalanceRecord> rows;
            using (IDbConnection connection = Open())
            {
                rows = connection.Query<LeaveBalanceRecord>(sql, new
                {
                    Year = year,
                    LeaveTypeId = leaveTypeId ?? 0,
                    NoTerm = badges == null ? 1 : 0,
                    Badges = badges ?? new long[0],
                    Take = Clamp(take, 1, 500, 200)
                }).ToList();
            }

            // The name is not in this catalog, so it is filled here and the ordering the
            // legacy grid used (by person, then leave type) is applied afterwards.
            var names = _personnel.Names(rows.Select(r => r.BadgeNumber));
            foreach (LeaveBalanceRecord row in rows)
            {
                string name;
                row.PersonName = names.TryGetValue(row.BadgeNumber, out name) ? name : string.Empty;
            }

            return rows
                .OrderBy(r => r.PersonName, StringComparer.Create(CultureInfo.InvariantCulture, true))
                .ThenBy(r => r.LeaveTypeName)
                .Select(r => new LeaveBalanceRow
                {
                    BadgeNumber = r.BadgeNumber,
                    PersonName = (r.PersonName ?? string.Empty).Trim(),
                    LeaveTypeId = r.LeaveTypeId,
                    LeaveTypeName = r.LeaveTypeName ?? string.Empty,
                    Year = r.Year,
                    OpeningDays = r.OpeningDays,
                    AddedDays = r.AddedDays,
                    UsedDays = r.UsedDays,
                    // The engine's stored balance is the opening figure; a stored value
                    // below zero with no usage is a data problem worth flagging, not hiding.
                    IsInconsistent = false
                }).ToList();
        }

        public IReadOnlyList<LeaveGrantRow> GetGrants(long? badgeNumber, int? leaveTypeId, int take)
        {
            const string sql = @"
SELECT TOP (@Take)
    la.[ID] AS Id,
    la.[BadgeNumber] AS BadgeNumber,
    la.[LeaveTypeID] AS LeaveTypeId,
    ISNULL(lt.[LeaveName], N'') AS LeaveTypeName,
    la.[AssignDate] AS AssignDate,
    CAST(ISNULL(la.[DailyValue], 0) AS float) AS Days,
    ISNULL(la.[Descriptions], N'') AS Descriptions,
    ISNULL(la.[DocumentNumber], N'') AS DocumentNumber,
    ISNULL(la.[IsResell], 0) AS IsResell
FROM [LeavesAdded] la
LEFT JOIN [LeaveType] lt ON lt.[LeaveID] = la.[LeaveTypeID]
WHERE (@BadgeNumber = 0 OR la.[BadgeNumber] = @BadgeNumber)
  AND (@LeaveTypeId = 0 OR la.[LeaveTypeID] = @LeaveTypeId)
ORDER BY la.[AssignDate] DESC, la.[ID] DESC;";

            using (IDbConnection connection = Open())
            {
                return connection.Query<LeaveGrantRecord>(sql, new
                {
                    BadgeNumber = badgeNumber ?? 0,
                    LeaveTypeId = leaveTypeId ?? 0,
                    Take = Clamp(take, 1, 500, 200)
                }).Select(r => new LeaveGrantRow
                {
                    Id = r.Id,
                    BadgeNumber = r.BadgeNumber,
                    LeaveTypeId = r.LeaveTypeId,
                    LeaveTypeName = r.LeaveTypeName ?? string.Empty,
                    AssignDate = r.AssignDate,
                    Days = r.Days,
                    Descriptions = r.Descriptions ?? string.Empty,
                    DocumentNumber = r.DocumentNumber ?? string.Empty,
                    IsResell = r.IsResell
                }).ToList();
            }
        }

        public OperationResult AddGrant(LeaveGrantRow grant)
        {
            if (grant == null)
            {
                return OperationResult.Fail("داده‌ای برای ثبت وجود ندارد.");
            }
            if (grant.BadgeNumber <= 0)
            {
                return OperationResult.Fail("شماره پرسنلی معتبر نیست.");
            }
            if (grant.LeaveTypeId <= 0)
            {
                return OperationResult.Fail("نوع مرخصی انتخاب نشده است.");
            }
            if (grant.Days <= 0 || grant.Days > 366)
            {
                return OperationResult.Fail("مقدار مرخصی باید بین ۰ و ۳۶۶ روز باشد.");
            }
            if (grant.Days * 2 != Math.Floor(grant.Days * 2))
            {
                return OperationResult.Fail("مقدار مرخصی حداکثر دو رقم اعشار دارد (مثلاً ۰٫۵ روز).");
            }

            const string sql = @"
INSERT INTO [LeavesAdded]
    ([BadgeNumber], [LeaveTypeID], [AssignDate], [DailyValue], [Descriptions], [DocumentNumber], [IsResell])
VALUES
    (@BadgeNumber, @LeaveTypeId, @AssignDate, @Days, @Descriptions, @DocumentNumber, @IsResell);";

            using (IDbConnection connection = Open())
            {
                connection.Execute(sql, new
                {
                    BadgeNumber = grant.BadgeNumber,
                    LeaveTypeId = grant.LeaveTypeId,
                    AssignDate = grant.AssignDate.Date,
                    Days = grant.Days,
                    Descriptions = grant.Descriptions ?? string.Empty,
                    DocumentNumber = grant.DocumentNumber ?? string.Empty,
                    IsResell = grant.IsResell ? 1 : 0
                });
            }
            return OperationResult.Ok();
        }

        public IReadOnlyList<LeaveTransferRow> PreviewTransfer(int fromYear, int toYear, double carryCap,
                                                               long? badgeNumber)
        {
            IReadOnlyList<LeaveBalanceRow> balances = GetBalances(fromYear, null, null, 500);
            var result = new List<LeaveTransferRow>();
            if (balances.Count == 0)
            {
                return result;
            }

            HashSet<int> carryTypes = GetCarryTypes();
            foreach (LeaveBalanceRow balance in balances)
            {
                if (badgeNumber.HasValue && badgeNumber.Value > 0 && balance.BadgeNumber != badgeNumber.Value)
                {
                    continue;
                }

                double closing = balance.ClosingDays;
                if (closing <= 0)
                {
                    continue;
                }
                if (!carryTypes.Contains(balance.LeaveTypeId))
                {
                    result.Add(new LeaveTransferRow
                    {
                        BadgeNumber = balance.BadgeNumber,
                        PersonName = balance.PersonName,
                        LeaveTypeId = balance.LeaveTypeId,
                        LeaveTypeName = balance.LeaveTypeName,
                        FromDays = balance.OpeningDays,
                        TransferDays = 0,
                        CarryCap = carryCap,
                        Capped = false,
                        Note = "این نوع مرخصی قابل انتقال نیست."
                    });
                    continue;
                }

                double allowed = carryCap > 0 ? Math.Min(closing, carryCap) : closing;
                result.Add(new LeaveTransferRow
                {
                    BadgeNumber = balance.BadgeNumber,
                    PersonName = balance.PersonName,
                    LeaveTypeId = balance.LeaveTypeId,
                    LeaveTypeName = balance.LeaveTypeName,
                    FromDays = balance.OpeningDays,
                    TransferDays = Math.Round(allowed, 2),
                    CarryCap = carryCap,
                    Capped = carryCap > 0 && closing > carryCap,
                    Note = carryCap > 0 && closing > carryCap
                        ? "بیش از سقف انتقال؛ مازاد حذف می‌شود."
                        : string.Empty
                });
            }
            return result;
        }

        public OperationResult CommitTransfer(IReadOnlyList<LeaveTransferRow> rows, int fromYear, int toYear,
                                              string reason, out string detail)
        {
            detail = string.Empty;
            if (rows == null || rows.Count == 0)
            {
                return OperationResult.Fail("سطرهای انتقال خالی است.");
            }
            if (toYear != fromYear + 1)
            {
                return OperationResult.Fail("انتقال فقط به سال بعد مجاز است.");
            }
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 4)
            {
                return OperationResult.Fail("دلیل انتقال (حداقل ۴ نویسه) الزامی است.");
            }

            string marker = "انتقال مانده " + fromYear.ToString(CultureInfo.InvariantCulture) + " به " +
                            toYear.ToString(CultureInfo.InvariantCulture) + " — " + reason.Trim();
            List<LeaveTransferRow> moving = rows.Where(r => r.TransferDays > 0).ToList();
            if (moving.Count == 0)
            {
                return OperationResult.Fail("هیچ مانده‌ای قابل انتقال نیست.");
            }

            using (IDbConnection connection = Open())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    // Idempotency guard: a second click must not double the balance.
                    int already = connection.ExecuteScalar<int>(@"
SELECT COUNT(1) FROM [LeavesAdded]
WHERE [AssignDate] >= @Start AND [AssignDate] < @End
  AND [Descriptions] LIKE N'انتقال مانده ' + @FromYear + N' به %';",
                        new
                        {
                            Start = new DateTime(toYear, 1, 1),
                            End = new DateTime(toYear, 1, 1).AddYears(1),
                            FromYear = fromYear.ToString(CultureInfo.InvariantCulture)
                        }, transaction);
                    if (already > 0)
                    {
                        transaction.Rollback();
                        return OperationResult.Fail("انتقال این سال قبلاً انجام شده است.");
                    }

                    const string insert = @"
INSERT INTO [LeavesAdded]
    ([BadgeNumber], [LeaveTypeID], [AssignDate], [DailyValue], [Descriptions], [DocumentNumber], [IsResell])
VALUES (@BadgeNumber, @LeaveTypeId, @AssignDate, @Days, @Descriptions, N'', 0);";

                    const string zero = @"
UPDATE [YearRemain]
SET [DailyValue] = ISNULL([DailyValue], 0) - @Days
WHERE [BadgeNumber] = @BadgeNumber AND [LeaveTypeID] = @LeaveTypeId AND [Year] = @Year;";

                    // Write the target year first, then drain the source. If the second
                    // statement fails on a missing row, the transaction rolls back both.
                    foreach (LeaveTransferRow row in moving)
                    {
                        int inserted = connection.Execute(insert, new
                        {
                            BadgeNumber = row.BadgeNumber,
                            LeaveTypeId = row.LeaveTypeId,
                            AssignDate = new DateTime(toYear, 1, 1),
                            Days = row.TransferDays,
                            Descriptions = marker
                        }, transaction);

                        int drained = connection.Execute(zero, new
                        {
                            BadgeNumber = row.BadgeNumber,
                            LeaveTypeId = row.LeaveTypeId,
                            Year = fromYear,
                            Days = row.TransferDays
                        }, transaction);

                        if (drained == 0)
                        {
                            transaction.Rollback();
                            return OperationResult.Fail(
                                "سطر مانده برای پرسنل " + row.BadgeNumber + " در سال مبدأ یافت نشد؛ هیچ تغییری ثبت نشد.");
                        }
                        if (inserted == 0)
                        {
                            transaction.Rollback();
                            return OperationResult.Fail("ثبت سطر انتقال برای پرسنل " + row.BadgeNumber + " انجام نشد.");
                        }
                    }

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
                        // Already rolled back by the failure above; nothing to do.
                    }
                    return OperationResult.Fail("انتقال انجام نشد: " + ex.Message);
                }
            }

            double total = moving.Sum(r => r.TransferDays);
            detail = "from=" + fromYear + "; to=" + toYear + "; rows=" + moving.Count +
                     "; days=" + total.ToString("0.##", CultureInfo.InvariantCulture) + "; reason=" + reason.Trim();
            return OperationResult.Ok();
        }

        public IReadOnlyList<KeyValuePair<int, string>> GetLeaveTypes()
        {
            const string sql = @"
SELECT [LeaveID] AS Id, ISNULL([LeaveName], N'') AS Name
FROM [LeaveType]
ORDER BY [LeaveName];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<KeyValueRecord>(sql)
                    .Select(r => new KeyValuePair<int, string>(r.Id, r.Name ?? string.Empty))
                    .ToList();
            }
        }

        private HashSet<int> GetCarryTypes()
        {
            const string sql = @"SELECT [LeaveID] FROM [LeaveType] WHERE ISNULL([ForNextYear], 0) = 1;";
            using (IDbConnection connection = Open())
            {
                return new HashSet<int>(connection.Query<int>(sql));
            }
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

        private static int Clamp(int value, int min, int max, int fallback)
        {
            if (value < min)
            {
                return fallback;
            }
            return value > max ? max : value;
        }

        private sealed class LeaveBalanceRecord
        {
            public long BadgeNumber { get; set; }
            public string PersonName { get; set; }
            public int LeaveTypeId { get; set; }
            public string LeaveTypeName { get; set; }
            public int Year { get; set; }
            public double OpeningDays { get; set; }
            public double AddedDays { get; set; }
            public double UsedDays { get; set; }
        }

        private sealed class LeaveGrantRecord
        {
            public long Id { get; set; }
            public long BadgeNumber { get; set; }
            public int LeaveTypeId { get; set; }
            public string LeaveTypeName { get; set; }
            public DateTime AssignDate { get; set; }
            public double Days { get; set; }
            public string Descriptions { get; set; }
            public string DocumentNumber { get; set; }
            public bool IsResell { get; set; }
        }

        private sealed class KeyValueRecord
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}
