# ۰۵ — نقشه بازنویسی پورتال T5Web (۶۷ کنترلر / ۳۴۳ کنش قدیمی → ماژول‌های جدید)

**تاریخ:** ۱۴۰۴/۰۸/۰۳
**پیش‌نیاز:** `docs/04-t5web-vs-t5v3-orchestration-fit.md` و `docs/03-engine-compatibility-verdict.md` (در پروژه ARIA)
**زمان‌بندی اجرا:** `docs/06-phase-plan.md` · **وضعیت زنده:** `docs/phase-status.csv`

---

## ۱. چرا بازنویسی، و چرا مستقل

`T5V3Web.dll` نسخه ۳.۵.۷ شصت‌وهفت کنترلر و ۳۴۳ کنش عمومی دارد، همه در یک اسمبلی بدون سورس، با
Kendo/jQuery از CDN و بدون هیچ آزمونی. دسترسی‌ها در یک مدل تخت با حدود چهل بولی
(`MenusAccessModel`) نگه‌داری می‌شوند، یعنی «چه کسی چه کاری می‌تواند بکند» نه قابل
آزمون است و نه قابل حسابرسی.

این پورتال باید سال‌ها در شبکه بسته سازمان اجرا شود، پس سه چیز لازم دارد که در نسخه
فعلی نیست:

1. **سورس واقعی** — تغییر یک برچسب نباید به دی‌کامپایل نیاز داشته باشد؛
2. **آفلاین کامل** — بدون CDN، بدون فونت وب، بدون فراخوانی بیرونی؛
3. **دسترسی قابل حسابرسی** — هر قابلیت یک شناسه پایدار و هر تغییر یک سطر در دفتر رخداد.

پورتال **مستقل از آریا** است: اسمبلی، جلسه، کوکی، مسیر انتشار و لاگین SQL جدا.
تنها اشتراک، پایگاه‌داده T5 است (بندهای R1–R8 در `docs/02-shared-database-contract.md`).

---

## ۲. وضعیت پیاده‌سازی — فاز A

| بخش | وضعیت | جایگاه |
|---|---|---|
| احراز هویت ویندوز (Negotiate) و تطبیق با `UserInfo.WebUser` | ✅ | `T5Web.Web/Security/AdminAuthorize.cs` |
| دروازه دسترسی بر پایه قابلیت (پیش‌فرض ممنوع، «ممنوع» همیشه برنده) | ✅ | `T5Web.Application/Services/AccessService.cs` |
| دفتر رخداد فقط-افزودنی با زنجیره هش SHA-256 | ✅ | `T5Web.Application/Services/AuditService.cs` |
| پرسنل: جست‌وجو، فیلتر واحد، ویرایش با اعتبارسنجی سمت سرور | ✅ | `Controllers/PersonnelController.cs` |
| چارت سازمانی با جلوگیری از حلقه | ✅ | `Controllers/DepartmentController.cs` |
| دستگاه‌ها و درب‌ها (پیکربندی اتصال، IP/پورت اعتبارسنجی‌شده) | ✅ | `Controllers/DeviceController.cs` |
| ماتریس سطح دسترسی | ✅ | `Controllers/AccessController.cs` |
| زنجیره گردش کار با اعتبارسنجی کامل | ✅ | `Controllers/WorkflowController.cs` |
| رخدادها و پایش سامانه | ✅ | `Controllers/AuditController.cs`، `DiagnosticsController.cs` |
| پایگاه‌داده: دو جدول اختصاصی + لاگین کم‌دسترسی | ✅ | `db/01-t5web-objects.sql`، `db/02-t5web-sql-login.sql` |
| آزمون‌ها: ۲۸ مورد، بدون هیچ وابستگی خارجی | ✅ | `T5Web.Tests/Program.cs` |

فاز A همان چیزی است که «مدیر روزمره» لازم دارد: چه کسی هستم، چه اجازه‌ای دارم،
چه کسی در سازمان هست، دستگاه‌ها سالم‌اند یا نه، و چه چیزی تغییر کرده است.

---

## ۳. نقشه ۶۷ کنترلر قدیمی

### فاز A — پایه و عملیات روزمره (پیاده‌سازی شد)

| کنترلر قدیمی | تصمیم |
|---|---|
| `HomeController` | جایگزین: `DashboardController` (ریشه پورتال، بدون صفحه معرفی) |
| `DashboardController` | شمارش‌های عملیاتی + وضعیت دستگاه‌ها + آخرین رخدادها |
| `UserInfoController` | `PersonnelController` (جست‌وجو/ویرایش). نوشتن فیلدهای حساس (عکس، اثر انگشت، رمز) عمداً حذف شد |
| `PersonnelStatusController` | در داشبورد ادغام شد (پرسنل پایان‌یافته/فعال) |
| `DeviceController` | `DeviceController` جدید، فقط فیلدهای قابل پیکربندی انسان |
| `WorkflowController` | `WorkflowController` جدید با اعتبارسنجی زنجیره |
| `ViewSystemLogsController` | `AuditController` روی جدول اختصاصی `T5WebAudit` (جدول `SystemLog` موتور دست‌نخورده می‌ماند) |

