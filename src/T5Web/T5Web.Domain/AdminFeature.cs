using System;
using System.Collections.Generic;

namespace T5Web.Domain
{
    /// <summary>
    /// Administrative capabilities of the T5Web portal.
    ///
    /// The legacy T5V3Web asked the database for a flat model with ~40 booleans
    /// (MenusAccessModel) and rendered the menu from it. That made "who may do what"
    /// implicit and untestable. Here every capability is an explicit enum member and
    /// the menu, the routes and the audit trail all speak the same vocabulary.
    ///
    /// Values are stable: they are persisted in the portal's own permission table and
    /// in the audit log, so never renumber them. New features are appended.
    /// </summary>
    public enum AdminFeature
    {
        // --- people ---
        PersonnelView = 10,
        PersonnelEdit = 11,
        PersonnelPhoto = 12,
        PersonnelImport = 13,

        // --- organisation ---
        DepartmentView = 20,
        DepartmentEdit = 21,
        TitlesView = 22,
        TitlesEdit = 23,

        // --- devices and gates ---
        DeviceView = 30,
        DeviceEdit = 31,
        GateEdit = 32,
        DeviceSync = 33,

        // --- access levels ---
        AccessView = 40,
        AccessEdit = 41,

        // --- workflow ---
        WorkflowView = 50,
        WorkflowEdit = 51,

        // --- operations ---
        ReportView = 60,
        RecordManualInsert = 61,
        LogView = 70,
        PortalAdmin = 90
    }

    /// <summary>
    /// Access level for one <see cref="AdminFeature"/>, mirroring the engine's
    /// AccessLevel enum (Deny / ReadOnly / Allow).
    /// </summary>
    public enum AccessLevel
    {
        Deny = 0,
        ReadOnly = 1,
        Allow = 2
    }

    /// <summary>Classification used by the menu and by the authorization filter.</summary>
    public static class AdminFeatures
    {
        private static readonly Dictionary<AdminFeature, string> TitlesFa =
            new Dictionary<AdminFeature, string>
            {
                { AdminFeature.PersonnelView, "مشاهده پرسنل" },
                { AdminFeature.PersonnelEdit, "ویرایش پرسنل" },
                { AdminFeature.PersonnelPhoto, "مدیریت تصویر پرسنل" },
                { AdminFeature.PersonnelImport, "ورود گروهی از اکسل" },
                { AdminFeature.DepartmentView, "مشاهده چارت سازمانی" },
                { AdminFeature.DepartmentEdit, "ویرایش چارت سازمانی" },
                { AdminFeature.TitlesView, "مشاهده عناوین شغلی" },
                { AdminFeature.TitlesEdit, "ویرایش عناوین شغلی" },
                { AdminFeature.DeviceView, "مشاهده دستگاه‌ها" },
                { AdminFeature.DeviceEdit, "ویرایش دستگاه‌ها" },
                { AdminFeature.GateEdit, "مدیریت درب‌ها" },
                { AdminFeature.DeviceSync, "همگام‌سازی دستگاه" },
                { AdminFeature.AccessView, "مشاهده سطوح دسترسی" },
                { AdminFeature.AccessEdit, "ویرایش سطوح دسترسی" },
                { AdminFeature.WorkflowView, "مشاهده گردش کار" },
                { AdminFeature.WorkflowEdit, "ویرایش گردش کار" },
                { AdminFeature.ReportView, "مشاهده گزارش‌ها" },
                { AdminFeature.RecordManualInsert, "ثبت دستی تردد" },
                { AdminFeature.LogView, "مشاهده رخدادها" },
                { AdminFeature.PortalAdmin, "مدیریت پورتال" }
            };

        public static string TitleOf(AdminFeature feature)
        {
            string title;
            return TitlesFa.TryGetValue(feature, out title) ? title : feature.ToString();
        }

        public static IEnumerable<AdminFeature> All()
        {
            return (AdminFeature[])Enum.GetValues(typeof(AdminFeature));
        }

        /// <summary>The level that a feature needs in order to be *usable* (not just visible).</summary>
        public static AccessLevel RequiredLevelFor(AdminFeature feature)
        {
            string name = feature.ToString();
            if (name.EndsWith("Edit", StringComparison.Ordinal) ||
                name.EndsWith("Import", StringComparison.Ordinal) ||
                name.EndsWith("Sync", StringComparison.Ordinal) ||
                name.Equals("RecordManualInsert", StringComparison.Ordinal) ||
                name.Equals("PortalAdmin", StringComparison.Ordinal))
            {
                return AccessLevel.Allow;
            }
            return AccessLevel.ReadOnly;
        }
    }
}
