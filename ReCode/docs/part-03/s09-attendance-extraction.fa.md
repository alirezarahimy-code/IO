# S09 — candidate استخراج بازه‌ای تردد SSR، نه پذیرش SDK/مدل واقعی

`DeviceAttendanceDownloads` و `DeviceAttendanceRows` با migration `20261112080000` اصل tupleهای typed SDK را در SQL نگه می‌دارند. `/workspace/device-attendance` از موجودی لینک دارد؛ تاریخچه، اصل داده، قرنطینه و رسیدهای `RawEventId/ConflictId` دیتابیس‌محورند. منابع legacy فقط خوانده شده‌اند؛ بدنهٔ decompile‌شده سورس اصلی نامیده نمی‌شود.

## قرارداد مشاهده‌شده و مرز دادهٔ اصلی

در `Personnel.ZK.ZKDevice`، `ReadAllGLogData(machine)` و سپس `WriteRecordsToList` با `SSR_GetGeneralLogData` برای TFT مشاهده شد. tuple شامل شناسهٔ رشته‌ای، VerifyMode، CheckType، Year/Month/Day/Hour/Minute/Second و WorkCode است؛ تقویم در این مسیر Gregorian است. child جدید **شناسه را به عدد تبدیل نمی‌کند و خطاهای تاریخ را دور نمی‌ریزد**. همهٔ مقدارهای typed، حتی تاریخ نامعتبر/کدهای signed یا هویت null، با JSON canonical نگهداری می‌شوند. این API بایت‌های wire یا vendor-native event ID را در اختیار نمی‌گذارد؛ آن‌ها حفظ‌شده یا ساخته‌شده ادعا نمی‌شوند.

SDK مشاهده‌شده کل log را می‌خواند؛ بازه در سرور **پس از نگهداری همهٔ tupleها** اعمال می‌شود، نه اینکه ادعای range API تأییدنشده شود. شروع بازه شامل و پایان غیرشامل با UTC صریح و دقت ثانیه است. timezone محل/شرکت/منبع و نسخهٔ هدف هنگام درخواست ثابت می‌شوند. کدهای متفاوت ورود/خروج صریح‌اند؛ پیش‌فرض حدسی وجود ندارد.

child فقط اتصال، pin سریال، شمارندهٔ رکورد (status 6)، ReadAllGLogData و enumeration SSR را انجام می‌دهد. هیچ clear/delete، DisableDevice، تغییر ساعت، template/photo/بایومتریک یا کنترل درب ندارد. موفقیت نیازمند برابری تعداد قبل/بعد با تعداد tupleهای ارسال‌شده است؛ **این برابری snapshot اتمیک یا مدل تأییدشده نیست**.

## مجوز، صف و بازیابی

- `devices.attendance.extract`: درخواست/لغو pending با UUID، نسخه، بازه و کدهای دقیق؛ retry همان UUID با دادهٔ یکسان idempotent است.
- `devices.attendance.read`: اصل tupleهای شخصی، قرنطینه، تاریخچه و رسیدها؛ سراسری، نه محدودهٔ دستگاه منتخب.
- `devices.attendance.execute`: فقط هویت مستقل سرویس؛ بدون حق ثبت به attendance.
- `devices.attendance.commit`: بررسی صریح همان صفحه، با `INGEST-SDK-ROWS` و نسخهٔ تازه؛ حق همراه `attendance.events.receive` یا بستهٔ قدیمی `attendance.ingest` نیز در همان PermissionTransaction لازم است.

قالب admin تازه read/extract/commit و operator تازه فقط read می‌گیرد. execute به هیچ قالب انسانی داده نمی‌شود. grant/revoke و منو/help سرور واقعی‌اند؛ نقش‌های موجود خودکار تغییر نمی‌کنند.

worker با `EnableAttendanceDownloads=true` و `AllowAttendanceRead=true` روی NativeEndpoint، فقط پس از آزمون قرارداد Gregorian/SSR مدل مجاز است؛ هر دو پیش‌فرض false. Windows و runtime/SDK هم‌معماری، IP/port/serial pin و key ring معتبر لازم‌اند. opt-in اثبات سازگاری نیست؛ Linux native unsupported است.

