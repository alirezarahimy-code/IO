# S10 — زمان‌بندی پایدار SDK؛ نه PUSH یا پذیرش سخت‌افزار

## دامنهٔ ساخته‌شده

`DevicePollSchedules` روی دو جدول واقعی `DevicePollSchedules/DevicePollRuns`، migration نسخه‌دار `20261112090000_DevicePollSchedules` و صف استخراج S09 کار می‌کند. `/workspace/device-schedules` صفحهٔ DB با ایجاد، تاریخچهٔ run، پیوند به دانلود واقعی، pause/resume نسخه‌دار و صفحه‌بندی ۵۰تایی است. تنظیمات هر زمان‌بندی پس از تأیید تغییرناپذیر است؛ تغییر نیازمند توقف و تأیید زمان‌بندی تازه است. این جزء **polling خواندنی SSR** است؛ پروفایل `zk-push` هنوز اجرا نمی‌شود.

- cursor اولیه UTC صریح، شامل، ثانیهٔ کامل و `Z` است. پنجره پایان غیرشامل و نسبت به اکنون با lag مصوب محدود می‌شود. هیچ timezone مرورگر یا کد ورود/خروجی حدس زده نمی‌شود.
- فاصلهٔ اجرای موفق ۱۵–۸۶۴۰۰ ثانیه، اندازهٔ پنجره ۶۰–۸۶۴۰۰، lag برابر ۰–۸۶۴۰۰، deadline برابر ۱–۶۰۰ است؛ بودجهٔ هر کارند نه سقف دستگاه/کاربر.
- فقط یک زمان‌بندی فعال برای هر DeviceId؛ هر endpoint فیزیکی دقیقاً یک DeviceId و یک مدیر SDK دارد. alias و اجرای همزمان مدیر خارجی پذیرفته نیست.
- هر tick کوتاه و transaction SQL مستقل است؛ انتظار شبکه بیرون transaction در child worker S09 است. run به دانلود واقعی و snapshot مقصد/محل/شرکت/zone/کدها وصل است؛ cursor صرف ایجاد یا شروع کار جلو نمی‌رود.
- فقط دانلود `succeeded` cursor را جلو می‌برد. حالت ثبت خودکارِ پیش‌فرض خاموش، هر tick حداکثر ۵۰ tuple را زیر اختیار **فعلی سازنده** از هستهٔ همان raw/conflict S09 می‌گذراند و `NextAfter` پایدار دارد. ناشناس/نامعتبر/بیرون‌بازه قرنطینه می‌ماند؛ «run موفق» به معنی ورود همهٔ tupleها یا محاسبهٔ کارکرد نیست.
- `FinishRun` و سپس cursor در یک PermissionTransaction commit می‌شوند؛ ذخیرهٔ میانی برای ترتیب FK/trigger است، نه commit نیمه‌کاره. SQL خطای 51022 برای دستکاری config، عقب‌بردن/جلوپریدن cursor بدون run واقعی، تغییر نتیجهٔ terminal، حذف history و downgrade مخرب دارد.

## نقش، مجوز و نصب

| مجوز | کاربرد و نقش شناخته‌شده |
|---|---|
| `devices.schedules.read` | metadata و run سراسری؛ قالب اپراتور و admin؛ نه حق اصل tupleهای شخصی |
| `devices.schedules.manage` | ایجاد و توقف؛ قالب admin، برای اپراتور فقط grant صریح؛ سازنده همچنین `devices.attendance.extract` لازم دارد |
| `devices.schedules.execute` | هویت سرویس اختصاصی، هرگز قالب انسانی؛ جدا از `devices.attendance.execute` |

ساخت حالت خودکار با `ENABLE-AUTOMATIC-SDK-INGEST` و حقوق مستقل `devices.attendance.commit` و `attendance.events.receive` یا بستهٔ قدیمی `attendance.ingest`؛ ساخت فقط‌خواندن با `ENABLE-SCHEDULED-SDK-READ`. توقف با `PAUSE-SCHEDULE`، ادامه با `RESUME-APPROVED-SCHEDULE`. همه نسخه و CSRF می‌خواهند؛ دادهٔ پاسخ نامعلومِ ایجاد با همان UUID و payload تکرار شود. قالب‌ها فقط نقطهٔ شروع‌اند؛ نقش موجود یا حق لغوشده خودکار بازگردانده نمی‌شود. هیچ implicit administrator grant وجود ندارد.

برای worker:

