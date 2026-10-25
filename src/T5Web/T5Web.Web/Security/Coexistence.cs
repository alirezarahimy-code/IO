using System;
using System.Configuration;

namespace T5Web.Web.Security
{
    /// <summary>Which application owns a screen right now.</summary>
    public enum CoexistenceMode
    {
        /// <summary>The portal is authoritative; the legacy site is retired.</summary>
        Portal = 0,

        /// <summary>
        /// Both run side by side: the portal is read-mostly, the legacy site still owns
        /// the screens it has, and the portal shows a banner on every page so nobody
        /// edits a record in the wrong system.
        /// </summary>
        Coexist = 1,

        /// <summary>The portal is installed but switched off; every request is redirected.</summary>
        Legacy = 2
    }

    /// <summary>
    /// The cut-over switch.
    ///
    /// A rewrite of a payroll-adjacent system is not switched on by copying files. Two
    /// deployments read the same database, so the only safe cut-over is explicit and
    /// reversible: one appSetting decides who owns a screen, and every page shows which
    /// mode is active. The switch is read from configuration on every request (no cache
    /// of a decision that an operator may be about to reverse).
    ///
    ///   T5WebMode        portal | coexist | legacy   (default: coexist)
    ///   T5WebLegacyUrl   the legacy site root, used for the "open in legacy" link and
    ///                    for the redirect in legacy mode
    /// </summary>
    public static class Coexistence
    {
        public const string ModeKey = "T5WebMode";
        public const string LegacyUrlKey = "T5WebLegacyUrl";

        /// <summary>
        /// Defaults to <see cref="CoexistenceMode.Coexist"/>: an unconfigured
        /// installation must never *look* authoritative.
        /// </summary>
        public static CoexistenceMode Mode
        {
            get
            {
                string raw = ConfigurationManager.AppSettings[ModeKey];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return CoexistenceMode.Coexist;
                }
                switch (raw.Trim().ToLowerInvariant())
                {
                    case "portal":
                        return CoexistenceMode.Portal;
                    case "legacy":
                        return CoexistenceMode.Legacy;
                    default:
                        return CoexistenceMode.Coexist;
                }
            }
        }

        public static string LegacyUrl
        {
            get
            {
                string raw = ConfigurationManager.AppSettings[LegacyUrlKey];
                return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
            }
        }

        public static bool IsLegacyAuthoritative
        {
            get { return Mode == CoexistenceMode.Legacy; }
        }

        /// <summary>The banner text, or an empty string when the portal is authoritative.</summary>
        public static string BannerFa
        {
            get
            {
                switch (Mode)
                {
                    case CoexistenceMode.Coexist:
                        return "حالت هم‌زیستی: پورتال و نسخه قبلی هر دو فعال‌اند. " +
                               "هر تغییری که اینجا ثبت کنید در همان پایگاه‌داده ذخیره می‌شود و در نسخه قبلی هم دیده می‌شود.";
                    case CoexistenceMode.Legacy:
                        return "حالت نسخه قبلی: پورتال برای بازبینی نصب شده ولی مرجع، نسخه قبلی است.";
                    default:
                        return string.Empty;
                }
            }
        }
    }
}
