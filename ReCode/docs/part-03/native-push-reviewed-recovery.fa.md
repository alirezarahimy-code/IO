# مسیر تکمیلی PUSH و بازیابی RemoteEnroll

## مرز پذیرش

این سند افزودهٔ عملیاتی به `gateway-enrollment-operations.fa.md` است؛ نتایج آزمون قبلی فقط مربوط به SHA ثبت‌شدهٔ همان سند هستند. موفقیت build یا آزمایش مصنوعی مجوز فعال‌سازی زیست‌سنجی یا شاهد کار با مدل نصب‌شده نیست. برای نسخهٔ حاضر، نتیجهٔ Windows در سند شواهد جدا ثبت می‌شود.

مسیر نرم‌افزاری جدید:

`دستگاه کلاسیک ATTLOG → collector خصوصی → spool → HTTPS/HMAC → SQL quarantine + audit → receipt → ACK intent → OK: n`

سپس، **جدا از ACK**:

`بازبینی اپراتور + نگاشت دقیق + بررسی تکرار + طبقه‌بندی صریح → preview → تأیید → raw attendance + immutable commitment + audit`

Gateway هویت خودش را ثابت می‌کند، نه دستگاه را. محدودیت IP و SN نیز احراز هویت نیست. این راهکار برای LAN/VLAN ایزوله و نصب مصوب است، نه اینترنت عمومی یا native authentication پروتکل‌های Security PUSH.

## نصب collector

1. در ویندوز منتشر کنید: `dotnet publish src/IO.PushCollector -c Release -o <collector-directory>`.
2. یک حساب محدود مستقل، spool روی دیسک محلی پایدار و configuration با ACL فقط همان حساب/مدیر تهیه کنید. روی share شبکه، پوشهٔ قابل‌نوشتن کاربران یا پوشهٔ موقت نصب نکنید. سرویس باید فقط یک instance مالک spool داشته باشد؛ یک مالک دیگر یا مهاجم محلی خارج از تضمین است.
3. یک binding مستقل در وب مرکزی بسازید. key و config را در مخزن کد، command line یا log قرار ندهید. HTTPS مرکزی با certificate معتبر، بدون bypass و بدون redirect الزامی است.
4. JSON به نام‌های دقیق زیر تهیه کنید. تمام مقادیر هویتی از نصب واقعی گرفته شوند، نه از نمونه‌های تست:
   - `Gateway`: همان ساختار `GatewayCourierConfiguration`، شامل `Server` (HTTPS origin با `/`)، `BindingId`, `GatewayId`, `DeviceId`, `SourceId`, `Serial`, `TimeZoneId`, `KeyVersion`, `KeyBase64`.
   - `ListenAddress`: آدرس خصوصی NIC؛ نه `0.0.0.0`؛ `ListenPort`: پورت رزروشده؛ `DeviceAddress`: IP خصوصی ثابت دستگاه. collector از proxy header برای دورزدن pin استفاده نمی‌کند.
   - `SpoolDirectory`: مسیر مطلق موجود و دارای ACL؛ `InstalledModelEvidence`: مرجع آزمایش همان مدل/firmware؛ `DurableReceiptAckApproval`: تصویب اینکه ACK یعنی قرنطینهٔ بادوام نه پردازش حقوق.
   - `OptionsResponse`: snapshot دقیق گزینه‌های کلاسیک مصوب، خط‌های LF با نام‌های مجاز و یکتا، شامل `Encrypt=0`. مقدار نمونه برای نصب واقعی تجویز نمی‌شود. دستورات دستگاه، enrollment، عکس، template و OPERLOG پشتیبانی نمی‌شوند.
   - هر سه `EnableNativeCollector`, `AllowUnverifiedLanDevice`, `AllowDurableQuarantineAck` باید آگاهانه `true` شوند. پیش‌فرض هر سه false است.
   - `MaximumSpoolEntries` پیش‌فرض ۱۰۰۰ و `MaximumSpoolBytes` پیش‌فرض ۶۴ MiB؛ ظرفیت ناکافی یعنی 507 و بدون ACK، نه حذف شواهد.
5. با supervisor حساب محدود اجرا کنید: `dotnet IO.PushCollector.dll --config <absolute-config-path>`. نصب خودکار Windows Service برای collector در این تغییر اضافه نشده است؛ ثبت سرویس/راه‌اندازی پس از boot و پایش آن باید در supervisor نصب انجام شود. installer موجود DeviceWorker برای این executable طراحی نشده است.
6. firewall ورودی collector فقط از IP دستگاه؛ خروجی فقط به HTTPS مرکزی. پورت collector را به وب عمومی، NAT اینترنتی یا reverse proxy عمومی متصل نکنید. در دستگاه آدرس collector را مطابق دستور همان مدل تنظیم کنید.

