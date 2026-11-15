# R13 و R14 — مسیر عملیاتی نرم‌افزاری حذف مصوب و بازیابی مستقل

## دامنهٔ دقیق؛ نه وعدهٔ پشتیبانی همهٔ مدل‌ها

R13 ابتدا و R14 پس از آن در همان مسیر پذیرش Windows اجرا می‌شوند. R13 دو خانوادهٔ مستقل `users` و `fingerprints` دارد؛ R14 خانوادهٔ `administrators`. اشتراک زیرساخت صف/قفل، مجوز مشترک یا مسیر native عمومی ایجاد نمی‌کند. [برش اول هسته](r13-user-erase-core.fa.md) و شاهد قدیمی آن تاریخچه‌اند؛ dispatcher/worker/API/UI و بازیابی که آن زمان ناتمام بودند، در این برش متصل شده‌اند. نتیجهٔ نهایی SHA/job و شمار بررسی‌ها در [شاهد پذیرش](r13-r14-protected-erases-evidence.json) ثبت می‌شود؛ pending به معنی پذیرش نیست.

| خانواده | تنها اثر مجاز | متد/flag ثابت native |
|---|---|---|
| users، R13 | حذف یک کاربر **عادی** دقیق و دادهٔ ده اسلات fingerprint او؛ بدون تغییر سایر کاربران | SSR: `SSR_DeleteEnrollDataExt(machine,person,12)`؛ legacy: `DeleteEnrollData(machine,personInt,machine,12)` |
| fingerprints، R13 | حذف تمام ده اسلات اثرانگشت همان فرد و حفظ خود کاربر/رمز/کارت/نام/وضعیت | SSR کد۱۳ همان متد؛ legacy کد۱۱؛ کدها معادل فرض نمی‌شوند |
| administrators، R14 | `ClearAdministrators(machine)`، فقط تنزل privilege تمام افراد به۰ با حفظ تمام کاربران/رمزها/کارت‌ها/templateها | هیچ SetUserInfo عمومی، حذف فرد، حذف template یا ClearData اجرا نمی‌شود |

اثر واقعی این متدها به مدل/firmware وابسته است؛ نام متد، flag مرجع و [دو TypeLib هش‌ثابت](r13-r14-protected-erase-typelib-evidence.json) اثبات رفتار نصب نیستند. اگر ClearAdministrators در مدل هدف افراد/رمز/قالب را حذف کند یا معنای دیگری داشته باشد، آن مدل برای این قرارداد R14 **unsupported و خاموش** است؛ نباید با تغییر نام نتیجه، آن را موفق نامید.

### تعیین‌تکلیف پشتیبانی profile و انواع template

این نسخه فقط profile مصوب **کاربر تردد و ده اسلات fingerprint۰..۹، فاقد سایر storeها** را اجرا می‌کند. شناسه‌های catalogue باید عددی باشند؛ در SSR، `001` و `1` متمایزند و هیچ Int32 coercion ندارند؛ legacy فقط nonnegative Int32 canonical است. هدف دارای privilege غیرصفر در R13 رد می‌شود؛ مجوز R14 جایگزین این منع نیست.

مدل دارای face/photo/palm/vein/اسلات اضافه، access group/door یا credential پنهانِ خارج catalogue، برای این قرارداد **پشتیبانی نمی‌شود**. حذف تک‌اسلات، حذف مستقیم عکس/چهره و تضمین حفظ این storeها پیاده‌سازی یا ادعا نشده؛ هیچ fallback یا فراخوانی حدسی برای آن‌ها وجود ندارد. مسیر انتقال face/photo در R10، به‌تنهایی مجوز یا قرارداد حذف آن‌ها نیست. اگر نصب این storeها را دارد، `FingerprintOnlyAttendanceProfile` نباید true شود و این قابلیت خاموش می‌ماند. این disposition صریح دامنهٔ نسخه است، نه شاهد اینکه مدل ناشناخته واقعاً فاقد آن‌هاست. افزودن پشتیبانی مدل/store تازه نیازمند قرارداد و پذیرش نرم‌افزاری جدید است؛ نتیجهٔ این برش مجوز حذف روی همهٔ مدل‌ها نیست.

## پیش‌شرط خارج از نرم‌افزار

