using System;
using System.Collections.Generic;

namespace T5Web.Domain.Models
{
    /// <summary>
    /// One person as shown in the personnel grid. Field names follow the real
    /// columns of [UserInfo] (see db/schema-manifest.json) - never the legacy
    /// view-model names, so a rename in the database breaks the build of this
    /// mapping rather than a shift.
    /// </summary>
    public sealed class PersonnelRow
    {
        public long BadgeNumber { get; set; }
        public string EmployeeNumber { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FatherName { get; set; } = string.Empty;
        public string NationalNo { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int DeptId { get; set; }
        public string DeptName { get; set; } = string.Empty;
        public int EmpTypeId { get; set; }
        public string EmpTypeName { get; set; } = string.Empty;
        public int CertificateId { get; set; }
        public DateTime? HiredDate { get; set; }
        public DateTime? FiredDate { get; set; }
        public bool Gender { get; set; }
        public bool IsProgramUser { get; set; }
        public bool IsWebUser { get; set; }
        public bool DeviceEnabled { get; set; }
        public string UserName { get; set; } = string.Empty;

        public string FullName
        {
            get
            {
                string full = (FirstName + " " + LastName).Trim();
                return string.IsNullOrEmpty(full) ? ("#" + BadgeNumber) : full;
            }
        }

        /// <summary>True when the person left the organisation (has a fire date).</summary>
        public bool IsFormer
        {
            get { return FiredDate.HasValue; }
        }
    }

    /// <summary>A node of the department tree ([Department]).</summary>
    public sealed class DepartmentNode
    {
        public int DepartmentId { get; set; }
        public int UpperDeptId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public List<DepartmentNode> Children { get; } = new List<DepartmentNode>();

        public int Depth { get; set; }

        public IEnumerable<DepartmentNode> Flatten()
        {
            yield return this;
            foreach (DepartmentNode child in Children)
            {
                foreach (DepartmentNode node in child.Flatten())
                {
                    yield return node;
                }
            }
        }
    }

    /// <summary>A node of the job-title chart ([TitlesChart]).</summary>
    public sealed class TitleNode
    {
        public int TitleId { get; set; }
        public int UpperTitleId { get; set; }
        public string TitleName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsObserver { get; set; }
        public List<TitleNode> Children { get; } = new List<TitleNode>();
    }

    /// <summary>A gate/zone ([Gate]) and the devices installed on it.</summary>
    public sealed class GateRow
    {
        public int GateId { get; set; }
        public int UpperGateId { get; set; }
        public string GateName { get; set; } = string.Empty;
        public int DeviceCount { get; set; }
    }

    /// <summary>
    /// A device as shown in the device screen. Only the fields an operator actually
    /// configures from the web portal are exposed; the protocol counters stay in the
    /// engine's own screens (they are written by the push service, not by a human).
    /// </summary>
    /// <summary>
    /// One device command the portal has queued. The portal never talks to hardware
    /// itself: it writes the intent here (plus an audit row) and the desktop agent that
    /// owns the device sockets drains it. This replaces the legacy
    /// <c>appSettings/Link5ServiceURL</c> HTTP call, which the web tier could not test.
    /// </summary>
    public sealed class DeviceCommandTicket
    {
        public long Id { get; set; }
        public int MachineNumber { get; set; }
        public string Command { get; set; } = string.Empty;
        public string RequestedBy { get; set; } = string.Empty;
        public DateTime RequestedUtc { get; set; }
        public int Status { get; set; }
        public string Note { get; set; } = string.Empty;

        public string StatusFa
        {
            get
            {
                switch (Status)
                {
                    case 0: return "در صف";
                    case 1: return "برداشته شد";
                    case 2: return "انجام شد";
                    case 3: return "ناموفق";
                    default: return "نامشخص";
                }
            }
        }
    }

    public sealed class DeviceRow
    {
        public int MachineNumber { get; set; }
        public string MachineName { get; set; } = string.Empty;
        public int GateId { get; set; }
        public string GateName { get; set; } = string.Empty;
        public int ConnectionType { get; set; }
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; }
        public int SerialPort { get; set; }
        public int BaudRate { get; set; }
        public string SerialNo { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public bool Push { get; set; }
        public DateTime? LastStamp { get; set; }

        public string ConnectionTypeFa
        {
            get
            {
                switch (ConnectionType)
                {
                    case 1: return "USB";
                    case 2: return "پورت سریال";
                    default: return "شبکه";
                }
            }
        }

        public string Endpoint
        {
            get
            {
                if (ConnectionType == 0)
                {
                    return Ip + ":" + Port;
                }
                if (ConnectionType == 1)
                {
                    return "USB";
                }
                return "COM" + SerialPort;
            }
        }

        /// <summary>Nothing received for more than a day means the device is probably offline.</summary>
        public bool IsStale(DateTime nowUtc)
        {
            if (!LastStamp.HasValue)
            {
                return true;
            }
            return (nowUtc - LastStamp.Value).TotalHours > 24;
        }
    }

    /// <summary>One effective permission row for a person.</summary>
    public sealed class PermissionRow
    {
        public long BadgeNumber { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public AdminFeature Feature { get; set; }
        public AccessLevel Level { get; set; }
    }

    /// <summary>One step of the approval chain ([Workflow]).</summary>
    public sealed class WorkflowStep
    {
        public int TitleId { get; set; }
        public string TitleName { get; set; } = string.Empty;
        public int AccepterTitleId { get; set; }
        public string AccepterTitleName { get; set; } = string.Empty;
        public int WorkflowType { get; set; }
        public int LeaveTypeId { get; set; }
        public int OrderIndex { get; set; }
    }

    /// <summary>
    /// Append-only audit entry. The portal writes its own log instead of relying on
    /// [SystemLog], because that table is owned by the engine and must not gain
    /// portal-specific columns.
    /// </summary>
    public sealed class AuditEntry
    {
        public long Id { get; set; }
        public DateTime UtcTime { get; set; }
        public string Actor { get; set; } = string.Empty;
        public AdminFeature Feature { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string PreviousHash { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;

        public string FeatureFa
        {
            get { return AdminFeatures.TitleOf(Feature); }
        }
    }

    /// <summary>The signed-in operator: identity plus effective permissions.</summary>
    public sealed class AdminSession
    {
        public long BadgeNumber { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string TitleName { get; set; } = string.Empty;
        public DateTime IssuedUtc { get; set; }
        private readonly Dictionary<AdminFeature, AccessLevel> _levels =
            new Dictionary<AdminFeature, AccessLevel>();

        public IReadOnlyDictionary<AdminFeature, AccessLevel> Levels
        {
            get { return _levels; }
        }

        public void Grant(AdminFeature feature, AccessLevel level)
        {
            // Deny always wins: a stale "Allow" must never resurrect a revoked right.
            AccessLevel current;
            if (_levels.TryGetValue(feature, out current) && current == AccessLevel.Deny)
            {
                return;
            }
            _levels[feature] = level;
        }

        public AccessLevel LevelOf(AdminFeature feature)
        {
            AccessLevel level;
            return _levels.TryGetValue(feature, out level) ? level : AccessLevel.Deny;
        }

        public bool CanView(AdminFeature feature)
        {
            return LevelOf(feature) != AccessLevel.Deny;
        }

        public bool CanEdit(AdminFeature feature)
        {
            return LevelOf(feature) == AccessLevel.Allow;
        }

        public bool IsPortalAdmin
        {
            get { return LevelOf(AdminFeature.PortalAdmin) == AccessLevel.Allow; }
        }
    }

    /// <summary>Result of a service call, so controllers never throw at the user.</summary>
    public sealed class OperationResult
    {
        private static readonly OperationResult OkInstance = new OperationResult(true, string.Empty);

        private OperationResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message;
        }

        public bool Succeeded { get; private set; }
        public string Message { get; private set; }

        public static OperationResult Ok()
        {
            return OkInstance;
        }

        public static OperationResult Fail(string message)
        {
            return new OperationResult(false, message);
        }
    }
}
