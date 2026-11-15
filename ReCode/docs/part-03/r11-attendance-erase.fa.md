# R11 — حذف همهٔ تردد دستگاه با آرشیو بادوام و بازبینی مستقل

## مرز این ردیف

این مسیر فقط `ClearData(machine, 1)` مرجع `Personnel.ZKDevice.DeleteAllRecords` است. `ClearData(machine, 5)` مربوط به پاکسازی همهٔ داده‌ها در R15 است؛ وجود آن اثبات factory reset نیست. حذف بازه، کاربر/قالب و مدیران در R12–R14 مستقل‌اند و از حق حذف تردد مجوز نمی‌گیرند. شواهد ABI دو DLL در [ممیزی hash-pinned](r11-attendance-erase-typelib-evidence.json) ثبت شده‌اند؛ ممیزی TypeLib اجرای COM یا اثبات رفتار firmware نصب نیست.

حذف در این مسیر فقط حافظهٔ تردد **دستگاه** را هدف می‌گیرد؛ هیچ ردیف raw attendance، tuple دریافت‌شده، quarantine، receipt یا audit در SQL حذف یا به‌صورت خودکار ingest نمی‌شود. راه بازگرداندن ترددها به دستگاه اضافه نشده است. تردد ناشناس، تاریخ/کد تفسیرنشده و ردیف خارج بازهٔ ingestion نیز باید قبل از حذف به‌صورت tuple کامل native در آرشیو SQL باقی بمانند. «فقط رخدادهای قابل محاسبه» آرشیو کافی نیست.

## شرط غیرقابل جایگزین: capture واقعاً متوقف باشد

`ReadAllGLogData`، count قبل/بعد، دو بار مشاهدهٔ یکسان، mutex برنامه و نداشتن worker رقیب **مانع ثبت تردد تازه در دستگاه نیستند**. بین آخرین خواندن و فرمان clear، firmware CAS وجود ندارد. قبل از اخذ آرشیو، capture جدید باید با رویهٔ فیزیکی غیرحذفی مصوب همان مدل متوقف شود و تا پایان read-back و تعیین وضعیت ادامه یابد؛ کنترل انحصاری شامل سایر نرم‌افزارها، کانال‌ها و کاربرانِ فیزیکی دستگاه است. دو شخص مستقل باید شاهد واقعی این وضعیت و حفظ آرشیو را بازبینی کنند.

هیچ فراخوانی خودکار `EnableDevice(false)`، بستن درب، تغییر AccGroup یا خاموش‌کردن تردد به‌عنوان میان‌بُر اضافه نشده است؛ اثر این APIها روی درب/حافظه/اتصالات بدون قرارداد نصب روشن نیست. اگر توقف واقعی capture در نصب قابل اثبات نیست، **این قابلیت باید خاموش بماند**. فرم یا مثال مصنوعی مدرک توقف فیزیکی نیست. نرم‌افزار صحت مراجع و جدایی دو حساب را بررسی می‌کند، نه اصالت فیزیکی شاهد یا استقلال واقعی اشخاص پشت دو حساب.

## فعال‌سازی صریح، پیش‌فرض خاموش

تنظیمات قابل‌اعتماد سرور/worker:

- `IO:AttendanceErase:EnableDeleteAll=false` به‌صورت پیش‌فرض؛
- `IO:AttendanceErase:EnableRecovery=false` به‌صورت مستقل؛
- `IO:AttendanceErase:Approvals`: قرارداد دقیق و یکتای `AttendanceEraseApproval`؛
- `AttendanceEraseApprovals` روی NativeEndpoint همان دستگاه در worker، عین قرارداد سرور؛ `AllowAttendanceRead=true`، `AttendanceReadFormat` یکسان، IP/port و serial ثابت؛
- executable/bridge مطلق و قابل‌اعتماد، حساب سرویس و DP key-ring مشترک طبق نصب قبلی.

قرارداد شامل `Device` با Serial/Product/Firmware دقیق، `ReadFormat` صریح SSR یا legacy-int32-minute-v1، `AuthorizationReference`, `RetentionReference`, `CaptureQuiescenceReference`, `ResetProcedureReference` و سه flag آگاهانهٔ `CaptureStoppedDuringMaintenance=true`, `ResetClearsPendingWrites=true`, `AllowErase=true` است. استفادهٔ مجدد از نوع identity زیست‌سنجی فقط برای سه رشتهٔ هویت است و هیچ مجوز زیست‌سنجی ایجاد نمی‌کند.

