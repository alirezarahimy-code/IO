# ادامهٔ بخش سوم: S09 استخراج خواندنی SSR و trace تردد

**بخش سوم = S04–S12 و هنوز کامل نشده است.** S10/S11 پیاده نشده‌اند و اجرای واقعی SDK، مدل/firmware، نصب عملیاتی و سناریوی چند دستگاه/قطعی/تبادل/تنظیمات باقی‌اند. این گزارش پذیرش سخت‌افزار یا اتمام بخش سوم نیست.

## پیاده‌سازی تازه

- `ZkAttendanceReadInput/Raw/Frame`, `NativeAttendanceReadSupervisor` و مسیر STA مستقل bridge: pin سریال، `ReadAllGLogData` و `SSR_GetGeneralLogData` فقط خواندنی، شمارندهٔ قبل/بعد و پایان دقیق، frame محدود، batchهای ۵۰تایی با backpressure، deadline و kill child. هیچ DisableDevice/clear/delete/نوشتن ساعت/درب/بایومتریک ندارد.
- `DeviceAttendanceDownloads/Rows` و migration `20261112080000`: اصل tupleهای typed SDK قبل از تفسیر، از جمله تاریخ/هویت بد، verify/work signed و تمام ردیف‌های خارج بازه، در SQL محفوظ‌اند. این API wire bytes یا native event id ندارد؛ آن‌ها ساخته/حفظ‌شده ادعا نشده‌اند.
- `DeviceAttendanceDownloads`: درخواست idempotent، بازهٔ UTC صریح با Z و ثانیهٔ کامل، کدهای in/out مستقل، exclusion مشترک، grant سرویس مستقل، lease متناسب با deadline و بازیابی interrupted بدون replay کور. قسمت committedِ stream ناقص محفوظ است ولی قابل ورود به attendance نیست.
- `RowsAsync/CommitPageAsync`: شروع شامل/پایان غیرشامل، timezone ثابت محل نه host، DST/overflow و هویت/نگاشت نامعتبر در قرنطینه، فقط snapshot کامل و همان نسخه/هدف، تأیید `INGEST-SDK-ROWS` و دو حق commit/receive. هستهٔ موجود `AttendanceIngestion.AppendInTransactionAsync` در همان transaction فراخوانی می‌شود و receipt واقعی file-like trace download→sequence/hash/ordinal→RawEventId/ConflictId دارد؛ core موازی ساخته نشده است.
- تکرارهای کاملاً یکسان در یک log ordinalهای جدا می‌گیرند و حذف نمی‌شوند. دریافت کامل تکراری، اصل tuple و receipt تازه نگه می‌دارد ولی envelope یکسان raw جدید نمی‌سازد؛ تفسیر متفاوت به conflict موجود می‌رود. **این هویت tuple/multiplicity، شناسهٔ فیزیکی نیست:** بعد از reset/replacement log، تغییر سخت‌افزار با همان source یا رخداد کاملاً یکسان، هم‌ارزی فیزیکی تضمین نمی‌شود. همهٔ شواهد downloadها باقی‌اند.
- canonical v1 مستقل از serializer options/reflection/Unicode blocklist/culture است: فیلدهای ثابت، decimal invariant و escape صریح هر code unit شناسه. متن ناقص/losssy Unicode به‌جای ترمیم خاموش، stream ناقص می‌کند.
- `/workspace/device-attendance`: موجودی لینک مستقیم دارد؛ درخواست/تاریخچه/اصل tupleها، قرنطینه، ثبت نسخه‌دار و بازیابی پاسخ نامعلوم. UTC مرورگر حدس زده نمی‌شود، textContent امن است، revoke داده‌ها/فرم/help/منو را پاک می‌کند.
- چهار حق `devices.attendance.read/extract/execute/commit`؛ admin تازه read/extract/commit، operator تازه read، هیچ قالب انسانی execute نمی‌گیرد. grant/revoke واقعی، help کامل و Panel هم‌زمان ساخته شدند. حقوق همچنان سراسری‌اند؛ دستگاه‌منتخب ادعا نمی‌شود.

## شاهد اجرا

کد **`59e2e3bf7c35df303b2fd9b268939bbcba61dbb9`**:

- SQL/HTTPS/مرورگر واقعی: **`43222725559` success**؛ **۵۷۴ PASS** شامل ۱۸ بررسی سرویس و ۵ بررسی HTTP S09.
- Windows/Linux: **`43222725328` success**؛ **۴۴ Foundation و ۵۵ portable**.
- **۱۰۵ مجوز، ۴۸ صفحه/مقاله، ۳۱ migration و ۵۶ جدول**؛ چارت مستقل و CSVها هم‌خوان‌اند.
- تست‌ها: pin/ترانکیشن/شمارنده/deadline/فیلدهای missing/Unicode، tuple خراب، DST/overflow، بازه، duplicate batch، multiplicity، read ناقص، ورود اتمیک raw، raw/conflict trace، دریافت دوباره، آزادسازی ناشناس فقط با نگاشت و re-review، لغو/recovery، grant/revoke و SQL history/downgrade guard. مرورگر واقعی preview، ready+unknown، commit واقعی raw و reload رسید را پیمود؛ هیچ SDK واقعی اجرا نشد.
- [شاهد ماشین‌خوان](../part-03/s09-component-evidence.json)، [راهنمای عملیات/مرز داده و هویت](../part-03/s09-attendance-extraction.fa.md)، [نصب/خاموشی پیش‌فرض worker](../part-03/device-worker-setup.fa.md).

مدل فقط پس از تأیید قرارداد Gregorian/SSR مجاز است؛ `EnableAttendanceDownloads/AllowAttendanceRead` پیش‌فرض false. شمارندهٔ پایدار atomic snapshot نیست، range در سرور و پس از حفظ داده اعمال می‌شود و حجم read خود دستگاه را کم نمی‌کند. alias چند DeviceId برای یک endpoint فیزیکی و مدیریت خارجی هم‌زمان سناریوی پذیرفته نیستند. اصل tuple و receipt واردشده immutable، downgrade دارای تاریخچه مسدود و حذف/retention خودکار مصوب نشده است. این نتیجه، S10 یا S11 یا پایان بخش سوم نیست.
