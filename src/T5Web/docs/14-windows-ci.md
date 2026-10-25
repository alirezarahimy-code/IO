# ۱۴ — کامپایل و آزمایش روی یک ویندوز واقعی (CI)

این سند پاسخ صادقانه به این درخواست است: «ویندوز سرور، IIS و SQL Server را نصب کن».
در محیطی که کد نوشته می‌شود این کار **فیزیکی نیست**؛ پس همان ماشین از راه دیگری گرفته شد:
یک **ویندوز واقعی در GitHub Actions** (`.github/workflows/windows-build.yml`).

---

## ۱. چرا نصب در محیط توسعه ممکن نیست (با شاهد)

| بررسی | نتیجه |
| :-- | :-- |
| سیستم‌عامل | Debian 12 (bookworm) — ویندوز نیست |
| مجازی‌ساز | `/dev/kvm` وجود ندارد؛ `qemu`, `docker`, `podman`, `wine` نصب نیستند و مخزن `apt` هم بسته است |
| مخازن مایکروسافت | `packages.microsoft.com` · `download.microsoft.com` · `builds.dotnet.microsoft.com` · `nuget.org` → همه `000` (بدون اتصال) |
| پکیج‌های مجاز | `pypi.org` · `registry.npmjs.org` · `github.com` |
| سخت‌افزار | ۲ هسته · ۳ گیگابایت RAM · ۱۹ گیگابایت دیسک |

**نتیجه:** ویندوز سرور روی یک لینوکس بدون KVM و بدون رسیدن به مدیای مایکروسافت نصب‌شدنی نیست؛
IIS هم مؤلفهٔ خودِ ویندوز است. «اجازه دادن» این محدودیت فیزیکی را برنمی‌دارد. تنها راه واقعی،
استفاده از یک ویندوز **بیرونی** بود؛ و آن ویندوز، runner گیت‌هاب است.

---

## ۲. چه چیزی روی آن ویندوز اجرا می‌شود

| گام | ابزار | چه چیزی را ثابت می‌کند |
| :-- | :-- | :-- |
| Static gates | `build-and-test.sh --gates` | ۱۳ گیت، همان‌ها که روی لینوکس هم اجرا می‌شوند (بدون وابستگی به ماشین توسعه) |
| Build | `msbuild T5Web.sln /p:Configuration=Release` | **اولین کامپایل واقعی C#** — چیزی که تا امروز انجام نشده بود |
| Suite | `T5Web.Tests.exe` | هر ۴۰ آزمون (`T01`…`T40`) روی .NET Framework 4.7.2 واقعی |
| SQL | SQL Server Express **LocalDB** (روی تصویر runner موجود است) | همان موتور T-SQL، بدون نیاز به نصب |
| Fixture | `tools/synthetic-schema.py` | ۷۱ جدول از مدل اثبات‌شده (۷۵ منهای ۴ جدول کاتالوگ `device`) |
| Scripts | `db/01` · `db/02` (دو بار) · `verify.sql` · `03-relations.sql` | اسکریپت‌ها واقعاً اجرا می‌شوند، idempotent می‌مانند، `MISSING`/`VIOLATION` ندارند |
| Fingerprint | `tools/migration-fingerprint.py --sql` + `--compare` | اثر انگشت روی یک سرور واقعی گرفته و مقایسه می‌شود |
| Smoke | `tools/sql-smoke.py --server '(localdb)\T5Web'` | هر ۸۱ دستور کاملِ SQL با `SET NOEXEC ON` کامپایل می‌شود |
| **IIS** | `deploy/0` → `1` → `3`، سپس ادعاهای `app pool` و دو کاوش HTTP | **نصب واقعی**: کیت استقرار روی یک IIS 10 واقعی اجرا می‌شود و سایت با Windows Authentication پاسخ می‌دهد (job سوم: `iis-install`) |

### چرا `SET NOEXEC ON` و نه اجرای واقعی

