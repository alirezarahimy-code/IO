# R08 — ابزار بازبینی فقط‌خواندنی شواهد PUSH

## دامنهٔ این ادامه

پس از پذیرش codec و journal مستقل، دستور «ادامه بده» برای افزودن ابزار خواندن شواهد دنبال شد. مرز اعتماد قبلی تغییر نکرده است: **دریافت مستقیم vendor و احراز هویت دستگاه هنوز پیاده‌سازی نشده‌اند**. نه gateway جایگزین تصویب شد، نه Serial/Stamp شناسهٔ معتبر دستگاه/انتقال محسوب می‌شوند.

## سرویس، API و صفحه

`DevicePushReview` با `PermissionTransaction` حق مستقل `devices.push.read`، فعال‌بودن کاربر و scope دستگاه را در هر درخواست بررسی می‌کند. حق `devices.push.retain` به‌تنهایی هیچ خواندنی اعطا نمی‌کند. هیچ نقش پیش‌فرضی حق تازه دریافت نمی‌کند. migration تازه لازم نیست؛ schema همچنان۴۸ migration و۶۱ جدول است.

- `GET /device-push-evidence?deviceId={optional UUID}&offset=0`: projection metadata، مرتب‌سازی زمان و UUID، حداکثر۵۰ مورد با hasMore واقعی؛ hash کلید unique نیست.
- `GET /device-push-evidence/{id}/rows?after=0`: حداکثر۵۰ سطر از snapshot تغییرناپذیر SQL، نه decode مجدد با قواعد timezone امروز؛ به همراه wireStatus، mappingStatus و quarantineStatus جدا.
- `GET /device-push-evidence/{id}/original`: بایت‌های دقیق اصل پیام به صورت Base64 با hash و برچسب تأییدنشده.
- `/workspace/device-push-evidence`: صفحهٔ فارسی فقط‌خواندنی با راهنمای contextual و پیوند در موجودی برای دارندهٔ حق خواندن.

هیچ POST، upload وب، فرمان، commit تردد یا مسیر `/iclock` اضافه نشده است. سرویس ثبت شواهد قبلی همچنان داخلی است. سه GET داده نیازمند نشست معتبرند و پاسخ‌ها از middleware موجود `no-store` می‌گیرند؛ مسیر ناشناس دستگاه نیستند. GETهای بازبینی فقط audit مشاهده می‌نویسند و اصل شواهد/تردد/کارمندان را تغییر نمی‌دهند. صفحات shell مانند سایر صفحات پروژه جانشین کنترل دسترسی سرویس نیستند؛ داده، راهنما و navigation بر اساس حق واقعی محدود می‌شوند.

## نگاشت و قرنطینه

- نگاشت فعلی بر اساس شناسهٔ دقیق باینری UTF-16 شخص، source، شرکت و بازهٔ نیمه‌باز UTC در **زمان وقوع** بررسی می‌شود؛ `001` با `1` یکی نیست. شناسهٔ wire با طول بیش از۱۰۰ برای مسیر نگاشت پشتیبانی نمی‌شود و دور زده نمی‌شود.
- وجود یک نگاشت دقیق فقط `exact-mapping-found` است، نه ready/imported. حتی آن سطر با `unverified-device-identity` در قرنطینه می‌ماند. نام یا شناسهٔ داخلی کارمند از این endpoint برگردانده نمی‌شود.
- multiplicity حفظ می‌شود و سطر تکراری برچسب بازبینی می‌گیرد؛ هیچ حذف خودکار یا dedup بین پیام‌ها انجام نمی‌شود.
- تغییر منبع/محل/شرکت/timezone یا غیرفعال‌شدن/تغییر پروفایل، `target-changed` می‌دهد؛ UTC اولیه بازتفسیر یا تاریخچه بازنویسی نمی‌شود.
- PunchState، VerifyMode، WorkCode و reservedها خام‌اند؛ IN/OUT یا payroll از آن‌ها استنتاج نمی‌شود.
- خروجی صریحاً `deviceIdentityVerified=false`، `attendanceWritten=false` و `vendorAckAvailable=false` دارد. این endpointها رسید پردازش ماندگار یا ACK نیستند.

## رفتار UI و لغو دسترسی

هر fetch با URL نسبی، credentials هم‌مبدأ و no-store است. محتوای شخصی فقط با textContent رندر می‌شود. بازخوانی/focus، پاسخ‌های قبلی را با epoch نامعتبر می‌کند؛ response دیررس نباید پس از لغو مجوز دادهٔ پاک‌شده را بازگرداند. خطای۴۰۱/۴۰۳، فهرست، سطرها، snapshot و فیلتر را پاک می‌کند. اصل پیام تنها با کلیک آگاهانه download می‌شود. هیچ دکمهٔ ثبت/retain/ACK وجود ندارد.

## آزمون‌ها و وضعیت پذیرش

۱۶ بررسی جدید در suite SQL/HTTPS: تفکیک retain/read، حدود cursor/offset، صفحه‌بندی۵۰+۱ پیام و۵۰+۵ سطر، projection بدون اصل پیام، شناسهٔ دقیق و مرز mapping، multiplicity، quarantine و raw codes، مشاهدهٔ تغییر mapping بدون ثبت، تثبیت UTC، scope روی list/rows/original، نشست واقعی HTTPS، اصل CRLF، URL مستقیم، نبود mutation و ingress، revoke دامنه و حق بدون ورود مجدد.

reporter کامل اکنون۱۰۳۱ خط PASS می‌خواهد (۱۰۱۵ قبلی +۱۶ جدید). ۱۹ فایل آزمون DOM آفلاین و۱۹ بررسی CI policy محلی موفق‌اند؛ این‌ها آزمون SDK/سخت‌افزار نیستند. build محلی زیرساخت با NuGet `NU1301`/TLS EOF مسدود است و پذیرش build/SQL نیازمند CI دقیق همان commit است. در زمان نوشتن این بخش، نتیجهٔ جدید CI هنوز ثبت نشده است.

**R08 باز است؛۹ مورد فنی R08–R16 + H01 جدا باقی‌اند.** احراز هویت و commissioning معتبر، قرارداد retry انتقال vendor، نگاشت مصوب کدها، رسید پردازش اتمیک و durable-before-ACK هنوز مانده‌اند. این ابزار خواندنی به‌تنهایی ردیف R08 را نمی‌بندد.
