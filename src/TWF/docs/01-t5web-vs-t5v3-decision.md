# سند تصمیم فنی — ادامه بازنویسی روی `T5Web` یا `T5V3`؟

**تاریخ:** ۱۴۰۴/۰۸/۰۳ · **وضعیت:** پیشنهاد، در انتظار تأیید
**پیش‌زمینه:** بازنویسی `TWF 4.28` با نام «آریا» به پایان رسیده (۴۱ تست، ۰ خطا). این سند انتخاب
هدف بعدی را با شاهد عددی از خود مخزن تعیین می‌کند.

---

## ۱. خلاصه تصمیم

| | **ادامه روی `T5Web [3.5.7]`** ✅ | ادامه روی `T5V3.9.2` ❌ |
| :--- | :--- | :--- |
| هم‌پشته با آریا | **بله** — ASP.NET MVC 5 / .NET 4.7.2 / Razor | خیر — WinForms دسکتاپ |
| قابلیت اجرا روی سرور آفلاین | **بله** (IIS، بدون دستگاه) | خیر (نیازمند ویندوز + دستگاه + دانگل) |
| قابلیت تأیید خودکار در این محیط | **بله** (همان الگوی ۴۱ تست) | خیر |
| اتصال به دستگاه | **HTTP Push** (`iclock` / `ATTLOG`) — بدون SDK بومی | SDK بومی: `Sensor.dll`, `Push.dll`, `T5V3.ZK.DLL`, USB |
| قفل | فقط `LockIP` + `LockType=Tiny` در Web.config + صفحه `ChangeLock` | **`Interop.TINYLib.dll` (دانگل سخت‌افزاری واقعی)** |
| هم‌پوشانی با کد بازنویسی‌شده | فقط ۵ کنترلر از ۶۴ | — |

**تصمیم پیشنهادی: هدف بازنویسی بعدی = `T5Web`، و `T5V3` به‌عنوان «مرجع موتور محاسبات» حفظ شود.**

---

## ۲. شاهد عددی (اندازه‌گیری‌شده از مخزن)

| سنجه | `T5Web [3.5.7]` | `T5V3.9.2` |
| :--- | ---: | ---: |
| تعداد فایل | ۳۸۰ | ۲۹ |
| حجم | ۴۸ MB | ۲۲ MB |
| نوع برنامه | Web (ASP.NET MVC 5) | Desktop (WinForms) |
| فریم‌ورک | .NET **4.7.2** | .NET Framework (WinForms) |
| تعداد کنترلر MVC | **۶۴** | ۰ |
| تعداد ویو | **۱۱۲** `.cshtml` + ۹ `.aspx` | — |
| خطوط ویو | **۱۹٬۴۴۲** | — (UI داخل فرم‌های کامپایل‌شده) |
| تعداد فرم دسکتاپ | — | **۱۳۵** فرم `frm*` |
| منبع قابل دسترس | Precompiled (`PrecompiledApp.config`, ۰ فایل `.cs`) | باینری (`T5V3.UI.DLL` = ۳.۷ MB) |
| گزارش‌ها | ۱۰ فایل **RDLC** جاسازی‌شده + `ReportViewer.WebForms` | RDLC + `ReportViewer.WinForms` |
| ORM | EF 6.4.4 + OData + WebAPI | EF + WebAPI SelfHost |

### وابستگی‌های سخت‌افزاری/قفل — تفاوت تعیین‌کننده

| مورد | T5Web | T5V3 |
| :--- | :---: | :---: |
| `Interop.TINYLib.dll` (دانگل) | ❌ ندارد | ✅ دارد |
| `T5V3.ZK.DLL` (ZKTeco بومی) | ❌ ندارد | ✅ دارد |
| `Sensor.dll` (WPF/سنسور) | ❌ ندارد | ✅ دارد |
| `Push.dll` | ❌ ندارد | ✅ دارد |
| `LockIP` / `LockType=Tiny` در پیکربندی | ✅ دارد (فقط تنظیم) | ✅ + دانگل واقعی |
| صفحه فعال‌سازی لایسنس (`ChangeLock`: Activation / PersonnelLimit / StationLimit / DeviceLimit / DemoLimit / TimeAppLimit) | ✅ دارد | ✅ دارد |
| دریافت تردد از دستگاه روی HTTP | ✅ **بله** (`ATTLOG`, `EnrollUser`, `chgFP`, `timesyncdata`) | ❌ خیر (بومی) |