چهار slot بین پنج queue عادلانه می‌چرخند؛ این سقف تعداد دستگاه/کاربر نیست. یک دستگاه با سایر دریافت/ارسال/diagnostic/تنظیمات هم‌زمان اجرا نمی‌شود. batch و صفحه ۵۰ tuple و frame حداکثر ۴۰۹۶ نویسه‌اند؛ شمار کل log سقف محصولی ندارد. deadline ۱–۶۰۰ ثانیه است. اصل بخش‌های committed تا آخرین batch پس از قطعی باقی می‌ماند؛ snapshot ناقص مجوز ورود به raw نمی‌گیرد. recovery پس از مهلت کامل child + دو دقیقه grace، running رهاشده را interrupted می‌کند؛ خودکار دوباره نمی‌خواند و موفقیت دیررس نمی‌پذیرد. بازخوانی تازه، intent تازه و بررسی صریح می‌خواهد.

## تفسیر، قرنطینه و اتصال به هسته

فقط snapshot کامل، همان دستگاه/نسخه/منبع/شرکت/محل/timezone، زمان معتبرِ بدون DST ابهام/شکاف/overflow، داخل بازه و دارای دقیقاً یک نگاشت معتبر برای همان هویت/شرکت/زمان، ready است. ناشناس، هویت بد، تاریخ بد، خارج بازه و کد ناشناخته دور ریخته نمی‌شوند. بعد از اصلاح مستقل نگاشت، ردیف ناشناس فقط با re-review تأییدشده می‌تواند آزاد شود؛ employee/mapping خودکار نوشته نمی‌شود.

ثبت حداکثر ۵۰ سطر در همان transaction مجازِ `AttendanceIngestion.AppendInTransactionAsync` انجام می‌شود؛ منطق raw/تعارض موازی ساخته نشده است. payload همان JSON canonical کامل tuple است؛ verify/work code نیز محفوظ‌اند. receipt واقعی هر سطر به RawEventId و در تعارض به ConflictId وصل است. اصل tuple/درخواست و رسید واردشده immutable؛ downgrade دارای داده با 51021 مسدود است.

## تکرار و محدودیت هویت رخداد

هر tuple کامل، hash SHA256 و رتبهٔ occurrence در **کل enumeration** دارد. تکرارهای کاملاً یکسان داخل همان log حذف نمی‌شوند: ordinalهای ۱،۲ و... جدا می‌مانند. شناسهٔ external، `zk-ssr-v1:<tupleHash>:<occurrenceOrdinal>`، صریحاً canonical-content/multiplicity است، **نه شناسهٔ بومی یا فیزیکی**. دریافت کامل دوباره با envelope یکسان، receipt جدید ولی raw مشترک می‌گیرد؛ تفسیر متفاوت به مسیر تعارض موجود می‌رود و raw قبلی overwrite نمی‌شود. همهٔ tupleهای هر download، از جمله خارج بازه/تکراری، نگه داشته می‌شوند.

پس از پاک‌سازی/جایگزینی log، تغییر سخت‌افزار با همان source، یا رخداد فیزیکی جدیدِ کاملاً یکسان با tuple و rank قبلی، هویت فیزیکی قابل اثبات نیست. این coalescence را به‌عنوان dedup سخت‌افزاری تضمین‌شده معرفی نکنید؛ پذیرش مدل/سیاست هویت و بازبینی trace لازم است. فرمت canonical v1 نام/ترتیب ثابت فیلدها، عدد decimal با InvariantCulture و escape صریح هر code unit شناسه به \uXXXX دارد؛ به escaping/reflection/culture تنظیمات serializer وابسته نیست و باید پایدار بماند؛ تغییر layout نیازمند طراحی نسخه و migration صریح است.

## آزمون

تست‌ها برای frame/pin/truncation/count/deadline، حفظ تاریخ بد، DST/overflow، batch replay، multiplicity، بازهٔ شامل/غیرشامل، unknown mapping، raw/conflict trace و repeated download، grant/revoke، version/cancel/recovery و SQL history guard اضافه شده‌اند. DOM offline و bridge/runtime محلی موفق‌اند؛ کد `59e2e3b` در SQL/HTTPS/browser `43222725559` و Windows/Linux `43222725328` **موفق شد: ۵۷۴ integration، ۴۴ Foundation، ۵۵ portable، ۱۰۵ حق، ۴۸ صفحه/مقاله، ۳۱ migration/۵۶ جدول**. مرورگر واقعی صف/preview/قرنطینه/ثبت raw و reload رسید را پیمود؛ SDK و چند دستگاه واقعی هنوز اجرا نشده‌اند. S10–S11 و پذیرش سخت‌افزار بخش سوم بازند.

[شاهد ماشین‌خوان، runها و محدودیت‌ها](s09-component-evidence.json).
