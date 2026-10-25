using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// In-memory entitlement matrix for the tests and the offline demo.
    ///
    /// Both directions are idempotent here too, so T31 can prove the "double click cannot
    /// duplicate a pair" rule without a SQL server.
    /// </summary>
    public sealed class InMemoryEntitlementStore : IEntitlementStore
    {
        private readonly List<KeyValuePair<int, string>> _empTypes = new List<KeyValuePair<int, string>>();
        private readonly List<KeyValuePair<int, string>> _leaveTypes = new List<KeyValuePair<int, string>>();
        private readonly HashSet<string> _pairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public InMemoryEntitlementStore EmpType(int id, string name)
        {
            _empTypes.Add(new KeyValuePair<int, string>(id, name));
            return this;
        }

        public InMemoryEntitlementStore LeaveType(int id, string name)
        {
            _leaveTypes.Add(new KeyValuePair<int, string>(id, name));
            return this;
        }

        public InMemoryEntitlementStore Grant(int empTypeId, int leaveTypeId)
        {
            _pairs.Add(EntitlementMatrix.KeyOf(empTypeId, leaveTypeId));
            return this;
        }

        public IReadOnlyList<KeyValuePair<int, string>> GetEmpTypes()
        {
            return _empTypes.ToList();
        }

        public IReadOnlyList<KeyValuePair<int, string>> GetLeaveTypes()
        {
            return _leaveTypes.ToList();
        }

        public IReadOnlyList<EntitlementRow> GetPairs()
        {
            return _pairs
                .Select(key => key.Split(':'))
                .Select(parts => new EntitlementRow
                {
                    EmpTypeId = int.Parse(parts[0]),
                    LeaveTypeId = int.Parse(parts[1])
                })
                .ToList();
        }

        public OperationResult Set(int empTypeId, int leaveTypeId, bool grant)
        {
            if (empTypeId <= 0 || leaveTypeId <= 0)
            {
                return OperationResult.Fail("نوع استخدام یا نوع مرخصی نامعتبر است.");
            }

            string key = EntitlementMatrix.KeyOf(empTypeId, leaveTypeId);
            if (grant)
            {
                _pairs.Add(key);
            }
            else
            {
                _pairs.Remove(key);
            }
            return OperationResult.Ok();
        }

        /// <summary>Builds the matrix exactly like the controller does.</summary>
        public EntitlementMatrix BuildMatrix()
        {
            return new EntitlementMatrix
            {
                EmpTypes = GetEmpTypes(),
                LeaveTypes = GetLeaveTypes(),
                Granted = new HashSet<string>(GetPairs().Select(p => p.Key), StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