کنترل انحصاری واقعی تمام کانال‌ها باید **پیش از دریافت metadata آرشیوی** برقرار و تا پایان readback حفظ شود: برنامه‌های دیگر، ثبت/ویرایش کاربر، capture/enrollment بیومتریک، reuse badge، کنسول فیزیکی و دستگاه‌های متصل. دو scan ثابت و mutex، CAS firmware یا اثبات توقف فیزیکی نیستند. هیچ DisableDevice/EnableDevice/RefreshData خودکار به‌عنوان جایگزین کنترل نصب اضافه نشده است.

قرارداد شامل مجوز قانونی، تصمیم نگهداری/حذف **غیرقابل‌بازیابی** و reset غیرحذفی همان مدل است. سامانه اصالت شاهد فیزیکی، استقلال واقعی دو انسان پشت حساب‌ها یا مجوز قانونی سازمان را خودکار اثبات نمی‌کند. هیچ raw template/password backup یا restore خودکار، secure media erase، حفاظت در برابر local-admin یا secure managed-memory wipe تضمین نشده است. raw فقط در child کوتاه‌عمر است؛ محیط اجرا و حساب سرویس باید مستقل محافظت شوند.

## پیکربندی مستقل و پیش‌فرض خاموش

برای هر خانوادهٔ ثابت `F = users | fingerprints | administrators`، جداگانه:

- `IO:ProtectedErase:F:Enabled=false`؛
- `IO:ProtectedErase:F:EnableRecovery=false`؛
- `IO:ProtectedErase:F:Approvals`: دقیقاً یک `ProtectedEraseApproval` برابر با قصد؛
- NativeEndpoint دستگاه در worker: `ProtectedEraseApprovals` شامل عین همان قرارداد، `AllowUserRead=true`، `UserReadFormat` مطابق inventory، IP/port/serial دقیق؛ مسیر مطلق dotnet/bridge و key-ring مشترک قابل‌اعتماد طبق نصب قبلی.

`ProtectedEraseApproval` شامل `Family`, `Inventory` از نوع `UserEraseApproval` برش اول و برای administrators، `AdministratorEffectReference` و `AllowClearAdministrators=true` است. Inventory شامل هویت Serial/Product/Firmware، قالب، اثر نصب، تصمیم حذف غیرقابل‌بازیابی، مجوز/کنترل/reset، کد absence دقیق و flagهای صریح profile/خواندن protected/حذف است. در خانوادهٔ administrators، `Inventory.Mode=ordinary-user` فقط انتخاب قرارداد catalogue است؛ مجوز حذف فرد ایجاد نمی‌کند. برای fingerprints باید `Mode=all-fingerprints` باشد. در دو خانوادهٔ R13، AdministratorEffectReference باید null و AllowClearAdministrators باید false باشد. واردکردن JSON در فرم نه تنظیم سرور را تغییر می‌دهد و نه قابلیت را فعال می‌کند.

هر خانواده شش حق مستقل زیر پیشوند `devices.protected.erase.F.` دارد: `read`, `propose`, `approve`, `execute`, `recover.propose`, `recover.approve`. هیچ template نقش پیش‌فرضی آن‌ها را ندارد. هر دو انسان به `devices.users.read` و scope همان دستگاه نیاز دارند؛ استخراج و اجرای دریافت کاربران نیز حقوق جداگانهٔ قبلی‌اند. سرویس به execute **همان خانواده** و دامنهٔ دستگاه نیاز دارد؛ حق سرویس، لغو grant یا scope انسان‌ها را دور نمی‌زند. R11/R12، انتقال بیومتریک و enrollment هیچ‌کدام حق رفع این fence یا اجرای این حذف را نمی‌دهند.

## آرشیو، proposal و اجرای یک‌بار

