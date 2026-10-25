using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Dapper;
using T5Web.Data.Engine;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// Production storage: Dapper over the shared T5 database.
    ///
    /// Read/write policy (docs/02-shared-database-contract.md, R1/R5):
    ///   * engine tables are read here; the only engine table this portal writes is
    ///     [UserInfo]/[Device]/[Workflow], which is exactly what the admin portal is
    ///     for and what the legacy T5V3Web already did;
    ///   * the portal's own state (access matrix, audit trail) lives in tables it owns
    ///     and creates (db/01-t5web-objects.sql), so the engine schema never grows
    ///     portal-specific columns.
    ///
    /// Every identifier comes from <see cref="SchemaMap"/>; tools/verify-sql-schema.py
    /// checks those against db/schema-manifest.json.
    /// </summary>
    public sealed class SqlAdminRepository : IAdminRepository
    {
        private readonly Func<IDbConnection> _connectionFactory;
        private readonly IEngineDirectory _engine;

        /// <param name="connectionFactory">Opens the shared T5 connection.</param>
        /// <param name="engine">
        /// Organisational master data source. When supplied (the production path), the
        /// four read paths below delegate to the engine's own DA layer; when null, the
        /// SQL fallback is used. See Engine/PersonnelEngineDirectory.cs for why reads
        /// delegate and writes do not.
        /// </param>
        public SqlAdminRepository(Func<IDbConnection> connectionFactory, IEngineDirectory engine = null)
        {
            if (connectionFactory == null)
            {
                throw new ArgumentNullException(nameof(connectionFactory));
            }
            _connectionFactory = connectionFactory;
            _engine = engine;
        }

        /// <summary>True when reads are served by the engine's data-access layer.</summary>
        public bool UsesEngineDirectory
        {
            get { return _engine != null; }
        }

        private IDbConnection Open()
        {
            IDbConnection connection = _connectionFactory();
            connection.Open();
            return connection;
        }

        // ------------------------------------------------------------------ session

        public PersonnelRow FindUserByUserName(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                return null;
            }

            const string sql = @"
SELECT TOP 1
    u.[BadgeNumber], u.[EmployeeNumber], u.[FirstName], u.[LastName], u.[FatherName],
    u.[NationalNo], u.[Phone], u.[Email], u.[DeptID], u.[EmpTypeID], u.[CertificateID],
    u.[HiredDate], u.[FiredDate], u.[Gender], u.[ProgramUser], u.[WebUser],
    u.[DeviceEnable], ISNULL(u.[UserName], N'') AS UserName,
    ISNULL(u.[Password], N'') AS Password
FROM [UserInfo] u
WHERE u.[UserName] = @UserName;";

            using (IDbConnection connection = Open())
            {
                return MapPersonnel(connection.QueryFirstOrDefault<PersonnelRecord>(sql, new { UserName = userName.Trim() }));
            }
        }

        public string GetTitleName(long badgeNumber)
        {
            const string sql = @"
SELECT TOP 1 ISNULL(tc.[TitleName], N'')
FROM [UserInfoTitles] uit
INNER JOIN [TitlesChart] tc ON tc.[TitleID] = uit.[TitleChartID]
WHERE uit.[BadgeNumber] = @BadgeNumber
ORDER BY uit.[ID] DESC;";

            using (IDbConnection connection = Open())
            {
                return connection.ExecuteScalar<string>(sql, new { BadgeNumber = badgeNumber }) ?? string.Empty;
            }
        }

        public int GetTitleId(long badgeNumber)
        {
            const string sql = @"
SELECT TOP 1 uit.[TitleChartID]
FROM [UserInfoTitles] uit
WHERE uit.[BadgeNumber] = @BadgeNumber
  AND (@Today IS NULL OR ((uit.[FromDate] IS NULL OR uit.[FromDate] <= @Today)
                       AND (uit.[ToDate] IS NULL OR uit.[ToDate] >= @Today)))
ORDER BY uit.[ID] DESC;";

            using (IDbConnection connection = Open())
            {
                return connection.ExecuteScalar<int?>(sql, new { BadgeNumber = badgeNumber, Today = DateTime.Today })
                       ?? 0;
            }
        }

        public IReadOnlyList<PermissionRow> GetPermissions(long badgeNumber)
        {
            // BadgeNumber <= 0 means "all rows" (used only for counting administrators).
            const string sql = @"
SELECT p.[BadgeNumber], p.[Feature], p.[Level],
       ISNULL(u.[FirstName], N'') + N' ' + ISNULL(u.[LastName], N'') AS PersonName
FROM [T5WebAccess] p
LEFT JOIN [UserInfo] u ON u.[BadgeNumber] = p.[BadgeNumber]
WHERE (@BadgeNumber <= 0 OR p.[BadgeNumber] = @BadgeNumber);";

            using (IDbConnection connection = Open())
            {
                return connection.Query<PermissionRecord>(sql, new { BadgeNumber = badgeNumber })
                    .Select(r => new PermissionRow
                    {
                        BadgeNumber = r.BadgeNumber,
                        PersonName = r.PersonName ?? string.Empty,
                        Feature = (AdminFeature)r.Feature,
                        Level = (AccessLevel)r.Level
                    })
                    .ToList();
            }
        }

        // ------------------------------------------------------------ organisation

        public IReadOnlyList<DepartmentNode> GetDepartments()
        {
            if (_engine != null)
            {
                // Member counts are a portal concern, so they are added here instead of
                // asking the engine DA to count rows it does not model.
                IReadOnlyList<DepartmentNode> engineRows = _engine.GetDepartments();
                var counts = CountMembersByDepartment();
                foreach (DepartmentNode node in engineRows)
                {
                    int count;
                    node.MemberCount = counts.TryGetValue(node.DepartmentId, out count) ? count : 0;
                }
                return engineRows;
            }

            const string sql = @"
SELECT d.[DepartmentID] AS DepartmentId,
       d.[UpperDeptID] AS UpperDeptId,
       d.[DepartmentName] AS DepartmentName,
       (SELECT COUNT(1) FROM [UserInfo] u WHERE u.[DeptID] = d.[DepartmentID]) AS MemberCount
FROM [Department] d
ORDER BY d.[DepartmentName];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<DepartmentNode>(sql).ToList();
            }
        }

        public IReadOnlyList<TitleNode> GetTitles()
        {
            if (_engine != null)
            {
                return _engine.GetTitles();
            }

            const string sql = @"
SELECT t.[TitleID] AS TitleId,
       t.[UpperTitleID] AS UpperTitleId,
       t.[TitleName] AS TitleName,
       ISNULL(t.[DisplayName], N'') AS DisplayName,
       ISNULL(t.[Observer], 0) AS IsObserver
FROM [TitlesChart] t
ORDER BY t.[TitleName];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<TitleNode>(sql).ToList();
            }
        }

        public IReadOnlyList<GateRow> GetGates()
        {
            const string sql = @"
SELECT g.[GateID] AS GateId,
       g.[UpperGateID] AS UpperGateId,
       g.[GateName] AS GateName,
       (SELECT COUNT(1) FROM [Device] d WHERE d.[GateID] = g.[GateID]) AS DeviceCount
FROM [Gate] g
ORDER BY g.[GateName];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<GateRow>(sql).ToList();
            }
        }

        public IReadOnlyList<KeyValuePair<int, string>> GetEmpTypes()
        {
            if (_engine != null)
            {
                return _engine.GetEmpTypes();
            }

            const string sql = @"
SELECT e.[EmpTypeID] AS EmpTypeId, ISNULL(e.[EmpTypeName], N'') AS EmpTypeName
FROM [EmpType] e
ORDER BY e.[EmpTypeName];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<EmpTypeRecord>(sql)
                    .Select(r => new KeyValuePair<int, string>(r.EmpTypeId, r.EmpTypeName ?? string.Empty))
                    .ToList();
            }
        }

        public OperationResult MoveDepartment(int departmentId, int newParentId)
        {
            const string sql = @"
UPDATE [Department] SET [UpperDeptID] = @NewParentId WHERE [DepartmentID] = @DepartmentId;";

            try
            {
                using (IDbConnection connection = Open())
                {
                    int affected = connection.Execute(sql, new { DepartmentId = departmentId, NewParentId = newParentId });
                    return affected == 1
                        ? OperationResult.Ok()
                        : OperationResult.Fail("واحد سازمانی یافت نشد.");
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("جابه‌جایی واحد ناموفق بود: " + ex.Message);
            }
        }

        private Dictionary<int, int> CountMembersByDepartment()
        {
            const string sql = @"
SELECT [DeptID] AS DeptId, COUNT(1) AS MemberCount
FROM [UserInfo]
GROUP BY [DeptID];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<DeptCountRecord>(sql)
                    .ToDictionary(r => r.DeptId, r => r.MemberCount);
            }
        }

        private sealed class DeptCountRecord
        {
            public int DeptId { get; set; }
            public int MemberCount { get; set; }
        }

        // --------------------------------------------------------------- personnel

        public IReadOnlyList<PersonnelRow> SearchPersonnel(string term, int? departmentId, bool includeFormer, int take)
        {
            const string sql = @"
SELECT TOP (@Take)
    u.[BadgeNumber], u.[EmployeeNumber], u.[FirstName], u.[LastName], u.[FatherName],
    u.[NationalNo], u.[Phone], u.[Email], u.[DeptID], u.[EmpTypeID], u.[CertificateID],
    u.[HiredDate], u.[FiredDate], u.[Gender], u.[ProgramUser], u.[WebUser],
    u.[DeviceEnable], ISNULL(u.[UserName], N'') AS UserName,
    ISNULL(d.[DepartmentName], N'') AS DeptName,
    ISNULL(e.[EmpTypeName], N'') AS EmpTypeName
FROM [UserInfo] u
LEFT JOIN [Department] d ON d.[DepartmentID] = u.[DeptID]
LEFT JOIN [EmpType] e ON e.[EmpTypeID] = u.[EmpTypeID]
WHERE (@Term = N''
       OR u.[FirstName] LIKE @Like
       OR u.[LastName] LIKE @Like
       OR u.[EmployeeNumber] LIKE @Like
       OR u.[NationalNo] LIKE @Like
       OR CAST(u.[BadgeNumber] AS nvarchar(20)) LIKE @Like)
  AND (@DeptId IS NULL OR u.[DeptID] = @DeptId)
  AND (@IncludeFormer = 1 OR u.[FiredDate] IS NULL)
ORDER BY u.[LastName], u.[FirstName];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<PersonnelRecord>(sql, new
                {
                    Take = take,
                    Term = term ?? string.Empty,
                    Like = "%" + (term ?? string.Empty) + "%",
                    DeptId = departmentId,
                    IncludeFormer = includeFormer ? 1 : 0
                }).Select(r => (PersonnelRow)MapPersonnel(r)).ToList();
            }
        }

        public PersonnelRow GetPersonnel(long badgeNumber)
        {
            const string sql = @"
SELECT TOP 1
    u.[BadgeNumber], u.[EmployeeNumber], u.[FirstName], u.[LastName], u.[FatherName],
    u.[NationalNo], u.[Phone], u.[Email], u.[DeptID], u.[EmpTypeID], u.[CertificateID],
    u.[HiredDate], u.[FiredDate], u.[Gender], u.[ProgramUser], u.[WebUser],
    u.[DeviceEnable], ISNULL(u.[UserName], N'') AS UserName,
    ISNULL(d.[DepartmentName], N'') AS DeptName
FROM [UserInfo] u
LEFT JOIN [Department] d ON d.[DepartmentID] = u.[DeptID]
WHERE u.[BadgeNumber] = @BadgeNumber;";

            using (IDbConnection connection = Open())
            {
                return MapPersonnel(connection.QueryFirstOrDefault<PersonnelRecord>(sql, new { BadgeNumber = badgeNumber }));
            }
        }

        public OperationResult UpdatePersonnel(PersonnelRow row)
        {
            if (row == null)
            {
                return OperationResult.Fail("رکورد پرسنل خالی است.");
            }

            // Only the fields an administrator may change are written. Password, photo,
            // device counters and the badge itself are deliberately excluded: they are
            // owned by other screens and by the engine's own synchronisation.
            const string sql = @"
UPDATE [UserInfo]
SET [FirstName] = @FirstName,
    [LastName] = @LastName,
    [FatherName] = @FatherName,
    [NationalNo] = @NationalNo,
    [Phone] = @Phone,
    [Email] = @Email,
    [DeptID] = @DeptId,
    [EmpTypeID] = @EmpTypeId,
    [HiredDate] = @HiredDate,
    [FiredDate] = @FiredDate,
    [WebUser] = @WebUser,
    [DeviceEnable] = @DeviceEnable
WHERE [BadgeNumber] = @BadgeNumber;";

            try
            {
                using (IDbConnection connection = Open())
                {
                    int affected = connection.Execute(sql, new
                    {
                        row.FirstName,
                        row.LastName,
                        row.FatherName,
                        row.NationalNo,
                        row.Phone,
                        row.Email,
                        row.DeptId,
                        row.EmpTypeId,
                        HiredDate = row.HiredDate,
                        FiredDate = row.FiredDate,
                        WebUser = row.IsWebUser ? 1 : 0,
                        DeviceEnable = row.DeviceEnabled ? 1 : 0,
                        row.BadgeNumber
                    });

                    return affected == 1
                        ? OperationResult.Ok()
                        : OperationResult.Fail("رکورد پرسنل یافت نشد.");
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("ثبت تغییرات ناموفق بود: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------- devices

        public IReadOnlyList<DeviceRow> GetDevices(int? gateId)
        {
            if (_engine != null)
            {
                IReadOnlyList<DeviceRow> all = _engine.GetDevices();
                if (!gateId.HasValue || gateId.Value <= 0)
                {
                    return all;
                }
                return all.Where(d => d.GateId == gateId.Value).ToList();
            }

            const string sql = @"
SELECT d.[MachineNumber] AS MachineNumber,
       d.[MachineName] AS MachineName,
       d.[GateID] AS GateId,
       ISNULL(g.[GateName], N'') AS GateName,
       d.[ConnectionType] AS ConnectionType,
       ISNULL(d.[IP], N'') AS Ip,
       d.[Port] AS Port,
       d.[SerialPort] AS SerialPort,
       d.[BaudRate] AS BaudRate,
       ISNULL(d.[SerialNo], N'') AS SerialNo,
       ISNULL(d.[Code], N'') AS Code,
       d.[Push] AS Push,
       d.[LastStamp] AS LastStamp
FROM [Device] d
LEFT JOIN [Gate] g ON g.[GateID] = d.[GateID]
WHERE (@GateId IS NULL OR d.[GateID] = @GateId)
ORDER BY d.[MachineNumber];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<DeviceRecord>(sql, new { GateId = gateId })
                    .Select(r => new DeviceRow
                    {
                        MachineNumber = r.MachineNumber,
                        MachineName = r.MachineName ?? string.Empty,
                        GateId = r.GateId,
                        GateName = r.GateName ?? string.Empty,
                        ConnectionType = r.ConnectionType,
                        Ip = r.Ip ?? string.Empty,
                        Port = r.Port,
                        SerialPort = r.SerialPort,
                        BaudRate = r.BaudRate,
                        SerialNo = r.SerialNo ?? string.Empty,
                        Code = r.Code ?? string.Empty,
                        Push = r.Push,
                        LastStamp = FromUnixSeconds(r.LastStamp)
                    })
                    .ToList();
            }
        }

        private static DateTime? FromUnixSeconds(long? raw)
        {
            if (!raw.HasValue || raw.Value <= 0)
            {
                return null;
            }
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(raw.Value);
        }

        public OperationResult UpdateDevice(DeviceRow row)
        {
            if (row == null || row.MachineNumber <= 0)
            {
                return OperationResult.Fail("شماره دستگاه نامعتبر است.");
            }
            if (string.IsNullOrWhiteSpace(row.MachineName))
            {
                return OperationResult.Fail("نام دستگاه الزامی است.");
            }
            if (row.ConnectionType == 0)
            {
                if (!IsValidIp(row.Ip))
                {
                    return OperationResult.Fail("نشانی IP نامعتبر است.");
                }
                if (row.Port <= 0 || row.Port > 65535)
                {
                    return OperationResult.Fail("پورت باید بین ۱ تا ۶۵۵۳۵ باشد.");
                }
            }

            const string sql = @"
UPDATE [Device]
SET [MachineName] = @MachineName,
    [GateID] = @GateId,
    [ConnectionType] = @ConnectionType,
    [IP] = @Ip,
    [Port] = @Port,
    [SerialPort] = @SerialPort,
    [BaudRate] = @BaudRate
WHERE [MachineNumber] = @MachineNumber;";

            try
            {
                using (IDbConnection connection = Open())
                {
                    int affected = connection.Execute(sql, new
                    {
                        row.MachineName,
                        row.GateId,
                        row.ConnectionType,
                        Ip = row.Ip ?? string.Empty,
                        row.Port,
                        row.SerialPort,
                        row.BaudRate,
                        row.MachineNumber
                    });
                    return affected == 1
                        ? OperationResult.Ok()
                        : OperationResult.Fail("دستگاه یافت نشد.");
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("ثبت تغییرات دستگاه ناموفق بود: " + ex.Message);
            }
        }

        private static bool IsValidIp(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            string[] parts = value.Split('.');
            if (parts.Length != 4)
            {
                return false;
            }
            foreach (string part in parts)
            {
                int octet;
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out octet) ||
                    octet < 0 || octet > 255)
                {
                    return false;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ access

        public OperationResult SetPermission(long badgeNumber, AdminFeature feature, AccessLevel level)
        {
            const string sql = @"
MERGE [T5WebAccess] AS target
USING (SELECT @BadgeNumber AS BadgeNumber, @Feature AS Feature) AS source
ON target.[BadgeNumber] = source.BadgeNumber AND target.[Feature] = source.Feature
WHEN MATCHED THEN UPDATE SET [Level] = @Level, [ChangedBy] = @ChangedBy, [ChangedUtc] = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT ([BadgeNumber], [Feature], [Level], [ChangedBy], [ChangedUtc])
     VALUES (@BadgeNumber, @Feature, @Level, @ChangedBy, SYSUTCDATETIME());";

            try
            {
                using (IDbConnection connection = Open())
                {
                    connection.Execute(sql, new
                    {
                        BadgeNumber = badgeNumber,
                        Feature = (int)feature,
                        Level = (int)level,
                        ChangedBy = CurrentActor
                    });
                    return OperationResult.Ok();
                }
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("ثبت سطح دسترسی ناموفق بود: " + ex.Message);
            }
        }

        /// <summary>Set by the web layer before a write so the audit trail has an actor.</summary>
        public string CurrentActor { get; set; } = "system";

        // ---------------------------------------------------------------- workflow

        public IReadOnlyList<WorkflowStep> GetWorkflow(int workflowType, int leaveTypeId)
        {
            const string sql = @"
SELECT w.[TitleID] AS TitleId,
       ISNULL(t.[TitleName], N'') AS TitleName,
       w.[AccepterTitleID] AS AccepterTitleId,
       ISNULL(a.[TitleName], N'') AS AccepterTitleName,
       w.[WorkflowType] AS WorkflowType,
       w.[LeaveTypeID] AS LeaveTypeId,
       w.[OrderIndex] AS OrderIndex
FROM [Workflow] w
LEFT JOIN [TitlesChart] t ON t.[TitleID] = w.[TitleID]
LEFT JOIN [TitlesChart] a ON a.[TitleID] = w.[AccepterTitleID]
WHERE w.[WorkflowType] = @WorkflowType AND w.[LeaveTypeID] = @LeaveTypeId
ORDER BY w.[OrderIndex];";

            using (IDbConnection connection = Open())
            {
                return connection.Query<WorkflowStep>(sql, new
                {
                    WorkflowType = workflowType,
                    LeaveTypeId = leaveTypeId
                }).ToList();
            }
        }

        public int CountInFlightRequests(int workflowType, int leaveTypeId)
        {
            // A request is "in flight" when it is not suspended and its accept rows do not
            // yet cover every step of the chain currently stored in [Workflow].
            const string sql = @"
SELECT COUNT(1)
FROM [Request] r
LEFT JOIN [Workflow] w
       ON w.[WorkflowType] = r.[WofkflowType]
      AND w.[LeaveTypeID] = r.[LeaveID]
WHERE ISNULL(r.[IsSuspend], 0) = 0
  AND (@WorkflowType = 0 OR r.[WofkflowType] = @WorkflowType)
  AND (@LeaveTypeId = 0 OR r.[LeaveID] = @LeaveTypeId)
  AND (SELECT COUNT(1) FROM [RequestAccept] a
        WHERE a.[RequestID] = r.[RequestID] AND ISNULL(a.[IsCancel], 0) = 0)
      < (SELECT COUNT(1) FROM [Workflow] w2
          WHERE w2.[WorkflowType] = r.[WofkflowType] AND w2.[LeaveTypeID] = r.[LeaveID]);";

            using (IDbConnection connection = Open())
            {
                return connection.ExecuteScalar<int>(sql, new { WorkflowType = workflowType, LeaveTypeId = leaveTypeId });
            }
        }

        public OperationResult SaveWorkflow(IReadOnlyList<WorkflowStep> steps, int workflowType, int leaveTypeId)
        {
            if (steps == null || steps.Count == 0)
            {
                return OperationResult.Fail("زنجیره تأیید خالی است.");
            }

            // One transaction: a half-replaced chain would strand in-flight requests.
            using (IDbConnection connection = Open())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    connection.Execute(
                        @"DELETE FROM [Workflow] WHERE [WorkflowType] = @WorkflowType AND [LeaveTypeID] = @LeaveTypeId;",
                        new { WorkflowType = workflowType, LeaveTypeId = leaveTypeId },
                        transaction);

                    const string insert = @"
INSERT INTO [Workflow] ([TitleID], [AccepterTitleID], [WorkflowType], [LeaveTypeID], [OrderIndex])
VALUES (@TitleId, @AccepterTitleId, @WorkflowType, @LeaveTypeId, @OrderIndex);";

                    foreach (WorkflowStep step in steps.OrderBy(s => s.OrderIndex))
                    {
                        connection.Execute(insert, new
                        {
                            step.TitleId,
                            step.AccepterTitleId,
                            WorkflowType = workflowType,
                            LeaveTypeId = leaveTypeId,
                            step.OrderIndex
                        }, transaction);
                    }

                    transaction.Commit();
                    return OperationResult.Ok();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return OperationResult.Fail("ثبت زنجیره تأیید ناموفق بود: " + ex.Message);
                }
            }
        }

        // ------------------------------------------------------------------- audit

        public void AppendAudit(AuditEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            const string sql = @"
INSERT INTO [T5WebAudit]
    ([UtcTime], [Actor], [Feature], [Action], [Target], [Detail], [PreviousHash], [Hash])
VALUES
    (@UtcTime, @Actor, @Feature, @Action, @Target, @Detail, @PreviousHash, @Hash);";

            using (IDbConnection connection = Open())
            {
                connection.Execute(sql, new
                {
                    entry.UtcTime,
                    entry.Actor,
                    Feature = (int)entry.Feature,
                    entry.Action,
                    entry.Target,
                    entry.Detail,
                    entry.PreviousHash,
                    entry.Hash
                });
            }
        }

        public IReadOnlyList<AuditEntry> GetAudit(int take, AdminFeature? feature)
        {
            const string sql = @"
SELECT TOP (@Take)
    a.[ID] AS Id, a.[UtcTime] AS UtcTime, a.[Actor] AS Actor, a.[Feature] AS Feature,
    a.[Action] AS Action, a.[Target] AS Target, a.[Detail] AS Detail,
    a.[PreviousHash] AS PreviousHash, a.[Hash] AS Hash
FROM [T5WebAudit] a
WHERE (@Feature IS NULL OR a.[Feature] = @Feature)
ORDER BY a.[ID] DESC;";

            using (IDbConnection connection = Open())
            {
                return connection.Query<AuditEntry>(sql, new
                {
                    Take = take,
                    Feature = feature.HasValue ? (int?)feature.Value : null
                }).ToList();
            }
        }

        public string GetLastAuditHash()
        {
            const string sql = @"SELECT TOP 1 [Hash] FROM [T5WebAudit] ORDER BY [ID] DESC;";
            using (IDbConnection connection = Open())
            {
                return connection.ExecuteScalar<string>(sql) ?? string.Empty;
            }
        }

        // ------------------------------------------------------------- diagnostics

        public IReadOnlyDictionary<string, int> GetTableCounts()
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var queries = new[]
            {
                new { Name = "UserInfo", Sql = @"SELECT COUNT(1) FROM [UserInfo];" },
                new { Name = "Department", Sql = @"SELECT COUNT(1) FROM [Department];" },
                new { Name = "Device", Sql = @"SELECT COUNT(1) FROM [Device];" },
                new { Name = "Workflow", Sql = @"SELECT COUNT(1) FROM [Workflow];" },
                new { Name = "T5WebAccess", Sql = @"SELECT COUNT(1) FROM [T5WebAccess];" },
                new { Name = "T5WebAudit", Sql = @"SELECT COUNT(1) FROM [T5WebAudit];" }
            };

            using (IDbConnection connection = Open())
            {
                foreach (var query in queries)
                {
                    try
                    {
                        counts[query.Name] = connection.ExecuteScalar<int>(query.Sql);
                    }
                    catch (Exception)
                    {
                        // A missing table is reported as -1 instead of breaking the page.
                        counts[query.Name] = -1;
                    }
                }
            }
            return counts;
        }

        public bool CanConnect(out string error)
        {
            try
            {
                using (IDbConnection connection = Open())
                {
                    connection.ExecuteScalar<int>(@"SELECT 1;");
                }
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ----------------------------------------------------------------- mapping

        private sealed class EmpTypeRecord
        {
            public int EmpTypeId { get; set; }
            public string EmpTypeName { get; set; }
        }

        /// <summary>Raw permission row: enum conversion happens here, not in Dapper.</summary>
        private sealed class PermissionRecord
        {
            public long BadgeNumber { get; set; }
            public int Feature { get; set; }
            public int Level { get; set; }
            public string PersonName { get; set; }
        }

        /// <summary>Raw device row: [LastStamp] is unix seconds, not a datetime.</summary>
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
            public string Code { get; set; }
            public bool Push { get; set; }
            public long? LastStamp { get; set; }
        }

        /// <summary>Raw row shape, so Dapper never guesses which column maps where.</summary>
        private sealed class PersonnelRecord
        {
            public long BadgeNumber { get; set; }
            public string EmployeeNumber { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string FatherName { get; set; }
            public string NationalNo { get; set; }
            public string Phone { get; set; }
            public string Email { get; set; }
            public int DeptId { get; set; }
            public string DeptName { get; set; }
            public int EmpTypeId { get; set; }
            public string EmpTypeName { get; set; }
            public int CertificateId { get; set; }
            public DateTime? HiredDate { get; set; }
            public DateTime? FiredDate { get; set; }
            public bool Gender { get; set; }
            public bool ProgramUser { get; set; }
            public bool WebUser { get; set; }
            public bool DeviceEnable { get; set; }
            public string UserName { get; set; }
        }

        private static PersonnelRow MapPersonnel(PersonnelRecord record)
        {
            if (record == null)
            {
                return null;
            }
            return new PersonnelRow
            {
                BadgeNumber = record.BadgeNumber,
                EmployeeNumber = record.EmployeeNumber ?? string.Empty,
                FirstName = record.FirstName ?? string.Empty,
                LastName = record.LastName ?? string.Empty,
                FatherName = record.FatherName ?? string.Empty,
                NationalNo = record.NationalNo ?? string.Empty,
                Phone = record.Phone ?? string.Empty,
                Email = record.Email ?? string.Empty,
                DeptId = record.DeptId,
                DeptName = record.DeptName ?? string.Empty,
                EmpTypeId = record.EmpTypeId,
                EmpTypeName = record.EmpTypeName ?? string.Empty,
                CertificateId = record.CertificateId,
                HiredDate = record.HiredDate,
                FiredDate = record.FiredDate,
                Gender = record.Gender,
                IsProgramUser = record.ProgramUser,
                IsWebUser = record.WebUser,
                DeviceEnabled = record.DeviceEnable,
                UserName = record.UserName ?? string.Empty
            };
        }
    }
}
