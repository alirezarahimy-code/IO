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
    /// Referential-integrity checks over the engine schema (docs/11 §3).
    ///
    /// Why this exists: the engine stores its relations *logically* - 31 of them cross
    /// two catalogs, so SQL Server cannot enforce them with a foreign key (docs/09). When
    /// a leave type is deleted on the desktop, every row that pointed at it keeps
    /// pointing at nothing, and the symptom is a wrong balance, not an error. This store
    /// turns that into a visible number on a page.
    ///
    /// Two rules the implementation keeps:
    ///   * one statement touches one catalog (`docs/09` §3) - the cross-catalog checks
    ///     read the small reference set first and compare in memory;
    ///   * every check is a COUNT plus a few sample keys, never a full dump: an integrity
    ///     page that streams 200,000 rows is a second problem, not a diagnostic.
    /// </summary>
    public sealed class SqlIntegrityStore : IIntegrityStore
    {
        private const int SampleSize = 5;

        private readonly Func<IDbConnection> _engineFactory;
        private readonly Func<IDbConnection> _personnelFactory;

        public SqlIntegrityStore(Func<IDbConnection> engineFactory, Func<IDbConnection> personnelFactory)
        {
            if (engineFactory == null)
            {
                throw new ArgumentNullException(nameof(engineFactory));
            }
            _engineFactory = engineFactory;
            _personnelFactory = personnelFactory ?? engineFactory;
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

        public IReadOnlyList<IntegrityCheckRow> Run()
        {
            var checks = new List<IntegrityCheckRow>
            {
                SingleCatalog("leaves-leavetype",
                    "مرخصی ثبت‌شده با نوع مرخصی ناموجود", "LeavesAdded.LeaveTypeID → LeaveType.LeaveID"),
                SingleCatalog("yearremain-leavetype",
                    "مانده سالانه با نوع مرخصی ناموجود", "YearRemain.LeaveTypeID → LeaveType.LeaveID"),
                SingleCatalog("daily-leaf-description",
                    "مرخصی روزانه با شرح ناموجود", "DailyLeaf.DescriptionID → LeaveDescription.ID"),
                SingleCatalog("request-leavetype",
                    "درخواست با نوع مرخصی ناموجود", "Request.LeaveID → LeaveType.LeaveID"),
                EmpTypeOrphans(),
                WorkflowTitleOrphans(),
                UserShiftBadgeOrphans()
            };
            return checks;
        }

        // ------------------------------------------------------------ single catalog

        /// <summary>Checks whose parent table lives in the same engine catalog.</summary>
        private IntegrityCheckRow SingleCatalog(string code, string title, string reference)
        {
            string sql;
            switch (code)
            {
                case "leaves-leavetype":
                    sql = @"SELECT COUNT(1) FROM [LeavesAdded] a
                            WHERE a.[LeaveTypeID] IS NOT NULL
                              AND NOT EXISTS (SELECT 1 FROM [LeaveType] t WHERE t.[LeaveID] = a.[LeaveTypeID]);";
                    break;
                case "yearremain-leavetype":
                    sql = @"SELECT COUNT(1) FROM [YearRemain] y
                            WHERE y.[LeaveTypeID] IS NOT NULL
                              AND NOT EXISTS (SELECT 1 FROM [LeaveType] t WHERE t.[LeaveID] = y.[LeaveTypeID]);";
                    break;
                case "daily-leaf-description":
                    sql = @"SELECT COUNT(1) FROM [DailyLeaf] d
                            WHERE d.[DescriptionID] IS NOT NULL AND d.[DescriptionID] <> 0
                              AND NOT EXISTS (SELECT 1 FROM [LeaveDescription] t WHERE t.[ID] = d.[DescriptionID]);";
                    break;
                case "request-leavetype":
                    sql = @"SELECT COUNT(1) FROM [Request] r
                            WHERE r.[LeaveID] IS NOT NULL
                              AND NOT EXISTS (SELECT 1 FROM [LeaveType] t WHERE t.[LeaveID] = r.[LeaveID]);";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(code));
            }

            using (IDbConnection connection = OpenEngine())
            {
                int count = connection.ExecuteScalar<int>(sql);
                return new IntegrityCheckRow
                {
                    Code = code,
                    TitleFa = title,
                    Reference = reference,
                    Count = count,
                    Samples = count == 0 ? new List<string>() : Samples(code),
                    NoteFa = "هر دو جدول در کاتالوگ موتور هستند؛ رابطه با یک دستور سنجیده می‌شود."
                };
            }
        }

        /// <summary>A handful of offending keys, so the operator can open the right row.</summary>
        private IReadOnlyList<string> Samples(string code)
        {
            string sql;
            switch (code)
            {
                case "leaves-leavetype":
                    sql = @"SELECT DISTINCT TOP (" + SampleSize + @") CAST(a.[LeaveTypeID] AS nvarchar(30)) + N' / ' +
                                   CAST(a.[BadgeNumber] AS nvarchar(30))
                            FROM [LeavesAdded] a
                            WHERE a.[LeaveTypeID] IS NOT NULL
                              AND NOT EXISTS (SELECT 1 FROM [LeaveType] t WHERE t.[LeaveID] = a.[LeaveTypeID]);";
                    break;
                case "yearremain-leavetype":
                    sql = @"SELECT DISTINCT TOP (" + SampleSize + @") CAST(y.[LeaveTypeID] AS nvarchar(30)) + N' / ' +
                                   CAST(y.[BadgeNumber] AS nvarchar(30))
                            FROM [YearRemain] y
                            WHERE y.[LeaveTypeID] IS NOT NULL
                              AND NOT EXISTS (SELECT 1 FROM [LeaveType] t WHERE t.[LeaveID] = y.[LeaveTypeID]);";
                    break;
                case "daily-leaf-description":
                    sql = @"SELECT DISTINCT TOP (" + SampleSize + @") CAST(d.[DescriptionID] AS nvarchar(30)) + N' / ' +
                                   CAST(d.[BadgeNumber] AS nvarchar(30))
                            FROM [DailyLeaf] d
                            WHERE d.[DescriptionID] IS NOT NULL AND d.[DescriptionID] <> 0
                              AND NOT EXISTS (SELECT 1 FROM [LeaveDescription] t WHERE t.[ID] = d.[DescriptionID]);";
                    break;
                default:
                    sql = @"SELECT DISTINCT TOP (" + SampleSize + @") CAST(r.[LeaveID] AS nvarchar(30)) + N' / ' +
                                   CAST(r.[BadgeNumber] AS nvarchar(30))
                            FROM [Request] r
                            WHERE r.[LeaveID] IS NOT NULL
                              AND NOT EXISTS (SELECT 1 FROM [LeaveType] t WHERE t.[LeaveID] = r.[LeaveID]);";
                    break;
            }

            using (IDbConnection connection = OpenEngine())
            {
                return connection.Query<string>(sql).ToList();
            }
        }

        // ------------------------------------------------------------ cross catalog

        /// <summary>
        /// EmpTypeLeave points at [EmpType] (Personnel) and [LeaveType] (engine). The links
        /// that lost their employment type are the ones that silently stop granting leave.
        /// </summary>
        private IntegrityCheckRow EmpTypeOrphans()
        {
            List<int> ids;
            using (IDbConnection connection = OpenPersonnel())
            {
                ids = connection.Query<int>(@"SELECT [EmpTypeID] FROM [EmpType];").ToList();
            }

            using (IDbConnection connection = OpenEngine())
            {
                List<int> used = connection.Query<int>(
                    @"SELECT DISTINCT [EmpTypeID] FROM [EmpTypeLeave] WHERE [EmpTypeID] IS NOT NULL;").ToList();
                var known = new HashSet<int>(ids);
                List<int> orphan = used.Where(id => !known.Contains(id)).OrderBy(id => id).ToList();
                return new IntegrityCheckRow
                {
                    Code = "emptypeleave-emptype",
                    TitleFa = "سهمیه مرخصی با نوع استخدام ناموجود",
                    Reference = "EmpTypeLeave.EmpTypeID → EmpType.EmpTypeID (دو کاتالوگ)",
                    Count = orphan.Count,
                    Samples = orphan.Take(SampleSize).Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList(),
                    NoteFa = "کاتالوگ‌ها جدا هستند، پس مقایسه در حافظه انجام می‌شود (docs/09 §3)."
                };
            }
        }

        /// <summary>The workflow chain names job titles; a deleted title breaks an approval path.</summary>
        private IntegrityCheckRow WorkflowTitleOrphans()
        {
            List<int> titles;
            using (IDbConnection connection = OpenPersonnel())
            {
                titles = connection.Query<int>(@"SELECT [TitleID] FROM [TitlesChart];").ToList();
            }

            using (IDbConnection connection = OpenEngine())
            {
                List<int> used = connection.Query<int>(@"
SELECT DISTINCT [TitleID] FROM [Workflow] WHERE [TitleID] IS NOT NULL
UNION
SELECT DISTINCT [AccepterTitleID] FROM [Workflow] WHERE [AccepterTitleID] IS NOT NULL;").ToList();
                var known = new HashSet<int>(titles);
                List<int> orphan = used.Where(id => !known.Contains(id)).OrderBy(id => id).ToList();
                return new IntegrityCheckRow
                {
                    Code = "workflow-title",
                    TitleFa = "گردش کار با عنوان شغلی ناموجود",
                    Reference = "Workflow.TitleID/AccepterTitleID → TitlesChart.TitleID (دو کاتالوگ)",
                    Count = orphan.Count,
                    Samples = orphan.Take(SampleSize).Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList(),
                    NoteFa = "زنجیره تأیید به این عنوان‌ها می‌رسد؛ عنوان حذف‌شده زنجیره را قطع می‌کند."
                };
            }
        }

        /// <summary>Shift assignments that name a person who is no longer in Personnel.</summary>
        private IntegrityCheckRow UserShiftBadgeOrphans()
        {
            List<long> used;
            using (IDbConnection connection = OpenEngine())
            {
                used = connection.Query<long>(
                    @"SELECT DISTINCT [BadgeNumber] FROM [UserShift] WHERE [BadgeNumber] IS NOT NULL;").ToList();
            }

            long[] candidates = used.Where(b => b > 0).ToArray();
            if (candidates.Length == 0)
            {
                return Clean("usershift-badge", "شیفت پرسنل ناموجود");
            }

            var known = new HashSet<long>();
            using (IDbConnection connection = OpenPersonnel())
            {
                foreach (long[] chunk in Chunks(candidates, 1000))
                {
                    foreach (long badge in connection.Query<long>(
                                 @"SELECT [BadgeNumber] FROM [UserInfo] WHERE [BadgeNumber] IN @Badges;",
                                 new { Badges = chunk }))
                    {
                        known.Add(badge);
                    }
                }
            }

            List<long> orphan = candidates.Where(b => !known.Contains(b)).OrderBy(b => b).ToList();
            return new IntegrityCheckRow
            {
                Code = "usershift-badge",
                TitleFa = "شیفت پرسنل ناموجود",
                Reference = "UserShift.BadgeNumber → UserInfo.BadgeNumber (دو کاتالوگ)",
                Count = orphan.Count,
                Samples = orphan.Take(SampleSize).Select(b => b.ToString(CultureInfo.InvariantCulture)).ToList(),
                NoteFa = "پرسنل حذف‌شده در Personnel، شیفتش در ReportT5V3 می‌ماند."
            };
        }

        private static IntegrityCheckRow Clean(string code, string title)
        {
            return new IntegrityCheckRow
            {
                Code = code,
                TitleFa = title,
                Reference = "—",
                Count = 0,
                Samples = new List<string>()
            };
        }

        private static IEnumerable<long[]> Chunks(long[] values, int size)
        {
            for (int index = 0; index < values.Length; index += size)
            {
                yield return values.Skip(index).Take(size).ToArray();
            }
        }
    }
}
