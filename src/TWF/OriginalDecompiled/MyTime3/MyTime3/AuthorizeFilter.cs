using MyTime3.Authorization;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace MyTime3
{
	public class AuthorizeFilter : AuthorizeAttribute
	{
		protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
		{
			filterContext.Result = new RedirectResult("~/Login/Login");
		}

		protected override bool AuthorizeCore(HttpContextBase httpContext)
		{
			bool num = base.Roles.Split(new char[1]
			{
				','
			}).ToList().IndexOf(LoginManager.GetRole(httpContext)) >= 0;
			bool flag = !LoginManager.IsExpired(httpContext);
			return num & flag;
		}
	}
}
