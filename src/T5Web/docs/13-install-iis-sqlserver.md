# ۱۳ — نصب IIS و SQL Server و راه‌اندازی «آریا ادمین»

این سند دقیقاً همان کاری را می‌گوید که یک نصب واقعی روی ویندوز لازم دارد: پنج اسکریپت،
به ترتیب، و یک چک‌لیست پذیرش که خودش PASS/FAIL می‌دهد.

> **یک شفاف‌سازی لازم:** محیطی که این بازنویسی در آن نوشته شده، لینوکس (Debian 12) است و
> دسترسی به مخازن بستهٔ مایکروسافت هم بسته است. IIS یک مؤلفهٔ **فقط-ویندوزی** است و روی لینوکس
> وجود ندارد؛ SQL Server هم بدون مخزن مایکروسافت نصب‌شدنی نیست. پس اینجا هیچ‌کدام نصب نشده و
> **ادعای نصب هم نمی‌کنیم**. چیزی که ساخته شد، همان نصب را روی سرور شما **یک‌دستوری و
> قابل‌اثبات** می‌کند: `deploy/*.ps1` + گیت `deploy-kit-gate.py` + این سند. بخش ۷ می‌گوید از شما
> چه چیزی لازم است تا نتیجه را با هم ببینیم.

---

## ۱. پیش‌نیازها

| مورد | حداقل | چرا |
| :-- | :-- | :-- |
| ویندوز | Windows Server 2016+ یا Windows 10/11 | اگر `WindowsOptionalFeature` روی نسخه‌ای قدیمی‌تر جواب نداد، بخش ۷ |
| .NET Framework | 4.7.2 (یا 4.8) | `T5Web.Web.csproj` روی `v4.7.2` است |
| SQL Server | 2016+ (Express کافی است) | `T-SQL` مورد استفاده: `AT TIME ZONE` نیست، پس 2012 هم معمولاً کافی است |
| دسترسی | Administrator روی سرور، `sysadmin` روی SQL | اسکریپت‌ها هم IIS را تغییر می‌دهند هم `CREATE LOGIN` می‌زنند |
| اینترنت | **لازم نیست** | همه‌چیز آفلاین است؛ مدیای نصب SQL را خودتان می‌آورید |
| MSBuild | فقط برای `-Build` | اگر بیلد را جای دیگری می‌گیرید، لازم نیست |

پوشهٔ استقرار:

```powershell
cd D:\IO\src\T5Web\deploy
Get-ChildItem          # 0-publish.ps1  1-install-iis.ps1  2-install-sqlserver.ps1  3-deploy-portal.ps1  4-verify-install.ps1
```

---

## ۲. گام ۱ — IIS

```powershell
.\1-install-iis.ps1
```

چه می‌کند: نقش‌های IIS، `StaticContent`، `DefaultDocument`، `HttpErrors`، `RequestFiltering`،
`HttpLogging`، `ASPNET45` (یعنی `NetFxExtensibility45` + ISAPI)، کنسول مدیریت و
**Windows Authentication** را فعال می‌کند؛ و در سطح سرور **Anonymous Authentication را خاموش** و
`directoryBrowse` را غیرفعال می‌کند.

عمداً نصب **نمی‌کند**: FTP، WebDAV، SMTP، Directory Browsing. هیچ‌کدام در پورتال استفاده نمی‌شوند
و هر کدام یک سطح حملهٔ اضافه است.

انتظار: هرfeature یک خط `already enabled` یا `enabling...`، سپس `W3SVC Running` و
`IIS is ready`. اگر `RestartNeeded` بود، اسکریپت با کد **3010** خارج می‌شود: ری‌استارت کنید و
دوباره اجرا کنید (بار دوم همه‌چیز `already enabled` است).

---

## ۳. گام ۲ — SQL Server و دو کاتالوگ

