using Microsoft.AspNetCore.Mvc;

namespace AriaAdmin.Web.Controllers;

// Same root-to-dashboard redirect as the current T5Web HomeController; the
// dashboard itself remains unavailable until a proved SQL/access adapter exists.
public sealed class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index() => RedirectToAction("Index", "Dashboard");
}
