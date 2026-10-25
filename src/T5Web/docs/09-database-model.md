# ۰۹ — مدل پایگاه‌داده: کاتالوگ‌ها، کلیدها و رابطه‌ها

**پیش‌نیاز:** `docs/01-rewrite-principles.md` · **تولید ماشینی:** `tools/engine-relations.py`
**خروجی ماشین‌خوان:** `engine/relations.json` و `engine/relations.tsv` · **گزارش سلامت:** `db/03-relations.sql`

---

## ۱. چرا این سند وجود دارد

اسکیمای T5 مال موتور است، نه مال این پورتال (`docs/01`، قاعده R1). پورتال به آن **فقط می‌خواند
و از راه DA یا SQL پارامتری می‌نویسد**، و هیچ ستونی به جدول‌های موتور اضافه نمی‌کند. اما «مال
موتور است» به معنای «بی‌مدل است» نیست: اگر مدل نباشد، هیچ‌کس نمی‌داند کدام جدول در کدام
کاتالوگ است، کدام ستون به کدام جدول اشاره می‌کند، و کدام ترتیب کوئری مجاز است. آن‌وقت اولین
استقرار روی سرور مشتری با `Invalid object name` می‌خوابد — که دقیقاً همان چیزی است که در
بازبینی این فاز پیدا شد (`BUG-28`).

پس مدل از **شاهد** ساخته می‌شود و در بیلد قفل می‌گردد. سه سطح شاهد:

| سطح | منبع | برچسب در مدل |
| :-- | :-- | :-- |
| DDL واقعی | `Personnel.sql` (۳۰ جدول) و `Link5.sql` (۴۶ جدول) — با `PRIMARY KEY` و **۳۳ `FOREIGN KEY`** | `declared` |
| مدل EF موتور | `T5V3.DB.dll` → `EntityClasses` (۵۴ جدول، بدون DDL در مخزن) | `inferred*` |
| نام ستون | ستونی که به `ID`/`Number` تمام می‌شود و در یک جدول یکتا حل می‌شود | `inferred` |

هیچ ستونی «نامعلوم» نمی‌ماند: هر ستون ارجاعی یا به جدولی **حل می‌شود** یا در فهرست
**استثناها با دلیل** می‌آید. شمارش نهایی: **۱۳۰ جدول، ۹۳ رابطه، ۹ استثنا، ۰ نامعلوم**.

---

## ۲. کاتالوگ‌ها (چهار، نه یکی)

| کاتالوگ | کلید اتصال | صاحب | تعداد جدول | نقش در پورتال |
| :-- | :-- | :-- | --: | :-- |
| `ReportT5V3` | `T5CnnStr` | موتور | ۵۴ | تردد، درخواست، مرخصی، شیفت، گردش کار، قواعد |
| `Personnel` | `PersonnelCnnStr` | موتور | ۳۳ | پرسنل، سازمان، عناوین، دستگاه، درب، مدرک |
| `Link5 service database` | — | سرویس دستگاه | ۴۰ | فقط مالک: صف/لاگ دستگاه. **پورتال هرگز به آن نمی‌نویسد** و در استقرار وب به آن وصل نمی‌شود |
| `ReportT5V3` (جدول‌های پورتال) | `T5CnnStr` | این پورتال | ۳ | `T5WebAccess`، `T5WebAudit`، `T5WebDeviceCommand` |

نکته‌ای که مدل آشکار کرد: سه نام در **دو** کاتالوگ وجود دارند — `Card`، `Schedule` و `SystemLog`
در DDL سرویس دستگاه و در مدل موتور. مدل، نسخه‌ی موتور را برمی‌گزیند (چون SQL پورتال روی همان
اجرا می‌شود) و همین تکراری‌بودن نام، دلیل دیگری است که «حدس زدن از نام جدول» ممنوع است.

---

## ۳. قانون طلایی: هر دستور SQL، یک کاتالوگ

نسخه قدیمی در **۷ دستور** جدول‌های دو کاتالوگ را در یک `JOIN` می‌آورد (نام پرسنل، نام دستگاه و
نام واحد در کنار تردد/مرخصی/شیفت/زنجیره تأیید). این فقط وقتی کار می‌کند که هر دو پایگاه روی یک
سرور باشند و لاگین SQL به هر دو دسترسی داشته باشد — در استقرار مشتری، `PersonnelDemo2` روی همان
سرور بود، ولی این یک **پیش‌فرض استقرار** است، نه یک قرارداد.

بازنویسی این کار را نمی‌کند:

