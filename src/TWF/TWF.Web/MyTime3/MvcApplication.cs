using System.Configuration;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using MyTime3.Controllers;
using MyTime3.Lock;
using MyTime3.Security;
using TD5;

namespace MyTime3
{
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            WebApiConfig.Register(GlobalConfiguration.Configuration);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            CheckLockResult checkLockResult = LockCheker.Check();
            GlobalHelper.LockIsOK = (checkLockResult.LockResult == LockResult.OK);
            GlobalHelper.TimeApp = checkLockResult.TimeApp;
            GlobalHelper.PersonnelCount = checkLockResult.PersonnelCount;
            GlobalHelper.FulLogoSize = checkLockResult.IsLogoFree;

            // Resolved from <connectionStrings> with <appSettings> fallback (legacy EF format supported).
            Init.Start(
                ConnectionStringResolver.PersonnelRequired,
                ConnectionStringResolver.T5Required);

            // Schema contract probe. Never throws: an incompatible or unreachable
            // database is reported through /Diagnostics/Schema instead of a 500 page.
            SchemaGuard.EnsureValidated();
        }
    }
}
