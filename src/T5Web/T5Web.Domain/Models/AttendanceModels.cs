using System;
using System.Collections.Generic;

namespace T5Web.Domain.Models
{
    /// <summary>
    /// One attendance punch ([Record]).
    ///
    /// [Record] has no identity column: a punch is identified by who, when and what
    /// (BadgeNumber + CheckTime + CheckType). That is why the portal never offers a
    /// "delete by id" button here - editing a punch means updating exactly that triple,
    /// which is also what the legacy grid did.
    /// </summary>
    public sealed class RecordRow
    {
        public long BadgeNumber { get; set; }
        public string PersonName { get; set; }
        public DateTime CheckTime { get; set; }
        public int CheckType { get; set; }
        public int MachineNumber { get; set; }
        public string MachineName { get; set; }
        public bool IsValid { get; set; }
        public int OriginalCheckType { get; set; }
        public int WorkCode { get; set; }
        public long InsertUser { get; set; }
        public DateTime? InsertTime { get; set; }

        /// <summary>Persian label of the punch direction, as the operator reads it.</summary>
        public string CheckTypeFa
        {
            get
            {
                switch (CheckType)
                {
                    case 0: return "ورود";
                    case 1: return "خروج";
                    case 2: return "شروع استراحت";
                    case 3: return "پایان استراحت";
                    case 4: return "اضافه‌کاری ورود";
                    case 5: return "اضافه‌کاری خروج";
                    default: return "نوع " + CheckType;
                }
            }
        }

        public string Direction
        {
            get
            {
                // T5 uses 0/1 for in/out and 4/5 for overtime in/out.
                return CheckType == 0 || CheckType == 4 ? "in" : "out";
            }
        }

        public bool IsManual
        {
            get { return InsertUser > 0; }
        }
    }

    /// <summary>Filter object for the records screen (never a raw SQL fragment).</summary>
    public sealed class RecordQuery
    {
        public long? BadgeNumber { get; set; }
        public int? MachineNumber { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public bool OnlyInvalid { get; set; }
        public bool OnlyManual { get; set; }
        public int Take { get; set; }

        public RecordQuery Clamped()
        {
            var query = new RecordQuery
            {
                BadgeNumber = BadgeNumber,
                MachineNumber = MachineNumber,
                FromDate = FromDate,
                ToDate = ToDate,
                OnlyInvalid = OnlyInvalid,
                OnlyManual = OnlyManual,
                Take = Take <= 0 ? 200 : Math.Min(Take, 1000)
            };
            // A wrong-way range is a typing mistake, not an empty result: swap it.
            if (query.FromDate.HasValue && query.ToDate.HasValue && query.FromDate > query.ToDate)
            {
                DateTime swap = query.FromDate.Value;
                query.FromDate = query.ToDate;
                query.ToDate = swap;
            }
            return query;
        }
    }

    /// <summary>One leave request ([Request]) with its approval state ([RequestAccept]).</summary>
    public sealed class RequestRow
    {
        public long RequestId { get; set; }
        public long BadgeNumber { get; set; }
        public string PersonName { get; set; }
        public string TitleName { get; set; }
        public int LeaveId { get; set; }
        public string LeaveName { get; set; }
        public int LeaveDescriptionId { get; set; }
        public string DescriptionText { get; set; }
        public DateTime ForDate { get; set; }
        public DateTime? FromTime { get; set; }
        public DateTime? ToTime { get; set; }
        public DateTime? CreateTime { get; set; }
        public bool IsDaily { get; set; }
        public bool IsSuspended { get; set; }
        public int WorkflowType { get; set; }
        public int StepCount { get; set; }
        public int AcceptedCount { get; set; }

        public bool IsFullyAccepted
        {
            get { return StepCount > 0 && AcceptedCount >= StepCount; }
        }

        public bool IsPending
        {
            get { return !IsFullyAccepted && !IsSuspended; }
        }

        public string StateFa
        {
            get
            {
                if (IsSuspended)
                {
                    return "لغو‌شده";
                }
                return IsFullyAccepted ? "تأیید نهایی" : "در انتظار تأیید";
            }
        }

        /// <summary>Duration in hours when the request has explicit from/to times.</summary>
        public double Hours
        {
            get
            {
                if (!FromTime.HasValue || !ToTime.HasValue)
                {
                    return 0;
                }
                double hours = (ToTime.Value - FromTime.Value).TotalHours;
                return hours > 0 ? Math.Round(hours, 2) : 0;
            }
        }
    }

    /// <summary>One row of the monthly attendance/leave summary.</summary>
    public sealed class AttendanceSummaryRow
    {
        public long BadgeNumber { get; set; }
        public string PersonName { get; set; }
        public string DeptName { get; set; }
        public int PresentDays { get; set; }
        public int LateCount { get; set; }
        public int EarlyCount { get; set; }
        public int MissingPunchDays { get; set; }
        /// <summary>Balance the engine stores for the year ([YearRemain].[DailyValue]).</summary>
        public double LeaveBalanceDays { get; set; }

        /// <summary>Leave granted during the window ([LeavesAdded].[DailyValue] (windowed by [AssignDate])).</summary>
        public double LeaveAddedDays { get; set; }
        public double OvertimeHours { get; set; }
    }

    /// <summary>Result of a request decision, so the controller can audit exactly what changed.</summary>
    public sealed class RequestDecision
    {
        public long RequestId { get; set; }
        public bool Cancel { get; set; }
        public string ReasonFa { get; set; }
    }
}
