# بخش سوم — ساخت کامل ارتباط با دستگاه‌های تردد

## مرز قطعی بخش و وضعیت جاری

**بخش سوم شامل S04 تا S12 است؛ پایان S04 پایان بخش سوم نیست. بخش سوم هنوز تکمیل و پذیرفته نشده است.** اعلام قبلیِ «بخش سوم کامل و بسته شد» نادرست بود و با این سند تصحیح می‌شود.

فقط S04 با شواهد CI پذیرفته شده است. S05 صف تست/worker مستقل، TCP diagnostic و probe بومی candidate را دارد ولی سخت‌افزاری پذیرفته نیست؛ S06 خواندن مشخصات/ظرفیت/ساعت و تنظیم صریح ساعت را به‌صورت candidate دارد؛ سایر تنظیمات و S07 تا S12 کامل نشده‌اند. ۴۲۷ PASS و CIهای موفق S04، شاهد اتصال یا آزمون سخت‌افزار نیستند و پذیرش کل بخش سوم را ثابت نمی‌کنند.

| مرحله | دقیقاً چه چیزی می‌سازیم؟ | نتیجهٔ قابل بررسی | وضعیت |
|---|---|---|---|
| S04 | معرفی چند دستگاه، مشخصات، گروه‌بندی و مجوزهای اپراتور | دستگاه‌ها جداگانه ثبت و مدیریت شوند؛ دسترسی اپراتور محدود و قابل لغو باشد. | ثبت/گروه‌بندی و مجوزهای مستقل سراسری پذیرفته؛ محدودسازی اپراتور به دستگاه‌های منتخب ساخته نشده و ادعا نمی‌شود. |
| S05 | سرویس ارتباط و فرم تست اتصال | اتصال، قطعی، خطا و زمان انتظار هر دستگاه مشخص باشد؛ خرابی یکی بقیه را متوقف نکند. | در حال ساخت؛ صف/worker و probe بومی candidate ZK با سریال پین‌شده؛ مدل و پذیرش سخت‌افزاری هنوز باز |
| S06 | اطلاعات، ظرفیت‌ها و تنظیمات پایه | مشخصات دستگاه خوانده شود؛ ساعت و تنظیمات پشتیبانی‌شده با تأیید تغییر کنند. | candidate نرم‌افزاری مشخصات/ظرفیت/ساعت، تنظیم ساعت و تغییر IPv4 با allowlist و بازیابی خواندنی؛ سایر تنظیمات و پذیرش مدل واقعی باز |
| S07 | دریافت کاربران دستگاه و تطبیق با کارکنان | کاربران ناشناس، تکراری و متعارض پیش از ثبت مشخص شوند. | candidate دریافت SSR و preview واقعی SQL؛ آزمون نرم‌افزاری موفق، بدون پذیرش مدل واقعی |
| S08 | درج و ارسال کاربران به یک یا چند دستگاه | ارسال اطلاعات کاربر، کارت و دادهٔ بیومتریکِ مجاز، با نتیجهٔ جداگانهٔ هر نفر/دستگاه انجام شود. | candidate ارسال عادیِ تأییدشده و صف/نتیجهٔ مستقل هر نفر/دستگاه، CI موفق؛ مدل واقعی باز |
| S09 | استخراج تردد و اتصال به هستهٔ سامانه | تردد واقعی دریافت، نگاشت و پردازش شود؛ دریافت مجدد رکورد تکراری نسازد. | candidate SSR/بازه/اصل tuple/قرنطینه و ثبت تأییدشدهٔ raw/conflict با هویت محتوامحلی؛ پذیرش مدل/SDK واقعی باز |
| S10 | دریافت زمان‌بندی‌شده و PUSH | برای مدل‌های پشتیبان، دریافت خودکار و بازیابی بعد از قطعی کار کند. | polling مصوب/cursor/run و ثبت خودکار اختیاری candidate؛ PUSH احرازهویت‌شده و مدل واقعی باز |
| S11 | صف فرمان، تنظیمات پیشرفته و عملیات حساس | فرمان‌ها قابل پیگیری باشند؛ حذف و تغییر حساس، مجوز مستقل، تأیید و حسابرسی داشته باشند. | تاریخچهٔ پنج صف، retry خواندنی، snapshot شش تنظیم پیشرفته و restart صریح با حق مستقل/unknown review؛ نوشتن تنظیمات/RemoteEnroll و مدل واقعی باز |
| S12 | ورود فایل/USB و آزمون کامل سخت‌افزار | سناریوی چند دستگاه، قطعی، دریافت و ارسال روی دستگاه واقعی تأیید شود. | جزء فایل/preview/قرنطینه/ثبت در CI موفق؛ ابزار نصب سرویس آمادهٔ آزمون؛ پذیرش عملیاتی/سخت‌افزار و کل S12 باز |

