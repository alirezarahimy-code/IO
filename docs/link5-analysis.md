# Link5 v4.0.4 — بررسی فنی و کاربرد

**منبع:** پوشهٔ `Link5 v4.0.4` در همین مخزن (۱۲۷ فایل، ۲۰۱ مگابایت) به‌همراه `SDK 6.3.1.45`
و `NewPack Dlls 10.06.1398`.
**روش:** هیچ‌چیز این سند از «حدس» نیامده. DDL با پارس‌کردن `Link5.sql` و `Personnel.sql`
(هر دو UTF-16) خوانده شد، اسمبلی‌ها با `dnfile` باز شدند (نوع‌ها، رشته‌های UTF-16،
`.resources`)، و کانفیگ/لاگ/InstallLog واقعی خوانده شد. هر ادعا در جدول «شاهد» آمده است.

---

## ۱. در یک جمله

**Link5 نرم‌افزار «ارتباط با دستگاه» است، نه یک سامانهٔ حضور و غیاب.**
دستگاه‌های اثر انگشت/چهره/کارت را می‌خواند، ترددها را در پایگاه خودش جمع می‌کند و بعد
**به یکی از چند سامانهٔ مقصد تحویل می‌دهد** — که `T5V3` فقط **یکی از نُه** مقصد ممکن است.

نوشتهٔ خودِ برنامه روی صفحهٔ About (از `Link5.PersianString.resources`):

> «نرم‌افزار ارتباطی شرکت سامانه‌های بُعد پنجم - Link5»

و در رشته‌های باینری: `LegalCopyright: 5thDSystems 2014`. یعنی `5` در Link5 از
«بُعد پنجم» می‌آید؛ این محصول **برند خودِ سازندهٔ T5** است، نه یک برنامهٔ بیگانه.
این توضیح می‌دهد چرا «خیلی شبیه T5» است: هر دو یک SDK، یک `Personnel.dll` و یک
`LockIP` دارند.

---

## ۲. ساختار: چهار جزء، پنج سرویس

| جزء | فایل | نقش |
| :-- | :-- | :-- |
| دسکتاپ | `Link5.exe` (WinForms، .NET 4.8، x86) | مدیریت دستگاه‌ها، زمان‌بندی فراخوانی، خروجی‌ها، نقشه، کنترل دسترسی، پشتیبان‌گیری |
| سرویس همه‌کاره | `service/Comm5Service v4.exe` (`AllInOneService`) | سرور Push دستگاه + ذخیرهٔ تردد + هم‌گام‌سازی پرسنل + آپلود عکس/اثر انگشت |
| سرویس قدیمی‌تر | `Link5 Service` (`Link5.Service.exe`) | همان خانواده؛ فقط `pdb` و `.application` نسخهٔ ۱.۷۷.۲.۲ در پوشه مانده |
| Push v2/v3 | `PushServicev2`، `PushV3 Service` | نسل‌های قبلی Push؛ آثارشان در `*.InstallLog` هست |
| نگهبان | `Link5 WatchDog` | راه‌اندازی مجدد سرویس‌ها |
| کیت دستگاه | `SDK 6.3.1.45` → `plcommpro.dll`, `pltcpcomm.dll`, … | **ZKTeco Standalone SDK** برای ارتباط مستقیم با دستگاه (پورت ۴۳۷۰) |

`Link5.CommandListener.dll` یک سرور HTTP کوچک است؛ رشتهٔ `Server: Behrooz Jafari-HTTPAPI/2.0`
در آن مانده (نام توسعه‌دهنده).

---

## ۳. کاربرد اصلی: «تحویل تردد به مقصد»

enum `Link5.Model.OutputType` — مستقیماً از اسمبلی:

