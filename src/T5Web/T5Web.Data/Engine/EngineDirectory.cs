using System;
using System.Data;
using System.Data.SqlClient;

namespace T5Web.Data.Engine
{
    /// <summary>
    /// Chooses which reader answers <see cref="IEngineDirectory"/>.
    ///
    /// This class is the whole of the portal's .NET Framework coupling, in one place, so
    /// that removing it is a one-line change and not an archaeology exercise (docs/16):
    ///
    ///   T5WebEngineDirectory = sql      the default: Dapper, four SELECTs, any platform
    ///                          engine   the pinned engine's EF6 DA layer (x86, Framework)
    ///                          auto     engine if the assembly loads, otherwise sql
    ///
    /// A typo is not "auto": an unreadable value falls back to `sql` and says nothing,
    /// because `auto` would hide a configuration mistake behind a working screen.
    /// </summary>
    public static class EngineDirectory
    {
        public const string Key = "T5WebEngineDirectory";

        public static IEngineDirectory Create(string mode, Func<string> personnelConnectionString)
        {
            if (personnelConnectionString == null)
            {
                throw new ArgumentNullException(nameof(personnelConnectionString));
            }

            string value = (mode ?? string.Empty).Trim().ToLowerInvariant();
            switch (value)
            {
                case "engine":
                    return new PersonnelEngineDirectory(personnelConnectionString);
                case "auto":
                    // Probing by type load, not by file existence: a 64-bit app pool can see
                    // Personnel.dll on disk and still be unable to load it, and that failure
                    // belongs to the engine path, not to this switch.
                    return CanLoadEngine()
                        ? (IEngineDirectory)new PersonnelEngineDirectory(personnelConnectionString)
                        : new SqlEngineDirectory(() => new SqlConnection(personnelConnectionString()));
                default:
                    return new SqlEngineDirectory(() => new SqlConnection(personnelConnectionString()));
            }
        }

        /// <summary>
        /// True when the engine's EF6 assembly is loadable in this process. Used only by
        /// `auto`; every other mode is explicit.
        /// </summary>
        public static bool CanLoadEngine()
        {
            try
            {
                Type probe = Type.GetType("Personnel.PersonnelSystem, Personnel", throwOnError: false);
                return probe != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Which reader would be used, for the startup banner and the audit line. Reading
        /// this must not open a connection.
        /// </summary>
        public static string Describe(string mode)
        {
            string value = (mode ?? string.Empty).Trim().ToLowerInvariant();
            switch (value)
            {
                case "engine":
                    return "موتور (Personnel.DB.*DA) — نیازمند اسمبلی x86 و app pool ۳۲-بیتی";
                case "auto":
                    return CanLoadEngine() ? "خودکار ← موتور" : "خودکار ← SQL";
                default:
                    return "SQL (Dapper) — مستقل از اسمبلی موتور";
            }
        }
    }
}
