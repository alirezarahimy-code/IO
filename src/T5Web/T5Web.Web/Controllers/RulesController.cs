using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using T5Web.Application.Modules;
using T5Web.Application.Services;
using T5Web.Domain;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    /// <summary>
    /// The three-layer attendance rule set: global rule, department exception,
    /// personnel exception.
    ///
    /// The CRUD itself lives in the generic module screens (<c>/Modules/schedule</c>,
    /// <c>/Modules/rule</c>, …); this controller answers the question the legacy screens
    /// could not: *which* rule actually applies to a person on a date, and where the
    /// layers contradict each other.
    /// </summary>
    [AdminGate(AdminFeature.RulesView)]
    public sealed class RulesController : PortalController
    {
        private const int MaxLayerRows = 500;

        public ActionResult Index(long? badge, string date, int globalRuleId = 0)
        {
            ModuleService modules = PortalServices.Modules;
            if (modules == null)
            {
                return NotConfigured("مخزن ماژول‌ها تنظیم نشده است.");
            }

            DateTime day;
            if (!DateTime.TryParseExact((date ?? string.Empty).Trim(), "yyyy-MM-dd",
                                        CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            {
                day = DateTime.Today;
            }

            var model = new RulesIndexModel
            {
                BadgeNumber = badge,
                Date = day,
                GlobalRuleId = globalRuleId,
                CanEdit = CurrentSession != null && CurrentSession.CanEdit(AdminFeature.RulesEdit),
                UserRules = LoadLayer(modules, "userrule"),
                DepartmentRules = LoadLayer(modules, "departmentrule")
            };

            if (badge.HasValue && badge.Value > 0)
            {
                int departmentId = 0;
                PersonnelRow person = PortalServices.Repository.GetPersonnel(badge.Value);
                if (person != null)
                {
                    departmentId = person.DeptId;
                }

                var resolver = new RuleResolutionService(model.UserRules, model.DepartmentRules, globalRuleId);
                model.Resolution = resolver.Resolve(badge.Value, departmentId, day);
            }

            ViewBag.Title = "قواعد حضور";
            return View(model);
        }

        /// <summary>
        /// Checks a candidate window before the generic module screen writes it.
        /// Kept here (not in the store) so both the UI and the tests use one rule.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.RulesEdit)]
        public ActionResult Check(int badgeNumber, int departmentId, int ruleId, string fromDate, string toDate,
                                 bool userLayer = true, long rowId = 0)
        {
            ModuleService modules = PortalServices.Modules;
            if (modules == null)
            {
                return NotConfigured("مخزن ماژول‌ها تنظیم نشده است.");
            }

            var candidate = new RuleLayerRow
            {
                RowId = rowId,
                BadgeNumber = badgeNumber,
                DepartmentId = departmentId,
                RuleId = ruleId,
                FromDate = ParseDate(fromDate),
                ToDate = ParseDate(toDate)
            };

            var resolver = new RuleResolutionService(LoadLayer(modules, "userrule"),
                                                     LoadLayer(modules, "departmentrule"), 0);
            OperationResult result = resolver.ValidateWindow(candidate, userLayer);

            Audit(AdminFeature.RulesEdit, "بررسی بازه قاعده",
                (userLayer ? "پرسنل " + badgeNumber : "واحد " + departmentId),
                "rule=" + ruleId + "; " + fromDate + ".." + toDate + "; " + (result.Succeeded ? "OK" : result.Message));

            TempData["Flash"] = result.Succeeded
                ? "بازه آزاد است؛ می‌توانید قاعده را ثبت کنید."
                : result.Message;
            return RedirectToAction("Index", new { badge = userLayer ? badgeNumber : (long?)null });
        }

        private IReadOnlyList<RuleLayerRow> LoadLayer(ModuleService modules, string code)
        {
            ModuleDescriptor descriptor = ModuleRegistry.Find(code);
            if (descriptor == null)
            {
                return new List<RuleLayerRow>();
            }

            ModulePage page = modules.Page(descriptor, string.Empty, 1, MaxLayerRows);
            var rows = new List<RuleLayerRow>();
            foreach (ModuleRow row in page.Rows)
            {
                rows.Add(new RuleLayerRow
                {
                    RowId = ToLong(code == "userrule" ? row["UserRuleID"] : row["DeptRuleID"]),
                    BadgeNumber = ToLong(row["BadgeNumber"]),
                    DepartmentId = ToInt(row["DepartmentID"]),
                    RuleId = ToInt(row["RuleID"]),
                    RuleType = ToInt(row["RuleType"]),
                    FromDate = ToDate(row["FromDate"]),
                    ToDate = ToDate(row["ToDate"])
                });
            }
            return rows;
        }

        private static int ToInt(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return 0;
            }
            int parsed;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static long ToLong(object value)
        {
            long parsed;
            return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static DateTime? ToDate(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return null;
            }
            DateTime parsed;
            if (value is DateTime)
            {
                return (DateTime)value;
            }
            return DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed)
                ? parsed
                : (DateTime?)null;
        }

        private static DateTime? ParseDate(string value)
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

        private ActionResult NotConfigured(string reason)
        {
            Response.StatusCode = 503;
            return View("~/Views/Shared/NotConfigured.cshtml", new ViewDataDictionary { { "Reason", reason } });
        }
    }

    public sealed class RulesIndexModel
    {
        public long? BadgeNumber { get; set; }
        public DateTime Date { get; set; }
        public int GlobalRuleId { get; set; }
        public bool CanEdit { get; set; }
        public IReadOnlyList<RuleLayerRow> UserRules { get; set; }
        public IReadOnlyList<RuleLayerRow> DepartmentRules { get; set; }
        public RuleResolution Resolution { get; set; }

        public string DateIso
        {
            get { return Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        }
    }
}
