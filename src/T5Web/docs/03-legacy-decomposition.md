# ۰۳ — تجزیه نسخه قدیمی (Legacy Decomposition)

این سند «درخت مرجع» پورتال است: از چه چیزی بازنویسی می‌کنیم، هر تکه‌اش کجاست، و در
نسخه جدید کدام صفحه/مدل/منبع داده جایش را می‌گیرد. آریا برای `TWF` یک درخت
`OriginalDecompiled/` داشت؛ برای T5Web کد منبع C# وجود ندارد، پس شاهد ما
**فایل‌های نما + متادیتای اسمبلی** است و همه اعداد این سند ماشینی استخراج شده‌اند.

## ۱. شاهدها (چه چیزی واقعاً در دست داریم)

| شاهد | مسیر | آنچه اثبات می‌کند |
| :-- | :-- | :-- |
| نماهای Razor | `T5Web [3.5.7]/App/Views/` — 112 فایل `*.cshtml` در 66 پوشه | ساختار صفحه‌ها، فیلدهای فرم، متن فارسی، کتابخانه‌های سمت مرورگر |
| اسمبلی وب | `T5Web [3.5.7]/App/bin/T5V3Web.dll` — **۶۴ کنترلر، ۳۳۱ کنش عمومی** | فهرست دقیق قابلیت‌ها (متادیتای `TypeDef`/`MethodDef`) |
| لایه داده موتور | `T5Web [3.5.7]/App/bin/T5V3.DB.dll` + `Personnel.dll` | نام واقعی جدول‌ها و DAها؛ مبنای `engine/engine-pin.json` |
| محاسبات | `T5V3.Attendance.dll`, `T5V3.LeaveCalcs.dll` | قواعد حضور و مرخصی که **باید** از موتور صدا زده شوند، نه بازنویسی |
| گزارش | `T5V3.Report.dll`, `T5V3.ReportGenerator.dll` | گزارش‌ساز گرافیکی قدیمی (حذف در زیرفاز D) |
| اسکریپت‌های سخت‌افزار | `Link5 v4.0.4/*.sql` (UTF-16LE) | اسکیمای پایگاه‌داده دستگاه/تردد |
| رشته‌های اتصال | `Web.config` نسخه قدیمی: `T5CnnStr`, `PersonnelCnnStr` | دو پایگاه‌داده: `ReportT5V3` و `Personnel` |

> **نکته روش:** هیچ‌کدام از این شاهدها «کد منبع» نیست. بنابراین هر ادعای این سند یا از
> متادیتای اسمبلی است یا از فایل نما؛ هیچ‌جا «حدس» به‌عنوان واقعیت نوشته نشده است.
> جای حدس، `SchemaMap` و `schema-manifest.json` نشسته‌اند.

## ۲. موجودی اسمبلی‌های نسخه قدیمی

| اسمبلی | نقش | در بازنویسی |
| :-- | :-- | :-- |
| `T5V3Web.dll` | ۶۴ کنترلر + ۲۴ پارشال + زیرساخت MVC | **جایگزین می‌شود** (`T5Web.Web`) |
| `T5V3.DB.dll` | DA جداول موتور (`EntityManagers.*DA`) | **قفل‌شده و مصرف می‌شود** |
| `Personnel.dll` | `PersonnelSystem`, `Personnel.DB.DBManager`, `Personnel.Model.*` | **قفل‌شده و مصرف می‌شود** (خواندن) |
| `T5V3.Attendance.dll` | محاسبه حضور | **مصرف می‌شود** (بدون بازنویسی) |
| `T5V3.LeaveCalcs.dll` | محاسبه مرخصی/مانده | **مصرف می‌شود** (بدون بازنویسی) |
| `T5V3.Model.dll` | مدل‌های مشترک موتور | مصرف می‌شود |
| `T5V3.Report*.dll` | گزارش‌ساز گرافیکی + `ReportViewer` | **حذف** (زیرفاز D: گزارش سمت سرور + CSV) |
| `EntityFramework.dll` | ORM نسخه قدیمی | **حذف** (پورتال Dapper همگام دارد) |
| `PersianDate.dll` | تبدیل تاریخ | **حذف** (`PersianCalendar` چارچوب) |
| `Interop.TINYLib.dll`, `T5V3.ZK.DLL`, `Push.dll`, `Sensor.dll` | قفل سخت‌افزاری و SDK بومی ۳۲-بیتی | **حذف کامل** (اصل ۲) |

