using System;
using System.Collections;
using TWF.Domain.Models;

namespace TWF.Web.Security
{
    /// <summary>
    /// Represents the outcome of an authorization check on a controller/action.
    /// Replaces MyTime3.AuthorizeFilter with explicit support for AJAX 401 responses,
    /// Admin-only endpoints, Observer (Guard) endpoints, and Zero-Lock operation.
    /// </summary>
    public sealed class AuthorizationDecision
    {
        public bool IsAllowed { get; }
        public int HttpStatusCode { get; }
        public string RedirectUrl { get; }
        public string DenialReason { get; }
        public UserSessionIdentity User { get; }

        private AuthorizationDecision(
            bool isAllowed,
            int httpStatusCode,
            string redirectUrl,
            string denialReason,
            UserSessionIdentity user)
        {
            IsAllowed = isAllowed;
            HttpStatusCode = httpStatusCode;
            RedirectUrl = redirectUrl ?? string.Empty;
            DenialReason = denialReason ?? string.Empty;
            User = user;
        }

        public static AuthorizationDecision Allow(UserSessionIdentity user)
        {
            return new AuthorizationDecision(true, 200, string.Empty, string.Empty, user);
        }

        public static AuthorizationDecision DenyUnauthenticated(bool isAjaxRequest, string loginUrl = "/Login/Login")
        {
            return new AuthorizationDecision(
                isAllowed: false,
                httpStatusCode: isAjaxRequest ? 401 : 302,
                redirectUrl: isAjaxRequest ? string.Empty : loginUrl,
                denialReason: "نشست کاربری معتبر نیست یا منقضی شده است.",
                user: null);
        }

        public static AuthorizationDecision DenyForbidden(UserSessionIdentity user, string reason = "دسترسی ندارید")
        {
            return new AuthorizationDecision(
                isAllowed: false,
                httpStatusCode: 403,
                redirectUrl: "/Home/Index",
                denialReason: reason,
                user: user);
        }
    }

    /// <summary>
    /// Policy evaluator for TWF routes and controllers.
    /// Replaces MyTime3.AuthorizeFilter (which lacked role granularity and returned HTML login pages to AJAX requests).
    /// </summary>
    public sealed class TwfAuthorizationEvaluator
    {
        private readonly SessionAuthManager _authManager;

        public TwfAuthorizationEvaluator(SessionAuthManager authManager)
        {
            _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        }

        public AuthorizationDecision Evaluate(
            string cookieValue,
            bool allowAnonymous = false,
            bool requireAdmin = false,
            bool requireObserver = false,
            bool isAjaxRequest = false,
            IDictionary requestItems = null,
            DateTime? utcNow = null)
        {
            if (allowAnonymous)
            {
                UserSessionIdentity optionalUser = _authManager.GetCurrentUser(cookieValue, requestItems, utcNow);
                return AuthorizationDecision.Allow(optionalUser);
            }

            UserSessionIdentity user = _authManager.GetCurrentUser(cookieValue, requestItems, utcNow);
            if (user == null)
            {
                return AuthorizationDecision.DenyUnauthenticated(isAjaxRequest);
            }

            if (requireAdmin && !user.IsAdmin && !user.IsTopLevelManager)
            {
                return AuthorizationDecision.DenyForbidden(user, "این بخش صرفاً ویژه مدیر سیستم است.");
            }

            if (requireObserver && !user.IsObserver && !user.IsAdmin)
            {
                return AuthorizationDecision.DenyForbidden(user, "این بخش صرفاً ویژه نگهبانی / ناظر تردد است.");
            }

            return AuthorizationDecision.Allow(user);
        }
    }
}
