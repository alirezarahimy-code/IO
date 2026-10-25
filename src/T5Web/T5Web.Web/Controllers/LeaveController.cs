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
    /// Leave balances, grants and the year-end transfer.
    ///
    /// Replaces CardController (balances), InsertDailyLeaveController (grants) and
    /// TransferRemainedLeaveController (year-end). Two rules are new:
    ///   * the transfer is previewed before it is written, and it is all-or-nothing;
    ///   * a grant without a document number is refused - the legacy screen accepted
    ///     anonymous grants, which is how balances drifted.
    /// </summary>
    [AdminGate(AdminFeature.LeaveView, AllowReadOnly = true)]
    public sealed class LeaveController : PortalController
    {
        public ActionResult Index(int? year, int? leaveTypeId, string term, int take = 200)
        {
            ILeaveStore store = PortalServices.LeaveStore;
            if (store == null)
            {
                return NotConfigured("مخزن مرخصی تنظیم نشده است.");
            }

            int current = year.HasValue && year.Value > 1900
                ? year.Value
                : JalaliYear(DateTime.Today);

            var model = new LeaveIndexModel
            {
                Year = current,
                LeaveTypeId = leaveTypeId,
                Term = term,
                Rows = store.GetBalances(current, leaveTypeId, term, take),
                LeaveTypes = store.GetLeaveTypes(),
                CanEdit = CanEdit()
            };
            ViewBag.Title = "مرخصی و مانده";
            return View(model);
        }

        public ActionResult Grants(long? badge, int? leaveTypeId, int take = 100)
        {
            ILeaveStore store = PortalServices.LeaveStore;
            if (store == null)
            {
                return NotConfigured("مخزن مرخصی تنظیم نشده است.");
            }

            var model = new LeaveGrantsModel
            {
                BadgeNumber = badge,
                LeaveTypeId = leaveTypeId,
                Rows = store.GetGrants(badge, leaveTypeId, take),
                LeaveTypes = store.GetLeaveTypes(),
                CanEdit = CanEdit()
            };
            ViewBag.Title = "ثبت مرخصی";
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.LeaveEdit)]
        public ActionResult Grant(long badge, int leaveTypeId, string assignDate, double days,
                                  string documentNumber, string descriptions)
        {
            ILeaveStore store = PortalServices.LeaveStore;
            if (store == null)
            {
                return NotConfigured("مخزن مرخصی تنظیم نشده است.");
            }
            if (string.IsNullOrWhiteSpace(documentNumber))
            {
                TempData["Flash"] = "شماره سند/نامه برای ثبت مرخصی الزامی است.";
                return RedirectToAction("Grants");
            }

            DateTime date;
            if (!DateTime.TryParseExact((assignDate ?? string.Empty).Trim(), "yyyy-MM-dd",
                                        CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                TempData["Flash"] = "تاریخ ثبت معتبر نیست.";
                return RedirectToAction("Grants");
            }

            var grant = new LeaveGrantRow
            {
                BadgeNumber = badge,
                LeaveTypeId = leaveTypeId,
                AssignDate = date,
                Days = days,
                DocumentNumber = documentNumber.Trim(),
                Descriptions = (descriptions ?? string.Empty).Trim()
            };

            OperationResult result = store.AddGrant(grant);
            if (!result.Succeeded)
            {
                TempData["Flash"] = result.Message;
                return RedirectToAction("Grants", new { badge });
            }

            Audit(AdminFeature.LeaveEdit, "ثبت مرخصی", "#" + badge + " " + JalaliText.Format(date),
                "type=" + leaveTypeId + "; days=" + days.ToString("0.##", CultureInfo.InvariantCulture) +
                "; doc=" + grant.DocumentNumber);
            TempData["Flash"] = "مرخصی ثبت شد.";
            return RedirectToAction("Grants", new { badge });
        }

        public ActionResult Transfer(int? fromYear, double carryCap = 0)
        {
            ILeaveStore store = PortalServices.LeaveStore;
            if (store == null)
            {
                return NotConfigured("مخزن مرخصی تنظیم نشده است.");
            }

            int from = fromYear.HasValue && fromYear.Value > 1900 ? fromYear.Value : JalaliYear(DateTime.Today) - 1;
            var model = new LeaveTransferPreviewModel
            {
                FromYear = from,
                CarryCap = carryCap,
                Rows = store.PreviewTransfer(from, from + 1, carryCap, null),
                CanEdit = CanEdit()
            };
            ViewBag.Title = "انتقال مانده سال";
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.LeaveEdit)]
        public ActionResult CommitTransfer(int fromYear, double carryCap, string reason)
        {
            ILeaveStore store = PortalServices.LeaveStore;
            if (store == null)
            {
                return NotConfigured("مخزن مرخصی تنظیم نشده است.");
            }

            // Recompute the preview server-side: the browser must not be able to post
            // its own amounts and invent balances.
            IReadOnlyList<LeaveTransferRow> rows = store.PreviewTransfer(fromYear, fromYear + 1, carryCap, null);
            string detail;
            OperationResult result = store.CommitTransfer(rows, fromYear, fromYear + 1, reason, out detail);
            if (!result.Succeeded)
            {
                TempData["Flash"] = result.Message;
                return RedirectToAction("Transfer", new { fromYear, carryCap });
            }

            Audit(AdminFeature.LeaveEdit, "انتقال مانده سال", fromYear + " → " + (fromYear + 1), detail);
            TempData["Flash"] = "انتقال مانده انجام شد (" + detail + ").";
            return RedirectToAction("Index", new { year = fromYear + 1 });
        }

        public ActionResult Export(int? year, int? leaveTypeId, string term)
        {
            ILeaveStore store = PortalServices.LeaveStore;
            if (store == null)
            {
                return NotConfigured("مخزن مرخصی تنظیم نشده است.");
            }

            int current = year.HasValue && year.Value > 1900 ? year.Value : JalaliYear(DateTime.Today);
            var builder = new StringBuilder();
            builder.AppendLine("\uFEFF" + JalaliText.CsvRow(new[]
            {
                "شماره", "نام", "نوع مرخصی", "سال", "مانده آغاز", "اضافه‌شده", "مصرف", "مانده پایان"
            }));

            foreach (LeaveBalanceRow row in store.GetBalances(current, leaveTypeId, term, 500))
            {
                builder.AppendLine(JalaliText.CsvRow(new[]
                {
                    row.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                    row.PersonName,
                    row.LeaveTypeName,
                    row.Year.ToString(CultureInfo.InvariantCulture),
                    row.OpeningDays.ToString("0.##", CultureInfo.InvariantCulture),
                    row.AddedDays.ToString("0.##", CultureInfo.InvariantCulture),
                    row.UsedDays.ToString("0.##", CultureInfo.InvariantCulture),
                    row.ClosingDays.ToString("0.##", CultureInfo.InvariantCulture)
                }));
            }

            return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv",
                "t5web-leave-" + current + ".csv");
        }

        private bool CanEdit()
        {
            return CurrentSession != null && CurrentSession.CanEdit(AdminFeature.LeaveEdit);
        }

        /// <summary>Jalali year of a Gregorian date, with the framework calendar.</summary>
        internal static int JalaliYear(DateTime value)
        {
            return new System.Globalization.PersianCalendar().GetYear(value);
        }

        private ActionResult NotConfigured(string reason)
        {
            Response.StatusCode = 503;
            return View("~/Views/Shared/NotConfigured.cshtml", new ViewDataDictionary { { "Reason", reason } });
        }
    }

    public sealed class LeaveIndexModel
    {
        public int Year { get; set; }
        public int? LeaveTypeId { get; set; }
        public string Term { get; set; }
        public IReadOnlyList<LeaveBalanceRow> Rows { get; set; }
        public IReadOnlyList<KeyValuePair<int, string>> LeaveTypes { get; set; }
        public bool CanEdit { get; set; }

        public double TotalClosing
        {
            get { return Rows == null ? 0 : Rows.Sum(r => r.ClosingDays); }
        }
    }

    public sealed class LeaveGrantsModel
    {
        public long? BadgeNumber { get; set; }
        public int? LeaveTypeId { get; set; }
        public IReadOnlyList<LeaveGrantRow> Rows { get; set; }
        public IReadOnlyList<KeyValuePair<int, string>> LeaveTypes { get; set; }
        public bool CanEdit { get; set; }
    }

    public sealed class LeaveTransferPreviewModel
    {
        public int FromYear { get; set; }
        public double CarryCap { get; set; }
        public IReadOnlyList<LeaveTransferRow> Rows { get; set; }
        public bool CanEdit { get; set; }

        public double TotalDays
        {
            get { return Rows == null ? 0 : Rows.Sum(r => r.TransferDays); }
        }
    }
}
