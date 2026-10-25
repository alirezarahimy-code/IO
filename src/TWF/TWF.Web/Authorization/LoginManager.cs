using System;
using System.Globalization;
using System.Web;
using MyTime3.Controllers;

namespace MyTime3.Authorization
{
    /// <summary>
    /// Rewritten LoginManager for MyTime3.Authorization (fixing BUG-02).
    /// - Fixes CookiesSecure parsing (uses typeof(bool) instead of typeof(int)).
    /// - Fixes IsExpired check to honor CookiesTime minutes from Web.config instead of hardcoded 8 hours.
    /// - Uses AES-256-CBC + HMAC-SHA256 via <see cref="Encryption"/>.
    /// </summary>
    public static class LoginManager
    {
        public const string AuthCookieName = "mytimeauth";

        public static void Login(long badgeNumber, string role, HttpContextBase httpContext)
        {
            if (httpContext == null)
                return;

            int cookiesTimeMinutes = (int)SettingsController.LoadValue("CookiesTime", typeof(int), 20);
            if (cookiesTimeMinutes <= 0)
            {
                cookiesTimeMinutes = 20;
            }

            // Fixed BUG-02: Legacy code passed typeof(int) instead of typeof(bool) for CookiesSecure!
            bool cookiesSecure = (bool)SettingsController.LoadValue("CookiesSecure", typeof(bool), false);

            DateTime now = DateTime.Now;
            string payload = string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1};{2}",
                now.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
                badgeNumber.ToString(CultureInfo.InvariantCulture),
                role ?? "user");

            var cookie = new HttpCookie(AuthCookieName, Encryption.Encrypt(payload))
            {
                Expires = now.AddMinutes(cookiesTimeMinutes),
                SameSite = SameSiteMode.Strict,
                Secure = cookiesSecure,
                HttpOnly = true,
                Path = "/"
            };

            httpContext.Response.AppendCookie(cookie);
            httpContext.Request.Cookies.Set(cookie);
        }

        public static void Logout(HttpContextBase httpContext)
        {
            if (httpContext == null)
                return;

            var expiredCookie = new HttpCookie(AuthCookieName, string.Empty)
            {
                Expires = DateTime.Now.AddDays(-1.0),
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Path = "/"
            };

            httpContext.Response.AppendCookie(expiredCookie);
            httpContext.Request.Cookies.Remove(AuthCookieName);
        }

        public static string GetRole(HttpContextBase httpContext)
        {
            if (httpContext == null)
                return string.Empty;

            try
            {
                HttpCookie cookie = httpContext.Request?.Cookies?.Get(AuthCookieName);
                if (cookie == null || string.IsNullOrWhiteSpace(cookie.Value))
                    return string.Empty;

                string decrypted = Encryption.Decrypt(cookie.Value);
                if (string.IsNullOrWhiteSpace(decrypted))
                    return string.Empty;

                string[] parts = decrypted.Split(';');
                return parts.Length >= 3 ? parts[2] : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool IsExpired(HttpContextBase httpContext)
        {
            if (httpContext == null)
                return true;

            try
            {
                HttpCookie cookie = httpContext.Request?.Cookies?.Get(AuthCookieName);
                if (cookie == null || string.IsNullOrWhiteSpace(cookie.Value))
                    return true;

                string decrypted = Encryption.Decrypt(cookie.Value);
                if (string.IsNullOrWhiteSpace(decrypted))
                    return true;

                string[] parts = decrypted.Split(';');
                if (parts.Length < 3)
                    return true;

                if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime issuedAt) &&
                    !DateTime.TryParse(parts[0], out issuedAt))
                {
                    return true;
                }

                // Fixed BUG-02: Honor CookiesTime (minutes) from Web.config instead of hardcoded -8 hours
                int cookiesTimeMinutes = (int)SettingsController.LoadValue("CookiesTime", typeof(int), 20);
                if (cookiesTimeMinutes <= 0)
                {
                    cookiesTimeMinutes = 20;
                }

                return DateTime.Now > issuedAt.AddMinutes(cookiesTimeMinutes);
            }
            catch
            {
                return true;
            }
        }

        public static string GetBadgeNumber(HttpContextBase httpContext)
        {
            if (httpContext == null)
                return string.Empty;

            try
            {
                HttpCookie cookie = httpContext.Request?.Cookies?.Get(AuthCookieName);
                if (cookie == null || string.IsNullOrWhiteSpace(cookie.Value))
                    return string.Empty;

                if (IsExpired(httpContext))
                    return string.Empty;

                string decrypted = Encryption.Decrypt(cookie.Value);
                if (string.IsNullOrWhiteSpace(decrypted))
                    return string.Empty;

                string[] parts = decrypted.Split(';');
                return parts.Length >= 2 ? parts[1] : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
