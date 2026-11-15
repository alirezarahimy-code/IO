# R12 — حذف بازهٔ بستهٔ تردد دستگاه با حفظ دقیق خارج بازه

## دامنه و شاهد مرجع

این مسیر فقط `DeleteAttlogBetweenTheDate(machine, from, through)` است. امضای `LONG/BSTR/BSTR` و خروجی BOOL از IZKEM با DISPID=501 در دو DLL هش‌ثابت استخراج شده است: [شاهد ABI](r12-attendance-range-erase-typelib-evidence.json)، ابزار `audit-attendance-range-erase-typelib.py`. در مرجع decompileشدهٔ `Personnel.ZKDevice` رشتهٔ `yyyy-MM-dd HH:mm:ss` دیده می‌شود. این شواهد **معنای شامل/غیرشامل مرز یا دقت firmware نصب‌شده را اثبات نمی‌کنند**؛ اجرای واقعی بدون قرارداد مستقل نصب مجاز نمی‌شود.

این مسیر نه `ClearData(machine,1)`، نه flag5، نه حذف کاربر/قالب/مدیر و نه factory reset را فراخوانی می‌کند. R11 و R12 خانواده‌های مستقل مجوز، endpoint، تأیید و نتیجه هستند؛ هیچ‌یک دیگری را فعال نمی‌کند. اشتراک جدول/قفل و رویهٔ کنترل archive برای جلوگیری از رقابت است، نه تجمیع مجوز. raw attendance، archive، quarantine، receipt و audit در SQL حذف یا خودکار ingest نمی‌شوند. بازگردانی تردد به دستگاه پیاده نشده است.

## معنای دقیق زمان

- هر دو مرز **شامل** هستند: `from <= native-wall-time <= through`؛ قالب دقیق میلادی `yyyy-MM-dd HH:mm:ss`، سال۲۰۰۰ تا۲۰۹۹ و حداکثر۳۱روز. بازهٔ معکوس، نامعتبر، ISO دارای T/Z/offset، ثانیهٔ کسری یا دامنهٔ بزرگ‌تر رد می‌شود؛ تقسیم یا گردکردن خودکار وجود ندارد.
- مرزها ساعت محلیِ خام دستگاه‌اند، **نه UTC و نه ساعت مرورگر**. `TimeZoneId` باید عین منطقهٔ site/archive باشد، اما به مرزها offset افزوده یا از آن‌ها کم نمی‌شود. زمان UTC توقف capture با زمان محلی انتخاب تردد متفاوت است.
- در برگشت ساعت پاییزی، دو رخداد با ساعت محلی برابر قابل تفکیک نیستند؛ **هر دو عمداً انتخاب می‌شوند**. حذف فقط یک occurrence پشتیبانی نمی‌شود. flag صریح `IncludeAllRepeatedWallClockOccurrences=true` لازم است. بازهٔ دارای پرش ساعت نیز صرفاً روی tupleهای موجود سنجیده می‌شود، نه با ساخت رخداد فرضی.
- در `legacy-int32-minute-v1`، ثانیه در native read گزارش نمی‌شود؛ فقط دقیقهٔ کامل مجاز است: ثانیهٔ ابتدا۰۰ و انتها۵۹. در SSR ثانیهٔ گزارش‌شده حفظ می‌شود. شاهد واقعی دقت firmware همچنان اجباری است.
- تاریخ native نامعتبر یا tuple غیرقابل طبقه‌بندی نباید بی‌صدا «خارج بازه» فرض شود؛ پیش‌نمایش/اجرای حذف پیش از dispatch رد می‌شود. هیچ تبدیل شناسهٔ `001` به `1`، نگاشت خودکار code یا حذف duplicate انجام نمی‌شود.

## پیش‌شرط غیرقابل جایگزین: توقف فیزیکی capture