```powershell
# الف) اگر هنوز هیچ نمونه‌ای روی سرور نیست: مدیای آفلاین بدهید
.\2-install-sqlserver.ps1 -SetupExe D:\media\SQLEXPR_x64_ENU.exe

# ب) اگر نمونه هست و پایگاه‌ها هم موجودند: فقط شیءهای پورتال و لاگین
.\2-install-sqlserver.ps1

# ج) اگر نمونه هست ولی پایگاه‌ها باید از پشتیبان بازگردانده شوند
.\2-install-sqlserver.ps1 -BackupFolder D:\backup
```

سه کار انجام می‌دهد و هر کدام را اگر انجام شده باشد رد می‌کند:

1. **نمونه:** اگر `SELECT 1` جواب داد، همان استفاده می‌شود. وگرنه فقط با `-SetupExe` نصب
   سایلنت انجام می‌شود (`/FEATURES=SQLEngine /SQLCOLLATION=Persian_100_CI_AS /SECURITYMODE=SQL`).
   پسورد SA در زمان اجرا پرسیده می‌شود و در هیچ فایلی نوشته نمی‌شود.
2. **کاتالوگ‌ها:** `ReportT5V3` و `Personnel` **ساخته نمی‌شوند**. یا باید موجود باشند یا از
   `-BackupFolder` بازگردانده شوند. دلیلش همان قاعدهٔ `docs/12` است: اسکیما مال موتور است و یک
   پایگاه خالی با اسکیمای حدسی، بدتر از نبودنش است.
3. **شیءهای پورتال:** `db\01-t5web-objects.sql` (سه جدول) و `db\02-t5web-sql-login.sql`
   (لاگین کم‌دسترسی). هر دو idempotent.

انتظار: خطوط `catalog ReportT5V3 present` / `restored`، سپس `T5Web portal objects are in place`
و `t5web_admin created with least-privilege grants`. در پایان یادآوری می‌کند که پسورد
`t5web_admin` را عوض کنید.

---

## ۳.۵. گام ۲.۵ — ساخت پوشهٔ قابل‌استقرار

پوشهٔ سورس، پوشهٔ سایت نیست: `.cs` و `obj\` و `bin\Release\` را نباید سرو کند.
`0-publish.ps1` همان کاری را می‌کند که یک publish واقعی می‌کند، بدون نیاز به Web Deploy
(که روی سرورهای بدون اینترنت نصب نیست):

```powershell
.\0-publish.ps1 -OutputPath C:\inetpub\T5WebAdmin -Build
```

خروجی: `Web.config`، `Global.asax`، `Views\`، `Content\` و `bin\` با اسمبلی‌ها.
اسکریپت در پایان **رد می‌کند** اگر در پوشهٔ منتشرشده `.cs`، `.csproj` یا `obj` مانده باشد —
سورس روی وب‌سرور یعنی سورس در دسترس.

---

## ۴. گام ۳ — استقرار خود پورتال

```powershell
.\3-deploy-portal.ps1 -SitePath C:\inetpub\T5WebAdmin -DataSource 'SRV-SQL01\T5' -Mode coexist -Port 8080

