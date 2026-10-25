using System;
using System.Collections.Generic;
using System.Linq;

namespace T5Web.Domain.Models
{
    /// <summary>One "this employment type has this leave type" link (<c>[EmpTypeLeave]</c>).</summary>
    public sealed class EntitlementRow
    {
        public int EmpTypeId { get; set; }
        public int LeaveTypeId { get; set; }

        public string Key
        {
            get { return EntitlementMatrix.KeyOf(EmpTypeId, LeaveTypeId); }
        }
    }

    /// <summary>
    /// The entitlement matrix: employment types (rows) against leave types (columns).
    ///
    /// The T5V3 desktop edits it in frmEmpLeaveType; the legacy web portal never exposed
    /// it at all, which is why an administrator had to keep a desktop session open just to
    /// give a new employment type its leave quota. The table itself is a pure link table
    /// (<c>[EmpTypeLeave]([EmpTypeID], [LeaveTypeID])</c>, both <c>int</c>), so the portal
    /// can own this screen without touching engine arithmetic.
    /// </summary>
    public sealed class EntitlementMatrix
    {
        public IReadOnlyList<KeyValuePair<int, string>> EmpTypes { get; set; } =
            new List<KeyValuePair<int, string>>();

        public IReadOnlyList<KeyValuePair<int, string>> LeaveTypes { get; set; } =
            new List<KeyValuePair<int, string>>();

        /// <summary>Granted pairs, in <c>emp:leave</c> form.</summary>
        public HashSet<string> Granted { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool IsGranted(int empTypeId, int leaveTypeId)
        {
            return Granted != null && Granted.Contains(KeyOf(empTypeId, leaveTypeId));
        }

        public int CountFor(int empTypeId)
        {
            if (Granted == null)
            {
                return 0;
            }
            string prefix = empTypeId.ToString() + ":";
            return Granted.Count(key => key.StartsWith(prefix, StringComparison.Ordinal));
        }

        public int GrantedCount
        {
            get { return Granted == null ? 0 : Granted.Count; }
        }

        public int PossibleCount
        {
            get { return EmpTypes.Count * LeaveTypes.Count; }
        }

        public static string KeyOf(int empTypeId, int leaveTypeId)
        {
            return empTypeId.ToString() + ":" + leaveTypeId.ToString();
        }
    }
}

namespace T5Web.Domain.Interfaces
{
    /// <summary>
    /// Leave entitlements per employment type (T5V3 desktop: frmEmpLeaveType).
    ///
    /// Reads employment types from the Personnel catalog and leave types from the engine
    /// catalog, one statement each (docs/09 - the portal never joins across the two).
    /// </summary>
    public interface IEntitlementStore
    {
        IReadOnlyList<KeyValuePair<int, string>> GetEmpTypes();

        IReadOnlyList<KeyValuePair<int, string>> GetLeaveTypes();

        IReadOnlyList<EntitlementRow> GetPairs();

        /// <summary>Grants or revokes one pair. Idempotent in both directions.</summary>
        OperationResult Set(int empTypeId, int leaveTypeId, bool grant);
    }
}