### معنای دوام و retry

اصل بایت‌ها با CreateNew/WriteThrough/Flush(true) محفوظ می‌شود؛ transfer ثابت با فایل موقت و rename منتشر می‌شود. سپس سرور مرکزی journal، receipt و audit را اتمیک commit می‌کند. تنها پس از receipt معتبر و ذخیرهٔ ACK intent، پیام `OK: n` برمی‌گردد. ACK intent اثبات دریافت پاسخ توسط دستگاه نیست. قطع ارتباط پس از commit ممکن است ارسال مجدد دستگاه ایجاد کند.

HTTP ناموفق/timeout، receipt نامعتبر، خطای ذخیره یا row خراب نباید ACK موفق تولید کند. retry پس‌زمینه و restart همان transfer ID و اصل بایت‌ها را استفاده می‌کنند؛ retry اتصال بسته را ACK نمی‌کند. درخواست جدید از دستگاه transfer جدید است: چون event GUID نداریم، نمی‌توان بدون شاهد تشخیص داد retransmission است یا دو punch واقعی یکسان. **هیچ dedup خودکار با hash/Stamp/time انجام نمی‌شود.**

هیچ شواهدی خودکار حذف نمی‌شود. مدیر باید alarm ظرفیت/خطا، backup، رسیدگی به invalid-wire و رویهٔ آرشیو مصوب داشته باشد. فایل `.tmp` یا original بدون transfer پس از crash، ACK نگرفته است؛ بدون بررسی دستی ID جدید نسازید. محافظت filesystem و صحت ذخیره‌ساز/UPS بخشی از استقرار است؛ Flush نرم‌افزاری تضمین صداقت سخت‌افزار ذخیره‌ساز نیست.

## پذیرش صریح تردد

وب مرکزی: `IO:PushGateway:EnableReviewedAttendanceCommit=true` فقط پس از تصویب trust boundary نصب. این مجوز مستقل از `EnableIngress` است.

در `/workspace/device-ingress-enrollment`، سه حق مستقل `devices.push.read`, `devices.push.commit`, `attendance.events.receive` و scope دستگاه لازم است. هیچ نقش پیش‌فرض این حق جدید را نمی‌گیرد.

- از صفحهٔ قرنطینه، journal، ردیف و نسخهٔ محفوظ دستگاه را بردارید. `GET /device-push-evidence/{id}/rows` نسخه، provenance gateway و commitmentهای قبلی را نیز برمی‌گرداند.
- یک UUID پذیرش ثابت، تفسیر `in` یا `out`، مرجع بررسی اعتماد و مرجع بررسی هم‌پوشانی با pull/انتقال‌های قبلی وارد کنید. کد خام PunchState به معنی خودکار ورود/خروج نیست.
- `POST /devices/push/preview`: بدون ثبت تردد؛ hash شامل نگاشت/کارمند/نسخه و تفسیر است.
- `POST /devices/push/commit`: همان review، hash و `TRUST-REVIEWED-GATEWAY-ROW`؛ تغییر مقصد/سایت/شرکت/منطقهٔ زمانی/نسخه یا نگاشت موجب 409 می‌شود. فقط journal دارای receipt gateway واجد شرایط است؛ staging دستی احراز مبدأ نیست.
- تردد خام، commitment تغییرناپذیر و audit یک تراکنش‌اند. retry عیناً با همان UUID، شناسهٔ raw قبلی را می‌دهد؛ تغییر retry یا پذیرش مجدد همان line با UUID دیگر رد می‌شود. شواهد دستگاه همچنان `deviceIdentityVerified=false` هستند.
- External ID از journal+line، **شناسهٔ پذیرش شواهد است نه GUID رخداد فیزیکی دستگاه**. سطرهای تکراری و overlap با transfer دیگر فقط پس از بررسی صریح انسانی پذیرفته شوند. این عملیات اجرای محاسبات حقوق نیست؛ raw در مسیر معمول پردازش/محاسبهٔ سامانه قرار می‌گیرد.

## بازیابی فیزیکی unknown؛ دو شخص مستقل

راهکار توصیه‌شده، assisted recovery است؛ از remote CancelOperation یا نبود callback هیچ موفقیت/توقف قطعی استنتاج نمی‌شود. این تغییر مشاهدهٔ خودکار جدید SDK اضافه نمی‌کند. شواهد فیزیکی و مشاهدهٔ اسلات در این مسیر **اظهار مسئولانهٔ انسان‌ها** هستند، نه تأیید ماشین.

