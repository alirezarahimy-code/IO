# R13 — برش اول هستهٔ حذف کاربر و اثرانگشت؛ ردیف هنوز باز است

## وضعیت و مرز صریح

این برش **هسته و mapper و ناظر child** را می‌سازد، نه قابلیت عملیاتی آمادهٔ استفاده. هنوز صف durable، migration، مجوز/allowlist نصب سرور و worker، preview/تأیید دو نفره، API/UI و بازیابی اختصاصی unknown به آن متصل نیستند. `IO.ZkBridge/Program.cs` discriminator حذف کاربر را dispatch نمی‌کند و هیچ worker یا endpoint وب این executor را فراخوانی نمی‌کند. متد supervisor فقط در fixture مصنوعی CI استفاده می‌شود؛ JSON فرم یا یک boolean جای مجوز عملیاتی نیست. اتصال destructive route پیش از تکمیل این حفاظت‌ها مجاز نیست.

**R13 بسته نشده و شمار۴ مورد R13–R16 + H01 ثابت است.** این برش گام اول اجرای ترتیبی R13 است؛ R14 شروع نشده است. Linux یا suite کامل تاریخی SQL۱۰۳۱ دوباره اجرا نمی‌شوند. پذیرش واقعی Windows/SDK و چنددستگاه در H01، انتهای کار است.

## مرجع و عدم تعمیم flag

مرجع فعال decompileشدهٔ `Personnel.ZKDevice.DeleteUsers` در خطوط۶۱۹..۶۷۰:

| قالب دقیق مصوب | فقط اثرانگشت‌های فرد | حذف کاربر |
|---|---|---|
| SSR با badge رشته‌ای | `SSR_DeleteEnrollDataExt(machine, person, 13)` | همان متد با۱۲ |
| non-TFT، عدد Int32 canonical | `DeleteEnrollData(machine, personInt, machine, 11)` | همان متد با۱۲ |

[شاهد دو TypeLib هش‌ثابت](r13-user-erase-typelib-evidence.json) و ابزار `audit-user-erase-typelib.py`، ABI چهار متد حذف/خواندن قالب را بررسی می‌کنند. کد۱۳ SSR را به legacy تعمیم نمی‌دهیم؛ flag از ورودی کاربر پذیرفته نمی‌شود، `IsTFTMachine` انتخاب خودکار ایجاد نمی‌کند و پس از false/exception هیچ fallback/retry وجود ندارد. `001` در SSR رشتهٔ مستقل از `1` است؛ در legacy، شناسهٔ غیرcanonical پیش از dispatch رد می‌شود. ترتیب/تنظیمات مرجع، اثبات اثر نصب‌شده نیستند؛ اینجا DisableDevice/EnableDevice/RefreshData خودکار اضافه نشده است.

## پروفایل محدود و مستقل نصب

قرارداد اولیه فقط پروفایل **attendance-user-and-ten-fingerprint-slots** را پوشش می‌دهد: کاربر عادی تردد، کارت/رمز/نام/وضعیت و ده اسلات اثرانگشت۰..۹. وجود یا اثر چهره، عکس، palm، رگ، اثرانگشت اضافه، access group/door، credential پنهان یا vendor store دیگر از این قرارداد نتیجه نمی‌شود. `FingerprintOnlyAttendanceProfile=true` باید متکی به شاهد واقعی همان مدل/firmware باشد؛ نبود شاهد به معنی unsupported و خاموش‌ماندن است، نه فرض اینکه دستگاه واقعاً این storeها را ندارد.

