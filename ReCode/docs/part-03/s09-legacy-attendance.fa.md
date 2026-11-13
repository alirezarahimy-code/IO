# S09 — انتقال دریافت تردد non-TFT از مرجع

این برش مسیر واقعی API مرجع را منتقل می‌کند، نه پروتکل جایگزین دستگاه. پیاده‌سازی COM در bridge ویندوز از همان CLSID، `SetCommPassword`، `Connect_Net`، `GetSerialNumber`، `GetDeviceStatus(machine,6)` و `ReadAllGLogData` استفاده می‌کند. محیط TCP مجازی فقط برای آزمون همین قرارداد/صف/worker است و ZK یا COM نامیده نمی‌شود.

## شاهد دقیق و اقتباس

- باینری موجود `T5V3.9.2/Personnel.DLL`، کلاس `Personnel.ZK.ZKDevice`، بدنهٔ مشاهده‌شدهٔ `WriteRecordsToList`؛ خروجی ILSpy فقط‌خواندنی `PersonnelZKDevice.decompiled.cs`، خطوط۱۲۳۷–۱۳۰۴. هش SHA256 خروجی: `58e33195ab271660e2f48b81d80167b91c693cb7986b955ee14c94d1b58b7eb5`.
- شاخهٔ TFT مرجع `SSR_GetGeneralLogData` و شاخهٔ دیگر `GetGeneralLogData` را فراخوانی می‌کند؛ `DateTime(...,0)` در شاخهٔ دوم شاهد دقت دقیقه است. تبدیل نام دستگاه به ادعای سازگاری نشده است.
- امضای `zkemkeeper.IZKEM.GetGeneralLogData`، خروجی metadata فقط‌خواندنی `IZKEM.decompiled.cs`، خط۵۶؛ دو خروجی مستقل `dwTMachineNumber` و `dwEMachineNumber` دارد. هش خروجی: `49856b1ed0aa89838405cfcef3a46da2f6275a80425898cda3cc70964a5ab701`.
- مرجع هر دو خروجی machine را در یک متغیر ref می‌نویسد؛ IO عمداً دو متغیر مستقل و دو فیلد `LegacyTerminalNumber/LegacyEnrollmentMachineNumber` دارد. هیچ‌کدام به‌جای pin هویت اتصال استفاده نمی‌شود و دور ریخته نمی‌شود.
- مقصد انتقال: `IO.ZkBridge/Program.cs:AttendanceReadSession.Next` و `IO.DeviceContracts/ZkAttendanceReadExecutor.cs`؛ صف/رسید در `IO.Infrastructure/Devices/DeviceAttendanceDownloads.cs`. هش باینری‌های اصلی و provenance در `zk-reference-contract.json` حفظ شده‌اند. decompile سورس اصلی نامیده نمی‌شود؛ مرجع تغییر نکرده است.

## دو قرارداد صریح؛ نه fallback

| مورد | `ssr-v1` | `legacy-int32-minute-v1` |
|---|---|---|
| فراخوانی | `SSR_GetGeneralLogData` | `GetGeneralLogData` |
| هویت | رشتهٔ دقیق، از جمله صفر ابتدایی | متن invariant خروجی Int32؛ صفر ابتدایی ساخته نمی‌شود |
| زمان | Gregorian تا ثانیه | Gregorian تا دقیقه |
| WorkCode | خروجی واقعی SDK | در این امضا خروجی ندارد |
| خروجی machine در tuple | ندارد | هر دو خروجی native محفوظ‌اند |
| هویت raw | `zk-ssr-v1:` + hash + multiplicity | `zk-legacy-int32-minute-v1:` + hash + multiplicity |

در raw legacy، `Second=0` و `WorkCode=0` فقط نمایش قراردادی «دقت دقیقه/فیلد ناموجود» هستند؛ **صفر اندازه‌گیری‌شده یا WorkCode واقعی دستگاه ادعا نمی‌شوند**. قالب immutable در والد و دو فیلد machine در payload تفاوت را حفظ می‌کنند. ثانیه/WorkCode غیرصفر، هویت padded/خارج Int32 یا metadata نصفه برای legacy رد می‌شود. خروجی منفی Int32 و تاریخ خراب به‌عنوان دادهٔ اصلی حفظ و در سرور قرنطینه می‌شوند، نه اصلاح یا حذف بی‌صدا.

انتخاب مرجع `IsTFTMachine` بود؛ IO به‌جای fallback هنگام خطا، انتخاب صریح نصب/درخواست منطبق دارد. خطای API قدیمی باعث اجرای SSR، نوشتن در دستگاه یا خواندن خودکار دوباره نمی‌شود. شمارنده/سریال قبل و بعد کنترل می‌شوند؛ شمارندهٔ ثابت تضمین snapshot اتمیک نیست.

## نصب هماهنگ و مسیر کاربر