۱. polling و همهٔ کارهای native رقیب متوقف شوند. پس از کنترل واقعی انحصاری و حداقل۱۰ثانیه settling، دریافت کامل کاربران از `/workspace/device-users` بسازید؛ snapshot باید `succeeded`، کامل/بدون duplicate، همان نسخهٔ دستگاه/source/company و قالب، تازه (حداکثر۱۵دقیقه) باشد. زمان شروع کنترل حداکثر۳۰دقیقه قبل و UTC است. تمام metadata کاربران، شامل مدیران غیرهدف، حفظ می‌شود؛ template و رمز در archive نیستند.
۲. صفحهٔ `/workspace/device-protected-erases` با انتخاب صریح خانواده، UUIDها/rowversionها، شاهد و قرارداد کار می‌کند. در users/fingerprints شناسهٔ فرد دقیق لازم است؛ در administrators فرد **باید خالی** باشد. Expected metadata از archive سمت سرور استخراج می‌شود، نه از فرم. tuple/رمز/template را در فرم وارد نکنید.
۳. `POST /devices/protected-erases/F/preview` بدون SDK/صف اجرایی، توالی کامل metadata را می‌خواند و digest مرتب‌شدهٔ **فقط ZkUser metadata** می‌سازد. این digest hash بیومتریک یا رمز نیست. نسخه/کل قصد/شمار/digest/Expected هدف به previewHash متصل‌اند. تغییر خانواده، subject، policy، نسخه یا archive، preview را باطل می‌کند.
۴. `POST /devices/protected-erases/F` با hash و عبارت `DELETE-DEVICE-F-IRREVERSIBLY` (F بزرگ) فقط proposal durable ایجاد می‌کند. UUID ثابت retry همان intent است؛ قصد متفاوت با همان UUID رد می‌شود. proposed از همین لحظه endpoint را برای صف‌های native دیگر رزرو می‌کند.
۵. شخص مستقل با نسخهٔ تازه، metadata و دامنهٔ اثر و شواهد واقعی را بررسی و در `/{id}/approve` امضا می‌کند. self-signature حتی با داشتن هر دو حق ممنوع است. proposal و authority اجرا عمر محدود۲دقیقه دارند؛ grant/scope/policy/archive در claim، credential، هر۵۰۰ms و قبل از ثبت نتیجه دوباره بررسی می‌شوند.
۶. Windows worker فقط خانواده‌های enabled را claim و endpoint approval دقیق را کنترل می‌کند. یک child STA، هویت و دو catalogue **کامل و transient** از کاربران/رمز/کارت/ده اسلات همهٔ کاربران می‌خواند؛ metadata هر دو scan با archive برابر و کل protected catalogue دو بار دقیقاً ثابت باشد. false از getter فقط با خروجی تمیز و کد absence دقیق مصوب نصب، غیبت است؛ خطای دیگر، شمار ناقص، card/password ناموجود، duplicate یا سقف رد می‌شوند.
۷. فقط یک فرمان ثابت همان خانواده dispatch می‌شود. برای R13، تمام protected data خارج هدف باید عین قبل باقی بماند؛ fingerprint-only خود فرد را نیز با رمز/کارت/نام/وضعیت عیناً حفظ می‌کند. user-delete علاوه بر غیبت از enumeration کامل، ده probe بعدی برای orphan fingerprints را لازم می‌کند. برای R14، تمام رکوردها و protected material باید عین قبل باشند و **تنها** privilege همه به۰ برسد. ACK یا count برابر کافی نیست.
۸. false/exception/deadline/خروجی نامعتبر/خواندن ناقص یا تغییر اضافه بعد از dispatch، unknown است؛ retry/fallback/requeue/restore خودکار نداریم. لغو proposed/pending قبل از اجرا نهایی است؛ لغو running فقط درخواست توقف و late success را unknown می‌کند. expired running هرگز دوباره dispatch نمی‌شود. خروجی child فقط ID/state است؛ raw در stdout/stderr/SQL/receipt/hash نیست.

حدود profile: پیش‌فرض۱۰۰ کاربر، hard۱۰۰۰؛ هر template حداکثر۶۵۵۳۶ نویسه و طول native؛ مجموع پیش‌فرض۱Mi نویسه، hard۸Mi؛ deadline حداکثر۶۰ثانیه. سه scan کامل برای دستگاه بزرگ ممکن است در۶۰ثانیه جا نشود؛ آن دامنه باید unsupported بماند، نه با تقسیم حذف یا افزایش حدسی timeout اجرا شود. آرشیو metadata و اشخاص سازمان/کارمندان/تردد SQL با این فرمان‌ها حذف نمی‌شوند.

## unknown و بازیابی اختصاصی با observation واقعی

