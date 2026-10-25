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
    /// The leave-entitlement link table.
    ///
    /// Column evidence:
    ///   * <c>[EmpType].[EmpTypeID]</c>, <c>[EmpType].[EmpTypeName]</c> - Personnel catalog;
    ///   * <c>[LeaveType].[LeaveID]</c>, <c>[LeaveType].[LeaveName]</c> - engine catalog;
    ///   * <c>[EmpTypeLeave].[EmpTypeID]</c>, <c>[EmpTypeLeave].[LeaveTypeID]</c> - engine
    ///     catalog, both <c>int</c> (engine ORM entity, no DDL statement exists for it).
    ///
    /// The link table has no surrogate key: the pair *is* the key. Both writes are
    /// therefore written as idempotent statements (`IF NOT EXISTS` / `DELETE ... WHERE`),
    /// so a double click on the matrix cannot produce a duplicate row that later makes the
    /// engine's join return the same leave type twice.
    /// </summary>
    public sealed class SqlEntitlementStore : IEntitlementStore
    {
        private readonly Func<IDbConnection> _engineFactory;
        private readonly Func<IDbConnection> _personnelFactory;

        public SqlEntitlementStore(Func<IDbConnection> engineFactory, Func<IDbConnection> personnelFactory)
        {
            if (engineFactory == null)
            {
                throw new ArgumentNullException(nameof(engineFactory));
            }
            _engineFactory = engineFactory;
            _personnelFactory = personnelFactory ?? engineFactory;
        }

        /// <summary>Opens the Personnel catalog (employment types live there).</summary>
        private IDbConnection OpenPersonnel()
        {
            IDbConnection connection = _personnelFactory();
            connection.Open();
            return connection;
        }

        /// <summary>Opens the engine catalog (leave types, the link table).</summary>
        private IDbConnection OpenEngine()
        {
            IDbConnection connection = _engineFactory();
            connection.Open();
            return connection;
        }

        public IReadOnlyList<KeyValuePair<int, string>> GetEmpTypes()
        {
            const string sql = @"
SELECT [EmpTypeID] AS Id, ISNULL([EmpTypeName], N'') AS Name
FROM [EmpType]
ORDER BY [EmpTypeName];";

            using (IDbConnection connection = OpenPersonnel())
            {
                return connection.Query<Named>(sql)
                    .Select(r => new KeyValuePair<int, string>(r.Id, (r.Name ?? string.Empty).Trim()))
                    .ToList();
            }
        }

        public IReadOnlyList<KeyValuePair<int, string>> GetLeaveTypes()
        {
            const string sql = @"
SELECT [LeaveID] AS Id, ISNULL([LeaveName], N'') AS Name
FROM [LeaveType]
ORDER BY [LeaveName];";

            using (IDbConnection connection = OpenEngine())
            {
                return connection.Query<Named>(sql)
                    .Select(r => new KeyValuePair<int, string>(r.Id, (r.Name ?? string.Empty).Trim()))
                    .ToList();
            }
        }

        public IReadOnlyList<EntitlementRow> GetPairs()
        {
            const string sql = @"SELECT [EmpTypeID] AS EmpTypeId, [LeaveTypeID] AS LeaveTypeId FROM [EmpTypeLeave];";
            using (IDbConnection connection = OpenEngine())
            {
                return connection.Query<EntitlementRow>(sql).ToList();
            }
        }

        public OperationResult Set(int empTypeId, int leaveTypeId, bool grant)
        {
            if (empTypeId <= 0 || leaveTypeId <= 0)
            {
                return OperationResult.Fail("نوع استخدام یا نوع مرخصی نامعتبر است.");
            }

            try
            {
                using (IDbConnection connection = OpenEngine())
                {
                    if (grant)
                    {
                        const string insert = @"
IF NOT EXISTS (SELECT 1 FROM [EmpTypeLeave] WHERE [EmpTypeID] = @Emp AND [LeaveTypeID] = @Leave)
    INSERT INTO [EmpTypeLeave] ([EmpTypeID], [LeaveTypeID]) VALUES (@Emp, @Leave);";
                        connection.Execute(insert, new { Emp = empTypeId, Leave = leaveTypeId });
                    }
                    else
                    {
                        const string delete = @"DELETE FROM [EmpTypeLeave] WHERE [EmpTypeID] = @Emp AND [LeaveTypeID] = @Leave;";
                        connection.Execute(delete, new { Emp = empTypeId, Leave = leaveTypeId });
                    }
                }
                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("ثبت سهمیه ناموفق بود: " + FirstLine(ex.Message));
            }
        }

        private static string FirstLine(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }
            string first = message.Split('\n')[0].Trim();
            return first.Length > 220 ? first.Substring(0, 220) + "…" : first;
        }

        private sealed class Named
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}
