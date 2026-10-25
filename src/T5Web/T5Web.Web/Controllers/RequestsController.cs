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
    /// Leave/over-time requests and their approval chain.
    ///
    /// Replaces the legacy ShowUsersRequest and the approval halves of RequestsReport.
    /// The rules the legacy screen lacked:
    ///   * a decision needs <see cref="AdminFeature.RequestApprove"/>, not just "can see";
    ///   * the approver's own title comes from their personnel record, never from the form
    ///     (posting a title would let anybody approve as the director);
    ///   * one step can be decided once (enforced in the store, not only in the UI).
    /// </summary>
    [AdminGate(AdminFeature.RequestView)]
    public sealed class RequestsController : PortalController
    {
        public ActionResult Index(bool pendingOnly = true, long? badge = null)
        {
            IAttendanceStore store = PortalServices.Attendance;
            if (store == null)
            {
                return NotConfigured("مخزن درخواست‌ها تنظیم نشده است.");
            }

            var model = new RequestsIndexModel
            {
                PendingOnly = pendingOnly,
                BadgeNumber = badge,
                Rows = store.QueryRequests(pendingOnly, badge.HasValue && badge.Value > 0 ? badge : null, 200),
                CanApprove = CurrentSession != null && CurrentSession.CanEdit(AdminFeature.RequestApprove),
                MyTitleId = MyTitleId()
            };
            ViewBag.Title = "درخواست‌ها";
            return View(model);
        }

        public ActionResult Detail(long id)
        {
            IAttendanceStore store = PortalServices.Attendance;
            if (store == null)
            {
                return NotConfigured("مخزن درخواست‌ها تنظیم نشده است.");
            }

            RequestRow row = store.GetRequest(id);
            if (row == null)
            {
                return HttpNotFound("درخواست یافت نشد.");
            }

            ViewBag.Title = "درخواست #" + id;
            return View(new RequestDetailModel
            {
                Row = row,
                CanApprove = CurrentSession != null && CurrentSession.CanEdit(AdminFeature.RequestApprove),
                MyTitleId = MyTitleId()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.RequestApprove)]
        public ActionResult Decide(long id, bool cancel)
        {
            IAttendanceStore store = PortalServices.Attendance;
            if (store == null)
            {
                return NotConfigured("مخزن درخواست‌ها تنظیم نشده است.");
            }

            AdminSession session = CurrentSession;
            int titleId = MyTitleId();
            if (titleId <= 0)
            {
                TempData["Flash"] = "برای تأیید درخواست باید عنوان شغلی فعال داشته باشید (جدول عناوین پرسنل).";
                return RedirectToAction("Index");
            }

            string detail;
            OperationResult result = store.DecideRequest(id, session == null ? 0 : session.BadgeNumber,
                                                         titleId, cancel, false, out detail);
            if (!result.Succeeded)
            {
                TempData["Flash"] = result.Message;
                return RedirectToAction("Index");
            }

            Audit(AdminFeature.RequestApprove, cancel ? "لغو درخواست" : "تأیید درخواست",
                "#" + id, detail);
            TempData["Flash"] = cancel ? "درخواست لغو شد." : "مرحله تأیید ثبت شد.";
            return RedirectToAction("Index");
        }

        public ActionResult Export(bool pendingOnly = true)
        {
            IAttendanceStore store = PortalServices.Attendance;
            if (store == null)
            {
                return NotConfigured("مخزن درخواست‌ها تنظیم نشده است.");
            }

            var builder = new StringBuilder();
            builder.AppendLine("\uFEFF" + JalaliText.CsvRow(new[]
            {
                "شناسه", "شماره پرسنلی", "نام", "نوع مرخصی", "تاریخ", "از", "تا", "ساعت",
                "مراحل", "تأییدشده", "وضعیت", "توضیح"
            }));

            foreach (RequestRow row in store.QueryRequests(pendingOnly, null, 500))
            {
                builder.AppendLine(JalaliText.CsvRow(new[]
                {
                    row.RequestId.ToString(CultureInfo.InvariantCulture),
                    row.BadgeNumber.ToString(CultureInfo.InvariantCulture),
                    row.PersonName,
                    row.LeaveName,
                    JalaliText.Format(row.ForDate),
                    row.FromTime.HasValue ? row.FromTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty,
                    row.ToTime.HasValue ? row.ToTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty,
                    row.Hours.ToString("0.##", CultureInfo.InvariantCulture),
                    row.StepCount.ToString(CultureInfo.InvariantCulture),
                    row.AcceptedCount.ToString(CultureInfo.InvariantCulture),
                    row.StateFa,
                    row.DescriptionText
                }));
            }

            return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv",
                "t5web-requests-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv");
        }

        /// <summary>
        /// The approver's title, taken from the personnel record - never from the request.
        /// </summary>
        private int MyTitleId()
        {
            AdminSession session = CurrentSession;
            if (session == null || PortalServices.Repository == null)
            {
                return 0;
            }
            PersonnelRow person = PortalServices.Repository.GetPersonnel(session.BadgeNumber);
            if (person == null)
            {
                return 0;
            }
            // The session already carries the display title; its id is resolved through
            // the repository so a renamed title cannot be spoofed by the browser.
            return PortalServices.Repository.GetTitleId(session.BadgeNumber);
        }

        private ActionResult NotConfigured(string reason)
        {
            Response.StatusCode = 503;
            return View("~/Views/Shared/NotConfigured.cshtml", new ViewDataDictionary { { "Reason", reason } });
        }
    }

    public sealed class RequestsIndexModel
    {
        public bool PendingOnly { get; set; }
        public long? BadgeNumber { get; set; }
        public IReadOnlyList<RequestRow> Rows { get; set; }
        public bool CanApprove { get; set; }
        public int MyTitleId { get; set; }
    }

    public sealed class RequestDetailModel
    {
        public RequestRow Row { get; set; }
        public bool CanApprove { get; set; }
        public int MyTitleId { get; set; }
    }
}
