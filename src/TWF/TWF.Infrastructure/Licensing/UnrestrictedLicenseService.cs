using TWF.Domain.Licensing;

namespace TWF.Infrastructure.Licensing
{
    /// <summary>
    /// Zero-Lock implementation of <see cref="ILicenseService"/>.
    /// Completely removes all dependencies on TINYLib (Tiny.ocx 32-bit COM dongle),
    /// personnel count limits, TimeApp date expirations, and banner size restrictions.
    /// When the management team decides on a future software/hardware licensing mechanism,
    /// a new implementation of <see cref="ILicenseService"/> can be registered in one place.
    /// </summary>
    public sealed class UnrestrictedLicenseService : ILicenseService
    {
        public static readonly UnrestrictedLicenseService Instance = new UnrestrictedLicenseService();

        public bool IsLicenseValid() => true;

        public int GetMaxPersonnelCount() => int.MaxValue;

        public bool IsPersonnelCountWithinLimit(int currentPersonnelCount) => true;

        public bool IsTimeAppActive() => true;

        public bool IsFullSizeLogoAllowed() => true;

        public LicenseStatus GetStatus() => LicenseStatus.CreateUnrestricted();
    }
}
