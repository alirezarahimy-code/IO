using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            return new PersonnelSystem(BuildEntityConnectionString(connectionString));
        }

        /// <summary>
        /// The engine is a Database First Entity Framework model, so PersonnelSystem
        /// needs an *entity* connection string - the one that points at the .csdl/.ssdl/
        /// .msl resources - and not the plain ADO.NET string the portal itself uses.
        ///
        /// Handing it the ADO string is what broke the first real install: EF decided it
        /// was "Code First" and answered, on every screen that reads the organisation,
        /// "The context is being used in Code First mode with code that was generated from
        /// an EDMX file" (BUG-37). The desktop application worked because its Web.config
        /// carried the metadata=... form; the portal stored only the provider part.
        ///
        /// The model resource names are read from the assembly instead of being copied
        /// from it: the pinned build calls them DB.PersonnelMdl.*, and a rebuilt engine
        /// may not. A missing resource is a plain EngineAccessException, not an EF error
        /// three layers down.
        /// </summary>
        public static string BuildEntityConnectionString(string providerConnectionString)
        {
            return BuildEntityConnectionString(providerConnectionString, typeof(PersonnelSystem).Assembly);
        }

        public static string BuildEntityConnectionString(string providerConnectionString, Assembly engineAssembly)
        {
            if (string.IsNullOrWhiteSpace(providerConnectionString))
            {
                throw new EngineAccessException("connection", null);
            }
            if (engineAssembly == null)
            {
                throw new ArgumentNullException(nameof(engineAssembly));
            }

            string[] resources = engineAssembly.GetManifestResourceNames();
            string csdl = FindModelResource(resources, ".csdl");
            string ssdl = FindModelResource(resources, ".ssdl");
            string msl = FindModelResource(resources, ".msl");
            if (csdl == null || ssdl == null || msl == null)
            {
                throw new EngineAccessException(
                    "model",
                    new InvalidOperationException(
                        "The engine assembly has no EDMX model (csdl/ssdl/msl). Expected the pinned " +
                        "Personnel.dll from engine/engine-pin.json; found " +
                        (resources.Length == 0 ? "no resources" : string.Join(", ", resources.Where(r => r.EndsWith(".csdl") || r.EndsWith(".ssdl") || r.EndsWith(".msl")).ToArray())) + "."));
            }

            // res://*/Name tells EF to look for that suffix in any loaded assembly, which
            // is where Personnel.dll ends up: the folder IIS runs from.
            return "metadata=res://*/" + csdl + "|res://*/" + ssdl + "|res://*/" + msl +
                   ";provider=System.Data.SqlClient;provider connection string=\"" +
                   providerConnectionString + "\"";
        }

        private static string FindModelResource(string[] resources, string suffix)
        {
            return resources
                .Where(r => r.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Length)
                .FirstOrDefault();
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