| مقدار | مقصد |
| :-- | :-- |
| `T5V3` | **T5 نسخهٔ ۳** (همین برنامه‌ای که روی سرور شماست) |
| `Chargoon` | چارگون |
| `NarmNegaran` | نرم‌نگاران |
| `Jahangostar` | جهان‌گستر |
| `PW` | دنیای پردازش |
| `Elmosanat` | علم و صنعت |
| `Database` | هر پایگاه SQL Server دلخواه، با **نگاشت ستون** |
| `TextFile` | فایل متنی |
| `WebServer` | سرور وب |

SQL هر مقصد داخل `Link5.Save.dll` است. برای T5V3:

```sql
IF NOT EXISTS(SELECT * FROM [Record] WHERE BadgeNumber={0} AND CheckTime='{1}')
INSERT INTO [Record](BadgeNumber, CheckTime, CheckType, MachineNumber, VerifyMethod,
                     WorkCode, IsValid, SourceType, OriginalSCheckType, InsertUser, InsertTime)
VALUES(...)
```

برای Chargoon `DataFile` + `ClockDmp`، برای نرم‌نگاران `TimeRecords`، و برای
«Database» عمومی: `INSERT INTO [{0}]([CardNo],[Station],[Year],…)` که جدول و ستون‌هایش
را کاربر در `frmOutput` انتخاب می‌کند.

**این مهم‌ترین نکتهٔ معماری است:** T5 «مقصد» است، نه «همتا». Link5 طراحی شده که هم‌زمان
به چند سامانه غذا بدهد؛ `ExportRecords` در تنظیمات هم همین را می‌گوید:
«نرم‌افزار نصب‌شده روی این سیستم ترددها را انتقال دهد».

### مسیر یک تردد

```
دستگاه ZK ──(SDK 4370 / ADMS-push روی 8081)──▶ Comm5Service v4
                    │
                    ▼
        Link5.dbo.RecordsLog  (+ Temp / Invalid)
                    │  آداپتر خروجی (Link5.Save)
                    ├──▶ ReportT5V3.dbo.Record        ← T5V3
                    ├──▶ Chargoon: DataFile, ClockDmp
                    ├──▶ NarmNegaran: TimeRecords
                    └──▶ هر جدول دیگر با نگاشت ستون
```

منبعِ تردد هم چهار حالت دارد (`RecordSource`): `Offline` (فراخوانی دوره‌ای)،
`Online`، `Push`، `Service`، `USB`.

### سرور Push

رشته‌های `Comm5Service v4.exe` پروتکل ADMS/iClock شرکت ZKTeco را کامل نشان می‌دهند:
`GET /iclock/cdata?SN=…&options=all&pushver=…`, `ATTLOG`/`OPERLOG`/`ATTPHOTO`/`BIOPHOTO`/`ERRORLOG`,
`Stamp=`/`OpStamp=`, `TransFlag`, `ErrorDelay`, `MultiBioDataSupport`.
مسیرهای سرویس: `/iclock/cdata`, `/uploadphoto`, `/SendToPushService`,
`/UploadUserBioData`, `/servercheck`.
لاگ واقعی (`Errorlog.txt`) همین را تأیید می‌کند:

```
TCP Listener http://192.168.21.112:8081/
LogNum=100, buffer:GET /iclock/cdata?SN=CPYE220460016&options=all&pushver=2.4.1…
```

---

## ۴. سه پایگاه داده

| پایگاه | جدول | نقش | رابطه با ما |
| :-- | --: | :-- | :-- |
| `Link5` | ۴۶ جدول + ۱ ویو (`AllData`) | خانهٔ خودِ Link5: دستگاه، زمان‌بندی، لاگ تردد، دسترسی، نقشه، کاربران وب | پورتال ما به آن وصل نمی‌شود |
| `Personnel` | ۳۰ جدول | **مشترک** با T5V3/T5Web/TWF: `UserInfo`, `Department`, `Device`, `Gate`, `DeviceMessage`, … | همان کاتالوگی است که T5Web ما می‌خواند |
| `VM5Te` (۱۹۲.۱۶۸.۲۱.۲۱۳) | — | سامانهٔ بیرونی (تصاویر/دوربین) که با `sa` به آن وصل می‌شود | بیرون از محدودهٔ ما |