### فاز B — داده پایه و ساختار سازمانی

| کنترلر قدیمی | تصمیم |
|---|---|
| `DepartmentController`، `DeptsChartController` | ✅ ادغام در `DepartmentController` جدید (درخت + جلوگیری از حلقه) |
| `TitleController` | ماژول «عناوین شغلی» روی `TitlesChartDA` |
| `CertificateController`، `EmpTypeController` | ماژول «داده پایه» (CRUD ساده، دو جدول کوچک) |
| `LeaveTypeController`، `LeaveDescriptionController` | ماژول «انواع مرخصی» روی `LeaveTypeDA`/`LeaveDescriptionDA` |
| `ExitTypeController` | **حذف**: جدولی به نام `ExitType` در اسکیمای موتور نیست؛ پرچم `LeaveType.DutyOrLeave` همان تفکیک را می‌دهد |
| `CalendarController`، `RamezanController` | ماژول «تقویم و تعطیلات» روی `CalendarDA`/`RamezanDA` |
| `UserAddPropertyController` | ماژول «فیلدهای اضافی پرسنل» روی `PropertyDA` |
| `ParametersController` | ماژول «پارامترهای سامانه» روی `ParameterDA` (فقط مدیر پورتال) |
| `DeputyController`، `ManagerController` | ماژول «جانشین و مدیر» روی `DeputyDA`/`PersonnelDeputyDA` |
| `ScheduleController`، `ShiftController`، `TempSchedulesController` | ماژول «شیفت و برنامه» روی `ScheduleDA`/`ShiftDA`/`ShiftDetailDA`/`TempScheduleDA`/`UserShiftDA` + `ScheduleController` جدید (زیرفاز ۵-C) |
| `AssignShiftController`، `GroupAssignShiftController` | اختصاص شیفت به پرسنل روی `UserShiftDA`؛ اختصاص گروهی با همان اعتبارسنجی بازه/همپوشانی (زیرفاز ۵-C) |
| `RuleController`، `AssignRuleController` | `RulesController` + ماژول `rule`؛ قاعده‌ها لایه‌ای (پرسنل → دپارتمان → عمومی) و تضاد در همان لایه خطاست |
| `CardController`، `TransferRemainedLeaveController` | مانده مرخصی و انتقال سالانه روی `YearRemainDA`/`LeavesAddedDA` |
| `InsertDailyLeaveController`، `InsertRecordController`، `EditLateEarlyController`، `PermitController`، `TotalControlController` | «اصلاح دستی» با ثبت اجباری در دفتر رخداد (هر نوشتن، یک سطر) |
| `ChangeLockController`، `USBManagementController` | حذف قفل سخت‌افزاری؛ در پورتال جدید وجود ندارد (Zero-Lock). خواندن تردد از دانگل USB هم حذف شد: ترددها از پایگاه‌داده می‌آیند |
| `RecordsController`، `UserSelectionController` | در ماژول‌های پرسنل/گزارش ادغام می‌شوند (انتخاب کاربر یک کامپوننت است، نه صفحه) |

### فاز C — گزارش‌ها

پورتال **هیچ** `ReportViewer` و هیچ‌کدام از `T5V3.Report*.dll` را بار نمی‌کند. تصمیم فاز D
(پس از آزمودن وابستگی) این شد: این موتور روی سروری که `ReportViewer` ندارد بالا نمی‌آید و
خروجی‌اش قابل آزمون خودکار نیست. جایگزین بومی: `ReportCatalog` (۱۱ گزارش) + خروجی CSV در
حافظه + نمایش **نام قالب RDLC رسمی** برای اعدادی که فقط موتور محاسبه می‌کند. هیچ عدد
محاسباتی‌ای از خود ساخته نمی‌شود و موتور گزارش هم بازنویسی نشده است.