> **نکته کلیدی:** `T5Web` یک نقطه پایانی کامل پروتکل Push دستگاه ZKTeco دارد
> (`iclock`، `cdata`، `ATTLOG`، `EnrollUser`). یعنی دستگاه‌ها مستقیماً با سرور وب حرف می‌زنند و
> **برای اتصال دستگاه هیچ نیازی به برنامه دسکتاپ نیست**. این دقیقاً با الزام «۱۰۰٪ آفلاین روی شبکه داخلی» می‌خواند.

---

## ۳. چرا `T5Web`؟

**۱) هم‌پشته بودن با آریا (بزرگ‌ترین صرفه‌جویی).**
آریا همین حالا روی ASP.NET MVC 5 / .NET 4.7.2 است. یعنی برای `T5Web` می‌توان دوباره استفاده کرد:
لایه‌های `TWF.Domain` / `TWF.Infrastructure` / `TWF.Application`، پروژه `TD5` (Dapper)،
آداپتور `InMemoryT5BackendAdapter`، هارنس ۴۱ تست، و کل سیستم UI صنعتی آفلاین.
برای `T5V3` باید یک محصول **دوم** با پشته‌ای کاملاً متفاوت (WinForms) ساخته شود.

**۲) قابلیت تأیید در همین محیط.**
بازنویسی WinForms را نمی‌توان اینجا ساخت و تست کرد (نه ویندوز، نه دستگاه، نه دانگل). آریا با
`InMemory` + ۴۱ تست خودکار تأیید شد؛ همان مسیر برای `T5Web` باز است.

**۳) کار افزودنی است، نه تکراری.**
از ۶۴ کنترلر `T5Web` فقط **۵** کنترلر قبلاً در آریا بازنویسی شده:

```
هم‌پوشانی (۵): Deputy, Home, OverTimeReport, PersonnelStatus, ShowUsersRequest
فقط در T5Web (۵۹): Account, AssignRule, AssignShift(Group), Calendar, Card, CardexReport,
  Certificate, ChangeLock, CustomWF, DailyReport, Dashboard, DashboardItem, Department,
  DeptsChart, Device, DeviceOperations, EditLateEarly, EmpType, ExitReport, ExitType, Export,
  Gate, GridReport, ImportFromExcel, InsertDailyLeave, InsertRecord, LateEarlyReport,
  LeaveDescription, LeaveType, Manager, Parameters, Permit, PersonnelStatusReport, Ramezan,
  Records, RemainedLeavesReport, ReportDatas, ReportDesign, ReportFilter, ReportTablix,
  ReportTablixColumn, ReportTextbox, ReportTitles, RequestsReport, Schedule, ShowReport,
  SumReport, TempSchedules, Title, TotalControl, TransferRemainedLeave, USBManagement,
  UserAddProperty, UserInfo, UserReport, UserSelection, ViewSystemLogs, Workflow, iclock
```

آریا = **پرتال خودخدمتی کارمند**؛ `T5Web` = **کنسول مدیریت**. کنار هم، یک سامانه کامل می‌سازند.

**۴) بدون قفل سخت‌افزاری.** تنها ردیف‌های قفل در `T5Web` دو تنظیم متنی و یک صفحه UI است که حذفشان
بی‌هزینه است. در `T5V3` قفل یک **دانگل واقعی** (`Interop.TINYLib.dll`) است.

---

## ۴. چرا `T5V3` را دور نمی‌ریزیم؟

`T5V3` مرجع سه چیز است که باید بازنویسی شوند اما از آن‌ها کپی می‌گیریم:

| دارایی در T5V3 | چرا مهم است |
| :--- | :--- |
| `T5V3.Attendance.dll` + `T5V3.LeaveCalcs.DLL` | موتور محاسبه کارکرد/مرخصی — همان چیزی که آریا هنوز به آن وابسته است |
| `T5V3.DB.dll` + `T5V3.Model.dll` | قرارداد داده و دامنه |
| ۱۳۵ فرم `frm*` | مرجع رفتار دقیق صفحه‌های مدیریتی (مانند `frmTotalControl`, `frmReportDesigner`, `frmYearRemainLeave`) |

