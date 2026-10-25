using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    /// <summary>
    /// The logs the portal may read (docs/11 §2): the desktop engine's change log, the
    /// device operations per person, and the download book.
    ///
    /// Read-only by construction: the controller has no POST action at all. The device
    /// service's own logs (<c>RecordsLog*</c>, <c>CommandLog</c>, <c>EvenetsLog</c>) are
    /// *not* here - they live in a catalog this portal never opens, and pretending
    /// otherwise would mean a second connection string and a second security review.
    /// </summary>
    [AdminGate(AdminFeature.LogView, AllowReadOnly = true)]
    public sealed class LogsController : PortalController
    {
        public ActionResult Index(string source, string from, string to, long? badge, string term, int take = 100)
        {
            ILogStore store = PortalServices.Logs;
            if (store == null)
            {
                return NotConfigured("مخزن لاگ‌ها تنظیم نشده است.");
            }

            IReadOnlyList<LogSourceDescriptor> sources = store.Sources();
            LogSourceDescriptor selected = sources.FirstOrDefault(s =>
                string.Equals(s.Code, source, StringComparison.OrdinalIgnoreCase)) ?? sources.FirstOrDefault();

            DateTime toDate = ParseDate(to, DateTime.Today);
            DateTime fromDate = ParseDate(from, toDate.AddDays(-6));
            if (fromDate > toDate)
            {
                DateTime swap = fromDate;
                fromDate = toDate;
                toDate = swap;
            }

            var model = new LogsIndexModel
            {
                Sources = sources,
                SourceCode = selected == null ? string.Empty : selected.Code,
                Selected = selected,
                From = fromDate,
                To = toDate,
                Badge = badge,
                Term = term ?? string.Empty,
                Take = take,
                Rows = selected == null
                    ? new List<LogEntryRow>()
                    : store.Search(selected.Code, fromDate, toDate, badge, term, take)
            };
            ViewBag.Title = selected == null ? "لاگ‌ها" : selected.TitleFa;
            return View(model);
        }

        /// <summary>"2026-10-25" or empty; the form always posts ISO, never a Jalali string.</summary>
        internal static DateTime ParseDate(string text, DateTime fallback)
        {
            DateTime parsed;
            if (!string.IsNullOrWhiteSpace(text) &&
                DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out parsed))
            {
                return parsed.Date;
            }
            return fallback.Date;
        }

        private ActionResult NotConfigured(string reason)
        {
            Response.StatusCode = 503;
            return View("~/Views/Shared/NotConfigured.cshtml", new ViewDataDictionary { { "Reason", reason } });
        }
    }

    /// <summary>View model for the log reader.</summary>
    public sealed class LogsIndexModel
    {
        public IReadOnlyList<LogSourceDescriptor> Sources { get; set; } = new List<LogSourceDescriptor>();
        public LogSourceDescriptor Selected { get; set; }
        public string SourceCode { get; set; } = string.Empty;
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public long? Badge { get; set; }
        public string Term { get; set; } = string.Empty;
        public int Take { get; set; }
        public IReadOnlyList<LogEntryRow> Rows { get; set; } = new List<LogEntryRow>();

        public string FromIso
        {
            get { return From.ToString("yyyy-MM-dd"); }
        }

        public string ToIso
        {
            get { return To.ToString("yyyy-MM-dd"); }
        }

        public int Days
        {
            get { return (int)(To.Date - From.Date).TotalDays + 1; }
        }
    }
}
