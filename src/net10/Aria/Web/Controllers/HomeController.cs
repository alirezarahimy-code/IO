using Aria.Web.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace Aria.Web.Controllers;

// Home/Index in TWF is an operational dashboard backed by the x86 engine,
// Personnel/T5 tables, leave calculations and request-specific permissions.
// Do not turn an invented dashboard into an HTTP 200 while those are absent.
public sealed class HomeController : Controller
{
    private readonly IWebHostEnvironment _host;

    public HomeController(IWebHostEnvironment host) =>
        _host = host ?? throw new ArgumentNullException(nameof(host));

    // Only original public MyBanner(2) / MyBanner(3) vendor images have an
    // executable original-pixel golden. NEVER dereference case 1's customer
    // LogoPath, nor pass caller-controlled filenames to the filesystem.
    [HttpGet]
    public IActionResult MyBanner(int index)
    {
        string? file = index switch
        {
            2 => "aria-about-arm.png",
            3 => "aria-about-logo.png",
            _ => null
        };
        return file is null ? NotFound() :
            PhysicalFile(Path.Combine(_host.WebRootPath, "assets", file), "image/png");
    }

    // Original TWF 4.28 About is a public action with no method-level
    // authorization. Only its DB-independent text is ported; new UI intentionally
    // avoids the original shared layout's DB calls and historical version banner.
    [HttpGet]
    public IActionResult About() => View();

    [HttpGet]
    public IActionResult Index(int sort = -1, bool g = true)
    {
        if (AriaRequestIdentity.Get(HttpContext) is null)
            return StatusCode(StatusCodes.Status403Forbidden);
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            "داشبورد آریا تا تأیید نشست، موتور محاسباتی و داده در دسترس نیست.");
    }
}
