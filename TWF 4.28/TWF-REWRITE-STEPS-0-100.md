# TWF 4.28 — مراحل بازنویسی (۰ تا ۱۰۰)

شرایط (همان T5Web):
- بازنویسی وفادار ۱:۱ به نسخه‌ی فعلی (منو/ترتیب/برچسب/فیلد/فلو/نقش) — **بدون تغییر زبان** (UI فارسی می‌ماند)
- فقط آنالیز استاتیک (REA + dnfile + CIL decoder `t5web-re-python`) — هیچ assembly مرجع اجرا نمی‌شود
- هر مرحله «تکمیل» است فقط وقتی **عملیات + نتیجه** آن در برابر نسخه‌ی مرجع (همان دیتابیس) راستی‌آزمایی شده باشد؛ وجود صفحه = تکمیل نیست
- ادعای «۱۰۰٪» فقط پس از پذیرش parity (مرحله ۱۰۰)
- **بدون قفل (تصمیم کاربر، 01/08/1405):** زیرسیستم قفل لایسنس T5 در محصول جدید **پیاده نمی‌شود**:
  `LockCheker`, `CheckLock`, `RetryCheck`, `RequestLockValuee`, `SetRequestsLock`,
  appSetting‌های `CheckT5Lock` / `RequestsLock` / `LockIP` — همه از scope خارج و در traceability
  با برچسب «عمداً حذف‌شده (بدون قفل)» ثبت می‌شوند. بقیه‌ی جریان Login/CAPTCHA/cook‌ie دقیقاً مثل مرجع.
- محل اجرای بازنویسی: همان شرایط T5Web (ریپازیتوری AOAS، branch جدا، مثلاً `twf-faithful-rebuild`)؛
  این سند در ریپازیتوری مرجع IO نگهداری می‌شود (کنار `PYTHON-REBUILD-PLAN.md`)

واژگان: «تست R» = تست در برابر نسخه‌ی مرجع روی دیتابیس مشترک (عملیات یکسان → نتیجه/وضعیت DB یکسان)

---

## فاز ۰ — بنیاد و RE (۰–۱۴)

- **۰.** AOAS: branch جدید `twf-faithful-rebuild` + اسکلت `twf-re-python` (Django + pyodbc + jdatetime) + ماژول config: خواندن دقیق ۳۷ appSetting و ۳ connection string (کلیدهای قفل خوانده می‌شوند ولی مصرف‌کننده‌شان پیاده نمی‌شود)
- **۱.** شواهد REA برای `MyTime3.dll` (static؛ members + IL متدهای کلیدی: کنترلرها، LoginManager، Encryption، LockCheker فقط برای مستندسازی رفتار بدون قفل)
- **۲.** شواهد REA برای `TD5.dll` (کل assembly — حجم کوچک)
- **۳.** شواهد REA برای `Personnel.dll` (بخش‌های DB/Model؛ UI WinForms خارج از دامنه)
- **۴.** اجرای CIL decoder/verifier `t5web-re-python` روی **کپی‌های T5V3.* داخل TWF** (۷ DLL، build متفاوت از T5Web 3.5.7) → evidence جدا + جدول تأییدشده جدا؛ تمرکز: Report/ReportGenerator/Model
- **۵.** ماژول تاریخ شمسی از IL `MyTime3.Controllers.Misc`: `ToLongPersianDate`, `DayOfWeekToString`, `TimeSpanToString`, `StringToTimeSpan` + یونیت‌تست (جفت با مقادیر مرجع)
- **۶.** مبدل هجری از IL `Personnel.Hijri.Converter` + تست‌های جفت (فرود هجری بر میلادی/شمسی)
- **۷.** ماژول `Encryption` (Encrypt/Decrypt) — پورت دقیق الگوریتم + تست سازگاری: cookie `mytimeauth` مرجع در پایتون خوانده شود و cookie پایتون در مرجع (بدون اجرا: مقایسه خروجی الگوریتم روی ورودی‌های ثابت از شواهد)
- **۸.** استخراج کامل کلاس `PersianString` (کلیه رشته‌های UI) → کاتالوگ مترجم در repo (منبع یگانه labelها)
- **۹.** ماژول اتصال: pyodbc → `192.168.21.213` (PersonnelDemo2 + ReportT5V3) + تست خواندن/نوشتن روی جدول‌های test
- **۱۰.** **مستندسازی حذف قفل:** فهرست کامل همه‌ی رفرنس‌های `LockCheker/CheckLock/RetryCheck/RequestLockValuee/SetRequestsLock/RequestsLock/CheckT5Lock/LockIP` در IL (به‌همراه flow اثرشان روی Login/Home) → در traceability با برچسب «عمداً حذف‌شده (بدون قفل)»؛ رفتار جایگزین مشخص می‌شود: لاگین مستقیم بدون گیت قفل، بقیه‌ی صفحه‌ها بدون تغییر
- **۱۱.** test-harness پذیرش: diff DOM صفحه‌به‌صفحه + diff وضعیت DB (query قبل/بعد) + اسکرین‌شات جفت‌به‌جفت (RTL/فونت)
- **۱۲.** `RouteConfig.RegisterRoutes` → urls.py (همان مسیرها/VERBها) + `BundleConfig.RegisterBundles` → static (بدون تغییر مسیر دارایی‌ها)
- **۱۳.** `_Layout.cshtml` + `_ViewStart` + `Error.cshtml` → Jinja2 (همان bootstrap classها، RTL، لوگو/فونت)
- **۱۴.** تست R صفحه‌ی layout خالی (اسکرین‌شات diff با مرجع بدون لاگین/با لاگین)

