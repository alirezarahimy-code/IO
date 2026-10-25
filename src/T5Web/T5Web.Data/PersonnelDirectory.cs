using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;

namespace T5Web.Data
{
    /// <summary>
    /// Reads the Personnel catalog — the second of the two databases this portal lives on.
    ///
    /// The T5 engine splits its data in two: attendance, requests, leave, shifts and the
    /// engine's own model live in <c>ReportT5V3</c> (connection string <c>T5CnnStr</c>),
    /// while people, departments, titles, devices and gates live in <c>Personnel</c>
    /// (<c>PersonnelCnnStr</c>). The legacy portal joined across the two catalogs, which
    /// only works when both sit on the same server and the SQL login can read both.
    ///
    /// This rewrite never does that (docs/09): a statement touches one catalog, and the
    /// display fields (a person's name, a machine's name, a department's name) are
    /// completed here and merged in memory. One code path, no cross-database assumption,
    /// and every query stays testable against a single database.
    /// </summary>
    public sealed class PersonnelDirectory
    {
        private readonly Func<IDbConnection> _connectionFactory;

        public PersonnelDirectory(Func<IDbConnection> connectionFactory)
        {
            if (connectionFactory == null)
            {
                throw new ArgumentNullException(nameof(connectionFactory));
            }
            _connectionFactory = connectionFactory;
        }

        private IDbConnection Open()
        {
            IDbConnection connection = _connectionFactory();
            connection.Open();
            return connection;
        }

        private static long[] Keys(IEnumerable<long> values)
        {
            return values == null
                ? new long[0]
                : values.Where(v => v > 0).Distinct().Take(2000).ToArray();
        }

        /// <summary>Badge number → "First Last". Empty string when the person is gone.</summary>
        public IReadOnlyDictionary<long, string> Names(IEnumerable<long> badges)
        {
            long[] keys = Keys(badges);
            var names = new Dictionary<long, string>();
            if (keys.Length == 0)
            {
                return names;
            }

            const string sql = @"
SELECT [BadgeNumber] AS BadgeNumber,
       ISNULL([FirstName], N'') + N' ' + ISNULL([LastName], N'') AS PersonName
FROM [UserInfo]
WHERE [BadgeNumber] IN @Badges;";

            using (IDbConnection connection = Open())
            {
                foreach (var row in connection.Query<PersonNameRow>(sql, new { Badges = keys }))
                {
                    names[row.BadgeNumber] = (row.PersonName ?? string.Empty).Trim();
                }
            }
            return names;
        }

        /// <summary>Machine number → device name, for the records grid.</summary>
        public IReadOnlyDictionary<int, string> MachineNames(IEnumerable<int> machineNumbers)
        {
            int[] keys = machineNumbers == null
                ? new int[0]
                : machineNumbers.Where(n => n > 0).Distinct().Take(2000).ToArray();
            var names = new Dictionary<int, string>();
            if (keys.Length == 0)
            {
                return names;
            }

            const string sql = @"
SELECT [MachineNumber] AS MachineNumber, ISNULL([MachineName], N'') AS MachineName
FROM [Device]
WHERE [MachineNumber] IN @Machines;";

            using (IDbConnection connection = Open())
            {
                foreach (var row in connection.Query<DeviceNameRow>(sql, new { Machines = keys }))
                {
                    names[row.MachineNumber] = row.MachineName ?? string.Empty;
                }
            }
            return names;
        }

        /// <summary>Department id → name.</summary>
        public IReadOnlyDictionary<int, string> DepartmentNames()
        {
            const string sql = @"
SELECT [DepartmentID] AS DepartmentId, ISNULL([DepartmentName], N'') AS DepartmentName
FROM [Department];";

            var names = new Dictionary<int, string>();
            using (IDbConnection connection = Open())
            {
                foreach (var row in connection.Query<DepartmentNameRow>(sql))
                {
                    names[row.DepartmentId] = row.DepartmentName ?? string.Empty;
                }
            }
            return names;
        }

        /// <summary>Title id → name, for the workflow screen.</summary>
        public IReadOnlyDictionary<int, string> TitleNames()
        {
            const string sql = @"
SELECT [TitleID] AS TitleId, ISNULL([TitleName], N'') AS TitleName FROM [TitlesChart];";

            var names = new Dictionary<int, string>();
            using (IDbConnection connection = Open())
            {
                foreach (var row in connection.Query<TitleNameRow>(sql))
                {
                    names[row.TitleId] = row.TitleName ?? string.Empty;
                }
            }
            return names;
        }

        /// <summary>
        /// Active personnel, used by the monthly summary: the engine's own aggregates are
        /// per badge, so the list of people has to come from here first.
        /// </summary>
        public IReadOnlyList<PersonRef> People(long? badgeNumber, int take)
        {
            const string sql = @"
SELECT TOP (@Take)
       p.[BadgeNumber] AS BadgeNumber,
       ISNULL(p.[FirstName], N'') + N' ' + ISNULL(p.[LastName], N'') AS PersonName,
       ISNULL(p.[DeptID], 0) AS DepartmentId
FROM [UserInfo] p
WHERE (@BadgeNumber = 0 OR p.[BadgeNumber] = @BadgeNumber)
ORDER BY p.[BadgeNumber];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<PersonRef>(sql, new
                {
                    Take = take <= 0 ? 200 : Math.Min(take, 1000),
                    BadgeNumber = badgeNumber ?? 0
                }).ToList();
            }
        }

        /// <summary>
        /// Badge numbers whose name (or badge number itself) matches a search term. The
        /// leave screen filters by person, but the leave ledger lives in the other
        /// catalog, so the filter is resolved here first.
        /// </summary>
        public IReadOnlyList<long> BadgesMatching(string term, int take)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return new List<long>();
            }

            const string sql = @"
SELECT TOP (@Take) p.[BadgeNumber] AS BadgeNumber
FROM [UserInfo] p
WHERE p.[FirstName] LIKE N'%' + @Term + N'%'
   OR p.[LastName] LIKE N'%' + @Term + N'%'
   OR CAST(p.[BadgeNumber] AS nvarchar(20)) = @Term
ORDER BY p.[BadgeNumber];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<long>(sql, new
                {
                    Term = term.Trim(),
                    Take = take <= 0 ? 500 : Math.Min(take, 2000)
                }).ToList();
            }
        }

        private sealed class PersonNameRow
        {
            public long BadgeNumber { get; set; }
            public string PersonName { get; set; }
        }

        private sealed class DeviceNameRow
        {
            public int MachineNumber { get; set; }
            public string MachineName { get; set; }
        }

        private sealed class DepartmentNameRow
        {
            public int DepartmentId { get; set; }
            public string DepartmentName { get; set; }
        }

        private sealed class TitleNameRow
        {
            public int TitleId { get; set; }
            public string TitleName { get; set; }
        }
    }

    /// <summary>One person as the Personnel catalog knows them.</summary>
    public sealed class PersonRef
    {
        public long BadgeNumber { get; set; }
        public string PersonName { get; set; }
        public int DepartmentId { get; set; }
    }
}
