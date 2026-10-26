using AriaAdmin.Web.Security;
using Microsoft.AspNetCore.Mvc;
using T5Web.Domain;

namespace AriaAdmin.Web.Controllers;

public sealed class DashboardController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        var session = PortalSession.Get(HttpContext);
        if (session?.LevelOf(AdminFeature.PersonnelView) is not (AccessLevel.ReadOnly or AccessLevel.Allow))
            return StatusCode(StatusCodes.Status403Forbidden);
        // Returning a fabricated 200 dashboard would conceal unported count/device,
        // audit and permission queries. The original screen is still TODO.
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            "داشبورد تا تأیید اتصال داده، مجوزها و گزارش‌ها در دسترس نیست.");
    }
}
