# R10 — عکسِ گذرا و بازیابی اختصاصی unknown

این سند تکمیل مسیر نرم‌افزاری برش اول [انتقال زیست‌سنجی](r10-biometric-transfer.fa.md) را شرح می‌دهد. پذیرش اجرای COM، سیاست زیست‌سنجی واقعی و آزمایش حداقل دو دستگاه همچنان فقط در H01 انجام می‌شود؛ نمونه‌های SYNTHETIC مجوز نصب نیستند. وضعیت پذیرش نرم‌افزاری در [شاهد همین نسخه](r10-photo-recovery-evidence.json) ثبت شده است؛ ساخت کد یا CI ناموفق، ردیف را نمی‌بندد. تست Linux طبق دستور کاربر اجرا نشده و suite کامل تاریخی SQL۱۰۳۱ نیز تکرار نشده است.

## عکس: API مشخص، نه passthrough فایل

مرجع قدیمی عکس را با `ReadFile(machine, badge.jpg, directory)` و `SendFile(machine, path)` جابه‌جا می‌کند؛ ABI هر دو نسخه در [شاهد TypeLib](r10-biometric-typelib-evidence.json) pinned است. APIهای `PTR BYTE` بدون ظرفیت اثبات‌شده استفاده نمی‌شوند. قرارداد عکس فقط `Kind=photo`, `Slot=0`, `FingerprintFlag=0`, `RequiresReadAllTemplate=false` و `PhotoHandlingReference` صریح را می‌پذیرد. شناسهٔ فرد حداکثر۲۰ رقم ASCII است؛ صفر ابتدایی حفظ می‌شود. مسیر یا پسوند از مرورگر پذیرفته نمی‌شود؛ نام دقیق فقط `شناسه.jpg` است.

همهٔ شروط اثرانگشت/چهره برقرارند: جفت دقیق مدل/firmware/serial و قالب، نگاشت جاری هر دو فرد به یک کارمند/شرکت، کاربر عادی فعال موجود، scope و حقوق مستقل در هر دو سر، کنترل انحصاری خارج از نرم‌افزار، پیش‌فرض خاموش در سرور و هر دو endpoint worker. عکس موجود متفاوت overwrite نمی‌شود. مبدأ و خالی‌بودن مقصد پیش از setter دوباره خوانده می‌شوند؛ read-back باید با تمام بایت‌های JPEG یکسان باشد. false/timeout/قطع grant/mismatch پس از dispatch برابر unknown است، نه مجوز تکرار.

`BiometricJpeg` پروفایل ساختاری bounded از JPEG baseline هشت‌بیتی، تک‌اسکن،۱ یا۳ component، حداکثر۴۰۹۶×۴۰۹۶ و طول مصوب حداکثر۱MiB را می‌پذیرد. truncation، دادهٔ دنباله و progressive رد می‌شوند؛ تبدیل قالب، decode تصویر یا حذف EXIF انجام نمی‌شود. این کنترل ساختاری جای اعتبارسنجی decoder دستگاه یا تأیید سازگاری قالب/ابعاد مدل نصب نیست. فایل معیوب یا خروجی SDK بدون EFS منتقل نمی‌شود. false فقط همراه کد دقیق absence مصوب و نبود فایل، «خالی» است؛ فایل نیمه‌کاره absence نیست.

## staging عملیاتی: جداگانه provision شود

تنظیمات `IO:Biometrics:PhotoStaging` فقط در worker و به‌صورت پیش‌فرض خاموش است:

```json
{
  "Enable": false,
  "Root": "R:\\io-photo",
  "StoragePolicyReference": "REPLACE-WITH-APPROVED-REFERENCE",
  "MaximumVolumeBytes": 268435456
}
```

این مثال مسیر یا سیاست واقعی ایجاد نمی‌کند و نباید عیناً فعال شود. تیم نصب باید برای **همان حساب سرویس** موارد زیر را پیش از فعال‌سازی provision و تأیید کند:

- volume کوچک NTFS مستقل و غیرسیستمی، با ظرفیت واقعی حداقل۳۲MiB و حداکثر سقف مصوب۳۲MiB تا۱GiB؛ پیش‌فرض سقف۲۵۶MiB؛ حداقل۴MiB فضای آزاد. پوشه مستقیماً زیر ریشهٔ volume باشد. دیسک عمومی temp/current-directory یا volume سیستم رد می‌شود. صرف quota یک پوشه روی دیسک عمومی کافی نیست.
- پوشهٔ ازقبل‌موجود، مالک همان service SID، ACL محافظت‌شده و غیرارثی، allow فقط برای service SID و SYSTEM؛ ACL وسیع رد می‌شود. volume، mount و محل نگهداری VHD در برابر کاربران دیگر محافظت شوند. پوشه به سرویس دیگری واگذار نشود.
- EFS و certificate/private key قابل استفاده در context واقعی Windows service و پروفایل آن؛ صرف موفقیت حساب تعاملی کافی نیست. کلید EFS، recovery agent، backup، pagefile، dump، snapshot و دسترسی مدیر محلی در سیاست داده تعیین‌تکلیف شوند. installer فعلی این volume یا کلید را خودکار ایجاد نمی‌کند.
- `StoragePolicyReference` عین `PhotoHandlingReference` قرارداد مصوب؛ راه‌اندازی مجدد worker برای تغییر تنظیمات pin‌شده. همهٔ enableها و approvalهای سرور/هر دو endpoint نیز لازم‌اند.