[شرط و رویهٔ R11](r11-attendance-erase.fa.md) برای R12 نیز برقرار است: پیش از اخذ آرشیو، ثبت تردد جدید واقعاً با رویهٔ غیرحذفی مصوب متوقف شود و تا پایان readback ادامه یابد؛ کنترل انحصاری همهٔ نرم‌افزارها، کانال‌ها و حضور فیزیکی را پوشش دهد. شمار ثابت، mutex، دو scan یا `EnableDevice(false)` اثبات توقف capture نیستند. نرم‌افزار CAS firmware ندارد و EnableDevice را به‌عنوان میان‌بر فراخوانی نمی‌کند. اگر نصب نتواند این شرط را تأیید کند، قابلیت باید خاموش بماند.

دو حساب مستقل جای دو انسان واقعاً مستقل نیستند. سامانه جدایی حساب‌ها، مرجع شاهد، نسخه و قرارداد را بررسی می‌کند؛ اصالت فیزیکی شاهد را خودکار اثبات نمی‌کند. مجوز توسعهٔ مجازی، مجوز قانونی حذف یا نگهداری واقعی داده نیست.

## فعال‌سازی مستقل؛ هیچ نقش پیش‌فرضی حق حذف ندارد

تنظیمات trusted سرور/worker:

- `IO:AttendanceErase:EnableRangeDeletes=false`؛ با `EnableDeleteAll` فرق دارد.
- `IO:AttendanceErase:EnableRangeRecovery=false`؛ از بازیابی R11 مستقل است.
- `IO:AttendanceErase:RangeApprovals`: دقیقاً یک `AttendanceRangeEraseApproval` مطابق قصد.
- روی NativeEndpoint همان دستگاه: `AttendanceRangeEraseApprovals` عین قرارداد بالا، `AllowAttendanceRead=true` و `AttendanceReadFormat` مطابق `Erase.ReadFormat`؛ pin دقیق IP/port/serial و مسیر مطلق bridge/dotnet و key-ring مشترک طبق نصب worker.

`AttendanceRangeEraseApproval` شامل `Erase` از نوع قرارداد R11 (هویت Serial/Product/Firmware، قالب، مجوز قانونی/نگهداری/توقف capture/reset، سقف تعداد و سن و settling و سه flag صریح)، به‌علاوهٔ `TimeZoneId`, `BoundaryReference`, `TimestampPrecisionReference`, `NativeBoundaryConvention="closed-wall-second-v1"`, `IncludeAllRepeatedWallClockOccurrences=true`, `AllowRangeErase=true` است. `Erase.AllowErase=true` شرط مشترک ایمنی است، نه فعال‌سازی حذف همه؛ برای R11 همچنان switch و allowlist مستقل لازم است. واردکردن JSON در فرم هیچ‌کدام را فعال نمی‌کند. فرم نام‌های JSON camelCase یا PascalCase قرارداد نصب را می‌پذیرد.

حقوق مستقل `devices.attendance.range.erase.` عبارت‌اند از `read`, `propose`, `approve`, `execute`, `recover.propose`, `recover.approve`. proposer و reviewer هر دو به `devices.attendance.read` و scope همان دستگاه نیاز دارند. حق استخراج/اجرای دریافت SDK همچنان جداست؛ هیچ template نقش پیش‌فرضی حق حذف بازه یا بازیابی آن ندارد. scope و مجوز دو انسان و سرویس در اجرا بازبینی می‌شوند.

حدود مشترک: حداکثر۱۰۰۰۰۰ tuple، سن آرشیو۶۰..۱۸۰۰ ثانیه (پیش‌فرض۹۰۰)، settling۱۰..۳۰۰ ثانیه (پیش‌فرض۱۰)، native deadline حداکثر۶۰ثانیه. حجمی که دو scan و حذف/readback آن در این حد جا نمی‌گیرد unsupported است؛ افزایش حد یا تقسیم حذف با حدس مجاز نیست.

## جریان نرم‌افزاری