## شرط بستن بخش سوم

- تمام S04 تا S12 با DB واقعی، مجوز قابل اعطا/لغو سمت سرور، نگاشت نقش/منو، UI، راهنما و آزمون متناسب ساخته شوند.
- آزمون شبیه‌ساز از آزمون دستگاه واقعی جدا گزارش شود؛ **شبیه‌ساز برای پایان این بخش کافی نیست.**
- مدل/firmware، پروتکل، محیط اجرای سرویس و سناریوهای واقعی چند دستگاه، قطعی، بازیابی، دریافت و ارسال با نتیجه و محدودیت‌های قابل بررسی ثبت شوند. نبود دسترسی به سخت‌افزار، پذیرش سخت‌افزاری را باز نگه می‌دارد؛ نتیجه نباید حدس زده شود.
- مدیریت دستگاه تردد مجوز کنترل فیزیکی درب نیست؛ کنترل درب و payroll خارج از دامنه‌اند.

## شواهد موجود، فقط برای S04

- [گزارش ثبت دستگاه و سابقهٔ تحویل](../full-audit/140-part3-delivery.md)
- [گزارش پذیرش S04 و گروه‌بندی](../full-audit/145-s04-groups-delivery.md)
- [شاهد ماشینی S04](s04-acceptance-evidence.json): کد `069f5aa`؛ Windows/Linux `43115912784` و SQL/HTTPS/browser `43115912754` موفق.
- بازاجرای موفق پس از ثبت شواهد روی `69d8382`: Windows/Linux `43116724600` و SQL/HTTPS/browser `43116725075`.

این شواهد فقط S04 را پوشش می‌دهند؛ هیچ‌یک پایان بخش سوم یا سازگاری دستگاه واقعی را اثبات نمی‌کنند.

## ادامهٔ اجرای دستور S05 تا S12

پاراگراف‌های شاهد زیر، سوابق تاریخی هر برش‌اند؛ وضعیت جاری در جدول بالا و `progress-evidence.json` و آخرین برش پایین صفحه است. اعداد قدیمی به همان commit تعلق دارند، نه آخرین کد.

[گزارش پیشرفت 146](../full-audit/146-s05-progress.md) و [راه‌اندازی worker اولیه](device-worker-setup.fa.md). هیچ نتیجهٔ TCP به‌عنوان اتصال/دریافت/ارسال دستگاه واقعی پذیرفته نمی‌شود.

مسیر آزمایش انتخاب‌شدهٔ کاربر: ZK / Windows؛ قرارداد اتصال از بدنهٔ باینری موجود استخراج و candidate probe مستقل نوشته شده، ولی مدل/firmware و نتیجهٔ آزمون دستگاه واقعی هنوز مشخص نیست.

شاهد جزء نرم‌افزاری native S05: [s05-native-component-evidence.json](s05-native-component-evidence.json)، ۴۵۸ PASS؛ هیچ تست COM یا دستگاه واقعی اجرا نشده. parser داخلی قالب USB مرجع نیز با تست‌های تقویم/DST/رد فرمت مبهم افزوده شده؛ هنوز UI/import به DB و پذیرش S12 ندارد.

