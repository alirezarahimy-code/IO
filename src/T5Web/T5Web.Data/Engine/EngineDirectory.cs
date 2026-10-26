using System;
using System.Data;

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
    /// Both implementations arrive as factories rather than being constructed here. That is
    /// not ceremony: `PersonnelEngineDirectory` and `System.Data.SqlClient` are exactly the
    /// two things a .NET 8/10 build cannot carry, so this file must not name them or the
    /// portable build would fail here too (CI proves it - spike/net8 targets net8.0 and
    /// net10.0 with this file compiled in).
    ///
    /// A typo is not "auto": an unreadable value falls back to `sql` and says nothing,
    /// because `auto` would hide a configuration mistake behind a working screen.
    /// </summary>
    public static class EngineDirectory
    {
        public const string Key = "T5WebEngineDirectory";

        /// <param name="mode">The appSetting value. Null, empty or unknown means `sql`.</param>
        /// <param name="personnelFactory">Opens the Personnel catalog. Never called here.</param>
        /// <param name="engineFactory">
        /// Creates the engine-backed reader, or null when the hosting project has no engine
        /// reference at all (the .NET 8/10 build). Supplying it is what keeps `engine` and
        /// `auto` available on .NET Framework for the cut-over comparison.
        /// </param>
        public static IEngineDirectory Create(
            string mode,
            Func<IDbConnection> personnelFactory,
            Func<IEngineDirectory> engineFactory)
        {
            if (personnelFactory == null)
            {
                throw new ArgumentNullException(nameof(personnelFactory));
            }

            string value = (mode ?? string.Empty).Trim().ToLowerInvariant();
            switch (value)
            {
                case "engine":
                    if (engineFactory == null)
                    {
                        // Loud, because this is a deployment that asked for the engine and
                        // got a build without it. Silently serving sql here would hide a
                        // misconfiguration behind screens that still look right.
                        throw new InvalidOperationException(
                            "T5WebEngineDirectory=engine was requested, but this build carries no engine " +
                            "reference. Use 'sql', or deploy the .NET Framework build that has it.");
                    }
                    return engineFactory();

                case "auto":
                    // Probing by type load, not by file existence: a 64-bit app pool can see
                    // Personnel.dll on disk and still be unable to load it, and that failure
                    // belongs to the engine path, not to this switch.
                    if (engineFactory != null && CanLoadEngine())
                    {
                        return engineFactory();
                    }
                    return new SqlEngineDirectory(personnelFactory);

                default:
                    return new SqlEngineDirectory(personnelFactory);
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
                // The assembly name, not a type from it: naming the type would add a compile
                // reference and defeat the point of this probe.
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
