using System.Web.Mvc;

namespace T5Web.Web.Controllers
{
    /// <summary>
    /// No sign-in form: authentication is Windows/Negotiate, exactly like the
    /// original T5V3Web deployment. This controller only explains a refusal.
    /// </summary>
    public sealed class AccountController : Controller
    {
        [AllowAnonymous]
        public ActionResult NoAccess()
        {
            ViewBag.Reason = ViewData["Reason"] as string;
            Response.StatusCode = Response.StatusCode == 200 ? 403 : Response.StatusCode;
            return View();
        }
    }
}
