using TWF.Infrastructure.Localization;

namespace MyTime3.Content
{
    /// <summary>
    /// Single source of truth for the product identity of the rewritten TWF system.
    /// The complete rewrite (TD5 + MyTime3 + Views) is registered under the name "آریا / Aria".
    /// 100% offline: no remote branding assets, no license endpoint, no hardware lock.
    /// </summary>
    public static class AriaBranding
    {
        /// <summary>Persian product name displayed across the whole UI.</summary>
        public const string ProductName = "سامانه حضور و غیاب و کارتابل آریا";

        /// <summary>Short Latin brand token used in version strings and file names.</summary>
        public const string ProductCode = "ARIA";

        /// <summary>Release version of the rewritten system.</summary>
        public const string Version = "5.0";

        public const string FullVersion = "Aria v5.0";

        /// <summary>One-line description shown on login / about screens.</summary>
        public const string Tagline = "سامانه جامع حضور و غیاب، کارتابل سازمانی و گردش کار";

        /// <summary>Architecture badge — the system ships without any lock or license dependency.</summary>
        public const string ArchitectureBadge = "Zero-Lock · 100% Offline · Layered Architecture";

        /// <summary>Legacy product this build replaces (kept for operator traceability).</summary>
        public const string RewrittenFrom = "TWF 4.28 (MyTime3.dll + TD5.dll)";

        /// <summary>App name resolved from localization so the UI stays fully translatable.</summary>
        public static string AppName
        {
            get { return PersianString.AppName; }
        }
    }
}