سه جدول `Link5` نامشان **نقطه** دارد: `WebService.Options`، `WebService.Queue`،
`WebService.TermsOfApproval` — یعنی `CREATE TABLE [dbo].[WebService.Options]`.
(همین سه جدول در مدل ما غایب‌اند؛ توضیح در §۶.)

---

## ۵. چه کارهایی از دسکتاپ انجام می‌شود

از ۲۸۰ کلید `Link5.PersianString.resources` و کلاس‌های `Link5.UI.frm*`:

| دسته | امکانات |
| :-- | :-- |
| دستگاه | برقراری ارتباط، تست ارتباط/Ping، مشخصات، تنظیمات، کدهای کلید (`KeyCodeConvert`)، هم‌زمانی ساعت، باز کردن درب، قطع آلارم، ری‌استارت، پشتیبان‌گیری و بازیابی |
| فراخوانی | همهٔ ترددها / بازهٔ زمانی / فقط جدید / آنلاین، بازهٔ خودکار، حذف پس از فراخوانی، حذف قدیمی‌تر از، حذف وقتی از سقف گذشت، زمان‌بندی هفتگی/ماهانه |
| Push | تنظیمات Push در برنامه و در سرویس، ارسال دستور با Push، وضعیت و زمان آخرین تردد |
| پرسنل | ورود از Excel، فیلدهای اجباری، بارگذاری روی دستگاه، فراخوانی از دستگاه، اثر انگشت همراه/بدون، عکس |
| کنترل دسترسی | مناطق (`Zones`)، نقشه (`Map`/`MapDoor` با تصویر و مختصات درب)، جدول زمانی (`TimeTable`)، `AccessControl`، `AccessGroup`، `AccessCombo`، پیش‌کارت (`PishCard`/`Card`) |
| دسترسی کاربران برنامه | ۲۱ حق مجزا (`UserAccessType`): از «فراخوانی ترددها» تا «تنظیمات Push» و «مدیریت کاربران»، به‌همراه «دسترسی به دستگاه‌ها» و «دسترسی به قسمت‌ها» |
| خروجی و گزارش | Excel، فایل متنی، گزارش گرافیکی (`ZedGraph`)، قالب‌بندی ستون‌ها، `ReportViewer` |
| جانبی | تنظیمات ایمیل، USB (فایل/مدیریت)، لاگ سرویس، رویدادهای ویندوز، بکاپ زمان‌بندی‌شده |
| سخت‌افزار روی PC | اثر انگشت با `Interop.ZKFPEngXControl`، پردازش تصویر/چهره با `Emgu.CV` + OpenCV (`x86`/`x64`)، آلارم با `Interop.ARM` |

جدول‌های کلیدی در `Link5`: `RecordsLog` / `RecordsLogTemp` / `RecordsLogInvalid` /
`RecordsLogData`، `DeviceSetting` (۱۶ ستون)، `DeviceStatus` (۲۲ ستون: آخرین فراخوانی،
آخرین موفق، شمارش رکورد/تکراری/ذخیره‌شده، نشانه‌های Push)، `SchedulerSettings` (۱۵ ستون)،
`Command` / `CommandLog` / `PushCommand` / `PushCommands` (صف دستور دستگاه)،
`EvenetsLog` / `EventLog` / `ErrorLog` / `SystemLog`، و `webpages_Membership` /
`webpages_Roles` / `User` → یعنی **یک ماژول وب با ASP.NET Membership** دارد
(برچسب «نصب ActiveX» نشان می‌دهد آن ماژول وب قدیمی و IE-محور است).

---

## ۶. قفل و لایسنس (و چرا به کار ما مربوط است)

`Link5.settings.xml`:

