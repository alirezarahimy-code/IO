# سیاست CI: توقف اجرای خودکار Linux به درخواست کاربر

## تصمیم و اثر

کاربر صریحاً خواست توسعه ادامه یابد، اما تست‌های Linux به علت زمان و منابع کنار گذاشته شوند. از این تغییر به بعد، هیچ job خودکار Linux در سه workflow این شاخه تخصیص نمی‌یابد. این تصمیم صرفاً محدودکردن پوشش CI است؛ نه حذف پشتیبانی محصول، نه بسته‌شدن R08–R16، نه پذیرش سخت‌افزار.

| مسیر | رفتار جدید |
|---|---|
| `io-cross-platform.yml` | با حفظ نام تاریخی فایل، نام نمایشی `IO .NET 10 Windows`؛ فقط `windows-2022`، بدون matrix لینوکس |
| `io-lightweight.yml` | فقط `windows-2022` و Git Bash؛ policy/audit/DOM/Chromium در Windows، با همان فیلتر تغییرات |
| `io-phase02-sql-check.yml` | بدون trigger push؛ تنها workflow_dispatch و شرط صریح `allow_linux_runner=true` که پیش‌فرض آن false است |

SQL قدیمی به Windows منتقل یا اجراشده معرفی نشده است. هیچ اجرای دستی Linux برای این تغییر آغاز نمی‌شود. اگر زمانی نیاز به اجرای آن باشد، مسئول مجاز باید آگاهانه گزینهٔ صریح را فعال کند؛ اجرای دستی بدون آن حتی runner لینوکس را تخصیص نمی‌دهد. این مسیر به عنوان امکان تشخیص آینده نگه داشته شده، نه استثنای خودکار از خواست کاربر.

## چه چیزی همچنان خودکار آزموده می‌شود؟

- تغییرات backend و فایل‌های حساس/ناشناخته: build و قراردادهای موجود .NET و publish و lifecycle سرویس فقط روی Windows.
- تغییرات UI: syntax و فایل‌های DOM آفلاین روی Windows. تغییرات preview نیز Chromium پیش‌نصب‌شدهٔ Windows را استفاده می‌کنند؛ Chrome از مسیر صریح Windows پیدا می‌شود.
- تغییر مستندات: فقط بررسی سبک Windows، بدون تکرار build سنگین یا SQL.
- لغو اجرای superseded، تاریخچهٔ before..after کل push، حق حداقلی GitHub `contents:read`، schema/ref audit، native emulator با transport محبوس و کنترل عدم ادعای سخت‌افزار حفظ شده‌اند.
- ۲۰ بررسی policy، از جمله قفل‌کردن runnerهای خودکار به Windows و opt-in اجباری Linux دستی. آزمون‌های fake-dotnet فقط کنترل شاخه‌های shell هستند؛ SQL واقعی اجرا نمی‌کنند.

## شفافیت پذیرش

آخرین SQL کامل واقعی همچنان `adcb551a6c65148358b12f910a93580907e0b229` با۱۰۳۱ PASS و [run43629451568](https://github.com/alirezarahimy-code/IO/actions/runs/43629451568) است. تست SQL حذف نشده، اما موفقیت build/DOM جدید جای اجرای آن را نمی‌گیرد. اگر کد SQL/تراکنشی بعداً تغییر کند و SQL تازه اجرا نشود، برش باید «SQL جدید آزموده نشده» بماند، نه پذیرفته‌شدهٔ کامل با شمارندهٔ تاریخی.

پذیرش قدیمی Windows/Linux روی commitهای گذشته همچنان شاهد تاریخی معتبر همان نسخه‌هاست. آن‌ها نباید به ادعای Linux-tested برای نسخه‌های بعد از این سیاست تبدیل شوند. خلاصهٔ light نیز صریحاً می‌گوید Linux platform و SQL integration اجرا نشده‌اند.

## وضعیت R08

آخرین کارکرد پذیرفته‌شده هنوز codec، journal داخلی و review فقط‌خواندنی است؛ [راهنمای review](s10-push-review-contract.fa.md). احراز هویت بومی دستگاه، receiver، پردازش/رسید و ACK هنوز ساخته نشده‌اند. تغییر CI پیش‌شرط قرارداد cryptographic یا تصمیم آگاهانه برای مرز اعتماد متفاوت را دور نمی‌زند. شمار رسمی۹ فنی + H01 ثابت است.

## نتیجهٔ تأیید همین تغییر

روی `e94e0b9d36dcc15046e641f55c1d73b0d047479f`، [build/قرارداد/publish/lifecycle ویندوز](https://github.com/alirezarahimy-code/IO/actions/runs/43631123619) و [light ویندوز](https://github.com/alirezarahimy-code/IO/actions/runs/43631123626) success شدند. API هر run دقیقاً یک job با label `windows-2022` نشان داد؛ مجموع runهای همان SHA فقط همین دو مورد است. در light،۲۰ policy،۱۹ فایل DOM و Chromium preview نیز واقعاً اجرا و موفق شدند. هیچ SQL یا Linux تازه‌ای اجرا نشده است.

مشکل اولیهٔ جداکنندهٔ مسیر در TypeLib audit با `Path.as_posix()` رفع شد؛ hashهای DLL و signatureهای مصوب دست‌نخورده‌اند. [شاهد ماشین‌خوان](ci-windows-only-evidence.json). نام نمایشی قدیمی workflow ممکن است در بعضی خروجی‌های GitHub cache شده باشد؛ label واقعی jobها و YAML جاری ملاک Windows-only بودن‌اند. پذیرش محصول/SQL قبلی در progress حفظ شده و این پذیرش CI جدا ثبت می‌شود.
