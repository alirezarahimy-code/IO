namespace T5Web.Data
{
    /// <summary>
    /// Every table and column this portal touches, in one place.
    ///
    /// The legacy T5V3Web had SQL strings scattered over 64 controllers, and the
    /// misspelling of a column (the engine really does store `WofkflowType`) only
    /// surfaced when a screen was opened in production. Centralising the names means
    /// a schema change is a one-file change, and tools/verify-sql-schema.py can check
    /// every identifier here against db/schema-manifest.json.
    ///
    /// Read-only on the engine tables; only *.Access / *.Audit are written by an
    /// administrative action on the portal's own tables.
    /// </summary>
    public static class SchemaMap
    {
        public static class UserInfo
        {
            public const string Table = "[UserInfo]";
            public const string BadgeNumber = "[BadgeNumber]";
            public const string DeptId = "[DeptID]";
            public const string EmpTypeId = "[EmpTypeID]";
            public const string CertificateId = "[CertificateID]";
            public const string EmployeeNumber = "[EmployeeNumber]";
            public const string FirstName = "[FirstName]";
            public const string LastName = "[LastName]";
            public const string FatherName = "[FatherName]";
            public const string NationalNo = "[NationalNo]";
            public const string Phone = "[Phone]";
            public const string Email = "[Email]";
            public const string Address = "[Address]";
            public const string Birthday = "[Birthday]";
            public const string HiredDate = "[HiredDate]";
            public const string FiredDate = "[FiredDate]";
            public const string Gender = "[Gender]";
            public const string UserName = "[UserName]";
            public const string Password = "[Password]";
            public const string ProgramUser = "[ProgramUser]";
            public const string WebUser = "[WebUser]";
            public const string DeviceEnable = "[DeviceEnable]";
            public const string Descriptions = "[Descriptions]";
        }

        public static class UserInfoTitles
        {
            public const string Table = "[UserInfoTitles]";
            public const string Id = "[ID]";
            public const string BadgeNumber = "[BadgeNumber]";
            public const string TitleChartId = "[TitleChartID]";
            public const string FromDate = "[FromDate]";
            public const string ToDate = "[ToDate]";
        }

        public static class TitlesChart
        {
            public const string Table = "[TitlesChart]";
            public const string TitleId = "[TitleID]";
            public const string TitleName = "[TitleName]";
            public const string DisplayName = "[DisplayName]";
            public const string UpperTitleId = "[UpperTitleID]";
            public const string Observer = "[Observer]";
        }

        public static class Department
        {
            public const string Table = "[Department]";
            public const string DepartmentId = "[DepartmentID]";
            public const string DepartmentName = "[DepartmentName]";
            public const string UpperDeptId = "[UpperDeptID]";
        }

        public static class Device
        {
            public const string Table = "[Device]";
            public const string MachineNumber = "[MachineNumber]";
            public const string MachineName = "[MachineName]";
            public const string GateId = "[GateID]";
            public const string ConnectionType = "[ConnectionType]";
            public const string Ip = "[IP]";
            public const string Port = "[Port]";
            public const string SerialPort = "[SerialPort]";
            public const string BaudRate = "[BaudRate]";
            public const string SerialNo = "[SerialNo]";
            public const string Code = "[Code]";
            public const string Push = "[Push]";
            public const string LastStamp = "[LastStamp]";
        }

        public static class Gate
        {
            public const string Table = "[Gate]";
            public const string GateId = "[GateID]";
            public const string GateName = "[GateName]";
            public const string UpperGateId = "[UpperGateID]";
        }

        public static class EmpType
        {
            public const string Table = "[EmpType]";
            public const string EmpTypeId = "[EmpTypeID]";
            public const string EmpTypeName = "[EmpTypeName]";
        }

        public static class Workflow
        {
            public const string Table = "[Workflow]";
            public const string TitleId = "[TitleID]";
            public const string AccepterTitleId = "[AccepterTitleID]";
            public const string WorkflowType = "[WorkflowType]";
            public const string LeaveTypeId = "[LeaveTypeID]";
            public const string OrderIndex = "[OrderIndex]";
        }

        /// <summary>Portal-owned permission table (src/T5Web/db/01-t5web-objects.sql).</summary>
        public static class PortalAccess
        {
            public const string Table = "[T5WebAccess]";
            public const string BadgeNumber = "[BadgeNumber]";
            public const string Feature = "[Feature]";
            public const string Level = "[Level]";
            public const string ChangedBy = "[ChangedBy]";
            public const string ChangedUtc = "[ChangedUtc]";
        }

        /// <summary>Portal-owned queue of device commands (db/01-t5web-objects.sql).</summary>
        public static class PortalDeviceCommand
        {
            public const string Table = "[T5WebDeviceCommand]";
            public const string Id = "[ID]";
            public const string MachineNumber = "[MachineNumber]";
            public const string Command = "[Command]";
            public const string RequestedBy = "[RequestedBy]";
            public const string RequestedUtc = "[RequestedUtc]";
            public const string Status = "[Status]";
            public const string Note = "[Note]";
        }

        /// <summary>Portal-owned, hash-chained audit table.</summary>
        public static class PortalAudit
        {
            public const string Table = "[T5WebAudit]";
            public const string Id = "[ID]";
            public const string UtcTime = "[UtcTime]";
            public const string Actor = "[Actor]";
            public const string Feature = "[Feature]";
            public const string Action = "[Action]";
            public const string Target = "[Target]";
            public const string Detail = "[Detail]";
            public const string PreviousHash = "[PreviousHash]";
            public const string Hash = "[Hash]";
        }
    }
}