## ۳. نگاشت ۶۴ کنترلر (شاهد: `T5V3Web.dll`)

ستون «کنش» تعداد متدهای عمومی هر کنترلر است — یعنی اندازه واقعی قابلیت، نه حدس.
«خانه جدید» یا کنترلر موجود است، یا ماژول برنامه‌ریزی‌شده در فاز ۵، یا حذفِ مستندشده.

| # | کنترلر قدیمی | کنش | خانه جدید | منبع داده | وضعیت |
| --: | :-- | --: | :-- | :-- | :-- |
| 1 | `Account` | 3 | `AccountController` (ورود/خروج ویندوزی) | `UserInfo.UserName`/`WebUser` | ✅ فاز ۱ |
| 2 | `AssignRule` | 7 | ماژول «قواعد حضور» | جداول قاعده موتور | ⏳ C |
| 3 | `Calendar` | 10 | ماژول «تقویم و تعطیلات» | جداول تقویم/تعطیلات موتور | ⏳ B |
| 4 | `Card` | 8 | ماژول «مانده مرخصی» | جداول مانده مرخصی موتور | ⏳ C |
| 5 | `CardexReport` | 3 | جایگزین: کاردکس پرسنل | `Record` | ⏳ D |
| 6 | `Certificate` | 6 | ماژول «داده پایه»: مدارک | `CertificateDA` | ⏳ B |
| 7 | `ChangeLock` | 2 | حذف (قفل سخت‌افزاری) | — | ⛔ Zero-Lock |
| 8 | `CustomWF` | 6 | `WorkflowController` (گردش کار سفارشی) | `Workflow` | ✅ فاز ۲ / ⏳ C |
| 9 | `DailyReport` | 3 | جایگزین: گزارش روزانه | `Record` | ⏳ D |
| 10 | `Dashboard` | 4 | `DashboardController` (شمارش عملیاتی) | `DashboardDA`, `UserInfo`, `Device` | ✅ فاز ۱ |
| 11 | `DashboardItem` | 4 | داشبورد قابل‌تنظیم (زیرفاز B) | `DashboardItemDA` | ⏳ B |
| 12 | `Department` | 6 | `DepartmentController` (درخت + جلوگیری از حلقه) | `DepartmentDA` | ✅ فاز ۲ |
| 13 | `DeptsChart` | 4 | ادغام در `DepartmentController` (نمودار استخدام/برکناری) | `DepartmentDA`, `UserInfo.HiredDate/FiredDate` | ✅ فاز ۲ |
| 14 | `Deputy` | 3 | ماژول «جانشین» | جداول جانشین موتور | ⏳ B |
| 15 | `Device` | 5 | `DeviceController` (پیکربندی دستگاه) | `DeviceDA` | ✅ فاز ۳ |
| 16 | `DeviceOperations` | 34 | `DeviceController`/`DeviceOpsController` (همگام‌سازی و دستور) | `DeviceMessageDA`, `DeviceOperationLogDA`, `UserDeviceValids`, `UserDeviceUploads` | ⏳ E |
| 17 | `EditLateEarly` | 2 | ماژول «اصلاح دستی تردد» | `Record` | ⏳ C |
| 18 | `EmpType` | 6 | ماژول «داده پایه»: انواع استخدام | `EmpTypeDA` | ⏳ B |
| 19 | `ExitReport` | 3 | جایگزین: گزارش خروج/مأموریت | `ExitType` | ⏳ D |
| 20 | `ExitType` | 4 | ماژول «انواع خروج/مأموریت» | `ExitType` | ⏳ B |
| 21 | `Export` | 2 | `ReportExportController` (CSV، بدون فایل‌ساز قابل حدس) | — | ⏳ D |
| 22 | `Gate` | 4 | `DeviceController` (درب‌ها) + ماژول درب | `GateDA` | ✅ فاز ۳ (خواندن) / ⏳ B (نوشتن) |
| 23 | `GridReport` | 2 | جایگزین: گزارش جدولی سمت سرور | `Record` | ⏳ D |
| 24 | `GroupAssignShift` | 6 | ماژول «اختصاص گروهی شیفت» | جداول شیفت/برنامه موتور | ⏳ C |
| 25 | `Home` | 10 | `DashboardController` + `DiagnosticsController` | `UserInfo`, `Device`, `T5WebAudit` | ✅ فاز ۱ |
| 26 | `ImportFromExcel` | 4 | ماژول «ورود از اکسل» (اعتبارسنجی سطر‌به‌سطر) | `UserInfo` | ⏳ E |
| 27 | `InsertDailyLeave` | 8 | ماژول «مرخصی روزانه» | جداول مرخصی موتور | ⏳ C |
| 28 | `InsertRecord` | 2 | ماژول «اصلاح دستی تردد» (با ثبت حسابرسی) | `Record` + `T5WebAudit` | ⏳ C |
| 29 | `LateEarlyReport` | 3 | جایگزین: گزارش تأخیر/تعجیل | `Record` | ⏳ D |
| 30 | `LeaveDescription` | 6 | ماژول «انواع مرخصی» (شرح‌ها) | `LeaveDescription` | ⏳ B |
| 31 | `LeaveType` | 6 | ماژول «انواع مرخصی» | `LeaveType` | ⏳ B |
| 32 | `Manager` | 4 | ادغام در ماژول جانشین/چارت | جداول جانشین موتور | ⏳ B |
| 33 | `OverTimeReport` | 3 | جایگزین: گزارش اضافه‌کاری | `Record` | ⏳ D |
| 34 | `Parameters` | 4 | ماژول «پارامترهای سامانه» (فقط مدیر پورتال) | جدول پارامترهای موتور | ⏳ B |
| 35 | `Permit` | 6 | ماژول «مجوز/مرخصی ساعتی» | جداول مجوز موتور | ⏳ C |
| 36 | `PersonnelStatus` | 3 | `PersonnelController` + داشبورد | `UserInfo` | ✅ فاز ۲ |
| 37 | `PersonnelStatusReport` | 3 | جایگزین: گزارش وضعیت پرسنل | `UserInfo` | ⏳ D |
| 38 | `Ramezan` | 6 | ادغام در ماژول تقویم (ماه رمضان) | جداول تقویم موتور | ⏳ B |
| 39 | `Records` | 2 | ماژول «ترددها» (نمایش و فیلتر) | `Record` | ⏳ C |
| 40 | `RemainedLeavesReport` | 3 | جایگزین: گزارش مانده مرخصی | جداول مرخصی موتور | ⏳ D |
| 41 | `ReportDatas` | 6 | حذف (زیرساخت گزارش‌ساز گرافیکی) | — | ⛔ زیرفاز D |
| 42 | `ReportDesign` | 8 | حذف (گزارش‌ساز گرافیکی؛ جایگزین: گزارش سمت سرور) | — | ⛔ زیرفاز D |
| 43 | `ReportFilter` | 6 | حذف (گزارش‌ساز گرافیکی) | — | ⛔ زیرفاز D |
| 44 | `ReportTablix` | 6 | حذف (گزارش‌ساز گرافیکی) | — | ⛔ زیرفاز D |
| 45 | `ReportTablixColumn` | 6 | حذف (گزارش‌ساز گرافیکی) | — | ⛔ زیرفاز D |
| 46 | `ReportTextbox` | 6 | حذف (گزارش‌ساز گرافیکی) | — | ⛔ زیرفاز D |
| 47 | `ReportTitles` | 6 | حذف (فهرست گزارش‌های گرافیکی) | — | ⛔ زیرفاز D |
| 48 | `RequestsReport` | 3 | جایگزین: گزارش درخواست‌ها | `Request` | ⏳ D |
| 49 | `Schedule` | 6 | ماژول «برنامه شیفت» | جداول شیفت/برنامه موتور | ⏳ C |
| 50 | `ShowReport` | 3 | جایگزین: `ReportsController` (گزارش روزانه) | `Record`, `UserInfo` | ⏳ D |
| 51 | `ShowUsersRequest` | 3 | ماژول «درخواست‌های پرسنل» | `Request`, `RequestAccept` | ⏳ C |
| 52 | `SumReport` | 3 | جایگزین: گزارش جمع‌بندی | `Record` | ⏳ D |
| 53 | `TempSchedules` | 3 | ماژول «برنامه موقت» | جداول شیفت/برنامه موتور | ⏳ C |
| 54 | `Title` | 9 | ماژول «عناوین شغلی» | `TitleChartDA` | ⏳ B |
| 55 | `TotalControl` | 5 | ماژول «کنترل کل» (اصلاح گروهی با حسابرسی) | `Record` + `T5WebAudit` | ⏳ C |
| 56 | `TransferRemainedLeave` | 3 | ماژول «انتقال مانده به سال بعد» | جداول مانده مرخصی موتور | ⏳ C |
| 57 | `USBManagement` | 2 | حذف (وابسته به دانگل/قفل سخت‌افزاری) | — | ⛔ Zero-Lock |
| 58 | `UserAddProperty` | 6 | ماژول «فیلدهای اضافی پرسنل» | `PropertyDA`, `PropertyDropdownValueDA`, `UserInfoProperties` | ⏳ B |
| 59 | `UserInfo` | 12 | `PersonnelController` (جست‌وجو/ویرایش/انتقال) | `UserInfoDA`, `UserInfoTitlesDA` | ✅ فاز ۲ |
| 60 | `UserReport` | 2 | جایگزین: گزارش پرسنلی (تکی/گروهی) | `UserInfo` | ⏳ D |
| 61 | `UserSelection` | 7 | کامپوننت انتخاب کاربر (نه صفحه) | `UserInfo` | ⏳ B |
| 62 | `ViewSystemLogs` | 1 | `AuditController` (زنجیره هش خود پورتال) | `T5WebAudit` | ✅ فاز ۱ |
| 63 | `Workflow` | 6 | `WorkflowController` (زنجیره با اعتبارسنجی) | `Workflow` | ✅ فاز ۲ |
| 64 | `iclock` | 3 | نقطه پایانی push دستگاه (`/iclock/cdata`) | `Device`, `Record` | ⏳ E |

