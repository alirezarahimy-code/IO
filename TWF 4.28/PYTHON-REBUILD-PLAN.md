# TWF 4.28 — فهرست کامل اجزا + برنامه بازنویسی در پایتون

> هدف: بازنویسی **کاملاً وفادار** (بدون تغییر رفتار/رابط) سامانه TWF 4.28 در پایتون،
> با UI فارسی. هر بخش «تکمیل‌شده» محسوب نمی‌شود مگر عملیات + نتیجه آن در برابر نسخه مرجع (نمونه واقعی + دیتابیس مشترک) راستی‌آزمایی شده باشد.
>
> روش تحلیل: فقط آنالیز استاتیک (dnfile + REA + decoder CIL در `t5web-re-python`). هیچ مجموعه‌بندی مرجع اجرا نشده است.

---

## ۰. خلاصه

| مورد | مقدار |
|---|---|
| نوع | وب‌اپلیکیشن ASP.NET MVC 5 + WebForms (ReportViewer)، net472 |
| عنوان داخلی | «سامانه حضور و غیاب بعد پنجم» |
| اجزای اختصاصی | **MyTime3.dll** (2,041,344 بایت، ۲۶۸ type، ۱۶۲۳ method) + **TD5.dll** (76,288 بایت، ۹۰ type، ۵۸۹ method) + **Personnel.dll** (937,984 بایت، ۳۷۶ type، ۳۲۵۰ method) |
| کپی‌های T5V3.* در bin | ۷ DLL (Model/DB/Attendance/Misc/LeaveCalcs/Report/ReportGenerator) — **build متفاوت از T5Web 3.5.7** (اندازه‌ها فرق دارند) |
| دیتابیس | SQL Server `192.168.21.213` (sa/Dba@123) — کتالوگ‌های **PersonnelDemo2** و **ReportT5V3** |
| نماها | **۵۹ cshtml + ۱ aspx** (ReportViewer) در ۲۹ پوشه controller |
| پیکربندی | **۳۷** appSetting + ۳ connection string |
| تاریخچه تغییرات 4.28 | «1-حل مشکل نمایش تاریخ های سال کبیسه» (نمایش تاریخ‌های سال کبیسه) |

معماری: `MyTime3` (لایه وب) از **TD5** (لایه داده T5 با API query به‌صورت fluent) و
**Personnel.dll** (لایه HR + دستگاه‌ها + Excel) و **T5V3.Report/ReportGenerator**
(موتور گزارش‌های پویا RDL از دیتابیس) استفاده می‌کند.

---

## ۱. چیدمان استقرار (App/)

```
App/
├── bin/            ۳۰ DLL (۳ اختصاصی + ۷ T5V3.* + ۲۰ کتابخانه‌ی جانبی)
├── Views/          ۵۹ cshtml + Views/Report/Report.aspx
├── Scripts/        jQuery/UI/knockout/bootstrap/select2/datatables + اسکریپت‌های فارسی
├── Content/        CSS (bootstrap 3.3.7, Main.css, style.css, calendar.css, persian-datepicker, ...)
├── Images/, fonts/ تصاویر + FontAwesome + glyphicons
├── Web.config      connStrings + ۳۷ appSettings
├── Global.asax     → MyTime3.MvcApplication
└── packages.config EF 6.4.4, Dapper 2.0.123, knockout 3.5.1, bootstrap 3.3.7,
                    WebApi 5.2.7, Newtonsoft 13.0.1, Antlr 3.5, WebGrease 1.5.2,
                    jQuery 3.1.1, jQuery.UI 1.12.1, Modernizr 2.8.3, ...
```

تنظیمات `httpRuntime`: `maxUrlLength=10999`, `maxQueryStringLength=2097151`.

---

## ۲. فهرست کامل اجزا

### ۲.۱ MyTime3.dll — لایه وب (ASP.NET MVC)

**راه‌اندازی:** `MvcApplication.Application_Start`، `RouteConfig.RegisterRoutes`، `BundleConfig.RegisterBundles`،
`FilterConfig.RegisterGlobalFilters`، `WebApiConfig.Register` (WebApi 5.2.7 ثبت است ولی **هیچ ApiController در assembly نیست** — احتمالاً برای سرویس‌های بعدی/سایر اپ؛ هنگام راستی‌آزمایی باید راست routes را از runtime بگیریم).

#### کنترلرها (۳۳) و اکشن‌های عمومی

