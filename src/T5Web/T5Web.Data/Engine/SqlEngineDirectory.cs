using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using T5Web.Domain.Models;

namespace T5Web.Data.Engine
{
    /// <summary>
    /// The same four directory reads as <see cref="PersonnelEngineDirectory"/>, issued as
    /// plain SQL over Dapper instead of through the engine's Entity Framework layer.
    ///
    /// Why it exists
    /// -------------
    /// The engine path is the only thing that forces this portal onto .NET Framework:
    /// `Personnel.dll` is a .NET Framework, x86, EF6 Database-First assembly, so loading it
    /// pins the app pool to 32-bit, requires the engine folder on the web server, and makes
    /// a .NET 8/10 port impossible. Measured (docs/16): of 49 files in the lower layers,
    /// exactly one touches the engine, and this replaces it.
    ///
    /// The columns are not guesses: engine/column-types.json is generated from the engine's
    /// own model plus the shipped DDL, and every identifier here is checked against
    /// db/schema-manifest.json by the SQL gate - the same protection the other 48 files have.
    ///
    /// Each read opens the connection itself, in its own method, on the Personnel factory.
    /// That is deliberate and not just style: tools/engine-relations.py decides by the
    /// enclosing method whether a statement aimed at the Personnel catalog is issued on the
    /// personnel connection, which is the mistake that produced BUG-01…BUG-07.
    /// </summary>
    public sealed class SqlEngineDirectory : IEngineDirectory
    {
        private readonly Func<IDbConnection> _personnelFactory;

        public SqlEngineDirectory(Func<IDbConnection> personnelFactory)
        {
            if (personnelFactory == null)
            {
                throw new ArgumentNullException(nameof(personnelFactory));
            }
            _personnelFactory = personnelFactory;
        }

        public IReadOnlyList<DepartmentNode> GetDepartments()
        {
            // [Department] has both Name (nullable, the engine model's choice) and
            // DepartmentName (not null, the DDL). COALESCE keeps a row visible even when
            // only one is filled - an empty name is what makes a chart look broken.
            const string sql = @"
SELECT [DepartmentID] AS DepartmentId,
       [UpperDeptID]  AS UpperDeptId,
       COALESCE(NULLIF([Name], N''), [DepartmentName], N'') AS DepartmentName
FROM [Department]
ORDER BY [DepartmentID];";
            try
            {
                using (IDbConnection connection = _personnelFactory())
                {
                    return connection.Query<DepartmentRecord>(sql)
                        .Select(row => new DepartmentNode
                        {
                            DepartmentId = row.DepartmentId,
                            UpperDeptId = row.UpperDeptId,
                            DepartmentName = row.DepartmentName ?? string.Empty
                        })
                        .ToList();
                }
            }
            catch (EngineAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EngineAccessException("Departments (sql)", ex);
            }
        }

        public IReadOnlyList<TitleNode> GetTitles()
        {
            const string sql = @"
SELECT [TitleID]      AS TitleId,
       [UpperTitleID] AS UpperTitleId,
       [TitleName]    AS TitleName,
       [DisplayName]  AS DisplayName,
       [Observer]     AS Observer
FROM [TitlesChart]
ORDER BY [TitleID];";
            try
            {
                using (IDbConnection connection = _personnelFactory())
                {
                    return connection.Query<TitleRecord>(sql)
                        .Select(row => new TitleNode
                        {
                            TitleId = row.TitleId,
                            UpperTitleId = row.UpperTitleId,
                            TitleName = row.TitleName ?? string.Empty,
                            DisplayName = row.DisplayName ?? string.Empty,
                            // Nullable in the table; false is the safe default, as in the engine.
                            IsObserver = row.Observer.GetValueOrDefault()
                        })
                        .ToList();
                }
            }
            catch (EngineAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EngineAccessException("TitlesChart (sql)", ex);
            }
        }