# اگر پوشهٔ سایت را جدا ساخته‌اید و می‌خواهید بیلد هم همین‌جا انجام شود
.\0-publish.ps1 -OutputPath C:\inetpub\T5WebAdmin -Build
.\3-deploy-portal.ps1 -SitePath C:\inetpub\T5WebAdmin -DataSource 'SRV-SQL01\T5' -Mode coexist -Port 8080
```

| کار | تنظیم | دلیل |
| :-- | :-- | :-- |
| App pool | `.NET CLR v4.0`, Integrated, `AlwaysRunning`, `idleTimeout=0`, `loadUserProfile=true`, **`enable32BitAppOnWin64=true`** | سطر آخر اجباری است: موتور پین‌شده x86 است (بخش ۴-الف) |
| هویت pool | `ApplicationPoolIdentity` | هیچ پسورد دومِنی ذخیره نمی‌شود |
| احراز هویت سایت | Windows on / Anonymous off (به‌علاوهٔ `<deny users="?" />` در `Web.config`) | دو لایهٔ مستقل |
| ACL | `IIS AppPool\<pool>` فقط `(RX)` روی پوشهٔ سایت | پورتال نباید بتواند کنار `Web.config` بنویسد |
| `Web.config` | `T5CnnStr`، `PersonnelCnnStr`، `T5WebMode`، `T5WebLegacyUrl` **جایگزین** می‌شوند | اگر append شود، کلید تکراری می‌سازد و `ConfigurationManager` در استارت‌آپ خطا می‌دهد |

اتصال به SQL فقط `integrated security=True` است. پسورد در `Web.config` یعنی پسورد در هر پشتیبان و
هر کپی از پوشهٔ سایت.

انتظار: `Web.config patched: source=…, mode=coexist` و آدرس نهایی
`http://localhost:8080/`.

---

## ۴-الف. چرا app pool باید ۳۲-بیتی باشد

اسمبلی‌های موتور پین‌شده — `Personnel.dll`، `T5V3.DB.dll`، `T5V3.Model.dll` و `Dapper.dll` در
`T5Web [3.5.7]\App\bin` — با هدر PE نوع **x86** ساخته شده‌اند. پروسهٔ کارگر ۶۴-بیتی نمی‌تواند آن‌ها را
بار کند و نتیجه‌اش این است:

```
Could not load file or assembly 'Personnel' or one of its dependencies.
An attempt was made to load a program with an incorrect format.
```

و چون خطا در **بارگذاری اسمبلی** رخ می‌دهد، نه در اجرای اکشن، حتی یک فایل ایستا مثل
`/Content/t5web-admin.css` هم `500` می‌دهد. این را در CI روی IIS واقعی دیدیم (`docs/14`)، نه در بیلد.

بنابراین `3-deploy-portal.ps1` همیشه `enable32BitAppOnWin64 = true` را ست می‌کند، `4-verify-install.ps1`
آن را به‌عنوان بررسی `bitness` می‌خواند، و gate استقرار اجازه نمی‌دهد این خط از اسکریپت حذف شود.

اسمبلی‌های خود پورتال `AnyCPU` هستند، پس ۳۲-بیتی‌شدن pool هیچ هزینه‌ای برایشان ندارد؛ تنها محدودیت
حافظهٔ پروسهٔ ۳۲-بیتی است که برای چند صد کاربر همکار کافی است. اگر روزی موتور ۶۴-بیتی شد، فقط همین
یک تنظیم برمی‌گردد.

---

## ۵. گام ۴ — پذیرش (این گام را حذف نکنید)

```powershell
.\4-verify-install.ps1 -BaseUrl http://localhost:8080 `
    -SourceFingerprint D:\cutover\source-before.txt `
    -PortalCredential (Get-Credential t5web_admin)
```

| بررسی | معیار PASS | اگر FAIL چه می‌گوید |
| :-- | :-- | :-- |
| `schema` | خروجی `verify.sql` هیچ `MISSING` ندارد | نام جدول/ستون یا کاتالوگ اشتباه است |
| `relations` | خروجی `03-relations.sql` هیچ `VIOLATION` ندارد | داده یتیم دارد؛ به صفحهٔ `/Integrity` نگاه کنید |
| `fingerprint` | `OK: fingerprints match` | انتقال ناقص/قدیمی است (`docs/12` §۳) |
| `auth-anonymous` | کد **401** | Windows Authentication خاموش است یا سایت Anonymous را می‌پذیرد |
| `auth-windows` | کد **200** و نه «دسترسی ندارید» | کاربر `UserInfo.WebUser = 0` است یا قابلیت `PersonnelView` ندارد |
| `leastpriv` | `DENIED` روی `CREATE TABLE` | لاگین پورتال زیادی اختیار دارد؛ `db\02` را بازبینی کنید |
| `headers` | `Content-Security-Policy` هست و `unsafe-inline` ندارد | هدرها در `Web.config` حذف/بازنویسی شده‌اند |