آخرین وضعیت تمام مراحل و ماندهٔ نرم‌افزار/سخت‌افزار: [progress-evidence.json](progress-evidence.json). CI موفق جاری روی `6c1652c`: Windows/Linux `43124834036` و SQL/HTTPS/browser `43124834043`؛ ۴۵۸ PASS و ۱۹ تست portable. این اعداد پایان S05..S12 نیستند.

## ادامهٔ S06

برش مشخصات/ظرفیت/ساعت و تنظیم ساعت با تأیید صریح: [سند، حقوق، نصب و بازیابی](s06-information-clock.fa.md). این candidate نرم‌افزاری است؛ آزمون مدل واقعی و پایان S06/S05–S12 ادعا نمی‌شود.

[شاهد جزء نرم‌افزاری S06](s06-component-evidence.json): کد `ba0b0b8`، SQL/HTTPS/browser `43167046675` و Windows/Linux `43167046677` موفق؛ ۴۸۵ PASS یکپارچه، ۴۰ Foundation و ۲۵ portable. این نتیجه شاهد COM/مدل واقعی نیست؛ S06 و بخش سوم همچنان پذیرش نهایی نشده‌اند.

## ادامهٔ S07

[قرارداد، حریم داده، نقش‌ها و بازیابی دریافت کاربران](s07-user-download.fa.md). مسیر جدید فقط خواندن/preview است و کارمند یا نگاشت را خودکار تغییر نمی‌دهد.

## جزء فایل S12

[واردکنندهٔ واقعی SQL، قرنطینه و پیوند به raw/conflicts](s12-file-import.fa.md). این مسیر فایل، شاهد استخراج SDK یا پذیرش چند دستگاه واقعی نیست.

جزء نرم‌افزاری S07 روی `56cca31` با SQL/HTTPS/browser `43170218579` و Windows/Linux `43170218552` موفق شد: ۵۱۰ PASS یکپارچه، ۴۱ Foundation و ۳۳ portable. [شاهد](s07-component-evidence.json). مدل/SDK واقعی همچنان آزموده نشده است.

آخرین جزء نرم‌افزاری S07 + فایل S12 روی `e70c43e` با SQL/HTTPS/browser `43171645469` و Windows/Linux `43171645658` موفق شد: **۵۳۲ PASS، ۴۲ Foundation، ۳۳ portable**؛ ۹۹ حق، ۴۶ صفحه/مقاله، ۲۹ migration/۵۳ جدول. [شاهد و محدودیت‌ها](s12-file-component-evidence.json). این نتیجه پایان بخش سوم نیست؛ S08–S11 هنوز پیاده نشده و پذیرش سخت‌افزار S05–S12 باقی است.

بازاجرای نهایی کد `534ef5f` در SQL/HTTPS/browser `43173466033` و Windows/Linux `43173466045` **موفق**: ۵۳۲ PASS یکپارچه، ۴۲ Foundation و **۳۵ portable**. lifetime سرویس Windows، installer با حساب مجازی/شروع دستی و الگوی systemd آماده‌اند؛ validation در CI نصب سرویس یا پذیرش سخت‌افزار نیست. آخرین [شاهد](s12-file-component-evidence.json) و [نصب/ACL/بازیابی](device-worker-setup.fa.md). بررسی قرارداد ارسال در [S08](s08-reference-safety.fa.md) نشان می‌دهد کپی مسیر legacy می‌تواند password/privilege/AccGroup را تغییر دهد؛ S08 هنوز پیاده نشده است.

### ادامهٔ S08