### جمع‌بندی نگاشت

| دسته | تعداد کنترلر |
| :-- | --: |
| ✅ پیاده‌شده در فازهای ۱–۴ | ۱۲ |
| ⏳ زیرفاز B (داده پایه و سازمان) | ۱۴ |
| ⏳ زیرفاز C (قواعد، شیفت، تردد، گردش کار) | ۱۳ |
| ⏳ زیرفاز D (گزارش‌ها) | ۱۳ |
| ⏳ زیرفاز E (دستگاه، push، اکسل) | ۳ |
| ⛔ حذف مستندشده (قفل سخت‌افزاری / گزارش‌ساز گرافیکی) | ۹ |
| **جمع** | **۶۴** |

## ۴. بدهی فنی نسخه قدیمی که در بازنویسی پرداخت می‌شود

| بدهی | شاهد | درمان |
| :-- | :-- | :-- |
| قالب Bootstrap 3 + `T5V3Web.Content.Persian.*` با تصاویر و فونت‌های محلی سنگین | `App/Content/` | CSS دست‌نویس `t5web-admin.css` (۴۳۸ سطر) |
| jQuery و اسکریپت درون‌خطی در نماها | `Views/*/Index.cshtml` | `t5web-admin.js` + `data-*` + CSP بدون `unsafe-inline` |
| فرم ورود با رمز (`Account.Login`) | `AccountController` (۳ کنش) | حذف؛ احراز هویت ویندوز IIS/Negotiate |
| یک صفحه برای هر عمل (`Edit/Delete/DeleteAll` در هر کنترلر) | الگوی تکرارشده در ۴۰+ کنترلر | یک الگوی مشترک: فهرست + مدال ویرایش + تأیید حذف |
| گزارش‌ساز گرافیکی با طراح در مرورگر | ۸ کنترلر `Report*` | گزارش سمت سرور با پارامتر و خروجی CSV |
| «حذف همه» بی‌ردپا (`DeleteAll`) | ۲۲ کنترلر | حذف گروهی فقط با `data-confirm` + یک سطر حسابرسی برای هر رکورد |
| SDK بومی ۳۲-بیتی برای ارتباط با دستگاه | `T5V3.ZK.DLL`, `Push.dll` | نقطه پایانی `iclock` سمت سرور (زیرفاز E)؛ بدون DLL بومی در IIS |
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

* فهرست ماشینی کنترلرها: `docs/legacy-controllers.txt` (۶۴ سطر، بدون هدر).
* برنامه زیرفازها: `docs/06-phase-plan.md`.
* دفتر باگ‌ها: `docs/04-bugs.md`.
* اثبات پوشش: آزمون `T13` (۶۴/۶۴ در `docs/05-t5web-rewrite-plan.md`) و آزمون `T25`
  در پایان زیرفاز F.
