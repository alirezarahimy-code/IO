using System;
using System.Collections.Generic;
using System.Web.Mvc;
using T5Web.Domain;
using T5Web.Web.Content;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    /// <summary>
    /// Portal health: database reachability, row counts and which product build is
    /// running. Anonymous-safe? No - configuration details are for PortalAdmin only.
    /// </summary>
    [AdminGate(AdminFeature.PortalAdmin, AllowReadOnly = true)]
    public sealed class DiagnosticsController : PortalController
    {
        public ActionResult Index()
        {
            string error;
            bool reachable = PortalServices.Repository.CanConnect(out error);

            ViewBag.Reachable = reachable;
            ViewBag.Error = error;
            ViewBag.Counts = reachable
                ? PortalServices.Repository.GetTableCounts()
                : new Dictionary<string, int>();
            ViewBag.ProductVersion = AriaAdminBranding.FullVersion + " (" +
                typeof(DiagnosticsController).Assembly.GetName().Version + ")";
            ViewBag.MachineName = Environment.MachineName;
            ViewBag.ServerTime = DateTime.Now;
            ViewBag.EntryAssembly = typeof(DiagnosticsController).Assembly.GetName().Name;
            return View();
        }
    }
}
