# R10 — انتقال زیست‌سنجی؛ برش اول اثرانگشت/چهره، نه بستن R10

## دامنهٔ جاری

ترتیب کار R10→R16 حفظ شده است. این برش مسیر واقعی نرم‌افزارِ انتقال مستقیم **یک اسلات اثرانگشت یا چهره** بین دو دستگاه را می‌سازد. R10 هنوز باز است: **عکس، بازیابی مستقل نتیجهٔ unknown و پوشش تکمیلی مجوز/بازیابی** باید پیش از پذیرش کل ردیف تکمیل شوند. R11 و حذف‌های بعدی هنوز شروع نشده‌اند. عدد مانده‌ها همچنان۷ نرم‌افزاری + H01 است.

انتخاب کمینه‌سازی داده در این برش، انتقال مستقیم دستگاه‌به‌دستگاه داخل **یک child یک‌بارمصرف STA** است؛ نه مخزن template در SQL، نه فایل export در مرورگر، نه فایل موقت خام. ورودی IPC فقط اتصال/شناسه/قرارداد است و خروجی فقط operation ID و state. template در وب، SQL، audit و خروجی bridge قرار نمی‌گیرد؛ digest قابل‌همبستگیِ template نیز ذخیره نمی‌شود. این مسیر جای import/export فایل یا vault زیست‌سنجی را نمی‌گیرد.

## مرجع و فرمت

مرجع رفتار، decompile نوع `Personnel.ZKDevice` از بستهٔ T5Web/Personnel و ABI دو `zkemkeeper.dll` موجود مخزن است. مسیر قدیمی برای اثرانگشت از `GetUserTmpExStr`/`SetUserTmpExStr` و برای چهره از `GetUserFaceStr`/`SetUserFaceStr` با index15 استفاده می‌کند. این یکسانی نام API، سازگاری الگوریتم/مدل/firmware دو دستگاه را ثابت نمی‌کند.

[شاهد TypeLib](r10-biometric-typelib-evidence.json) از باینری‌ها به صورت داده استخراج شده است، نه بارگذاری یا اجرای COM:

- fingerprint getter/setter: DispId236/238 با BSTR و flag/native length؛
- face getter/setter: DispId233/234 با BSTR و طول native؛
- طول native از SDK حفظ می‌شود و با تعداد کاراکتر یا اندازهٔ Base64 یکی فرض نمی‌شود؛ رشته به ASCII دیگری تبدیل یا normalize نمی‌شود؛
- دادهٔ این قرارداد باید printable ASCII بدون whitespace و در سقف کاراکتر/طول native مصوب باشد. فرمت ناسازگار رد می‌شود، نه اصلاح حدسی؛
- کد غیبت دقیق و غیرصفر در هر دستگاه مستقل تصویب می‌شود. false عمومی، exception، buffer ناقص یا خطای نامعلوم «اسلات خالی» نیست؛
- fingerprint index0..9 و flag0/1 صریح است؛ مدل‌های دیگر و duress flag حدس زده نمی‌شوند.

### چرا عکس فعلاً به setter template نگاشت نشده است؟

کد مرجع عکس را با `ReadFile(machine, badge.jpg, directory)` و `SendFile(machine, path)` و فایل موقت خام جابه‌جا می‌کند. برخی ABIها فقط `PTR BYTE` خروجی بدون ظرفیت buffer دارند؛ از روی این امضا ساخت buffer امن اثبات نمی‌شود. در این برش هیچ generic file passthrough، badge/path دلخواه، byte-pointer حدسی یا کپی فایل خام در پوشهٔ برنامه اضافه نشده است. کار بعدی R10 طراحی انتقال عکس با نام ثابت، محدوده/صحت محتوا، حفاظت و quota فضای staging، پاک‌سازی crash و read-back قابل‌اثبات است؛ عکس از دامنه حذف نشده است.

## پیش‌شرط‌ها و کنترل داده

