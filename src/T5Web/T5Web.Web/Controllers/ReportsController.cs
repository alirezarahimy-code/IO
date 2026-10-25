using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using T5Web.Application.Services;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    /// <summary>
    /// The report shell: filter, access check and export.
    ///
    /// Replaces 11 legacy report controllers (docs/03 lists 26 report actions). Three
    /// rules are new:
    ///   * a report the session cannot read is not listed (legacy listed everything);
    ///   * the date window is validated per report, because a 366-day grid report is a
    ///     table scan and the legacy screen happily accepted it;
    ///   * the export is built in memory - no remote font, no RDLC service, no network.
    /// </summary>
    [AdminGate(AdminFeature.ReportView, AdminFeature.PersonnelView, AdminFeature.LeaveView,
               AdminFeature.RequestView, AllowReadOnly = true)]
    public sealed class ReportsController : PortalController
    {
        public ActionResult Index(string code, string from, string to, long? badge, int? departmentId)
        {
            IReadOnlyList<ReportDescriptor> visible = ReportCatalog.VisibleFor(CurrentSession);
            if (visible.Count == 0)
            {
                Response.StatusCode = 403;
                return View("~/Views/Account/NoAccess.cshtml", new ViewDataDictionary
                {
                    { "Reason", "هیچ گزارشی برای سطح دسترسی شما تعریف نشده است." }
                });
            }

            ReportDescriptor descriptor = null;
            if (!string.IsNullOrWhiteSpace(code))
            {
                descriptor = visible.FirstOrDefault(r =>
                    string.Equals(r.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
                if (descriptor == null)
                {
                    // The report exists but this session cannot read it: 404, not 403, so the
                    // screen does not confirm what it is not allowed to see.
                    return HttpNotFound("گزارش یافت نشد.");
                }
            }

            DateTime fromDate = ParseIso(from) ?? DateTime.Today.AddDays(-6);
            DateTime toDate = ParseIso(to) ?? DateTime.Today;

            var model = new ReportsIndexModel
            {
                Reports = visible,
                Selected = descriptor,
                From = fromDate,
                To = toDate,
                BadgeNumber = badge,
                DepartmentId = departmentId
            };

            if (descriptor != null)
            {
                OperationResult range = ReportCatalog.ValidateRange(descriptor, fromDate, toDate);
                if (!range.Succeeded)
                {
                    model.Error = range.Message;
                }
                else
                {
                    IReportStore store = PortalServices.Reports;
                    if (store == null)
                    {
                        model.Error = "موتور گزارش تنظیم نشده است.";
                    }
                    else
                    {
                        model.Result = store.Run(descriptor.Code, fromDate, toDate, badge, departmentId);
                        Audit(AdminFeature.ReportView, "اجرای گزارش", descriptor.TitleFa,
                            "from=" + fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                            "; to=" + toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                            "; rows=" + model.Result.RowCount);
                    }
                }
            }

            ViewBag.Title = "گزارش‌ها";
            return View(model);
        }

        public ActionResult Export(string code, string from, string to, long? badge, int? departmentId)
        {
            ReportDescriptor descriptor = ReportCatalog.Find(code);
            if (descriptor == null || CurrentSession == null || !CurrentSession.CanView(descriptor.Feature))
            {
                return HttpNotFound("گزارش یافت نشد.");
            }
            if (CurrentSession.LevelOf(AdminFeature.ReportExport) != AccessLevel.Allow)
            {
                Response.StatusCode = 403;
                return View("~/Views/Account/NoAccess.cshtml", new ViewDataDictionary
                {
                    { "Reason", "برای خروجی گرفتن، سطح دسترسی «" + AdminFeatures.TitleOf(AdminFeature.ReportExport) + "» لازم است." }
                });
            }

            DateTime fromDate = ParseIso(from) ?? DateTime.Today.AddDays(-6);
            DateTime toDate = ParseIso(to) ?? DateTime.Today;

            OperationResult range = ReportCatalog.ValidateRange(descriptor, fromDate, toDate);
            IReportStore store = PortalServices.Reports;
            if (!range.Succeeded || store == null)
            {
                TempData["Flash"] = range.Succeeded ? "موتور گزارش تنظیم نشده است." : range.Message;
                return RedirectToAction("Index", new { code, from, to, badge, departmentId });
            }

            ReportResult result = store.Run(descriptor.Code, fromDate, toDate, badge, departmentId);
            var builder = new StringBuilder();
            builder.AppendLine("\uFEFF" + JalaliText.CsvRow(result.Columns));
            foreach (IReadOnlyList<string> row in result.Rows)
            {
                builder.AppendLine(JalaliText.CsvRow(row));
            }

            Audit(AdminFeature.ReportExport, "خروجی گزارش", descriptor.TitleFa,
                "rows=" + result.RowCount + "; " + result.RangeFa);

            string name = "t5web-" + descriptor.Code + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv";
            return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", name);
        }

        private static DateTime? ParseIso(string value)
        {
            DateTime parsed;
            if (!string.IsNullOrWhiteSpace(value) &&
                DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out parsed))
            {
                return parsed;
            }
            return null;
        }
    }

    public sealed class ReportsIndexModel
    {
        public IReadOnlyList<ReportDescriptor> Reports { get; set; }
        public ReportDescriptor Selected { get; set; }
        public ReportResult Result { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public long? BadgeNumber { get; set; }
        public int? DepartmentId { get; set; }
        public string Error { get; set; }

        public string FromIso
        {
            get { return From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        }

        public string ToIso
        {
            get { return To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        }
    }
}
