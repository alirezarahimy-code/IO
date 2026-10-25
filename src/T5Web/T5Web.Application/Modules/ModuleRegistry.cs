using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain;
using T5Web.Domain.Models;

namespace T5Web.Application.Modules
{
    /// <summary>
    /// Every legacy "simple table" screen, declared once.
    ///
    /// Provenance of each column name and type (this is the whole point):
    ///   * column names come from db/schema-manifest.json, which is built from the
    ///     vendor DDL and the engine's own data layer;
    ///   * column types come from engine/column-types.json, which is built from the
    ///     vendor DDL (authoritative) and, where DDL is silent, from the pinned engine's
    ///     ORM entities;
    ///   * <see cref="ModuleField.TypeEvidence"/> records which of the two supplied the
    ///     type, so a reviewer can check any single field without reading a tool.
    ///
    /// Test T16 re-derives all three of those facts and fails the build on any field
    /// whose column or type cannot be proved. Nothing here is a guess.
    ///
    /// Columns the sources cannot type (for example CalendarHoliday.HolidayDate, which
    /// has no ORM mapping and no DDL statement) are deliberately left out: the screen
    /// shows the row read-only rather than writing a value it cannot bind correctly.
    /// </summary>
    public static class ModuleRegistry
    {
        private static readonly List<ModuleDescriptor> Modules = Build();

        public static IReadOnlyList<ModuleDescriptor> All()
        {
            return Modules;
        }

        public static ModuleDescriptor Find(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }
            return Modules.FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<ModuleDescriptor> ForGroup(string groupFa)
        {
            return Modules.Where(m => string.Equals(m.GroupFa, groupFa, StringComparison.Ordinal)).ToList();
        }

        public static IReadOnlyList<string> Groups()
        {
            return Modules.Select(m => m.GroupFa).Distinct().ToList();
        }

        // ------------------------------------------------------------------ helpers

        private static ModuleField Key(string column, string labelFa, FieldKind kind, string evidence, bool identity = false)
        {
            ModuleField field = ModuleField.New(column, labelFa, kind, evidence);
            field.IsKey = true;
            field.Identity = identity;
            field.Required = true;
            field.MaxLength = kind == FieldKind.Text ? 100 : 0;
            return field;
        }

        private static ModuleField Text(string column, string labelFa, int maxLength, string evidence, bool required = false)
        {
            ModuleField field = ModuleField.New(column, labelFa, FieldKind.Text, evidence);
            field.MaxLength = maxLength;
            field.Required = required;
            return field;
        }

        private static ModuleField Number(string column, string labelFa, string evidence, bool required = false)
        {
            ModuleField field = ModuleField.New(column, labelFa, FieldKind.Number, evidence);
            field.Required = required;
            return field;
        }

        private static ModuleField Flag(string column, string labelFa, string evidence)
        {
            return ModuleField.New(column, labelFa, FieldKind.Boolean, evidence);
        }

        // ------------------------------------------------------------------ modules