[ارسال عادی کاربران](s08-user-upload.fa.md): preview واقعی سرور، تأیید hash، intent مستقل برای هر نفر/دستگاه، حفظ password فقط در حافظهٔ child، بدون AccGroup/درب/بایومتریک، نتیجهٔ partial و unknown، بازبینی با snapshot تازه و retry انتخابی. کد `ce81149` با SQL/HTTPS/browser `43218632179` و Windows/Linux `43218632286` موفق شد: **۵۵۱ integration، ۴۳ Foundation، ۴۵ portable**؛ ۱۰۱ حق، ۴۷ صفحه/مقاله، ۳۰ migration و ۵۴ جدول. [شاهد](s08-component-evidence.json). پذیرش سخت‌افزار هنوز باز است.

### ادامهٔ S09

[استخراج SDK و قرنطینه](s09-attendance-extraction.fa.md) روی `59e2e3b` با SQL/HTTPS/browser `43222725559` و Windows/Linux `43222725328` موفق شد: **۵۷۴ integration، ۴۴ Foundation، ۵۵ portable**؛ ۱۰۵ حق، ۴۸ صفحه/مقاله، ۳۱ migration/۵۶ جدول. [شاهد](s09-component-evidence.json). اصل tupleها و رتبهٔ تکرار محفوظ‌اند؛ هویت محتوامحلی، شناسهٔ بومی/فیزیکی رخداد نیست. S10–S11 و پذیرش واقعی SDK/مدل/چند دستگاه همچنان بازند؛ بخش سوم کامل نشده است.


### ادامهٔ S10 و S11

[زمان‌بندی SDK](s10-sdk-schedules.fa.md) روی `5477723` با SQL/HTTPS/browser `43228450432` و Windows/Linux `43228450449` **موفق** شد: ۵۹۹ integration، ۴۵ Foundation، ۵۵ portable؛ ۱۰۸ حق، ۴۹ صفحه/مقاله، ۳۲ migration/۵۸ جدول در آن commit. [شاهد S10](s10-component-evidence.json). این polling مصوب است نه PUSH.

[تاریخچهٔ فرمان و retry خواندنی](s11-command-history.fa.md) با SQL view پنج صف واقعی و lineage immutable افزوده شد؛ آخرین وضعیت CI در [progress-evidence.json](progress-evidence.json) و [شاهد S11](s11-component-evidence.json) ثبت می‌شود. تنظیمات پیشرفته/RemoteEnroll، PUSH امن و پذیرش واقعی چند دستگاه/قطعی/تبادل/تنظیمات همچنان باقی‌اند؛ **بخش سوم کامل نشده است**. هیچ سیاست بایومتریک، مدل یا نصب SDK از روی نام DLL تأیید نمی‌شود.


خط مبنای قبلی S10 + تاریخچه/retry خواندنی S11: کد `e3c751f`، SQL/HTTPS/مرورگر واقعی `43232418581` و Windows/Linux `43232418391` **موفق**؛ **۶۱۸ integration، ۴۶ Foundation، ۲۶ domain و ۵۵ portable**؛ **۱۱۰ حق، ۵۰ صفحه/مقاله، ۳۳ migration/۵۹ جدول**. [شاهد دقیق](s11-component-evidence.json). همهٔ این اعداد نرم‌افزاری‌اند؛ `realSdkExecuted=false` و `hardwareVerified=false` و `part3Complete=false`.


### ادامهٔ خواندن تنظیمات پیشرفته S11

[قرارداد، نقش، نصب و بازیابی](s11-settings-read.fa.md): snapshot شش GetDeviceInfo ثابت مرجع، raw signed/null و حق مستقل خواندن/اجرای اضافی سرویس؛ بدون نوشتن یا RemoteEnroll. آخرین CI تأییدشده در [progress-evidence.json](progress-evidence.json) و شاهد این برش ثبت می‌شود؛ مدل واقعی و بخش سوم همچنان پذیرفته نشده‌اند.


