# R08 — برش نخست PUSH: قرارداد wire، نه گیرندهٔ امن کامل

## دامنه و شمارنده

به‌ترتیب دستور ادامهٔ۹ مانده، از R08 شروع شد. این برش **هیچ ردیف ledger را نمی‌بندد**؛ شمارنده۹ فنی + H01 باقی است. decoder مستقل ساخته شده، نه دریافت vendor PUSH آمادهٔ بهره‌برداری. هیچ endpoint عمومی `/iclock/*`، ثبت‌نام خودکار، grant جدید، SQL journal یا ورود خودکار تردد اضافه نشده است. R09–R16 هم با این برش تکمیل نامیده نمی‌شوند.

## مرجع دقیق و آنچه واقعاً می‌گوید

[شاهد شماره‌خط و hash](s10-push-protocol-reference-evidence.json): متن عمومی از سند منتسب به سازنده `Attendance PUSH Communication Protocol 20200325`، repository `daniel-mekuria/zk_server`، commit `3139200c0f7364b51973ad4d160f78e3a5b83cab`، Git blob `bdc00d5bdcbbea5b94f9a0945afb362364f9d3a1` و SHA256 `4c4b8032c32d3e9ed3dbad32b9847302b2c377e1a4adb54fd7ea980f704a5d9c`. این mirror عمومی است، نه انتشار رسمی اصالت‌سنجی‌شده یا شاهد firmware هدف.

- بخش11.2: درخواست POST به `/iclock/cdata` با SN و `table=ATTLOG`؛ Stamp اختیاری و cursor است، نه nonce یا شناسهٔ رخداد.
- قالب مشخص هفت‌ستونی tab: PIN، زمان محلی Gregorian، Status، Verify، Workcode، Reserved، Reserved؛ رکوردها با LF جدا می‌شوند.
- پاسخ موفق `OK: تعداد رکورد پردازش‌شده` است. این پاسخ باید پس از پذیرش/ذخیرهٔ قطعی سرور ارسال شود، نه فقط پس از خواندن socket یا parse.
- بخش‌های6/7 و Appendix8 تبادل public key و factors را توصیف می‌کنند، اما بسته‌بندی کتابخانهٔ رمزنگاری و الگوریتم ترکیب عوامل برای کلید نشست را کامل مشخص نمی‌کنند. CRC32 ذکرشده نیز به‌خودی‌خود احراز هویت cryptographic یا HMAC نیست.
- schema این سند با Security PUSH `rtlog` یکی فرض نمی‌شود؛ SN، MAC، IP، header، متن تبلیغاتی `PushProtVer` و token حاصل از اطلاعات عمومی هیچ‌کدام به‌تنهایی اثبات هویت نیستند. HTTPS صرفاً با احراز سرور نیز هویت دستگاه را اثبات نمی‌کند.

## کد ساخته‌شده

`src/IO.DeviceContracts/ZkPushAttendanceCodec.cs` با قرارداد صریح `zk-attendance-push-20200325-attlog-seven-utf8-v1`:

- فقط `ATTLOG` دقیق؛ بدون fallback به rtlog، OPERLOG، BIODATA، عکس یا USB پنج‌ستونی.
- UTF8 strict؛ بدون locale fallback، BOM stripping یا trim پنهانی؛ سقف پیام1MiB،۱۰هزار رکورد و۱۰۲۴ کاراکتر هر خط فقط بودجهٔ یک پیام‌اند، نه سقف کاربر/دستگاه.
- حفظ byteهای اصلی، CRLF/LF و SHA256 کل پیام، hash هر tuple و multiplicity برای تکرار عیناً برابر؛ hash tuple شناسهٔ رخداد فیزیکی نیست و dedup دائمی/SQL هنوز ساخته نشده است.
- PIN و شش فیلد دیگر خام حفظ می‌شوند؛ کد ناشناخته یا صفر به IN/OUT یا نقش تبدیل نمی‌شود. هیچ نگاشت فرد، ثبت تردد یا payroll اجرا نمی‌شود.
- timezone از ورودی مصوب لازم است؛ ساعت میزبان مبنا نیست. زمان مبهم/ناموجود DST و تبدیل clamp‌شده قرنطینه می‌شوند؛ دادهٔ خام در اصل پیام باقی است.
- رندر تشخیصی بدون شخص، سریال یا cursor است؛ حافظهٔ اصل پیام از تغییر ورودی/خروجی محافظت می‌شود.
- `FormatCommittedAcknowledgement` **فقط formatter** با کنترل id/hash/count رسید ارائه‌شده توسط caller مورداعتماد آینده است. خودش commit یا احراز هویت را اثبات نمی‌کند؛ Guid ساختگی هم جای SQL receipt معتبر نیست. هیچ route این تابع را با پارامتر درخواست کاربر صدا نمی‌زند. پیام malformed/مخلوط formatter موفق ندارد.