کد خروج = تعداد FAIL. اسکریپت فقط می‌خواند؛ اجرای آن روی تولید بی‌خطر است.

---

## ۶. دو کار که اسکریپت عمداً انجام نمی‌دهد

**الف) نگاشت هویت pool به لاگین SQL.** اسکریپت‌های ۱ تا ۳ به SQL دست نمی‌زنند، چون آن‌ها ممکن است روی
سرور دیگری باشند و اجرایشان به sysadmin نیاز دارد. اتصال پورتال `integrated security=True` است، پس SQL Server هویت **app pool** را می‌بیند، نه شما را.
بدون این گام هر صفحه با `Login failed for user 'IIS APPPOOL\T5WebAdmin'` می‌افتد:

```powershell
sqlcmd -S 'SRV-SQL01\T5' -E -b -i db\04-grant-app-pool-login.sql `
       -v AppPoolLogin="IIS APPPOOL\T5WebAdmin"
```

این فایل عمداً فهرست دسترسی‌ها را تکرار **نمی‌کند**: هویت pool را عضو همان نقشی می‌کند که
`db\02` ساخته و پر کرده است (`t5web_app` در هر دو کاتالوگ). نسخهٔ دومِ آن فهرست، همان جایی است که یک
صفحه برای یک هویت باز و برای هویت دیگر بسته می‌شود. اگر `-PoolName` را در گام ۳ عوض کرده‌اید، همین
مقدار را اینجا هم بدهید — اسکریپت نام را حدس نمی‌زند.

**ب) `UserInfo.WebUser`.** هیچ اسکریپتی این ستون را عوض نمی‌کند؛ مال موتور است. اولین Administrator
را با نسخهٔ دسکتاپ (یا یک `UPDATE` با تأیید صریح مدیر داده) تعیین کنید، بعد `/Access` را پر کنید.

---

## ۷. چه چیزی از شما لازم است تا نتیجه را با هم ببینیم

جواب «نصب شد و کار می‌کند» بدون خروجی، ادعاست. اگر این پنج مورد را بدهید، می‌توانم خروجی را
خط‌به‌خط بخوانم و اگر جایی شکست، همان‌جا اصلاح کنم:

| # | مورد | چرا |
| :-- | :-- | :-- |
| ۱ | نسخهٔ ویندوز (`winver`) و اینکه سرور دامنه است یا workgroup | Windows Auth و `IIS APPPOOL\…` رفتار متفاوت دارند |
| ۲ | نسخه و نسخهٔ نمونه SQL (`SELECT @@VERSION`, `-Instance`) | `Persian_100_CI_AS` و قابلیت `TRY…CATCH` |
| ۳ | آیا `ReportT5V3` و `Personnel` از قبل روی سرور هستند؟ | تعیین می‌کند گام ۲-ج لازم است یا نه |
| ۴ | خروجی `.\4-verify-install.ps1 … \| Tee-Object verify.log` | همان چک‌لیست، با شاهد |
| ۵ | اگر بیلد می‌خواهید: خروجی `msbuild T5Web.sln /p:Configuration=Release /v:minimal` | کامپایل C# در محیط من ممکن نیست (بدون `csc`/`dotnet`) و اولین بیلد واقعی همان‌جا اتفاق می‌افتد |

اگر دسترسی به یک ویندوز واقعی بدهید (RDP/runner)، همان چهار اسکریپت را خودم اجرا و خروجی را
گزارش می‌کنم. بدون آن، صادقانه‌ترین کار همین است: کیت آماده، گیت‌شده، و منتظر یک اجرا.

**راه میان‌بر:** پیش از هر چیز، مسیر CI را ببینید — `docs/14-windows-ci.md`. سه job روی ویندوز واقعی
(`windows-latest`) اجرا می‌شوند: کامپایل و آزمون‌ها، `db/*.sql` روی SQL Server Express LocalDB، و
**`iis-install`** که همین پنج اسکریپت را پشت‌سرهم اجرا می‌کند: publish، نصب IIS، استقرار سایت، سپس
ادعاهای app pool (از جمله ۳۲-بیتی بودن)، `401` برای درخواست ناشناس و `200` + CSP برای کاربر ویندوزی.
یعنی این سند دیگر «دستورِ آزمون‌نشده» نیست؛ هر خطی که در CI سبز است، روی یک IIS واقعی هم اجرا شده.

---

## ۸. وقتی یک بررسی FAIL می‌شود

| نشانه | علت رایج | درمان |
| :-- | :-- | :-- |
| `HTTP Error 500.19` | نقش `ASPNET45` نصب نیست یا `configSource` اشتباه | `.\1-install-iis.ps1` را دوباره بزنید؛ `%windir%\System32\inetsrv\appcmd list config` |
| `500` روی *همهٔ* آدرس‌ها، حتی `.css` · متن: `Could not load file or assembly … incorrect format` | app pool ۶۴-بیتی است و موتور x86 | `.\3-deploy-portal.ps1` را دوباره بزنید؛ باید `enable32BitAppOnWin64=true` را ست کند (بخش ۴-الف) |
| `500.19` · `Cannot add duplicate collection entry … fileExtension` | یک قاعدهٔ `requestFiltering` دو بار تعریف شده | `Web.config` نسخهٔ فعلی را بگذارید: هر extension اول `<remove>` و بعد `<add>` می‌شود |
| `HTTP Error 500.21` | `ManagedPipelineMode` روی Classic است | `3-deploy-portal.ps1` را دوباره اجرا کنید (Integrated را ست می‌کند) |
| `HTTP Error 401.2` با مرورگر دامنه | `Negotiate` در providers نیست | در `3` تنظیم شده؛ `appcmd set config /section:windowsAuthentication` را چک کنید |
| ۵۰۳ و «رشته اتصال تنظیم نشده» | `T5CnnStr` خالی است | `3` را با `-DataSource` درست اجرا کنید |
| `Login failed for user 'IIS APPPOOL\…'` | لاگین ویندوزی ساخته نشده | بخش ۶-الف |
| همه‌چیز ۲۰۰ است ولی صفحه‌ها خالی‌اند | کاربر `WebUser=0` یا بدون `AccessView` | `/Access` و `UserInfo.WebUser` |
| `cannot protect plain text secrets sent over unencrypted connections` | PowerShell 7 با `http` و Windows Auth | اسکریپت‌های کیت در **Windows PowerShell 5.1** اجرا شوند؛ `4-verify-install.ps1` خودش در ۷ فلگ لازم را اضافه می‌کند |
| `verify.sql` → `MISSING` | کاتالوگ اشتباه یا نسخهٔ قدیمی موتور | `-T5Database`/`-PersonnelDatabase` را چک کنید؛ `docs/12` §۳-۳ |

---

## ۹. فایل‌های مرتبط

| فایل | کار |
| :-- | :-- |
| `deploy/1-install-iis.ps1` … `4-verify-install.ps1` | کیت نصب، هر چهار idempotent |
| `deploy/README.md` | ترتیب اجرا و جدول «چه می‌کند / چه لازم دارد» |
| `tools/deploy-kit-gate.py` | گیت بیلد: syntax، guard، idempotency، بدون پسورد، ارجاع‌ها، ترتیب مستند |
| `db/01-t5web-objects.sql` · `02-t5web-sql-login.sql` | شیءهای پورتال و لاگین کم‌دسترسی |
| `db/verify.sql` · `03-relations.sql` | گیت‌های گام ۴ |
| `docs/08-runbook.md` | نصب، `coexist`، پشتیبان‌گیری، بازگردانی |
| `docs/12-data-migration.md` | انتقال داده و اثر انگشت |