1. migration بنیاد `20261113110000_DeviceLegacyAttendanceReads` را اعمال کنید؛ مجموع بنیاد۴۲ و Identity۲ است. ردیف‌های قبلی به‌طور سازگار `ssr-v1` می‌مانند. constraint دقیق، trigger منع تغییر قالب و guard downgrade از تاریخچه محافظت می‌کنند.
2. worker و bridge را **هماهنگ از همان نسخه** منتشر کنید. start frame تازه `ReadFormat` دارد؛ bridge قدیمی بدون tag پذیرفته نمی‌شود تا endpoint با قرارداد اشتباه اجرا نشود.
3. روی NativeEndpoint صریح تنظیم کنید: `AllowAttendanceRead=true` و `AttendanceReadFormat="legacy-int32-minute-v1"` برای مسیر non-TFT مرجع. برای SSR مقدار پیش‌فرض `ssr-v1` می‌ماند. `EnableAttendanceDownloads`، pin سریال/IP/port/machine و key-ring قبلی نیز لازم‌اند. قالب نصب نامعتبر در startup رد می‌شود؛ قالب درخواست نامنطبق، بدون تحویل secret/اجرای child، unsupported می‌شود.
4. در صفحهٔ `/workspace/device-attendance` قرارداد را انتخاب کنید، بازهٔ UTC، نسخه و کدهای ورود/خروج را صریح بدهید. قالب در intent/retry/claim و نمایش تاریخچه/اصل داده حفظ می‌شود. پاسخ نامعلوم اجازهٔ تغییر قالب همان UUID را نمی‌دهد.
5. snapshot کامل را با نگاشت دقیق همان هویت عددی بررسی و سپس صفحه را تأیید کنید. `1` و `00001` یک هویت فرض نمی‌شوند. اصل تاریخ/هویت خراب و ناشناس باقی می‌ماند. قطعی قبل از تکمیل مجوز commit نمی‌دهد؛ retry خواندنیِ تأییدشده lineage و قالب والد را حفظ می‌کند.

حقوق موجود `devices.attendance.read/extract/execute/commit` و حق مستقل دریافت attendance بدون تغییر اعمال می‌شوند؛ قابلیت جدید مسیر دورزدن grant نیست. scope دستگاه منتخب در query، claim، credential و append برقرار است. Linux بومی COM ندارد؛ مسیر تستی پنهانی برای فعال‌کردن COM در نصب وجود ندارد.

## تغییر قالب منبع و عدم دوبرابرشدن خاموش

دو قالب با دقت متفاوت اثبات نمی‌کنند دو tuple یک رخداد فیزیکی‌اند؛ نه صفر ابتدایی و نه ثانیه قابل حدس نیست. بنابراین اگر یک منبع قبلاً receipt واقعاً ingested از قالب دیگری داشته باشد، ردیف جدید با `format-history-review-required` قرنطینه می‌شود و خودکار وارد raw نمی‌شود. یکسان‌سازی/مهاجرت تاریخچهٔ مختلط نیازمند قرارداد بررسی مستقل است؛ نه حذف تاریخچه و نه ادغام حدسی فعال نشده است. bytes و شناسهٔ raw قدیمی SSR تغییر نمی‌کنند.

زمان‌بندی موجود فعلاً همچنان intentهای SSR می‌سازد؛ این برش آن را بی‌صدا legacy نمی‌کند. برای endpoint legacy از درخواست/تکرار خواندنی صریح این صفحه استفاده می‌شود. توسعهٔ قالب صریح polling برش مستقل بعدی است.

## آزمون و مرز پذیرش

- ۱۶ آزمون قرارداد و فرایند/TCP: حفظ هویت/دو machine، پایداری canonical، رد شکل اشتباه/قالب ناشناخته/child بدون tag، عدم fallback، خطای شمارنده/سریال، حفظ تاریخ خراب، دو دستگاه با قطعی مستقل، بازخوانی و timeout.
- ۱۳ بررسی SQL: حق مستقل، retry idempotent/immutability، binding قالب هنگام credential، شکل raw، quarantine، multiplicity، dedup، سازگاری SSR، guard تاریخچهٔ مختلط، lineage و منع downgrade.
- سه بررسی تازهٔ HTTPS/worker/SQL در fixture واقعی: mismatch نصب، partial legacy، و دو بار استخراج/ثبت سه‌صفحه‌ای از فرم Chromium با۲۰۶ receipt و۱۰۳ raw.
- راهنمای صفحه و تست DOM، انتخاب قالب و تکرار دقیق پاسخ نامعلوم را پوشش می‌دهند. نتیجه و SHA دقیق فقط پس از CI موفق در `s09-legacy-attendance-evidence.json` ثبت می‌شود.

این انتقال نرم‌افزار است؛ اجرای COM در این محیط، سازگاری مدل/firmware سازمان یا پذیرش نهایی دستگاه واقعی ادعا نشده است. آزمون سخت‌افزار طبق تصمیم کاربر در انتهای تکمیل نرم‌افزار انجام می‌شود؛ وجود مرجع فعال، مبنای اقتباس است و نبود سخت‌افزار محلی مانع انتقال این مسیر نبود.