حدود: `MaximumRecords` برابر۱ تا۱۰۰۰۰۰؛ `MaximumArchiveAgeSeconds` برابر۶۰ تا۱۸۰۰ (پیش‌فرض۹۰۰)؛ `MinimumSettlingSeconds` برابر۱۰ تا۳۰۰ (پیش‌فرض۱۰). پروفایل واقعی، کدهای مدل و رویهٔ توقف/reset باید در H01 روی نصب هدف اثبات شوند. native deadline حداکثر۶۰ ثانیه است؛ حجم بزرگی که دو scan و clear/readback آن در مهلت نمی‌گنجد نباید با حدس timeout یا تقسیم حذف اجرا شود.

هیچ نقش پیش‌فرضی این حقوق را دریافت نمی‌کند:

- `devices.attendance.erase.read`: metadata صف، نه tupleها یا حق حذف؛
- `devices.attendance.erase.propose` به همراه `devices.attendance.read`: پیشنهاد حذف با آرشیو قابل بررسی؛
- `devices.attendance.erase.approve` به همراه حق خواندن آرشیو: شخص مستقل برای تأیید؛
- `devices.attendance.erase.execute`: فقط سرویس با scope همان دستگاه؛
- `devices.attendance.erase.recover.propose` و `devices.attendance.erase.recover.approve`: جدا برای بازیابی؛ هر شخص به حق خواندن آرشیو و دامنهٔ دستگاه نیاز دارد.

حق دریافت SDK همچنان `devices.attendance.extract` و حق اجرای دریافت `devices.attendance.execute` است. حذف مجوز استخراج یا commit تردد را به‌طور ضمنی نمی‌دهد. grant و scope هر دو انسان و سرویس در مسیر اجرا بازبینی می‌شوند.

## جریان حذف

۱. polling و کارهای native دیگر متوقف شوند. capture طبق رویهٔ مصوب واقعاً متوقف، settling سپری، سپس دریافت **کامل native** از مسیر موجود `/workspace/device-attendance` ساخته شود. اگر بازهٔ ingestion کوچک انتخاب شده، باز هم کل tupleهای SDK در `DeviceAttendanceRows` حفظ می‌شوند؛ digest حذف از همهٔ آن‌هاست، نه فقط ردیف‌های قابل ingest. دریافت باید `succeeded`، همان نصب/منبع/site/company/zone/نسخه و قالب، تازه و در سقف مصوب باشد. tupleهای آرشیو در SQL تغییرناپذیرند.
۲. صفحهٔ `/workspace/device-attendance-erases` فقط UUIDها، نسخهٔ دستگاه و آرشیو، زمان توقف capture به UTC، مرجع شاهد و approval مصوب را می‌گیرد؛ مسیر SDK یا data flag از کاربر پذیرفته نمی‌شود.
۳. `POST /devices/attendance-erases/preview` هیچ SDK/clear اجرا نمی‌کند؛ آرشیو را با توالی کامل و bounded می‌خواند و SHA256 مرتب‌شدهٔ tupleهای canonical را با حفظ **تعداد تکرارها** تعهد می‌کند. ترتیب دریافت اهمیت ندارد، ولی تبدیل `001` به `1`، کم‌شدن duplicate یا جایگزینی با همان count mismatch است. این تعهد شناسهٔ رخداد فیزیکی نیست.
۴. `POST /devices/attendance-erases`: intent عین preview و hash، با `DELETE-ALL-DEVICE-ATTENDANCE-AFTER-DURABLE-ARCHIVE`. پیشنهاد durable و audit اتمیک می‌شوند؛ UUID ثابت فقط retry همان پیشنهاد است، نه تکرار فرمان. از حالت proposed، endpoint رزرو می‌شود. تغییر هر ورودی preview را باطل می‌کند.
۵. شخص مستقل آرشیو کامل و شاهد تداوم توقف capture را بررسی و با نسخهٔ تازه در `POST /devices/attendance-erases/{id}/approve` امضا می‌کند. self-signature حتی با داشتن هر دو permission رد می‌شود. پیشنهاد۲ دقیقه اعتبار دارد و پس از امضا نیز مجوز اجرا حداکثر۲ دقیقه معتبر است. آرشیو و نصب نباید میان proposal و اجرا تغییر کنند؛ review/commit آرشیو که rowversion را عوض کند نیز باعث رد محافظه‌کارانه می‌شود.
۶. worker Windows حق/دامنهٔ پیشنهاددهنده و بازبین، وضعیت نصب، policy و آرشیو، pin endpoint، credential و مالکیت claim را دوباره بررسی می‌کند. حین اجرا هر۵۰۰ms و پیش از commit، authority بازبینی می‌شود. lease منقضیِ running فقط unknown می‌شود و هرگز دوباره dispatch نمی‌شود.
۷. یک child STA دو بار کل log را با count قبل/بعد و هویت کامل می‌خواند؛ multiset هر دو بار باید با تعهد آرشیو برابر باشد. mismatch/خواندن ناقص/قالب دیگر/هویت دیگر قبل از clear رد می‌شود. دستگاه خالی با آرشیو خالی، `already-empty` و بدون clear است.
۸. mapper تولیدی فقط `ClearData(machine,1)` را یک‌بار فراخوانی می‌کند. پس از true، خواندن **کامل و خالی** با count و هویت درست لازم است تا `deleted-verified` ثبت شود. false، exception، deadline، از دست‌رفتن خروجی یا readback ناموفق بعد از dispatch برابر `unknown` است. این وضعیت شکستِ اثبات‌شده یا مجوز تکرار نیست.

