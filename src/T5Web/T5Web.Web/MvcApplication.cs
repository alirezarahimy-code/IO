using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Web.Mvc;
using System.Web.Routing;
using T5Web.Data;
using T5Web.Data.Engine;
using T5Web.Web.Security;

namespace T5Web.Web
{
    /// <summary>
    /// Portal start-up.
    ///
    /// Two things happen here and nowhere else:
    ///   1. the connection to the shared T5 database is built once, from configuration;
    ///   2. the repository the whole portal depends on is created.
    ///
    /// The repository is handed the engine directory (Personnel.DB.*DA from the pinned
    /// build) as its source of organisational master data. If those assemblies are
    /// missing the portal still starts: the directory is simply absent and reads fall
    /// back to the SQL path that db/schema-manifest.json verified. Losing the engine is
    /// a degraded mode, not an outage - but it is never silent, PortalServices exposes
    /// the mode and the diagnostics page prints it.
    ///
    /// There is no licence check, no machine binding and no outbound call: the portal
    /// runs on an isolated network, and a start-up that phones home would hang there.
    /// </summary>
    public class MvcApplication : System.Web.HttpApplication
    {
        public static string StartupError { get; private set; } = string.Empty;

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);

            try
            {
                string connectionString = BuildConnectionString();
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    StartupError = "رشته اتصال پایگاه‌داده تنظیم نشده است (appSettings: T5CnnStr).";
                    return;
                }

                var engineDirectory = new PersonnelEngineDirectory(() => connectionString);
                PortalServices.Configure(
                    new SqlAdminRepository(() => new SqlConnection(connectionString), engineDirectory),
                    new SqlModuleStore(() => new SqlConnection(connectionString)));
            }
            catch (Exception ex)
            {
                // A configuration mistake must produce a readable page, not a yellow screen.
                StartupError = "راه‌اندازی پورتال ناموفق بود: " + ex.Message;
            }
        }

        /// <summary>
        /// Accepts a plain ADO connection string, and also the legacy EF-format string
        /// the customer's Web.config already contains
        /// ("metadata=...;provider=System.Data.SqlClient;provider connection string=...").
        /// Reusing the existing value means a migration needs no new secret on the server.
        /// </summary>
        public static string BuildConnectionString()
        {
            string raw = ConfigurationManager.AppSettings["T5CnnStr"];
            if (string.IsNullOrWhiteSpace(raw))
            {
                var configured = ConfigurationManager.ConnectionStrings["T5CnnStr"];
                raw = configured == null ? string.Empty : configured.ConnectionString;
            }
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            const string marker = "provider connection string=";
            int index = raw.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return raw;
            }

            // Everything after the marker *is* the ADO connection string. It contains
            // its own ';' separators, so it must be taken verbatim (only de-quoted);
            // splitting on the first ';' would silently truncate the catalog.
            return raw.Substring(index + marker.Length).Trim().Trim('"', '\'').Trim();
        }
    }

    public static class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            if (filters == null)
            {
                return;
            }
            // Runs before authorization: a portal that cannot read its configuration
            // must explain that, not pretend the user lacks permission.
            filters.Add(new BootstrapFilter());
            filters.Add(new HandleErrorAttribute());
        }
    }

    public static class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            if (routes == null)
            {
                return;
            }
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");
            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Dashboard", action = "Index", id = UrlParameter.Optional });
        }
    }
}
