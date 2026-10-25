# تحلیل شکاف T5Web ↔ T5V3 و برنامهٔ افزودن قابلیت‌ها

این سند پاسخ به این پرسش است: «سامانهٔ وب (آریا ادمین) با نرم‌افزار دسکتاپ T5V3 چقدر تفاوت دارد و چه چیزی از آن باید به وب اضافه شود؟»

روش کار فقط خواندنی و از روی شاهد بوده است: ۸۱ فرم `T5V3.UI.DLL`، ۴۶ کلاس `*DA` در `T5V3.DB.dll`، ۱۲۹ جدول `db/schema-manifest.json`، ۲۴ کلید `Settings.xml` نسخهٔ دسکتاپ و ۳۴۳ کنش نسخهٔ وب قدیم. هیچ جدولی از روی نام حدس زده نشده است.

---

## ۱. تفاوت موتور و داده: عملاً صفر

| لایه | T5V3 ۹.۲ (دسکتاپ) | T5Web ۳.۵.۷ (وب قدیم) | آریا ادمین |
| :-- | :-- | :-- | :-- |
| دامنه‌های داده (`*DA`) | ۴۶ | همان ۴۶ (صفر تفاوت) | روی همان اسکیمای ۱۲۹ جدول |
| `T5V3.Attendance` | ۸۲۲ شناسه | **تفاوت شناسه‌ها = ۰** | فراخوانی نمی‌شود (محاسبه در موتور می‌ماند) |
| `T5V3.LeaveCalcs` | — | تفاوت = ۰ | — |
| تنها تفاوت واقعی | ۹.۲ `CustomWF` را حذف کرده | ۳.۵.۷ دارد | ماژول `customworkflow` برای پایگاه‌های قدیم نگه داشته شده |
| سطح UI | ۸۱ فرم WinForms | ۶۷ کنترلر / ۳۴۳ کنش | ۲۰ کنترلر / ۱۷۸ مقصد + ۵۱ ماژول + ۱۲ گزارش |

نتیجه: مزیت T5V3 **در UI دسکتاپ و اتصال بومی سخت‌افزار** است، نه در داده و منطق حضور. بازنویسی وب چیزی از منطق موتور را از دست نداده است.

## ۲. نگاشت ۸۱ فرم دسکتاپ

| وضعیت | تعداد | نمونه |
| :-- | --: | :-- |
| پوشش کامل | ۴۶ | `frmMain`، `frmRule`، `frmShift`، `frmSchedule`، `frmCalendar`، `frmTotalControl`، `frmValidLeave`، `frmRamezan`، `frmParameter`، `frmViewLog`، ۱۱ گزارش |
| پوشش نسبی | ۶ | `frmCard` (کارتکس خواندنی)، `frmEditOverTimes`، `frmLeavesReport`، `frmAddDailyLeave` |
| شکاف قابل انتقال | ۱۱ | تصاویر (۳ فرم)، `frmPosition`، `frmA3Report`، `frmCalcsReport`، `frmLeaveDescription`، `frmEmpLeaveType`، `frmPishCards`، `frmDataBase`/`frmManageDatabase`، `frmSystemOptions` |
| عمداً دسکتاپ می‌ماند | ۱۰ | طراح RDLC (۸ فرم) + `frmReportViewer` + `frmChangeLockType` ⛔ |
| بی‌معنا در وب | ۸ | `frmLogin`/`frmRamz` (Negotiate ویندوز)، `frmLogo`، `frmAbout`، انتخاب‌گرها |

## ۳. موج ۱ (تحویل‌شده در همین تغییر)

| قابلیت | منبع در T5V3 | پیاده‌سازی |
| :-- | :-- | :-- |
| آرشیو تصاویر پرسنل + برگهٔ چاپی | `frmPhoto`، `frmLastPhotos`، `frmPhotosReport` | `PhotosController` (`Index`/`Image`/`Sheet`) روی `[UserInfo].[Photo]` (نوع `image` در DDL)، گیت `PersonnelPhoto`، فقط‌خواندنی، سقف ۴ مگابایت، تشخیص Content-Type از بایت‌های جادویی، کش خصوصی |
| سهمیهٔ مرخصی نوع استخدام | `frmEmpLeaveType` | `Leave/Entitlements` + `IEntitlementStore` روی `[EmpTypeLeave]`؛ هر دو عمل idempotent |
| توضیحات مرخصی | `frmLeaveDescription` | ماژول عمومی `leavedescription` روی `[LeaveDescription]` (شاهد: `orm`) |
| لیست مرخصی‌ها | `frmLeavesReport` | گزارش `leaves` در `ReportCatalog` (گزارش دوازدهم) |
| مرخصی روزانه | `frmAddDailyLeave` | از قبل با ماژول `dailyLeaf` پوشش داشت (این مورد در موج ۱ فقط بازبینی شد) |

آزمون `T31` هر پنج مورد را می‌سنجد: وجود صفحه‌ها، گیت قابلیت، فقط‌خواندنی بودن تصاویر، سقف حجم، نرمال‌سازی Content-Type، idempotent بودن سهمیه، وجود ماژول و گزارش، و اینکه هیچ فایلی به قفل سخت‌افزاری اشاره نکند.

