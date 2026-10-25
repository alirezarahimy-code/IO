using System;
using System.Collections.Generic;

namespace T5Web.Domain.Models
{
    /// <summary>One schedule (a named day pattern: start, end, rest, rule).</summary>
    public sealed class ScheduleRow
    {
        public int ScheduleId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public int WorkMins { get; set; }
        public int RuleId { get; set; }
        public string Color { get; set; } = string.Empty;

        /// <summary>How many shifts contain this schedule ([ShiftDetail]).</summary>
        public int ShiftCount { get; set; }
    }

    /// <summary>One shift (a cycle of schedules) and its day coverage.</summary>
    public sealed class ShiftRow
    {
        public int ShiftId { get; set; }
        public string ShiftName { get; set; } = string.Empty;
        public int Period { get; set; }
        public int Unit { get; set; }
        public string WeekEnds { get; set; } = string.Empty;
        public int DayCount { get; set; }
        public IReadOnlyList<int> DayIndexes { get; set; } = new List<int>();

        public string PeriodFa
        {
            get
            {
                switch (Unit)
                {
                    case 1: return Period + " روز";
                    case 2: return Period + " هفته";
                    case 3: return Period + " ماه";
                    default: return Period.ToString();
                }
            }
        }
    }

    /// <summary>One shift assignment window for a person ([UserShift]).</summary>
    public sealed class UserShiftRow
    {
        public long BadgeNumber { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public int ShiftId { get; set; }
        public string ShiftName { get; set; } = string.Empty;
        public DateTime FromDate { get; set; }
        public DateTime? EndDate { get; set; }

        public bool IsOpen
        {
            get { return !EndDate.HasValue; }
        }
    }
}

namespace T5Web.Domain.Interfaces
{
    /// <summary>
    /// Schedules, shifts and their assignment to people.
    ///
    /// The engine owns the calendar arithmetic; this store owns the *rules* the legacy
    /// screens skipped: a shift must cover contiguous days, an assignment window may not
    /// overlap another window for the same person, and a retroactive change is limited
    /// to <see cref="T5Web.Domain.SchedulePolicy.RetroactiveDays"/> because older punches
    /// have already been calculated and reported.
    /// </summary>
    public interface IScheduleStore
    {
        IReadOnlyList<ScheduleRow> GetSchedules();

        IReadOnlyList<ShiftRow> GetShifts();

        IReadOnlyList<UserShiftRow> GetAssignments(long? badgeNumber, int take);

        OperationResult AssignShift(long badgeNumber, int shiftId, DateTime fromDate, DateTime? endDate,
                                    out string detail);
    }
}

namespace T5Web.Domain
{
    /// <summary>Numbers that the schedule rules share with the tests.</summary>
    public static class SchedulePolicy
    {
        /// <summary>How far back a shift assignment may be changed or backdated.</summary>
        public const int RetroactiveDays = 30;

        /// <summary>Longest accepted shift cycle, in days.</summary>
        public const int MaxCycleDays = 62;

        /// <summary>
        /// Whether a proposed assignment start is acceptable. Backdating beyond the
        /// policy window is refused: those days are already calculated and reported.
        /// </summary>
        public static bool IsStartAcceptable(DateTime fromDate, DateTime today, out string reason)
        {
            reason = string.Empty;
            if (fromDate.Date < today.Date.AddDays(-RetroactiveDays))
            {
                reason = "تغییر برنامه برای گذشته بیش از " + RetroactiveDays +
                         " روز مجاز نیست؛ گزارش‌های تاریخی تغییر می‌کنند.";
                return false;
            }
            return true;
        }
    }
}
