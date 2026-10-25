using TWF.Domain.Licensing;
using TWF.Infrastructure.Licensing;

namespace MyTime3.Lock
{
    /// <summary>
    /// Rewritten Zero-Lock checker (Phase 3).
    /// Completely removes TINYLib ActiveX/COM hardware dongle dependencies (CLSID 0CA5A93D-F6BD-4398-809A-9F767276EF77)
    /// and delegates to <see cref="ILicenseService"/> (<see cref="UnrestrictedLicenseService"/>).
    /// </summary>
    public static class LockCheker
    {
        public static ILicenseService LicenseService { get; set; } = new UnrestrictedLicenseService();

        public static CheckLockResult Check()
        {
            LicenseStatus status = LicenseService.GetStatus();
            return new CheckLockResult
            {
                LockResult = status.IsValid ? LockResult.OK : LockResult.NotFound,
                PersonnelCount = status.MaxPersonnelCount,
                UserName = status.CustomerTitle,
                IsLogoFree = status.IsFullSizeLogoAllowed,
                TimeApp = string.Empty
            };
        }
    }
}