```xml
<LockType>5</LockType> <IsNetworkLock>True</IsNetworkLock> <LockIP>172.16.4.6</LockIP>
<AppID>…</AppID> <ServerAuth>…</ServerAuth>
```

* **قفل سخت‌افزاری** با `TINYLib` (`Link5.Options.dll` → `TINYLib.Tiny`, `_DTiny`)، همان
  خانواده‌ای که در `NewPack Dlls` با `32bit_register.bat` / `96bitServiceInstaller632.exe`
  رجیستر می‌شود — و همان چیزی که در TWF/ARIA حذف کردیم (Zero-Lock).
* **اثر انگشت سخت‌افزار** از WMI: `Win32_Processor.ProcessorId`، `Win32_DiskDrive.Signature`،
  `Win32_BaseBoard`، `Win32_NetworkAdapterConfiguration.MACAddress`.
* **قفل شبکه‌ای** روی `LockIP` — همان آدرس `172.16.4.6` که در `T5V3.9.2/Settings.xml` هم هست.
* **فهرست ماژول‌های خریداری‌شده** در رشته‌ها: `db; file; elmo; jahan; pw; chargoon; t5; web; narm; push; service; app; allzk; sm:1`
  — یعنی هر خروجی/قابلیت جداگانه لایسنس می‌شود.
* سقف‌ها: `DemoDays`، `DeviceLimit`، `PersonnelCount` («تعداد پرسنل مجاز»)، و پیام‌های
  «قفل فعال شده است» / «خطا در قفل سخت‌افزاری» / «محدودیت تعداد دستگاه رعایت نشده است».

**نکتهٔ حساس:** خودِ Link5 به بیرون هم زنگ می‌زند. در `Comm5Service v4.exe`:

```
https://vcrservice95361.salamat.gov.ir/Api/Card/VaccineCardPe?vk=
```

یعنی فراخوانی API کارت واکسن (سامانهٔ سلامت) با `AppID`/`ServerAuth`. اگر شرط «۱۰۰٪ آفلاین»
برای سامانهٔ ماست، این رفتار باید در استقرار جدا بماند — سامانهٔ ما به آن وابسته نیست، ولی
Link5 روی همان سرور به اینترنت وابسته است.

---

## ۷. هم‌پوشانی با T5V3 — پاسخ به «خیلی شبیه T5 است»

`T5V3.9.2/Settings.xml` این را دارد:

```xml
<PushIsEnable>True</PushIsEnable> <PushPort>9000</PushPort>
<AutoDownload>True</AutoDownload> <PhotosPath>E:\PHOTO</PhotosPath>
<LockIP>172.16.4.6</LockIP>
```

و `T5V3.ZK.DLL` نوع `T5V3.ZK.ZKDevice` را دارد. یعنی **T5V3 خودش هم با دستگاه حرف می‌زند**
(فراخوانی + شنوندهٔ Push روی ۹۰۰۰). پس روی سرور شما دو برنامهٔ مستقل ادعای «ارتباط با
دستگاه» دارند:

| موضوع | Link5 | T5V3 |
| :-- | :-- | :-- |
| SDK دستگاه | ZK SDK 6.3.1.45 | `T5V3.ZK.DLL` (همان خانواده) |
| شنوندهٔ Push | ۸۰۸۱ | ۹۰۰۰ |
| خانهٔ تردد خام | `Link5.dbo.RecordsLog` | مستقیم در `Record` |
| نوشتن در `Record` | آداپتر `OutputType.T5V3` | خودش |
| کنترل دسترسی/نقشه/درب | دارد (نقشه، مناطق، TimeTable) | ندارد |
| محاسبات حضور و غیاب | ندارد | دارد |

هر دو هم `IF NOT EXISTS … INSERT` می‌زنند، پس تردد تکراری ثبت نمی‌شود؛ ولی اینکه
**کدام مرجع است** یک تصمیم عملیاتی است، نه فنی. اگر هر دو فعال باشند، هر کدام بخشی از
دستگاه‌ها را می‌خوانند و نتیجه، دو حقیقت نیمه‌کامل است.

