using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    /// <summary>
    /// Schedules, shifts and assignment windows.
    ///
    /// Replaces ScheduleController / TempSchedulesController / AutoSchedule from the
    /// legacy portal (the legacy portal had no ShiftController at all - shifts were only
    /// editable on the desktop). The CRUD of the raw rows stays in the module screens;
    /// this controller adds the two rules the legacy screens lacked:
    ///   * a shift cycle must cover contiguous days 1..n (a gap makes the engine's
    ///     day-index lookup return the wrong schedule);
    ///   * a person may hold only one assignment window at a time, and a backdated
    ///     change is limited to <see cref="SchedulePolicy.RetroactiveDays"/>.
    /// </summary>
    [AdminGate(AdminFeature.ScheduleView, AllowReadOnly = true)]
    public sealed class ScheduleController : PortalController
    {
        public ActionResult Index(long? badge, int take = 200)
        {
            IScheduleStore store = PortalServices.ScheduleStore;
            if (store == null)
            {
                return NotConfigured("مخزن شیفت و برنامه تنظیم نشده است.");
            }

            IReadOnlyList<ShiftRow> shifts = store.GetShifts();
            var model = new ScheduleIndexModel
            {
                BadgeNumber = badge,
                Schedules = store.GetSchedules(),
                Shifts = shifts,
                Assignments = store.GetAssignments(badge, take),
                CanEdit = CurrentSession != null && CurrentSession.CanEdit(AdminFeature.ScheduleEdit),
                CycleProblems = shifts
                    .Select(shift => new KeyValuePair<ShiftRow, string>(shift, CycleProblem(shift)))
                    .Where(pair => !string.IsNullOrEmpty(pair.Value))
                    .ToList()
            };
            ViewBag.Title = "شیفت و برنامه";
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.ScheduleEdit)]
        public ActionResult Assign(long badge, int shiftId, string fromDate, string endDate)
        {
            IScheduleStore store = PortalServices.ScheduleStore;
            if (store == null)
            {
                return NotConfigured("مخزن شیفت و برنامه تنظیم نشده است.");
            }

            DateTime from;
            if (!DateTime.TryParseExact((fromDate ?? string.Empty).Trim(), "yyyy-MM-dd",
                                        CultureInfo.InvariantCulture, DateTimeStyles.None, out from))
            {
                TempData["Flash"] = "تاریخ شروع معتبر نیست.";
                return RedirectToAction("Index", new { badge });
            }

            DateTime? to = null;
            DateTime parsedTo;
            if (!string.IsNullOrWhiteSpace(endDate) &&
                DateTime.TryParseExact(endDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out parsedTo))
            {
                to = parsedTo;
            }

            string detail;
            OperationResult result = store.AssignShift(badge, shiftId, from, to, out detail);
            if (!result.Succeeded)
            {
                TempData["Flash"] = result.Message;
                return RedirectToAction("Index", new { badge });
            }

            Audit(AdminFeature.ScheduleEdit, "تخصیص شیفت", "#" + badge, detail);
            TempData["Flash"] = "برنامه ثبت شد.";
            return RedirectToAction("Index", new { badge });
        }

        /// <summary>
        /// A shift cycle must be a contiguous 1..n run; a gap means the engine's day-index
        /// lookup silently falls back to the first schedule.
        /// </summary>
        internal static string CycleProblem(ShiftRow shift)
        {
            if (shift == null)
            {
                return string.Empty;
            }
            if (shift.DayIndexes == null || shift.DayIndexes.Count == 0)
            {
                return "هیچ روزی به این شیفت تخصیص نیافته است؛ حداقل یک برنامه لازم است.";
            }

            List<int> sorted = shift.DayIndexes.Distinct().OrderBy(i => i).ToList();
            if (sorted[0] != 1)
            {
                return "شماره روزها باید از ۱ شروع شود (شروع فعلی: " + sorted[0] + ").";
            }
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] != i + 1)
                {
                    return "روزهای شیفت پیوسته نیستند (روز " + (i + 1) + " خالی است).";
                }
            }
            if (sorted.Count > SchedulePolicy.MaxCycleDays)
            {
                return "چرخه شیفت بیش از " + SchedulePolicy.MaxCycleDays + " روز است.";
            }
            return string.Empty;
        }

        private ActionResult NotConfigured(string reason)
        {
            Response.StatusCode = 503;
            return View("~/Views/Shared/NotConfigured.cshtml", new ViewDataDictionary { { "Reason", reason } });
        }
    }

    public sealed class ScheduleIndexModel
    {
        public long? BadgeNumber { get; set; }
        public IReadOnlyList<ScheduleRow> Schedules { get; set; }
        public IReadOnlyList<ShiftRow> Shifts { get; set; }
        public IReadOnlyList<UserShiftRow> Assignments { get; set; }
        public IReadOnlyList<KeyValuePair<ShiftRow, string>> CycleProblems { get; set; }
        public bool CanEdit { get; set; }
    }
}
