using Microsoft.AspNetCore.Mvc;

namespace AriaAdmin.Web.Controllers;

public sealed class AccountController : Controller
{
    // An explanatory 403 page, never a password form or an unauthenticated portal.
    [HttpGet]
    public IActionResult NoAccess()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }
}