۱. polling و کارهای native دیگر متوقف، capture واقعاً متوقف و settling طی شود. دریافت SDK **کامل و بادوام** از `/workspace/device-attendance` بسازید؛ تمام tupleهای native، حتی ناشناس، کد نامعلوم یا خارج بازهٔ ingestion باید در `DeviceAttendanceRows` موجود باشند. فقط خروجی ingestion آرشیو حذف نیست.
۲. صفحهٔ `/workspace/device-attendance-range-erases` دو مرز محلی، approval نصب، UUIDها و rowversion دستگاه/آرشیو، زمان UTC توقف capture و مرجع شاهد را می‌گیرد. مسیر SDK، نام متد یا data flag ورودی کاربر نیست.
۳. `POST /devices/attendance-range-erases/preview` با CSRF، بدون SDK و بدون صف اجرایی، توالی کامل آرشیو را می‌خواند؛ digest کل multiset و digest خارج بازه را با حفظ multiplicity می‌سازد و شمار کل/منتخب/باقی‌مانده و معنای محلی را برمی‌گرداند. نسخه، زمان، zone، تمام قصد و هر دو تعهد به hash متصل‌اند. تغییر هر ورودی، preview مرورگر و hash سرور را باطل می‌کند.
۴. `POST /devices/attendance-range-erases` با intent عین preview، hash و عبارت `DELETE-CLOSED-DEVICE-LOCAL-WALL-RANGE` فقط پیشنهاد durable می‌سازد. UUID ثابت صرفاً retry همان پیشنهاد است، نه تکرار فرمان. از proposed، قفل بین تمام صف‌های native برقرار است.
۵. انسان مستقل مرزها، منطقه و دقت، شمار منتخب/باقی‌مانده، آرشیو و شواهد واقعی توقف را بازبینی و با نسخهٔ تازه در `/{id}/approve` تأیید می‌کند. self-signature حتی با هر دو grant ممنوع است؛ قصد و مجوز اجرا هرکدام حداکثر۲دقیقه معتبرند.
۶. worker Windows فقط خانوادهٔ enabled را claim می‌کند؛ claimer حذف همه نمی‌تواند قصد بازه را بردارد. pin دقیق مستقل endpoint، credential، policy، scope و authority دوباره کنترل می‌شوند؛ هر۵۰۰ms و پیش از ثبت نتیجه نیز authority بازبینی می‌شود. running منقضی هرگز replay نمی‌شود.
۷. child STA، هویت و شمار قبل/بعد و تمام tupleها را **دو بار** می‌خواند؛ هر دو بار کل digest، شمار منتخب و digest خارج بازه باید با آرشیو مصوب برابر باشند. اگر منتخب صفر باشد `range-empty` بدون dispatch برمی‌گردد. در غیر این صورت mapper فقط یک‌بار `DeleteAttlogBetweenTheDate(machine, from, through)` را با دو رشتهٔ دقیق فراخوانی می‌کند.
۸. برای `range-deleted-verified`، خواندن کامل تازه لازم است: شمار برابر کل منهای منتخب، هیچ رکورد منتخب باقی نمانده، و کل multiset فعلی دقیقاً برابر digest خارج بازهٔ قبل از حذف باشد. ACK، شمار باقی‌ماندهٔ برابر یا حذف‌شدن منتخب به‌تنهایی کافی نیست. حذف اضافه، تغییر tuple با شمار ثابت، باقی‌ماندن یکی از مرزها، false/exception/deadline/قطع خروجی یا نقص readback پس از dispatch، `unknown` است؛ فرمان تکرار نمی‌شود.

stateهای native فقط `range-deleted-verified`, `range-empty`, `archive-mismatch`, `failed-before-write`, `unknown`, `unsupported` هستند. حتی نتیجهٔ همبستهٔ `deleted-verified` از خانوادهٔ R11 در این مسیر پذیرفته نمی‌شود. خروجی child فقط ID/state است؛ tuple، credential و دادهٔ شخصی log نمی‌شوند.

