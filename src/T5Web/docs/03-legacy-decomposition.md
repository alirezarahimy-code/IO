# ۰۳ — تجزیه نسخه قدیمی (Legacy Decomposition)

این سند «درخت مرجع» پورتال است: از چه چیزی بازنویسی می‌کنیم، هر تکه‌اش کجاست، و در
نسخه جدید کدام صفحه/مدل/منبع داده جایش را می‌گیرد. آریا برای `TWF` یک درخت
`OriginalDecompiled/` داشت؛ برای T5Web کد منبع C# وجود ندارد، پس شاهد ما
**فایل‌های نما + متادیتای اسمبلی** است و همه اعداد این سند ماشینی استخراج شده‌اند.

## ۱. شاهدها (چه چیزی واقعاً در دست داریم)

| شاهد | مسیر | آنچه اثبات می‌کند |
| :-- | :-- | :-- |
| نماهای Razor | `T5Web [3.5.7]/App/Views/` — ۱۱۲ فایل `*.cshtml` در ۶۵ پوشه (۶۴ کنترلر + `Shared`) | ساختار صفحه‌ها، فیلدهای فرم، متن فارسی، کتابخانه‌های سمت مرورگر |
| اسمبلی وب | `T5Web [3.5.7]/App/bin/T5V3Web.dll` — **۶۷ کنترلر، ۳۴۳ کنش عمومی** | فهرست دقیق قابلیت‌ها (متادیتای `TypeDef`/`MethodDef`) |
| لایه داده موتور | `T5Web [3.5.7]/App/bin/T5V3.DB.dll` + `Personnel.dll` | نام واقعی جدول‌ها و DAها؛ مبنای `engine/engine-pin.json` |
| محاسبات | `T5V3.Attendance.dll`, `T5V3.LeaveCalcs.dll` | قواعد حضور و مرخصی که **باید** از موتور صدا زده شوند، نه بازنویسی |
| گزارش | `T5V3.Report.dll`, `T5V3.ReportGenerator.dll` | گزارش‌ساز گرافیکی قدیمی (حذف در زیرفاز D) |
| اسکریپت‌های سخت‌افزار | `Link5 v4.0.4/*.sql` (UTF-16LE) | اسکیمای پایگاه‌داده دستگاه/تردد |
| رشته‌های اتصال | `Web.config` نسخه قدیمی: `t5ConnectionString` → `ReportT5V3`، `personnelConnectionString` → `Personnel` | دو پایگاه‌داده؛ در پورتال جدید `T5CnnStr` و `PersonnelCnnStr` (`docs/09`) |

> **نکته روش:** هیچ‌کدام از این شاهدها «کد منبع» نیست. بنابراین هر ادعای این سند یا از
> متادیتای اسمبلی است یا از فایل نما؛ هیچ‌جا «حدس» به‌عنوان واقعیت نوشته نشده است.
> جای حدس، `SchemaMap` و `schema-manifest.json` نشسته‌اند.

## ۲. موجودی اسمبلی‌های نسخه قدیمی

| اسمبلی | نقش | در بازنویسی |
| :-- | :-- | :-- |
| `T5V3Web.dll` | ۶۷ کنترلر + ۲۴ پارشال + زیرساخت MVC | **جایگزین می‌شود** (`T5Web.Web`) |
| `T5V3.DB.dll` | DA جداول موتور (`EntityManagers.*DA`) | **قفل‌شده و مصرف می‌شود** |
| `Personnel.dll` | `PersonnelSystem`, `Personnel.DB.DBManager`, `Personnel.Model.*` | **قفل‌شده و مصرف می‌شود** (خواندن) |
| `T5V3.Attendance.dll` | محاسبه حضور | **مصرف می‌شود** (بدون بازنویسی) |
| `T5V3.LeaveCalcs.dll` | محاسبه مرخصی/مانده | **مصرف می‌شود** (بدون بازنویسی) |
| `T5V3.Model.dll` | مدل‌های مشترک موتور | مصرف می‌شود |
| `T5V3.Report*.dll` | گزارش‌ساز گرافیکی + `ReportViewer` | **حذف** (زیرفاز D: گزارش سمت سرور + CSV) |
| `EntityFramework.dll` | ORM نسخه قدیمی | **حذف** (پورتال Dapper همگام دارد) |
| `PersianDate.dll` | تبدیل تاریخ | **حذف** (`PersianCalendar` چارچوب) |
| `TC5.dll` | ماشین‌حساب حضور سمت وب (`TC5.Attendance`: `Calculate`، `ProcessedRecord`، `GetActiveRule`، `GetAutoSchedule`، `GetTempSchedule`) + `UserInfoDA`/`DepartmentDA` | **مصرف نمی‌شود**: پورتال هیچ عدد محاسباتی نمی‌سازد و این اسمبلی جزء بیلد قفل‌شده موتور نیست؛ عدد رسمی در دسکتاپ دیده می‌شود (`docs/02` فاز C) |
| `TMisc5.dll` | تبدیل تاریخ هجری (`ToHijriDate`، `DateConverter`) | **حذف** (`PersianCalendar` چارچوب، `T6`/`T17`) |
| `Interop.TINYLib.dll`, `T5V3.ZK.DLL`, `Push.dll`, `Sensor.dll` | قفل سخت‌افزاری و SDK بومی ۳۲-بیتی | **حذف کامل** (اصل ۲) |

