using System.Configuration;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using MyTime3.Lock;
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

            Init.Start(
                ConfigurationManager.AppSettings["PersonnelCnnStr"],
                ConfigurationManager.AppSettings["T5CnnStr"]);
        }
    }
}
