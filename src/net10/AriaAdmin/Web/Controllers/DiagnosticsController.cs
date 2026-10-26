using AriaAdmin.Web.Security;
using Microsoft.AspNetCore.Mvc;
using T5Web.Domain;

namespace AriaAdmin.Web.Controllers;

// Current MVC5 diagnostics require PortalAdmin. Do not disclose a machine name,
// connection string, database counts or stack trace while the adapter is absent.
public sealed class DiagnosticsController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        var session = PortalSession.Get(HttpContext);
        if (session?.LevelOf(AdminFeature.PortalAdmin) != AccessLevel.Allow)
            return StatusCode(StatusCodes.Status403Forbidden);
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            "تشخیص پایگاه‌داده تا تکمیل سازگارکنندهٔ ایمن در دسترس نیست.");
    }
}
