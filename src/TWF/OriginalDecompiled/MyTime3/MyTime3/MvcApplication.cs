using MyTime3.Lock;
using System.Configuration;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using TD5;

namespace MyTime3
{
	public class MvcApplication
	{
		protected void Application_Start()
		{
			//IL_0019: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected O, but got Unknown
			AreaRegistration.RegisterAllAreas();
			WebApiConfig.Register(GlobalConfiguration.Configuration);
			FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
			RouteConfig.RegisterRoutes(RouteTable.get_Routes());
			BundleConfig.RegisterBundles(BundleTable.Bundles);
			CheckLockResult checkLockResult = LockCheker.Check();
			GlobalHelper.LockIsOK = (checkLockResult.LockResult == LockResult.OK);
			GlobalHelper.TimeApp = checkLockResult.TimeApp;
			GlobalHelper.PersonnelCount = checkLockResult.PersonnelCount;
			GlobalHelper.FulLogoSize = checkLockResult.IsLogoFree;
			Init.Start(ConfigurationManager.get_AppSettings()["PersonnelCnnStr"], ConfigurationManager.get_AppSettings()["T5CnnStr"]);
		}

		public MvcApplication()
			: this()
		{
		}
	}
}