در هر دو سر، کاربر عادی **موجود و فعال** لازم است؛ مقایسهٔ BSTR برگشتی با شناسهٔ دقیق انجام می‌شود، نه تبدیل `001` به عدد۱. نام، کارت، password، privilege، AccGroup و دادهٔ درب نوشته نمی‌شوند. ساخت کاربر، حذف template، overwrite، BeginBatchUpdate و RemoteEnroll در این مسیر وجود ندارند.

برای یافتن دقیق کاربر، SDK داخل child ممکن است فهرست کاربران و name/password را موقتاً materialize کند؛ `AllowTransientUserEnumeration` باید جداگانه و آگاهانه true باشد. `RequiresReadAllTemplate` تنها با شاهد نیاز cache همان مدل فعال شود؛ این API ممکن است SDK را وادار به cache چند template کند، هرچند برنامه فقط یک اسلات را انتخاب و منتقل می‌کند. هیچ‌یک به معنی مجازبودن حقوقی خودکار نیست. رشتهٔ managed پاک‌سازی امن تضمین‌شده ندارد؛ memory dump/pagefile و دسترسی مدیر سیستم باید در سیاست نصب لحاظ شوند. سقف BSTR پس از دریافت از SDK بررسی می‌شود؛ این سقف جای OS memory quota برای SDK معیوب را نمی‌گیرد.

## فعال‌سازی پیش‌فرض خاموش

`IO:Biometrics:EnableTransfers=false` پیش‌فرض است. برای فعال‌سازی، `IO:Biometrics:Approvals` در سرور و `BiometricTransferApprovals` در **هر دو** NativeEndpoint worker باید یک قرارداد یکسان و یکتا داشته باشند:

- `Source` و `Target`: Serial/Product/Firmware دقیق و متفاوت؛
- `Kind`: فقط fingerprint یا face؛ `Slot` و `FingerprintFlag` طبق حدود بالا؛
- `FormatReference`: شاهد سازگاری دقیق قالب/الگوریتم دو مدل؛
- `AuthorizationReference`: مرجع رضایت/مبنای قانونی و دسترسی همان موضوع؛
- `RetentionReference`: مرجع نگهداری گذرا، pagefile/dump و حذف؛
- `ExclusiveControlReference`: شاهد کنترل انحصاری دستگاه‌ها در طول انتقال؛
- `SourceAbsentError`, `TargetAbsentError`: کدهای دقیق مدل، نه۰ یا int.MinValue؛
- `MaximumCharacters`, `MaximumNativeLength`: حدود مصوب، هرکدام۱ تا۱MiB؛
- `RequiresReadAllTemplate`, `AllowTransfer=true`, `AllowTransientUserEnumeration=true` فقط پس از تصویب آثارشان.

هیچ قرارداد واقعی از مقدار نمونهٔ SYNTHETIC در آزمون‌ها ساخته نمی‌شود. هنوز دستگاه واقعی یا سیاست واقعی سازمان تصویب نشده است؛ حالت توصیه‌شده برای نصب فعلی خاموش است.

## مجوز، صف و اجرای واقعی نرم‌افزار

صفحهٔ `/workspace/device-ingress-enrollment` فرم مستقل R10 دارد. حقوق جدید هیچ نقش پیش‌فرضی ندارند:

- `devices.biometrics.jobs.read`: فقط metadata؛ دیدن ردیف نیازمند scope هر دو دستگاه است؛
- `devices.biometrics.source.read` و `devices.biometrics.transfer`: در هر دو سر، چون preflight/readback مقصد نیز دادهٔ حساس می‌خواند؛
- `devices.biometrics.execute`: فقط حساب سرویس با scope هر دو سر.

دو شناسهٔ source/target باید از نگاشت دقیق و جاری به **یک کارمند و یک شرکت** برسند؛ دو دستگاه متفاوت با host/port و serial متفاوت لازم‌اند. انتخاب scope فقط یکی از دستگاه‌ها اطلاعات جفت را آشکار نمی‌کند، اما fence داخلی همچنان آن endpoint را در برابر عملیات هم‌زمان محافظت می‌کند.