## ۴. چیزهایی که در موج ۱ **اضافه نشد** و دلیل مکتوب دارد

| مورد | دلیل |
| :-- | :-- |
| **ساعات کار هفتگی (`TimeTable`)** | جدول `[TimeTable]` بر اساس `engine/relations.json` در کاتالوگ **سرویس دستگاه** است، نه `ReportT5V3`. پورتال به آن کاتالوگ وصل نمی‌شود (همان قاعده‌ای که ماژول `option` را هم حذف کرد). صفحهٔ ساعات کار در دسکتاپ می‌ماند. |
| **قفل سخت‌افزاری/نرم‌افزاری** (`Interop.TINYLib`، `frmChangeLockType`، `PassCheckProps`، `KeyCodeConvert`) | ⛔ خلاف تصمیم ثابت Zero-Lock؛ هیچ‌گاه اضافه نمی‌شود. |
| **درایورهای بومی** (`T5V3.ZK.DLL`، `Sensor.dll`، `Push.dll`، `iclock`) | **دلیل قبلی این ردیف غلط بود.** اسمبلی‌های موتور `ILONLY + 32BITREQUIRED` هستند و app pool ما ۳۲-بیتی است (اثبات‌شده در CI: `enable32Bit=True`)، پس از نظر فنی *بارگذاری‌شدنی* هستند. دلیل درست دو چیز است: (۱) یک اپ وب نباید نشست طولانی دستگاه/push را نگه دارد — مال سرویس ویندوز است؛ (۲) `T5V3.LeaveCalcs` فقط از `T5V3.UI` صدا زده می‌شود و قرارداد عمومی‌اش مستند نیست. **تعامل دستگاه از طریق صف `[T5WebDeviceCommand]` انجام می‌شود — ولی فقط اگر `T5WebDeviceTransport` ست شده باشد؛ وگرنه دستور رد می‌شود (BUG-44، `docs/15`).** |
| **طراح گزارش RDLC** (۸ فرم، ۴۴ کنش) | در مرورگر قابل ایمن‌سازی و آزمون نیست؛ در دسکتاپ می‌ماند و دسترسی‌اش با گیت `ReportView` بسته شده. |
| **رمز محلی** (`frmRamz`، `ChangePass`) | احراز هویت با Negotiate + `WebUser=1` + `T5WebAccess`. |

## ۵. موج‌های بعدی (به ترتیب اولویت)

| موج | محتوا | ریسک |
| :-- | :-- | :-- |
| ۲ | نگهداری/پشتیبان‌گیری پایگاه (`frmDataBase`، `frmManageDatabase`)، تنظیمات سامانه (`frmSystemOptions`)، لاگ خام دستگاه (`RecordsLog*`)، پایش و پاسخ دستور (`DeviceStatus`/`CommandLog`)، لاگ‌های عملیاتی (`OperationLog`/`EvenetsLog`) | متوسط — نیاز به مجوز `BACKUP DATABASE` |
| ۳ | سِمَت سازمانی (`Position`/`PositionStep`/`UserPositionData`)، جانشین فردی (`PersonnelDeputy`)، سطوح دسترسی موتور (`AccessGroup`/`UserAccess`) | متوسط — نیاز به مدل‌سازی نوع ستون‌ها |
| ۴ | ویرایش مقادیر محاسبه (`EditedValue`)، کارتکس قابل اعمال (`frmPishCards`) | بالا — معنای این جدول‌ها در موتور است و بدون تعریف رسمی نباید بازسازی شود (`AllowValue` در موج ۳ اضافه شد) |
| ۵ | بیومتریک/الگوها (`Template`، `UserBioData`)، آلارم و لیست سیاه، واکسن، ژئوفنس (`Area`/`AreaPoint`)، گزارش A3/محاسبات | بالا — SQL این گزارش‌ها داخل `T5V3.Report.dll` است و در مخزن نیست |

## ۵-الف. موج ۲ و ۳

موج ۲ (لاگ‌ها و پایش) و موج ۳ (سازمان، دسترسی، ژئوفنس، پرونده پرسنل) در
`docs/11-waves-two-three.md` پیاده و مستند شده‌اند: ۲۱ ماژول تازه، خوانندهٔ سه لاگ و
صفحهٔ «سلامت داده». آنچه هنوز باز است: موج ۴ (`EditedValue`، پیش‌کارت) و موج ۵
(گزارش‌های A3/محاسبات).

## ۶. چیزهایی که از موج ۱ به بعد باید در هر تغییر رعایت شود

1. هر قابلیت جدید یک تست نگهبان داشته باشد (الگو: `T31`).
2. هر جدول تازه، هم در `engine/column-types.json` شاهد نوع داشته باشد و هم در `engine/relations.json` کاتالوگش مشخص شود؛ گیت‌های هفتم و هشتم هر دو را می‌سنجند.
3. هر صفحهٔ جدید: `AdminGate` + `ValidateAntiForgeryToken` روی هر `POST` + یک سطر حسابرسی برای هر نوشتن + یک ورودی در `MenuFor`.
4. هیچ وابستگی قفل/دانگل/شبکه اضافه نشود (گیت `lint-csharp.py` الگوی `TINYLib` را رد می‌کند).
