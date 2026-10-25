using System.Web.Mvc;
using T5Web.Domain;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    [AdminGate(AdminFeature.LogView, AllowReadOnly = true)]
    public sealed class AuditController : PortalController
    {
        public ActionResult Index(int take = 100)
        {
            var entries = PortalServices.Audit.Recent(take);
            ViewBag.BrokenIndex = PortalServices.Audit.VerifyChain(entries);
            ViewBag.Take = take;
            return View(entries);
        }
    }
}
