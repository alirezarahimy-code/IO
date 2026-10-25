using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain;
using T5Web.Domain.Models;

namespace T5Web.Application.Services
{
    /// <summary>
    /// The report catalogue: one entry per legacy report screen that survives the rewrite.
    ///
    /// Legacy inventory (docs/03) has 343 actions across 67 controllers; 26 of them are
    /// report screens. The rewrite keeps the 11 that answer a real operational question
    /// (plus one that only ever existed in the desktop client, "لیست مرخصی‌ها" - docs/10)
    /// and drops the RDLC designer (ReportDatas / ReportFilter / ReportTablix* /
    /// ReportTextbox / ReportTitles / ReportDesign) - a browser-based graphical report
    /// designer cannot be secured, tested or kept offline, and the RDLC files themselves
    /// stay reusable in the desktop application.
    ///
    /// Every report declares its source: "engine" reports are rendered by the pinned
    /// T5V3.Report engine through the desktop print path; "query" reports are plain
    /// aggregates this portal computes itself. Nothing in this catalogue requires a
    /// network call.
    /// </summary>
    public static class ReportCatalog
    {
        private static readonly List<ReportDescriptor> Reports = new List<ReportDescriptor>
        {
            new ReportDescriptor
            {
                Code = "daily",
                TitleFa = "گزارش روزانه تردد",
                LegacyController = "DailyReport",
                Source = "query",
                Feature = AdminFeature.ReportView,
                NoteFa = "ترددهای یک روز یا بازه کوتاه، با نام دستگاه و وضعیت اعتبار.",
                MaxDays = 62
            },
            new ReportDescriptor
            {
                Code = "late-early",
                TitleFa = "گزارش تأخیر و تعجیل",
                LegacyController = "LateEarlyReport",
                Source = "engine",
                Feature = AdminFeature.ReportView,
                NoteFa = "خروجی موتور حضور؛ بایت‌به‌بایت همان محاسبه نسخه دسکتاپ.",
                MaxDays = 31
            },
            new ReportDescriptor
            {
                Code = "overtime",
                TitleFa = "گزارش اضافه‌کاری",
                LegacyController = "OverTimeReport",
                Source = "engine",
                Feature = AdminFeature.ReportView,
                NoteFa = "اضافه‌کاری محاسبه‌شده توسط موتور، بدون بازمحاسبه در پورتال.",
                MaxDays = 31
            },
            new ReportDescriptor
            {
                Code = "exit",
                TitleFa = "گزارش خروج از سازمان",
                LegacyController = "ExitReport",
                Source = "query",
                Feature = AdminFeature.ReportView,
                NoteFa = "پرسنل با تاریخ ترک، در بازه انتخابی.",
                MaxDays = 366
            },
            new ReportDescriptor
            {
                Code = "sum",
                TitleFa = "گزارش جمع‌بندی ماهانه",
                LegacyController = "SumReport",
                Source = "query",
                Feature = AdminFeature.ReportView,
                NoteFa = "روزهای حضور، تأخیر، تعجیل و تردد ناقص برای هر نفر.",
                MaxDays = 62
            },
            new ReportDescriptor
            {
                Code = "cardex",
                TitleFa = "کارتکس پرسنل",
                LegacyController = "CardexReport",
                Source = "query",
                Feature = AdminFeature.ReportView,
                NoteFa = "گردش تردد و مرخصی یک نفر در بازه.",
                MaxDays = 366
            },
            new ReportDescriptor
            {
                Code = "user",
                TitleFa = "فهرست پرسنل",
                LegacyController = "UserReport",
                Source = "query",
                Feature = AdminFeature.PersonnelView,
                NoteFa = "فهرست پرسنل با واحد و نوع استخدام.",
                MaxDays = 366
            },
            new ReportDescriptor
            {
                Code = "personnel-status",
                TitleFa = "وضعیت پرسنل",
                LegacyController = "PersonnelStatusReport",
                Source = "query",
                Feature = AdminFeature.PersonnelView,
                NoteFa = "پرسنل فعال/غیرفعال، دستگاه‌فعال، کاربر وب.",
                MaxDays = 366
            },
            new ReportDescriptor
            {
                Code = "remained-leaves",
                TitleFa = "مانده مرخصی",
                LegacyController = "RemainedLeavesReport",
                Source = "query",
                Feature = AdminFeature.LeaveView,
                NoteFa = "مانده پایان سال برای هر نفر و نوع مرخصی.",
                MaxDays = 366
            },
            new ReportDescriptor
            {
                Code = "leaves",
                TitleFa = "لیست مرخصی‌ها",
                LegacyController = "T5V3/frmLeavesReport",
                Source = "query",
                Feature = AdminFeature.LeaveView,
                NoteFa = "مرخصی‌های ثبت‌شده در بازه تاریخ ثبت، با نوع، روز، سند و توضیح. " +
                         "این گزارش فقط در نسخه دسکتاپ بود (frmLeavesReport) و به پورتال اضافه شد.",
                MaxDays = 366
            },
            new ReportDescriptor
            {
                Code = "requests",
                TitleFa = "درخواست‌ها",
                LegacyController = "RequestsReport",
                Source = "query",
                Feature = AdminFeature.RequestView,
                NoteFa = "درخواست‌ها با وضعیت زنجیره تأیید.",
                MaxDays = 366
            },
            new ReportDescriptor
            {
                Code = "grid",
                TitleFa = "جدول حضور",
                LegacyController = "GridReport",
                Source = "engine",
                Feature = AdminFeature.ReportView,
                NoteFa = "جدول ماهانه حضور؛ محاسبه موتور، نمایش پورتال.",
                MaxDays = 31
            }
        };

        public static IReadOnlyList<ReportDescriptor> All()
        {
            return Reports;
        }

        public static ReportDescriptor Find(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }
            return Reports.FirstOrDefault(r =>
                string.Equals(r.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The reports one session may see. A report the session cannot read is not
        /// listed at all - the legacy portal listed every report and only failed on
        /// execution, which leaked the existence of payroll reports to everyone.
        /// </summary>
        public static IReadOnlyList<ReportDescriptor> VisibleFor(AdminSession session)
        {
            if (session == null)
            {
                return new List<ReportDescriptor>();
            }
            return Reports.Where(r => session.CanView(r.Feature)).ToList();
        }

        /// <summary>
        /// Validates a requested window. Every report has its own ceiling because a
        /// 366-day grid report is a table scan, while a 366-day personnel list is fine.
        /// </summary>
        public static OperationResult ValidateRange(ReportDescriptor descriptor, DateTime from, DateTime to)
        {
            if (descriptor == null)
            {
                return OperationResult.Fail("گزارش مشخص نیست.");
            }
            if (to.Date < from.Date)
            {
                return OperationResult.Fail("تاریخ پایان نمی‌تواند پیش از تاریخ شروع باشد.");
            }
            int days = (int)(to.Date - from.Date).TotalDays + 1;
            if (days > descriptor.MaxDays)
            {
                return OperationResult.Fail(
                    "بازه انتخابی " + days + " روز است؛ برای «" + descriptor.TitleFa +
                    "» حداکثر " + descriptor.MaxDays + " روز مجاز است.");
            }
            if (from.Year < 1900 || to.Year > 2200)
            {
                return OperationResult.Fail("بازه تاریخ معتبر نیست.");
            }
            return OperationResult.Ok();
        }
    }
}