### ریسک واقعی که این بررسی آشکار کرد ⚠️

هر دو محصول **همان خانواده موتور** را با **دو نسخه واگرا** حمل می‌کنند:

```
T5V3.DB.dll                desktop=434,688 bytes   web=428,544 bytes   ← دو باینری متفاوت
T5V3.Model.dll             desktop=103,424 bytes   web=104,960 bytes   ← دو باینری متفاوت
T5V3.Attendance.dll        desktop= 84,992 bytes   web= 84,992 bytes   ← اندازه یکسان، محتوا متفاوت
T5V3.Misc.dll              desktop= 46,080 bytes   web= 46,080 bytes   ← اندازه یکسان، محتوا متفاوت
```

یعنی الان دو «حقیقت» از قوانین محاسبه در سازمان وجود دارد (دسکتاپ ۳.۹.۲ در برابر بسته وب ۳.۵.۷).
**این مهم‌تر از انتخاب UI است و باید در فاز اول حل شود.**

---

## ۵. برنامه پیشنهادی (۵ فاز، به همان سبک آریا)

| فاز | کار | خروجی قابل تأیید |
| :--- | :--- | :--- |
| **A** | استخراج سورس از `T5V3Web.dll` (precompiled) با ILSpy + فهرست کامل ۶۴ کنترلر، مدل‌ها و `Persian` (منابع) | درخت `OriginalDecompiled/T5Web/` + گزارش تطابق |
| **B** | **یکسان‌سازی موتور**: انتخاب یک نسخه واحد از `T5V3.*` + حذف واگرایی | `ENGINE.md` + تست برابری خروجی محاسبات |
| **C** | **Zero-Lock**: حذف `LockIP`/`LockType`/`ChangeLock`/`USBManagement` و هر بررسی لایسنس | تست `TEST-LOCK-*` |
| **D** | پورت ۵۹ کنترلر مدیریتی به معماری آریا + حفظ `iclock` (دریافت تردد) | `TWF.Admin` (یا ادغام در `TWF.Web`) |
| **E** | UI صنعتی آفلاین برای ۱۱۲ ویو + جایگزینی ۱۰ گزارش RDLC با موتور گزارش آفلاین آریا + تست نهایی | سوئیت کامل، صفر ارجاع خارجی |

### یک تصمیم لازم از طرف شما

دو راه برای ساختار تحویل وجود دارد:

| گزینه | توضیح | مزیت | ریسک |
| :--- | :--- | :--- | :--- |
| **۱. ادغام در یک سامانه آریا** (پیشنهاد من) | یک Solution، یک استقرار IIS، نقش‌محور: `user` = پرتال خودخدمتی، `admin` = کنسول مدیریت | یک موتور، یک احراز هویت، یک UI، نگهداری ساده‌تر | مهاجرت بزرگ‌تر؛ باید مرز دسترسی‌ها دقیق تفکیک شود |
| **۲. دو برنامه جدا** | آریا (کارمند) + «آریا ادمین» (مدیریت) با کد مشترک | استقرار تدریجی، ریسک کمتر در گام اول | دو بسته، دو پیکربندی، احتمال واگرایی دوباره |

---

## ۶. صداقت فنی

- همه اعداد این سند از خود مخزن اندازه‌گیری شده‌اند (تعداد فایل، حجم، `strings` روی اسمبلی‌ها، شمارش کنترلر/فرم/ویو).
- **تعداد کنترلرها و فرم‌ها از متادیتای اسمبلی استخراج شده**، نه از سورس؛ چون `T5Web` به‌صورت
  `Precompiled` تحویل داده شده و **هیچ فایل `.cs` ندارد** (۰ فایل). فاز A برای همین لازم است.
- ادعای «بدون نیاز به دسکتاپ برای دستگاه» بر پایه وجود اکشن‌های پروتکل `iclock/ATTLOG/EnrollUser` در
  `T5V3Web.dll` است؛ پیش از فاز D باید با یک دستگاه واقعی صحت‌سنجی شود.
- زنجیره ابزار بیلد (`csc`/`dotnet`/ILSpy) در این محیط پس از ری‌استارت پاک شده است و برای فاز A باید بازسازی شود.
