using System;
using System.Collections;
using TWF.Domain.Licensing;
using TWF.Domain.Models;
using TWF.Infrastructure.Licensing;
using TWF.Web.Security;

namespace TWF.Web.Helpers
{
    /// <summary>
    /// Zero-Lock replacement for legacy MyTime3.GlobalHelper.
    /// Delegates all lock/entitlement checks to <see cref="ILicenseService"/> (defaulting to <see cref="UnrestrictedLicenseService"/>)
    /// and resolves user identity per-request via <see cref="SessionAuthManager"/> without any thread-unsafe static user fields.
    /// </summary>
    public static class GlobalHelper
    {
        private static ILicenseService _licenseService = UnrestrictedLicenseService.Instance;
        private static SessionAuthManager _sessionAuthManager;

        public static void Configure(ILicenseService licenseService, SessionAuthManager sessionAuthManager = null)
        {
            _licenseService = licenseService ?? UnrestrictedLicenseService.Instance;
            _sessionAuthManager = sessionAuthManager;
        }

        /// <summary>
        /// Always returns true under UnrestrictedLicenseService (replaces TINYLib hardware lock check).
        /// </summary>
        public static bool LockIsOK
        {
            get => _licenseService.IsLicenseValid();
            set { /* Ignored in Zero-Lock mode; controlled by ILicenseService */ }
        }

        /// <summary>
        /// Returns int.MaxValue under UnrestrictedLicenseService (removes personnel count cap).
        /// </summary>
        public static int PersonnelCount
        {
            get => _licenseService.GetMaxPersonnelCount();
            set { /* Ignored in Zero-Lock mode; controlled by ILicenseService */ }
        }

        /// <summary>
        /// Always returns true under UnrestrictedLicenseService (removes 150x50 banner resize restriction).
        /// </summary>
        public static bool FulLogoSize
        {
            get => _licenseService.IsFullSizeLogoAllowed();
            set { /* Ignored in Zero-Lock mode; controlled by ILicenseService */ }
        }

        /// <summary>
        /// Always returns "Unlimited" under UnrestrictedLicenseService.
        /// </summary>
        public static string TimeApp
        {
            get => "9999/12/29";
            set { /* Ignored in Zero-Lock mode; controlled by ILicenseService */ }
        }

        /// <summary>
        /// Always returns true under UnrestrictedLicenseService (removes TimeApp date expiration lock).
        /// </summary>
        public static bool WithTimeApp() => _licenseService.IsTimeAppActive();

        public static string GetLoggedInUserName(string cookieValue, IDictionary requestItems = null)
        {
            UserSessionIdentity user = _sessionAuthManager?.GetCurrentUser(cookieValue, requestItems);
            return user?.UserName ?? string.Empty;
        }

        public static long GetLoggedInBadgeNumber(string cookieValue, IDictionary requestItems = null)
        {
            UserSessionIdentity user = _sessionAuthManager?.GetCurrentUser(cookieValue, requestItems);
            return user?.BadgeNumber ?? -1;
        }

        public static int GetLoggedInUserInfoTitleID(string cookieValue, IDictionary requestItems = null)
        {
            UserSessionIdentity user = _sessionAuthManager?.GetCurrentUser(cookieValue, requestItems);
            return user?.TitleId ?? 0;
        }
    }
}
