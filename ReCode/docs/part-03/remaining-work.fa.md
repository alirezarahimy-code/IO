# ماندهٔ دقیق بخش سوم — S04 تا S12

**این فهرست، تغییر دامنه یا پذیرش نهایی نیست. `part3Complete=false`.** طبق تأکید مجدد کاربر، آزمایش واقعی Windows/SDK و دستگاه فیزیکی به انتها موکول است و مانع اجرای نرم‌افزار نیست. [تصمیم اجرا و معیارهای مستقل](software-and-lab-acceptance.fa.md). کدهای فعال دو مرجع مبنای اقتباس رفتارند؛ تست مجازی از مسیر واقعی وب/SQL/worker نیز به مجموعه اضافه شده است. کدهای مرجع فقط‌خواندنی و بدنه‌های decompile باینری مبنای مشاهدهٔ قراردادند؛ نام DLL یا API اثبات پشتیبانی مدل/firmware نیست. آخرین commit و CI معتبر در [progress-evidence.json](progress-evidence.json) ثبت می‌شود.

## وضعیت جاری پس از R15/R16

**ماندهٔ فنی حل‌نشده در دامنهٔ مستند تحویل:۰؛ پذیرش واقعی H01:باز.** R15 مسیر مصوب ClearData۵ دارد و **factory reset نیست**. R16 به‌عنوان تعیین‌تکلیف بسته شد: **USB/serial مستقیم در این نسخه unsupported است و adapter آن ساخته نشده**؛ فایل USB جای آن نیست. این نتیجه هم‌ارزی تمام قابلیت‌های مرجع یا پذیرش دستگاه نیست. [گزارش تحویل جاری](part-03-software-delivery.fa.md)، [شاهد نهایی Windows/UI](r15-r16-software-evidence.json)، [آزمایشگاه پایانی H01](h01-final-windows-lab-handoff.fa.md). `part3Complete=false` و default-off واقعی حفظ شده؛ Linux تست نشده است.

## جدول و ادامه‌های تاریخی ثبت نیازها

جدول زیر و برش‌های بعدی تاریخچه‌اند؛ عبارت‌های «ساخته نشده/مانده» در آن‌ها با followup همان ردیف و ledger جاری خوانده شوند، نه به‌عنوان وضعیت نهایی نرم‌افزار.