`GET /devices/attendance-range-erases` و cancel/approve فقط همین خانواده را هدف می‌گیرند؛ استفاده از endpoint حذف همه برای retarget قصد بازه (یا برعکس) رد می‌شود. cancel proposed/pending پیش از dispatch نهایی است؛ cancel running فقط درخواست توقف است و late ACK را موفقیت نمی‌کند. UI متن امن، CSRF و version صریح دارد، در grant/revoke/پاسخ دیررس پاک می‌شود و raw tuple یا localStorage ندارد.

## unknown و بازیابی اختصاصی بدون بازپخش

unknown در همان جدول مشترک، تمام نوشتن‌های native بعدی را fence می‌کند؛ read-only برای اخذ مشاهدهٔ جدید پس از توقف child مجاز است. حذف همه، انتقال بیومتریک، enrollment یا صف دیگر نمی‌تواند از نام خانوادهٔ متفاوت برای دورزدن آن استفاده کند.

ابتدا همهٔ worker/childهای قبلی متوقف شوند، reset **غیرحذفی** مصوب برای پاک‌شدن فرمان معلق اجرا، capture دوباره متوقف و settling طی شود؛ سپس یک آرشیو کامل تازه ساخته شود. proposal بازیابی باید `RecoveryOfId` همان unknown اصلی، **عین همان خانواده و range/approval** و زمان/آرشیو پس از reset را داشته باشد. switch و دو حق بازیابی مستقل و امضای شخص دوم لازم است؛ عبارت تأیید `RELEASE-RANGE-FENCE-NOT-DELETE-SUCCESS` است. بازیابی در وب/SQL انجام می‌شود و هیچ فرمان حذف به child نمی‌فرستد؛ فقط یک رکورد جدید `released` می‌سازد و unknown اصلی تا ابد دست‌نخورده می‌ماند. این مشاهده موفقیت حذف قبلی را اثبات نمی‌کند؛ هر حذف بعدی به preview/proposal تازه نیاز دارد.

migration۵۵ سه ستون nullable به جدول موجود اضافه می‌کند، نه جدول موازی؛ تعداد جدول‌های domain۶۹ است. trigger SQL قصد/مرز/selected count/digest باقی‌مانده را immutable می‌کند، تبدیل خانواده و تغییر terminal/requeue/delete history را رد می‌کند؛ constraint نتیجهٔ اشتباه خانواده و lifecycle/cancel را می‌بندد. downgrade با وجود هر قصد بازه ممنوع است و تاریخچه برای rollback پاک نمی‌شود. مدل و snapshot با همان ستون‌ها به‌روز شده‌اند.

## پذیرش نرم‌افزار در برابر نصب واقعی

شاهد نهایی runها و شمار دقیق بررسی‌ها در [r12-attendance-range-erase-evidence.json](r12-attendance-range-erase-evidence.json) ثبت می‌شود؛ pending یا شکست CI، پذیرش نیست. آزمون‌های جدید شامل قرارداد مرز/دقت/تکرار ساعت، خود executor و production COM mapper با double، supervisor واقعی با child مصنوعی، SQL/صف/worker واقعی Windows و ورود/TLS/CSRF/default-off/revoke واقعی HTTPS و DOM هستند. LocalDB و child مصنوعی، اجرای SDK/COM روی دستگاه نیستند.

Linux و suite کامل تاریخی SQL۱۰۳۱ دوباره اجرا نشده‌اند. اثبات مرز شامل و دقت native روی مدل واقعی، کنترل capture همهٔ کانال‌ها، سیاست واقعی سازمان، Windows/SDK نصب‌شده و سناریوی قطع/over-deletion با دادهٔ مجاز در H01 باقی‌اند. تا احراز این‌ها، default-off حفظ می‌شود. تکمیل R12 نرم‌افزاری، مجوز حذف واقعی یا تکمیل بخش۳ نیست؛ بعدی R13 است.
