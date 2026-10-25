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

        // --- attendance records and requests (phase 5) ---
        RecordsView = 15,
        RecordsEdit = 16,
        RequestView = 17,
        RequestApprove = 18,

        // --- organisation ---
        DepartmentView = 20,
        DepartmentEdit = 21,
        TitlesView = 22,
        TitlesEdit = 23,

        // --- base data (phase 5): emp types, certificates, properties ---
        BaseDataView = 24,
        BaseDataEdit = 25,
        ParametersView = 26,
        ParametersEdit = 27,

        // --- devices and gates ---
        DeviceView = 30,
        DeviceEdit = 31,
        GateEdit = 32,
        DeviceSync = 33,
        DeviceCommand = 34,
        GateView = 35,

        // --- access levels ---
        AccessView = 40,
        AccessEdit = 41,

        // --- workflow ---
        WorkflowView = 50,
        WorkflowEdit = 51,

        // --- rules, schedules and leave (phase 5) ---
        RulesView = 52,
        RulesEdit = 53,
        ScheduleView = 54,
        ScheduleEdit = 55,
        LeaveView = 56,
        LeaveEdit = 57,

        // --- operations ---
        ReportView = 60,
        RecordManualInsert = 61,
        ReportExport = 62,
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
                { AdminFeature.PersonnelImport, "ورود گروهی از فایل" },
                { AdminFeature.RecordsView, "مشاهده ترددها" },
                { AdminFeature.RecordsEdit, "ویرایش ترددها" },
                { AdminFeature.RequestView, "مشاهده درخواست‌ها" },
                { AdminFeature.RequestApprove, "تأیید درخواست‌ها" },
                { AdminFeature.BaseDataView, "مشاهده داده پایه" },
                { AdminFeature.BaseDataEdit, "ویرایش داده پایه" },
                { AdminFeature.ParametersView, "مشاهده پارامترها" },
                { AdminFeature.ParametersEdit, "ویرایش پارامترها" },
                { AdminFeature.DeviceCommand, "ارسال دستور به دستگاه" },
                { AdminFeature.GateView, "مشاهده درب‌ها" },
                { AdminFeature.RulesView, "مشاهده قواعد حضور" },
                { AdminFeature.RulesEdit, "ویرایش قواعد حضور" },
                { AdminFeature.ScheduleView, "مشاهده شیفت و برنامه" },
                { AdminFeature.ScheduleEdit, "ویرایش شیفت و برنامه" },
                { AdminFeature.LeaveView, "مشاهده مرخصی و مانده" },
                { AdminFeature.LeaveEdit, "ویرایش مرخصی و مانده" },
                { AdminFeature.ReportExport, "خروجی گرفتن از گزارش‌ها" },
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
                name.EndsWith("Approve", StringComparison.Ordinal) ||
                name.Equals("RecordManualInsert", StringComparison.Ordinal) ||
                name.Equals("DeviceCommand", StringComparison.Ordinal) ||
                name.Equals("PortalAdmin", StringComparison.Ordinal))
            {
                return AccessLevel.Allow;
            }
            return AccessLevel.ReadOnly;
        }
    }
}