| مرحله | آنچه نرم‌افزاری ساخته و آزموده شده | ماندهٔ واقعی |
|---|---|---|
| S04 | ثبت چنددستگاه، گروه metadata، grant/revoke، حفاظت secret | پذیرفته‌شده؛ ثبت دستگاه اثبات اتصال نیست |
| S05 | صف SQL، هویت مستقل، timeout/چهار slot، bridge بیرون وب، pin سریال، سلامت و heartbeat؛ نصب/اجرا/توقف واقعی worker با SCM/systemd در CI جدا | نصب SDK سازگار در آزمایشگاه Windows، اتصال و خطا/قطعی واقعی چند دستگاه؛ نصب CI با DB synthetic این آزمون نیست |
| S06 | مشخصات/ظرفیت/ساعت، تنظیم ساعت تأییدشده، unknown/review، شش GetDeviceInfo خام nullable؛ برش IPv4 قدیم/جدید با inspection خواندنی (CI موفق؛۲۶ SQL،۳ HTTPS و۱۶ قرارداد/فرایند) | هر هفت تنظیم با قرارداد نرم‌افزاری source-backed پذیرفته‌اند؛ شاهد writable COM مدل و lease/اعمال DHCP در H01؛ قرارداد نصب و پذیرش واقعی در H01 |
| S07 | دریافت SSR کاربران و پیش‌نمایش exact نگاشت/تعارض/ناشناس بدون overwrite | اجرای واقعی مدل/SDK؛ مسیر non-TFT خواندنی GetAllUserInfo اکنون با قرارداد صریح legacy-int32-v1 و آزمون نرم‌افزاری ساخته شده، نه fallback حدسی SSR. پذیرش سخت‌افزار جدا باقی است |
| S08 | preview/hash، ارسال عادی فردی/چند نفر/چند دستگاه، نتیجهٔ مستقل، partial retry و unknown review | رفت‌وبرگشت واقعی روی مدل/کارت مصوب و سنجش قطعی/موفقیت جزئی؛ ارسال عادی non-TFT اکنون با UserWriteFormat مستقل و read-back/unknown review ساخته و آزموده شده، ولی snapshot legacy مجوز اجرای SSR نیست و پذیرش مدل واقعی باز است؛ دادهٔ اثرانگشت/چهره/عکس بدون سیاست حریم خصوصی/نگهداری و پشتیبانی مدل فعال نیست |
| S09 | دریافت SSR و non-TFT مرجع با قالب صریح و حفظ دو machine، اصل tuple، range UTC سرور، قرنطینه، receipt raw/conflict، تکرارپذیری، دقت دقیقه و جلوگیری از raw مختلط، بازیابی restart | استخراج/قطعی/دریافت تکراری واقعی و trace تا محاسبه؛ tuple+رتبهٔ multiplicity شناسهٔ رخداد فیزیکی نیست؛ کامل‌شدن خواندن SDK snapshot اتمیک نیست |
| S10 | polling مصوب با قالب ثابت SSR/non-TFT و run/دانلود منطبق، cursor/run، backoff، pause/resume، revoke، auto-ingest اختیاری و بازخوانی overlap صریح | پروتکل vendor PUSH برای مدل پشتیبان با هویت رمزنگاری‌شده، replay/size/rate/congestion/recovery؛ polling یا بازخوانی overlap نام PUSH نمی‌گیرد؛ پذیرش واقعی زمان‌بندی |
| S11 | history پنج صف، retry خواندنی با lineage؛ settings.read با حق و تأیید دوم؛ زمان بیکاری کد۴ با preview/hash، approval دقیق محصول/firmware/مقدار و unknown review؛ برش جدید device.restart با حقوق مستقل/unknown review و بدون replay نوشتن (CI موفق؛۱۷ SQL،۳ HTTPS و۱۱ قرارداد/فرایند) | RemoteEnroll با قرارداد مدل و سیاست بیومتریک، و تکمیل پذیرش واقعی restart و تعیین‌تکلیف/پیاده‌سازی مصوب reset/delete تردد/کاربر/مدیر با حقوق و تأییدهای مستقل و unknown recovery؛ هیچ‌کدام از دکمهٔ retry خواندن قابل اجرا نیستند |
| S12 | فایل UTF-8 پنج‌ستونی Personnel، حفظ bytes، preview/قرنطینه/commit، نصب lifecycle واقعی دو OS در CI و health SQL | نصب عملیاتی آزمایشگاه/کلید/ACL/SDK و ماتریس واقعی مدل‌ها؛ سناریوی واقعی چنددستگاه/قطعی/دریافت تکراری/تبادل/تنظیمات، نمونهٔ واقعی فایل مدل‌های هدف و فساد فایل؛ simulator کافی نیست |

## وضعیت تاریخی پس از R08/R09 و پیش از پذیرش R10

مسیر نرم‌افزاری gateway منتخب و RemoteEnroll پیش‌فرض خاموش، شامل collector/ACK بادوام، پذیرش صریح raw و بازیابی فیزیکی دو نفره تکمیل و روی `cffed82` در Windows با۶۳ بررسی یکپارچه و۴۷ قرارداد پذیرفته شد. [راهنما](native-push-reviewed-recovery.fa.md)، [شاهد](native-push-reviewed-recovery-evidence.json)، [شمارندهٔ به‌روز:۷ فنی + H01](remaining-count-ledger.fa.md). مدل/سیاست/دستگاه واقعی هنوز پیش‌شرط استقرار است؛ متن‌های قدیمی زیر که collector یا unknown recovery را «ساخته‌نشده» می‌نامند تاریخی‌اند. ادامه از R10 است؛ part3Complete=false.

## پیش‌نیاز بستن مانده‌های مدل‌محور