        private static List<ModuleDescriptor> Build()
        {
            var modules = new List<ModuleDescriptor>();

            modules.Add(new ModuleDescriptor
            {
                Code = "emptype",
                TitleFa = "انواع استخدام",
                GroupFa = "داده پایه",
                Table = "EmpType",
                OrderBy = "EmpTypeName",
                LabelColumn = "EmpTypeName",
                ViewFeature = AdminFeature.BaseDataView,
                EditFeature = AdminFeature.BaseDataEdit,
                AllowCreate = true,
                AllowDelete = true,
                NoteFa = "نوع استخدام در پرونده پرسنل و در قواعد مرخصی به کار می‌رود.",
                Fields = new List<ModuleField>
                {
                    Key("EmpTypeID", "شناسه", FieldKind.Number, "ddl", identity: true),
                    Text("EmpTypeName", "نام نوع استخدام", 100, "ddl", required: true)
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "certificate",
                TitleFa = "مدارک تحصیلی",
                GroupFa = "داده پایه",
                Table = "Certificate",
                OrderBy = "CertName",
                LabelColumn = "CertName",
                ViewFeature = AdminFeature.BaseDataView,
                EditFeature = AdminFeature.BaseDataEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("CertID", "شناسه", FieldKind.Number, "ddl", identity: true),
                    Text("CertName", "عنوان مدرک", 100, "ddl", required: true)
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "leavetype",
                TitleFa = "انواع مرخصی",
                GroupFa = "مرخصی و تردد",
                Table = "LeaveType",
                OrderBy = "LeaveName",
                LabelColumn = "LeaveName",
                ViewFeature = AdminFeature.LeaveView,
                EditFeature = AdminFeature.LeaveEdit,
                AllowCreate = true,
                AllowDelete = true,
                NoteFa = "فیلدهای ساعتی/روزانه به‌دلیل نبود شاهد قطعی برای نوع ستون، فقط خواندنی است.",
                Fields = new List<ModuleField>
                {
                    // LeaveID is the identity column in the database; the engine's entity
                    // calls the same column LeaveTypeID. The rename is recorded as evidence
                    // ("orm-rename:LeaveTypeID") so T16 can prove the int type instead of
                    // trusting the name.
                    Key("LeaveID", "شناسه", FieldKind.Number, "orm-rename:LeaveTypeID"),
                    Text("LeaveName", "نام مرخصی", 100, "orm", required: true),
                    Text("Symbol", "نماد", 20, "orm"),
                    Number("Color", "رنگ (کد)", "orm"),
                    Flag("Payment", "با حقوق", "orm"),
                    Flag("OnlyOne", "فقط یک‌بار در سال", "orm"),
                    Number("MaxDailyInMonth", "سقف روزانه در ماه", "orm"),
                    Number("MaxDailyInYear", "سقف روزانه در سال", "orm"),
                    Number("MaxMinus", "حداکثر کسر", "orm"),
                    Number("StateKey", "کلید وضعیت", "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "calendar",
                TitleFa = "تقویم‌های کاری",
                GroupFa = "مرخصی و تردد",
                Table = "Calendar",
                OrderBy = "CalendarName",
                LabelColumn = "CalendarName",
                ViewFeature = AdminFeature.ScheduleView,
                EditFeature = AdminFeature.ScheduleEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("CalendarID", "شناسه", FieldKind.Number, "orm"),
                    Text("CalendarName", "نام تقویم", 100, "orm", required: true)
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "ramezan",
                TitleFa = "ماه رمضان",
                GroupFa = "مرخصی و تردد",
                Table = "Ramezan",
                OrderBy = "Year",
                ViewFeature = AdminFeature.ScheduleView,
                EditFeature = AdminFeature.ScheduleEdit,
                AllowCreate = true,
                AllowDelete = true,
                NoteFa = "از/تا تاریخ رمضان در جداول موتور نوع ستون مشخصی ندارد، پس فقط خوانده می‌شود.",
                Fields = new List<ModuleField>
                {
                    Key("ID", "شناسه", FieldKind.Number, "orm"),
                    Number("Year", "سال", "orm", required: true),
                    Flag("Late192123", "تأخیر ۱۹ تا ۲۳", "orm"),
                    Number("Late192123Value", "مقدار تأخیر", "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "parameter",
                TitleFa = "پارامترهای محاسباتی",
                GroupFa = "قواعد",
                Table = "Parameter",
                OrderBy = "ParameterName",
                LabelColumn = "ParameterName",
                ViewFeature = AdminFeature.ParametersView,
                EditFeature = AdminFeature.ParametersEdit,
                AllowCreate = true,
                AllowDelete = false,
                NoteFa = "پارامترها ورودی موتور محاسبات مرخصی‌اند؛ تغییرشان روی همه مانده‌ها اثر می‌گذارد.",
                Fields = new List<ModuleField>
                {
                    Key("ParameterID", "شناسه", FieldKind.Number, "orm"),
                    Text("ParameterName", "نام پارامتر", 100, "orm", required: true),
                    Text("UniqueSymbol", "نماد یکتا", 50, "orm"),
                    Text("Formula", "فرمول", 250, "orm"),
                    Text("SumFormula", "فرمول جمع", 250, "orm"),
                    Flag("IsSum", "جمعی است", "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "property",
                TitleFa = "فیلدهای اضافی پرسنل",
                GroupFa = "داده پایه",
                Table = "Property",
                OrderBy = "PropertyName",
                LabelColumn = "PropertyName",
                ViewFeature = AdminFeature.BaseDataView,
                EditFeature = AdminFeature.BaseDataEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("PropertyID", "شناسه", FieldKind.Number, "ddl"),
                    Text("PropertyName", "نام فیلد", 100, "ddl", required: true),
                    Number("PropertyType", "نوع فیلد", "ddl"),
                    Flag("IsSearchFilter", "در جست‌وجو نمایش داده شود", "ddl")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "dropdownvalue",
                TitleFa = "مقادیر فیلد انتخابی",
                GroupFa = "داده پایه",
                Table = "PropertyDropdownValue",
                OrderBy = "DropdownValue",
                LabelColumn = "DropdownValue",
                ViewFeature = AdminFeature.BaseDataView,
                EditFeature = AdminFeature.BaseDataEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("DropdownValueID", "شناسه", FieldKind.Number, "orm"),
                    Number("PropertyID", "فیلد", "orm", required: true),
                    Text("DropdownValue", "مقدار", 100, "orm", required: true)
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "gate",
                TitleFa = "درب‌ها",
                GroupFa = "دستگاه‌ها",
                Table = "Gate",
                OrderBy = "GateName",
                LabelColumn = "GateName",
                ViewFeature = AdminFeature.GateView,
                EditFeature = AdminFeature.GateEdit,
                AllowCreate = true,
                AllowDelete = true,
                NoteFa = "درب والد نمی‌تواند خودش یا زیرمجموعه خودش باشد؛ همان قاعده چارت سازمانی.",
                Fields = new List<ModuleField>
                {
                    Key("GateID", "شناسه", FieldKind.Number, "ddl"),
                    Text("GateName", "نام درب", 100, "ddl", required: true),
                    Number("UpperGateID", "درب والد", "ddl")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "option",
                TitleFa = "تنظیمات پورتال",
                GroupFa = "قواعد",
                Table = "Option",
                OrderBy = "SettingsID",
                ViewFeature = AdminFeature.ParametersView,
                EditFeature = AdminFeature.ParametersEdit,
                AllowCreate = false,
                AllowDelete = false,
                NoteFa = "کلید/مقدار تنظیمات موتور؛ افزودن کلید جدید فقط از دسکتاپ انجام می‌شود.",
                Fields = new List<ModuleField>
                {
                    Key("SettingsID", "کلید", FieldKind.Number, "ddl"),
                    Text("Value", "مقدار", 400, "ddl")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "deputy",
                TitleFa = "جانشین عناوین",
                GroupFa = "داده پایه",
                Table = "Deputy",
                OrderBy = "TitleID",
                ViewFeature = AdminFeature.TitlesView,
                EditFeature = AdminFeature.TitlesEdit,
                AllowCreate = true,
                AllowDelete = true,
                NoteFa = "هر سطر یعنی «عنوان، جانشینِ عنوان دیگر است»؛ کلید ترکیبی است.",
                Fields = new List<ModuleField>
                {
                    Key("TitleID", "عنوان", FieldKind.Number, "orm"),
                    Key("DeputyTitleID", "جانشین", FieldKind.Number, "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "devicemessage",
                TitleFa = "پیام‌های دستگاه",
                GroupFa = "دستگاه‌ها",
                Table = "DeviceMessage",
                OrderBy = "SMSID",
                ViewFeature = AdminFeature.DeviceView,
                EditFeature = AdminFeature.DeviceEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("SMSID", "شناسه", FieldKind.Number, "ddl"),
                    Text("Content", "متن پیام", 400, "ddl", required: true),
                    Number("DurationMins", "مدت نمایش (دقیقه)", "ddl"),
                    Number("SMSType", "نوع پیام", "ddl"),
                    ModuleField.New("ValidFromTime", "معتبر از", FieldKind.Date, "ddl")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "userdevicevalid",
                TitleFa = "اعتبار دستگاه هر پرسنل",
                GroupFa = "دستگاه‌ها",
                Table = "UserDeviceValid",
                OrderBy = "BadgeNumber",
                ViewFeature = AdminFeature.DeviceView,
                EditFeature = AdminFeature.DeviceEdit,
                AllowCreate = false,
                AllowDelete = false,
                NoteFa = "کلید ترکیبی پرسنل/دستگاه؛ فقط وضعیت اعتبار قابل تغییر است.",
                Fields = new List<ModuleField>
                {
                    Key("BadgeNumber", "شماره پرسنلی", FieldKind.Number, "ddl"),
                    Key("MachineNumber", "دستگاه", FieldKind.Number, "ddl"),
                    Flag("IsValid", "معتبر", "ddl")
                }
            });

            return modules;
        }
    }
}