## ۳. نگاشت ۶۷ کنترلر (شاهد: `T5V3Web.dll`)

ستون «کنش» تعداد متدهای عمومی هر کنترلر است — یعنی اندازه واقعی قابلیت، نه حدس.
«خانه جدید» یا کنترلر موجود است، یا ماژول برنامه‌ریزی‌شده در فاز ۵، یا حذفِ مستندشده.

| # | کنترلر قدیمی | کنش | خانه جدید | منبع داده | وضعیت |
| --: | :-- | --: | :-- | :-- | :-- |
| 1 | `Account` | 2 | `AccountController` (ورود/خروج ویندوزی) | `UserInfo.UserName`/`WebUser` | ✅ فاز ۱ |
| 2 | `AssignRule` | 7 | ماژول «قواعد حضور» | جداول قاعده موتور | ✅ ۵-C (`RulesController`, `T19`) |
| 3 | `AssignShift` | 6 | `ScheduleController` + ماژول `usershift` (تخصیص تک‌نفره؛ `AutoSch` حذف شد — الگوریتم زمان‌بندی خودکار مال موتور است) | `UserShift`, `Shift`, `Schedule` | ✅ ۵-C (`ScheduleController`, `T22`) |
| 4 | `Calendar` | 10 | ماژول «تقویم و تعطیلات» | جداول تقویم/تعطیلات موتور | ✅ ۵-B (ماژول `calendar`), `T16` |
| 5 | `Card` | 8 | ماژول «مانده مرخصی» | جداول مانده مرخصی موتور | ✅ ۵-C (`LeaveController`, `T21`) |
| 6 | `CardexReport` | 3 | جایگزین: کاردکس پرسنل | `Record` | ✅ ۵-D (`ReportsController`, `T23`) |
| 7 | `Certificate` | 6 | ماژول «داده پایه»: مدارک | `CertificateDA` | ✅ ۵-B (ماژول `certificate`), `T16` |
| 8 | `ChangeLock` | 2 | حذف (قفل سخت‌افزاری) | — | ⛔ Zero-Lock |
| 9 | `CustomWF` | 6 | `WorkflowController` (گردش کار سفارشی) | `Workflow` | ✅ ۵-C (ماژول `customworkflow` + گارد زنجیره, `T20`) |
| 10 | `DailyReport` | 3 | جایگزین: گزارش روزانه | `Record` | ✅ ۵-D (`ReportsController`, `T23`) |
| 11 | `Dashboard` | 4 | `DashboardController` (شمارش عملیاتی) | `DashboardDA`, `UserInfo`, `Device` | ✅ فاز ۱ |
| 12 | `DashboardItem` | 4 | داشبورد قابل‌تنظیم (زیرفاز B) | `DashboardItemDA` | ✅ ۵-B (ماژول `dashboard`), `T16` |
| 13 | `Department` | 6 | `DepartmentController` (درخت + جلوگیری از حلقه) | `DepartmentDA` | ✅ فاز ۲ |
| 14 | `DeptsChart` | 4 | ادغام در `DepartmentController` (نمودار استخدام/برکناری) | `DepartmentDA`, `UserInfo.HiredDate/FiredDate` | ✅ فاز ۲ |
| 15 | `Deputy` | 3 | ماژول «جانشین» | جداول جانشین موتور | ✅ ۵-B (ماژول `deputy`), `T16` |
| 16 | `Device` | 5 | `DeviceController` (پیکربندی دستگاه) | `DeviceDA` | ✅ فاز ۳ |
| 17 | `DeviceOperations` | 33 | `DeviceController`/`DeviceOpsController` (همگام‌سازی و دستور) | `DeviceMessageDA`, `DeviceOperationLogDA`, `UserDeviceValids`, `UserDeviceUploads` | ✅ ۵-E (`DeviceController` — مجموعه‌دستور بسته, `T26`) |
| 18 | `EditLateEarly` | 2 | ماژول «اصلاح دستی تردد» | `Record` | ✅ ۵-C (`RecordsController.Correct`, `T20`) |
| 19 | `EmpType` | 6 | ماژول «داده پایه»: انواع استخدام | `EmpTypeDA` | ✅ ۵-B (ماژول `emptype`), `T16` |
| 20 | `ExitReport` | 3 | جایگزین: گزارش خروج/مأموریت | `ExitType` | ✅ ۵-D (`ReportsController`, `T23`) |
| 21 | `ExitType` | 4 | حذف (جدول در اسکیمای موتور نیست؛ پرچم `LeaveType.DutyOrLeave` جایش را گرفت) | `LeaveType` | ⛔ جدول `ExitType` در اسکیمای موتور نیست؛ پرچم `LeaveType.DutyOrLeave` جایش را گرفته |
| 22 | `Export` | 2 | `ReportExportController` (CSV، بدون فایل‌ساز قابل حدس) | — | ✅ ۵-D (`ReportsController.Export`, `T23`) |
| 23 | `Gate` | 4 | `DeviceController` (درب‌ها) + ماژول درب | `GateDA` | ✅ ۵-B (ماژول `gate`), `T16` |
| 24 | `GridReport` | 2 | جایگزین: گزارش جدولی سمت سرور | `Record` | ✅ ۵-D (`ReportsController`, `T23`) |
| 25 | `GroupAssignShift` | 6 | ماژول «اختصاص گروهی شیفت» | جداول شیفت/برنامه موتور | ✅ ۵-C (`ScheduleController` — تخصیص تک‌نفره, `T22`) |
| 26 | `Home` | 9 | `DashboardController` + `DiagnosticsController` | `UserInfo`, `Device`, `T5WebAudit` | ✅ فاز ۱ |
| 27 | `ImportFromExcel` | 4 | ماژول «ورود از اکسل» (اعتبارسنجی سطر‌به‌سطر) | `UserInfo` | ✅ ۵-E (`ImportController` — CSV آفلاین, `T24`) |
| 28 | `InsertDailyLeave` | 8 | ماژول «مرخصی روزانه» | جداول مرخصی موتور | ✅ ۵-C (`LeaveController.Grant`, `T21`) |
| 29 | `InsertRecord` | 2 | ماژول «اصلاح دستی تردد» (با ثبت حسابرسی) | `Record` + `T5WebAudit` | ✅ ۵-C (`RecordsController.Insert`, `T20`) |
| 30 | `LateEarlyReport` | 3 | جایگزین: گزارش تأخیر/تعجیل | `Record` | ✅ ۵-D (`ReportsController`, `T23`) |
| 31 | `LeaveDescription` | 6 | ماژول «انواع مرخصی» (شرح‌ها) | `LeaveDescription` | ✅ ۵-B (نمایش در ماژول `dailyLeaf`؛ متن بدون شاهد نوع، فقط‌خواندنی), `T16` |
| 32 | `LeaveType` | 6 | ماژول «انواع مرخصی» | `LeaveType` | ✅ ۵-B (ماژول `leavetype`), `T16` |
| 33 | `Manager` | 4 | ادغام در ماژول جانشین/چارت | جداول جانشین موتور | ✅ ۵-B (ادغام در ماژول `deputy`), `T16` |
| 34 | `OverTimeReport` | 3 | جایگزین: گزارش اضافه‌کاری | `Record` | ✅ ۵-D (`ReportsController`, `T23`) |
| 35 | `Parameters` | 4 | ماژول «پارامترهای سامانه» (فقط مدیر پورتال) | جدول پارامترهای موتور | ✅ ۵-B (ماژول `parameter`), `T16` |
| 36 | `Permit` | 6 | ماژول «مجوز/مرخصی ساعتی» | جداول مجوز موتور | ✅ ۵-C (ماژول `hourlyleave` + `LeaveController`, `T21`) |
| 37 | `PersonnelStatus` | 3 | `PersonnelController` + داشبورد | `UserInfo` | ✅ فاز ۲ |
| 38 | `PersonnelStatusReport` | 3 | جایگزین: گزارش وضعیت پرسنل | `UserInfo` | ✅ ۵-D (`ReportsController`, `T23`) |
| 39 | `Ramezan` | 6 | ادغام در ماژول تقویم (ماه رمضان) | جداول تقویم موتور | ✅ ۵-B (ماژول `ramezan`), `T16` |
| 40 | `Records` | 2 | ماژول «ترددها» (نمایش و فیلتر) | `Record` | ✅ ۵-C (`RecordsController`, `T20`) |
| 41 | `RemainedLeavesReport` | 3 | جایگزین: گزارش مانده مرخصی | جداول مرخصی موتور | ✅ ۵-D (`ReportsController`, `T23`) |
| 42 | `ReportDatas` | 6 | حذف (زیرساخت گزارش‌ساز گرافیکی) | — | ⛔ حذف در زیرفاز D (گزارش‌ساز گرافیکی مرورگری قابل نگهداری و آزمون نیست) |
| 43 | `ReportDesign` | 8 | حذف (گزارش‌ساز گرافیکی؛ جایگزین: گزارش سمت سرور) | — | ⛔ حذف در زیرفاز D (گزارش‌ساز گرافیکی مرورگری قابل نگهداری و آزمون نیست) |
| 44 | `ReportFilter` | 6 | حذف (گزارش‌ساز گرافیکی) | — | ⛔ حذف در زیرفاز D (گزارش‌ساز گرافیکی مرورگری قابل نگهداری و آزمون نیست) |
| 45 | `ReportTablix` | 6 | حذف (گزارش‌ساز گرافیکی) | — | ⛔ حذف در زیرفاز D (گزارش‌ساز گرافیکی مرورگری قابل نگهداری و آزمون نیست) |
| 46 | `ReportTablixColumn` | 6 | حذف (گزارش‌ساز گرافیکی) | — | ⛔ حذف در زیرفاز D (گزارش‌ساز گرافیکی مرورگری قابل نگهداری و آزمون نیست) |
| 47 | `ReportTextbox` | 6 | حذف (گزارش‌ساز گرافیکی) | — | ⛔ حذف در زیرفاز D (گزارش‌ساز گرافیکی مرورگری قابل نگهداری و آزمون نیست) |
| 48 | `ReportTitles` | 6 | حذف (فهرست گزارش‌های گرافیکی) | — | ⛔ حذف در زیرفاز D (گزارش‌ساز گرافیکی مرورگری قابل نگهداری و آزمون نیست) |
| 49 | `RequestsReport` | 3 | جایگزین: گزارش درخواست‌ها | `Request` | ✅ ۵-D (`ReportsController`, `T23`) |
| 50 | `Rule` | 6 | `RulesController` + ماژول `rule` | `Rule` | ✅ ۵-C (`RulesController`, `T19`) |
| 51 | `Schedule` | 6 | ماژول «برنامه شیفت» | جداول شیفت/برنامه موتور | ✅ ۵-C (`ScheduleController`, `T22`) |
| 52 | `Shift` | 5 | `ScheduleController` + ماژول `shift` | `Shift` | ✅ ۵-C (`ScheduleController`, `T22`) |
| 53 | `ShowReport` | 3 | جایگزین: `ReportsController` (گزارش روزانه) | `Record`, `UserInfo` | ✅ ۵-D (`ReportsController`, `T23`) |
| 54 | `ShowUsersRequest` | 2 | ماژول «درخواست‌های پرسنل» | `Request`, `RequestAccept` | ✅ ۵-C (`RequestsController`, `T20`) |
| 55 | `SumReport` | 3 | جایگزین: گزارش جمع‌بندی | `Record` | ✅ ۵-D (`ReportsController`, `T23`) |
| 56 | `TempSchedules` | 3 | ماژول «برنامه موقت» | جداول شیفت/برنامه موتور | ✅ ۵-C (ماژول `tempschedule` + `ScheduleController`, `T22`) |
| 57 | `Title` | 9 | ماژول «عناوین شغلی» | `TitleChartDA` | ✅ ۵-B (ماژول `title`), `T16` |
| 58 | `TotalControl` | 5 | ماژول «کنترل کل» (اصلاح گروهی با حسابرسی) | `Record` + `T5WebAudit` | ✅ ۵-C (اصلاح تک‌رکورد با دلیل اجباری — اصلاح گروهی حذف شد, `T20`) |
| 59 | `TransferRemainedLeave` | 3 | ماژول «انتقال مانده به سال بعد» | جداول مانده مرخصی موتور | ✅ ۵-C (`LeaveController.Transfer`, `T21`) |
| 60 | `USBManagement` | 2 | حذف (وابسته به دانگل/قفل سخت‌افزاری) | — | ⛔ Zero-Lock |
| 61 | `UserAddProperty` | 6 | ماژول «فیلدهای اضافی پرسنل» | `PropertyDA`, `PropertyDropdownValueDA`, `UserInfoProperties` | ✅ ۵-B (ماژول‌های `property` / `dropdownvalue` / `userinfoproperty`), `T16` |
| 62 | `UserInfo` | 12 | `PersonnelController` (جست‌وجو/ویرایش/انتقال) | `UserInfoDA`, `UserInfoTitlesDA` | ✅ فاز ۲ |
| 63 | `UserReport` | 2 | جایگزین: گزارش پرسنلی (تکی/گروهی) | `UserInfo` | ✅ ۵-D (`ReportsController`, `T23`) |
| 64 | `UserSelection` | 7 | کامپوننت انتخاب کاربر (نه صفحه) | `UserInfo` | ✅ ۵-B (انتخاب‌گر مشترک در همه ماژول‌ها), `T16` |
| 65 | `ViewSystemLogs` | 1 | `AuditController` (زنجیره هش خود پورتال) | `T5WebAudit` | ✅ فاز ۱ |
| 66 | `Workflow` | 6 | `WorkflowController` (زنجیره با اعتبارسنجی) | `Workflow` | ✅ فاز ۲ |
| 67 | `iclock` | 2 | سرویس دستگاه دسکتاپ (نه پورتال) | `Device`, `Record` | ⛔ خارج از دامنه پورتال: ارتباط بومی دستگاه در سرویس دسکتاپ می‌ماند؛ پورتال داده را از پایگاه می‌خواند |

