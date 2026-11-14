# راه‌اندازی Gateway و RemoteEnroll — حدود واقعی مسیر جدید

> این سند شاهد تاریخی برش `248cbcd` است. collector، پذیرش صریح تردد و بازیابی دو نفره در [افزودهٔ جدید](native-push-reviewed-recovery.fa.md) و [شواهد جدید](native-push-reviewed-recovery-evidence.json) تکمیل و روی Windows پذیرفته شده‌اند؛ جملات «هنوز ساخته نشده» در این سند مربوط به همان برش تاریخی‌اند.

این برش دیگر فقط قرارداد قابل‌حمل نیست: Web، SQL، courier، صف، worker و adapter ایزوله اضافه شده‌اند. **اما «مسیر نرم‌افزاری قابل نصب» با «راه‌اندازی عملیاتی روی دستگاه واقعی» یکسان نیست.** پذیرش نرم‌افزاری روی `248cbcd` ثبت شد: [Windows/LocalDB](https://github.com/alirezarahimy-code/IO/actions/runs/43640642835) با۳۹ بررسی SQL/HTTPS/courier/صف/worker و۴۷ قرارداد/child مصنوعی؛ [light Windows](https://github.com/alirezarahimy-code/IO/actions/runs/43640642847) نیز موفق است. [شاهد ساختاریافته](gateway-enrollment-operational-evidence.json) حدود باقی‌مانده را جدا ثبت می‌کند. این اجرای LocalDB جای اجرای کامل تاریخی SQL۱۰۳۱ نیست.

## ۱. پیش‌شرط مشترک

- فقط Windows برای تست‌های خودکار جدید؛ SQL این برش با LocalDB موقت روی runner Windows اجرا می‌شود. اجرای SQL/Linux خودکار همچنان خاموش است.
- .NET10 و migrationهای foundation تا `20261114130000_RemoteEnrollments` و migrationهای identity را با ابزار مهاجرت موجود پروژه، پس از backup و بازبینی اسکریپت، اعمال کنید. برنامه در startup مهاجرت یا ساخت حساب انجام نمی‌دهد.
- Web و worker باید به همان SQL و key-ring محافظت‌شده با application identity یکسان دسترسی داشته باشند؛ دسترسی فایل‌ها فقط به حساب‌های سرویس مربوط داده شود. TLS معتبر روی Web لازم است. forwarded headers و گواهی نامعتبر خودکار پذیرفته نمی‌شوند.
- صفحهٔ جدید: `/workspace/device-ingress-enrollment`. مجوزهای منو فقط نمایش را کنترل می‌کنند؛ تمام APIها حق و scope را دوباره در تراکنش بررسی می‌کنند. راهنما داخل برنامه نیز اضافه شده است.

## ۲. Gateway: مرز احراز هویت و نگهداری

### معنای این مسیر

`IO.PushGateway` یک **courier فایل spool محلی** است. فایل ATTLOG را با HMAC مستقل به `/gateway/v1/attendance` روی HTTPS می‌رساند. نه listener دستگاه در `/iclock` است، نه رمزنگاری native vendor، نه اثبات اینکه دستگاه به یک فرد تعلق دارد. serial و source در binding ثابت هستند، اما هویت native دستگاه هنوز تأیید نشده است.

پاسخ فقط `gateway-retained-device-unverified` است. این رسید بعد از commit اتمیک اصل بایت‌ها، snapshot تفسیر، receipt یکتا و audit امنیتی صادر می‌شود. **هیچ `OK:n` به firmware تولید نمی‌شود و تردد یا حقوق خودکار ثبت نمی‌شود.** تأمین‌کنندهٔ فایل محلی و تحویل/تأیید native دستگاه همچنان یک مسئولیت جدا و حل‌نشده‌اند؛ اجرای courier به‌تنهایی receiver عملیاتی native محسوب نمی‌شود.

### provisioning

۱. دستگاه را با profile صریح `zk-push`، source و timezone درست ثبت کنید. نسخهٔ هشت‌بایتی فعلی دستگاه را از موجودی بخوانید.
۲. اپراتور باید هر دو حق `devices.credentials.manage` و `devices.push.retain` را برای همان دستگاه داشته باشد. حساب سرویس منتخب نیز باید `devices.push.retain` و scope همان دستگاه را داشته باشد. هیچ حق از نام نقش یا شمارهٔ سریال استنتاج نمی‌شود.
۳. یک کلید مستقل تصادفی ۳۲بایت بسازید؛ از comm key دستگاه، SN، IP یا Stamp کلید نسازید. کلید را از طریق کانال امن در gateway قرار دهید و در فرم اتصال وارد کنید. فرم پس از موفقیت کلید را پاک می‌کند؛ سرور آن را فقط با Data Protection ذخیره می‌کند و هرگز در فهرست برنمی‌گرداند.
۴. شناسهٔ binding و gateway، شناسهٔ دستگاه و service principal، serial و key-version را صریح وارد کنید. timezone/source و نسخهٔ نصب در binding pin می‌شوند. retry همان binding فقط با همان داده و کلید مجاز است.
۵. در Web، `IO:PushGateway:EnableIngress=true` را **عمداً** تنظیم کنید. پیش‌فرض false است و در آن حالت machine route اصلاً map نمی‌شود. endpoint محدودیت یک‌مگابایت و نرخ ۱۲۰درخواست در دقیقه برای هر IP دارد.

### اجرای courier

روی Windows:

```powershell
dotnet publish ReCode/src/IO.PushGateway -c Release -o C:\IO\push-gateway
dotnet C:\IO\push-gateway\IO.PushGateway.dll C:\IO\secure\gateway.json C:\IO\spool\transfer.json
```

فایل پیکربندی دارای فیلدهای `Server` (مبدأ HTTPS بدون path/query/userinfo)، `BindingId`, `GatewayId`, `DeviceId`, `SourceId`, `Serial`, `TimeZoneId`, `KeyVersion`, `KeyBase64` است. این فایل secret است؛ آن را به Git، لاگ یا مکالمه نفرستید. `SourceId` و timezone باید عین binding سرور باشند.

فایل transfer دارای `TransferId` صریح و پایدار، `BodyFile` با مسیر مطلق اصل ATTLOG و `Stamp` nullable است. عدم Stamp با رشتهٔ خالی یکسان نیست. payload و metadata پس از اولین ارسال نباید تغییر کنند. هر اجرای مجدد همان TransferId را با timestamp امضای تازه می‌فرستد؛ server stable fingerprint را مقایسه می‌کند. از Stamp، hash سطر یا timestamp شناسهٔ رخداد نسازید. دو سطر یکسان داخل یک پیام حذف نمی‌شوند.

courier گواهی TLS عادی را اعتبارسنجی می‌کند، redirect دنبال نمی‌کند، body/پاسخ را محدود می‌کند، receipt را با transfer/hash/count تطبیق می‌دهد و فقط پس از پاسخ معتبر فایل `transfer.json.receipt.json` می‌نویسد. اصل spool را پاک نمی‌کند. خطا، قطع ارتباط یا گم‌شدن پاسخ **دلیل ساخت TransferId تازه نیست**؛ همان فایل را نگه دارید و وضعیت/مجوز را بررسی کنید. retry فقط حمل شواهد است، نه replay فرمان دستگاه.

### لغو و چرخش کلید

اتصال را با نسخهٔ تازه از همان صفحه لغو کنید. لغو یک‌طرفه و تاریخچه immutable است. پس از آن حتی retry پیام قدیمی رد می‌شود. برای چرخش، binding جدید با key-version و کلید جدید بسازید. تغییر نسخهٔ دستگاه/source/timezone یا لغو حق/scope حساب سرویس یا اپراتور provisionکننده نیز دریافت را متوقف می‌کند. بازبینی اصل پیام به حق مستقل `devices.push.read` نیاز دارد.

## ۳. RemoteEnroll: نرم‌افزار یکپارچه، اجرای واقعی پیش‌فرض ممنوع

### سیاست پیشنهادی تا زمان تصمیم سازمان

هیچ نمونه یا تنظیم فعال واقعی در repository قرار نگرفته است. `IO:RemoteEnroll:EnableRequests` در سرور و worker باید false/حذف‌شده بماند تا مسئول مجاز سازمان، مالک دستگاه و مسئول حفاظت داده تصمیم بگیرند. آزمایش روی فرد واقعی فقط با سیاست قانونی/سازمانی متناسب، مجوز مشخص، هدف محدود، مدت نگهداری، روش حذف، مسئول پاسخ‌گویی و شاهد مدل نصب انجام شود. شمارهٔ سندِ ساخته‌شده یا متنی که کاربر در فرم نوشته مجوز نیست.

### دروازه‌های فعال‌سازی پس از تصویب

- حق درخواست/لغو: `devices.enrollment.request`؛ خواندن شناسهٔ فرد و وضعیت: `devices.enrollment.read`؛ حساب سرویس: `devices.enrollment.execute`. هیچ قالب نقش پیش‌فرض این سه حق را نمی‌گیرد.
- در تنظیمات قابل‌اعتماد سرور **و** worker، `IO:RemoteEnroll:EnableRequests=true` و آرایهٔ `IO:RemoteEnroll:Approvals` با approval دقیق لازم است. worker علاوه بر این، در endpoint pin شدهٔ `IO:DeviceWorker:NativeEndpoints`، آرایهٔ `EnrollmentApprovals` مطابق همان approval را لازم دارد.
- approval شامل `Serial`, `Product`, `Firmware`, `FingerIndex` (۰ تا۹)، `SuccessActionCode`, `EmptySlotErrorCode` غیرصفر و متفاوت از sentinel نامعلوم `Int32.MinValue`، `EventMask` مثبت، `ModelEvidence`, `AuthorizationReference`, `RetentionAndDeletionPolicyReference`, `AllowEnrollment=true` است. مقادیر success/absence/event mask از مدل نصب تعیین می‌شوند؛ اعداد آزمایش مصنوعی توصیهٔ vendor نیستند.
- endpoint، comm key، مسیر مطلق dotnet و bridge و key-ring از همان پیکربندی امن موجود worker می‌آیند. Web هیچ credential یا مسیر اجرایی از فرم دریافت نمی‌کند. COM فقط در child جدید Windows/STA ایجاد می‌شود؛ نه در Web.
- درخواست به دستگاه enabled/standalone و نسخهٔ فعلی و نگاشت دقیق و معتبر همان SourcePersonId به کارمند نیاز دارد. صفر ابتدایی شناسه حفظ می‌شود. confirmation برابر `ENROLL-NEW-FINGER-NO-OVERWRITE` است. deadline کل۱۰تا۶۰ثانیه، انتظار فرد حداکثر۴۵ثانیه و کمتر از deadline process است.

### چرخهٔ اجرا و ایمنی

صف SQL یک بار claim می‌شود. polling باید خاموش و تمام کارهای دیگر همان دستگاه تمام شده باشند. صف‌های connection/operation/user-read/user-write/attendance و schedule با همان قفل تراکنشی و fence از enrollment جدا نگه داشته می‌شوند. worker پیش از دریافت credential و در حین انتظار، مجوز اپراتور/سرویس، scope، mapping، نسخهٔ نصب، policy و درخواست توقف را دوباره بررسی می‌کند.

bridge هویت کامل را می‌خواند و حضور اسلات را بررسی می‌کند. اسلات موجود رد می‌شود. false عمومی SDK اسلات خالی محسوب نمی‌شود؛ فقط کد absence دقیقِ مصوب نصب اجازه می‌دهد شروع شود. **SDK presence-only ندارد:** `GetUserTmpExStr` ممکن است template را موقتاً در حافظهٔ child materialize کند. آن رشته به worker/Web/SQL/فایل خروجی فرستاده نمی‌شود؛ با خروج process آزاد می‌شود، اما پاک‌سازی امن رشتهٔ managed تضمین نشده است. این رفتار باید در policy واقعی پذیرفته شده باشد.

`StartEnrollEx` فقط یک بار اجرا می‌شود. ACK موفقیت نهایی نیست. callback فقط در همان child تک‌عملیاتی به operation pin می‌شود؛ شخص، انگشت، کد موفقیت و طول مثبت باید مطابق باشند و سپس هویت و حضور جدید اسلات مستقل دوباره بررسی شوند. اتصال event و ABI از دو DLL hash-pinned به‌صورت خواندنی ممیزی شده‌اند؛ این شاهد اجرای firmware نیست. مالکیت فیزیکی/انحصاری دستگاه و نبود enrollment خارجی هم‌زمان جزو پذیرش نصب است؛ callback native GUID ندارد.

### توقف، crash و نتیجهٔ نامعلوم

- لغو pending تضمین می‌کند dispatch انجام نمی‌شود.
- لغو running فقط درخواست توقف است. worker child را متوقف می‌کند؛ deadline parent process tree را kill می‌کند. نتیجه پس از dispatch نامعلوم می‌ماند، حتی اگر SDK CancelOperation=true بدهد.
- lease منقضی یا شکست commit/از‌دست‌رفتن authority، کار را دوباره pending نمی‌کند. نتیجهٔ نامعلوم هرگز خودکار retry نمی‌شود.
- `unknown` تمام نوشتن‌های بعدی همان دستگاه، enrollment و polling را مسدود می‌کند؛ خواندن بازیابی هنوز ممکن است. **API بازکردن این قفل، پاک‌کردن تاریخچه یا تأیید حدسی نتیجه وجود ندارد.** رسیدگی حضوری و قرارداد بازیابی واقعی هنوز لازم‌اند. این محدودیت عمداً پنهان یا با ACK برطرف نشده است.

## ۴. معنای پذیرش این برش

CI جدید می‌تواند schema واقعی SQL Server/LocalDB، HTTPS واقعی، rollback audit، retry و revocation و صف/worker با child مصنوعی را بررسی کند. COM واقعی، firmware، اثر روی انگشت واقعی، native PUSH receiver/processing/ACK، سیاست واقعی زیست‌سنجی و بازیابی حضوری را ثابت نمی‌کند. تا این مرزها حل نشده‌اند، عبارت «هر دو روی دستگاه واقعی کاملاً عملیاتی شدند» یا کاهش بی‌قید شمارنده درست نیست.

### حدود fixture گواهی Windows

تست courier از خود executable تولیدی و اعتبارسنجی معمول TLS استفاده می‌کند. فقط روی runner موقت و elevated Windows، یک گواهی loopback مصنوعی با private key سازگار با Schannel ساخته و موقتاً به `LocalMachine Root` افزوده می‌شود؛ همان گواهی در finally برداشته می‌شود. CurrentUser Root ممکن است پنجرهٔ تأیید غیرتعاملی را معطل کند و برای این fixture استفاده نمی‌شود. هیچ گزینهٔ insecure یا callback پذیرش گواهی نامعتبر به courier تولیدی افزوده نشده است. این fixture دستور نصب گواهی آزمایشی روی سرور واقعی نیست. مرحلهٔ LocalDB سقف۴دقیقه دارد.
