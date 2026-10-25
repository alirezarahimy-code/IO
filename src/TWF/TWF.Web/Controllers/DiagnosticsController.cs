using System.Web.Mvc;
using MyTime3.Security;
using TWF.Infrastructure.Localization;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Self-diagnosis endpoints for the Aria portal.
    ///
    /// These actions must stay reachable even when the database is unavailable or the
    /// schema is incompatible — otherwise an operator sees only a 500 page and cannot
    /// tell what to fix.
    /// </summary>
    [AllowAnonymous]
    public class DiagnosticsController : Controller
    {
        /// <summary>
        /// Shows the schema contract verdict plus, when available, the exact list of
        /// missing tables/columns and the remediation commands.
        /// </summary>
        [HttpGet]
        public ActionResult Schema()
        {
            SchemaCheckResult result = SchemaGuard.LastResult
                ?? SchemaGuard.EnsureValidated();

            ViewBag.ProductName = PersianString.AppName;
            ViewBag.IsCompatible = result.IsCompatible;
            ViewBag.WasChecked = result.WasChecked;
            ViewBag.DatabaseReachable = result.DatabaseReachable;
            ViewBag.Message = result.Message;
            ViewBag.Problems = result.Problems;
            ViewBag.CheckedAt = result.CheckedAt.ToString("yyyy/MM/dd HH:mm:ss");
            ViewBag.ContractTableCount = SchemaGuard.Contract.Count;

            return View("~/Views/Diagnostics/Schema.cshtml", result);
        }

        /// <summary>Forces a fresh schema probe (used after an operator fixes the database).</summary>
        [HttpGet]
        public ActionResult Recheck()
        {
            SchemaGuard.EnsureValidated(force: true);
            return RedirectToAction("Schema");
        }

        /// <summary>Lightweight liveness endpoint for the deployment/monitoring script.</summary>
        [HttpGet]
        public JsonResult Health()
        {
            SchemaCheckResult result = SchemaGuard.LastResult;
            return Json(
                new
                {
                    status = result == null ? "unknown" : (result.IsCompatible ? "ok" : "degraded"),
                    schemaCompatible = result != null && result.IsCompatible,
                    databaseReachable = result != null && result.DatabaseReachable,
                    checkedAt = result != null ? result.CheckedAt.ToString("s") : null,
                    contractTables = SchemaGuard.Contract.Count
                },
                JsonRequestBehavior.AllowGet);
        }
    }
}