### جمع‌بندی نگاشت

| دسته | تعداد کنترلر |
| :-- | --: |
| ✅ پیاده‌شده در فازهای ۱–۴ | 10 |
| ✅ زیرفاز B (داده پایه و سازمان) | 14 |
| ✅ زیرفاز C (قواعد، شیفت، تردد، گردش کار) | 17 |
| ✅ زیرفاز D (گزارش‌ها) | 13 |
| ✅ زیرفاز E (دستگاه و ورود داده) | 2 |
| ⛔ حذف یا خارج از دامنه (قفل، گزارش‌ساز گرافیکی، `iclock`، `ExitType`) | 11 |
| **جمع** | **67** |

منبع اعداد این جدول ماشینی است: `tools/legacy-coverage.py` کنترلرها و **کنش‌های عمومی** را از
`T5V3Web.dll` می‌خواند (۶۷ کنترلر / ۳۴۳ کنش) و فهرست کنش‌به‌کنش در `docs/legacy-actions.csv` است.


## ۴. بدهی فنی نسخه قدیمی که در بازنویسی پرداخت می‌شود

| بدهی | شاهد | درمان |
| :-- | :-- | :-- |
| قالب Bootstrap 3 + `T5V3Web.Content.Persian.*` با تصاویر و فونت‌های محلی سنگین | `App/Content/` | CSS دست‌نویس `t5web-admin.css` (۴۳۸ سطر) |
| jQuery و اسکریپت درون‌خطی در نماها | `Views/*/Index.cshtml` | `t5web-admin.js` + `data-*` + CSP بدون `unsafe-inline` |
| فرم ورود با رمز (`Account.Login`) | `AccountController` (۲ کنش) | حذف؛ احراز هویت ویندوز IIS/Negotiate |
| یک صفحه برای هر عمل (`Edit/Delete/DeleteAll` در هر کنترلر) | الگوی تکرارشده در ۳۱ کنترلر | یک الگوی مشترک: فهرست + مدال ویرایش + تأیید حذف |
| گزارش‌ساز گرافیکی با طراح در مرورگر | ۷ کنترلر `Report*` (۴۴ کنش) | گزارش سمت سرور با پارامتر و خروجی CSV |
| «حذف همه» بی‌ردپا (`DeleteAll`) | ۲۷ کنترلر | حذف گروهی فقط با `data-confirm` + یک سطر حسابرسی برای هر رکورد |
| `JOIN` بین دو کاتالوگ در یک دستور (۷ مورد: نام پرسنل/دستگاه/واحد کنار تردد و مرخصی) | دو کلید اتصال `T5CnnStr` + `PersonnelCnnStr` در `Web.config` | هر دستور یک کاتالوگ؛ ستون‌های نمایشی با `PersonnelDirectory` در حافظه وصل می‌شوند و `db/03-relations.sql` سلامت رابطه‌ها را می‌سنجد (`docs/09`، `BUG-28`) |
| فراخوانی HTTP به سرویس دستگاه (`Link5ServiceURL = http://127.0.0.1:15222/Device/`) | `Web.config` نسخه قدیمی | پورتال هیچ درخواست بیرونی نمی‌فرستد: دستور در صف `[T5WebDeviceCommand]` ثبت می‌شود و سرویس دسکتاپ آن را برمی‌دارد (`docs/08` بند ۵-الف، `BUG-26`) |
| SDK بومی ۳۲-بیتی برای ارتباط با دستگاه | `T5V3.ZK.DLL`, `Push.dll` | هیچ DLL بومی در IIS بارگذاری نمی‌شود؛ ارتباط دستگاه در سرویس دسکتاپ می‌ماند و پورتال فقط `Device`/`Record` را می‌خواند و دستور را در صف می‌گذارد |
| تاریخ شمسی دستی | `PersianDate.dll` | `PersianCalendar` (`BUG-12`) |

