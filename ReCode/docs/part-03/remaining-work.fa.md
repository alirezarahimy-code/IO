# ماندهٔ دقیق بخش سوم — S04 تا S12

**این فهرست، تغییر دامنه یا پذیرش نهایی نیست. `part3Complete=false`.** طبق تأکید مجدد کاربر، آزمایش واقعی Windows/SDK و دستگاه فیزیکی به انتها موکول است و مانع اجرای نرم‌افزار نیست. [تصمیم اجرا و معیارهای مستقل](software-and-lab-acceptance.fa.md). کدهای فعال دو مرجع مبنای اقتباس رفتارند؛ تست مجازی از مسیر واقعی وب/SQL/worker نیز به مجموعه اضافه شده است. کدهای مرجع فقط‌خواندنی و بدنه‌های decompile باینری مبنای مشاهدهٔ قراردادند؛ نام DLL یا API اثبات پشتیبانی مدل/firmware نیست. آخرین commit و CI معتبر در [progress-evidence.json](progress-evidence.json) ثبت می‌شود.

| مرحله | آنچه نرم‌افزاری ساخته و آزموده شده | ماندهٔ واقعی |
|---|---|---|
| S04 | ثبت چنددستگاه، گروه metadata، grant/revoke، حفاظت secret | پذیرفته‌شده؛ ثبت دستگاه اثبات اتصال نیست |
| S05 | صف SQL، هویت مستقل، timeout/چهار slot، bridge بیرون وب، pin سریال، سلامت و heartbeat؛ نصب/اجرا/توقف واقعی worker با SCM/systemd در CI جدا | نصب SDK سازگار در آزمایشگاه Windows، اتصال و خطا/قطعی واقعی چند دستگاه؛ نصب CI با DB synthetic این آزمون نیست |
| S06 | مشخصات/ظرفیت/ساعت، تنظیم ساعت تأییدشده، unknown/review، شش GetDeviceInfo خام nullable؛ برش IPv4 قدیم/جدید با inspection خواندنی (CI موفق؛۲۶ SQL،۳ HTTPS و۱۶ قرارداد/فرایند) | DHCP/TCPPort نیازمند شاهد writable COM؛ مسیرهای کامل مصوب DLST/الگوریتم/mask/gateway/MAC اکنون پذیرفته‌اند؛ قرارداد نصب و پذیرش واقعی در H01 |
| S07 | دریافت SSR کاربران و پیش‌نمایش exact نگاشت/تعارض/ناشناس بدون overwrite | اجرای واقعی مدل/SDK؛ مسیر non-TFT خواندنی GetAllUserInfo اکنون با قرارداد صریح legacy-int32-v1 و آزمون نرم‌افزاری ساخته شده، نه fallback حدسی SSR. پذیرش سخت‌افزار جدا باقی است |
| S08 | preview/hash، ارسال عادی فردی/چند نفر/چند دستگاه، نتیجهٔ مستقل، partial retry و unknown review | رفت‌وبرگشت واقعی روی مدل/کارت مصوب و سنجش قطعی/موفقیت جزئی؛ ارسال عادی non-TFT اکنون با UserWriteFormat مستقل و read-back/unknown review ساخته و آزموده شده، ولی snapshot legacy مجوز اجرای SSR نیست و پذیرش مدل واقعی باز است؛ دادهٔ اثرانگشت/چهره/عکس بدون سیاست حریم خصوصی/نگهداری و پشتیبانی مدل فعال نیست |
| S09 | دریافت SSR و non-TFT مرجع با قالب صریح و حفظ دو machine، اصل tuple، range UTC سرور، قرنطینه، receipt raw/conflict، تکرارپذیری، دقت دقیقه و جلوگیری از raw مختلط، بازیابی restart | استخراج/قطعی/دریافت تکراری واقعی و trace تا محاسبه؛ tuple+رتبهٔ multiplicity شناسهٔ رخداد فیزیکی نیست؛ کامل‌شدن خواندن SDK snapshot اتمیک نیست |
| S10 | polling مصوب با قالب ثابت SSR/non-TFT و run/دانلود منطبق، cursor/run، backoff، pause/resume، revoke، auto-ingest اختیاری و بازخوانی overlap صریح | پروتکل vendor PUSH برای مدل پشتیبان با هویت رمزنگاری‌شده، replay/size/rate/congestion/recovery؛ polling یا بازخوانی overlap نام PUSH نمی‌گیرد؛ پذیرش واقعی زمان‌بندی |
| S11 | history پنج صف، retry خواندنی با lineage؛ settings.read با حق و تأیید دوم؛ زمان بیکاری کد۴ با preview/hash، approval دقیق محصول/firmware/مقدار و unknown review؛ برش جدید device.restart با حقوق مستقل/unknown review و بدون replay نوشتن (CI موفق؛۱۷ SQL،۳ HTTPS و۱۱ قرارداد/فرایند) | شاهد پشتیبانی نوشتن COM برای DHCP/TCPPort، RemoteEnroll با قرارداد مدل و سیاست بیومتریک، و تکمیل پذیرش واقعی restart و تعیین‌تکلیف/پیاده‌سازی مصوب reset/delete تردد/کاربر/مدیر با حقوق و تأییدهای مستقل و unknown recovery؛ هیچ‌کدام از دکمهٔ retry خواندن قابل اجرا نیستند |
| S12 | فایل UTF-8 پنج‌ستونی Personnel، حفظ bytes، preview/قرنطینه/commit، نصب lifecycle واقعی دو OS در CI و health SQL | نصب عملیاتی آزمایشگاه/کلید/ACL/SDK و ماتریس واقعی مدل‌ها؛ سناریوی واقعی چنددستگاه/قطعی/دریافت تکراری/تبادل/تنظیمات، نمونهٔ واقعی فایل مدل‌های هدف و فساد فایل؛ simulator کافی نیست |

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

