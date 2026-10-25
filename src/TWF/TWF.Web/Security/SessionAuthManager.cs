using System;
using System.Collections;
using TWF.Application.Security;
using TWF.Domain.Models;

namespace TWF.Web.Security
{
    /// <summary>
    /// Request-scoped authentication and session identity resolver for TWF.Web.
    /// Caches the resolved <see cref="UserSessionIdentity"/> inside the current HTTP request's Items dictionary
    /// ("TWF.CurrentUser") so multiple calls within the same HTTP request never hit the database twice
    /// and never leak user data across concurrent requests (fixing BUG-01).
    /// </summary>
    public sealed class SessionAuthManager
    {
        public const string RequestContextUserKey = "TWF.CurrentUser";

        private readonly SessionManager _sessionManager;

        public SessionAuthManager(SessionManager sessionManager)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        public SessionCookieDescriptor SignIn(long badgeNumber, string userName, string role, IDictionary requestItems = null, DateTime? utcNow = null)
        {
            SessionCookieDescriptor cookie = _sessionManager.CreateSessionCookie(badgeNumber, userName, role, utcNow);
            if (requestItems != null && _sessionManager.TryResolveSessionIdentity(cookie.Value, utcNow, out UserSessionIdentity identity))
            {
                requestItems[RequestContextUserKey] = identity;
            }
            return cookie;
        }

        public void SignOut(IDictionary requestItems = null)
        {
            if (requestItems != null && requestItems.Contains(RequestContextUserKey))
            {
                requestItems.Remove(RequestContextUserKey);
            }
        }

        public UserSessionIdentity GetCurrentUser(string cookieValue, IDictionary requestItems = null, DateTime? utcNow = null)
        {
            if (requestItems != null && requestItems.Contains(RequestContextUserKey))
            {
                if (requestItems[RequestContextUserKey] is UserSessionIdentity cached)
                    return cached;
            }

            if (!_sessionManager.TryResolveSessionIdentity(cookieValue, utcNow, out UserSessionIdentity resolved))
                return null;

            if (requestItems != null)
            {
                requestItems[RequestContextUserKey] = resolved;
            }

            return resolved;
        }
    }
}
