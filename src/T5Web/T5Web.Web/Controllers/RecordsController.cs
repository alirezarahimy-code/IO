using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    /// <summary>
    /// Attendance punches: the screen an operator actually lives in.
    ///
    /// Covers the legacy Records / InsertRecord / EditLateEarly / TotalControl
    /// controllers. Two rules carried over from the redesign:
    ///   * reading needs <see cref="AdminFeature.RecordsView"/>, writing needs
    ///     <see cref="AdminFeature.RecordManualInsert"/> - the legacy portal had one
    ///     flag for both, so a viewer could correct a punch;
    ///   * every write is audited with the old and new value, because a corrected punch
    ///     changes somebody's pay.
    /// </summary>
    [AdminGate(AdminFeature.RecordsView)]
    public sealed class RecordsController : PortalController
    {
        public ActionResult Index(string from, string to, long? badge, int? machine, bool onlyInvalid = false,
                                 bool onlyManual = false, int take = 200)
        {
            IAttendanceStore store = PortalServices.Attendance;
            if (store == null)
            {
                return NotConfigured("مخزن ترددها تنظیم نشده است.");
            }

            var query = new RecordQuery
            {
                BadgeNumber = badge.HasValue && badge.Value > 0 ? badge : null,
                MachineNumber = machine.HasValue && machine.Value > 0 ? machine : null,
                FromDate = ParseIso(from),
                ToDate = ParseIso(to),
                OnlyInvalid = onlyInvalid,
                OnlyManual = onlyManual,
                Take = take
            }.Clamped();

            var model = new RecordsIndexModel
            {
                Query = query,
                Rows = store.QueryRecords(query),
                CheckTypes = store.GetCheckTypes(),
                Devices = PortalServices.Repository.GetDevices(null),
                CanEdit = CurrentSession != null && CurrentSession.CanEdit(AdminFeature.RecordManualInsert)
            };
            ViewBag.Title = "ترددها";
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.RecordManualInsert)]
        public ActionResult Correct(long badge, string checkTime, int checkType, int originalType,
                                    bool isValid, string reason)
        {
            IAttendanceStore store = PortalServices.Attendance;
            if (store == null)
            {
                return NotConfigured("مخزن ترددها تنظیم نشده است.");
            }
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 4)
            {
                TempData["Flash"] = "برای اصلاح تردد، دلیل (حداقل ۴ نویسه) لازم است.";
                return RedirectToAction("Index");
            }

            DateTime when;
            if (!DateTime.TryParseExact((checkTime ?? string.Empty).Trim(), "yyyy-MM-ddTHH:mm",
                                        CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
            {
                TempData["Flash"] = "زمان تردد معتبر نیست.";
                return RedirectToAction("Index");
            }

            var row = new RecordRow
            {
                BadgeNumber = badge,
                CheckTime = when,
                CheckType = checkType,
                OriginalCheckType = originalType,
                IsValid = isValid
            };

            OperationResult result = store.SaveRecord(row, false);
            if (!result.Succeeded)
            {
                TempData["Flash"] = result.Message;
                return RedirectToAction("Index");
            }

            Audit(AdminFeature.RecordManualInsert, "اصلاح تردد", "#" + badge + " @ " + checkTime,
                "type " + originalType + "→" + checkType + "; valid=" + isValid + "; reason=" + reason.Trim());
            TempData["Flash"] = "تردد اصلاح شد و در دفتر رخداد ثبت گردید.";
            return RedirectToAction("Index", new { badge = badge });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.RecordManualInsert)]
        public ActionResult Insert(long badge, string checkTime, int checkType, int machine, string reason)
        {
            IAttendanceStore store = PortalServices.Attendance;
            if (store == null)
            {
                return NotConfigured("مخزن ترددها تنظیم نشده است.");
            }
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 4)
            {
                TempData["Flash"] = "برای ثبت دستی تردد، دلیل (حداقل ۴ نویسه) لازم است.";
                return RedirectToAction("Index");
            }

            DateTime when;
            if (!DateTime.TryParseExact((checkTime ?? string.Empty).Trim(), "yyyy-MM-ddTHH:mm",
                                        CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
            {
                TempData["Flash"] = "زمان تردد معتبر نیست.";
                return RedirectToAction("Index");
            }

            var row = new RecordRow
            {
                BadgeNumber = badge,
                CheckTime = when,
                CheckType = checkType,
                MachineNumber = machine,
                IsValid = true,
                InsertUser = CurrentSession == null ? 0 : CurrentSession.BadgeNumber,
                InsertTime = DateTime.UtcNow
            };

            OperationResult result = store.SaveRecord(row, true);
            if (!result.Succeeded)
            {
                TempData["Flash"] = result.Message;
                return RedirectToAction("Index");
            }

            Audit(AdminFeature.RecordManualInsert, "ثبت دستی تردد", "#" + badge + " @ " + checkTime,
                "type " + checkType + "; machine=" + machine + "; reason=" + reason.Trim());
            TempData["Flash"] = "تردد دستی ثبت شد و در دفتر رخداد ثبت گردید.";
            return RedirectToAction("Index", new { badge = badge });
        }

        public ActionResult Export(string from, string to, long? badge, int? machine, bool onlyInvalid = false)
        {
            IAttendanceStore store = PortalServices.Attendance;
            if (store == null)
            {
                return NotConfigured("مخزن ترددها تنظیم نشده است.");
            }

            var query = new RecordQuery
            {
                BadgeNumber = badge,
                MachineNumber = machine,
                FromDate = ParseIso(from),
                ToDate = ParseIso(to),
                OnlyInvalid = onlyInvalid,
                Take = 1000
            }.Clamped();

            var builder = new StringBuilder();
            builder.AppendLine("\uFEFF" + JalaliText.CsvRow(new[]
            {
                "شماره پرسنلی", "نام", "تاریخ", "ساعت", "نوع", "دستگاه", "معتبر", "ثبت دستی"
            }));

            foreach (RecordRow row in store.QueryRecords(query))
            {
                builder.AppendLine(JalaliText.CsvRow(new[]
                {
                    row.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                    row.PersonName,
                    JalaliText.Format(row.CheckTime),
                    row.CheckTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    row.CheckTypeFa,
                    row.MachineName,
                    row.IsValid ? "بله" : "خیر",
                    row.IsManual ? "بله" : "خیر"
                }));
            }

            string name = "t5web-records-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv";
            return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", name);
        }

        private ActionResult NotConfigured(string reason)
        {
            Response.StatusCode = 503;
            return View("~/Views/Shared/NotConfigured.cshtml", new ViewDataDictionary { { "Reason", reason } });
        }

        private static DateTime? ParseIso(string value)
        {
            DateTime date;
            if (!string.IsNullOrWhiteSpace(value) &&
                DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out date))
            {
                return date;
            }
            return null;
        }
    }

    /// <summary>Model of the records screen.</summary>
    public sealed class RecordsIndexModel
    {
        public RecordQuery Query { get; set; }
        public IReadOnlyList<RecordRow> Rows { get; set; }
        public IReadOnlyList<KeyValuePair<int, string>> CheckTypes { get; set; }
        public IReadOnlyList<DeviceRow> Devices { get; set; }
        public bool CanEdit { get; set; }

        public string FromIso
        {
            get { return Query != null && Query.FromDate.HasValue ? Query.FromDate.Value.ToString("yyyy-MM-dd") : string.Empty; }
        }

        public string ToIso
        {
            get { return Query != null && Query.ToDate.HasValue ? Query.ToDate.Value.ToString("yyyy-MM-dd") : string.Empty; }
        }
    }
}