هستهٔ پنج setter عددی و خواندن کلید روی f737144 با SQL۸۸۸ و Windows/Linux/light سبز است. برش نوشتن کامل کلید در97cac12 با SQL۹۱۰، Windows/Linux و light موفق شده است؛ دیگر «کار بعدیِ هنوز ساخته‌نشده» نیست. خواندن خام DLST با شاهد embedded TypeLib رویc71d1d1 با21 قرارداد/child، SQL۹۱۵ و Windows/Linux/light موفق شد. مانع «getter پیدا نشده» رفع شد؛ آن نیازها اکنون در برش مستقل profile با قرارداد کامل، طرح بازیابی و آزمون پذیرفته‌شده6e8f9d9 ساخته شده‌اند؛ DHCP/TCPPort از نظر شاهد COM همچنان بازند. PUSH امن، حذف و سیاست بیومتریک همچنان ناتمام‌اند. وضعیت لحظه‌ای CI در progress-evidence نگهداری می‌شود؛ pending/cancelled پذیرفته نیست و هیچ‌یک تکمیل بخش۳ نیست.

در ادامهٔ تحقیق DLST، disassembly همان DLL علاوه بر قالب۸رقمی، شاخه‌های متنی دارای DLSTMode=0/CurTimeMode=0 را نشان داد؛ [شاهد و اقدام بعدی](s11-daylight-read.fa.md). پیش از نوشتن، read-back/حفظ یا تغییر صریح این حالت‌های جانبی هم باید قرارداد داشته باشد. getter سه‌خروجی به‌تنهایی old-state کامل نیست. این پیش‌شرط در برش پذیرفته‌شدهٔ profile پیاده و آزموده شده است؛ ادامهٔ فعلی R05/R07 و ledger باقی‌مانده است، نه ادعای تکمیل بخش۳.

## شمارندهٔ فعلی پس از برش profile

[ledger ثابت](remaining-count-ledger.fa.md): **۱۱ مورد فنی + H01**؛ پنج ردیف DLST/الگوریتم/mask/gateway/MAC با مسیر کامل نرم‌افزاری و CI6e8f9d9 بسته شدند. DHCP/TCPPort، PUSH، عملیات حساس و اتصال مستقیم طبق شناسه‌های همان ledger بازند. [شاهد نرم‌افزار](s11-profile-settings-evidence.json) جای مدل/SDK واقعی نیست.
