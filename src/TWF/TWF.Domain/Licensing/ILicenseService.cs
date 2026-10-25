namespace TWF.Domain.Licensing
{
    /// <summary>
    /// Abstraction for license and feature entitlement checks.
    /// Replaces MyTime3.Lock.LockCheker, Interop.TINYLib, and GlobalHelper lock properties.
    /// </summary>
    public interface ILicenseService
    {
        /// <summary>
        /// Gets whether the system license is valid (replaces GlobalHelper.LockIsOK).
        /// </summary>
        bool IsLicenseValid();

        /// <summary>
        /// Gets the maximum allowed personnel count (replaces GlobalHelper.PersonnelCount).
        /// </summary>
        int GetMaxPersonnelCount();

        /// <summary>
        /// Checks whether the current active personnel count is within the licensed limit.
        /// </summary>
        bool IsPersonnelCountWithinLimit(int currentPersonnelCount);

        /// <summary>
        /// Gets whether TimeApp / mobile & advanced attendance features are active
        /// (replaces GlobalHelper.WithTimeApp() and GlobalHelper.TimeApp).
        /// </summary>
        bool IsTimeAppActive();

        /// <summary>
        /// Gets whether full-size custom organization banners are allowed without 150x50 downscaling
        /// (replaces GlobalHelper.FulLogoSize).
        /// </summary>
        bool IsFullSizeLogoAllowed();

        /// <summary>
        /// Returns a full snapshot of the current license status.
        /// </summary>
        LicenseStatus GetStatus();
    }
}