---

## ۸. یافته‌های مستقیم برای کار خودمان

### ۸.۱ یک عیب واقعی در مدل ما: دو ستون `Record` غایب است

`engine/column-types.json` برای `[Record]` ۱۱ ستون دارد و `VerifyMethod` و `SourceType`
در آن نیست. ولی **دو شاهد مستقل** می‌گویند این دو ستون در جدول واقعی هستند:

1. `Link5.Save.dll` → `INSERT INTO [Record](… VerifyMethod … SourceType …)`
2. `T5V3.9.2/T5V3.DB.dll` → همان INSERT با همان ۱۱ ستون، از ORM خودِ T5

یعنی مدل ما ۲ ستون از ۱۱ ستون این جدول را کم دارد (نه برعکس). دروازه‌های ما این را
نمی‌گیرند چون هیچ‌جای کدمان این دو ستون را نمی‌نویسد — تا روزی که بخواهیم «نوع شناسایی»
را در پورتال نشان دهیم.

### ۸.۲ دستور دستگاه: ما یک صف بدون مصرف‌کننده ساخته‌ایم

نسخهٔ قدیمی پورتال، دستور دستگاه را به Link5 می‌داد:

`T5Web [3.5.7]/App/Web.config`:
```xml
<add key="Link5ServiceURL" value="http://127.0.0.1:15222/Device/" />
```
و در `T5V3Web.dll` پیام خطای `ErrorInConnectionToLink5Service` و
`DeviceOperationsController` هست.

در بازنویسی ما، `DeviceController.Command` یک ردیف در جدول اختراعی
`T5WebDeviceCommand` می‌گذارد و **هیچ‌کس آن را برنمی‌دارد** — نه Link5، نه سرویس ما.
یعنی دکمهٔ «ارسال دستور» چیزی را در دنیای واقعی تغییر نمی‌دهد. سه راه:

1. برگشت به قرارداد قبلی: `POST http://127.0.0.1:15222/Device/…` (نیازمند بازسازی امضای endpoint از روی نسخهٔ قبلی)؛
2. نوشتن در صف خودِ Link5: `Link5.dbo.PushCommand` / `PushCommands` / `Command`;
3. نگه‌داشتن صف خودمان و نوشتن یک سرویس پل که آن را به Link5/دستگاه منتقل کند.

### ۸.۳ مالکیت ستون‌های `Device*` در `UserInfo` = Link5

`UpdatePersonnel` ما این ستون‌ها را عمداً نمی‌نویسد و کامنتش می‌گوید «مال هم‌گام‌سازی
موتور است». حالا می‌دانیم آن «موتور» **Link5** است: با `MERGE INTO UserInfo` این‌ها را
از دستگاه به‌روز می‌کند — `DeviceName, DevicePass, DevicePriv, DeviceCardNo, DviceGroup,
DeviceSerialNumber, State, LastReadUser`. پس این تصمیم درست بوده و باید مستند شود که
مالکش Link5 است (و `DevicePass`/`DevicePriv` هرگز از پورتال نوشته نشود).

### ۸.۴ سه جدول هم‌نام در دو کاتالوگ → ابهام مدل (از جنس BUG-40)

`Card`، `Schedule`، `SystemLog` هم در `Link5.sql` ساخته می‌شوند و هم ORMِ `T5V3.DB.dll`
مدلشان می‌کند. `engine/relations.json` همه را `t5` گذاشته و شاهد Link5 را دور ریخته
(ابزار ادعا می‌کند «هر جدول یک کاتالوگ دارد»، در حالی که برای این سه، دو کاتالوگ دارد).
اثر عملی: اگر روزی کسی `Schedule` را «مال ReportT5V3» فرض کند و بخواهد از `Link5` هم
بخواند، سند دروغ می‌گوید.