        public IReadOnlyList<KeyValuePair<int, string>> GetEmpTypes()
        {
            const string sql = @"
SELECT [EmpTypeID]   AS EmpTypeId,
       [EmpTypeName] AS EmpTypeName
FROM [EmpType]
ORDER BY [EmpTypeID];";
            try
            {
                using (IDbConnection connection = _personnelFactory())
                {
                    return connection.Query<EmpTypeRecord>(sql)
                        .Select(row => new KeyValuePair<int, string>(row.EmpTypeId, row.EmpTypeName ?? string.Empty))
                        .ToList();
                }
            }
            catch (EngineAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EngineAccessException("EmpType (sql)", ex);
            }
        }

        public IReadOnlyList<DeviceRow> GetDevices()
        {
            const string sql = @"
SELECT [MachineNumber]  AS MachineNumber,
       [MachineName]    AS MachineName,
       [GateID]         AS GateId,
       [GateName]       AS GateName,
       [ConnectionType] AS ConnectionType,
       [IP]             AS Ip,
       [Port]           AS Port,
       [SerialPort]     AS SerialPort,
       [BaudRate]       AS BaudRate,
       [SerialNo]       AS SerialNo,
       [SerialNumber]   AS SerialNumber,
       [Code]           AS Code,
       [Push]           AS Push,
       [LastStamp]      AS LastStamp
FROM [Device]
ORDER BY [MachineNumber];";
            try
            {
                using (IDbConnection connection = _personnelFactory())
                {
                    return connection.Query<DeviceRecord>(sql)
                        .Select(row => new DeviceRow
                        {
                            MachineNumber = row.MachineNumber,
                            MachineName = row.MachineName ?? string.Empty,
                            GateId = row.GateId,
                            GateName = row.GateName ?? string.Empty,
                            ConnectionType = row.ConnectionType,
                            Ip = row.Ip ?? string.Empty,
                            Port = row.Port,
                            SerialPort = row.SerialPort,
                            BaudRate = row.BaudRate,
                            // Two columns carry the same idea: the engine model prefers
                            // SerialNumber, the legacy screen labelled SerialNo. Use either.
                            SerialNo = string.IsNullOrWhiteSpace(row.SerialNumber)
                                ? (row.SerialNo ?? string.Empty)
                                : row.SerialNumber,
                            Code = row.Code ?? string.Empty,
                            Push = row.Push.GetValueOrDefault(),
                            LastStamp = FromUnixSeconds(row.LastStamp)
                        })
                        .ToList();
                }
            }
            catch (EngineAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EngineAccessException("Device (sql)", ex);
            }
        }

        /// <summary>[Device].[LastStamp] is unix seconds, and 0 means "never seen".</summary>
        private static DateTime? FromUnixSeconds(long? value)
        {
            if (!value.HasValue || value.Value <= 0)
            {
                return null;
            }
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(value.Value);
        }

        // The table's column names are aliased to these properties in the SELECT above.
        // Concrete classes, not `dynamic`: on .NET Framework `dynamic` needs a
        // Microsoft.CSharp reference this layer does not carry, and the failure would only
        // appear at run time.
        private sealed class DepartmentRecord
        {
            public int DepartmentId { get; set; }
            public int UpperDeptId { get; set; }
            public string DepartmentName { get; set; }
        }

        private sealed class TitleRecord
        {
            public int TitleId { get; set; }
            public int UpperTitleId { get; set; }
            public string TitleName { get; set; }
            public string DisplayName { get; set; }
            public bool? Observer { get; set; }
        }

        private sealed class EmpTypeRecord
        {
            public int EmpTypeId { get; set; }
            public string EmpTypeName { get; set; }
        }

        private sealed class DeviceRecord
        {
            public int MachineNumber { get; set; }
            public string MachineName { get; set; }
            public int GateId { get; set; }
            public string GateName { get; set; }
            public int ConnectionType { get; set; }
            public string Ip { get; set; }
            public int Port { get; set; }
            public int SerialPort { get; set; }
            public int BaudRate { get; set; }
            public string SerialNo { get; set; }
            public string SerialNumber { get; set; }
            public string Code { get; set; }
            public bool? Push { get; set; }
            public long? LastStamp { get; set; }
        }
    }
}