1. product/model/firmware واقعی و معماری/نسخهٔ SDK مجاز؛ snapshot اطلاعات واقعی هر دستگاه از مسیر موجود، بدون ارسال رمز یا فایل credential در گفتگو.
2. برای نوشتن تنظیمات: سند یا شاهد قابل ردیابی همان مدل، code، نوع/واحد، دامنهٔ مجاز، اثر جانبی و شیوهٔ read-back/recovery. شش عدد raw فعلی واحد/دامنه را ثابت نمی‌کنند. اتصال دستگاه تردد مجوز کنترل درب نیست.
3. برای PUSH: مشخصات سازندهٔ همان مدل شامل authentication/key handling، امضای پیام یا سازوکار تأییدشدهٔ هویت، replay، encoding/clock، bounds و recovery. یک endpoint عمومی و حدس query-string زنجیرهٔ امن PUSH نیست.
4. برای RemoteEnroll و انتقال بیومتریک: سیاست مصوب دسترسی، رضایت/مبنای مجاز، نگهداری/حذف و قرارداد مدل/finger-index/flag؛ تأیید عمومی مدیریت دستگاه این سیاست نیست. قالب یا نمونهٔ بیومتریک خام به گفتگو فرستاده نشود.
5. اجرای واقعی Windows lab با حداقل دو دستگاه مستقل، ثبت serialهای متمایز و اجرای سناریوهای مصوب از UI و worker فعلی. قطع/وصل شبکه، بازاجرای دریافت و رفت‌وبرگشت داده باید واقعاً رخ دهند؛ timestamp یا heartbeat به‌جای آن پذیرفته نمی‌شود.

نوشتن مخرب بدون این مرزها، غیرفعال‌کردن تردد دستگاه، تغییر password/admin/AccGroup از مسیر legacy، کنترل فیزیکی درب و payroll راه میان‌بُر بستن بخش سوم نیستند. ویژگیِ پشتیبانی‌نشده باید با شاهد همان مدل در coverage ثبت شود، نه فقط به نام DLL نسبت داده شود.


## تصمیم ادامه با دستگاه مجازی

آزمایشگاه stateful دو دستگاه با پروتکل خصوصی TCP و **۱۹ بررسی Windows/Linux** ساخته و اجرا شد. با آن یک ایراد واقعی در fail-fast ناظر native stream کشف و اصلاح شد. [راهنما و تحویل تست واقعی](virtual-device-lab.fa.md). این ادامه با توافق تست مجازی اکنون/تست واقعی توسط تیم بعداً است؛ مانده‌های جدول را حذف یا completed نمی‌کند. relay تست در worker نصب‌شده قابل انتخاب نیست؛ emulator پروتکل ZK، اجرای COM و end-to-end SQL/UI/device با این ابزار ادعا نشده‌اند.

## پژوهش سازنده و برش جدید

[بررسی منابع رسمی/عمومی ZKTeco](zk-vendor-research.fa.md): تفاوت firmwareهای F18، محدودیت سند قدیمی PUSH، اثر حذف template و قرارداد راه‌اندازی مجدد ثبت شد. device.restart اکنون کد/UI/SQL و آزمون دارد؛ [شاهد CI این برش](s11-restart-evidence.json) با ۷۰۷ بررسی integration و موفقیت Windows/Linux ثبت شد. این برش reset/حذف/RemoteEnroll/PUSH امن را کامل نمی‌کند.

[برش تغییر IPv4 و بازیابی دو مقصد](s06-network-ip.fa.md) اکنون کد/UI/SQL و آزمون دارد؛ [شاهد CI موفق](s06-network-ip-evidence.json) با۷۳۶ بررسی integration و Windows/Linux ثبت شد. این قابلیت gateway/mask/DHCP/port، نوشتن generic تنظیمات یا PUSH را پیاده‌سازی نمی‌کند.

## برش زمان بیکاری

[کد۴، دقیقه و مرز خواب/خاموشی](s11-idle-minutes.fa.md) با [CI موفق](s11-idle-minutes-evidence.json)،۷۶۸ integration و۲۳ قرارداد/فرایند ساخته شد. فقط دامنهٔ برنامه۱..۹۹۹ و مقادیر مصوب دقیق نصب؛ صفر/غیرفعال و کد۸ حدس زده نشد. این برش نه سایر تنظیمات را می‌بندد و نه پذیرش مدل واقعی را.

