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
    /// The three logs the portal may read, one statement per catalog.
    ///
    /// Evidence for every column used here: <c>engine/column-types.json</c>.
    ///   * <c>[SystemLog]</c>         - EventTime, EventType, OperationType, UserID,
    ///                                  ComputerName, Descriptions, EventID;
    ///   * <c>[OperationLog]</c>      - OperationTime, OperationType, BadgeNumber,
    ///                                  MachineNumber, DeviceSerialnumber, Param1..4;
    ///   * <c>[DownloadRecordsLog]</c> - EventTime, EventID, MachineNumber, MachineIP,
    ///                                  ComputerName, RecordsCount, Success, Descriptions,
    ///                                  DeviceDownloadEventType, UserID.
    ///
    /// Each source has its own method so the two-catalog gate can prove which connection
    /// the statement runs on (docs/09 §3, layer 2).
    /// </summary>
    public sealed class SqlLogStore : ILogStore
    {
        public const int MaxTake = 500;

        private static readonly List<LogSourceDescriptor> Descriptors = new List<LogSourceDescriptor>
        {
            new LogSourceDescriptor
            {
                Code = "system",
                TitleFa = "رخدادهای سامانه دسکتاپ",
                Table = "SystemLog",
                Catalog = "t5",
                NoteFa = "تغییرات ثبت‌شده توسط نسخه دسکتاپ T5V3 (کاربر، رایانه، نوع رخداد)."
            },
            new LogSourceDescriptor
            {
                Code = "operation",
                TitleFa = "عملیات دستگاه روی پرسنل",
                Table = "OperationLog",
                Catalog = "personnel",
                NoteFa = "هر عملیات دستگاه برای یک نفر: نوع عملیات و چهار پارامتر آن."
            },
            new LogSourceDescriptor
            {
                Code = "download",
                TitleFa = "دفتر دانلود تردد",
                Table = "DownloadRecordsLog",
                Catalog = "t5",
                NoteFa = "هر بار دانلود از دستگاه: تعداد رکورد، موفق/ناموفق و رایانه مبدأ."
            }
        };

        private readonly Func<IDbConnection> _engineFactory;
        private readonly Func<IDbConnection> _personnelFactory;
        private readonly PersonnelDirectory _directory;

        public SqlLogStore(Func<IDbConnection> engineFactory, Func<IDbConnection> personnelFactory,
                           PersonnelDirectory directory = null)
        {
            if (engineFactory == null)
            {
                throw new ArgumentNullException(nameof(engineFactory));
            }
            _engineFactory = engineFactory;
            _personnelFactory = personnelFactory ?? engineFactory;
            _directory = directory ?? new PersonnelDirectory(_personnelFactory);
        }

        public IReadOnlyList<LogSourceDescriptor> Sources()
        {
            return Descriptors;
        }

        public IReadOnlyList<LogEntryRow> Search(string source, DateTime from, DateTime to,
                                                 long? badgeNumber, string term, int take)
        {
            int limit = take <= 0 ? 100 : (take > MaxTake ? MaxTake : take);
            string normalized = (source ?? string.Empty).Trim().ToLowerInvariant();
            int days = (to.Date - from.Date).Days;
            if (days < 0)
            {
                return new List<LogEntryRow>();
            }
            // A log is not an archive: the screen asks for a short window, and the query
            // says so rather than scanning a decade of rows.
            if (days > 62)
            {
                from = to.AddDays(-62);
            }

            IReadOnlyList<LogEntryRow> rows;
            switch (normalized)
            {
                case "operation":
                    rows = OperationLogs(from, to, badgeNumber, term, limit);
                    break;
                case "download":
                    rows = DownloadLogs(from, to, term, limit);
                    break;
                default:
                    rows = SystemLogs(from, to, term, limit);
                    break;
            }

            var names = _directory.Names(rows.Select(r => r.BadgeNumber).Where(b => b > 0));
            var machines = _directory.MachineNames(rows.Select(r => r.MachineNumber).Where(m => m > 0));
            foreach (LogEntryRow row in rows)
            {
                string name;
                if (row.BadgeNumber > 0 && names.TryGetValue(row.BadgeNumber, out name))
                {
                    row.PersonName = name;
                }
                string machine;
                if (row.MachineNumber > 0 && machines.TryGetValue(row.MachineNumber, out machine))
                {
                    row.MachineName = machine;
                }
            }
            return rows;
        }

        private IDbConnection OpenEngine()
        {
            IDbConnection connection = _engineFactory();
            connection.Open();
            return connection;
        }

        private IDbConnection OpenPersonnel()
        {
            IDbConnection connection = _personnelFactory();
            connection.Open();
            return connection;
        }

        private IReadOnlyList<LogEntryRow> SystemLogs(DateTime from, DateTime to, string term, int take)
        {
            const string sql = @"
SELECT TOP (@Take)
    [EventTime] AS Time,
    ISNULL([EventType], 0) AS EventType,
    ISNULL([OperationType], 0) AS OperationType,
    ISNULL([UserID], 0) AS UserId,
    ISNULL([ComputerName], N'') AS ComputerName,
    ISNULL([Descriptions], N'') AS Descriptions
FROM [SystemLog]
WHERE [EventTime] BETWEEN @From AND @To
  AND (@Term = N'' OR [Descriptions] LIKE @Term OR [ComputerName] LIKE @Term)
ORDER BY [EventTime] DESC;";

            using (IDbConnection connection = OpenEngine())
            {
                return connection.Query(sql, new
                {
                    Take = take,
                    From = from.Date,
                    To = to.Date.AddDays(1).AddSeconds(-1),
                    Term = string.IsNullOrWhiteSpace(term) ? string.Empty : "%" + term.Trim() + "%"
                })
                    .Cast<IDictionary<string, object>>()
                    .Select(row => new LogEntryRow
                    {
                        Source = "system",
                        Time = AsDate(row, "Time"),
                        BadgeNumber = AsLong(row, "UserId"),
                        MachineNumber = 0,
                        KindFa = "رخداد " + AsLong(row, "EventType").ToString(CultureInfo.InvariantCulture) +
                                 " / عملیات " + AsLong(row, "OperationType").ToString(CultureInfo.InvariantCulture),
                        Detail = AsText(row, "Descriptions") + " · " + AsText(row, "ComputerName")
                    })
                    .ToList();
            }
        }

        private IReadOnlyList<LogEntryRow> OperationLogs(DateTime from, DateTime to, long? badgeNumber,
                                                         string term, int take)
        {
            const string sql = @"
SELECT TOP (@Take)
    [OperationTime] AS Time,
    ISNULL([BadgeNumber], 0) AS BadgeNumber,
    ISNULL([MachineNumber], 0) AS MachineNumber,
    ISNULL([OperationType], N'') AS OperationType,
    ISNULL([DeviceSerialnumber], N'') AS Serial,
    ISNULL([Param1], N'') AS Param1,
    ISNULL([Param2], N'') AS Param2,
    ISNULL([Param3], N'') AS Param3,
    ISNULL([Param4], N'') AS Param4
FROM [OperationLog]
WHERE [OperationTime] BETWEEN @From AND @To
  AND (@Badge = 0 OR [BadgeNumber] = @Badge)
  AND (@Term = N'' OR [OperationType] LIKE @Term OR [DeviceSerialnumber] LIKE @Term)
ORDER BY [OperationTime] DESC;";

            using (IDbConnection connection = OpenPersonnel())
            {
                return connection.Query(sql, new
                {
                    Take = take,
                    From = from.Date,
                    To = to.Date.AddDays(1).AddSeconds(-1),
                    Badge = badgeNumber.HasValue && badgeNumber.Value > 0 ? badgeNumber.Value : 0,
                    Term = string.IsNullOrWhiteSpace(term) ? string.Empty : "%" + term.Trim() + "%"
                })
                    .Cast<IDictionary<string, object>>()
                    .Select(row => new LogEntryRow
                    {
                        Source = "operation",
                        Time = AsDate(row, "Time"),
                        BadgeNumber = AsLong(row, "BadgeNumber"),
                        MachineNumber = (int)AsLong(row, "MachineNumber"),
                        KindFa = AsText(row, "OperationType"),
                        Detail = Join(AsText(row, "Param1"), AsText(row, "Param2"),
                                      AsText(row, "Param3"), AsText(row, "Param4"), AsText(row, "Serial"))
                    })
                    .ToList();
            }
        }

        private IReadOnlyList<LogEntryRow> DownloadLogs(DateTime from, DateTime to, string term, int take)
        {
            const string sql = @"
SELECT TOP (@Take)
    [EventTime] AS Time,
    ISNULL([MachineNumber], 0) AS MachineNumber,
    ISNULL([MachineIP], N'') AS MachineIp,
    ISNULL([ComputerName], N'') AS ComputerName,
    ISNULL([RecordsCount], 0) AS RecordsCount,
    ISNULL([Success], 0) AS Success,
    ISNULL([Descriptions], N'') AS Descriptions,
    ISNULL([DeviceDownloadEventType], 0) AS EventType
FROM [DownloadRecordsLog]
WHERE [EventTime] BETWEEN @From AND @To
  AND (@Term = N'' OR [ComputerName] LIKE @Term OR [MachineIP] LIKE @Term)
ORDER BY [EventTime] DESC;";

            using (IDbConnection connection = OpenEngine())
            {
                return connection.Query(sql, new
                {
                    Take = take,
                    From = from.Date,
                    To = to.Date.AddDays(1).AddSeconds(-1),
                    Term = string.IsNullOrWhiteSpace(term) ? string.Empty : "%" + term.Trim() + "%"
                })
                    .Cast<IDictionary<string, object>>()
                    .Select(row => new LogEntryRow
                    {
                        Source = "download",
                        Time = AsDate(row, "Time"),
                        BadgeNumber = 0,
                        MachineNumber = (int)AsLong(row, "MachineNumber"),
                        KindFa = AsLong(row, "Success") == 1 ? "دانلود موفق" : "دانلود ناموفق",
                        Detail = AsLong(row, "RecordsCount").ToString(CultureInfo.InvariantCulture) + " رکورد · " +
                                 AsText(row, "ComputerName") + " · " + AsText(row, "MachineIp")
                    })
                    .ToList();
            }
        }

        private static object Read(IDictionary<string, object> row, string column)
        {
            object value;
            if (row.TryGetValue(column, out value))
            {
                return value;
            }
            foreach (KeyValuePair<string, object> pair in row)
            {
                if (string.Equals(pair.Key, column, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
            return null;
        }

        private static DateTime? AsDate(IDictionary<string, object> row, string column)
        {
            object value = Read(row, column);
            if (value == null || value == DBNull.Value)
            {
                return null;
            }
            return Convert.ToDateTime(value, CultureInfo.InvariantCulture);
        }

        private static long AsLong(IDictionary<string, object> row, string column)
        {
            object value = Read(row, column);
            if (value == null || value == DBNull.Value)
            {
                return 0;
            }
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private static string AsText(IDictionary<string, object> row, string column)
        {
            object value = Read(row, column);
            return value == null || value == DBNull.Value
                ? string.Empty
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string Join(params string[] parts)
        {
            return string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }
}