- دو mode بسته: `ordinary-user` و `all-fingerprints`؛ تک‌اسلات، چهره/عکس و حذف مدیران مجوز نمی‌گیرند.
- target حتماً `Privilege=0` و metadata دقیق `ZkUser Expected` دارد؛ هدف administrator حتی با approval معتبر رد می‌شود. presence از enumeration کامل است، نه false از getter فرد.
- `UserEraseApproval` هویت Serial/Product/Firmware، قالب ثابت، mode، مرجع profile، اثر دقیق native، مجوز قانونی، تصمیم صریح حذف **غیرقابل بازیابی**، کنترل انحصاری، reset غیرحذفی و کد دقیق خطای نبود اسلات را لازم می‌کند. `AllowDelete`, `AllowTransientProtectedCatalogue`, `FingerprintOnlyAttendanceProfile` همگی پیش‌فرض false هستند.
- intent حذف تمام اثرانگشت‌های **فعلی** کاربر دقیق است؛ ادعای حذف نسخهٔ template که پیش‌تر در SQL تصویب شده نداریم. هیچ template یا digest template در SQL/receipt وجود ندارد. کنترل انحصاری واقعی باید از پیش از اخذ metadata برای پیشنهاد تا پایان readback برقرار باشد؛ اگر امکان reuse badge، ثبت بیومتریک یا تغییر concurrent وجود دارد، نباید فعال شود.
- نگهداری/بازیابی خام بیومتریک اختراع نشده است: هیچ backup قالب/رمز یا مسیر restore خودکار نداریم. مرجع تصمیم غیرقابل‌بازیابی باید با سیاست نگهداری/حذف واقعی سازمان سازگار باشد. این references اصالت فیزیکی یا قانونی را خودکار اثبات نمی‌کنند.

این محدودسازی جای تعیین‌تکلیف سایر مدل‌ها و storeها در ادامهٔ R13 را نمی‌گیرد؛ برای بستن ردیف باید دامنهٔ دقیق پشتیبانی/عدم پشتیبانی مستند و مسیر عملیاتی کامل پذیرفته شود.

## الگوریتم هسته؛ تمام دادهٔ محافظت‌شده در child

۱. هویت دقیق قبل و بعد هر scan بررسی می‌شود. شمار کاربر native کد۲ قبل/پس از enumeration و پس از خواندن قالب‌ها باید با تعداد کامل برابر باشد. شمار ناموجود، enumeration ناقص، شناسهٔ تکراری یا catalogue بزرگ‌تر از سقف، قبل از حذف رد می‌شوند؛ scan تقسیم یا truncate نمی‌شود.
۲. همهٔ کاربران، از جمله مدیران غیرهدف، فقط در حافظهٔ child خوانده می‌شوند: نام/کارت/privilege/enabled، رمز و ده اسلات اثرانگشت با داده/طول/flag دقیق. این وسعت صرفاً برای اثبات حفظ catalogue خارج هدف است؛ حق تغییر مدیر یا templateهای دیگر ایجاد نمی‌کند. تمام شناسه‌های catalogue در این پروفایل باید عددی باشند؛ SSR بدون Int32 coercion حفظ می‌شود.
۳. false از getter اسلات فقط با خروجی تمیز و کد نبود **دقیقاً مصوب نصب** نبود محسوب می‌شود؛ در غیر این صورت کل scan شکست می‌خورد. کد خطای stale/نامعتبر نباید در نصب به‌عنوان absence contract تصویب شود. اگر `RequiresReadAllTemplate=true` باشد، refresh cache باید موفق باشد. پشتیبانی ABI، خودبه‌خود پشتیبانی این absence contract نیست.
۴. دو catalogue کامل و مستقل قبل از dispatch باید دقیقاً برابر باشند؛ تغییر رمز/کارت/نام/وضعیت، جایگزینی fingerprint با همان count، یا تغییر کاربر دیگر رد می‌شود. شمار ثابت یا دو scan **CAS firmware یا کنترل انحصاری فیزیکی نیستند**.
۵. فقط یک متد/flag ثابت مرجع اجرا می‌شود. کاربر قبلاً غایب، `already-absent`؛ فقط اثرانگشت‌ها از قبل خالی، `already-empty` بدون dispatch است. این‌ها اثبات موفقیت حذف قبلی نیستند.
۶. بعد از ACK، catalogue کامل دوباره خوانده می‌شود. در حذف fingerprints، خود کاربر با همان رمز/کارت/نام/وضعیت باید باقی و هر ده اسلات خالی باشند. در حذف user، کاربر باید از enumeration کامل حذف شده باشد و probe مجدد هر ده اسلات هدف نیز absence مصوب نشان دهد تا orphan قابل خواندن پنهان نماند. تمام دادهٔ محافظت‌شده و هر ده اسلات **همهٔ کاربران دیگر، از جمله مدیران** باید دقیقاً برابر قبل باقی بماند.
۷. حذف اضافه، باقی‌ماندن یک template، تغییر رمز هدف در حالت fingerprints، خطای readback/هویت، false، exception، خروجی گم‌شده/ناهمبسته، حالت موفق خانوادهٔ دیگر یا deadline پس از dispatch، `unknown` است. هیچ retry/fallback/restore خودکار نداریم. دادهٔ خارج catalogue گزارش‌شده و secure media erasure اثبات نمی‌شود.

