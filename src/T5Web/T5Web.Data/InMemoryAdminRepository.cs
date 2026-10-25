using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// Deterministic in-memory implementation used by the test suite and by the
    /// offline demo mode.
    ///
    /// It is deliberately *not* a stub: it enforces the same uniqueness rules as SQL
    /// Server (badge numbers, machine numbers) so a service test exercises the same
    /// failure paths the production database would raise.
    /// </summary>
    public sealed class InMemoryAdminRepository : IAdminRepository
    {
        private readonly List<PersonnelRow> _people = new List<PersonnelRow>();
        private readonly List<DepartmentNode> _departments = new List<DepartmentNode>();
        private readonly List<TitleNode> _titles = new List<TitleNode>();
        private readonly List<GateRow> _gates = new List<GateRow>();
        private readonly List<DeviceRow> _devices = new List<DeviceRow>();
        private readonly List<DeviceCommandTicket> _deviceCommands = new List<DeviceCommandTicket>();
        private readonly List<PermissionRow> _permissions = new List<PermissionRow>();
        private readonly List<WorkflowStep> _workflow = new List<WorkflowStep>();
        private readonly List<AuditEntry> _audit = new List<AuditEntry>();
        private readonly Dictionary<long, string> _passwords = new Dictionary<long, string>();
        private long _auditSequence;

        public string LastError { get; private set; } = string.Empty;

        public InMemoryAdminRepository Seed(
            IEnumerable<PersonnelRow> people = null,
            IEnumerable<DepartmentNode> departments = null,
            IEnumerable<TitleNode> titles = null,
            IEnumerable<GateRow> gates = null,
            IEnumerable<DeviceRow> devices = null)
        {
            if (people != null)
            {
                _people.Clear();
                _people.AddRange(people);
            }
            if (departments != null)
            {
                _departments.Clear();
                _departments.AddRange(departments);
            }
            if (titles != null)
            {
                _titles.Clear();
                _titles.AddRange(titles);
            }
            if (gates != null)
            {
                _gates.Clear();
                _gates.AddRange(gates);
            }
            if (devices != null)
            {
                _devices.Clear();
                _devices.AddRange(devices);
            }
            return this;
        }

        public InMemoryAdminRepository SeedPassword(long badgeNumber, string password)
        {
            _passwords[badgeNumber] = password ?? string.Empty;
            return this;
        }

        public InMemoryAdminRepository SeedPermission(long badgeNumber, AdminFeature feature, AccessLevel level)
        {
            _permissions.Add(new PermissionRow
            {
                BadgeNumber = badgeNumber,
                PersonName = _people.FirstOrDefault(p => p.BadgeNumber == badgeNumber)?.FullName ?? string.Empty,
                Feature = feature,
                Level = level
            });
            return this;
        }

        // ------------------------------------------------------------------ session

        public PersonnelRow FindUserByUserName(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                return null;
            }
            return _people.FirstOrDefault(
                p => string.Equals(p.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<PermissionRow> GetPermissions(long badgeNumber)
        {
            if (badgeNumber <= 0)
            {
                return _permissions.ToList();
            }
            return _permissions.Where(p => p.BadgeNumber == badgeNumber).ToList();
        }

        public string GetTitleName(long badgeNumber)
        {
            return _titles.FirstOrDefault()?.TitleName ?? string.Empty;
        }

        public int GetTitleId(long badgeNumber)
        {
            TitleNode title = _titles.FirstOrDefault();
            return title == null ? 0 : title.TitleId;
        }

        // ------------------------------------------------------------ organisation

        public IReadOnlyList<DepartmentNode> GetDepartments()
        {
            return _departments.ToList();
        }

        public IReadOnlyList<TitleNode> GetTitles()
        {
            return _titles.ToList();
        }

        public IReadOnlyList<GateRow> GetGates()
        {
            return _gates.ToList();
        }

        public IReadOnlyList<KeyValuePair<int, string>> GetEmpTypes()
        {
            return new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(1, "رسمی"),
                new KeyValuePair<int, string>(2, "پیمانی"),
                new KeyValuePair<int, string>(3, "شرکتی")
            };
        }

        public OperationResult MoveDepartment(int departmentId, int newParentId)
        {
            DepartmentNode node = _departments.FirstOrDefault(d => d.DepartmentId == departmentId);
            if (node == null)
            {
                return OperationResult.Fail("واحد سازمانی یافت نشد.");
            }
            node.UpperDeptId = newParentId;
            return OperationResult.Ok();
        }

        // --------------------------------------------------------------- personnel

        public IReadOnlyList<PersonnelRow> SearchPersonnel(string term, int? departmentId, bool includeFormer, int take)
        {
            IEnumerable<PersonnelRow> query = _people;

            if (!string.IsNullOrWhiteSpace(term))
            {
                string needle = term.Trim();
                query = query.Where(p =>
                    p.FirstName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.LastName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.EmployeeNumber.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.NationalNo.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.BadgeNumber.ToString().Contains(needle));
            }
            if (departmentId.HasValue && departmentId.Value > 0)
            {
                query = query.Where(p => p.DeptId == departmentId.Value);
            }
            if (!includeFormer)
            {
                query = query.Where(p => !p.IsFormer);
            }

            return query.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).Take(take).ToList();
        }

        public PersonnelRow GetPersonnel(long badgeNumber)
        {
            return _people.FirstOrDefault(p => p.BadgeNumber == badgeNumber);
        }

        public OperationResult UpdatePersonnel(PersonnelRow row)
        {
            if (row == null)
            {
                return OperationResult.Fail("رکورد پرسنل خالی است.");
            }
            int index = _people.FindIndex(p => p.BadgeNumber == row.BadgeNumber);
            if (index < 0)
            {
                return OperationResult.Fail("رکورد پرسنل یافت نشد.");
            }
            _people[index] = row;
            return OperationResult.Ok();
        }

        // ----------------------------------------------------------------- devices

        public IReadOnlyList<DeviceRow> GetDevices(int? gateId)
        {
            if (gateId.HasValue && gateId.Value > 0)
            {
                return _devices.Where(d => d.GateId == gateId.Value).OrderBy(d => d.MachineNumber).ToList();
            }
            return _devices.OrderBy(d => d.MachineNumber).ToList();
        }

        public OperationResult UpdateDevice(DeviceRow row)
        {
            if (row == null)
            {
                return OperationResult.Fail("دستگاه نامعتبر است.");
            }
            int index = _devices.FindIndex(d => d.MachineNumber == row.MachineNumber);
            if (index < 0)
            {
                return OperationResult.Fail("دستگاه یافت نشد.");
            }
            _devices[index] = row;
            return OperationResult.Ok();
        }

        // ------------------------------------------------------- device commands

        public OperationResult EnqueueDeviceCommand(DeviceCommandTicket ticket)
        {
            if (ticket == null)
            {
                return OperationResult.Fail("دستگاه انتخاب نشده است.");
            }
            if (ticket.MachineNumber <= 0 || _devices.All(d => d.MachineNumber != ticket.MachineNumber))
            {
                return OperationResult.Fail("دستگاه یافت نشد.");
            }
            if (string.IsNullOrWhiteSpace(ticket.Command))
            {
                return OperationResult.Fail("دستور انتخاب نشده است.");
            }

            ticket.Id = _deviceCommands.Count + 1;
            ticket.RequestedUtc = ticket.RequestedUtc == default(DateTime) ? DateTime.UtcNow : ticket.RequestedUtc;
            ticket.Status = 0;
            _deviceCommands.Add(ticket);
            return OperationResult.Ok();
        }

        public IReadOnlyList<DeviceCommandTicket> GetDeviceCommands(int take)
        {
            return _deviceCommands.OrderByDescending(c => c.Id).Take(take).ToList();
        }

        // ------------------------------------------------------------------ access

        public OperationResult SetPermission(long badgeNumber, AdminFeature feature, AccessLevel level)
        {
            PermissionRow existing = _permissions.FirstOrDefault(
                p => p.BadgeNumber == badgeNumber && p.Feature == feature);

            if (existing == null)
            {
                _permissions.Add(new PermissionRow
                {
                    BadgeNumber = badgeNumber,
                    PersonName = _people.FirstOrDefault(p => p.BadgeNumber == badgeNumber)?.FullName ?? string.Empty,
                    Feature = feature,
                    Level = level
                });
            }
            else
            {
                existing.Level = level;
            }
            return OperationResult.Ok();
        }

        // ---------------------------------------------------------------- workflow

        public IReadOnlyList<WorkflowStep> GetWorkflow(int workflowType, int leaveTypeId)
        {
            return _workflow
                .Where(s => s.WorkflowType == workflowType && s.LeaveTypeId == leaveTypeId)
                .OrderBy(s => s.OrderIndex)
                .ToList();
        }

        public OperationResult SaveWorkflow(IReadOnlyList<WorkflowStep> steps, int workflowType, int leaveTypeId)
        {
            _workflow.RemoveAll(s => s.WorkflowType == workflowType && s.LeaveTypeId == leaveTypeId);
            foreach (WorkflowStep step in steps)
            {
                step.WorkflowType = workflowType;
                step.LeaveTypeId = leaveTypeId;
                _workflow.Add(step);
            }
            return OperationResult.Ok();
        }

        public int CountInFlightRequests(int workflowType, int leaveTypeId)
        {
            return InFlightRequests;
        }

        /// <summary>Test hook: how many stranded requests the in-memory repo pretends to hold.</summary>
        public int InFlightRequests { get; set; }

        // ------------------------------------------------------------------- audit

        public void AppendAudit(AuditEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }
            entry.Id = ++_auditSequence;
            _audit.Add(entry);
        }

        public IReadOnlyList<AuditEntry> GetAudit(int take, AdminFeature? feature)
        {
            IEnumerable<AuditEntry> query = _audit;
            if (feature.HasValue)
            {
                query = query.Where(a => a.Feature == feature.Value);
            }
            return query.OrderByDescending(a => a.Id).Take(take).ToList();
        }

        public string GetLastAuditHash()
        {
            AuditEntry last = _audit.OrderByDescending(a => a.Id).FirstOrDefault();
            return last == null ? string.Empty : last.Hash;
        }

        // ------------------------------------------------------------- diagnostics

        public IReadOnlyDictionary<string, int> GetTableCounts()
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "UserInfo", _people.Count },
                { "Department", _departments.Count },
                { "Device", _devices.Count },
                { "Workflow", _workflow.Count },
                { "T5WebDeviceCommand", _deviceCommands.Count },
                { "T5WebAccess", _permissions.Count },
                { "T5WebAudit", _audit.Count }
            };
        }

        public bool CanConnect(out string error)
        {
            error = LastError;
            return string.IsNullOrEmpty(LastError);
        }
    }
}