خط مبنای قبلی snapshot: `12db64a`، SQL/HTTPS/مرورگر `43235930669` و Windows/Linux `43235930789` هر دو success؛ ۶۳۸ بررسی integration، ۴۷ Foundation، ۲۶ domain، ۶۱ portable؛ ۱۱۲ حق، ۵۱ صفحه/مقاله، ۳۴ migration و ۵۹ جدول. [شاهد مستقل snapshot](s11-settings-read-evidence.json)؛ این اعداد نرم‌افزاری‌اند، نه پذیرش SDK/مدل واقعی.


### سلامت اجرایی سرویس و آزمون نصب واقعی دو سیستم

کد `8faefd5` با SQL/HTTPS/مرورگر `43243793048` و Windows/Linux `43243792727` **موفق**: **۶۵۷ integration، ۴۸ Foundation، ۲۶ domain، ۶۱ portable؛ ۱۱۴ حق، ۵۲ صفحه/مقاله، ۳۶ migration/۶۰ جدول**. `/workspace/device-health` صف‌ها، آخرین خواندن تردد، زمان‌بندی و heartbeat مستقل SQL را نشان می‌دهد؛ نه آنلاین/آفلاین حدسی. retry تنظیمات به حق typed جاری و checkbox/تأیید دوم نیاز دارد و هیچ نوشتنی را replay نمی‌کند.

نصب/اجرا/توقف واقعی SCM ویندوز و systemd لینوکس با worker منتشرشده و حساب اختصاصی در CI آزموده شد؛ DB آن عمداً unavailable/synthetic و endpoint native خالی است. این نصب آزمایشگاه یا اجرای SDK نیست. [راهنما](s12-service-health.fa.md) و [شاهد](s12-service-health-evidence.json). نوشتن تنظیمات مدل‌محور، RemoteEnroll با سیاست بیومتریک، vendor PUSH امن و پذیرش واقعی چنددستگاه هنوز بازند؛ بخش سوم کامل نشده است.


### ادامهٔ S10 — تردد دیررس در بازخوانی هم‌پوشان

کد `0f31066`، SQL/HTTPS/مرورگر `43255522942` و Windows/Linux `43255522608` هر دو **موفق**: **۶۶۷ integration؛ ۴۸ Foundation، ۲۶ domain، ۶۱ portable؛ ۳۷ migration/۶۰ جدول**. `OverlapSeconds` پیش‌فرض۰، مصوب و immutable است؛ تردد دیررس داخل lookback از مسیر raw/receipt موجود وارد می‌شود و اصل تکراری دوباره ساخته نمی‌شود؛ cursor عقب نمی‌رود و روی partial جلو نمی‌رود. دیررس خارج lookback تضمین نشده است. [راهنما](s10-overlap.fa.md)، [شاهد](s10-overlap-evidence.json)، و **[ماندهٔ دقیق S04–S12 و پیش‌نیاز هر مورد](remaining-work.fa.md)**. این جزء جای PUSH/نوشتن/بیومتریک یا پذیرش واقعی دستگاه نیست.


### آزمایشگاه مجازی و اصلاح واقعی خطای native stream