حدود پیش‌فرض:۱۰۰ کاربر (سقف hard۱۰۰۰)،۶۵۵۳۶ نویسه/طول native هر template،۱Mi نویسهٔ تجمعی (سقف hard۸Mi)؛ همه داخل child. مهلت supervisor۱..۶۰ثانیه است؛ سه scan کامل × تعداد کاربران × ده getter برای دستگاه بزرگ ممکن است در۶۰ثانیه جا نشود؛ نصب باید دامنهٔ کوچک قابل‌اثبات را انتخاب کند، نه اینکه timeout یا سقف را حدسی افزایش دهد.

`UserEraseCatalogue`, `UserEraseEntry`, `UserEraseFingerprint` property سریال‌شونده ندارند؛ raw biometric/password در JSON، log، hash یا receipt قرار نمی‌گیرد. `ToString`ها redacted هستند. IPC ورودی فقط credential خصوصی و metadata هدف/approval دارد؛ خروجی فقط operation ID/state است. عمر reference محدود و خروج child لازم است، اما managed string و local-admin دسترسی‌ناپذیری یا secure memory wipe تضمین نمی‌شوند.

## ادامهٔ لازم برای R13

- آرشیو metadata کامل و تازه، preview/hash نسخه‌دار و دو انسان مستقل با scope و شواهد کنترل واقعی؛ بدون raw template یا رمز در DB/وب.
- مجوزهای جداگانهٔ دو mode/دو قالب و allowlist دقیق سرور و endpoint، default-off؛ supervisor/bridge فقط پشت همین مسیر وصل شوند.
- صف single-dispatch و تاریخچهٔ SQL immutable، fence مشترک با attendance erase، biometric، enrollment و صف‌های native دیگر؛ expired running فقط unknown و هرگز replay.
- بازیابی **اختصاصی** با reset غیرحذفی/توقف child و observation فقط‌خواندنی تازه، با امضای مستقل و حفظ unknown اصلی. از بازیابی R11/R12 برای رفع این fence استفاده نشود.
- API/UI با CSRF/نسخه/پاک‌کردن پاسخ دیررس و HTTPS/SQL/worker/child در Windows؛ سپس ثبت پذیرش کامل نرم‌افزاری و فقط آن زمان بررسی کاهش شمارنده.
- تعیین‌تکلیف روشن مدل‌های دارای face/photo/door و رفتار broad delete؛ این برش مجوز حذف یا ادعای حفظ storeهای اثبات‌نشده نمی‌دهد.

شاهد CI این برش پس از موفقیت واقعی در `r13-user-erase-core-evidence.json` ثبت می‌شود؛ بیلد محلی به‌تنهایی پذیرش نیست. هیچ route تولیدی، اجرای COM/SDK یا حذف واقعی در این برش انجام نشده است.

## نتیجهٔ CI همین برش

[run43708241242/job132793947609](https://github.com/alirezarahimy-code/IO/actions/runs/43708241242) روی `685485067012440dc93e19104dafb1e5e7f2dae5` با label واقعی `windows-2022` موفق است: **۴۸ بررسی مصنوعی جدید R13** از خود executor و mapper تولیدی، تطبیق دادهٔ محافظت‌شده، مرز دو قالب/دو mode، خطا/over-deletion/orphan، عدم افشای raw و supervisor با child واقعی مصنوعی.۱۷۵ بررسی SQL/HTTPS/صف/worker قبلی،۳۴ قرارداد بازه،۲۳ حذف همه،۶۳ بیومتریک/EFS و۴۷ gateway/enrollment نیز regression سبز ماندند؛ هیچ تست عملیاتی SQL/worker/HTTPS جدید R13 اضافه نشده است.

[light43708241251](https://github.com/alirezarahimy-code/IO/actions/runs/43708241251) نیز موفق است؛ در این scope، DOM/Chromium به‌درستی skip شده‌اند چون UI تغییر نکرده است. بایت‌های dispatcher واقعی bridge، تمام worker/web/infrastructure با مبنای۲۷۱۲۷۹۹ برابرند؛ route جدید native اضافه نشده است. [شاهد ماشین‌خوان](r13-user-erase-core-evidence.json). پذیرش فقط هسته است؛ **R13 هنوز باز و شمار۴ + H01 ثابت**.