۱. `POST /devices/biometric-transfers/preview`: فقط نصب/نسخه/نگاشت/سیاست را بررسی می‌کند؛ SDK یا template خوانده نمی‌شود.
۲. `POST /devices/biometric-transfers`: همان intent و previewHash و `COPY-BIOMETRIC-TO-EMPTY-SLOT`؛ درخواست و audit اتمیک‌اند. UUID ثابت فقط برای retry عین همان درخواست است، نه شروع دوبارهٔ عملیات.
۳. هر دو دستگاه برای pending/running به‌صورت پایدار رزرو می‌شوند. polling باید خاموش و کارهای قبلی تمام/بررسی شده باشند. worker جدید در Windows فعال می‌شود و credential هر دو سر، قرارداد سرور/worker، نسخه‌ها و grantهای اپراتور را دوباره کنترل می‌کند.
۴. child هویت کامل هر دو سر، کاربر عادی، قالب مبدأ و حضور مقصد را می‌خواند. اسلات یکسان موجود no-op/unchanged، اسلات متفاوت موجود refused است. پیش از dispatch مبدأ و مقصد دوباره خوانده می‌شوند؛ این readها **CAS اتمیک firmware نیستند** و مجوز کار هم‌زمان نرم‌افزار ثالث نمی‌دهند.
۵. setter فقط برای مقصد خالی اجرا می‌شود؛ success صرف SDK کافی نیست. کل داده، flag و native length با read-back مقایسه می‌شوند. mismatch/exception/timeout/قطع authority پس از dispatch به unknown می‌رود، نه retry.
۶. حق/دامنه حین اجرا با فاصلهٔ۵۰۰ms و پیش از commit نتیجه بازبینی می‌شود؛ این فاصله تضمین توقف آنی firmware نیست. ناظر child deadline≤۶۰s دارد و ورودی/خروجی/خطا bounded و بدون log شخصی است.

لغو pending با نسخهٔ تازه، قبل از dispatch نهایی است؛ لغو running فقط درخواست توقف است. `GET /devices/biometric-transfers` metadata صفحه‌ای می‌دهد. نتیجهٔ copied فقط نتیجهٔ پروتکل bridge فعلی است؛ synthetic child در CI جای اجرای SDK نیست.

## unknown و باقی‌ماندهٔ همین ردیف

unknown هدف نوشتن‌های بعدی را مسدود می‌کند و هیچ API requeue یا override ندارد. خواندن تشخیصی عادی همچنان ممکن است. بازیابی فیزیکی enrollment برای این صف قابل استفاده نیست؛ نباید آن را خودسرانه به template transfer تعمیم داد. زیرکار بعدی باید observation بدون افشای template و شواهد/مجوز رفع fence مختص انتقال را اضافه کند. تا آن زمان unknown عمداً مسدود می‌ماند؛ R10 بسته نمی‌شود.

مهاجرت `20261114150000_BiometricTransfers` جدول metadata-only جدید را اضافه می‌کند؛ مجموع مدل۵۲ migration و۶۷ جدول دامنه است. قصد درخواست، claim و نتیجهٔ نهایی با trigger محافظت می‌شوند؛ downgrade با سابقهٔ موجود رد می‌شود. اجرای خودکار migration در startup اضافه نشده است.

## آزمون و پذیرش

تست‌های قرارداد/session و supervisor با مادهٔ صریحاً مصنوعی، و تست Windows LocalDB صف/مجوز/دو endpoint/worker واقعی با child مصنوعی اضافه شده‌اند. پذیرش نرم‌افزاری این برش روی `5a64085` ثبت شد: [Windows با۸۲ بررسی یکپارچه، شامل۱۹ بررسی جدید صف/worker و۳۱ قرارداد مصنوعی جدید](https://github.com/alirezarahimy-code/IO/actions/runs/43692226428) و [light/UI موفق همان SHA](https://github.com/alirezarahimy-code/IO/actions/runs/43692226421)، شامل۲۱ فایل DOM. [شواهد ساختاریافته](r10-biometric-transfer-evidence.json). این پذیرش، کل R10 یا SDK/دستگاه واقعی را نمی‌بندد؛ suite کامل تاریخی SQL۱۰۳۱ دوباره اجرا نشد. به دستور کاربر تست Linux اجرا نمی‌شود. شمارندهٔ R10 پس از این برش کم نمی‌شود.