1. هر دستور فقط به یک کاتالوگ می‌زند؛
2. ستون‌های نمایشی (نام پرسنل، نام دستگاه، نام واحد) با `PersonnelDirectory` از کاتالوگ
   `Personnel` خوانده و **در حافظه** به سطرها وصل می‌شوند؛
3. به‌همین‌دلیل `SqlAdminRepository`، `SqlAttendanceStore`، `SqlLeaveStore` و `SqlScheduleStore`
   دو کارخانه‌ی اتصال می‌گیرند (و اگر کلید `PersonnelCnnStr` تنظیم نشده باشد، هر دو به یک
   پایگاه اشاره می‌کنند — استقرار تک‌پایگاهی، همان کد).

`tools/engine-relations.py --check` این قانون را **در بیلد** و در **دو لایه** می‌سنجد:

1. **سطح دستور:** هر رشته SQL در `T5Web.Data`/`T5Web.Application`/`T5Web.Web` استخراج می‌شود و
   اگر دو کاتالوگ را در یک دستور بیامیزد، بیلد شکست می‌خورد؛
2. **سطح متد:** اگر یک دستور فقط به جدول‌های `Personnel` می‌زند، متدِ دربرگیرنده باید
   `OpenPersonnel()`/`personnelFactory` را باز کند، نه `Open()`؛ و برعکس.

لایهٔ دوم را دقیقاً برای همان اشتباهی گذاشتیم که در بازبینی پیدا شد: `GetTableCounts` و
`CountMembersByDepartment` جدول پرسنلی را روی اتصال موتور می‌خواندند و لایهٔ اول آن را نمی‌گرفت
(لایهٔ اول به یک دستور نگاه می‌کند، لایهٔ دوم به اینکه آن دستور روی *کدام اتصال* اجرا می‌شود).
آزمون `T28` لایهٔ اول را مستقل از پایتون تکرار می‌کند.

### ۳-۱. کدام مصرف‌کننده، کدام کاتالوگ

| مصرف‌کننده | اتصال | کاتالوگ | شاهد |
| :-- | :-- | :-- | :-- |
| `SqlAdminRepository` — نشست، پرسنل، واحد، عنوان، دستگاه، درب، مدرک | `OpenPersonnel()` | `Personnel` | `engine/relations.json` → `databases` |
| `SqlAdminRepository` — گردش کار، دسترسی‌ها، حسابرسی، شمارش‌های موتور | `Open()` | `ReportT5V3` | همان |
| `SqlAttendanceStore`، `SqlLeaveStore`، `SqlScheduleStore` | `Open()` (+ نام‌ها از `PersonnelDirectory`) | `ReportT5V3` | `BUG-22`، `BUG-28` |
| `PersonnelDirectory` | کارخانهٔ پرسنلی که به سازنده‌اش داده می‌شود | `Personnel` | نام کلاس |
| `PersonnelEngineDirectory` (`Personnel.dll`) | `PersonnelCnnStr` | `Personnel` | `BUG-30` — قبلاً `T5CnnStr` بود |
| `SqlModuleStore` (۳۰ ماژول) | بر اساس `ModuleDescriptor.Catalog` | ۲۰ ماژول `t5` · ۱۰ ماژول `Personnel` | `T16` هر ماژول را با این فایل می‌سنجد |
| `SqlPhotoStore` (آرشیو تصاویر، `docs/10`) | `personnelFactory` (`OpenPersonnel`) | `Personnel` | `[UserInfo].[Photo]` نوع `image` در DDL |
| `SqlEntitlementStore` (سهمیهٔ مرخصی) | `EmpType` از `Personnel` · `LeaveType`/`EmpTypeLeave` از `t5` | دو کاتالوگ، دو دستور | `T31` + لایهٔ دوم گیت |
| جدول‌های پورتال (`T5WebAccess`/`T5WebAudit`/`T5WebDeviceCommand`) | `Open()` | `ReportT5V3` | `db/01-t5web-objects.sql` |
| کاتالوگ سرویس دستگاه | هیچ‌کدام | `Link5 service database` | پورتال به آن وصل نمی‌شود؛ ماژول `option` و صفحهٔ «ساعات کار هفتگی» (`[TimeTable]`) به همین دلیل حذف/اضافه نشدند (`docs/10` §5) |

---

## ۴. کلیدها

| وضعیت | تعداد | توضیح |
| :-- | --: | :-- |
| کلید اعلام‌شده در DDL | ۵۳ | `CONSTRAINT [PK_...] PRIMARY KEY` |
| کلید استنتاجی (مدل/قرارداد نام) | ۷ | جدول‌های مدل EF که DDL ندارند و کلیدشان از رویه‌ی خود موتور روشن است |
| بدون شاهد کلید | ۸ | جدول‌های لاگ/پیکربندی سرویس دستگاه (`DeviceErrorLog`، `DeviceOperationLog`، `DownloadRecordsLog`، `OperationLog`، `SchedulerOptions`، `SchedulerSettings`، `AppRule`، `Ghadir1`) — پورتال آن‌ها را فقط می‌خواند و کلید ادعا نمی‌کند |

