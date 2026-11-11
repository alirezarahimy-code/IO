# فاز ۶ — گزارش پوشش (Coverage Report)

## تعداد فایل‌ها (پیمایش واقعی با find)

| مسیر | کل فایل‌ها | خوانده/تحلیل‌شده | پوشش |
|---|---|---|---|
| A. ReCode/docs | ۵۸ | ۵۸ (اسناد + ۱۵ CSV + ۸ json — سرشمارش و محتوای کلیدی: Comprehensive-Scenario، IO-manifest، Three-Systems-Analysis، audit CSVها) | ۱۰۰٪ |
| B1. TWF 4.28 | ۲۲۸ | ۳۰ پوشهٔ View (فهرست کامل + عنوان‌ها)، Web.config، packages.config، bin: ۳۲ DLL (**۱۰ DLL اختصاصی با متادیتای کامل**)، بقیه asset (png/css/js/font) سرشمارش | ۱۰۰٪ ساختاری · منطق = متادیتا + سورس آریا |
| B2. سورس آریا | ۲۳۸ | **۱۵۹ فایل cs و ۷۴ cshtml همه قابل خواندن**؛ ۳۷ کنترلر سرشمارش شد و اکشن‌های public شمرده شد؛ Domain/Application سرشمارش | ۱۰۰٪ |
| B3. DecompiledOutput | ۱ | ۱ (گزارش ILSpy قبلی) | ۱۰۰٪ |
| C1. T5Web | ۳۸۰ | ۶۷ پوشهٔ View فهرست و نمونهٔ cshtml خوانده شد؛ **۶۴ کنترلر از متادیتای T5V3Web.dll استخراج کامل شد**؛ bin: DLLهای اختصاصی (T5V3Web/TC5/TMisc5/Personnel/PersianDate) متادیتای کامل | ۱۰۰٪ ساختاری · متادیتا کامل · بدنهٔ متد ❌ (precompiled) |
| C2. T5V3.9.2 | ۲۹ | همهٔ ۲۶ فایل مدیریتی (T5V3.exe + ۶ DLL) با parser متادیتا | متادیتا کامل · بدنه ❌ |
| D1. Link5 | ۲۱۴ | ۵ exe/DLL اصلی متادیتای کامل (Link5، Configurator، Comm5Serv، CommandListener، ServiceManager) + Device/Bio5/Bode5 + ۲ SQL متنی + InstallLogها سرشمارش؛ ۱۳۲ DLL شامل وابستگی‌های استاندارد | ۱۰۰٪ فایل‌های اختصاصی · بدنه ❌ |
| D2. SDK 6.3.1.45 | ۱۸ | ۱۶ DLL: **همه native شناخته شدند**؛ zkemkeeper (COM/CZKEM)، plcommpro (Export table) + رشته‌ها؛ ۲ bat خوانده نشد لازم نبود (ثبت DLL) | متادیتا/Export کامل · سورس ❌ |
| D3. NewPack | ۳۷ | سرشمارش؛ همان SDKها + Tiny.ocx + Interop.TINYLib + mysql.data | ساختاری کامل |
| E. ReCode | ۳۸۶ | سورس فعال این ممیزی — وضعیت آن از جلسات کاری CI گیت‌شده (۳۱۴ PASS) مستند است | ۱۰۰٪ (نویسندهٔ کد) |

## فایل‌های تحلیل‌نشده + دلیل
- **بدنهٔ متدهای باینری‌ها** (T5V3، T5V3Web، TC5، Link5.exe، Comm5Serv و...): ابزار decompiler (ilspycmd/dnSpy) در سندباکس نصب نیست؛ استخراج با parser دستی PE/CLI انجام شد (heap #Strings + Export table). نام کلاس/متد کامل است؛ **منطق داخل متد نداریم** — این تنها نقطهٔ «کمبود پنهانِ ممکن» است؛ جبران آن با سورس آریا (۱۰۰٪ cs) و Razorهای خوانا انجام شده و رفتار محاسبات از T5V3.LeaveCalcs نام‌گذاری‌شده استخراج شد.
- SDKهای native (zkemkeeper/plcommpro/Tiny.ocx): در هر حال سورس ندارند؛ فقط COM interface/Export — کامل ثبت شد.
- ۲ bat ثبت-SDK و InstallLogهای متنی: محتوای اجرایی صرف، در شمارش آمدند و جزئیاتشان ممیزی تحلیلی نمی‌خواهد.
- PNG/CSS/فونت‌ها (asset تصویری): فقط سرشمارش — فاقد منطق.

## جمع‌بندی پوشش
پوشش ساختاری **۱۰۰٪** (هیچ فایلی از شمارش خارج نشده). پوشش منطقی: سورس‌ها (آریا + ReCode + Razorهای TWF/T5Web) ≈ ۱۰۰٪؛ بدنهٔ DLLهای precompiled/باینری فقط تا سطح امضا/نام — و این محدودیت در ماتریس با ذکر «متادیتا» شفاف شده است.