### ۸.۵ سه جدول که ابزار ما اصلاً ندید

`WebService.Options`، `WebService.Queue`، `WebService.TermsOfApproval` نامشان نقطه دارد.
regex شناسه‌ها در `engine-relations.py` (و در `T42`) با `[A-Za-z0-9_]` کار می‌کند، پس این
سه جدول بی‌صدا از مدل حذف شدند: ۴۶ − ۳ (هم‌نام) − ۳ (نقطه‌دار) = همان ۴۰ جدولِ کاتالوگ
`device`. یعنی «۴۰ جدول» یک عدد تصادفی نیست، **حاصل دو خطای تجزیه** است.

### ۸.۶ ریسک‌های امنیتی که در همان پوشه دیدم

| مورد | جای آن |
| :-- | :-- |
| رمز `sa` به‌صورت متن در `VM5DB` | `Link5.settings.xml` |
| `Password=123` در نمونهٔ رشته‌ای | `Link5.exe` (`Provider=SQLOLEDb.1;…`) |
| جدول `User` با ستون `Password` (دسکتاپ) | `Link5.sql` |
| یک `P@ssw0rd5THD` در رشته‌ها | `Comm5Service v4.exe` |
| لاگ‌ها رمزنگاری‌شده (قابل خواندن نیست) | `Log.txt` (۹٬۸۹۴ خط) |
| مکالمهٔ دستگاه‌ها روی HTTP ساده | `Errorlog.txt` |

این‌ها مال برنامهٔ ما نیست، ولی روی همان سرور است و در استقرار باید حساب شود.

---

## ۹. چه چیزی ثابت شد و چه چیزی نه

**ثابت شد (از فایل‌های همین پوشه):** نام و نسخه و سازنده؛ فهرست نوع‌ها؛ ۴۶ جدول و ۱ ویو
`Link5`؛ ۳۰ جدول `Personnel`؛ ۹ مقصد خروجی و SQL تک‌تکشان؛ پروتکل Push و شماره‌های پورت؛
فهرست قابلیت‌ها؛ سازوکار قفل؛ دو ستون غایب `Record`؛ قرارداد `Link5ServiceURL`.

**ثابت نشد:** اینکه روی سرور شما **کدام سرویس‌ها در حال اجرا** هستند؛ اینکه کدام مقصد
(`OutputType`) فعال است؛ اینکه Push روی ۸۰۸۱ یا ۹۰۰۰ یا هر دو کار می‌کند؛ شمار دستگاه‌ها؛
نسخهٔ دقیق لایسنس و اینکه `LockIP=172.16.4.6` در دسترس است یا نه.

---

## ۱۰. پیشنهاد

1. **جای Link5 را در نقشهٔ معماری تثبیت کنیم**: Link5 = لایهٔ ارتباط دستگاه؛ T5 = موتور
   محاسبات؛ آریا/TWF = کارتابل؛ T5Web = کنسول مدیریت. چهار برنامه، دو پایگاه مشترک.
2. **دستور دستگاه** را به یکی از سه راه §۸.۲ وصل کنیم — وگرنه دکمه‌اش تزئینی است.
3. `VerifyMethod` و `SourceType` را به مدل `Record` اضافه کنیم (شاهد دوگانه دارد) و
   `T42`/`relations` را برای نام‌های نقطه‌دار درست کنیم.
4. `Card`/`Schedule`/`SystemLog` را از «یک کاتالوگ» به «دو کاتالوگ» تغییر دهیم و در
   `docs/09` بنویسیم.
5. برای سؤال «کدام دستگاه را کدام برنامه می‌خواند؟» یک تصمیم عملیاتی بگیریم؛ اگر Link5
   مرجع دستگاه است، شنوندهٔ Push خودمان و `T5WebDeviceCommand` باید حذف یا پل شوند.
