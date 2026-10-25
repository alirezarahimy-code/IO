namespace T5Web.Web.Content
{
    /// <summary>
    /// Single source of truth for the product identity of the rewritten T5Web portal.
    ///
    /// The rewrite of the 67-controller legacy portal (T5V3Web.dll 3.5.7) is registered
    /// under the name «آریا ادمین» / Aria Admin, in the same way the TWF rewrite
    /// (src/TWF) was registered under the name «آریا» / Aria. The folder stays
    /// `src/T5Web` (paths, tools and tests depend on it); the product does not.
    ///
    /// 100% offline: no remote branding asset, no CDN, no font or icon download.
    /// </summary>
    public static class AriaAdminBranding
    {
        /// <summary>Persian product name displayed across the whole UI.</summary>
        public const string ProductName = "آریا ادمین";

        /// <summary>Short Latin brand token used in version strings and log lines.</summary>
        public const string ProductCode = "ARIA-ADMIN";

        /// <summary>Release version of the rewritten portal.</summary>
        public const string Version = "5.0";

        /// <summary>Version string shown in the console title and the diagnostics screen.</summary>
        public const string FullVersion = "Aria Admin v5.0";

        /// <summary>Persian long title used in documents and the runbook.</summary>
        public const string ProductTitleFa = "کنسول مدیریت سامانه حضور و غیاب T5 — آریا ادمین";

        /// <summary>One-line description shown under the rail brand.</summary>
        public const string Tagline = "مدیریت پرسنل، تردد، مرخصی، شیفت، گردش کار و دستگاه‌ها";

        /// <summary>Architecture badge: what this portal is and what it is not.</summary>
        public const string ArchitectureBadge = "Windows Auth · 100% Offline · Zero-Lock";

        /// <summary>Legacy product this portal replaces (kept for operator traceability).</summary>
        public const string RewrittenFrom = "T5Web [3.5.7] (T5V3Web.dll, 67 controllers / 343 actions)";

        /// <summary>Title used in the browser tab: "صفحه · آریا ادمین".</summary>
        public static string WindowTitle(string pageTitle)
        {
            return string.IsNullOrWhiteSpace(pageTitle)
                ? ProductName
                : pageTitle + " · " + ProductName;
        }
    }
}
