using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using MyTime3.Authorization;

namespace MyTime3
{
    public class AuthorizeFilter : AuthorizeAttribute
    {
        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            filterContext.Result = new RedirectResult("~/Login/Login");
        }

        public bool CheckAuthorized(HttpContextBase httpContext)
        {
            return AuthorizeCore(httpContext);
        }

        protected override bool AuthorizeCore(HttpContextBase httpContext)
        {
            if (httpContext == null || LoginManager.IsExpired(httpContext))
            {
                return false;
            }

            string currentRole = LoginManager.GetRole(httpContext);
            if (string.IsNullOrWhiteSpace(currentRole))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(base.Roles))
            {
                return true;
            }

            var allowedRoles = base.Roles
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim());

            return allowedRoles.Any(r => string.Equals(r, currentRole, StringComparison.OrdinalIgnoreCase));
        }
    }
}
