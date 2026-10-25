# ۱۳ — نصب IIS و SQL Server و راه‌اندازی «آریا ادمین»

این سند دقیقاً همان کاری را می‌گوید که یک نصب واقعی روی ویندوز لازم دارد: چهار اسکریپت،
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
Get-ChildItem          # 1-install-iis.ps1  2-install-sqlserver.ps1  3-deploy-portal.ps1  4-verify-install.ps1
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

## ۴. گام ۳ — استقرار خود پورتال

```powershell
.\3-deploy-portal.ps1 -DataSource 'SRV-SQL01\T5' -Mode coexist -Port 8080

# با بیلد روی همان سرور
.\3-deploy-portal.ps1 -DataSource 'SRV-SQL01\T5' -Mode coexist -Port 8080 -Build
```

| کار | تنظیم | دلیل |
| :-- | :-- | :-- |
| App pool | `.NET CLR v4.0`, Integrated, `AlwaysRunning`, `idleTimeout=0`, `loadUserProfile=true` | اولین درخواست بعد از recycle نباید کند باشد؛ `loadUserProfile` برای پروفایل IIS لازم است |
| هویت pool | `ApplicationPoolIdentity` | هیچ پسورد دومِنی ذخیره نمی‌شود |
| احراز هویت سایت | Windows on / Anonymous off (به‌علاوهٔ `<deny users="?" />` در `Web.config`) | دو لایهٔ مستقل |
| ACL | `IIS AppPool\<pool>` فقط `(RX)` روی پوشهٔ سایت | پورتال نباید بتواند کنار `Web.config` بنویسد |
| `Web.config` | `T5CnnStr`، `PersonnelCnnStr`، `T5WebMode`، `T5WebLegacyUrl` **جایگزین** می‌شوند | اگر append شود، کلید تکراری می‌سازد و `ConfigurationManager` در استارت‌آپ خطا می‌دهد |

اتصال به SQL فقط `integrated security=True` است. پسورد در `Web.config` یعنی پسورد در هر پشتیبان و
هر کپی از پوشهٔ سایت.

انتظار: `Web.config patched: source=…, mode=coexist` و آدرس نهایی
`http://localhost:8080/`.

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

**الف) نگاشت هویت pool به لاگین SQL.** اگر `t5web_admin` را ساخته‌اید و می‌خواهید پورتال با آن
وصل شود، به‌جای پسورد در `Web.config`، لاگین ویندوزی بسازید:

```sql
USE [master];
CREATE LOGIN [IIS APPPOOL\T5WebAdmin] FROM WINDOWS;
USE [ReportT5V3];
CREATE USER  [IIS APPPOOL\T5WebAdmin] FOR LOGIN [IIS APPPOOL\T5WebAdmin];
ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\T5WebAdmin];
-- و همان GRANT های db\02-t5web-sql-login.sql روی چهار جدول موتور + سه جدول پورتال
```

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

---

## ۸. وقتی یک بررسی FAIL می‌شود

| نشانه | علت رایج | درمان |
| :-- | :-- | :-- |
| `HTTP Error 500.19` | نقش `ASPNET45` نصب نیست یا `configSource` اشتباه | `.\1-install-iis.ps1` را دوباره بزنید؛ `%windir%\System32\inetsrv\appcmd list config` |
| `HTTP Error 500.21` | `ManagedPipelineMode` روی Classic است | `3-deploy-portal.ps1` را دوباره اجرا کنید (Integrated را ست می‌کند) |
| `HTTP Error 401.2` با مرورگر دامنه | `Negotiate` در providers نیست | در `3` تنظیم شده؛ `appcmd set config /section:windowsAuthentication` را چک کنید |
| ۵۰۳ و «رشته اتصال تنظیم نشده» | `T5CnnStr` خالی است | `3` را با `-DataSource` درست اجرا کنید |
| `Login failed for user 'IIS APPPOOL\…'` | لاگین ویندوزی ساخته نشده | بخش ۶-الف |
| همه‌چیز ۲۰۰ است ولی صفحه‌ها خالی‌اند | کاربر `WebUser=0` یا بدون `AccessView` | `/Access` و `UserInfo.WebUser` |
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
