using Microsoft.AspNetCore.Mvc;

namespace Aria.Web.Controllers;

// No login form/POST until TWF 4.28's cookie format, expiry, secrets, captcha,
// lockout and employee access have been verified in a disposable environment.
// Never collect passwords through a route that cannot authenticate them.
public sealed class LoginController : Controller
{
    [HttpGet]
    public IActionResult Login()
    {
        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return View();
    }
}
