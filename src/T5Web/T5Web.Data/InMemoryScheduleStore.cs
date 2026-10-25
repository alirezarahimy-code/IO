using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// In-memory schedule store: same overlap rule and same retroactive policy as the
    /// SQL store, so T19 can assert both without a database.
    /// </summary>
    public sealed class InMemoryScheduleStore : IScheduleStore
    {
        private readonly List<ScheduleRow> _schedules = new List<ScheduleRow>();
        private readonly List<ShiftRow> _shifts = new List<ShiftRow>();
        private readonly List<UserShiftRow> _assignments = new List<UserShiftRow>();

        public DateTime Today { get; set; } = DateTime.Today;

        public InMemoryScheduleStore AddSchedule(int id, string name, params int[] dayIndexes)
        {
            _schedules.Add(new ScheduleRow { ScheduleId = id, Name = name, ShiftCount = dayIndexes.Length });
            return this;
        }

        public InMemoryScheduleStore AddShift(int id, string name, int period, params int[] dayIndexes)
        {
            _shifts.Add(new ShiftRow
            {
                ShiftId = id,
                ShiftName = name,
                Period = period,
                Unit = 1,
                DayIndexes = dayIndexes.OrderBy(i => i).ToList(),
                DayCount = dayIndexes.Length
            });
            return this;
        }

        public InMemoryScheduleStore AddAssignment(long badge, int shiftId, DateTime from, DateTime? to)
        {
            _assignments.Add(new UserShiftRow
            {
                BadgeNumber = badge,
                PersonName = "پرسنل " + badge,
                ShiftId = shiftId,
                ShiftName = ShiftName(shiftId),
                FromDate = from.Date,
                EndDate = to.HasValue ? (DateTime?)to.Value.Date : null
            });
            return this;
        }

        public IReadOnlyList<ScheduleRow> GetSchedules()
        {
            return _schedules.ToList();
        }

        public IReadOnlyList<ShiftRow> GetShifts()
        {
            return _shifts.ToList();
        }

        public IReadOnlyList<UserShiftRow> GetAssignments(long? badgeNumber, int take)
        {
            return _assignments
                .Where(a => !badgeNumber.HasValue || badgeNumber.Value <= 0 || a.BadgeNumber == badgeNumber.Value)
                .OrderByDescending(a => a.FromDate)
                .Take(take <= 0 ? 200 : take)
                .ToList();
        }

        public OperationResult AssignShift(long badgeNumber, int shiftId, DateTime fromDate, DateTime? endDate,
                                          out string detail)
        {
            detail = string.Empty;
            if (badgeNumber <= 0)
            {
                return OperationResult.Fail("شماره پرسنلی معتبر نیست.");
            }
            if (shiftId <= 0 || _shifts.All(s => s.ShiftId != shiftId))
            {
                return OperationResult.Fail("شیفت انتخاب نشده است.");
            }

            string policyFailure;
            if (!SchedulePolicy.IsStartAcceptable(fromDate, Today, out policyFailure))
            {
                return OperationResult.Fail(policyFailure);
            }
            if (endDate.HasValue && endDate.Value.Date < fromDate.Date)
            {
                return OperationResult.Fail("تاریخ پایان نمی‌تواند پیش از تاریخ شروع باشد.");
            }

            DateTime start = fromDate.Date;
            DateTime end = endDate.HasValue ? endDate.Value.Date : DateTime.MaxValue.Date;
            bool overlaps = _assignments.Any(a =>
                a.BadgeNumber == badgeNumber &&
                a.FromDate.Date <= end &&
                (a.EndDate.HasValue ? a.EndDate.Value.Date : DateTime.MaxValue.Date) >= start);
            if (overlaps)
            {
                return OperationResult.Fail("بازه انتخابی با یک بازه فعال دیگر برای همین پرسنل هم‌پوشانی دارد.");
            }

            _assignments.Add(new UserShiftRow
            {
                BadgeNumber = badgeNumber,
                PersonName = "پرسنل " + badgeNumber,
                ShiftId = shiftId,
                ShiftName = ShiftName(shiftId),
                FromDate = start,
                EndDate = endDate.HasValue ? (DateTime?)end : null
            });

            detail = "badge=" + badgeNumber + "; shift=" + shiftId + "; from=" + start.ToString("yyyy-MM-dd");
            return OperationResult.Ok();
        }

        private string ShiftName(int shiftId)
        {
            ShiftRow shift = _shifts.FirstOrDefault(s => s.ShiftId == shiftId);
            return shift == null ? "شیفت " + shiftId : shift.ShiftName;
        }
    }
}