parent پیش از child یک زیرپوشهٔ GUID با ACL خصوصی و EFS، deadline و lease marker می‌سازد. metadata staging حاوی تصویر یا digest نیست. parent قفل انحصاری root و child قفل مستقل زیرپوشه دارد. فایل source و target فقط داخل همان lease است؛ بایت‌های خوانده‌شده در حافظهٔ child پس از Dispose صفر می‌شوند. template/passwordهای managed و bufferهای داخلی SDK مشمول تضمین پاک‌سازی نیستند.

پس از خروج یا kill+انتظار bounded برای پایان child، parent زیرپوشه را پاک می‌کند؛ پاک‌سازی ناموفق نمی‌تواند copied/observation موفق بسازد. worker در startup و دوره‌ای نیز orphan را بررسی می‌کند. orphan جوان، child فعال، marker ناقص، نام/فایل ناشناخته، reparse point یا زیرپوشهٔ غیرمنتظره باعث fail-closed عکس و نیاز به بررسی مدیر می‌شود؛ recursive purge عمومی وجود ندارد. فقط lease معتبر منقضی‌شده پس از۲ دقیقه حاشیه و آزادشدن قفل child پاک می‌شود. اختلال staging مسیر غیرعکس را متوقف نمی‌کند.

این طراحی **secure erase روی SSD/backup، محرمانگی در برابر مدیر محلی، sandbox برای SDK بدخواه یا quota حافظهٔ داخلی COM** را تضمین نمی‌کند. فایل رمز‌شدهٔ orphan تا پاک‌سازی بعدی/رسیدگی مدیر باقی می‌ماند؛ سیاست نگهداری باید این رخداد را پوشش دهد. SDK نصب‌شده باید برای فایل‌نویسی اضافی/رفتار واقعی و پاک‌سازی در H01 بررسی شود. اسکریپت `prepare-biometric-photo-ci.ps1` فقط volume دورریختنی runner را می‌سازد؛ ابزار provision نصب واقعی نیست.

## بازیابی مختص همین صف، نه recovery ثبت اثرانگشت

`IO:Biometrics:EnableRecovery=false` پیش‌فرض است. `IO:Biometrics:RecoveryApprovals` باید یک `BiometricRecoveryApproval` دقیق و یکتا داشته باشد:

- `Transfer`: قرارداد عین انتقال اصلی؛
- `ResetProcedureReference`: شاهد رویهٔ **غیرحذفی** مناسب همان جفت که درخواست معلق را پاک کند؛
- `MinimumSettlingSeconds`:۱۰ تا۳۰۰؛
- `ResetClearsPendingWrites=true` و `AllowRecovery=true` فقط پس از تصویب واقعی.

توقف فیزیکی/تخلیهٔ firmware از ACK یا کشته‌شدن process استنتاج نمی‌شود؛ دو شخص مستقل مسئول اصالت شواهد توقف worker/child و reset هستند. حساب‌های متمایز جای کنترل سازمانی استقلال اشخاص را نمی‌گیرند. حقوق جدید هیچ نقش پیش‌فرض ندارند:

- `devices.biometrics.recovery.propose` به همراه `devices.biometrics.inspect` و `devices.biometrics.source.read` در هر دو سر؛
- `devices.biometrics.recovery.approve` در هر دو سر برای شخص مستقل؛
- سرویس همچنان به `devices.biometrics.execute` و scope هر دو سر نیاز دارد؛ خواندن metadata نیازمند `devices.biometrics.jobs.read` است.