کلیدهای ترکیبی مهم (بدون DDL، ولی قطعی از روی مدل): `Record(BadgeNumber, CheckTime, CheckType)` ·
`Request(RequestID)` · `YearRemain(BadgeNumber, LeaveTypeID)` · `ShiftDetail(ShiftID, ScheduleID, DayIndex)` ·
`Workflow(WorkflowType, LeaveTypeID, OrderIndex)` · `T5WebAccess(BadgeNumber, Feature)`.

---

## ۵. رابطه‌ها

**۹۳ رابطه**: ۳۳ `declared` + ۶۰ `inferred`؛ از این میان **۳۱ رابطه بین دو کاتالوگ** است و
بنابراین هرگز نمی‌تواند `FOREIGN KEY` باشد (SQL Server FK بین دو پایگاه ندارد). این‌ها
«رابطه‌ی منطقی» هستند: کد آن‌ها را در حافظه حل می‌کند و `db/03-relations.sql` درستی‌شان را
روی داده‌ی واقعی می‌سنجد.

### ۵-۱. رابطه‌های بین‌کاتالوگی (۳۱)

| جدول مبدأ | رابطه |
| :-- | :-- |
| `device.Command` | `MachineNumber → personnel.Device.MachineNumber` |
| `device.CommandLog` | `MachineNumber → personnel.Device.MachineNumber` |
| `device.DeviceAlarm` | `MachineNumber → personnel.Device.MachineNumber` |
| `device.EvenetsLog` | `MachineNumber → personnel.Device.MachineNumber` |
| `device.IntervalSchedule` | `MachineNumber → personnel.Device.MachineNumber` |
| `device.PushCommand` | `MachineNumber → personnel.Device.MachineNumber` |
| `device.RecordsLog` | `MachineNumber → personnel.Device.MachineNumber` |
| `device.RecordsLogData` | `MachineNumber → personnel.Device.MachineNumber` |
| `device.RecordsLogInvalid` | `MachineNumber → personnel.Device.MachineNumber` |
| `device.RecordsLogTemp` | `MachineNumber → personnel.Device.MachineNumber` |
| `device.ScheduleDevice` | `ScheduleID → t5.Schedule.ScheduleID` |
| `device.SchedulerOptions` | `MachineNumber → personnel.Device.MachineNumber` |
| `personnel.DepartmentRules` | `RuleID → t5.Rule.RuleID` |
| `personnel.LeaveTypes` | `DecreaseFromLeaveID → t5.LeaveType.LeaveID`؛ `ParameterID → t5.Parameter.ParameterID` |
| `personnel.UserRules` | `RuleID → t5.Rule.RuleID` |
| `portal.T5WebDeviceCommand` | `MachineNumber → personnel.Device.MachineNumber` |
| `t5.Area` | `ManagerID → personnel.UserInfo.BadgeNumber` |
| `t5.Card` | `MachineNumber → personnel.Device.MachineNumber` |
| `t5.DepartmentRule` | `DepartmentID → personnel.Department.DepartmentID` |
| `t5.DownloadRecordsLog` | `MachineNumber → personnel.Device.MachineNumber` |
| `t5.DownloadTimeDevice` | `MachineNumber → personnel.Device.MachineNumber` |
| `t5.HourlyLeave` | `BadgeNumber → personnel.UserInfo.BadgeNumber` |
| `t5.LeavesAdded` | `BadgeNumber → personnel.UserInfo.BadgeNumber` |
| `t5.Record` | `MachineNumber → personnel.Device.MachineNumber` |
| `t5.Request` | `BadgeNumber → personnel.UserInfo.BadgeNumber` |
| `t5.UserPositionData` | `BadgeNumber → personnel.UserInfo.BadgeNumber` |
| `t5.UserRule` | `BadgeNumber → personnel.UserInfo.BadgeNumber` |
| `t5.ValidLeave` | `BadgeNumber → personnel.UserInfo.BadgeNumber` |
| `t5.Workflow` | `AccepterTitleID → personnel.TitlesChart.TitleID`؛ `TitleID → personnel.TitlesChart.TitleID` |
### ۵-۲. استثناها (۹ ستون که ارجاع نیستند)