## تطبیق سرتاسری جدید و اصلاح‌های ایمنی

[گزارش تطبیق ۵۳ ردیفی](device-reference-parity.fa.md) با [موجودی هش‌دار](device-reference-inventory.json) و [crosswalk کامل](device-reference-crosswalk.csv) اضافه شد. [دامنهٔ دستگاه منتخب اپراتور](s04-device-operator-scope.fa.md) اکنون با سیاست و grant/revoke نسخه‌دار، اعمال در query/worker و آزمون SQL/Chromium ساخته شده است؛ grant عملیاتی سراسری با scope یکی نیست. [adapter تردد native non-TFT مرجع](s09-legacy-attendance.fa.md) اکنون ساخته و در قرارداد/SQL/HTTPS/Chromium/worker آزموده شده است؛ [زمان‌بندی با قالب ثابت SSR/non-TFT](s10-legacy-polling.fa.md) نیز به مسیر واقعی worker و SQL وصل و آزموده شده است؛ پذیرش واقعی مدل در انتها باقی است. USB فایل با اتصال مستقیم USB یکی نیست. خواندن ثابت IP گزارش‌شده و نسخهٔ خام الگوریتم اضافه شد؛ هیچ setter عمومی یا fallback نسخه۹ فعال نشد. محدودیت بین صف‌های polling و چهار نوشتن حساس/unknown و binding کامل credential اصلاح و regression اضافه شد. نتیجهٔ CI دقیق در [شاهد جدید](device-reference-parity-evidence.json) ثبت می‌شود؛ جدول بالا پذیرش سخت‌افزاری نمی‌شود.

## ثبت تصمیم مشخص، نه توقف کلی

[مدل، API و تصمیم لازم برای هر خانواده](model-and-policy-decisions.fa.md) اکنون شماره‌خط و قرارداد مشاهده‌شده را از دامنه/اثر نامعلوم و سیاست داده جدا می‌کند. [خواندن مصوب کلید عملکردی و گزارش mask/gateway](s11-function-key-read.fa.md) نیز پیاده و روی f737144 آزموده شد؛ [نوشتن کامل کلید](s11-function-key-write.fa.md) جدا روی97cac12 با SQL۹۱۰ و Windows/Linux/light موفق است. نوشتن DLST و سایر تنظیمات باقی‌مانده، عملیات مخرب و PUSH امن با این کار completed نشده‌اند.

## برش جاری تنظیمات و مجوز کلید

پنج پارامتر عددی با [قرارداد بسته و پیش‌فرض خاموش](s11-approved-raw-settings.fa.md) در کد ساخته شد؛ این پیاده‌سازی جای تعیین مدل/firmware/واحد/اثر/مقادیر واقعی را نمی‌گیرد. مسیر واقعی HTTPS/SQL/worker/child/TCP برای lost ACK و review نیز با دادهٔ مصنوعی افزوده شد. اصلاح مستقل keys.read/keys.execute و حذف افشای payload در settings-only اجرا شده؛ SQL۸۸۸ و Windows/Linux/light کد f737144 موفق‌اند. این موارد صرفاً دکمهٔ غیرفعال نیستند، اما completed یا دستگاه‌پذیرفته نامیده نشده‌اند.

### نقطهٔ پایان این برش و کار بعدی

هستهٔ پنج setter عددی و خواندن کلید روی f737144 با SQL۸۸۸ و Windows/Linux/light سبز است. برش نوشتن کامل کلید در97cac12 با SQL۹۱۰، Windows/Linux و light موفق شده است؛ دیگر «کار بعدیِ هنوز ساخته‌نشده» نیست. خواندن خام DLST با شاهد embedded TypeLib رویc71d1d1 با21 قرارداد/child، SQL۹۱۵ و Windows/Linux/light موفق شد. مانع «getter پیدا نشده» رفع شد؛ آن نیازها اکنون در برش مستقل profile با قرارداد کامل، طرح بازیابی و آزمون پذیرفته‌شده6e8f9d9 ساخته شده‌اند؛ TCPPort با شاهد write باینری85347c2 نرم‌افزاری بسته شد؛ قرارداد نرم‌افزاری DHCP با شاهد firmware و mapper5e1f332 بسته شد؛ COM مدل واقعی در H01 است. PUSH امن، حذف و سیاست بیومتریک همچنان ناتمام‌اند. وضعیت لحظه‌ای CI در progress-evidence نگهداری می‌شود؛ pending/cancelled پذیرفته نیست و هیچ‌یک تکمیل بخش۳ نیست.