## فاز ۱ — لایه داده TD5 + Personnel (۱۵–۳۰)

- **۱۵.** `TD5.Init`: `Start/GetValueByName/GetInitialCatalog` (خواندن کانفیگ/کتالوگ اولیه)
- **۱۶.** `TD5.Model`: آبجکت‌های `DapQuery`1`, `DapType`, `Request` (فیلدها/رفتار)
- **۱۷.** `TD5.Repo` (رپوژیتوری مرکزی)
- **۱۸.** query-builder fluent از IL: `Where/OrderBy/First/Tolist/Any/Execute/Select/SelectMany` → SQL پارامتری دقیق (همان operatorها/ترتیب شرط‌ها)
- **۱۹.** سرویس `UserInfoController` (ستون‌های BadgeNumber/FirstName/.../DevicePass + CRUD)
- **۲۰.** سرویس `UserRuleControllers` (RuleType/RuleId/FromDate/ToDate + CRUD)
- **۲۱.** سرویس `DepartmentRuleControllers` (DepartmentID + CRUD)
- **۲۲.** سرویس `CalendarControllers` (تقویم/تعطیلات + CRUD)
- **۲۳.** سرویس `LeaveTypeControllers` — کامل: ستون‌های سقف‌ها (`MaxHoursInDay/MaxHoursInMonth/MaxDailyInYear/MaxDailyInMonth/EachXHoursADay/MaxMinus`), `ForNextYear/ForNextYearSum/GetInHolidays/GetInType/DecreaseFromLeaveID/OnlyOne/AutoInHolidays/ValidFreeOT/Payment/Symbol` + CRUD + `GetByID/GetAll/AddNew`
- **۲۴.** سرویس `RequestController` (ستون‌های Id/BadgeNumber/WorkflowType/DateTime/ForDate/LeaveId/FromTime/ToTime/RecordTime/OverTimeType/IsSuspend/IsDaily... + `GetT5Model`)
- **۲۵.** `TD5.Controllers.Execution` (اجرای query؛ transaction/commit رفتارمند)
- **۲۶.** پورت آبجکت‌های `T5V3.Model` مصرفی TWF (Request/LeaveType/...) با فیلدها و رفتار مطابق کپی TWF (از مرحله ۴)
- **۲۷.** `GlobalHelper`: `WithTimeApp/GetLoggedInUserName/BadgeNumber/UserInfo/UserInfoTitleID/AddLeaveTypes/AddOverTimeTypes/AddRewardPenaltyTypes/AddDutyTypes/AddLeaveDescriptions/GetRule`
- **۲۸.** **تست R لایه داده:** مجموعه‌ای از queryهای واقعی (از شواهد/فلوهای بعدی) → خروجی پایتون = خروجی مرجع (log شواهد)
- **۲۹.** لایه Personnel (بخش ۱) از مدل EF `DB.PersonnelMdl` (csdl/ssdl/msl خارج‌شده از Personnel.dll): `UserInfoDA/DepartmentDA/EmpTypeDA/TitleChartDA/UserInfoTitlesDA/UserInfoPropertyDA/CertificateDA`
- **۳۰.** لایه Personnel (بخش ۲): `DeviceDA/GateDA/DeviceMessageDA/DeviceOperationLogDA/FontSizeDA/PropertyDA/PropertyDropdownValueDA/TemplateDA/DashboardDA/DashboardItemDA/ToolbarItemsDA/MyTimeToolbarItem/UserDeviceUploadDA/UserDeviceValidDA` + `DBManager`

## فاز ۲ — احراز هویت (بدون قفل) (۳۱–۴۰)

- **۳۱.** `LoginManager`: `Login/Logout/GetRole/IsExpired/GetBadgeNumber` — cookie `mytimeauth` (رمزنگاری‌شده با مرحله ۷، عمر `CookiesTime=20`)
- **۳۲.** `AuthorizeFilter` (`HandleUnauthorizedRequest/AuthorizeCore`) — رفتار redirect/پیام دقیقاً مثل مرجع برای هر نقش
- **۳۳.** CAPTCHA مطابق رفتار CaptchaMvc (تولید/تأیید/پیام‌های خطا فارسی) — بدون تغییر ظاهر
- **۳۴.** صفحه `Login` (Login.cshtml → template): فیلدها/برچسب‌ها/پیام‌ها + `TestUserPass` + `PreserveLoginUrl` — **بدون** اکشن‌های `CheckLock/RetryCheck` (حذف قفل؛ مسیرهای `/Login/CheckLock` را در traceability «عمداً حذف‌شده» می‌کنیم و redirect جایگزین تعریف نمی‌شود مگر مرجع flow آن را در حالت غیرقفل داشته باشد)
- **۳۵.** **تست R لاگین:** کاربر معتبر/ناسازگار رمز/CAPTCHA غلط/کاربر غیرفعال/نقش‌ها — cookie و redirect پایتون = مرجع
- **۳۶.** `ChangePassword` + محدودیت‌ها: `ChangePassRestriction/ChangePassMonthly/ChangePassLimit=3` (پیام‌ها دقیق)
- **۳۷.** `ChangePasswordForce` (flow اجباری)
- **۳۸.** `Logout` + صفحه `About`
- **۳۹.** **تأیید حذف قفل:** سناریوی لاگین و ورود به Home بدون هیچ گیت قفل (doc: «بدون قفل» + فهرست رفتارهای حذف‌شده از مرحله ۱۰)
- **۴۰.** سبز‌شدن کل test-suite احراز هویت در برابر مرجع (تمام caseها)

## فاز ۳ — UI مشترک و منو (۴۱–۴۸)

- **۴۱.** `_Menu` (منوی کناری: اقلام/ترتیب/برچسب‌ها/نمایش بر اساس نقش — مثل مرجع)
- **۴۲.** `_Toolbar` + `_ToolbarSettings` + اکشن‌های `SetToolbar/GetToolbars/ToolbarShow`
- **۴۳.** `_HomeStatus` + `_CurrentTime` + `_UserName`
- **۴۴.** `_RequestForm` (فرم مشترک درخواست؛ فیلدها/اعتبارسنجی سمت کلاینت jquery.validate یکسان)
- **۴۵.** `_SelectMultiUser` (انتخاب چند کاربر)
- **۴۶.** partial `_RemainLeave` (نمایش باقی‌مانده مرخصی)
- **۴۷.** دارایی‌های front: همان فایل‌ها در همان مسیرها (bootstrap 3.3.7, jQuery 3.1.1/1.11.2/3.6.1, jQuery UI 1.12.1, validate+unobtrusive, unobtrusive-ajax, knockout 3.5.1, select2, bootstrap-select, datatables.net-bs4, persian-datepicker, JsFarsiCalendar, ptTimeSelect, maskedinput, spin, menuScript, index.js, modernizr + فونت‌ها/تصاویر)
- **۴۸.** **تست R بصری UI مشترک:** اسکرین‌شات diff هر partial (lağin‌شده، هر نقش)

## فاز ۴ — داشبورد (۴۹–۵۶)

- **۴۹.** `Home.Index` + `MakeCount` + `GetTableData`
- **۵۰.** `GetDailyDetailsReport` + `CalculateDailyDetailsReport`
- **۵۱.** `CalculateDailyAbsence` (محاسبه‌ی غیبت روزانه)
- **۵۲.** `ForNDayReport` (بازه `ShowDays=30`)
- **۵۳.** `GetPositionsList` + `GetStatusList` + `GetValidLeaveTypes`
- **۵۴.** `GetSuspendRequests` + `GetRequestsFor` (درخواست‌های معلق)
- **۵۵.** `MyBanner` + صفحه `About` (نهایی، با داده)
- **۵۶.** **تست R داشبورد:** شمارنده‌ها/جدول/گزارش روزانه/وضعیت‌ها = مرجع (کاربران/تاریخ‌های نمونه)

## فاز ۵ — درخواست‌ها (۵۷–۷۴)

- **۵۷.** زیرساخت مشترک درخواست: `AddPersonnelToViewBag` + اعمال سقف‌ها (`RequestRecordsDailyLimit=15/MonthlyLimit=16/MaxRequestAgeDays=400/LastDayForRequest=300/HoursOfDays=8/ShowDays=30/ValidLateEarly/ValidHourlyFree`) + قواعد LeaveType (ستون‌های سقف + تعطیلات)
- **۵۸.** **دیر (Late):** Add/Edit + `RequestLate/EditRequestLate`
- **۵۹.** **زود (Early):** Add/Edit + `RequestEarly/EditRequestEarly`
- **۶۰.** **مرخصی روزانه (DailyLeave):** Add/Edit + `RequestDaily/EditRequestDaily/AddRequest` + `GetRemainD`
- **۶۱.** **مرخصی ساعته (HourlyLeave):** Add/Edit + `RequestHourly/EditRequestHourly/AddRequest` + `CheckDailyLeave` + اکشن‌های تستی `RequestDailyTest/AddRequestTest`
- **۶۲.** **کار روزانه (DailyDuty):** Add/Edit + `RequestDaily/EditRequestDaily/AddRequest`
- **۶۳.** **کار ساعته (HourlyDuty):** Add/Edit + `RequestHourly/EditRequestHourly/AddRequest` + `CheckRemainDays`
- **۶۴.** **اضافه‌کاری (OverTime):** Add/Edit + `RequestOverTime`
- **۶۵.** **اضافه‌کاری ثابت (ConstOT):** Index/Edit/EditAll + `GetAllDailyConstOT/GetOwnedTitles/GetOverTimeSubUsers`
- **۶۶.** **پاداش/کیفر (Reward):** Add + `RequestReward`
- **۶۷.** **ثبت (Record):** Add/Edit + `RequestForRecord/EditRequestRecord` + `GetRecordRequestsCount` + `MonthStart/MonthEnd`
- **۶۸.** `Request.New` + `FromUser` + `Edit` + `SendPosition` (RequestController)
- **۶۹.** `GeneralRequest` + `GeneralRequestWeb` + `AddRequest` + `AppExtraDataForRequest.GetAppExtraData`
- **۷۰.** `AddLocationRequest` + مدل `LatLng` (ثبت موقعیت)
- **۷۱.** `ShowPhoto` (نمایش عکس/موقعیت درخواست)
- **۷۲.** باقی‌مانده مرخصی: `RemainLeave` controller: `GetRemain/RemainLeave/RemainLeaveJ/RemainLeaveJj/GetForUser/ForPersonnel/GetLeaveSubUsers/Index`
- **۷۳.** **تست R قواعد اعتبارسنجی** هر نوع: caseهای مثبت/منفی (سقف روز/ماه/سال، بازه ممنوع، تعطیل، درخواست تکراری، حداقل/حداکثر ساعت) — پیام خطای فارسی دقیقاً مثل مرجع
- **۷۴.** **تست R end-to-end هر ۸ نوع درخواست:** ساخت در مرجع → ساخت یکسان در پایتون (همان کاربر/تاریخ) → **وضعیت پایانی DB یکسان** + ردیف‌های گزارش‌ها یکسان

## فاز ۶ — تأیید/انصراف و مدیریت (۷۵–۸۴)

- **۷۵.** `ShowUsersRequest`: Index + `GetForUser` + `getStatus` + `AddPersonnelToViewBag` (درخواست‌های زیرمجموعه)
- **۷۶.** `ShowRequest`: Index + `GetRequests`
- **۷۷.** `AcceptRequest` (تأیید): Index + `Accept/AcceptRequest/AcceptByID/AcceptByID1/AcceptByID2/AcceptAll/AcceptAllRequests` + `InsertData` + `_RequestsList`
- **۷۸.** `AcceptRequest` (لغو): `Cancel/CancelRequest/CancelByID/CancelAll/CancelAllRequests`
- **۷۹.** `EditRequest`: Index + `GetRequests` + `EditByID` + `CancelRequest` + `Delete` + `CheckRequestTime`
- **۸۰.** `Undo`: Index + `Undo` + `DeleteData`
- **۸۱.** اعلان: `NotificationManager.NewRequestAdded` + `Notify` (Index/SetViewed) + `CanDownloadNoti5/GetNoti5`
- **۸۲.** `Settings`: Index + `Change` + `LoadSettings` + `LoadValue` (همان ۳۷ کلید؛ تغییر مقدار → اثر در کل اپ مثل مرجع)
- **۸۳.** `SelectArea`: Index + `GetAreas/GetArea/DeleteArea/InsertNew/GetUserAreas/GetPersonnel`
- **۸۴.** `Deputy` (نماینده): Index + `GetState/Add/Delete` + `_CurrentDeputy` + `DescriptionClass` (GetDescriptionInDate/GetDescriptionInOld)

## فاز ۷ — گزارش‌ها (۸۵–۹۴)

- **۸۵.** `Report.Index` + `AddReportsToViewBag` + `GetPersonnel` + فیلترها (خود/کارکنان/بازه هجری `FromDateHijri/ToDateHijri`)
- **۸۶.** پورت `ReportController.GetSchema` — تولید RDL پویا از دیتابیس (دقیقاً همان RDL: نام/paramها/DataSet1)
- **۸۷.** پورت `ReportController.GetData` — پر کردن `DataSet1` با پارامترهای `ReportID/isSelf/SelectedBadgeNumbers/exportType/FromDateHijri/ToDateHijri/Page/action`
- **۸۸.** رندرر RDL پایتون برای **زیرمجموعه‌ی RDL مورد استفاده** (از IL `T5V3.Report/ReportGenerator` کپی TWF): خروجی/صفحه‌بندی/عنوان مطابق رفتار ReportViewer 10
- **۸۹.** معادل `Report.aspx`: دکمه‌های «صفحه قبل/بعد» + POST `/Report/Report{?params}` + کنترل export بر اساس `ReportExport` + عنوان «{bode5} - {Report}»
- **۹۰.** `TotalCalcsReport` + `DailyReport` (ReportController)
- **۹۱.** `LeavesReport`: Index + `isObs` (تعطیلات رسمی/محرّم)
- **۹۲.** `LocationReport`: Index + `GetReport/Report` + `GetImage` + `ShowPhoto` + `AddPersonnelToViewBag`
- **۹۳.** `OverTimeReport`: Index + `HasAnyUsers` + `Show` + `ChangeBefore/ChangeAfter/AcceptBefore/AcceptAfter/CancelBefore/CancelAfter`
- **۹۴.** `PersonnelStatus`: Index + `ViewR`: Index (AJAX `RemainLeaveJj`) + `RenderRazorViewToString` equivalent (بازرندر partial به string)

## فاز ۸ — مابقی و پذیرش (۹۵–۱۰۰)

- **۹۵.** `CustomVirtualPathProvider`/`CustomVirtualFile` (`Decrypt/FileExists/GetFile/Open`) + `TINYLib` (درک IL → معادل رفتاری؛ هر دو فقط آنالیز استاتیک)
- **۹۶.** WebApi: بررسی routes ثبت‌شده در مرجع (assembly ApiController ندارد؛ اگر endpoint فعالی در runtime هست → پیاده، وگرنه ثبت در traceability «بدون مصرف»)
- **۹۷.** **Regression کامل:** همه‌ی routes (از RouteConfig) + همه‌ی flowهای فاز ۴–۷ + diff بصری ۵۹+۱۱ صفحه (اسکرین‌شات جفت‌به‌جفت)
- **۹۸.** ماتریس traceability TWF-00: هر اکشن/قاعده/رشته ← شواهد IL ← فایل پایتون ← تست ← نتیجه (همان استاندارد doc 33) + بخش جدا برای موارد «عمداً حذف‌شده (بدون قفل)»
- **۹۹.** سناریوی پذیرش end-to-end (کاربرهای نمونه، هر نقش): لاگین → داشبورد → ۸ نوع درخواست → تأیید/لغو/Undo → گزارش‌های RDL → export → تنظیمات → delegate/area — در مرجع و پایتون روی دیتابیس مشترک
- **۱۰۰.** **پذیرش parity:** سبز‌شدن فاز ۹۷+۹۹ → اعلام «parity» (تا آن زمان هیچ ادعای تکمیل/۱۰۰٪) + بستن گزارش پایان بازنویسی TWF

---

## وابستگی فازها

```
۰ ← ۱ ← ۲ ← ۳ ← ۴ ← ۵ ← ۶ ← ۷ ← ۸
(هر فاز روی قبلی؛ داخل فاز: ترتیب شماره‌ها = ترتیب اجرا؛
هر شماره = یک واحد کاری با تست R خودش)
```

## اعداد کلیدی (برای تخمین/پیگیری)

| ناحیه | واحد |
|---|---|
| MyTime3 | ۱۶۲۳ method / ۳۳ کنترلر / ۳۷ مدل |
| TD5 | ۵۸۹ method / ۶ سرویس query |
| Personnel (سهم وب) | ~۱۰۰۰ method از ۳۲۵۰ (UI WinForms/زک/USB خارج) |
| T5V3.* کپی TWF | ۷ DLL (decoder مرحله ۴) |
| نما | ۵۹ cshtml + ۱ aspx + ۱۱ Shared = ۷۱ قالب |
| حذف‌شده (بدون قفل) | ۲ اکشن Login + LockCheker + ۳ appSetting مصرفی |
