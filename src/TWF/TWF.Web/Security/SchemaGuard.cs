using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
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

        public SchemaCheckResult(
            bool wasChecked,
            bool isCompatible,
            bool databaseReachable,
            IEnumerable<string> problems,
            string message,
            DateTime checkedAt)
        {
            WasChecked = wasChecked;
            IsCompatible = isCompatible;
            DatabaseReachable = databaseReachable;
            _problems = problems == null ? new List<string>() : new List<string>(problems);
            Message = message ?? string.Empty;
            CheckedAt = checkedAt;
        }

        /// <summary>False when the guard deliberately skipped (unit tests / in-memory mode).</summary>
        public bool WasChecked { get; private set; }

        /// <summary>True only when the database satisfies the whole contract.</summary>
        public bool IsCompatible { get; private set; }

        public bool DatabaseReachable { get; private set; }

        public IReadOnlyList<string> Problems
        {
            get { return _problems.AsReadOnly(); }
        }

        public string Message { get; private set; }

        public DateTime CheckedAt { get; private set; }

        internal static SchemaCheckResult Skipped(string reason)
        {
            return new SchemaCheckResult(false, true, true, null, reason, DateTime.Now);
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
    /// </summary>
    public static class SchemaGuard
    {
        private static readonly object SyncRoot = new object();
        private static SchemaCheckResult _lastResult;

        /// <summary>
        /// Tables the portal reads/writes directly, with the columns that must exist.
        /// Table entries with no columns only require the table itself.
        /// Keep in sync with docs/02-shared-database-contract.md.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string[]> Contract =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                // --- core request / workflow (Aria writes) ---
                { "Request", new[] { "RequestID", "BadgeNumber", "WorkflowType", "IsSuspend" } },
                { "RequestAccept", new[] { "RequestID" } },
                { "Workflow", new string[0] },

                // --- operational results written after the final approval ---
                { "DailyLeave", new string[0] },
                { "HourlyLeave", new string[0] },
                { "Record", new[] { "BadgeNumber", "CheckTime" } },
                { "OverTime", new string[0] },

                // --- delegation / org chart ---
                { "Deputy", new string[0] },
                { "PersonnelDeputy", new string[0] },
                { "UserInfo", new[] { "BadgeNumber" } },
                { "Calendar", new[] { "CalendarID" } },
                { "LeaveTypes", new[] { "LeaveTypeID", "LeaveName" } },
                { "UserRules", new string[0] },
                { "DepartmentRules", new string[0] },

                // --- geo / position ---
                { "Area", new string[0] },
                { "Position", new string[0] },

                // --- owned by Aria itself (created by db/01-app-objects.sql) ---
                { "PassCheckProps", new[] { "Badge", "LastChangeDate" } }
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

            if (problems.Count == 0)
            {
                return new SchemaCheckResult(
                    true,
                    true,
                    true,
                    null,
                    "اسکیمای پایگاه‌داده با این نسخه آریا سازگار است.",
                    DateTime.Now);
            }

            return new SchemaCheckResult(
                true,
                false,
                true,
                problems,
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
                return new SchemaCheckResult(
                    true,
                    false,
                    false,
                    new[] { "اتصال به پایگاه‌داده برقرار نشد: " + ex.Message },
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
