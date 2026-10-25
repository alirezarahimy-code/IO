using System;
using TWF.Domain.ValueObjects;

namespace TWF.Domain.Models
{
    /// <summary>
    /// Represents a leave type definition in T5 (T5V3.Model.LeaveType).
    /// </summary>
    public sealed class LeaveTypeDefinitionDto
    {
        public int LeaveTypeId { get; set; }
        public string LeaveName { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public bool IsDuty { get; set; }
        public bool IsHourly { get; set; }
        public int EachXHoursADayMinutes { get; set; } = 440; // Default 7h 20m = 440 mins
        public bool GetInHolidays { get; set; }
        public int MaxMinusMinutes { get; set; }
        public int YearlyMaxMinutes { get; set; } = 26 * 440; // Default 26 days/year
        public int MonthlyMaxMinutes { get; set; } = 0;
        public int? DecreaseFromLeaveId { get; set; }
        public bool IsPaid { get; set; } = true;
    }

    /// <summary>
    /// Represents an approved daily leave record in T5 (DailyLeaves).
    /// </summary>
    public sealed class DailyLeaveRecordDto
    {
        public int Id { get; set; }
        public long BadgeNumber { get; set; }
        public DateTime LeaveDate { get; set; }
        public DateTime ForDate { get => LeaveDate; set => LeaveDate = value; }
        public int LeaveTypeId { get; set; }
        public int DetailDescriptionId { get; set; }
        public string Descriptions { get; set; } = string.Empty;
        public string DocumentNumber { get; set; } = string.Empty;
        public bool IsHoliday { get; set; }
    }

    /// <summary>
    /// Represents an approved hourly leave record in T5 (HourlyLeaves).
    /// </summary>
    public sealed class HourlyLeaveRecordDto
    {
        public int Id { get; set; }
        public long BadgeNumber { get; set; }
        public DateTime FromTime { get; set; }
        public DateTime ToTime { get; set; }
        public int LeaveTypeId { get; set; }
        public int DetailDescriptionId { get; set; }
        public string Descriptions { get; set; } = string.Empty;
        public int ValidDurationMinutes => Math.Max(0, (int)Math.Round((ToTime - FromTime).TotalMinutes));
    }

    /// <summary>
    /// Represents an employee's calculated leave balance for a Solar Hijri year (RemainLeave).
    /// Eliminates BUG-06 by computing exact PersianCalendar year boundaries.
    /// </summary>
    public sealed class LeaveBalanceSummaryDto
    {
        public long BadgeNumber { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeFullName { get => EmployeeName; set => EmployeeName = value; }
        public int LeaveTypeId { get; set; }
        public string LeaveTypeName { get; set; } = string.Empty;
        public int PersianYear { get; set; }
        public DateTime YearStartDate { get; set; }
        public DateTime AsOfDate { get; set; }
        public int EachDayHoursMinutes { get; set; } = 440;
        public int AnnualEntitlementMinutes { get; set; }
        public int CarryOverMinutes { get; set; }
        public int DailyTakenDays { get; set; }
        public int DailyTakenMinutes { get; set; }
        public int HourlyTakenMinutes { get; set; }
        public int TotalTakenMinutes { get; set; }
        public int PendingRequestedMinutes { get; set; }
        public int RemainingMinutes { get; set; }
        public int MaxMinusMinutes { get; set; }

        public int EffectiveRemainingWithPendingMinutes => RemainingMinutes - PendingRequestedMinutes;

        public double TotalTakenDays => EachDayHoursMinutes > 0 ? (double)TotalTakenMinutes / EachDayHoursMinutes : 0.0;
        public double PendingRequestedDays => EachDayHoursMinutes > 0 ? (double)PendingRequestedMinutes / EachDayHoursMinutes : 0.0;
        public double RemainingDays => EachDayHoursMinutes > 0 ? (double)RemainingMinutes / EachDayHoursMinutes : 0.0;
        public double MaxMinusDays => EachDayHoursMinutes > 0 ? (double)MaxMinusMinutes / EachDayHoursMinutes : 0.0;

        public string FormattedHourlyTaken => TimeDurationHelper.MinutesToTimeString(HourlyTakenMinutes);
        public string FormattedTotalTaken => FormatDaysAndHours(TotalTakenMinutes, EachDayHoursMinutes);
        public string FormattedPendingRequested => FormatDaysAndHours(PendingRequestedMinutes, EachDayHoursMinutes);
        public string FormattedRemaining => FormatDaysAndHours(RemainingMinutes, EachDayHoursMinutes);
        public string FormattedEffectiveRemaining => FormatDaysAndHours(EffectiveRemainingWithPendingMinutes, EachDayHoursMinutes);

        public static string FormatDaysAndHours(int totalMinutes, int dayMinutes)
        {
            if (dayMinutes <= 0)
                dayMinutes = 440;

            bool negative = totalMinutes < 0;
            int abs = Math.Abs(totalMinutes);
            int days = abs / dayMinutes;
            int remMinutes = abs % dayMinutes;
            int hours = remMinutes / 60;
            int mins = remMinutes % 60;

            string sign = negative ? "-" : string.Empty;
            return $"{sign}{days} روز و {hours:00}:{mins:00} ساعت";
        }
    }
}
