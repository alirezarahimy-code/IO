using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using MyTime3.Controllers;

namespace MyTime3.Security
{
    /// <summary>
    /// Result of comparing the live database against the schema contract that
    /// the rewritten portal (Aria) was built for.
    /// </summary>
    public sealed class SchemaCheckResult
    {
        private readonly List<string> _problems;
        private readonly List<string> _advisories;

        public SchemaCheckResult(
            bool wasChecked,
            bool isCompatible,
            bool databaseReachable,
            IEnumerable<string> problems,
            IEnumerable<string> advisories,
            string message,
            DateTime checkedAt)
        {
            WasChecked = wasChecked;
            IsCompatible = isCompatible;
            DatabaseReachable = databaseReachable;
            _problems = problems == null ? new List<string>() : new List<string>(problems);
            _advisories = advisories == null ? new List<string>() : new List<string>(advisories);
            Message = message ?? string.Empty;
            CheckedAt = checkedAt;
        }

        /// <summary>False when the guard deliberately skipped (unit tests / in-memory mode).</summary>
        public bool WasChecked { get; private set; }

        /// <summary>True only when the database satisfies every *required* object.</summary>
        public bool IsCompatible { get; private set; }

        public bool DatabaseReachable { get; private set; }

        /// <summary>Blocking problems: the portal must not run with these.</summary>
        public IReadOnlyList<string> Problems
        {
            get { return _problems.AsReadOnly(); }
        }

        /// <summary>
        /// Non-blocking observations: objects Aria uses that we could not prove
        /// from the shipped vendor schema. Reported, never blocking, because a
        /// false alarm here would take a healthy system offline.
        /// </summary>
        public IReadOnlyList<string> Advisories
        {
            get { return _advisories.AsReadOnly(); }
        }

        public string Message { get; private set; }

        public DateTime CheckedAt { get; private set; }

        internal static SchemaCheckResult Skipped(string reason)
        {
            return new SchemaCheckResult(false, true, true, null, null, reason, DateTime.Now);
        }
    }

    /// <summary>
    /// Guards the boundary between Aria and the shared T5 database.
    ///
    /// Background: Aria is a *consumer* of a schema owned by the T5 engine (see
    /// docs/02-shared-database-contract.md). If an operator upgrades T5V3/T5Web to a
    /// build with a different schema, the portal used to fail mid-shift with a raw
    /// "Invalid column name 'X'" SQL exception thrown from deep inside a controller.
    ///
    /// This guard checks the contract once at start-up and lets the UI show one
    /// clear Persian page instead. It never throws: a missing database is reported,
    /// not propagated.
    ///
    /// Two tiers of evidence
    /// ---------------------
    /// Required  - the table/column is proven by at least one of: the vendor DDL
    ///             (Link5 v4.0.4/Personnel.sql), the T5V3.DB entity metadata, or
    ///             the legacy TD5 data layer that runs in production. Proven absence
    ///             is a real incompatibility and blocks the portal.
    /// Advisory  - Aria uses the object but no shipped evidence exists (for example
    ///             tables created by the T5V3 installer that are not in the vendor
    ///             SQL). Reported on the diagnostics page, never blocking.
    /// </summary>
    public static class SchemaGuard
    {
        private static readonly object SyncRoot = new object();
        private static SchemaCheckResult _lastResult;

        /// <summary>
        /// Objects Aria cannot work without. Every entry is backed by
        /// db/schema-manifest.json (see tools/build-schema-manifest.py);
        /// tools/verify-sql-schema.py fails if an entry here has no evidence.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string[]> Contract =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                // --- request / workflow (Aria writes the request, engine consumes it) ---
                { "Request", new[] { "RequestID", "BadgeNumber", "WofkflowType", "IsSuspend", "FromTime", "ToTime", "CreateDate", "Descriptions" } },
                { "RequestAccept", new[] { "RequestID", "AccepterTitleID", "AcceptDate", "IsCancel" } },
                { "Workflow", new[] { "TitleID", "AccepterTitleID", "OrderIndex", "WorkflowType" } },

                // --- operational results ---
                { "Record", new[] { "BadgeNumber", "CheckTime" } },
                { "HourlyLeave", new[] { "BadgeNumber", "FromTime", "ToTime" } },

                // --- people and organisation ---
                { "UserInfo", new[] { "BadgeNumber", "DeptID", "UserName", "Password" } },
                { "UserInfoTitles", new[] { "BadgeNumber", "TitleChartID" } },
                { "TitlesChart", new[] { "TitleID", "TitleName", "UpperTitleID" } },
                { "Department", new[] { "DepartmentID", "DepartmentName", "UpperDeptID" } },

                // --- rules / calendar ---
                { "LeaveTypes", new[] { "LeaveID", "LeaveName" } },
                { "UserRules", new[] { "BadgeNumber", "RuleID" } },
                { "DepartmentRules", new[] { "DepartmentID", "RuleID" } },
                { "Calendar", new[] { "CalendarID" } },

                // --- delegation / geo ---
                { "Deputy", new[] { "TitleID", "DeputyTitleID" } },
                { "PersonnelDeputy", new[] { "BadgeNumber", "DeputyBadgeNumber" } },
                { "Area", new[] { "AreaID" } },
                { "Position", new[] { "PositionID" } },

                // --- owned by Aria itself (created by db/01-app-objects.sql) ---
                { "PassCheckProps", new[] { "Badge", "LastChangeDate" } }
            };