در ادامهٔ تحقیق DLST، disassembly همان DLL علاوه بر قالب۸رقمی، شاخه‌های متنی دارای DLSTMode=0/CurTimeMode=0 را نشان داد؛ [شاهد و اقدام بعدی](s11-daylight-read.fa.md). پیش از نوشتن، read-back/حفظ یا تغییر صریح این حالت‌های جانبی هم باید قرارداد داشته باشد. getter سه‌خروجی به‌تنهایی old-state کامل نیست. این پیش‌شرط در برش پذیرفته‌شدهٔ profile پیاده و آزموده شده است؛ ادامهٔ فعلی R08–R16 و H01 باقی‌مانده است، نه ادعای تکمیل بخش۳.

## شمارندهٔ فعلی پس از برش profile

[ledger ثابت](remaining-count-ledger.fa.md): **۹ مورد فنی + H01**؛ هر هفت ردیف DLST/الگوریتم/mask/gateway/DHCP/MAC/port با مسیر و قرارداد نرم‌افزاری و CI پذیرفته‌اند؛ [شاهد تکمیلی5e1f332](s11-dhcp-write-contract.fa.md). PUSH، عملیات حساس و اتصال مستقیم طبق شناسه‌های همان ledger بازند. [شاهد نرم‌افزار](s11-profile-settings-evidence.json) جای مدل/SDK واقعی نیست.

## R08 — آغاز مرتب مانده‌های۹گانه

[codec هفت‌ستونی PUSH](s10-push-attlog-contract.fa.md) با۴۷ بررسی و CI کامل6d11f0f پذیرفته شد؛ فقط زیرکار wire است، نه PUSH امن. هویت cryptographic، SQL journal/receipt، endpoint محدود و UI/permission مستقل هنوز ساخته نشده‌اند؛ R08 و جمع۹ فنی باز می‌مانند. انتخاب vendor-native به قرارداد کامل SDK نیاز دارد؛ edge احرازشده مرز اعتماد متفاوتی است و بدون تصمیم صریح جایگزین نمی‌شود.

### ادامهٔ مستقل R08 پذیرفته شد

[نگهداری شواهد SQL و رسید پس از commit](s10-push-journal-contract.fa.md) روی `b1bfee7` با۲۴ بررسی جدید و SQL کامل۱۰۱۵ و Windows/Linux موفق است. این کار بدون بازکردن endpoint و بدون تصمیم خودسرانه برای gateway انجام شد؛ نه device authentication و نه processed ACK است. R08 هنوز باز و ترتیب R09–R16 محفوظ است.

### بازبینی خواندنی R08 پذیرفته شد

[ابزار فارسی شواهد تأییدنشدهٔ PUSH](s10-push-review-contract.fa.md) با مجوز/دامنهٔ مستقل، APIهای فقط GET، snapshot UTC ثابت و preview نگاشت/قرنطینه روی `adcb551` پذیرفته شد: SQL/HTTPS۱۰۳۱ و Windows/Linux موفق،۱۹ فایل DOM و۱۹ policy موفق. بدون دریافت مستقیم vendor، بدون ثبت تردد و بدون ACK؛ R08 باز است.

### اتصال جدید R08/R09 روی Windows پذیرفته شد؛ هنوز اعلام تکمیل بی‌قید نیست

برش `248cbcd` با۳۹ بررسی یکپارچهٔ Windows LocalDB/HTTPS/courier/صف/worker و۴۷ قرارداد/child و light موفق، مسیرهای واقعی نرم‌افزار را وصل کرد. [راهنمای عملیاتی](gateway-enrollment-operations.fa.md) و [شاهد](gateway-enrollment-operational-evidence.json). هویت gateway با هویت دستگاه یکی نیست: receiver بومی محل و پردازش/ACK هنوز ساخته نشده‌اند. enrollment واقعی default-off است؛ policy/مدل و بازیابی unknown هنوز لازم‌اند و COM واقعی آزمایش نشده است. R08/R09 بسته نشده‌اند؛۹ فنی + H01 باقی است. هیچ تست Linux یا اجرای مجدد suite کامل SQL در این برش انجام نشد.