پیش‌شرط نصب: مدل/firmware و سیاست زیست‌سنجی همان درخواست قبلی باید معلوم باشند؛ یک رویهٔ **reset غیرحذفی** باید با شاهد نصب ثابت کند عملیات enrollment معلق را پاک می‌کند. factory reset یا حذف همه‌داده‌ها راهکار این مسیر نیست. در نبود این شواهد، فعال نکنید و unknown مسدود می‌ماند.

در سرور، `IO:RemoteEnroll:EnablePhysicalRecovery=true` و `IO:RemoteEnroll:RecoveryApprovals` را فقط پس از تصویب تنظیم کنید. هر عضو آرایه:

- `Enrollment`: عین `RemoteEnrollApproval` درخواست قبلی، شامل مرجع مجوز و نگهداری/حذف؛
- `PhysicalResetProcedureReference`: مرجع رویهٔ مصوب مدل؛
- `MinimumSettlingSeconds`: ۱۰ تا ۳۰۰ طبق شاهد نصب، نه انتخاب تصادفی؛
- `ResetClearsPendingEnrollment=true`, `AllowRecovery=true` فقط با شاهد بالا.

اجرای حضوری:

1. سرویس و process/child قبلی را واقعاً متوقف کنید؛ همهٔ polling و کارهای دستگاه را متوقف/تمام کنید. منتظر unknown نهایی و ثبت `FinishedAtUtc` بمانید. هیچ process قدیمی نباید بعد از reset دوباره دستور بدهد.
2. بعد از unknown، reset غیرحذفی مصوب را اجرا و شاهد توقف process و reset را در مخزن دسترسی‌محدود ثبت کنید.
3. پس از زمان settling مصوب، اسلات همان شخص/انگشت را مشاهده کنید؛ فقط `present` یا `absent` و مرجع شاهد، نه template یا تصویر، ثبت شود. حضور اسلات به معنی completion درخواست قبلی نیست و هرگز مجوز overwrite نمی‌دهد.
4. شخص اول با `devices.enrollment.recovery.propose` پیشنهاد را در فرم یا `POST /devices/enrollment-recoveries` ثبت می‌کند. زمان reset باید UTC، بعد از پایان unknown، حداقل یک settling قبل از اکنون و حداکثر یک ساعت قبل باشد. این پیشنهاد **قفل را باز نمی‌کند**.
5. شخص دوم با `devices.enrollment.recovery.approve`، scope همان دستگاه، نسخهٔ پیشنهاد تازه و شاهد مستقل، `POST /devices/enrollment-recoveries/{id}/approve` را تأیید می‌کند. اعطای هر دو مجوز به شخص اول اجازهٔ self-approve نمی‌دهد؛ لغو حق/دامنهٔ پیشنهاددهنده مانع تأیید است. هر دو عبارت `PHYSICAL-RESET-NOT-ENROLLMENT-SUCCESS` را صریح وارد کنند.
6. بعد از تأیید، فقط fence همین unknown رفع می‌شود. **رکورد قبلی همچنان unknown و ResultJson تهی می‌ماند**؛ هیچ requeue، completion مصنوعی یا حذف تاریخچه وجود ندارد. سایر unknownها و کارهای فعال همچنان مانع‌اند. درخواست آینده فقط با intent جدید و کنترل فعلی presence/no-overwrite قابل بررسی است. اسلات موجود را به قصد retry دوباره enroll نکنید.

دریافت پیشنهادها: `GET /devices/enrollments/{id}/recoveries` با حق enrollment.read و scope؛ شناسه‌ها، نسخه و شواهد شخصی را در log عمومی یا ticket عمومی کپی نکنید.

## مهاجرت و بازگشت

مهاجرت `20261114140000_IngressCompletion` دو جدول `DevicePushCommits` و `RemoteEnrollmentRecoveries` اضافه می‌کند؛ مدل مجموعاً ۵۱ migration و ۶۶ جدول دامنه دارد. مهاجرت startup خودکار فعال نشده است. پس از backup و بررسی DBA طبق مسیر موجود bootstrap اجرا شود. downgrade با وجود شواهد در این دو جدول عمداً رد می‌شود؛ rollback باینری با حفظ تاریخچه و flagهای false باید طراحی شود، نه حذف شواهد.

## باقی‌ماندهٔ نصب

این مسیر شکاف collector، پذیرش صریح raw و بازیابی انسانی کنترل‌شده را پوشش می‌دهد؛ احراز هویت native vendor، صحت IP/SN، شاهد reset واقعی مدل، مجوز سازمانی زیست‌سنجی و آزمون دستگاه واقعی را ایجاد نمی‌کند. بدون این پیش‌شرط‌ها سیستم امن به معنی **خاموش/مسدودماندن** است، نه دورزدن کنترل‌ها. H01 پذیرش سخت‌افزار مستقل باقی می‌ماند.