| ستون | دلیل |
| :-- | :-- |
| `Record.WorkCode` | کد کار؛ عددی از دستگاه، جدول متناظر در اسکیما نیست |
| `Record.CheckType` / `SourceType` / `VerifyMethod` | enum موتور/دستگاه (نوع تردد، منبع، روش احراز) |
| `DeviceErrorLog.CmdId` | شناسه فرمانی که خود دستگاه می‌فرستد |
| `CommandLog.CommandId` | شناسه فرمان سمت دستگاه؛ پورتال جدول `Command` را نمی‌نویسد |
| `DownloadRecordsLog.EventID` | enum رویداد سرویس Link5 |
| `AccessControl.Badgenumber` | همان `BadgeNumber` با حرف کوچک در DDL؛ ستون‌های مدل موتور به `UserInfo` اشاره دارد |
| `DashboardItem.ItemID` | شناسه آیتم نوار ابزار دسکتاپ (`Misc/ToolbarItems.xml`)، جدولی ندارد |
| `Area.UserID` / `SystemLog.UserID` / `DownloadRecordsLog.UserID` | شناسه کاربر نرم‌افزار؛ در این کاتالوگ جدول `User` وجود ندارد و نگاشت به پرسنل اثبات نشده |

### ۵-۳. دو مورد که مدل، «جدول» نمی‌داند

`Entities` و `sys` در `schema-manifest.json` هستند ولی جدول نیستند (اولی نام مجموعه‌های EF،
دومی مخزن سامانه‌ای SQL Server). مدل آن‌ها را با دلیل حذف می‌کند و این را در `not_tables` نگه
می‌دارد؛ در غیر این صورت هر ادعای «۱۳۰ جدول پوشش داده شد» دو سطر باد دارد (`BUG-29`).

---

## ۶. سلامت رابطه‌ها روی داده‌ی واقعی: `db/03-relations.sql`

FK نمی‌تواند اضافه شود (مالکیت اسکیما + دو کاتالوگ)، پس درستیِ رابطه‌ها با یک گزارش
**فقط-خواندنی** سنجیده می‌شود که پیش و پس از هر مهاجرت/ورود گروهی/اصلاح دستی اجرا می‌شود:

| گروه بررسی | چه چیزی را می‌سنجد |
| :-- | :-- |
| کلیدها | تکرار در `T5WebAccess(BadgeNumber, Feature)` |
| هم‌کاتالوگ | `Workflow → TitlesChart` و `LeaveType`، `ShiftDetail → Shift/Schedule`، `YearRemain`/`LeavesAdded → LeaveType`، `RequestAccept → Request`، `UserShift → Shift` |
| سلسله‌مراتب | حلقه و والد ناموجود در `Department`، `TitlesChart`، `Gate` |
| بین‌کاتالوگ | `Record`/`Request`/`YearRemain`/`UserShift`/`T5WebAccess` → `UserInfo`؛ `Record`/`T5WebDeviceCommand` → `Device`؛ `UserInfoTitles` → `TitlesChart` |

اگر کاتالوگ `Personnel` روی سرور دیگری باشد، بخش بین‌کاتالوگ «n/a» می‌شود و باید همان نیمه را
روی سرور دوم اجرا کرد (دلیلش در بالای اسکریپت نوشته شده). خروجی با یک سطر `Verdict` تمام می‌شود
تا بتوان آن را در گیت استقرار گذاشت.

---

## ۷. اگر فردا جدول یا کوئری اضافه شد

1. جدول تازه در `db/schema-manifest.json` (یا `db/01-t5web-objects.sql` برای جدول‌های خود پورتال) بیاید؛
2. `python3 tools/engine-relations.py --write` اجرا و `engine/relations.{json,tsv}` دوباره تولید شود؛
3. اگر ستون ارجاعی تازه‌ای «نامعلوم» بود، در `ALIASES` یا `EXCEPTIONS` **با دلیل** ثبت شود
   (گیت تا آن لحظه سبز نمی‌شود — این عمدی است)؛
4. کوئری تازه فقط یک کاتالوگ را لمس کند؛ ستون‌های نمایشی از `PersonnelDirectory` بیاید؛
5. `./build-and-test.sh --gates` و آزمون `T28` باید سبز شوند.

---

## ۸. پیوندها

* `docs/02-phase-log.md` — شرح شکاف‌هایی که این ممیزی بست (`BUG-28`، `BUG-29`)
* `docs/08-runbook.md` — نصب دو کلید اتصال و اجرای `db/03`
* `engine/relations.tsv` — فهرست کامل رابطه‌ها (یک سطر برای هر رابطه)
* `db/03-relations.sql` — گزارش سلامت فقط-خواندنی
