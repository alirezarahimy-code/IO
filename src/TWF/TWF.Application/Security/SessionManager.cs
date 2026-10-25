using System;
using TWF.Domain.Interfaces;
using TWF.Domain.Models;

namespace TWF.Application.Security
{
    /// <summary>
    /// Describes the HTTP cookie attributes for the TWF authentication session cookie ("mytimeauth").
    /// </summary>
    public sealed class SessionCookieDescriptor
    {
        public string CookieName { get; }
        public string Value { get; }
        public bool HttpOnly { get; }
        public bool Secure { get; }
        public string SameSite { get; }
        public DateTime IssuedAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
        public SessionTokenPayload Payload { get; }

        public SessionCookieDescriptor(
            string cookieName,
            string value,
            bool httpOnly,
            bool secure,
            string sameSite,
            DateTime issuedAtUtc,
            DateTime expiresAtUtc,
            SessionTokenPayload payload)
        {
            CookieName = cookieName ?? "mytimeauth";
            Value = value ?? string.Empty;
            HttpOnly = httpOnly;
            Secure = secure;
            SameSite = sameSite ?? "Lax";
            IssuedAtUtc = issuedAtUtc;
            ExpiresAtUtc = expiresAtUtc;
            Payload = payload;
        }
    }

    /// <summary>
    /// Application service managing session token creation, validation, and per-request UserSessionIdentity resolution.
    /// Replaces MyTime3.Authorization.LoginManager and fixes BUG-01 (thread-unsafe static user) and BUG-02 (cookie flags & expiration).
    /// </summary>
    public sealed class SessionManager
    {
        public const string DefaultCookieName = "mytimeauth";

        private readonly IAuthTokenService _tokenService;
        private readonly IAppSettingsService _settingsService;
        private readonly ITwfDataRepository _dataRepository;

        public SessionManager(
            IAuthTokenService tokenService,
            IAppSettingsService settingsService,
            ITwfDataRepository dataRepository)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _dataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));
        }

        /// <summary>
        /// Creates an encrypted, HMAC-signed session cookie descriptor honoring CookiesSecure and CookiesTime from Web.config.
        /// Fixes BUG-02:
        /// 1. CookiesSecure is properly read as bool (legacy code passed typeof(int), causing an exception and forcing Secure = false).
        /// 2. Session lifetime honors CookiesTime (legacy IsExpired hardcoded AddHours(-8.0) regardless of CookiesTime).
        /// 3. HttpOnly is enforced as true (legacy code left HttpOnly false, exposing the session cookie to XSS).
        /// </summary>
        public SessionCookieDescriptor CreateSessionCookie(
            long badgeNumber,
            string userName,
            string role,
            DateTime? utcNow = null)
        {
            TwfAppSettings settings = _settingsService.GetSettings();
            DateTime issuedUtc = utcNow ?? DateTime.UtcNow;
            TimeSpan lifetime = settings.GetSessionLifetime();
            DateTime expiresUtc = issuedUtc.Add(lifetime);

            var payload = new SessionTokenPayload(
                badgeNumber: badgeNumber,
                userName: userName,
                role: role,
                issuedAtUtc: issuedUtc,
                expiresAtUtc: expiresUtc);

            string token = _tokenService.ProtectToken(payload);

            return new SessionCookieDescriptor(
                cookieName: DefaultCookieName,
                value: token,
                httpOnly: true,
                secure: settings.CookiesSecure,
                sameSite: "Lax",
                issuedAtUtc: issuedUtc,
                expiresAtUtc: expiresUtc,
                payload: payload);
        }

        /// <summary>
        /// Validates the encrypted cookie token and resolves a fresh, immutable <see cref="UserSessionIdentity"/>
        /// for the current request without touching any static fields (fixes BUG-01).
        /// </summary>
        public bool TryResolveSessionIdentity(
            string cookieValue,
            DateTime? utcNow,
            out UserSessionIdentity identity)
        {
            identity = null;
            if (string.IsNullOrWhiteSpace(cookieValue))
                return false;

            DateTime effectiveNow = utcNow ?? DateTime.UtcNow;
            if (!_tokenService.TryUnprotectToken(cookieValue, effectiveNow, out SessionTokenPayload payload))
                return false;

            UserTitleProfileDto profile = _dataRepository.GetUserTitleProfile(payload.BadgeNumber);
            if (profile == null)
            {
                // Fallback if user has no TitlesChart row yet
                identity = new UserSessionIdentity(
                    badgeNumber: payload.BadgeNumber,
                    userName: payload.UserName,
                    firstName: string.Empty,
                    lastName: string.Empty,
                    role: payload.Role,
                    titleId: 0,
                    titleName: string.Empty,
                    upperTitleId: 0,
                    upperTitleName: string.Empty,
                    departmentId: 0,
                    departmentName: string.Empty,
                    isObserver: false,
                    isTopLevelManager: payload.BadgeNumber == 0,
                    issuedAtUtc: payload.IssuedAtUtc,
                    expiresAtUtc: payload.ExpiresAtUtc);
                return true;
            }

            bool isTopLevel = payload.BadgeNumber == 0 ||
                              (profile.TitleId > 0 && (profile.UpperTitleId <= 0 || profile.UpperTitleId == profile.TitleId));

            identity = new UserSessionIdentity(
                badgeNumber: profile.BadgeNumber,
                userName: !string.IsNullOrWhiteSpace(profile.UserName) ? profile.UserName : payload.UserName,
                firstName: profile.FirstName,
                lastName: profile.LastName,
                role: payload.Role,
                titleId: profile.TitleId,
                titleName: profile.TitleName,
                upperTitleId: profile.UpperTitleId,
                upperTitleName: profile.UpperTitleName,
                departmentId: profile.DepartmentId,
                departmentName: profile.DepartmentName,
                isObserver: profile.IsObserver,
                isTopLevelManager: isTopLevel,
                issuedAtUtc: payload.IssuedAtUtc,
                expiresAtUtc: payload.ExpiresAtUtc);

            return true;
        }
    }
}
