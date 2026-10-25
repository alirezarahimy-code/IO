using System;
using System.Collections.Generic;
using System.Linq;
using Personnel;
using Personnel.Model;
using T5Web.Domain.Models;

namespace T5Web.Data.Engine
{
    /// <summary>
    /// Reads organisational master data through the engine's own data-access layer
    /// (`Personnel.DB.*DA`) instead of hand-written SQL.
    ///
    /// Why this exists
    /// ---------------
    /// Phase 1 of this portal read four entities with hand-written SQL, and that is
    /// exactly where seven identifiers that do not exist were found (BUG-01…BUG-07):
    /// `[UserInfos]`, `[Departments]`, `NationalCode`, `UserInfoTitles.TitleID`,
    /// `TitlesChart.DepartmentID`, `[Description]` and seven invented Request columns.
    /// The engine's DA layer already knows the real schema, so reading through it makes
    /// that whole class of bug impossible.
    ///
    /// Why reads only
    /// --------------
    /// The engine's `AddNew`/`Edit`/`Delete` methods take a `DBManager` instance, and in
    /// the pinned build that instance binds a process-wide event bridge
    /// (`DBManager.RaiseEvent`/`UpdateDB`) used by the desktop application. Sharing it
    /// from a web process would couple the portal to desktop state, and `UpdateDB` is on
    /// the forbidden list (removed in engine 3.9.2). Writes therefore stay in
    /// `SqlAdminRepository`, which uses `SchemaMap` and is verified against
    /// db/schema-manifest.json by tools/verify-sql-schema.py.
    ///
    /// Verified against the pinned build (t5web-3.5.7):
    ///   DepartmentDA.GetAll()            -> List&lt;Personnel.Model.Department&gt;
    ///   TitleChartDA.GetAll()            -> List&lt;Personnel.Model.TitleChart&gt;
    ///   EmpTypeDA.GetAll()               -> List&lt;Personnel.Model.EmpType&gt;
    ///   DeviceDA.GetAll()                -> List&lt;Personnel.Model.Device&gt;
    /// A type or member drift here is caught by tools/lint-csharp.py against
    /// engine/engine-pin.json, so it fails the build instead of the shift.
    /// </summary>
    public sealed class PersonnelEngineDirectory : IEngineDirectory
    {
        private readonly Func<string> _connectionString;

        public PersonnelEngineDirectory(Func<string> connectionString)
        {
            if (connectionString == null)
            {
                throw new ArgumentNullException(nameof(connectionString));
            }
            _connectionString = connectionString;
        }

        /// <summary>
        /// One PersonnelSystem per operation.
        ///
        /// A shared singleton was rejected: PersonnelSystem opens its own connection
        /// state and the desktop original is single-threaded, so reusing one instance
        /// across requests would be a data race. Construction is cheap (the connection
        /// itself is pooled by ADO.NET).
        /// </summary>
        private PersonnelSystem OpenSystem()
        {
            string connectionString = _connectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new EngineAccessException("connection", null);
            }
            return new PersonnelSystem(connectionString);
        }

        public IReadOnlyList<DepartmentNode> GetDepartments()
        {
            try
            {
                List<Department> rows = OpenSystem().DB.Departments.GetAll();
                if (rows == null)
                {
                    return new List<DepartmentNode>();
                }
                return rows
                    .Select(d => new DepartmentNode
                    {
                        DepartmentId = d.DepartmentID,
                        UpperDeptId = d.UpperDeptID,
                        // The engine model calls the column "Name"; the table calls it
                        // DepartmentName. The model is right - it is what the engine maps.
                        DepartmentName = d.Name ?? string.Empty
                    })
                    .ToList();
            }
            catch (EngineAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EngineAccessException("Departments.GetAll", ex);
            }
        }

        public IReadOnlyList<TitleNode> GetTitles()
        {
            try
            {
                List<TitleChart> rows = OpenSystem().DB.TitlesCharts.GetAll();
                if (rows == null)
                {
                    return new List<TitleNode>();
                }
                return rows
                    .Select(t => new TitleNode
                    {
                        TitleId = t.TitleID,
                        UpperTitleId = t.UpperTitleID,
                        TitleName = t.TitleName ?? string.Empty,
                        DisplayName = t.DisplayName ?? string.Empty,
                        // Nullable in the engine model; false is the safe default.
                        IsObserver = t.Observer.HasValue && t.Observer.Value
                    })
                    .ToList();
            }
            catch (EngineAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EngineAccessException("TitlesCharts.GetAll", ex);
            }
        }

        public IReadOnlyList<KeyValuePair<int, string>> GetEmpTypes()
        {
            try
            {
                List<EmpType> rows = OpenSystem().DB.EmpTypes.GetAll();
                if (rows == null)
                {
                    return new List<KeyValuePair<int, string>>();
                }
                return rows
                    .Select(e => new KeyValuePair<int, string>(e.EmpTypeID, e.EmpTypeName ?? string.Empty))
                    .ToList();
            }
            catch (EngineAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EngineAccessException("EmpTypes.GetAll", ex);
            }
        }

        public IReadOnlyList<DeviceRow> GetDevices()
        {
            try
            {
                List<Device> rows = OpenSystem().DB.Devices.GetAll();
                if (rows == null)
                {
                    return new List<DeviceRow>();
                }
                return rows
                    .Select(d => new DeviceRow
                    {
                        MachineNumber = d.MachineNumber,
                        MachineName = d.MachineName ?? string.Empty,
                        GateId = d.GateID,
                        GateName = d.GateName ?? string.Empty,
                        // ConnectionType is an engine enum (TCPIP/USB/SerialPort);
                        // the portal stores the same three numbers.
                        ConnectionType = (int)d.ConnectionType,
                        Ip = d.IP ?? string.Empty,
                        Port = d.Port,
                        SerialPort = d.SerialPort,
                        BaudRate = d.BaudRate,
                        SerialNo = d.SerialNumber ?? string.Empty,
                        Code = d.Code ?? string.Empty,
                        Push = d.Push,
                        LastStamp = FromUnixSeconds(d.LastStamp)
                    })
                    .ToList();
            }
            catch (EngineAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EngineAccessException("Devices.GetAll", ex);
            }
        }

        /// <summary>[Device].[LastStamp] is unix seconds, and 0 means "never seen".</summary>
        private static DateTime? FromUnixSeconds(long value)
        {
            if (value <= 0)
            {
                return null;
            }
            try
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(value);
            }
            catch (ArgumentOutOfRangeException)
            {
                // A corrupt counter must not break the whole device list.
                return null;
            }
        }
    }
}
