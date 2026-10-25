using System;
using System.Configuration;

namespace T5Web.Web.Security
{
    /// <summary>
    /// Per-module kill switch for the cut-over period.
    ///
    /// During coexistence one module may behave badly (a schema difference, a rule that
    /// needs rethinking) while the rest of the portal is fine. Turning the whole portal
    /// off for that would push operators back to the legacy site for everything; this
    /// switch turns off exactly one module.
    ///
    ///   &lt;add key="ModuleEnabled:parameter" value="false" /&gt;
    ///
    /// Absent key means enabled: a deployment that never heard of the switch keeps
    /// working, and a typo in the key cannot silently disable a screen.
    /// </summary>
    public static class ModuleSwitch
    {
        public const string Prefix = "ModuleEnabled:";

        public static bool IsEnabled(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }
            string raw = ConfigurationManager.AppSettings[Prefix + code.Trim()];
            if (string.IsNullOrWhiteSpace(raw))
            {
                return true;
            }
            string value = raw.Trim().ToLowerInvariant();
            return value != "false" && value != "0" && value != "no" && value != "off";
        }

        /// <summary>The reason shown to an operator who opens a disabled module.</summary>
        public static string DisabledFa(string code)
        {
            return "ماژول «" + code + "» در این نصب غیرفعال شده است " +
                   "(کلید «" + Prefix + code + "» در Web.config). برای فعال‌سازی، مقدار را true کنید.";
        }
    }
}