لغو proposed/pending پیش از اجرا نهایی است؛ لغو running فقط درخواست توقف و ممکن است unknown باشد. نتیجهٔ child فقط ID/state محدود است؛ tuple یا credential در خروجی و log قرار نمی‌گیرد. صفحه با CSRF، نسخهٔ صریح، متن امن و پاک‌سازی داده در revoke/پاسخ دیررس کار می‌کند؛ state مرورگر در localStorage نگهداری نمی‌شود.

## بازیابی unknown، بدون بازپخش

original unknown برای همیشه تغییرناپذیر می‌ماند و نوشتن‌های native دیگر را مسدود می‌کند. دریافت خواندنی عادی برای بررسی/آرشیو تازه همچنان مجاز است. برای رفع fence:

۱. worker/child قبلی واقعاً متوقف شوند؛ reset غیرحذفیِ مصوب مدل باید فرمان clear معلق را خنثی کند. capture همچنان متوقف بماند. `QuiescedAtUtc` این بار زمان reset/توقف تازه است و باید پس از پایان original unknown باشد.
۲. پس از settling، یک دریافت کامل بادوام **تازه** بسازید. دادهٔ باقی‌مانده، چه صفر و چه غیرصفر، حفظ و بررسی شود. count/digest فعلی نه موفقیت و نه شکست عملیات اصلی را ثابت می‌کنند؛ unknown می‌ماند.
۳. همان preview/proposal با `RecoveryOfId` برابر UUID اصلی و confirmation مستقل `RELEASE-ATTENDANCE-FENCE-NOT-DELETE-SUCCESS` ثبت شود. policy، نسخه و نصب اصلی باید همان باشند؛ فعال‌سازی مستقل recovery لازم است.
۴. شخص مستقل با حق recovery.approve، نسخهٔ تازه، آرشیو و شواهد reset/توقف را بررسی می‌کند. ردیف جداگانهٔ recovery به `released` می‌رود و فقط fence همان unknown رفع می‌شود. **هیچ فرمان native، clear دوباره، setter یا تغییر نتیجهٔ اصلی در این مسیر وجود ندارد.** بازیابی enrollment یا biometric مجوز این کار نیست.
۵. هر حذف بعدی intent، آرشیو، preview و دو تأیید تازه می‌خواهد. unknown قبلی requeue، حذف یا success نمی‌شود. اگر نصب عوض شده، آرشیو ناقص است، capture متوقف نیست یا reset معلق را پاک نمی‌کند، مسیر fail-closed است و نباید با SQL دستی دور زده شود.

## ذخیره، مهاجرت و حدود پذیرش

`20261114170000_AttendanceErase` جدول metadata-only `DeviceAttendanceErases` را با FK آرشیو و lineage بازیابی می‌افزاید؛ مجموع۵۴ migration و۶۹ جدول دامنه است. triggerها قصد، نسخه/تعهد آرشیو، ترتیب دو امضا، اصل unknown و نتیجهٔ نهایی را محافظت می‌کنند؛ unique active index رزرو یک endpoint و unique released recovery رفع یک‌بارهٔ fence را تقویت می‌کنند. downgrade با هر سابقهٔ حذف/بازیابی رد می‌شود. startup خودکار migration جدید اضافه نشده است.

آزمون‌های این ردیف با tupleها، هویت‌ها و child صریحاً مصنوعی اجرا می‌شوند؛ mapper fixed flag واقعی با double و supervisor واقعی فرایند، SQL/صف/worker، UI و مرز HTTP جدا بررسی می‌شوند. این‌ها اجرای COM/دستگاه، توقف capture واقعی، اصالت شواهد انسانی، backup/restore عملیاتی یا مجوز حذف واقعی نیستند. پیش‌فرض خاموش حفظ می‌شود. تست Linux و اجرای مجدد suite کامل تاریخی SQL۱۰۳۱ طبق محدودیت جاری انجام نمی‌شود. نتیجهٔ پذیرش دقیق پس از پایان CI در شاهد این ردیف ثبت خواهد شد؛ تا آن زمان شمارنده کاهش نمی‌یابد.
