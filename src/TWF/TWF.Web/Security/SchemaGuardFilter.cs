using System.Web.Mvc;
using System.Web.Routing;

namespace MyTime3.Security
{
    /// <summary>
    /// When the schema contract is violated, every request is redirected to a single
    /// Persian diagnostics page instead of surfacing a raw SQL exception inside a
    /// half-rendered screen (the legacy behaviour during a shift).
    ///
    /// The filter is deliberately passive:
    ///   * it does nothing when the guard was skipped (tests / in-memory mode);
    ///   * it does nothing when the database is compatible;
    ///   * it never blocks the diagnostics page itself (no redirect loop);
    ///   * it never blocks static content.
    /// </summary>
    public sealed class SchemaGuardFilter : ActionFilterAttribute
    {
        /// <summary>Controller that owns the diagnostics page.</summary>
        public const string DiagnosticsControllerName = "Diagnostics";

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            base.OnActionExecuting(filterContext);

            SchemaCheckResult result = SchemaGuard.LastResult;
            if (result == null || !result.WasChecked || result.IsCompatible)
            {
                return;
            }

            if (filterContext == null || filterContext.HttpContext == null)
            {
                return;
            }

            RouteData route = filterContext.RouteData;
            string controller = route != null && route.Values.ContainsKey("controller")
                ? (route.Values["controller"] as string ?? string.Empty)
                : string.Empty;

            if (string.Equals(controller, DiagnosticsControllerName, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(controller, "Static", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            filterContext.Result = new RedirectToRouteResult(
                new RouteValueDictionary
                {
                    { "controller", DiagnosticsControllerName },
                    { "action", "Schema" }
                });
        }
    }
}
