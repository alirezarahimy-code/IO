using Microsoft.AspNetCore.Mvc;

namespace Aria.Web.Controllers;

// Original TWF rewrite exposes anonymous schema/health actions; this scaffold
// deliberately discloses no connection, table, column, machine or schema data.
public sealed class DiagnosticsController : Controller
{
    [HttpGet]
    public IActionResult Schema()
    {
        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return View();
    }

    [HttpGet]
    public IActionResult Health() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { status = "unavailable", databaseReachable = (bool?)null, schemaCompatible = (bool?)null });
}