۴۷ بررسی در `ZkPushCodecTests` برای قالب، byte/hash، multiplicity، Stamp، UTF8، حد پیام/خط/تعداد، DST دو سیستم‌عامل، حفظ رشته و redaction و شکل ACK اضافه شد. هر دو workflow Windows/Linux و SQL آن را جداگانه اجرا می‌کنند؛ این تست‌ها جزو991 SQL نیستند و SQL یا authentication آزموده‌شده معرفی نمی‌شوند.

## مرز مجوز و امنیت

این کتابخانه route/UI نیست؛ هیچ دادهٔ شخصی واقعی دریافت یا نگهداری نمی‌کند و مجوز موجود را گسترش نمی‌دهد. دریافت عملیاتی آینده باید حقوق مستقل دریافت سرویس، مدیریت اتصال/کلید، مشاهده و commit انسانی، scope، revoke و audit داشته باشد؛ نقش یا SN نباید grant ایجاد کند. هیچ biometric، حذف یا فرمان دستگاه در این برش مجاز نشده است.

## گام‌های باقیماندهٔ R08 به‌ترتیب

1. تثبیت مسیر هویت cryptographic قابل پیاده‌سازی: SDK/کتابخانه و قرارداد کامل vendor برای مدل‌های مجاز، **یا** تصمیم صریح دربارهٔ edge gateway احرازشده با مرز اعتماد جداگانه؛ دومی نباید به‌عنوان احراز هویت native دستگاه معرفی شود. الگوریتم جلسه از روی نام publickey/factors حدس زده نمی‌شود.
2. SQL journal immutable برای اصل پیام/خط‌ها و receipts؛ idempotency با byte/hash و tuple/multiplicity، conflict/quarantine و atomic ACK بعد از commit؛ Stamp تنها کلید dedup نیست.
3. endpoint محدود و rate/size/congestion/backpressure، مدل/session pinning، ابطال کلید و جلوگیری از downgrade/replay؛ بدون auto-register یا HTTP ناامن.
4. API/UI واقعی برای grant/revoke/scope، وضعیت اتصال، رسیدها و preview/commit نگاشت به مسیر raw موجود. نبود mapping غیبت/جریمه نیست.
5. آزمون یکپارچه HTTPS/SQL، قطع پیش/پس commit، ACK ازدست‌رفته، retry و چنددستگاه؛ سپس پذیرش سخت‌افزاری H01 در انتها.

پس «قرارداد wire ساخته شد» با «PUSH امن تکمیل شد» تفاوت دارد. سیاست بیومتریک/رضایت/نگهداری و حذف در R09–R15 نیز باید مستقل و صریح باشند؛ دستور کلی تکمیل این موارد، دادهٔ خام یا عملیات مخرب را خودکار مجاز نمی‌کند.

## پذیرش محدود همین برش

روی `6d11f0f`، [Windows/Linux](https://github.com/alirezarahimy-code/IO/actions/runs/43625122261)، [SQL۹۹۱](https://github.com/alirezarahimy-code/IO/actions/runs/43625122266) و [light](https://github.com/alirezarahimy-code/IO/actions/runs/43625122273) همگی success شدند.۴۷ بررسی codec جدا از991 SQL و129 profile هستند. [شاهد محدود](s10-push-wire-evidence.json) صریحاً authentication/endpoint/SQL journal و بستن R08 را false نگه می‌دارد. شمارنده همچنان۹ فنی + H01 است.