| Controller | اکشن‌ها |
|---|---|
| **Home** | CheckLock, Index, IsAdmin, MakeCount, RenderRazorViewToString, GetTableData, GetDailyDetailsReport, GetPositionsList, GetStatusList, GetValidLeaveTypes, CalculateDailyAbsence, CalculateDailyDetailsReport, GetSuspendRequests, GetRequestsFor, About, MyBanner, ForNDayReport, RequestLockValuee, SetRequestsLock, CanDownloadNoti5, GetNoti5, SetToolbar, GetToolbars, SideMenu, ToolbarShow |
| **Login** | Login(GET/POST), CheckLock, RetryCheck, TestUserPass, ChangePassword(GET/POST), ChangePasswordForce(GET/POST), Change, Logout |
| **Request** | New, FromUser, SendPosition, Edit, GeneralRequestWeb, GeneralRequest, AddLocationRequest, AddRequest, ShowPhoto, AddPersonnelToViewBag |
| **Late** | Add(GET/POST), Edit(GET/POST), EditRequestLate, RequestLate |
| **Early** | Add(GET/POST), Edit(GET/POST), EditRequestEarly, RequestEarly |
| **DailyLeave** | Add(GET/POST), Edit(GET/POST), EditRequestDaily, RequestDaily, AddRequest, GetRemainD |
| **HourlyLeave** | Add(GET/POST), Edit(GET/POST), EditRequestHourly, CheckDailyLeave, RequestDailyTest, AddRequestTest, RequestHourly, AddRequest |
| **DailyDuty** | Add(GET/POST), Edit(GET/POST), EditRequestDaily, RequestDaily, AddRequest |
| **HourlyDuty** | Add(GET/POST), Edit(GET/POST), EditRequestHourly, CheckRemainDays, RequestHourly, AddRequest |
| **OverTime** | Add(GET/POST), Edit(GET/POST), RequestOverTime |
| **ConstOT** | Index, Edit(GET/POST), EditAll, GetAllDailyConstOT, GetOwnedTitles, GetOverTimeSubUsers |
| **Reward** | Add(GET/POST), RequestReward |
| **Record** | Add(GET/POST), Edit(GET/POST), EditRequestRecord, GetRecordRequestsCount, MonthStart, MonthEnd, RequestForRecord |
| **AcceptRequest** | Index, Accept, AcceptRequest, CancelRequest, AcceptByID2/1/0, AcceptAll, Cancel, CancelByID, AcceptAllRequests, CancelAllRequests, InsertData |
| **ShowRequest** | Index, GetRequests |
| **ShowUsersRequest** | Index, AddPersonnelToViewBag, GetForUser, getStatus |
| **EditRequest** | Index, GetRequests, CancelRequest, EditByID, Delete, CheckRequestTime |
| **Undo** | Index, Undo, DeleteData |
| **Notify** | Index, SetViewed |
| **Deputy** | Index, GetState, Delete, Add |
| **SelectArea** | Index, GetPersonnel, GetAreas, GetArea, DeleteArea, InsertNew, GetUserAreas |
| **Settings** | Index, Change, LoadSettings, LoadValue |
| **Report** | Index, Report, GetPersonnel, TotalCalcsReport, DailyReport, AddPersonnelToViewBag, AddReportsToViewBag, GetSchema, GetData |
| **LeavesReport** | Index, isObs |
| **LocationReport** | Index, GetImage, GetReport, Report, ShowPhoto, AddPersonnelToViewBag |
| **OverTimeReport** | Index, HasAnyUsers, Show, ChangeBefore, ChangeAfter, AcceptBefore, CancelBefore, AcceptAfter, CancelAfter |
| **PersonnelStatus** | Index |
| **ViewR** | Index (AJAX، بدون Layout — نتیجه‌ی `RemainLeaveJj`) |
| **RemainLeave** | GetRemain, RemainLeaveJ, RemainLeaveJj, RemainLeave, GetForUser, RenderRazorViewToString, Index, ForPersonnel, AddPersonnelToViewBag, GetLeaveSubUsers |
| **ChangePass** | ChangePassCheck, CheckPassMonthly, CheckPassForce, PassChanged |
| **DBInitializer** | GetT5DBInstance, GetPersonnelSystem |
| **DescriptionClass** | GetDescriptionInDate, GetDescriptionInOld |
| **Misc** (static) | DayOfWeekToString, ToLongPersianDate, TimeSpanToString, StringToTimeSpan |
| **NotificationManager** | NewRequestAdded |

#### مدل‌ها (۳۷ کلاس در `MyTime3.Models`)

Request, GeneralRequest, DailyExitRequest, DailyLeaveRequest, HourlyExitRequest,
HourlyLeaveRequest, LateEarlyRequest, OverTimeRequest, RewardPenaltyRequest,
RecordRequest, AppExtraDataForRequest, Deputy, HomeModel, LoginModel,
PersonnelModel, PerosnnelStatusModel, SettingsModel, ChangePasswordModel,
RemainLeave, ReportModel, ADayReport, TotalCalcsModel, LocationReportModel,
LatLng, InShiftExit, OverTimeForDay, OTConstDaily, SumConstOT, UserInfoModel,
UserInfoTitlesModel, TitleChartModel, TitleChartClass, ShowRequestModel.

#### زیرساخت

