# ماندهٔ دقیق بخش سوم — S04 تا S12

**این فهرست، تغییر دامنه یا پذیرش نهایی نیست. `part3Complete=false`.** کدهای مرجع فقط‌خواندنی و بدنه‌های decompile باینری مبنای مشاهدهٔ قراردادند؛ نام DLL یا API اثبات پشتیبانی مدل/firmware نیست. آخرین commit و CI معتبر در [progress-evidence.json](progress-evidence.json) ثبت می‌شود.

| مرحله | آنچه نرم‌افزاری ساخته و آزموده شده | ماندهٔ واقعی |
|---|---|---|
| S04 | ثبت چنددستگاه، گروه metadata، grant/revoke، حفاظت secret | پذیرفته‌شده؛ ثبت دستگاه اثبات اتصال نیست |
| S05 | صف SQL، هویت مستقل، timeout/چهار slot، bridge بیرون وب، pin سریال، سلامت و heartbeat؛ نصب/اجرا/توقف واقعی worker با SCM/systemd در CI جدا | نصب SDK سازگار در آزمایشگاه Windows، اتصال و خطا/قطعی واقعی چند دستگاه؛ نصب CI با DB synthetic این آزمون نیست |
| S06 | مشخصات/ظرفیت/ساعت، تنظیم ساعت تأییدشده، unknown/review، شش GetDeviceInfo خام nullable؛ برش IPv4 قدیم/جدید با inspection خواندنی (CI موفق؛۲۶ SQL،۳ HTTPS و۱۶ قرارداد/فرایند) | سایر تنظیمات شبکه و نوشتن تنظیمات پیشرفته پس از قرارداد دقیق مدل/firmware، واحد/دامنه/اثر و pin؛ پذیرش واقعی تنظیم ساعت/مشخصات |
| S07 | دریافت SSR کاربران و پیش‌نمایش exact نگاشت/تعارض/ناشناس بدون overwrite | اجرای واقعی مدل/SDK؛ مسیر non-TFT خواندنی GetAllUserInfo اکنون با قرارداد صریح legacy-int32-v1 و آزمون نرم‌افزاری ساخته شده، نه fallback حدسی SSR. پذیرش سخت‌افزار جدا باقی است |
| S08 | preview/hash، ارسال عادی فردی/چند نفر/چند دستگاه، نتیجهٔ مستقل، partial retry و unknown review | رفت‌وبرگشت واقعی روی مدل/کارت مصوب و سنجش قطعی/موفقیت جزئی؛ ارسال عادی non-TFT اکنون با UserWriteFormat مستقل و read-back/unknown review ساخته و آزموده شده، ولی snapshot legacy مجوز اجرای SSR نیست و پذیرش مدل واقعی باز است؛ دادهٔ اثرانگشت/چهره/عکس بدون سیاست حریم خصوصی/نگهداری و پشتیبانی مدل فعال نیست |
| S09 | دریافت SDK با حفظ اصل tuple، range UTC سرور، قرنطینه، receipt raw/conflict، تکرارپذیری و بازیابی restart | استخراج/قطعی/دریافت تکراری واقعی و trace تا محاسبه؛ tuple+رتبهٔ multiplicity شناسهٔ رخداد فیزیکی نیست؛ کامل‌شدن خواندن SDK snapshot اتمیک نیست |
| S10 | polling مصوب، cursor/run، backoff، pause/resume، revoke، auto-ingest اختیاری و بازخوانی overlap صریح | پروتکل vendor PUSH برای مدل پشتیبان با هویت رمزنگاری‌شده، replay/size/rate/congestion/recovery؛ polling یا بازخوانی overlap نام PUSH نمی‌گیرد؛ پذیرش واقعی زمان‌بندی |
| S11 | history پنج صف، retry خواندنی با lineage؛ settings.read با حق و تأیید دوم؛ زمان بیکاری کد۴ با preview/hash، approval دقیق محصول/firmware/مقدار و unknown review؛ برش جدید device.restart با حقوق مستقل/unknown review و بدون replay نوشتن (CI موفق؛۱۷ SQL،۳ HTTPS و۱۱ قرارداد/فرایند) | سایر نوشتن‌های تنظیمات مدل‌محور، RemoteEnroll با قرارداد مدل و سیاست بیومتریک، و تکمیل پذیرش واقعی restart و تعیین‌تکلیف/پیاده‌سازی مصوب reset/delete تردد/کاربر/مدیر با حقوق و تأییدهای مستقل و unknown recovery؛ هیچ‌کدام از دکمهٔ retry خواندن قابل اجرا نیستند |
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
