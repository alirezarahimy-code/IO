using System;

namespace TWF.Domain.Licensing
{
    /// <summary>
    /// Represents the current licensing and feature-entitlement status of the TWF system.
    /// Designed to decouple the application from hardware locks (TINYLib / Tiny.ocx)
    /// so licensing policy can be swapped via Dependency Injection at any time.
    /// </summary>
    public sealed class LicenseStatus
    {
        public bool IsValid { get; }
        public string Mode { get; }
        public int MaxPersonnelCount { get; }
        public bool IsMobileTimeAppAllowed { get; }
        public bool IsFullSizeLogoAllowed { get; }
        public string CustomerTitle { get; }
        public DateTime EvaluatedAtUtc { get; }

        public LicenseStatus(
            bool isValid,
            string mode,
            int maxPersonnelCount,
            bool isMobileTimeAppAllowed,
            bool isFullSizeLogoAllowed,
            string customerTitle,
            DateTime evaluatedAtUtc)
        {
            IsValid = isValid;
            Mode = mode ?? "Unrestricted";
            MaxPersonnelCount = maxPersonnelCount;
            IsMobileTimeAppAllowed = isMobileTimeAppAllowed;
            IsFullSizeLogoAllowed = isFullSizeLogoAllowed;
            CustomerTitle = customerTitle ?? string.Empty;
            EvaluatedAtUtc = evaluatedAtUtc;
        }

        public static LicenseStatus CreateUnrestricted()
        {
            return new LicenseStatus(
                isValid: true,
                mode: "ZeroLock-Unrestricted",
                maxPersonnelCount: int.MaxValue,
                isMobileTimeAppAllowed: true,
                isFullSizeLogoAllowed: true,
                customerTitle: "Unrestricted Enterprise Edition",
                evaluatedAtUtc: DateTime.UtcNow);
        }
    }
}