| کلاس | وظیفه |
|---|---|
| `Authorization.LoginManager` | Login/Logout/GetRole/IsExpired/GetBadgeNumber — مدیریت نشست و cookie `mytimeauth` |
| `Authorization.Encryption` | Encrypt/Decrypt — رمزنگاری cookie و مقادیر پنهان (الگوریتم باید از IL استخراج و ۱:۱ کپی شود) |
| `Lock.LockCheker` | Check/StringToDateTime/GetToday + `CheckLockResult/LockResult` — بررسی قفل لایسنس T5 (پیوند با `CheckT5Lock` و `RequestsLock=42972`) |
| `Content.PersianString` | **کلیه متن‌های فارسی UI** (استاتیک) — منبع labels بازنویسی |
| `Content.ImagesResource`, `Content.Noti5` | رزورس‌های تصویر + اعلان «Noti5» (دانلود/نمایش) |
| `GlobalHelper` | WithTimeApp, GetLoggedInUserName/BadgeNumber/UserInfo/UserInfoTitleID, AddLeaveTypes, AddOverTimeTypes, AddRewardPenaltyTypes, AddDutyTypes, AddLeaveDescriptions, GetRule — پل با مدل T5 |
| `TINYLib.Tiny` (+`_DTiny`, `_DTinyEvents`) | کتابخانه کوچک تعبیه‌شده (رویدادها + interop) — باید IL آن خوانده شود تا کاربرد دقیق مشخص شود |
| `CustomVirtualPathProvider` / `CustomVirtualFile` | Decrypt/FileExists/GetFile/Open — سرو فایل‌های مجازی **رمزنگاری‌شده** از برون مسیر فیزیکی |
| `Views.Report.Report` (code-behind) | Page_Load از Report.aspx (بخش ۲.۷) |
| `Properties.Resources` | رشته‌های resource |

### ۲.۲ TD5.dll — لایه داده T5 (fluent query)

