using System;
using System.Collections.Generic;
using T5Web.Domain.Models;

namespace T5Web.Domain.Models
{
    /// <summary>One leave balance row: a person, a leave type and a year.</summary>
    public sealed class LeaveBalanceRow
    {
        public long BadgeNumber { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public int LeaveTypeId { get; set; }
        public string LeaveTypeName { get; set; } = string.Empty;
        public int Year { get; set; }

        /// <summary>Opening balance carried by the engine ([YearRemain].[DailyValue]).</summary>
        public double OpeningDays { get; set; }

        /// <summary>Days granted during the year ([LeavesAdded] sum).</summary>
        public double AddedDays { get; set; }

        /// <summary>Days consumed ([DailyLeaf] sum in days).</summary>
        public double UsedDays { get; set; }

        /// <summary>Opening + added - used. The screen must never show a different number.</summary>
        public double ClosingDays
        {
            get { return Math.Round(OpeningDays + AddedDays - UsedDays, 2); }
        }

        /// <summary>True when the stored balance disagrees with the arithmetic (BUG risk).</summary>
        public bool IsInconsistent { get; set; }

        public string YearFa
        {
            get { return JalaliText.Format(new DateTime(Year, 1, 1)); }
        }
    }

    /// <summary>A single leave grant ([LeavesAdded]).</summary>
    public sealed class LeaveGrantRow
    {
        public long Id { get; set; }
        public long BadgeNumber { get; set; }
        public int LeaveTypeId { get; set; }
        public string LeaveTypeName { get; set; } = string.Empty;
        public DateTime AssignDate { get; set; }
        public double Days { get; set; }
        public string Descriptions { get; set; } = string.Empty;
        public string DocumentNumber { get; set; } = string.Empty;
        public bool IsResell { get; set; }
    }

    /// <summary>Preview line of a year-end transfer: who gets how much.</summary>
    public sealed class LeaveTransferRow
    {
        public long BadgeNumber { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public int LeaveTypeId { get; set; }
        public string LeaveTypeName { get; set; } = string.Empty;
        public double FromDays { get; set; }
        public double TransferDays { get; set; }
        public double CarryCap { get; set; }
        public bool Capped { get; set; }
        public string Note { get; set; } = string.Empty;
    }
}

namespace T5Web.Domain.Interfaces
{
    /// <summary>
    /// Leave balances, grants and the yearly transfer.
    ///
    /// The engine keeps the authoritative numbers; this store only reads them and
    /// writes the operator's explicit grants. Nothing here recomputes attendance.
    /// </summary>
    public interface ILeaveStore
    {
        IReadOnlyList<LeaveBalanceRow> GetBalances(int year, int? leaveTypeId, string term, int take);

        IReadOnlyList<LeaveGrantRow> GetGrants(long? badgeNumber, int? leaveTypeId, int take);

        OperationResult AddGrant(LeaveGrantRow grant);

        /// <summary>What a transfer from the source year would move, capped per leave type.</summary>
        IReadOnlyList<LeaveTransferRow> PreviewTransfer(int fromYear, int toYear, double carryCap,
                                                        long? badgeNumber);

        /// <summary>Moves the previewed amounts; all-or-nothing.</summary>
        OperationResult CommitTransfer(IReadOnlyList<LeaveTransferRow> rows, int fromYear, int toYear,
                                       string reason, out string detail);

        IReadOnlyList<KeyValuePair<int, string>> GetLeaveTypes();
    }
}
