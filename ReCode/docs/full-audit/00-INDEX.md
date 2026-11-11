# 00-INDEX — ممیزی کامل پنج سامانه (نقشهٔ خواندن)

تاریخ ممیزی: ۲۰۲۶-۱۱-۰۸ · شاخهٔ `arena/5496cceb-io` · ابزارها: `find`, python PE/CLI-metadata parser (`/tmp/mdex.py`, `/tmp/exp.py` — موقت), `strings`, grep. هیچ فایل موجودی تغییر نکرده است؛ فقط خواندن و تولید گزارش.

## موجودی فایل‌ها (فاز ۰ — نتیجهٔ پیمایش واقعی)

| مسیر | کل فایل‌ها | پسوندهای اصلی | ماهیت |
|---|---|---|---|
| A. `IO/ReCode/docs` | ۵۸ | 35 md, 15 csv, 8 json | مانیفست + اسناد + audit CSV |
| B1. `IO/TWF 4.28` | ۲۲۸ | 59 cshtml, 39 png, 36 css, 32 js, **32 dll**, 3 pdb, 3 config | سایت MVC منتشرشده؛ Razor خوانا؛ منطق در bin DLL |
| B2. `io-stage1-review/src/TWF` | ۲۳۸ | **159 cs**, 74 cshtml, 3 csproj | سورس کامل آریا (Application/Domain/Tests + Web فقط Views) |
| B3. `IO/DecompiledOutput` | ۱ | 1 md | گزارش امکان‌سنجی بازسازی TWF 4.28 با ILSpy |
| C1. `IO/T5Web [3.5.7]` | ۳۸۰ | 112 cshtml, 75 dll, 71 png, 9 aspx, **0 cs** | سایت **precompiled** (`PrecompiledApp.config`)؛ Razor خوانا؛ کنترلرها در `bin/T5V3Web.dll` |
| C2. `IO/T5V3.9.2` | ۲۹ | 1 exe + 25 dll + 2 xml | **باینری .NET** (بدون obfuscation) — دسکتاپ WinForms |
| D1. `IO/Link5 v4.0.4` | ۲۱۴ | 132 dll, 35 xml, 13 pdb, 7 exe, 7 config, 2 sql | **باینری .NET** + سرویس‌های ویندوز سرویس |
| D2. `IO/SDK 6.3.1.45` | ۱۸ | 16 dll + 2 bat | **native (غیرmanaged)** — ZKTeco StandAlone SDK (zkemkeeper COM + plcommpro) |
| D3. `IO/NewPack Dlls 10.06.1398` | ۳۷ | 29 dll, 3 ocx, 3 bat | SDKهای native + Tiny.ocx (بایومتریک) |
| E. `IO/ReCode` (کل، بدون node_modules) | ۳۸۶ | **124 cs**, 36 js, 34 html, 24 json, 6 csproj, 15 csv, 21 py | سامانهٔ در حال ساخت (شاخهٔ فعال) |

## پروژه‌ها و Referenceها (فاز ۰-۲)

- ReCode: بدون sln؛ ۶ csproj SDK-style در `Directory.Build.props`: `TargetFramework=net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`. پکیج‌ها: `Microsoft.EntityFrameworkCore.SqlServer 10.0.11`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.11`, `System.Security.Cryptography.Pkcs 10.0.13`. زنجیرهٔ Reference: `IO.Domain ← IO.Infrastructure ← IO.Web ← IO.Bootstrap`.
- سورس آریا: `TWF.Application.csproj`, `TWF.Domain.csproj`, `TWF.Tests.csproj` (۳ csproj؛ `TWF.Web` فقط پوشهٔ Controllers/Models/Views بدون csproj در snapshot).
- T5Web / T5V3 / Link5 / SDK: **هیچ csproj/sln** — deployed/binary فقط.

## یافتهٔ معماری کلیدی فاز ۰ (شاهد: نام فایل در bin)

`TWF 4.28/App/bin` و `T5Web [3.5.7]/App/bin` هر دو روی **هستهٔ یکسان T5V3** بنا شده‌اند: `T5V3.Model.dll`, `T5V3.DB.dll`, `T5V3.Attendance.dll`, `T5V3.LeaveCalcs.dll`, `T5V3.Misc.dll`, `T5V3.Report.dll`, `T5V3.ReportGenerator.dll` + `Personnel.dll` + `PersianDate.dll`. یعنی سه فرانت (T5V3.exe دسکتاپ، TWF 4.28 وب، T5V3Web وب) یک هستهٔ دامنه مشترک دارند — این هسته همان چیزی است که ReCode باید بازسازی کند.

## نقشهٔ خواندن فازها

| فایل | محتوا |
|---|---|
| `10-phase1-reference-systems.md` | فاز ۱ — ۹ بند برای TWF، T5Web، T5V3، Link5، SDK با شاهد |
| `20-phase2-recode-status.md` | فاز ۲ — همان ۹ بند برای ReCode با وضعیت ✅🟡🔴📝⚪ |
| `30-coverage-matrix.md` + `coverage-matrix.csv` | فاز ۳ — ماتریس ماژول‌ها × پنج سیستم |
| `40-gap-register.md` + `gap-register.csv` | فاز ۴ — ثبت شکاف‌ها با شدت |
| `50-charts.md` | فاز ۵ — نمودارهای Mermaid + CSVها |
| `60-coverage-report.md` | فاز ۶ — گزارش پوشش |
| `70-executive-summary.md` | جمع‌بندی مدیریتی |
| `80-go-live-destination.md` | **مقصد نهایی (GO-LIVE)** — تعریف «اجرایی در سازمان»، شکل نهایی پروژه، زنجیرهٔ رنگی داریم/لازم/تصمیم |
| `90-approved-scope.md` | تصمیم‌های جدید مصوب + ۲۴ صفحهٔ پیشنهادی + قیود پذیرش |
| `100-execution-plan.md` + `execution-steps.csv` | مسیر اجرای ۳۳ برش با پیش‌نیاز، خروجی، پذیرش و پوشش تمام شکاف‌ها |
| `110-current-inventory-and-requirements.md` | موجودی جاری، نواقص، شروط .NET 10/دو سیستم، مجوز و راهنمای نقش‌محور |
| `current-pages.csv` + `current-permissions.csv` | شواهد مسیر/فایل و کاتالوگ مجوزهای کد در برابر انتخاب‌گر UI |
| `IO-charts.html` | چارت انسانی مستقل و آفلاین؛ درخت صفحات به تفکیک نقش |
| `inventory.csv` | ماشین‌خوان: فرم‌ها/پاپ‌آپ‌ها/منوها × وضعیت ReCode |

## محدودیت‌های اثبات‌شدهٔ ابزار

- dotnet SDK / ilspycmd / dnSpy در سندباکس در دسترس نیست → استخراج باینری با parser دستی PE/CLI (heap#Strings + جدول Export) و `strings` انجام شد؛ **بدنهٔ متد بازسازی نشد**.
- `zkemkeeper.dll`, `plcommpro.dll`, `Tiny.ocx` و سایر SDKها native/COM هستند → فقط Export table و رشته‌ها.
- هیچ obfuscation در DLLهای مدیریتی دیده نشد (`obfuscated-suspect: 0` در همه؛ نام‌های معنادار مثل `CheckLockDB`, `CZKEM`, `LeaveCalcs`).