| کلاس | اکشن‌ها |
|---|---|
| `TD5.Init` | Start, GetValueByName, GetInitialCatalog (خواندن کانفیگ/کتالوگ اولیه) |
| `TD5.Repo` | رپوژیتوری مرکزی |
| `TD5.Model.DapQuery`1`, `DapType`, `Request` | ساختار query + مدل Request |
| `UserInfoController` | GetUserInfo, InsertUserInfo, UpdateUserInfo, DeleteUserInfo + **فیلد‌های ستونی**: BadgeNumber, WorkflowType, EmpTypeId, CertificateId, Janbaz, Gender, EmployeeNumber, FirstName, LastName, FatherName, NationalNo, Address, Phone, Email, Birthday, HiredDate, FiredDate, ValidFrom, ValidTo, Photo, Descriptions, UserName, Password, ProgramUser, WebUser, DeviceName, DeviceEnable, DevicePass, DevicePriv, DeviceCardNo, DviceGroup + `Where, OrderBy, Tolist, First, SelectMany, Select, Execute, Any` |
| `UserRuleControllers` | GetUserRule/Insert/Update/Delete + RuleType, RuleId, FromDate, ToDate + همان API fluent |
| `DepartmentRuleControllers` | Get/Insert/Update/Delete DepartmentRule + DepartmentID, RuleId, FromDate, ToDate + fluent |
| `CalendarControllers` | Get/Insert/Update/Delete Calendar + CalendarId, CalendarName + fluent |
| `LeaveTypeControllers` | GetLeaveType/Insert/Update/Delete/GetByID/GetAll/AddNew + ستون‌ها: Id, LeaveName, Color, DutyOrLeave, Hourly, StateKey, **MaxHoursInDay, MaxHoursInMonth, MaxDailyInYear, MaxDailyInMonth, EachXHoursADay, MaxMinus**, GetInType, ForNextYear, ForNextYearSum, GetInHolidays, Payment, Symbol, AutoInHolidays, OnlyOne, ParameterID, **DecreaseFromLeaveID**, ValidFreeOT + fluent |
| `RequestController` | GetRequest + ستون‌ها: Id, BadgeNumber, WorkflowType, DateTime, ForDate, LeaveId, LeaveDescriptionId, FromTime, ToTime, RecordTime, OverTimeType, OverTimeAllowValue, IsSuspend, Descriptions, IsDaily + fluent + `GetT5Model` |
| `Execution` | اجرای query (بدون اکشن عمومی؛ internal) |

**الگو:** هر «Controller» واقعاً یک **service query-builder** است:
`GetUserInfo().Where(BadgeNumber: x).First()` → SQL پارامتری‌شده.
این همان لایه‌ای است که MyTime3 با دیتابیس T5 (`ReportT5V3`) کار می‌کند و
منبع قواعد workflow (LeaveType/UserRule/DepartmentRule/Calendar) است.

### ۲.۳ Personnel.dll — زیرسیستم پرسنل/دستگاه (WinForms + DB + Excel)

| گروه | کلاس‌ها |
|---|---|
| `Personnel.Model` (۲۷) | UserInfo, Department, Device, DeviceAdvancedOptions, DeviceCapacity, DeviceMessage, DeviceOperationLog, Record, RecordSource, RecordVerifyStyle, Gate, EmpType, Certificate, Dashboard, DashboardItem, DashboardModel, Property, PropertyDropdownValue, PropertyType, PropertyDropdownValue, Template, TitleChart, UserInfoTitle, UserInfoProperty, FunctionKey, USBFileData, USBFileRecords, UserDeviceUpload, UserDeviceValid, ConnectionType, SMSType, ... |
| `Personnel.DB` (~۳۵) | **EF** (مدل `DB.PersonnelMdl` به‌صورت csdl/ssdl/msl داخل‌افکنی‌شده) + کلاس‌های DA: UserInfoDA, DepartmentDA, DeviceDA, GateDA, EmpTypeDA, CertificateDA, DashboardDA/DashboardItemDA, DeviceMessageDA, DeviceOperationLogDA, FontSizeDA, MyTimeToolbarItem, PropertyDA, PropertyDropdownValueDA, TemplateDA, TitleChartDA, ToolbarItemsDA, UserDeviceUploadDA, UserDeviceValidDA, UserInfoPropertyDA, UserInfoTitlesDA + **DBManager** |
| `Personnel.ZK` (۱۳) | **SDK دستگاه اثرانگشت ZKEM**: CZKEM (interop), ZKDevice, DataCollector, ActiveDevices, PhotoFolder, Upload/Download/Backup/Transaction events — جمع‌آوری ثبت ورود/خروج از دستگاه |
| `Personnel.USBFiles` (۴) | RecordReader, UserDataReader, UserDataWriter, USBLogResource — **وارد کردن لاگ دستگاه از USB** |
| `T5V3.ExcelDataReader` + `ExcelDataReader2.*` (~۴۰) | ریدر XLS/BIFF داخلی (بدون وابستگی به Excel) |
| `ExcelWriterPers.*` (~۱۵) | رایتر XLS/BIFF داخلی (سازنده خروجی اکسل فارسی) |
| `Personnel.Hijri.Converter` | **مبدل تاریخ هجری/شمسی** |
| `Personnel.PhotoFace` | عکس چهره (دوربین/وبکم) |
| `Personnel.UI` (~۳۵) | **فرم‌های دسکتاپ**: frmDevice, frmUserInfo, frmGate, frmUSB, frmExcel, frmDownloadUsers, frmUploadUsers, frmEditPersonnel, frmDepartment, frmEmpType, frmTitleChart, frmDeptsChart, frmDeviceLogs, frmDeviceProperties, frmDeviceSMS, frmCertificate, frmChkMTU, frmDeviceFinder, frmSelectTitle/User/Department(s), ucSelect* (user controls), FormLogger, UIColorSettings, UIResource — یعنی Personnel.dll هم‌زمان یک **اپ دسکتاپ مستقل** (سامانه پرسنلی) است |
| دیگه‌ها | Dbcon, PersonnelSystem, Program, Misc.DataGridToDataTable |

> **حد سهم دسکتاپ/وب:** در بازنویسی **وب**، فرم‌های `Personnel.UI` و زک/USB مستقیم در دامنه نیستند،
> ولی **جدول‌ها و عملیات DB** همان‌ها (کاتالوگ `PersonnelDemo2`) اسکیمای داده‌ی مشترک وب‌اپ را می‌سازند
> (کاربران، واحد‌ها، عناوین، دستگاه‌ها، ثبت‌ها).

### ۲.۴ کپی‌های T5V3.* در bin/ TWF (موتور گزارش و مدل)

| DLL | اندازه در TWF 4.28 | در T5Web 3.5.7 |
|---|---|---|
| T5V3.Model | 94,720 | 104,960 |
| T5V3.DB | 428,544 | 428,544 |
| T5V3.Attendance | 84,992 | 84,992 |
| T5V3.Misc | 45,056 | 46,080 |
| T5V3.LeaveCalcs | 13,824 | 13,824 |
| T5V3.Report | 202,752 | 207,872 |
| T5V3.ReportGenerator | 123,904 | 123,904 |

**نکته مهم:** buildهای TWF با نسخه‌های T5Web 3.5.7 **کاملاً یکسان نیستند** (Model/Misc/Report فرق اندازه دارند).
Decoder/Verifier موجود در `t5web-re-python` روی این کپی‌ها باید **دوباره اجرا و تأیید شود** (اسکریپت `rebuild_all.py` مسیر DLL را param می‌گیرد) تا بازنویسی موتور گزارش دقیقاً مطابق همین کپی‌ها باشد.

### ۲.۵ DLLهای جانبی bin/ (نسخه‌ها)

EntityFramework 6.4.4 (+SqlServer 586KB), Dapper 2.0.123, Newtonsoft.Json 13.0.1,
Microsoft.ReportViewer **10** (Common 6.4MB + WebForms 2.1MB), CaptchaMvc, WebGrease 1.5.2,
Antlr 3.5, System.Web.Http (WebApi) 5.2.7 (+Client/Core/WebHost/Formatting/Primitives/Extensions),
System.Web.Mvc, System.Web.Optimization, System.Web.Razor, System.Web.WebPages*, System.Web.Helpers,
Microsoft.Web.Infrastructure, Microsoft.Bcl* (transitive).

### ۲.۶ دیتابیس

| اتصال | کاتالوگ | مصرف‌کننده |
|---|---|---|
| `Personnel` (connStrings) | **PersonnelDemo2** @192.168.21.213 (sa/Dba@123) | Personnel.dll (EF `DB.PersonnelMdl` + Dapper) |
| `T5CnnStr` (appSetting, EF) | **ReportT5V3** | MyTime3/TD5 (EF `EntityClasses.mdlT5V3`) — درخواست‌ها، LeaveType، Calendar، Rule، اسکیمای گزارش |
| `PersonnelCnnStr` (appSetting, EF) | PersonnelDemo2 | کپی EF اتصال Personnel |

### ۲.۷ نماها (۵۹ cshtml + ۱ aspx)

**Shared (۱۱):** `_Layout`, `_Menu`, `_Toolbar`, `_ToolbarSettings`, `_RequestForm`, `_SelectMultiUser`, `_RemainLeave`, `_HomeStatus`, `_CurrentTime`, `_UserName`, `Error` + `_ViewStart`

**بر اساس controller:**
- Login: Login, ChangePassword, ChangePasswordForce, CheckLock
- Home: Index, About
- Request: New, FromUser, ShowPhoto
- Late/Early/DailyLeave/HourlyLeave/DailyDuty/HourlyDuty: Add + Edit (هرکدام)
- OverTime: Add
- ConstOT: Index, Edit
- Reward: Add
- Record: Add, Edit
- AcceptRequest: Index + `_RequestsList`
- ShowRequest: Index
- ShowUsersRequest: Index
- EditRequest: Index
- Undo: Index
- Deputy: Index + `_CurrentDeputy`
- SelectArea: Index
- Settings: Index
- RemainLeave: Index, ForPersonnel, GetForUser
- Report: Index (+ `Report.aspx`)
- LeavesReport: Index
- LocationReport: Index
- OverTimeReport: Index, Show
- PersonnelStatus: Index
- ViewR: Index

#### Report.aspx (تک‌نمونه WebForms)

ReportViewer 10 با **تعریف گزارش پویا از دیتابیس**:
- `ReportController.GetSchema(cookie, params)` → RDL، `GetData(...)` → `DataSet1`
- پارامترها: `ReportID, isSelf, SelectedBadgeNumbers, exportType, FromDateHijri, ToDateHijri, Page, action`
- صفحه‌بندی دستی (دکمه‌های «صفحه قبل/بعد» با POST به `/Report/Report{?params}`)،
- عنوان: `"{bode5} - {Report}"`، تاریخ‌ها هجری، خروجی (export) مشروط به `ReportExport=True`
- cookie احراز هویت: `mytimeauth` (ارزش رمزنگاری‌شده)

### ۲.۸ دارایی‌های فرانت‌اند

**Scripts:** jquery-3.1.1 (slim/min + 3.6.1 + 1.11.2 برای aspx), jquery-ui-1.12.1,
jquery.validate(.unobtrusive) + additional-methods, jquery.unobtrusive-ajax, knockout-3.5.1,
bootstrap 3.3.7 (+select), select2.min, datatables.net + datatables.net-bs4,
persian-datepicker.js, JsFarsiCalendar.js, jquery.ptTimeSelect.js, jquery.maskedinput.js,
spin.js, menuScript.js, index.js, Select2script.js, ru.js, modernizr-2.8.3.

**Content/CSS:** Main.css, style.css, bootstrap(.min/-theme), calendar.css, font-awesome.css,
jquery-ui.css, persian-datepicker.css, select2.min.css, jquery.ptTimeSelect.css, bootstrap-select.min.css, themes/.
**فونت‌ها:** FontAwesome (otf/eot/svg/ttf/woff/woff2) + glyphicons. **RTL کامل، UI فارسی.**

### ۲.۹ پیکربندی — ۳۷ appSetting

| کلید | مقدار | معنی (استنتاجی از نام/مصرف) |
|---|---|---|
| LockIP | 127.0.0.1 | IP مجاز برای برخی عملیات قفل |
| Captcha | true | CAPTCHA در لاگین |
| CheckT5Lock | false | فعال/غیرفعال بررسی قفل T5 |
| LogoPath | e:\kusha.png | مسیر لوگو |
| webpages:Version / Enabled | 2.0.0.0 / false | زیرساخت WebPages |
| PreserveLoginUrl | true | حفظ URL هدف بعد از لاگین |
| ClientValidationEnabled / UnobtrusiveJavaScriptEnabled | true | اعتبارسنجی سمت کلاینت |
| **LeaveID** | 2 | شناسه مرخصی اصلی |
| **Limit1LeaveID** | 1 | شناسه مرخصی محدودیت‌۱ |
| ReportExport | True | نمایش کنترل export گزارش |
| HideLeaveTypes | 0 | مخفی‌سازی نوع مرخصی |
| ShowDays | 30 | بازه نمایش روزها |
| CookiesTime | 20 | عمر cookie (دقیقه) |
| CookiesSecure | false | پرچم Secure |
| **RequestsLock** | 42972 | مقدار/کلید قفل درخواست‌ها |
| **RequestRecordsDailyLimit** | 15 | سقف درخواست ثبت در روز |
| **RequestRecordsMonthlyLimit** | 16 | سقف درخواست ثبت در ماه |
| ValidLateEarly | True | فعال دیر/زودرس |
| ViewDailyDuty / ViewHourlyDuty | True | نمایش کار روزانه/ساعته |
| ValidHourlyFree | False | ساعت آزاد |
| **HoursOfDays** | 8 | ساعت کاری روز |
| ViewDailyLeave / ViewHourlyLeave | True | نمایش مرخصی روزانه/ساعته |
| **MaxRequestAgeDays** | 400 | حداکثر قدمت درخواست |
| ViewRewPen | True | نمایش پاداش/کیفر |
| RequestRecord | True | فعال درخواست ثبت |
| ViewConstOT | True | نمایش اضافه‌کاری ثابت |
| **LastDayForRequest** | 300 | آخرین روز مجاز درخواست |
| ViewOverTimeRequest | True | نمایش درخواست اضافه‌کاری |
| RequestForUsers | True | درخواست برای دیگران |
| ExportReport | True | خروجی گزارش |
| ChangePassRestriction | True | محدودیت تغییر رمز |
| ChangePassMonthly | True | تغییر ماهانه رمز |
| **ChangePassLimit** | 3 | سقف تغییر رمز |
| ChangePassForce | False | اجبار تغییر رمز |

---

## ۳. نقشه‌ی عملیاتی (رفتارهایی که باید ۱:۱ بازتولید شوند)

1. **ورود/خروج:** فرم Login + CAPTCHA (CaptchaMvc)، بررسی `LockIP`، `CheckLock/RetryCheck` (قفل T5)، نشست cookie `mytimeauth` (رمزنگاری‌شده، ۲۰ دقیقه)، `GetRole`، پرچم‌های تغییر رمز (ماهانه/اجباری/سقف ۳)، About، ChangePassword(Force).
2. **قفل:** `LockCheker.Check` با `CheckT5Lock`/`RequestsLock=42972`/`RequestLockValuee`/`SetRequestsLock` — قفل‌های لایسنسی T5.
3. **داشبورد (Home):** شمارنده‌ها (MakeCount/GetTableData)، گزارش روزانه جزئی (GetDailyDetailsReport/CalculateDailyAbsence/ForNDayReport)، درخواست‌های معلق (GetSuspendRequests/GetRequestsFor)، About/MyBanner، منوی کناری و نوارابزار (SideMenu/Toolbar/ToolbarShow/SetToolbar/GetToolbars)، اعلان Noti5 (CanDownloadNoti5/GetNoti5).
4. **درخواست‌ها (۸ نوع + ثبت + پاداش/کیفر):** دیر (Late)، زود (Early)، مرخصی روزانه (DailyLeave)، مرخصی ساعته (HourlyLeave)، کار روزانه (DailyDuty)، کار ساعته (HourlyDuty)، اضافه‌کاری (OverTime)، اضافه‌کاری ثابت (ConstOT)، پاداش/کیفر (Reward)، ثبت (Record). هرکدام: Add/Edit + فرم مشترک `_RequestForm` + اعتبارسنجی از **قواعد TD5** (سقف‌های LeaveType: MaxHoursInDay/MaxHoursInMonth/MaxDailyInYear/MaxDailyInMonth/EachXHoursADay/MaxMinus/OnlyOne/GetInHolidays/DecreaseFromLeaveID + سقف‌های appSettings: DailyLimit=15, MonthlyLimit=16, MaxRequestAgeDays=400, LastDayForRequest=300, HoursOfDays=8, ShowDays=30) + باقی‌مانده مرخصی (RemainLeave/GetRemainD/CheckDailyLeave/CheckRemainDays/GetRecordRequestsCount) + موقعیت جغرافیایی (AddLocationRequest/LatLng/LocationReport).
5. **تأیید/انصراف:** ShowUsersRequest (لیست درخواست‌های زیرمجموعه + getStatus)، ShowRequest، AcceptRequest (تک‌تک/همه: AcceptByID1/2/0، AcceptAll، CancelAll، InsertData)، EditRequest (ویرایش/لغو/حذف/CheckRequestTime)، Undo (برگرداندن + DeleteData)، اعلان NewRequestAdded (Notify: Index/SetViewed).
6. **گزارش‌ها:** Report (فیلتر: شخص/کارکنان/بازه هجری/صفحه → RDL پویا + ReportViewer + export)، TotalCalcsReport، DailyReport، LeavesReport (+isObs روزهای تعطیل)، LocationReport (استعلام موقعیت + عکس + ShowPhoto)، OverTimeReport (Show + قبل/بعد تایید/لغو: Change/Accept/Cancel Before/After)، PersonnelStatus، ViewR (AJAX باقی‌مانده).
7. **مدیریت:** Settings (Change/LoadSettings/LoadValue — خواندن همان appSettingsها)، SelectArea (واحدها/منطقه‌ها: GetAreas/GetArea/DeleteArea/InsertNew/GetUserAreas)، Deputy (نماینده: GetState/Add/Delete)، ChangePass، SelectArea.

---

## ۴. برنامه بازنویسی در پایتون

### ۴.۱ تصمیمات معماری

| مورد | انتخاب | دلیل |
|---|---|---|
| فریم‌ورک | **Django (≥4.2)** | نزدیک‌ترین هم‌ترازی با ASP.NET MVC (نمای سرور-ساید + template + session/auth + admin)، ساده‌ترین مسیر برای ۵۹ نمای Razor→Jinja2 |
| رندر | Jinja2 + Bootstrap 3.3.7/JQuery/knockout/datatables **همان دارایی‌ها** | وفاداری ظاهری بدون بازطراحی |
| DB | **pyodbc → SQL Server** | دیتابیس مشترک با نسخه مرجع (قید پروژه) |
| تاریخ شمسی | **jdatetime** | معادل `System.Globalization.PersianCalendar` + `Personnel.Hijri.Converter` |
| دیتا‌لایر | بازسازی **TD5** به‌صورت Python query-builder پارامتری (Dapper-style) + کلاس‌های Personnel DA (SQL مستقیم مطابق EF) | رفتار queryها ۱:۱ |
| CAPTCHA | کتابخانه پایتون با **رفتار** مطابق CaptchaMvc (مقادیر session/تأیید) | وفاداری عملیاتی |
| گزارش | `GetSchema/GetData` → RDL + **رندر RDL** (رندرر پایتونی برای زیرمجموعه RDL مورد استفاده؛ نیازمند IL T5V3.Report/ReportGenerator از همین کپی‌ها) | ReportViewer 10 پایتون ندارد — بزرگ‌ترین ریسک |

> اصول: هیچ رفتار/برچسب/روالی تغییر نمی‌کند؛ UI فارسی می‌ماند؛ اعتبارسنجی فقط استاتیک
> (هیچ assembly مرجعی اجرا نمی‌شود)؛ هر صفحه «تمام» نیست مگر عملیات+نتیجه در برابر مرجع تأیید شده باشد.

### ۴.۲ مراحل (به‌ترتیب، با وابستگی)

**مرحله ۰ — اسکلت و زیرساخت مشترک**
1. ساخت repository/package `twf-re-python` (ساختار مشابه `t5web-re-python`): settings، config reader (۳۷ appSetting + ۳ اتصال)، logging، test harness.
2. ماژول تاریخ شمسی: `ToLongPersianDate`/`DayOfWeekToString`/تبدیل Hijri (از IL `Misc` و `Personnel.Hijri.Converter`) + تست‌های جفت (jdatetime ↔ ارقام مرجع).
3. `Encryption` (Encrypt/Decrypt از IL) → همان الگوریتم/کلیدها → سازگاری کامل cookie `mytimeauth` با نسخه مرجع (به‌گونه‌ای که cookie مرجع در پایتون readable باشد و برعکس).
4. ابزار RE: اجرای decoder/verifier `t5web-re-python` روی **کپی‌های T5V3.* داخل TWF** (۷ DLL) → evidence جداگانه `evidence_twf/*.json` + جدول IL تأییدشده (به‌ویژه Report/ReportGenerator/Model که build متفاوت‌اند).

**مرحله ۱ — لایه داده**
5. بازسازی TD5: `Repo` + query-builder (Where/OrderBy/Tolist/First/Any/Execute/Select) روی ستون‌های دقیق ۶ service (UserInfo، UserRule، DepartmentRule، Calendar، LeaveType، Request) — از IL هرکدام.
6. مدل‌های `TD5.Model.Request` + `DapQuery`/`DapType` و `Init.Start/GetValueByName/GetInitialCatalog`.
7. لایه Personnel: کلاس‌های DA ( UserInfoDA، DepartmentDA، DeviceDA، GateDA، ...) — تبدیل EF `DB.PersonnelMdl` (csdl/ssdl/msl خارج‌شده از Personnel.dll) به SQL پایتون؛ `DBManager`.
8. پل `GlobalHelper` (WithTimeApp، GetLoggedInUserInfo، AddLeaveTypes/OverTime/RewardPenalty/Duty/Descriptions، GetRule).

**مرحله ۲ — احراز هویت و قفل**
9. `LoginManager` (Login/Logout/GetRole/IsExpired/GetBadgeNumber) + cookie session (CookiesTime=20) + `AuthorizeFilter` (HandleUnauthorizedRequest/AuthorizeCore).
10. `LockCheker` (Check/StringToDateTime/GetToday) + اکشن‌های CheckLock/RetryCheck + RequestsLock=42972.
11. CAPTCHA + `TestUserPass` + `ChangePassword(Force)` با پرچم‌های ChangePass*.

**مرحله ۳ — نماها و کنترلرها (۵۹ نمای Razor → Jinja2)**
12. `_Layout/_Menu/_Toolbar/_RequestForm/_SelectMultiUser/_RemainLeave/_HomeStatus/_CurrentTime/_UserName/_ToolbarSettings` + همه رشته‌های `PersianString` به‌عنوان کتالوگ مترجم.
13. Login (۴) → Home (۲ + AJAX اکشن‌ها) → Request/New/FromUser/ShowPhoto.
14. هشت فرم درخواست + Edit (۱۶ نمای Add/Edit) + ConstOT + Reward + Record — با validation دقیق TD5/appSettings و باقی‌مانده (RemainLeave/GetRemainD/CheckRemainDays/GetRecordRequestsCount).
15. تأیید/انصراف: ShowUsersRequest، ShowRequest، AcceptRequest (+_RequestsList)، EditRequest، Undo، Notify.
16. مدیریت: Settings، SelectArea، Deputy، ChangePass.
17. گزارش‌های غیر-RDL: LeavesReport، LocationReport، OverTimeReport، PersonnelStatus، ViewR، RemainLeave (ForPersonnel/GetForUser).

**مرحله ۴ — موتور گزارش (RDL پویا)**
18. `ReportController.GetSchema/GetData` (پارامترهای ReportID/isSelf/SelectedBadgeNumbers/exportType/FromDateHijri/ToDateHijri/Page/action) + `AddReportsToViewBag/GetPersonnel/TotalCalcsReport/DailyReport`.
19. رندرر RDL پایتون: استخراج IL `T5V3.Report`/`T5V3.ReportGenerator` (کپی TWF) → درک ساختار RDL تولیدی → رندر HTML/PDF مطابق رفتار ReportViewer 10 (صفحه‌بندی، عنوان، export). اگر زیرمجموعه RDL ساده باشد، رندر اختصاصی کافی است؛ در غیر این‌صورت bridge به رندرر خارجی مجاز است **صرفاً برای همان خروجی** (رفتار صفحه باید حفظ شود).
20. صفحه Report.aspx → معادل پایتون (دکمه‌های صفحه قبل/بعد + POST `/Report/Report`).

**مرحله ۵ — فایل‌های مجازی و نکات باقی‌مانده**
21. `CustomVirtualPathProvider` (Decrypt/FileExists/GetFile) + `CustomVirtualFile.Open` — همان مسیرها و الگوریتم.
22. `TINYLib` — درک IL و جایگزینی رفتارمند (احتمالاً helper رویداد/interop).
23. `Noti5` (CanDownloadNoti5/GetNoti5) + `NotificationManager.NewRequestAdded`.
24. WebApi: بررسی runtime routes ثبت‌شده؛ اگر endpoint فعالی وجود دارد، پیاده‌سازی (در assembly ApiController نیست — احتمالاً صرفاً scaffold).

**مرحله ۶ — راستی‌آزمایی وفاداری (پذیرش)**
25. تست دود: کل routes (از RouteConfig + اکشن‌ها) در برابر مرجع (کد وضعیت + ساختار پاسخ).
26. تست عملیاتی هر جریان (بخش ۳): عمل در نسخه مرجع → عمل در پایتون (همان DB) → **تطابق DOM + مقادیر + وضعیت پایانی DB**.
27. diff بصری ۵۹+۱ صفحه (اسکرین‌شات جفت‌به‌جفت، RTL/فونت/جدول).
28. ماتریس traceability: اکشن/نماد/قاعده IL → فایل پایتون → تست → نتیجه (مشابه docs/33).

### ۴.۳ تخمین حجم (بر اساس روشده)

| ناحیه | واحد کار |
|---|---|
| MyTime3 — ۳۳ کنترلر + ۳۷ مدل + زیرساخت | ~۱۶۲۳ method (عمده‌ی حجم) |
| TD5 — ۶ query service + Init/Repo/Model | ~۵۸۹ method |
| Personnel — DB/Model (سهم وب) | ~۱۰۰۰ method مرتبط (از ۳۲۵۰؛ UI زک/WinForms خارج) |
| T5V3.Report/ReportGenerator/Model (کپی TWF) | ~۲۶۰۰ method (decoder موجود) |
| نماها | ۶۰ + ۱۱ Shared = ۷۱ قالب |

### ۴.۴ ریسک‌ها و نکات باز

1. **ReportViewer 10** — بزرگ‌ترین ریسک؛ رندر RDL پایتون لازم است (بخش ۱۹).
2. **build متفاوت T5V3.*** در TWF — باید decode/verify دوباره روی این کپی‌ها (بخش ۰-۴).
3. **الگوریتم Encryption** — باید دقیقاً استخراج شود تا cookieها سازگار باشند.
4. **CustomVirtualPathProvider** — مسیر/الگوریتم باید از IL.
5. **زک/USB** — دسکتاپ؛ در دامنه وب نیست ولی اسکیمای DB مشترک است (جدول‌های Device/Record/Gate/USB...).
6. **TINYLib** — کاربرد دقیق بعد از خواندن IL.
7. **WebApi routes** — راستی از runtime مرجع (فقط مشاهده، بدون اجرا توسط ما: از deployment موجود).
8. **قفل‌های لایسنس** (RequestsLock=42972) — رفتار باید هم‌سان باشد بدون دور زدن لایسنس (قید پروژه: بدون license bypass).

### ۴.۵ ترتیب پیشنهادی اجرا

```
۰ (skel+config+date+encryption+RE-on-TWF-copies)
→ ۱ (TD5 + Personnel DA)
→ ۲ (auth+lock+captcha)
→ ۳ (نماها/اکشن‌ها — گروه‌به‌گروه با تست)
→ ۴ (RDL reports)
→ ۵ (virtual files + TINYLib + Noti5 + WebApi)
→ ۶ (parity acceptance)
```

هر گروهِ مرحله ۳ به‌محض تمام‌شدن، با تست عملیاتی همان گروه (در برابر مرجع) بسته می‌شود؛
ادامه با گروه بعد فقط بعد از سبز‌شدن تست‌های گروه قبلی.