unknown اصلی، نوشتن‌های بعدی native از همهٔ خانواده‌ها را می‌بندد؛ دریافت metadata خواندنی برای بازیابی پس از توقف child مجاز است. worker/child قبلی متوقف، reset غیرحذفی مصوب برای پاک‌شدن فرمان معلق انجام، کنترل انحصاری دوباره برقرار و settling طی شود؛ سپس archive تازه بعد از reset بسازید.

پیشنهاد بازیابی همان خانواده، subject، approval، نسخهٔ نصب و `RecoveryOfId` unknown اصلی را می‌خواهد. switch و حق recover.propose جدا لازم است. این پیشنهاد فقط **inspection خواندنی** را با همان worker/credential/pin و catalogue محافظت‌شده صف می‌کند؛ native delete/ClearAdministrators در branch InspectOnly قابل دسترسی نیست. child باید metadata جاری را با archive تازه تطبیق دهد تا `observed` ثبت شود. bare snapshot، failed inspection یا ACK حذف قبلی جای observation نیست.

فقط پس از observed، شخص مستقل با `recover.approve`، حق خواندن archive، scope و نسخهٔ تازه، نتیجه و شاهد reset را بررسی می‌کند. عبارت `RELEASE-F-FENCE-NOT-DELETE-SUCCESS` است (F بزرگ). رکورد جدید released می‌شود و unknown اصلی ثابت می‌ماند. مشاهدهٔ فعلی، موفقیت حذف قبلی را اثبات نمی‌کند. هر حذف بعدی به archive/preview/proposal/دو امضای تازه نیاز دارد. endpoint یا بازیابی خانوادهٔ دیگر نمی‌تواند subject/family را عوض یا unknown را پاک کند.

اگر بازبینی observation در مهلت۲دقیقه انجام نشد یا مشاهده کنار گذاشته شد، دارندهٔ `recover.propose` با نسخهٔ جاری از همان `/{id}/cancel` استفاده می‌کند: **observed→inspection-failed**، زمان و شواهد observation ثابت، unknown اصلی همچنان مسدود. سپس archive/پیشنهاد inspection تازه بسازید. این عمل release یا replay حذف نیست؛ DB اجازهٔ امضای دوبارهٔ مشاهدهٔ کنارگذاشته‌شده را نمی‌دهد. مسیر UI دکمهٔ مخصوص همین وضعیت دارد؛ بررسی SQL/worker چرخهٔ مشاهده، کنارگذاشتن، منع نوشتن و مشاهدهٔ جدید تا امضای مستقل را طی می‌کند.

جدول مشترک `DeviceProtectedErases` در migration۵۶ با rowversion، intent immutable، FK archive و parent، index یکتای active/released و trigger ضد تغییر terminal/replay/delete ساخته شد؛ تعداد جدول‌های domain۷۰ است. خانواده‌ها در DB و قرارداد نتیجه هم تفکیک شده‌اند. downgrade با وجود تاریخچه ممنوع است. قفل‌ها با attendance erase، enrollment، biometric، upload، clock/settings/network و polling مشترک‌اند؛ mutex نرم‌افزار جای کنترل فیزیکی نیست.

## پوشش پذیرش و حدود واقعی

آزمون Windows به ترتیب سه خانواده users، fingerprints و administrators را از SQL/permission/preview/تأیید، worker واقعی و child مصنوعی، cancellation→unknown، fence، reset/archive جدید، observation واقعی read-only و امضای مستقل تا released طی می‌کند. HTTPS واقعی با login/TLS/CSRF/default-off/live-revoke و نگاشت embedded HTML/JS، DOM جدایی خانواده/پاک‌شدن preview و پاسخ دیررس نیز پوشش جدا دارند. TypeLib و mapper تولیدی با double آزموده می‌شوند؛ SDK/COM دستگاه اجرا نمی‌شود.

شمار نهایی و SHA/job موفق در `r13-r14-protected-erases-evidence.json` ثبت می‌شود. Linux، suite کامل تاریخی SQL۱۰۳۱ و suite کامل rollback دوباره اجرا نشده‌اند. مدل واقعی/absence/اثر ClearAdministrators/کنترل همهٔ کانال‌ها/مجوز واقعی و نصب Windows/SDK چنددستگاه در H01، انتهای کار باقی‌اند. نرم‌افزار پذیرفته‌شده مجوز حذف واقعی نیست؛ تمام switchهای واقعی خاموش بمانند تا این پیش‌شرط‌ها احراز شوند.
