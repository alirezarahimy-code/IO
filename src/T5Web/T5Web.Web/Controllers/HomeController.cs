using System.Web.Mvc;

namespace T5Web.Web.Controllers
{
    /// <summary>The portal has no marketing page; the root is the dashboard.</summary>
    public sealed class HomeController : Controller
    {
        public ActionResult Index()
        {
            return RedirectToAction("Index", "Dashboard");
        }
    }
}