## برش نخست تاریخی R10

انتقال مستقیم اثرانگشت/چهره به اسلات خالی اکنون metadata-only SQL queue، مجوز و scope هر دو سر، preview دقیق نگاشت، UI فارسی، worker واقعی و bridge ایزوله دارد. [Windows `5a64085`:۳۱ قرارداد مصنوعی و۸۲ بررسی یکپارچه، شامل۱۹ جدید](r10-biometric-transfer-evidence.json). عکس و بازیابی اختصاصی unknown باقی‌اند؛ R10 باز و R11 هنوز شروع نشده است. جزئیات و محدودیت‌های واقعی SDK/قالب/حافظه در [راهنمای R10](r10-biometric-transfer.fa.md) ثبت شده‌اند. عدد۷ فنی + H01 تغییر نکرده است.

## وضعیت تاریخی پس از R10 و پیش از R11

عکس خصوصی bounded روی NTFS/EFS، پاک‌سازی crash و بازیابی اختصاصی unknown با observation بدون setter و امضای مستقل تکمیل و روی `22105bd` پذیرفته شد: [Windows۱۰۰ یکپارچه +۶۳ قرارداد مصنوعی زیست‌سنجی +۴۷ قرارداد قبلی](https://github.com/alirezarahimy-code/IO/actions/runs/43695380624)، [light/UI](https://github.com/alirezarahimy-code/IO/actions/runs/43695380618). [راهنما](r10-photo-recovery.fa.md)، [شاهد](r10-photo-recovery-evidence.json). R10 صرفاً نرم‌افزاری بسته شد؛ واقعی default-off و H01 باز است. Linux و suite کامل تاریخی SQL۱۰۳۱ تکرار نشدند.

ماندهٔ ثابت اکنون **۶ نرم‌افزاری + H01** است: R11 حذف همهٔ تردد، R12 حذف بازه، R13 حذف کاربر/قالب، R14 حذف مدیران، R15 پاکسازی همهٔ داده‌ها/تفاوت factory reset، R16 تعیین‌تکلیف USB/serial مستقیم؛ سپس پذیرش واقعی H01. ترتیب حفظ می‌شود. شمارنده و معیار دقیق در [دفتر مانده‌ها](remaining-count-ledger.fa.md) است. R11 هنوز پیاده‌سازی/پذیرفته نشده؛ مرجع `ClearData(machine,1)` به‌تنهایی اخذ durable پیش از حذف یا جلوگیری از ازدست‌رفتن تردد جدید را اثبات نمی‌کند.

## وضعیت تاریخی پس از R11 و پیش از R12

حذف همهٔ تردد دستگاه با آرشیو کامل native، دو بازبین مستقل، تطبیق دوبارهٔ multiset، read-back خالی و بازیابی اختصاصی unknown تکمیل شد. [Windows d2c5d95:۱۲۹ بررسی یکپارچه، شامل۲۹ جدید؛۲۳ قرارداد مصنوعی جدید حذف](https://github.com/alirezarahimy-code/IO/actions/runs/43697672956) و [light/UI۲۲ فایل DOM، محتوای UI بدون تغییر](https://github.com/alirezarahimy-code/IO/actions/runs/43697294069). [راهنما](r11-attendance-erase.fa.md)، [شاهد](r11-attendance-erase-evidence.json). شرط توقف فیزیکی capture با count یا mutex جایگزین نشده و اجرای واقعی همچنان default-off است؛ هیچ SDK/حذف واقعی اجرا نشد.

ماندهٔ ثابت **۵ نرم‌افزاری + H01**: R12 حذف بازه با مرز زمانی دقیق، R13 حذف کاربر/قالب، R14 حذف مدیران، R15 all-data در برابر factory reset و R16 تعیین‌تکلیف USB/serial مستقیم. سپس H01 واقعی. ردیف‌های «R11 باقی» در متن‌های بالاتر تاریخچه‌اند. Linux یا suite کامل تاریخی SQL۱۰۳۱ تکرار نشده است. مرحلهٔ بعد R12 هنوز پیاده‌سازی/پذیرفته نشده؛ امضای `DeleteAttlogBetweenTheDate` و قالب رشتهٔ مرجع به‌تنهایی inclusive/exclusive بودن مرز firmware را اثبات نمی‌کنند.

## وضعیت پس از پذیرش R12 و پیش از برش اول R13

حذف بازهٔ بستهٔ ساعت محلی دستگاه با مجوز مستقل مدل/مرز/دقت، آرشیو کامل native، دو انسان مستقل، دو scan کامل، تک‌dispatch و تطبیق دقیق multiset باقی‌مانده تکمیل شد. بازیابی اختصاصی بدون بازپخش، unknown اصلی را ثابت نگه می‌دارد؛ مجوز حذف همه جایگزین مجوز بازه نیست. [Windows7787f65:۱۷۵ یکپارچه، شامل۴۶ جدید R12؛۳۴ قرارداد مصنوعی بازه](https://github.com/alirezarahimy-code/IO/actions/runs/43703583328) و [UI ویندوز۲۳ فایل DOM و Chromium، محتوای بدون تغییر](https://github.com/alirezarahimy-code/IO/actions/runs/43702837985). [راهنما](r12-attendance-range-erase.fa.md)، [شاهد دقیق SHA/job و failureهای اصلاح‌شده](r12-attendance-range-erase-evidence.json).

ماندهٔ ثابت **۴ نرم‌افزاری + H01**: R13 حذف کاربر/قالب، R14 حذف مدیران، R15 all-data در برابر factory reset و R16 تعیین‌تکلیف USB/serial مستقیم. سپس H01 واقعی. مطالب «R12 باقی/ساخته‌نشده» در بالا تاریخچهٔ همان برش‌ها هستند. actual SDK/COM و حذف واقعی اجرا نشده، معنای مرز firmware و توقف فیزیکی capture باید در نصب هدف اثبات شوند؛ default-off باقی است. هیچ Linux یا suite کامل تاریخی SQL۱۰۳۱/rollback تازه اجرا نشد. R13 مرحلهٔ بعد است و هنوز پیاده‌سازی/پذیرفته نشده؛ part3Complete=false.

## وضعیت تاریخی — برش اول هستهٔ R13، پیش از پذیرش عملیاتی

هستهٔ محدود حذف کاربر عادی یا تمام اثرانگشت‌های همان فرد در پروفایل مصوب فاقد face/photo/door، با دو catalogue transient محافظت‌شده و readback دقیق سایر کاربران ساخته شد. [Windows6854850:۴۸ بررسی مصنوعی جدید +۱۷۵ یکپارچهٔ قبلی به‌عنوان regression](https://github.com/alirezarahimy-code/IO/actions/runs/43708241242). [راهنما و محدودیت‌ها](r13-user-erase-core.fa.md)، [شاهد](r13-user-erase-core-evidence.json). flag۱۳ SSR و۱۱ legacy معادل فرض نشده‌اند؛ raw template/رمز در DB یا IPC خروجی نیست. dispatcher تولیدی هنوز عمداً اضافه نشده است.

**۴ نرم‌افزاری + H01 ثابت است؛ R13 بسته نشده.** مرحلهٔ بعد همچنان تکمیل R13 است: مسیر durable مجوز/preview/دو امضا، صف و fence، observation و بازیابی اختصاصی unknown، سپس worker/bridge/API/UI و Windows integrated acceptance. سایر storeهای بیومتریک و کنترل دسترسی بدون قرارداد جدا پشتیبانی‌شده نیستند و باید پیش از بستن ردیف تعیین‌تکلیف شوند. R14–R16 بعد از آن و پذیرش واقعی H01 در انتهاست. هیچ Linux، SQL۱۰۳۱ کامل، SDK/COM یا حذف واقعی اجرا نشد.

## وضعیت تاریخی — پس از R13/R14 و پیش از R15/R16

مسیر کامل سه خانوادهٔ مستقل users/fingerprints/administrators از archive و preview/دو انسان تا صف immutable، fence همهٔ صف‌های native، worker/bridge/API/UI، readback محافظت‌شده و بازیابی اختصاصی unknown ساخته و روی Windows پذیرفته شد. R14 فقط demotion با `ClearAdministrators` و حفظ افراد/رمز/کارت/template است، نه حذف مدیر به‌عنوان شخص. profile محدود فاقد face/photo/palm/vein/door/hidden store و ده اسلات fingerprint صریحاً تعیین‌تکلیف شده؛ مدل دیگر پشتیبانی‌شده فرض نمی‌شود و default-off می‌ماند. [قرارداد و راهنمای نصب/بازیابی](r13-r14-protected-erases.fa.md)، [شاهد](r13-r14-protected-erases-evidence.json).

[Windows740dca2](https://github.com/alirezarahimy-code/IO/actions/runs/43710921010):۲۹۶ یکپارچه، شامل۱۲۱ جدید R13/R14 (۱۰۳ SQL/صف/worker و۱۸ HTTPS واقعی) +۳۴ قرارداد مصنوعی جدید protected و۴۸ هستهٔ قبلی R13؛ سایر regressionها نیز موفق. [UI همان SHA](https://github.com/alirezarahimy-code/IO/actions/runs/43710921014):۲۴ فایل DOM و Chromium موفق. observation بازیابی قابل کنارگذاشتن به inspection-failed است تا انقضای مهلت بازبین قفل دائمی ایجاد نکند؛ این عمل original unknown را تغییر یا unlock نمی‌کند و inspection تازه لازم است.

ماندهٔ جاری **۲ نرم‌افزاری**: **R15** پاکسازی همهٔ داده‌ها با قرارداد و مجوز مستقل و تمایز صریح از factory reset، سپس **R16** تعیین‌تکلیف USB/serial مستقیم؛ **H01** پذیرش واقعی Windows/SDK/چنددستگاه جدا و آخر. متن‌های مانده‌های قدیمی، تاریخچهٔ همان SHAها هستند. هیچ SDK/COM، بیومتریک واقعی یا حذف واقعی اجرا نشده؛ مجوز واقعی سازمان/اثر مدل/کنترل همهٔ کانال‌ها هنوز شرط فعال‌سازی‌اند و default-off حفظ شده. Linux، SQL۱۰۳۱ کامل و rollback کامل دوباره اجرا نشد؛ `part3Complete=false`.

## تحویل جاری R15/R16

[Windows355](https://github.com/alirezarahimy-code/IO/actions/runs/43713356207) و [UI26 DOM/Chromium همان b6791a5](https://github.com/alirezarahimy-code/IO/actions/runs/43713356176) موفق‌اند. R15 پس از دو archive کامل و دو امضای مستقل، یک ClearData۵ و readback کل profile و reconnect همان endpoint/key؛ unknown بدون replay و recovery با observation خواندنی/امضای مستقل. [راهنما](r15-all-data-clear.fa.md). R16 صریحاً عدم پشتیبانی USB/serial مستقیم در این نسخه، با علت فنی/مرجع واقعی و کنترل registry/IPC/API/UI؛ نه حذف نامرئی دامنه یا ادعای ساخت adapter. [تصمیم](r16-direct-transport-disposition.fa.md).

طبق [ledger](remaining-count-ledger.fa.md)، R01–R15 در قراردادهای مصوب پذیرفته و R16 تعیین‌تکلیف شده‌اند؛ ۰ فنی حل‌نشدهٔ این تحویل + H01 واقعی. SDK/COM، اثر واقعی۵، سیاست/کنترل فیزیکی سازمان و نصب دو دستگاه هنوز آزموده نشده‌اند. factory reset، direct USB/serial و broad deletion خارج profile به قابلیت پیاده‌شده تبدیل نمی‌شوند. softwareComplete=true فقط دامنهٔ منتخب؛ fullLegacyFeatureParity=false و part3Complete=false. Linux، SQL تاریخی۱۰۳۱ و rollback کامل تکرار نشدند.