| کنترلر قدیمی | تصمیم |
|---|---|
| `DailyReportController`، `ExitReportController`، `LateEarlyReportController`، `OverTimeReportController` | پوسته گزارش با فیلترهای استاندارد |
| `PersonnelStatusReportController`، `RemainedLeavesReportController`، `RequestsReportController`، `ShowUsersRequestController` | همان الگو، با کنترل دسترسی `ReportView` |
| `SumReportController`، `GridReportController`، `ShowReportController` | نمایش گزارش‌های از پیش تعریف‌شده (`ReportDA`) |
| `ReportDatasController`، `ReportFilterController`، `ReportTablixController`، `ReportTablixColumnController`، `ReportTextboxController`، `ReportTitlesController`، `ReportDesignController` | **باز نمی‌شود** (۷ کنترلر / ۴۴ کنش): ویرایشگر گرافیکی گزارش در مرورگر؛ دسترسی به‌ازای گزارش با گیت `ReportView` جایگزین شد |
| `CardexReportController`، `UserReportController`، `ExportController` | خروجی Excel/PDF با همان موتور، بدون افزونه بیرونی/CDN |

### فاز D — دستگاه و ورود داده

| کنترلر قدیمی | تصمیم |
|---|---|
| `DeviceOperationsController` | ارسال دستور به دستگاه (فقط با دسترسی `DeviceSync`، هر دستور ثبت می‌شود)؛ مجموعه‌دستور بسته: `sync-users`, `sync-time`, `pull-punches`, `pull-users`, `clear-punches`. حذف بدون تأیید دوگانه (`DeleteAllData`) بازنویسی نمی‌شود |
| `GateController` | مدیریت درب‌ها روی `Gate` |
| `DashboardItemController` | داشبورد قابل تنظیم (اختیاری، پس از نیازسنجی) |
| `ImportFromExcelController` | ورود گروهی پرسنل با پیش‌نمایش و رد سطرهای نامعتبر |
| `AccountController` | در پورتال جدید لازم نیست: احراز هویت ویندوز است و صفحه «عدم دسترسی» جایش را گرفت |

### فاز E — حذف و جایگزینی

| کنترلر قدیمی | تصمیم |
|---|---|
| `CustomWFController` | **حذف**: موتور `3.9.2` کل API آن را برداشته (`CustomWFDA`، `Model.CustomWF`)؛ استفاده از آن، آریا را به بیلد ناسازگار گره می‌زند |
| `ReportDesignController` | **حذف**: ویرایشگر گرافیکی RDLC در مرورگر (Kendo) قابل نگهداری نیست؛ به‌جایش قالب‌های ثابت + پارامتر |
| `iclockController` | **در پورتال بازنویسی نمی‌شود**: نقطه دریافت push دستگاه‌های ZKTeco است و باید در همان سرویس موتور بماند (R11) |

---

## ۴. قواعد پذیرش برای فازهای بعدی

0. **داده از دو کاتالوگ می‌آید، ولی هر دستور یک کاتالوگ را لمس می‌کند** — ستون‌های نمایشی با
   `PersonnelDirectory` در حافظه وصل می‌شوند و مدل/رابطه‌ها در `engine/relations.json` قفل است (`docs/09`، `T28`).

1. **هیچ SQL دست‌ساز برای نوشتن در جداول موتور** — نوشتن از طریق DA موتور انجام می‌شود؛
   هر SQL خواندنی که ناچار نوشته شد، باید `tools/verify-sql-schema.py` را پاس کند.
2. **هر عمل نوشتنی یک سطر رخداد** با قابلیت، هدف و جزئیات دارد.
3. **هر قابلیت جدید** یک عضو `AdminFeature` با مقدار پایدار (بدون شماره‌گذاری مجدد) می‌گیرد.
4. **هر صفحه** باید بدون شبکه بیرونی کار کند؛ آزمون `T09` آن را تضمین می‌کند.
5. **هر فرم POST** توکن ضد CSRF دارد؛ آزمون `T10` آن را تضمین می‌کند.
6. **هیچ آزمونی** به SQL Server نیاز ندارد (تا زمانی که آزمون یکپارچه‌سازی جدا اضافه شود).

---

## ۵. کارهای باقی‌مانده که عمداً انجام نشده‌اند

* **آزمون یکپارچه‌سازی با پایگاه‌داده واقعی** — در این محیط SQL Server وجود ندارد؛
  باید یک‌بار روی سرور مشتری با `db/verify.sql` اجرا شود.
* **مهاجرت داده** — پورتال داده جدیدی نمی‌سازد؛ فقط روی داده موجود کار می‌کند.
* **رندر گزارش‌های RDLC** — پورتال آن را انجام نمی‌دهد (بخش ۳، فاز C). عدد رسمی موتور
  همچنان در برنامه دسکتاپ دیده می‌شود؛ پورتال تردد خام و CSV می‌دهد. در دوره گذار دو
  برنامه همزمان روی یک پایگاه‌داده کار می‌کنند و هر دو به یک بیلد موتور مقیدند (`engine-pin.json`).
* **اجرای گزارش سلامت رابطه‌ها** — `db/03-relations.sql` نوشته شده ولی هرگز اجرا نشده است
  (بدون SQL Server)؛ نخستین کار روی سرور مشتری.