`SET NOEXEC ON` دستور را **کامپایل** می‌کند و اجرا نمی‌کند: SQL Server نام هر جدول و ستون را
resolve می‌کند، نوع‌ها را می‌سنجد و بعد نتیجه را دور می‌ریزد. این همان چیزی است که
`Invalid column name` را می‌گیرد، بدون آنکه چیزی بنویسد یا حذف کند. چون پایگاه تست خالی است،
اجرای واقعی `INSERT`/`UPDATE` هم چیزی بیشتر ثابت نمی‌کرد.

---

## ۳. چطور اجرا می‌شود

**خودکار:** با هر push روی `src/T5Web/**` (و هر PR).

**دستی:**

```bash
gh workflow run windows-build.yml --ref arena/a4df3135-io
gh run list --workflow windows-build.yml --limit 3
gh run watch <run-id>
```

**روی سرور خودتان** (بدون CI): همان چهار اسکریپت `deploy/`، به ترتیب `docs/13`.

---

## ۴. چه چیزی این سند *ثابت نمی‌کند*

| مورد | چرا | جای واقعی‌اش |
| :-- | :-- | :-- |
| Windows Authentication با حساب دامنه | runner عضو دامنه نیست؛ کاوش ۲۰۰ با حساب محلی همان runner انجام می‌شود | یک ورود واقعی با کاربر `WebUser` روی سرور شما |
| HTTPS، گواهی و نام میزبان | سایت CI روی `http://localhost:8080` است | `-Port`/بایندینگ واقعی + سیاست HTTPS در `1-install-iis.ps1` |
| کاتالوگ‌های واقعی `ReportT5V3`/`Personnel` | در job IIS هیچ کاتالوگی ساخته نمی‌شود؛ صفحاتی که SQL می‌خوانند ۵۰۰ می‌دهند | `deploy/2` + `4-verify-install.ps1` روی سرور شما |
| `System.Web.Mvc` و `Personnel.dll` واقعی | در مخزن هستند (پوشهٔ `T5Web [3.5.7]`) و CI از همان‌ها استفاده می‌کند؛ ولی رفتار runtime آن‌ها با IIS سنجیده نمی‌شود | `deploy/4-verify-install.ps1` |
| اسکیمای واقعی موتور | fixture از مدل ساخته می‌شود، نه از نصب فروشنده | `deploy/2` با `-BackupFolder` |
| دادهٔ واقعی و کارایی | پایگاه تست خالی است | سرور آزمایشی شما |

قاعده‌ای که از `docs/12` می‌آید و اینجا هم برقرار است: **سبز شدن CI یعنی «SQL با مدل اثبات‌شده
می‌سازد»، نه «پایگاه‌داده درست نصب شده».**

---

## ۵. اگر CI سبز شد چه چیزی عوض می‌شود

| ادعا | پیش از CI | پس از CI سبز |
| :-- | :-- | :-- |
| «کد کامپایل می‌شود» | ادعا (بدون csc/dotnet) | اثبات: MSBuild + خروجی `Release` |
| «۳۷ آزمون پاس می‌شوند» | طراحی‌شده، اجرانشده | اجراشده روی .NET 4.7.2 |
| «اسکریپت‌های `db/` کار می‌کنند» | متن بازبینی‌شده | اجرا روی SQL Server واقعی |
| «۸۱ دستور SQL معتبر است» | بررسی نام‌ها + پارس `sqlglot` | کامپایل واقعی در موتور |
| «اثر انگشت کار می‌کند» | منطق مقایسه آزمون‌شده | اجرای واقعی + `--compare` |

اگر CI قرمز شد، خروجی همان چیزی است که برای اصلاح لازم است؛ بخش ۶ به سند `docs/13` §۸ وصل است.

---

## ۵.۱ نتیجهٔ نخستین اجرای واقعی (و چرا مهم بود)

سه اجرا لازم شد و هر سه، عیب واقعی را نشان دادند — نه عیب محیط:

| اجرا | نشانه | علت | اصلاح |
| :-- | :-- | :-- | :-- |
| ۱ | `Show toolchain` شکست | `msbuild` روی `windows-latest` روی `PATH` نیست | `microsoft/setup-msbuild@v2` |
| ۲ | `error : The BaseOutputPath/OutputPath property is not set … Configuration='Release'` | هیچ `.csproj`ای بلوک `Debug`/`Release` نداشت؛ `build-and-test.sh` همیشه `csc` را مستقیم صدا می‌زد | تنظیمات هر دو پیکربندی + `<Private>true</Private>` برای `Dapper`/`System.Web.Mvc` (`BUG-32`) |
| ۳ | `verify: Department.DepartmentName column MISSING` | `verify.sql` با `OBJECT_ID(N'dbo.…')` فقط کاتالوگ جاری را می‌دید | پرسش با نام کاتالوگ + `USE [Personnel]` در `db/02` + گیت `sql-grant-gate.py` (`BUG-33`) |
| ۴ | همان خطای `OutputPath` تکرار شد | اصلاح اجرا ۲ با جابه‌جایی متنی روی `.csproj` نوشته شده بود و تگ `</Project>`ِ داخل `<Project>{guid}</Project>` را گرفته بود؛ XML خراب شد | اصلاح ساختاری + گیت `tools/project-file-gate.py` که هر پنج `.csproj` را XML-پارس می‌کند (`BUG-32`) |
| ۴ب | همان خطای `OutputPath` **باز هم** تکرار شد | گروه‌های `Debug`/`Release` بعد از `Import`ِ `Microsoft.CSharp.targets` گذاشته شده بودند؛ MSBuild پرچم `_OutputPathWasMissing` را در **لحظهٔ import** می‌سنجد، پس مقدار بعدی فایده ندارد. با ارزیابی پروژه توسط خودِ API ام‌اس‌بیلد دیده شد: `OutputPath=bin\Debug\` و `_OutputPathWasMissing=true` در یک ارزیابی | گروه‌ها به **قبل از** `Import` منتقل شدند و گیت `project-file-gate.py` حالا ترتیب را هم می‌سنجد (نه به‌عنوان سلیقه، به‌عنوان معناشناسی) |
| ۴ | `fingerprint: target fingerprint has no table lines` | `sqlcmd … -s ''` در PowerShell آرگومان خالی می‌گیرد و هیچ سطری چاپ نمی‌شد؛ خروجی هم با BOM نوشته می‌شد | ورودی خالی حذف شد، `Set-Content -Encoding ascii`، ابزار با `utf-8-sig` می‌خواند، و گام CI حالا خودش «کمتر از ۸۰ سطر جدول» را خطا می‌داند |
| ۵ | `smoke: Invalid column name 'WorkflowType'` | مدل ستون‌ها `Workflow.WorkflowType` و `Request.WofkflowType` را نداشت (پروپرتی ORM ندارند) ولی جدول واقعی دارد؛ اسکیمای جانشین نمی‌توانست آن دستورها را کامپایل کند | دو استثنای نام‌دار در `tools/engine-column-types.py` با شاهد manifest + گیت `synthetic-schema` که خودش وجود ستون را در manifest می‌سنجد + `verify.sql` که روی سرور واقعی هم می‌پرسد |
| ۵ | `smoke: Msg 102` روی چهار دستور `IN @Badges` | Dapper پیش از رسیدن دستور به موتور، `IN @Ids` را به فهرست پارامترها باز می‌کند؛ متن خام معتبر نیست | `sql-smoke` همان باز‌کردن را شبیه‌سازی می‌کند (table variable) و خودش هم آزمون دارد |
| ۸ | `error CS0246: 'Dapper' / 'Personnel' could not be found` | هر شش `HintPath` یک پوشه کم داشتند (`..\..` به‌جای `..\..\..`)؛ فایل سرجایش بود، MSBuild جای دیگری را نگاه می‌کرد | مسیرها اصلاح شد و گیت `project-file-gate.py` حالا وجود فیزیکی هر `HintPath` را می‌سنجد |
| ۷ | `smoke: Msg 402 / Msg 206` روی دو دستور درست | نوع پارامترها از **نام** حدس زده می‌شد: `@PendingOnly` («end») تاریخ شد و با `0` مقایسه شد، `@FromYear` («from») تاریخ شد و به رشته چسبید | نوع حالا از **نحوهٔ استفاده** می‌آید (چسبیدن به رشته، `TOP(@x)`، مقایسه با عدد) و نام فقط پیش‌فرض است |
| ۶ | `smoke: Invalid column name 'LeaveID' / 'CreateDate'` (و پیش از آن چهار مورد مشابه) | همان کلاس: ستون‌هایی که موتور دارد و موجودیت ORM نشان نمی‌دهد | ۱۳ ستون در `EXTRA_COLUMNS` (شاهد manifest + ارجاع در SQL خودمان)، `verify.sql` تک‌تکشان را روی سرور می‌پرسد، و گیت تازهٔ `tools/sql-column-coverage.py` این کلاس را در بیلد پیدا می‌کند، نه در دور بعدی CI |

درس عملی: **گیت استاتیک، اجرا نیست.** هر سه عیب از آن دسته‌ای بودند که متن درست به نظر
می‌رسد و فقط در موتور/کامپایلر خطا می‌دهند. این همان چیزی است که `docs/12` دربارهٔ پشتیبان و
`docs/07` دربارهٔ روش می‌گوید.

---

## ۵.۲ نتیجهٔ job سوم (`iis-install`) — دوازده اجرا، یازده عیب واقعی

هدف این job یک جمله است: **کیت استقرار را اجرا کن، ادعا نکن.** رسیدن به سبز، دوازده اجرا برد و
هر اجرا دقیقاً یک عیب را رو کرد؛ هیچ‌کدام در بیلد یا در بازبینی متن دیده نمی‌شد:

| اجرا | نشانه | علت | اصلاح |
| :-- | :-- | :-- | :-- |
| ۱ | `Publish (exit code 1)` بدون پیام | خطای PowerShell داخل `run:` گم می‌شود | `try/catch` + `::error::` و تخت‌کردن خطوط جدید |
| ۲ | `MSBuild failed with exit code 1` | همان: خروجی MSBuild دور ریخته می‌شد | اسکریپت خروجی را چاپ می‌کند و تا ۵ خط `: error` را در استثنا می‌آورد |
| ۳ | `CS1617: Invalid option '7.3' for /langversion` | `MSBuild.exe` نسخهٔ ۴ فریم‌ورک، `LangVersion 7.3` را نمی‌فهمد | کشف MSBuild با `vswhere` در `0` و `3` |
| ۴ | `the published folder contains source: …\Content\AriaAdminBranding.cs` | انتشار، `**\*.cs` را هم کپی می‌کرد | فیلتر پسوند در `0` + `requestFiltering` در `Web.config` |
| ۵ | `the published folder is missing bin\T5Web.Web.dll` | اسم اسمبلی وب `T5Web.Admin` است (csproj `AssemblyName`) و خروجی‌اش در `bin\` است، نه `bin\Release\` | انتظار روی `bin\T5Web.Admin.dll` + یافتن پوشه در `bin\<Configuration>` یا `bin\` |
| ۶ | `Cannot find drive. A drive with the name 'IIS' does not exist` | اسکریپت‌ها `#requires -Version 5.1` دارند و provider `WebAdministration` فقط در Windows PowerShell است؛ CI آن‌ها را با `pwsh` صدا می‌زد | `shell: powershell` + بررسی وجود درایو `IIS:` با پیام روشن |
| ۷ | `The app pool is the one the runbook describes` | خودِ ادعا: `$pool.$name` مسیر نقطه‌ای را باز نمی‌کند | خواندن صریح هر مقدار + گزارش مقدار واقعی در برابر انتظار |
| ۸ | `anonymous request returned 500` بدون جزئیات | بدنهٔ ۵۰۰ هیچ‌جا چاپ نمی‌شد | `-SkipHttpErrorCheck` و چاپ متن صفحه |
| ۹ | `Cannot add duplicate collection entry … 'fileExtension' set to '.cs'` | IIS خودش `.cs` را می‌بندد؛ `add` دوباره = خطای پیکربندی و **۵۰۰.۱۹ روی کل سایت** | `<remove>` پیش از هر `add` (`BUG-35`) |
| ۱۰ | `Could not load file or assembly 'Personnel' … incorrect format` | اسمبلی‌های موتور **x86** هستند و app pool ۶۴-بیتی بود؛ حتی `.css` هم ۵۰۰ می‌داد | `enable32BitAppOnWin64 = true` در `3`، بررسی `bitness` در `4`، ادعا در CI (`BUG-34`) |
| ۱۱ | `authenticated request returned 0` | PowerShell 7 با `-UseDefaultCredentials` روی `http` مقدار ۰ برمی‌گرداند و دلیل را در پیام استثنا می‌گذارد | چاپ `$_.Exception.Message` + رویدادهای `Application`/`System` |
| ۱۲ | `cannot protect plain text secrets sent over unencrypted connections` | **خودِ PowerShell 7** اجازهٔ فرستادن اعتبارنامه روی `http` را نمی‌دهد | `-AllowUnencryptedAuthentication` (در `4` فقط برای ۶+) |
| ۱۴ | `the term '.\deploy\4-verify-install.ps1' is not recognized` | مسیر نسبی از ریشهٔ مخزن غلط بود | `./src/T5Web/deploy/4-verify-install.ps1` |
| ۱۵ | گام با کد ۱ شکست، بدون پیام | `powershell` در Actions مقدار `$LASTEXITCODE` را به‌عنوان کد گام برمی‌گرداند و `4` با «تعداد FAIL» خارج می‌شود (بررسی‌های پایگاه‌داده در این job کاتالوگ ندارند) | صفر کردن صریح `$LASTEXITCODE` و `exit 0` |
| ۱۶ | `did not pass 'bitness' ->` با متن خالی | `Write-Host` در PowerShell ۵ به جریان **information** می‌نویسد؛ `2>&1` آن را نمی‌گیرد | `*>&1` |
| ۱۷ | **سبز** | — | هر سه job سبز، و `4-verify-install.ps1` هم واقعاً اجرا می‌شود (بررسی‌های `bitness` و `auth-anonymous`) |

درس: **اجرا کردن، عیب‌هایی می‌دهد که بازبینی هرگز نمی‌دهد** — ۵۰۰.۱۹ و `BadImageFormat` هر دو
از آن دسته‌اند که فقط یک IIS واقعی نشانشان می‌دهد.

---

## ۶. نکتهٔ هزینه و نگهداشت

* runner ویندوز در گیت‌هاب برای مخزن عمومی رایگان است؛ برای خصوصی، دقیقه‌ها از سهمیه کم می‌شود
  (ویندوز ۲ برابر ضریب دارد). برای همین `paths` فیلتر شده تا فقط تغییرات `src/T5Web/**` اجرا را
  روشن کنند، و `concurrency` اجرای هم‌زمان را لغو می‌کند.
* اگر Actions در سازمان خاموش است، همان workflow را به یک runner خودمیزبان ویندوزی بدهید:
  `runs-on: [self-hosted, windows]` و از قبل `MSBuild` و `sqlcmd` را نصب کنید. هیچ‌جای workflow
  دستور نصب از اینترنت ندارد جز `pip install dnfile` (اختیاری: بدون آن گیت `schema-manifest`
  پیام `STALE` می‌دهد).