۱. `POST /devices/biometric-recoveries`: UUID پیشنهاد، UUID انتقال اصلی unknown، UUID inspection تازه، نسخهٔ انتقال، approval مصوب، زمان reset واقعی UTC و فقط مراجع شواهد توقف/reset؛ تأیید `RECOVER-BIOMETRIC-FENCE-NOT-COPY-SUCCESS`. reset باید پس از پایان unknown، در یک ساعت اخیر و قدیمی‌تر از settling مصوب باشد. retry عین همان پیشنهاد همان inspection را برمی‌گرداند؛ تغییر آن رد می‌شود.
۲. پیشنهاد فقط یک ردیف **فقط‌خواندنی** در صف پایدار همان جفت/فرد ایجاد می‌کند. pending/running همچنان هر دو endpoint را رزرو می‌کند. سیاست و grantها هنگام claim، credential، هر۵۰۰ms و commit دوباره بررسی می‌شوند. `InspectBiometric` wire جداگانه است؛ bridge انتقال معمولی، `InspectOnly` را رد می‌کند. observation هرگز setter ندارد و خروجی copied را نمی‌پذیرد.
۳. `GET /devices/biometric-transfers/{id}/recoveries`: metadata و version پیشنهاد، وضعیت/زمان inspection و مراجع شواهد؛ AND scope هر دو دستگاه اعمال می‌شود. ماده یا digest از child خارج نمی‌شود.
۴. خروجی مشاهده فقط `observed-equal`, `observed-different`, `target-absent`, `inspection-failed` است. مقایسه با **مبدأ فعلی** انجام می‌شود؛ هیچ‌کدام اثبات نمی‌کنند عملیات اصلی قبلاً موفق یا ناموفق بوده است. timeout/لغو/قطع مجوز observation فقط `inspection-failed` است، نه unknown نوشتن تازه. مبدأ فعلی باید موجود و خواندنی بماند؛ مبدأ خالی/نامعتبر نیز inspection-failed می‌شود و با assertion انسانی دور زده نمی‌شود. شکست inspection مقصد را باز نمی‌کند؛ پیشنهاد تازهٔ صریح لازم است، نه replay نوشتن.
۵. `POST /devices/biometric-recoveries/{id}/approve`: version تازهٔ پیشنهاد، مرجع بازبینی مستقل و همان confirmation. خودامضایی حتی با هر دو حق ممنوع است. original باید هنوز unresolved unknown، نسخه/نصب/نگاشت و حقوق پیشنهاددهنده جاری، همهٔ کارهای دیگر متوقف و observation موفق، پس از پیشنهاد و حداکثر۵ دقیقه قدیمی باشد. فعال‌سازی/approval نیز دوباره کنترل می‌شود.
۶. فقط امضای مستقل، fence همان unknown را رفع می‌کند. **ردیف اصلی unknown برای همیشه تغییرناپذیر می‌ماند**؛ requeue، overwrite، delete یا تبدیل به copied وجود ندارد. هر انتقال بعدی intent/preview جدید و preflight عادی لازم دارد و اسلات موجود متفاوت همچنان رد می‌شود. recovery ثبت اثرانگشت، مجوز این مسیر نیست.

مهاجرت `20261114160000_BiometricPhotoRecovery` lineage inspection و جدول metadata-only `DeviceBiometricRecoveries` را اضافه می‌کند؛ مجموع۵۳ migration و۶۸ جدول دامنه. triggerهای SQL از قصد/lineage/نتیجهٔ نهایی، امضای یک‌طرفه، دو حساب مستقل و مشاهدهٔ تازه محافظت می‌کنند. downgrade با سابقهٔ عکس/inspection/recovery رد می‌شود. سیاست حقوقی، grant جاری و صحت شواهد انسانی همچنان کنترل application/سازمان است، نه ادعای اثبات با trigger.

## نتیجهٔ پذیرش نرم‌افزار

کد `22105bd28f6d7a13d4aed6b8ca9e818e3b2ed80a` با [Windows۲۰۲۲](https://github.com/alirezarahimy-code/IO/actions/runs/43695380624) و [light/UI همان SHA](https://github.com/alirezarahimy-code/IO/actions/runs/43695380618) success شد:۱۰۰ بررسی یکپارچهٔ SQL/HTTPS/صف/worker (۱۸ افزوده بر۸۲ قبلی)،۶۳ قرارداد زیست‌سنجی مصنوعی (۳۲ افزوده بر۳۱ قبلی)،۴۷ قرارداد قبلی gateway/enrollment و۲۱ فایل DOM. حجم NTFS دورریختنی۱۲۸MiB، ACL خصوصی و EFS واقعاً provision و استفاده و detach شدند؛ SDK/COM اجرا نشده است. آزمون واقعی worker از SQL تا child مصنوعی، مشاهدهٔ تازه، جدایی اشخاص، revoke، trigger تغییرناپذیری و درخواست جدید پس از رفع fence پوشش داده شد. snapshot EF با مدل جاری برابر بود و migrationها روی LocalDB اعمال شدند؛ downgrade کامل تاریخی در این suite تکرار نشده است.

خطای پیش از پذیرش `80045d8` ناشی از تبدیل null اختیاری در سازندهٔ config مصنوعی به رشتهٔ خالی بود؛ پس از اصلاح omission، `762aa68` و نسخهٔ hardened نهایی هر دو green شدند. کنترل‌های دقیق قرارداد برای عبور تست ضعیف نشدند. **R10 نرم‌افزاری بسته و مانده۷→۶ شد**؛ نتیجهٔ نصب واقعی یا مجوز زیست‌سنجی از این پذیرش استنتاج نمی‌شود. ادامه از R11 و سپس سایر ردیف‌ها به‌ترتیب است.
