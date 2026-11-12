# ادامهٔ بخش سوم: S08 ارسال عادی کاربران

**بخش سوم = S04–S12 و هنوز کامل نشده است.** S09–S11 پیاده نشده‌اند و پذیرش واقعی SDK/مدل/چند دستگاه/قطعی/تنظیمات باقی است. این ادامه یک candidate نرم‌افزاری جدید می‌افزاید؛ نه موفقیت ساختگی سخت‌افزار.

## پیاده‌سازی

- `DeviceUserUploads`: جدول و migration `20261112070000`؛ یک intent immutable برای هر نفر/دستگاه، BatchId مشترک، rowversion، نتیجهٔ مستقل/partial، تاریخچهٔ نگهداری‌شده و downgrade guard 51020.
- `DeviceUserUploads.PreviewAsync/RequestAsync`: نگاشت جاریِ دقیق کارمند، snapshot موفق هدف، desired نام فعلی کارمند/کارت/enabled، hash دقیق preview و تأیید `UPLOAD-ORDINARY-USERS`. پاسخ نامعلوم ثبت با همان intent idempotent است؛ expected هدف هرگز overwrite پنهانی نمی‌شود.
- `ZkUserWriteExecutor`، supervisor و bridge مستقل STA: read-before-write و read-back، password موجود فقط در حافظهٔ child، شخص جدید بدون password با تأیید، privilege صفر. **هیچ AccGroup/درب/قالب/عکس/delete یا disable کل دستگاه فراخوانی نمی‌شود.** ورودی admin، نام بیش از ۲۰ نویسه یا قرارداد نامعتبر fail-closed است.
- worker چهار slot را عادلانه بین صف‌ها تقسیم می‌کند؛ اجرای مستقل دستگاه‌ها، exclusion همان دستگاه، permission سرویس، endpoint/serial pin و opt-in فقط attendance-only. ارسال به‌صورت پیش‌فرض خاموش است و مدل واقعی تأیید نشده است.
- پس از dispatch، خروجی خراب/false/timeout یا توقف سرویس به unknown منجر می‌شود؛ نه retry خودکار. unknown برای همان فرد با snapshot موفق تازهٔ پس از نوشتن و `REVIEWED-USER` بررسی می‌شود؛ outcome قدیمی تغییر نمی‌کند. retry انتخابی intent جدید است؛ افراد موفق تکرار نمی‌شوند. لغو تضمینی فقط pending است.
- `/workspace/device-user-uploads`: سازندهٔ فرد/دستگاه و صفحهٔ چندآیتمی، preview واقعی سرور، تأیید، نتیجه/تاریخچهٔ SQL، لغو و بازبینی unknown. پیوند از inventory، safe text rendering، حفظ intent پاسخ نامعلوم، CSRF و پاک‌سازی در revoke.
- `devices.users.upload` و `devices.users.upload.execute` مستقل از read؛ admin تازه upload، operator تازه read موجود، هیچ قالب انسانی execute نمی‌گیرد. grant/revoke/منو/help هم‌زمان ساخته شده‌اند.

## شاهد اجرا

کد **`ce8114978aa1d9aeaf2d570cc567e18b9cdfd2f6`**:

- SQL/HTTPS/مرورگر واقعی: **`43218632179` success**؛ **۵۵۱ PASS** شامل ۱۴ بررسی سرویس S08 و ۵ بررسی HTTP S08.
- Windows/Linux: **`43218632286` success**؛ **۴۳ Foundation و ۴۵ portable**.
- **۱۰۱ حق، ۴۷ صفحه/مقاله، ۳۰ migration و ۵۴ جدول**؛ CSV و چارت مستقل به‌روز شدند.
- تست مرورگر واقعی preview، تأیید، صف، لغو و reload را پیمود. خطای اولیهٔ resource، مجوز fixture و envelope preview در این مسیر اصلاح و سپس کل CI بازاجرا شد؛ gate تضعیف یا mock جای مرورگر/SQL گذاشته نشد.
- [شاهد ماشین‌خوان](../part-03/s08-component-evidence.json)، [راهنمای عملیات و محدودیت‌ها](../part-03/s08-user-upload.fa.md).

مدیریت هم‌زمان خارجی هنوز می‌تواند بین read و write دخالت کند؛ enumeration اتمیک/CAS سخت‌افزاری نیست. opt-in تنظیمات، مجوز سازگاری یا پذیرش دستگاه محسوب نمی‌شود. فقط آزمایشگاه واقعی می‌تواند حفظ metadata و رفت‌وبرگشت مدل را تأیید کند. بایومتریک تا سیاست مصوب خاموش است؛ این مانع دادهٔ عادی نیست. payroll و کنترل فیزیکی درب خارج دامنه‌اند.
