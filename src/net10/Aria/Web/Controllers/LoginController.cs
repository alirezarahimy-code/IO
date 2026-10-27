using Microsoft.AspNetCore.Mvc;

namespace Aria.Web.Controllers;

// No login/password-change POST until the same-account credential verifier,
// manager/Personnel policy, fresh tamper-evident session, antiforgery and
// original TWF 4.28 login/route golden have been approved and tested.
// The original DES cookie has no MAC and must NOT be accepted on cut-over.
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
