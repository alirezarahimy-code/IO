using Aria.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace Aria.Web.Controllers;

// Home/Index in TWF is an operational dashboard backed by the x86 engine,
// Personnel/T5 tables, leave calculations and request-specific permissions.
// Do not turn an invented dashboard into an HTTP 200 while those are absent.
public sealed class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index(int sort = -1, bool g = true)
    {
        if (AriaRequestIdentity.Get(HttpContext) is null)
            return StatusCode(StatusCodes.Status403Forbidden);
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            "داشبورد آریا تا تأیید نشست، موتور محاسباتی و داده در دسترس نیست.");
    }
}