1. migration تأییدشده و هویت سرویس فعال/مجوز مستقل `devices.schedules.execute` را provision کنید.
2. `IO:DeviceWorker:EnableSchedules=true` و `EnableAttendanceDownloads=true`؛ هر دو پیش‌فرض false.
3. برای native endpoint تأییدشده `AllowAttendanceRead=true` و pin کامل/SDK Windows و ACL/key ring مطابق [نصب](device-worker-setup.fa.md). نبود تأیید مدل را با روشن‌کردن flag جبران نکنید.
4. worker چهار slot و شش صف منصفانه دارد؛ tick SQL هم یک نوبت است، نه slot شبکهٔ نامحدود. قفل مشترک و busy/backpressure از دو کار فعال همان DeviceId جلوگیری می‌کند.

## قطعی، توقف و اختیار

- pending/collecting cursor را تغییر نمی‌دهد؛ شکست خواندن backoff نماییِ پایدار با سقف فاصلهٔ ۸۶۴۰۰ ثانیه دارد و از همان cursor تأییدنشده و overlap مصوب بازخوانی می‌کند؛ انتهای range همچنان با بودجهٔ window و lag جاری محدود می‌شود. تکرار صرفاً read است؛ نوشتن، clear/delete/clock/enroll انجام نمی‌شود.
- `unsupported/identity-mismatch/credential-unavailable/endpoint-denied/device-changed/revoked/cancelled` زمان‌بندی را متوقف می‌کنند؛ تغییر مقصد/rowversion نیازمند تنظیمات تازه و تأیید تازه است.
- manage/extract و در حالت خودکار commit/receive سازنده در هر گام بازبینی می‌شود. revoke، زمان‌بندی را متوقف و pending را لغو می‌کند. سرویس execute مستقل و فعال بودن actor در سرور بررسی می‌شود.
- pause، pending را لغو می‌کند؛ اجرای running ممکن است تمام شود ولی run زمان‌بندی بسته می‌شود، cursor و ثبت خودکار آن ادامه نمی‌یابند. قبلاً ثبت‌شده rollback نمی‌شود. فقط خود سازنده با اختیار فعلی می‌تواند تنظیمات ثابت را resume کند؛ مدیریت مجاز می‌تواند pause کند.
- بدون worker یا execute معتبر، صف/بازیابی خودبه‌خود اجرا نمی‌شود. lease/partial recovery همان قرارداد S09 است؛ نتیجهٔ late یا تعداد ناقص کامل تلقی نمی‌شود.

## محدودیت قطعی

این پیاده‌سازی **PUSH احرازهویت‌شده، حفاظت replay و rate/size پروتکل دستگاه نیست**. برای آن مدل/firmware و قرارداد احراز هویت واقعی لازم است؛ serial pin احراز هویت رمزنگاری‌شده نیست. API باز و ناشناس برای دریافت vendor payload ساخته نشده است. بودجه/CSRF/هویت SQL، جای امنیت پروتکل PUSH را نمی‌گیرند.

SDK همهٔ log را می‌خواند و بازه در سرور اعمال می‌شود؛ lag به‌تنهایی تضمین کشف رخداد دیررس در پنجرهٔ قبلاً تأییدشده نیست. [هم‌پوشانی صریح](s10-overlap.fa.md) با پیش‌فرض۰ اضافه شده است؛ مقدار مثبت تا StartUtc clamp می‌شود و از مسیر dedup موجود عبور می‌کند، ولی دیررس قدیمی‌تر از lookback همچنان تضمین نشده است. تغییر ساعت/پاکسازی یا reset/replacement log همچنان محدودیت هویت tuple+رتبهٔ تکرار S09 را دارد. counter برابر atomic snapshot نیست. no hardware executed؛ پایان S10 یا بخش سوم ادعا نمی‌شود.

[شاهد نرم‌افزاری S10](s10-component-evidence.json): ۲۰ بررسی سرویس SQL، ۵ HTTP، DOM آفلاین و مرورگر واقعی شامل pause/resume/reload. این‌ها جای سناریوی واقعی چند دستگاه/قطعی نیستند.


ادامهٔ جدید: [قرارداد/نصب/حقوق/آزمون بازخوانی هم‌پوشان](s10-overlap.fa.md) و [شاهد دقیق](s10-overlap-evidence.json). اعداد ۲۰ سرویس/۵ HTTP بالا مربوط به commit تاریخی برش اولیه‌اند؛ مجموعهٔ فعلی ۲۸ سرویس/۷ HTTP زمان‌بندی دارد.
