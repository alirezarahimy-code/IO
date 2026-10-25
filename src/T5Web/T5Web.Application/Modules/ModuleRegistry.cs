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

        private static ModuleField Date(string column, string labelFa, string evidence, bool required = false)
        {
            ModuleField field = ModuleField.New(column, labelFa, FieldKind.Date, evidence);
            field.Required = required;
            return field;
        }

        private static ModuleField Time(string column, string labelFa, string evidence)
        {
            return ModuleField.New(column, labelFa, FieldKind.Time, evidence);
        }

        private static ModuleField Show(string column, string labelFa, FieldKind kind)
        {
            // Display-only: the column is in the schema manifest but its type is not
            // modelled by the engine, so the portal shows it and never binds it.
            ModuleField field = ModuleField.New(column, labelFa, kind, "display");
            field.DisplayOnly = true;
            return field;
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
                Catalog = "personnel",
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
                Catalog = "personnel",
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
                Catalog = "personnel",
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
                Catalog = "personnel",
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
                Catalog = "personnel",
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
                Catalog = "personnel",
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
                Catalog = "personnel",
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


            // ------------------------------------------------ rules (phase 5-C)

            modules.Add(new ModuleDescriptor
            {
                Code = "rule",
                TitleFa = "قواعد حضور",
                GroupFa = "قواعد",
                Table = "Rule",
                OrderBy = "ParamName",
                LabelColumn = "ParamName",
                ViewFeature = AdminFeature.RulesView,
                EditFeature = AdminFeature.RulesEdit,
                AllowCreate = true,
                AllowDelete = false,
                NoteFa = "این قواعد ورودی موتور محاسبه تأخیر/اضافه‌کاری‌اند؛ تغییرشان از روز بعد اعمال می‌شود.",
                Fields = new List<ModuleField>
                {
                    Key("RuleID", "شناسه", FieldKind.Number, "orm"),
                    Text("ParamName", "نام قاعده", 100, "orm", required: true),
                    Flag("OverTime", "اضافه‌کاری محاسبه شود", "orm"),
                    Flag("OverTimeBefore", "اضافه‌کاری قبل از شیفت", "orm"),
                    Flag("OverTimeAfter", "اضافه‌کاری بعد از شیفت", "orm"),
                    Flag("FreeOverTime", "اضافه‌کاری آزاد", "orm"),
                    Flag("EarlyMore", "تعجیل مجاز", "orm"),
                    Flag("LateMore", "تأخیر مجاز", "orm"),
                    Time("OverTimeMax", "سقف اضافه‌کاری", "orm"),
                    Time("OverTimeBeforeMax", "سقف قبل از شیفت", "orm"),
                    Time("OverTimeAfterMax", "سقف بعد از شیفت", "orm"),
                    Time("FreeOverTimeMax", "سقف آزاد", "orm"),
                    Time("ConstOTMaxDaily", "سقف ثابت روزانه", "orm"),
                    Time("MaxBalanceDay", "سقف مانده روز", "orm"),
                    Time("MinOverTimeInterval", "کمینه فاصله اضافه‌کاری", "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "userrule",
                TitleFa = "قاعده هر پرسنل",
                GroupFa = "قواعد",
                Table = "UserRule",
                OrderBy = "UserRuleID",
                ViewFeature = AdminFeature.RulesView,
                EditFeature = AdminFeature.RulesEdit,
                AllowCreate = true,
                AllowDelete = true,
                NoteFa = "بازه‌های زمانی یک پرسنل نباید هم‌پوشانی داشته باشند؛ کنترل در RulesController انجام می‌شود.",
                Fields = new List<ModuleField>
                {
                    Key("UserRuleID", "شناسه", FieldKind.Number, "orm"),
                    Number("BadgeNumber", "شماره پرسنلی", "orm", required: true),
                    Number("RuleID", "قاعده", "orm", required: true),
                    Date("FromDate", "از تاریخ", "orm", required: true),
                    Date("ToDate", "تا تاریخ", "orm"),
                    Show("RuleType", "نوع قاعده", FieldKind.Number)
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "departmentrule",
                TitleFa = "قاعده هر واحد",
                GroupFa = "قواعد",
                Table = "DepartmentRule",
                OrderBy = "DeptRuleID",
                ViewFeature = AdminFeature.RulesView,
                EditFeature = AdminFeature.RulesEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("DeptRuleID", "شناسه", FieldKind.Number, "orm"),
                    Number("DepartmentID", "واحد سازمانی", "orm", required: true),
                    Number("RuleID", "قاعده", "orm", required: true),
                    Date("FromDate", "از تاریخ", "orm", required: true),
                    Date("ToDate", "تا تاریخ", "orm")
                }
            });

            // --------------------------------------------- schedules (phase 5-C)

            modules.Add(new ModuleDescriptor
            {
                Code = "schedule",
                TitleFa = "قالب‌های شیفت",
                GroupFa = "مرخصی و تردد",
                Table = "Schedule",
                OrderBy = "ScheduleID",
                ViewFeature = AdminFeature.ScheduleView,
                EditFeature = AdminFeature.ScheduleEdit,
                AllowCreate = true,
                AllowDelete = true,
                NoteFa = "نام قالب (SchName) نوع ستون تأییدشده ندارد، پس نمایش داده می‌شود و از پورتال ویرایش نمی‌شود.",
                Fields = new List<ModuleField>
                {
                    Key("ScheduleID", "شناسه", FieldKind.Number, "orm"),
                    Show("SchName", "نام قالب", FieldKind.Text),
                    Number("ScheduleTime", "مدت (دقیقه)", "orm"),
                    Number("ScheduleTimeType", "نوع زمان‌بندی", "orm"),
                    Date("ScheduleTimeOfDay", "زمان مبنا", "orm"),
                    Number("Color", "رنگ (کد)", "orm"),
                    Number("RuleID", "قاعده", "orm"),
                    Number("CalendarID", "تقویم", "orm"),
                    Time("IntervalB", "فاصله", "orm"),
                    Number("WorkMins", "دقیقه کارکرد", "orm"),
                    Flag("RestTime", "استراحت دارد", "orm"),
                    Flag("BeforeI", "قبل از ورود", "orm"),
                    Flag("AfterI", "بعد از خروج", "orm"),
                    Text("Symbol", "نماد", 20, "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "shift",
                TitleFa = "شیفت‌ها",
                GroupFa = "مرخصی و تردد",
                Table = "Shift",
                OrderBy = "ShiftName",
                LabelColumn = "ShiftName",
                ViewFeature = AdminFeature.ScheduleView,
                EditFeature = AdminFeature.ScheduleEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("ShiftID", "شناسه", FieldKind.Number, "orm"),
                    Text("ShiftName", "نام شیفت", 100, "orm", required: true),
                    Number("Period", "دوره (روز)", "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "usershift",
                TitleFa = "شیفت هر پرسنل",
                GroupFa = "مرخصی و تردد",
                Table = "UserShift",
                OrderBy = "BadgeNumber",
                ViewFeature = AdminFeature.ScheduleView,
                EditFeature = AdminFeature.ScheduleEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("BadgeNumber", "شماره پرسنلی", FieldKind.Number, "orm"),
                    Key("ShiftID", "شیفت", FieldKind.Number, "orm"),
                    Key("FromDate", "از تاریخ", FieldKind.Date, "orm"),
                    Show("EndDate", "تا تاریخ", FieldKind.Date)
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "tempschedule",
                TitleFa = "برنامه موقت",
                GroupFa = "مرخصی و تردد",
                Table = "TempSchedule",
                OrderBy = "BadgeNumber",
                ViewFeature = AdminFeature.ScheduleView,
                EditFeature = AdminFeature.ScheduleEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("BadgeNumber", "شماره پرسنلی", FieldKind.Number, "orm"),
                    Key("AssignDate", "تاریخ", FieldKind.Date, "orm"),
                    Show("ScheduleID", "قالب شیفت", FieldKind.Number)
                }
            });

            // ------------------------------------------------ leave (phase 5-C)

            modules.Add(new ModuleDescriptor
            {
                Code = "yearremain",
                TitleFa = "مانده مرخصی سالانه",
                GroupFa = "مرخصی و تردد",
                Table = "YearRemain",
                OrderBy = "BadgeNumber",
                ViewFeature = AdminFeature.LeaveView,
                EditFeature = AdminFeature.LeaveEdit,
                AllowCreate = true,
                AllowDelete = true,
                NoteFa = "مانده‌ای که موتور محاسبه می‌کند دست‌نخورده می‌ماند؛ این صفحه فقط مقدار ذخیره‌شده را نشان می‌دهد.",
                Fields = new List<ModuleField>
                {
                    Key("BadgeNumber", "شماره پرسنلی", FieldKind.Number, "orm"),
                    Key("LeaveTypeID", "نوع مرخصی", FieldKind.Number, "orm"),
                    Key("Year", "سال", FieldKind.Number, "orm"),
                    Number("DailyValue", "مانده (روز)", "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "leavesadded",
                TitleFa = "مرخصی‌های افزوده",
                GroupFa = "مرخصی و تردد",
                Table = "LeavesAdded",
                OrderBy = "ID",
                ViewFeature = AdminFeature.LeaveView,
                EditFeature = AdminFeature.LeaveEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("ID", "شناسه", FieldKind.Number, "orm"),
                    Number("BadgeNumber", "شماره پرسنلی", "orm", required: true),
                    Number("LeaveTypeID", "نوع مرخصی", "orm", required: true),
                    Text("DocumentNumber", "شماره سند", 50, "orm"),
                    Text("Descriptions", "توضیح", 250, "orm"),
                    Flag("IsResell", "قابل تبدیل به وجه", "orm"),
                    Show("AssignDate", "تاریخ ثبت", FieldKind.Date),
                    Show("DailyValue", "مقدار (روز)", FieldKind.Number)
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "dailyLeaf",
                TitleFa = "مرخصی روزانه",
                GroupFa = "مرخصی و تردد",
                Table = "DailyLeaf",
                OrderBy = "Date",
                ViewFeature = AdminFeature.LeaveView,
                EditFeature = AdminFeature.LeaveEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("BadgeNumber", "شماره پرسنلی", FieldKind.Number, "orm"),
                    Key("Date", "تاریخ", FieldKind.Date, "orm"),
                    Key("LeaveID", "نوع مرخصی", FieldKind.Number, "orm"),
                    Number("DescriptionID", "شرح", "orm"),
                    Text("DocumentNumber", "شماره سند", 50, "orm"),
                    Text("Descriptions", "توضیح", 250, "orm"),
                    Show("InsertDate", "زمان ثبت", FieldKind.Date)
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "hourlyleave",
                TitleFa = "مرخصی ساعتی",
                GroupFa = "مرخصی و تردد",
                Table = "HourlyLeave",
                OrderBy = "ID",
                ViewFeature = AdminFeature.LeaveView,
                EditFeature = AdminFeature.LeaveEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("ID", "شناسه", FieldKind.Number, "orm"),
                    Number("BadgeNumber", "شماره پرسنلی", "orm", required: true),
                    Date("FromTime", "از", "orm", required: true),
                    Date("ToTime", "تا", "orm", required: true),
                    Text("Descriptions", "توضیح", 250, "orm"),
                    Show("LeaveID", "نوع مرخصی", FieldKind.Number)
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "validleave",
                TitleFa = "مرخصی معتبر (مجوز)",
                GroupFa = "مرخصی و تردد",
                Table = "ValidLeave",
                OrderBy = "ID",
                ViewFeature = AdminFeature.LeaveView,
                EditFeature = AdminFeature.LeaveEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("ID", "شناسه", FieldKind.Number, "orm"),
                    Number("BadgeNumber", "شماره پرسنلی", "orm", required: true),
                    Date("FromDate", "از تاریخ", "orm", required: true),
                    Date("ToDate", "تا تاریخ", "orm", required: true),
                    Time("ValidHours", "ساعت مجاز", "orm"),
                    Text("Descriptions", "توضیح", 250, "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "calholiday",
                TitleFa = "روزهای تعطیل تقویم",
                GroupFa = "مرخصی و تردد",
                Table = "CalendarHoliday",
                OrderBy = "CalendarID",
                ViewFeature = AdminFeature.ScheduleView,
                EditFeature = AdminFeature.ScheduleEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("CalendarID", "تقویم", FieldKind.Number, "orm"),
                    Key("HolidayDate", "تاریخ تعطیل", FieldKind.Date, "orm"),
                    Text("Descriptions", "شرح", 100, "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "customworkflow",
                TitleFa = "گردش کار سفارشی",
                GroupFa = "قواعد",
                Table = "CustomWorkflow",
                OrderBy = "CustomWorkflowID",
                LabelColumn = "Label",
                ViewFeature = AdminFeature.WorkflowView,
                EditFeature = AdminFeature.WorkflowEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("CustomWorkflowID", "شناسه", FieldKind.Number, "orm"),
                    Text("Label", "عنوان", 100, "orm", required: true),
                    Text("DescriptionsStartWith", "شرح با این عبارت شروع شود", 100, "orm"),
                    Text("DescriptionsContains", "شرح شامل این عبارت", 100, "orm"),
                    Number("LeaveTypeID", "نوع مرخصی", "orm"),
                    Number("LeaveDescriptionID", "شرح مرخصی", "orm"),
                    Number("MinRequestLen", "کمینه طول شرح", "orm"),
                    Number("MaxRequestLen", "بیشینه طول شرح", "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "leavedescription",
                TitleFa = "توضیحات مرخصی",
                GroupFa = "مرخصی و تردد",
                Table = "LeaveDescription",
                Catalog = "t5",
                OrderBy = "ID",
                ViewFeature = AdminFeature.LeaveView,
                EditFeature = AdminFeature.LeaveEdit,
                AllowCreate = true,
                AllowDelete = true,
                NoteFa = "فهرست شرح‌های استاندارد مرخصی که در ثبت مرخصی انتخاب می‌شوند " +
                         "(دسکتاپ: frmLeaveDescription). شناسه را اپراتور تعیین می‌کند، چون ستون identity نیست.",
                Fields = new List<ModuleField>
                {
                    Key("ID", "شناسه", FieldKind.Number, "orm"),
                    // The ORM exposes Description as nvarchar without a declared length, so the
                    // portal caps input at a value the engine itself accepts everywhere.
                    Text("Description", "شرح", 250, "orm", required: true)
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "userinfoproperty",
                TitleFa = "مقدار فیلد اضافی پرسنل",
                GroupFa = "داده پایه",
                Table = "UserInfoProperty",
                Catalog = "personnel",
                OrderBy = "BadgeNumber",
                ViewFeature = AdminFeature.BaseDataView,
                EditFeature = AdminFeature.BaseDataEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("BadgeNumber", "شماره پرسنلی", FieldKind.Number, "orm"),
                    Key("PropertyID", "فیلد", FieldKind.Number, "orm"),
                    Text("StringValue", "مقدار متنی", 250, "orm"),
                    Number("DropDownValue", "مقدار انتخابی", "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "dashboard",
                TitleFa = "گروه‌های داشبورد",
                GroupFa = "داشبورد",
                Table = "Dashboard",
                Catalog = "personnel",
                OrderBy = "ID",
                ViewFeature = AdminFeature.BaseDataView,
                EditFeature = AdminFeature.BaseDataEdit,
                AllowCreate = true,
                AllowDelete = true,
                Fields = new List<ModuleField>
                {
                    Key("ID", "شناسه", FieldKind.Number, "orm"),
                    Number("BadgeNumber", "شماره پرسنلی", "orm", required: true),
                    Text("GroupName", "نام گروه", 100, "orm", required: true),
                    Number("OrderIndex", "ترتیب", "orm")
                }
            });

            modules.Add(new ModuleDescriptor
            {
                Code = "title",
                TitleFa = "عناوین شغلی",
                GroupFa = "سازمان",
                Table = "TitlesChart",
                Catalog = "personnel",
                OrderBy = "TitleName",
                LabelColumn = "TitleName",
                ViewFeature = AdminFeature.TitlesView,
                EditFeature = AdminFeature.TitlesEdit,
                AllowCreate = true,
                // Deleting a title that still has personnel or a workflow step would
                // orphan both; the delete guard lives in TitlesController.Until then the
                // module stays create/edit only.
                AllowDelete = false,
                NoteFa = "چارت عناوین؛ تغییر والد با همان قاعده ضدحلقه واحدها بررسی می‌شود.",
                Fields = new List<ModuleField>
                {
                    Key("TitleID", "شناسه", FieldKind.Number, "ddl", identity: true),
                    Text("TitleName", "عنوان", 100, "ddl", required: true),
                    Text("DisplayName", "عنوان نمایشی", 100, "ddl"),
                    Number("UpperTitleID", "عنوان بالاتر", "ddl"),
                    Text("Duties", "شرح وظایف", 500, "ddl"),
                    Text("Certs", "مدارک لازم", 500, "ddl"),
                    Text("Address", "نشانی", 200, "ddl"),
                    Text("EmailAddress", "ایمیل", 100, "ddl"),
                    Flag("Observer", "ناظر", "ddl")
                }
            });

            return modules;
        }
    }
}