## ۵. آنچه عمداً بازنویسی **نمی‌شود**

1. **محاسبه حضور و مرخصی.** `T5V3.Attendance`/`LeaveCalcs` قفل‌شده و بایت‌به‌بایت با آنچه
   `TWF 4.28` استفاده می‌کند یکسان است. بازنویسی آن یعنی دو الگوریتم موازی برای یک عدد
   قانونی (مانده مرخصی) — ریسک غیرقابل قبول.
2. **قفل سخت‌افزاری.** طبق تصمیم مدیریت موقتاً حذف می‌شود؛ دو کنترلر مرتبط حذف شده‌اند.
3. **گزارش‌ساز گرافیکی.** یک محصول جداگانه است (`T5V3.ReportGenerator`) و در پورتال وب
   فقط به‌صورت `ReportViewer` ظاهر می‌شد. زیرفاز D جایگزین بومی می‌سازد.
4. **مدیریت رمز/اثر انگشت/عکس پرسنل.** نوشتن این فیلدها در وب (که سرویس بومی دستگاه را
   ندارد) فقط می‌تواند داده را خراب کند؛ در پورتال فقط خوانده می‌شود.

## ۶. پیوند با برنامه

* فهرست ماشینی کنترلرها: `docs/legacy-controllers.txt` (۶۷ سطر، با یک سطر توضیح در ابتدا).
* برنامه زیرفازها: `docs/06-phase-plan.md`.
* دفتر باگ‌ها: `docs/04-bugs.md`.
* اثبات پوشش: آزمون `T13` (۶۷/۶۷ در `docs/05-t5web-rewrite-plan.md`) و آزمون `T25`
  در پایان زیرفاز F.