        /// <summary>
        /// Objects Aria reads/writes but that no shipped evidence proves. Reported
        /// on the diagnostics page; never blocks start-up.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string[]> AdvisoryContract =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "DailyLeave", new string[0] },
                { "OverTime", new string[0] }
            };

        public static SchemaCheckResult LastResult
        {
            get
            {
                lock (SyncRoot)
                {
                    return _lastResult;
                }
            }
        }

        /// <summary>
        /// Pure comparison used by the guard and by the automated tests.
        /// </summary>
        /// <param name="presentObjects">
        /// Pairs of "Table" or "Table.Column" that exist in the database.
        /// </param>
        public static SchemaCheckResult Evaluate(IEnumerable<string> presentObjects)
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (presentObjects != null)
            {
                foreach (string item in presentObjects)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        present.Add(item.Trim());
                    }
                }
            }

            var problems = new List<string>();
            var advisories = new List<string>();

            foreach (var entry in Contract)
            {
                string table = entry.Key;
                string[] columns = entry.Value ?? new string[0];

                bool tableSeen = present.Contains(table)
                    || present.Any(p => p.StartsWith(table + ".", StringComparison.OrdinalIgnoreCase));

                if (!tableSeen)
                {
                    problems.Add("جدول «" + table + "» در پایگاه‌داده یافت نشد.");
                    continue;
                }

                foreach (string column in columns)
                {
                    // "Table" alone means "table exists" is enough; a column requirement
                    // is satisfied only by the explicit "Table.Column" pair.
                    if (!present.Contains(table + "." + column))
                    {
                        problems.Add("ستون «" + column + "» در جدول «" + table + "» یافت نشد.");
                    }
                }
            }

            foreach (var entry in AdvisoryContract)
            {
                string table = entry.Key;
                bool tableSeen = present.Contains(table)
                    || present.Any(p => p.StartsWith(table + ".", StringComparison.OrdinalIgnoreCase));

                if (!tableSeen)
                {
                    advisories.Add("جدول اختیاری «" + table + "» یافت نشد؛ اگر آریا از آن استفاده می‌کند، نصب T5 را بررسی کنید.");
                    continue;
                }

                foreach (string column in entry.Value ?? new string[0])
                {
                    if (!present.Contains(table + "." + column))
                    {
                        advisories.Add("ستون اختیاری «" + column + "» در جدول «" + table + "» یافت نشد.");
                    }
                }
            }

            if (problems.Count == 0)
            {
                string message = advisories.Count == 0
                    ? "اسکیمای پایگاه‌داده با این نسخه آریا سازگار است."
                    : "اسکیمای پایگاه‌داده با این نسخه آریا سازگار است؛ " + advisories.Count +
                      " مورد اختیاری برای بررسی گزارش شد.";

                return new SchemaCheckResult(true, true, true, null, advisories, message, DateTime.Now);
            }

            return new SchemaCheckResult(
                true,
                false,
                true,
                problems,
                advisories,
                "اسکیمای پایگاه‌داده با این نسخه آریا سازگار نیست. " +
                problems.Count + " مورد ناسازگار یافت شد؛ جزئیات در ادامه آمده است.",
                DateTime.Now);
        }

        /// <summary>
        /// Queries the live database. Never throws: connection problems are reported
        /// as an unreachable-database result so the UI can explain the situation.
        /// </summary>
        public static SchemaCheckResult Validate(Func<string, IDbConnection> connectionFactory, string connectionString)
        {
            if (connectionFactory == null || string.IsNullOrWhiteSpace(connectionString))
            {
                return new SchemaCheckResult(
                    true,
                    false,
                    false,
                    new[] { "رشته اتصال پایگاه‌داده تنظیم نشده است." },
                    null,
                    "پیکربندی پایگاه‌داده ناقص است (T5CnnStr).",
                    DateTime.Now);
            }

            const string sql = @"
SELECT TABLE_NAME + '.' + COLUMN_NAME AS ObjectName
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo';";

            try
            {
                var present = new List<string>();
                using (IDbConnection connection = connectionFactory(connectionString))
                {
                    connection.Open();
                    using (IDbCommand command = connection.CreateCommand())
                    {
                        command.CommandText = sql;
                        command.CommandTimeout = 15;
                        using (IDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                present.Add(Convert.ToString(reader.GetValue(0)));
                            }
                        }
                    }
                }
                return Evaluate(present);
            }
            catch (Exception ex)
            {
                // Deliberately no stack trace and no raw SQL text in the message:
                // this string is rendered on an anonymous page.
                return new SchemaCheckResult(
                    true,
                    false,
                    false,
                    new[] { "اتصال به پایگاه‌داده برقرار نشد: " + ex.Message },
                    null,
                    "پایگاه‌داده در دسترس نیست. سرویس SQL Server و رشته اتصال را بررسی کنید.",
                    DateTime.Now);
            }
        }

        /// <summary>
        /// Runs the guard once per process (safe to call on every start-up).
        /// Returns the cached result on subsequent calls unless force = true.
        /// </summary>
        public static SchemaCheckResult EnsureValidated(bool force = false)
        {
            lock (SyncRoot)
            {
                if (!force && _lastResult != null)
                {
                    return _lastResult;
                }

                // Automated tests and the in-memory backend have no real schema.
                if (DBInitializer.IsTestMode)
                {
                    _lastResult = SchemaCheckResult.Skipped("حالت تست/درون‌حافظه‌ای: بررسی اسکیما انجام نشد.");
                    return _lastResult;
                }

                string connectionString = ConnectionStringResolver.T5;
                _lastResult = Validate(cs => new System.Data.SqlClient.SqlConnection(cs), connectionString);
                return _lastResult;
            }
        }

        /// <summary>Clears the cached result (used by tests and by the diagnostics page).</summary>
        public static void Reset()
        {
            lock (SyncRoot)
            {
                _lastResult = null;
            }
        }
    }
}
