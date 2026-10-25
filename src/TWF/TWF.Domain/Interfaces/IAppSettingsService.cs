using System.Collections.Generic;
using TWF.Domain.Models;

namespace TWF.Domain.Interfaces
{
    /// <summary>
    /// Thread-safe service interface for reading and persisting TWF application settings.
    /// </summary>
    public interface IAppSettingsService
    {
        /// <summary>
        /// Gets an immutable snapshot of the current TWF application settings.
        /// </summary>
        TwfAppSettings GetSettings();

        /// <summary>
        /// Reloads settings from the underlying Web.config / configuration store.
        /// </summary>
        TwfAppSettings ReloadSettings();

        /// <summary>
        /// Persists updated settings to the configuration store and refreshes the in-memory cache.
        /// </summary>
        void SaveSettings(TwfAppSettings updatedSettings);

        /// <summary>
        /// Reads a raw key-value dictionary of all settings.
        /// </summary>
        IDictionary<string, string> GetRawSettingsDictionary();
    }
}
