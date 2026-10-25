using System;

namespace TWF.Domain.Models
{
    /// <summary>
    /// Immutable per-request identity of the authenticated TWF user.
    /// Replaces the thread-unsafe static TitleChartClass.user field (BUG-01) and raw cookie strings.
    /// </summary>
    public sealed class UserSessionIdentity
    {
        public long BadgeNumber { get; }
        public string UserName { get; }
        public string FirstName { get; }
        public string LastName { get; }
        public string Role { get; }
        public int TitleId { get; }
        public string TitleName { get; }
        public int UpperTitleId { get; }
        public string UpperTitleName { get; }
        public int DepartmentId { get; }
        public string DepartmentName { get; }
        public bool IsObserver { get; }
        public bool IsTopLevelManager { get; }
        public DateTime IssuedAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }

        public bool IsAdmin =>
            BadgeNumber == 0 ||
            string.Equals(Role, "admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(UserName, "admin", StringComparison.OrdinalIgnoreCase);

        public string FullName
        {
            get
            {
                if (IsAdmin && string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName))
                    return "مدیر سیستم";
                string combined = $"{FirstName} {LastName}".Trim();
                return string.IsNullOrEmpty(combined) ? UserName : combined;
            }
        }

        public UserSessionIdentity(
            long badgeNumber,
            string userName,
            string firstName,
            string lastName,
            string role,
            int titleId,
            string titleName,
            int upperTitleId,
            string upperTitleName,
            int departmentId,
            string departmentName,
            bool isObserver,
            bool isTopLevelManager,
            DateTime issuedAtUtc,
            DateTime expiresAtUtc)
        {
            BadgeNumber = badgeNumber;
            UserName = userName ?? string.Empty;
            FirstName = firstName ?? string.Empty;
            LastName = lastName ?? string.Empty;
            Role = string.IsNullOrWhiteSpace(role) ? "user" : role.Trim();
            TitleId = titleId;
            TitleName = titleName ?? string.Empty;
            UpperTitleId = upperTitleId;
            UpperTitleName = upperTitleName ?? string.Empty;
            DepartmentId = departmentId;
            DepartmentName = departmentName ?? string.Empty;
            IsObserver = isObserver;
            IsTopLevelManager = isTopLevelManager;
            IssuedAtUtc = issuedAtUtc;
            ExpiresAtUtc = expiresAtUtc;
        }
    }

    /// <summary>
    /// Represents the cryptographic payload encoded inside the "mytimeauth" session cookie.
    /// </summary>
    public sealed class SessionTokenPayload
    {
        public long BadgeNumber { get; }
        public string UserName { get; }
        public string Role { get; }
        public DateTime IssuedAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
        public string Nonce { get; }

        public SessionTokenPayload(
            long badgeNumber,
            string userName,
            string role,
            DateTime issuedAtUtc,
            DateTime expiresAtUtc,
            string nonce = null)
        {
            BadgeNumber = badgeNumber;
            UserName = userName ?? string.Empty;
            Role = string.IsNullOrWhiteSpace(role) ? "user" : role.Trim();
            IssuedAtUtc = issuedAtUtc;
            ExpiresAtUtc = expiresAtUtc;
            Nonce = string.IsNullOrWhiteSpace(nonce) ? Guid.NewGuid().ToString("N") : nonce;
        }

        public bool IsExpired(DateTime utcNow)
        {
            return utcNow >= ExpiresAtUtc;
        }
    }
}
