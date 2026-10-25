using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using T5Web.Domain;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    /// <summary>
    /// Data health: the logical relations the engine keeps without foreign keys
    /// (docs/11 §3). This is the screen that answers "why is somebody's balance wrong"
    /// before anyone opens SQL Server Management Studio.
    ///
    /// Portal administrators only, and read-only: the checks explain a problem, they never
    /// repair it. Repairing means deciding which side of a broken link is correct, and
    /// that decision belongs to the operator on the record, not to a button.
    /// </summary>
    [AdminGate(AdminFeature.PortalAdmin, AllowReadOnly = true)]
    public sealed class IntegrityController : PortalController
    {
        public ActionResult Index()
        {
            IIntegrityStore store = PortalServices.Integrity;
            if (store == null)
            {
                return NotConfigured("مخزن بررسی سلامت داده تنظیم نشده است.");
            }

            IReadOnlyList<IntegrityCheckRow> checks;
            string error = null;
            try
            {
                checks = store.Run();
            }
            catch (Exception ex)
            {
                checks = new List<IntegrityCheckRow>();
                error = ex.Message;
            }

            var model = new IntegrityIndexModel
            {
                Checks = checks,
                Error = error
            };
            ViewBag.Title = "سلامت داده";
            ViewBag.Error = error;
            return View(model);
        }

        private ActionResult NotConfigured(string reason)
        {
            Response.StatusCode = 503;
            return View("~/Views/Shared/NotConfigured.cshtml", new ViewDataDictionary { { "Reason", reason } });
        }
    }

    /// <summary>View model for the integrity page.</summary>
    public sealed class IntegrityIndexModel
    {
        public IReadOnlyList<IntegrityCheckRow> Checks { get; set; } = new List<IntegrityCheckRow>();
        public string Error { get; set; }

        public int FailedChecks
        {
            get { return Checks == null ? 0 : Checks.Count(c => !c.Passed); }
        }

        public int OrphanRows
        {
            get { return Checks == null ? 0 : Checks.Sum(c => c.Count); }
        }

        public bool Healthy
        {
            get { return Checks != null && Checks.Count > 0 && FailedChecks == 0; }
        }
    }
}