طبق تصمیم کاربر، تست نرم‌افزاری با دستگاه مجازی جلو رفت و تست دستگاه واقعی برای تیم باز ماند. کد `b1df7dd` با [Windows/Linux 43290520920](https://github.com/alirezarahimy-code/IO/actions/runs/43290520920) و [SQL/HTTPS/مرورگر 43290520893](https://github.com/alirezarahimy-code/IO/actions/runs/43290520893) **هر دو موفق**. ۱۹ بررسی TCP دو دستگاه stateful به ۶۱ بررسی مرزی موجود اضافه شد؛ ۶۶۷ integration موجود نیز سبز ماند. این ۱۹ بررسی از SQL/UI/worker عبور نمی‌کنند و private JSON هستند، نه پروتکل ZK یا اجرای COM.

یک ایراد واقعی Windows کشف و رفع شد: پس از رد frame، `Task.WhenAll` منتظر childِ گیرکرده در pipe می‌ماند. انتظار اکنون fail-fast است؛ regression با ۸۱۹۲ frame اضافی و اجرای کنترلِ الگوی قبلی در Linux نیز آزموده شد. post-dispatch write همچنان unknown است؛ retry خودکار اضافه نشده است. [راهنما/سناریوها/تحویل آزمایشگاه واقعی](virtual-device-lab.fa.md)، [شاهد](virtual-device-lab-evidence.json). `hardwareVerified=false` و `part3Complete=false` پابرجاست.


### ادامهٔ S07 — دریافت legacy non-TFT بدون fallback

کد `50087b9` با SQL/HTTPS/مرورگر `43295571681` و Windows/Linux `43295571689` هر دو **success**؛ **۶۷۷ integration،۱۰ portable جدید قرارداد خواندن،۶۱ portable قبلی،۱۹ virtual،۳۸ migration و۶۰ جدول**. `ReadFormat` در فرم/API/SQL ثابت، claim و start frame صریح و UserReadFormat نصب منطبق است؛ legacy فقط Int32 غیرمنفی canonical دارد، صفر ابتدایی قابل بازیابی نیست و کارت تبدیل عددی نمی‌شود. snapshot هدف legacy و endpoint legacy هر دو از نوشتن SSR جلوگیری می‌کنند. [راهنما](s07-legacy-users.fa.md)، [شاهد](s07-legacy-users-evidence.json). شمارندهٔ ثابت و تست مجازی جای مدل/SDK واقعی نیست؛ بخش سوم هنوز کامل نشده است.


### ادامهٔ S08 — ارسال عادی legacy non-TFT

کد `cce03b0`؛ SQL/HTTPS/مرورگر `43300044652` و Windows/Linux `43300044555` هر دو **success**؛ **۶۸۷ integration،۱۲ قرارداد نوشتن و۴ TCP جدید**. snapshot هدف نوع immutable را تعیین می‌کند؛ UserWriteFormat نصب مستقل و پیش‌فرض SSR است. legacy فقط شناسهٔ canonical Int32 و پیام WriteLegacyUser جدا دارد؛ نه fallback SSR. حفظ password فقط حافظه‌ای، null پیش از mutation رد، read-back متفاوت/پاسخ گم‌شده unknown، review فقط با شاهد تازهٔ همان قرارداد و retry انتخابی تازه است. مدل با password masked قابل تأیید نیست. schema/حق/endpoint جدید لازم نبود؛ SnapshotId و ReadFormat SQL قبلاً immutable‌اند. [راهنما](s08-legacy-user-write.fa.md)، [شاهد](s08-legacy-user-write-evidence.json)، [مانده‌ها](remaining-work.fa.md). `part3Complete=false` و SDK/مدل واقعی همچنان تأییدنشده‌اند.

## پژوهش سازنده و restart مستقل S11

[بررسی منابع رسمی/عمومی ZKTeco](zk-vendor-research.fa.md) · [شاهد restart](s11-restart-evidence.json) · [گزارش ممیزی ۱۵۸](../full-audit/158-zk-research-restart.md). این برش restart است، نه reset کارخانه یا حذف داده. پاسخ SDK و مشاهدهٔ دسترسی مجدد، اثبات قطعی پایان reboot نیستند. جزئیات اعطا/لغو و نصب در [راهنمای worker](device-worker-setup.fa.md) و راهنمای داخل صفحهٔ اطلاعات آمده‌اند.

## تغییر IPv4 و بازیابی بدون تکرار کور

[راهنمای S06/S11 شبکه](s06-network-ip.fa.md) · [شاهد CI](s06-network-ip-evidence.json) · [ممیزی۱۵۹](../full-audit/159-network-ip-transition.md). این برش فقط SetDeviceIP/GetDeviceIP و RefreshData محدود است؛ ACK نوشتن همیشه نیازمند مشاهدهٔ خواندنی تازه و بازبینی است. Host/pin نصب خودکار عوض نمی‌شوند؛ سایر تنظیمات شبکه و پذیرش واقعی باقی‌اند.
