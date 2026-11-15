# شمارندهٔ ثابت مانده‌های بخش۳

این خط مبنای شمارش از وضعیت اجرایی پذیرفته‌شدهٔ `c71d1d15424aaaa94e079e70a6fa001dcc572066` استخراج شد؛ شمارندهٔ تاریخیِ ازقبل‌ثبت‌شده نیست. اسناد پیشین فهرست موضوعی داشتند، نه واحد شمارش ثابت.

## جمع

- ۰ ردیف فنی حل‌نشده در دامنهٔ مستند این تحویل (خط مبنا۱۶؛ R01–R15 مسیر نرم‌افزاری مصوب و R16 تعیین‌تکلیف صریح پشتیبانی دارد). این عدد به معنی۱۶ قابلیت پیاده‌شده یا هم‌ارزی کامل با مرجع نیست.
- ۱ بستهٔ مستقل پذیرش واقعی SDK/چنددستگاه، طبق دستور کاربر در انتها؛ داخل عدد۰ نیست؛ در خط مبنای۱۶ نیز جدا بود.
- شمار ردیف باز، درصد پیشرفت یا تخمین زمان نیست؛ ردیف‌ها هم‌اندازه نیستند.

| شناسهٔ ثابت | مورد باز | نوع مانده |
|---|---|---|
| — | ردیف فنی حل‌نشده‌ای در دامنهٔ این تحویل نمانده | تنها H01 واقعی باز است؛ محدودیت‌های صریح پشتیبانی زیر را بخوانید |

R16 به‌عنوان **تعیین‌تکلیف نسخه** بسته شد: direct USB/serial در این تحویل پشتیبانی نمی‌شود و adapter آن ساخته نشده؛ متدهای مرجع واقعاً موجودند. factory reset نیز پشتیبانی نمی‌شود؛ ClearData۵ جای آن نیست. جزئیات و علت و کنترل‌های اجرایی در بخش نهایی همین دفتر ثبت است. بستن شمارنده این محدودیت‌ها را ناپدید یا پذیرش سخت‌افزار نمی‌کند.

## بستهٔ پذیرش پایانی — P3-H01

نصب عملیاتی Windows/SDK/ACL و حداقل دو دستگاه واقعی؛ اتصال/قطعی، اطلاعات و تنظیمات مصوب، رفت‌وبرگشت کاربران، استخراج و دریافت تکراری، polling و نمونهٔ فایل واقعی. این یک بستهٔ پیگیری است، نه یک تست ساده؛ زیرسناریوهای موجود در راهنمای پذیرش باید همگی تعیین‌تکلیف شوند. simulator، CI دو سیستم‌عامل یا heartbeat این بسته را نمی‌بندند.

## کاهش مشاهده‌شده با همین تفکیک

- نوشتن کامل کلید عملکردی یک ردیف فنی بسته‌شدهٔ نرم‌افزاری است: `97cac12`، suite۹۱۰ و Windows/Linux/light موفق؛ پذیرش واقعی در H01 باقی است. با همین واحد شمارش، پیش از بسته‌شدن آن **۱۷ ردیف** و در خط مبنای c71d1d1 **۱۶ ردیف** باز می‌بود. این مقایسه بازسازی‌شده است، نه شمارش تاریخی ازقبل ثبت‌شده.
- خواندن DLST روی `c71d1d1`، suite۹۱۵ و Windows/Linux/light موفق است؛ اما چون R01 «نوشتن کامل DLST» است، خواندن به‌تنهایی عدد خط مبنای۱۶ را کم نمی‌کرد؛ کاهش جدید فقط از پذیرش نوشتن کامل در برش زیر است. پیشرفت زیرکار ثبت می‌شود، نه بسته‌شدن مصنوعی ردیف.

## قاعدهٔ به‌روزرسانی

۱. شناسه‌ها ثابت می‌مانند؛ افزودن تست/permission/رفع ایراد زیر همان ردیف، ردیف تازه نیست.
۲. برای کاهش شمارنده، مسیر نرم‌افزاری کامل با CI مرتبط پذیرفته شود، یا تعیین‌تکلیف پشتیبانی/خروج از دامنه با شاهد/تصمیم معتبر ثبت شود. pending، دکمهٔ غیرفعال و نبود دستگاه دلیل بستن نیستند.
۳. اگر دامنهٔ واقعی تازه‌ای کشف یا درخواست شد، افزودن ردیف با علت و تغییر شمارنده جدا گزارش شود؛ بی‌صدا عدد ثابت نگه داشته نشود.
۴. پیشرفت زیرکار، ماندهٔ فنی و پذیرش واقعی جدا گزارش شوند. تعداد PASS جای تعداد قابلیت بسته‌شده نیست.

## نتیجهٔ تاریخی برش نخست هفت تنظیم — 6e8f9d9

نسخهٔ `6e8f9d9` با [SQL۹۹۱](https://github.com/alirezarahimy-code/IO/actions/runs/43611624749) و [Windows/Linux همان SHA](https://github.com/alirezarahimy-code/IO/actions/runs/43611624708) موفق شد؛ ۱۰۰ بررسی قرارداد/child، ۶۲ بررسی SQL مختص profile، ۱۴ سناریوی افزودهٔ worker و فرم واقعی Chromium پوشش داده شده‌اند. این تست‌ها SDK/دستگاه واقعی نیستند.

| شناسهٔ ثابت بسته‌شدهٔ نرم‌افزاری | قابلیت | شرط و حد پذیرش |
|---|---|---|
| P3-R01 | DLST با حالت کامل جانبی | قالب هشت‌رقمی، تفسیر سالانه/zone مصوب نصب، preflight و readback کامل؛ نه پشتیبانی حدسی تمام modeها/مدل‌ها |
| P3-R02 | تغییر الگوریتم | حالت/اثر دقیق نصب و شاهد حفظ template پیش‌شرط است؛ حذف/تبدیل بیومتریک مجاز نشده |
| P3-R03 | subnet mask | NetMask، مقصد اصلی/بازیابی مصوب، unknown شبکه و inspection مستقل |
| P3-R04 | gateway | GATEIPAddress، مقصد اصلی/بازیابی مصوب، unknown شبکه و inspection مستقل |
| P3-R06 | MAC | SetDeviceMAC/RefreshData؛ readback مستقل پس از قطع، نه موفقیت از ACK |

بنابراین کاهش این برش **۱۶ → ۱۱** است، نه هفت بسته‌شدن با اتکا به adapter یا شمار تست. R05 و R07 مسیر و تست نرم‌افزاری دارند ولی شاهد کافی نوشتن COM برای `DHCP`/`TCPPort` مدل هدف هنوز باز است؛ unsupported عمومی اعلام نشده‌اند. H01 همچنان جدا و باز است.

خطاهای میانی Chromium پذیرفته نشدند: انتظار async تست پیش از پایان inspection عبور می‌کرد و سرور به‌درستی409 می‌داد؛ انتظار رسید صریح SQL/HTTPS و قفل ورودی هنگام تعویض تاریخچه اصلاح و مسیر کامل سبز شد. ممیزی حداکثر Unicode نیز ورودی بیش از8KB را آشکار کرد؛ سقف محدود stdin اصلاح و خروجی/SQL بدون افزایش سقف آزموده شد.

جزئیات مسیر، حدود شاهد و نصب: [قرارداد هفت تنظیم](profile-settings-contract.fa.md)؛ [شاهد](s11-profile-settings-evidence.json). آزمایش واقعی همان مدل‌ها، zone، template و بازیابی شبکه هنوز در H01 انجام نشده است.

## کاهش بعدی با شاهد setter باینری port

`85347c2` با [SQL۹۹۱](https://github.com/alirezarahimy-code/IO/actions/runs/43618440584)، [Windows/Linux](https://github.com/alirezarahimy-code/IO/actions/runs/43618440580) و [آزمون سبک شامل۳۱ probe](https://github.com/alirezarahimy-code/IO/actions/runs/43618440620) همگی success شد. `P3-R07` اکنون **بستهٔ نرم‌افزاری** است: علاوه بر مسیر کامل مصوب و CI قبلی، SDK6 مسیر نوشتنی صریح `SetDeviceInfo(70)` برای TCPPort دارد و setter عمومی دو DLL hash-pinned نیز همان payload را به مرز transport می‌رساند. کد69 در این DLL ControlMinute است و استفاده نمی‌شود. شاهد [قابل بازتولید](s11-network-native-dispatch.fa.md) است، نه اجرای COM/firmware.

کاهش جدید **۱۱ → ۱۰** و کاهش تجمعی خط مبنا **۱۶ → ۱۰** است. از هفت درخواست، شش ردیف نرم‌افزاری R01/R02/R03/R04/R06/R07 پذیرفته‌اند؛ R05 هنوز باز است. writer عمومی ثالث DHCP پیدا و با hash/خط ثبت شد، اما UDP ثالث یا ACK مصنوعی شاهد اجرای COM همان firmware نیست؛ این پیشرفت تحقیق شمارنده را کاهش نمی‌دهد. پذیرش مدل واقعی هر شش قابلیت همچنان در H01 است. معیار بستن port شاهد مسیر write صریح در SDK همراه نرم‌افزار کامل است، نه ادعای شناخت firmware نصب؛ همان تفکیک نرم‌افزار/مدل برای سایر ردیف‌ها برقرار است.

## تکمیل قرارداد نرم‌افزاری DHCP — 5e1f332

[SQL۹۹۱](https://github.com/alirezarahimy-code/IO/actions/runs/43621817180)، [Windows/Linux](https://github.com/alirezarahimy-code/IO/actions/runs/43621817209) و [light شامل ممیزی چهار blob](https://github.com/alirezarahimy-code/IO/actions/runs/43621817192) روی همین SHA همگی success شدند. [شاهد تازه](s11-dhcp-write-contract.fa.md) برخلاف forwarding نام دلخواه، گزینهٔ شناخته‌شده DHCP با دامنه0..1، handler فرمان12 و ذخیره/بارگذاری در سورس تاریخی firmware را ثبت می‌کند؛ سربرگ ZKSoftware است ولی انتشار رسمی اصالت‌سنجی‌شده نیست. همان mapper تولیدی نیز با۲۹ تست تازهٔ double، هر دو جهت و خطا/unknown و inspection را پوشش می‌دهد؛ جمع profile۱۲۹ شد. افزایش تست به‌تنهایی مبنای بستن نیست؛ خلأ نگاشت نرم‌افزاری با شاهد منبع برطرف شد.

**R05 نرم‌افزاری بسته و کاهش این برش۱۰→۹؛ کاهش تجمعی۱۶→۹ است.** اکنون هر هفت R01–R07 مسیر کامل و قرارداد نرم‌افزاری پذیرفته دارند. نبود تأیید firmware نصب، COM واقعی، اعمال DHCP/lease و بازیابی شبکه ناپدید نشده و مانند شش مورد دیگر در H01 باز است. این بسته‌شدن ادعای target-firmware support از روی سورس تاریخی، readback فایل یا ACK نیست؛ اجرا همچنان فقط برای قرارداد/approval دقیق مدل نصب و حقوق مستقل مجاز است. بندهای R05 باز در بخش‌های بالا تاریخچهٔ6e8f9d9/85347c2 هستند.

## شروع مرتب۹ مانده — زیرکار R08، بدون کاهش شمارنده

[برش قرارداد wire ATTLOG](s10-push-attlog-contract.fa.md) و۴۷ بررسی codec ساخته شد؛ هنوز authentication، endpoint عملیاتی و SQL journal ندارد. این زیرکار **R08 را نمی‌بندد**؛ جمع۹ فنی + H01 است. جزئیات کلید نشست در مرجع عمومی به کتابخانهٔ vendor ارجاع شده و از روی SN، Stamp یا CRC32 حدس زده نمی‌شود.

### ادامهٔ R08: شواهد SQL مستقل، نه بستن ردیف

پس از واگذاری انتخاب به پیشنهاد عامل، [مسیر محافظه‌کارانهٔ journal](s10-push-journal-contract.fa.md) انتخاب شد: دریافت عمومی غیرفعال؛ نگهداری داخلی شواهد با حق و scope مستقل، رسید فقط پس از commit، بدون ACK vendor یا ثبت تردد. migration۴۸ و۲۴ بررسی SQL اضافه شده‌اند؛ موفقیت دقیق CI باید جدا تأیید شود. جمع۹ فنی + H01 تغییر نکرده است.

پذیرش محدود journal روی `b1bfee7`: SQL۱۰۱۵ و Windows/Linux موفق؛ [شاهد](s10-push-journal-evidence.json). journal داخلی تکمیل شده، اما R08 به علت باقی‌ماندن احراز هویت و receiver/پردازش/ACK همچنان باز است؛ شمار۹ فنی + H01 ثابت است.

### ادامهٔ R08: ابزار بازبینی فقط‌خواندنی

[فهرست/سطرها/اصل پیام و نگاشت فعلی](s10-push-review-contract.fa.md) با حق مستقل خواندن و scope دستگاه افزوده شد.۱۶ بررسی SQL/HTTPS جدید،۱۹ فایل DOM و۱۹ policy؛ پذیرش remote جدید باید جدا ثبت شود. بدون POST، بدون دستگاه احرازشده، بدون ثبت تردد یا ACK؛ R08 باز و جمع۹ فنی + H01 ثابت است.

پذیرش محدود ابزار review روی `adcb551`: SQL/HTTPS۱۰۳۱ و Windows/Linux موفق؛ [شاهد](s10-push-review-evidence.json). فهرست، سطرها، اصل پیام و قرنطینهٔ خواندنی قابل استفاده‌اند؛ احراز هویت دستگاه، receiver و processing/ACK هنوز مانده‌اند. جمع۹ فنی + H01 تغییر نکرده است.

### تغییر سیاست تست به درخواست کاربر

[CI خودکار فقط Windows](ci-windows-only.fa.md): matrix لینوکس حذف و light به Windows منتقل شد؛ SQL لینوکسی خودکار متوقف و اجرای دستی آن نیازمند opt-in پیش‌فرض false است. SQL۱۰۳۱ روی adcb551 شاهد تاریخی باقی می‌ماند، نه تست تازه. این صرفه‌جویی پوشش جدید Linux/SQL را ادعا نمی‌کند و شمار۹ فنی + H01 را تغییر نمی‌دهد.

### R08/R09 — پیشنهاد انتخاب‌شده و برش مستقل جدید

با واگذاری انتخاب به عامل، جهت gateway احرازشده (مرزی جدا از native authentication) و RemoteEnroll صرفاً مصنوعی تا تعیین policy انتخاب شدند. [کد و حدود دقیق](s10-gateway-and-s09-enroll-contracts.fa.md): HMAC/binding/commit boundary و lifecycle یک‌dispatch با اثبات حضور و unknown/cancel؛ شاهد امضای۳ متد از۲ DLL. این‌ها هنوز production store/receiver یا RemoteEnroll صف/bridge/API نیستند؛ policy واقعی تصویب نشده است. هیچ ردیف بسته نمی‌شود؛۹ فنی + H01 ثابت است.

تأیید برش مستقل روی `5dae6ad`:۴۲ قرارداد مصنوعی Windows،۲۱ policy و auditor دو TypeLib موفق؛ [شاهد](s10-gateway-s09-enroll-software-evidence.json). این نتیجه به معنی تکمیل عملیاتی PUSH/RemoteEnroll نیست؛ store/receiver و مسیر یکپارچهٔ enrollment و policy واقعی باقی‌اند. هیچ کاهش شمارنده‌ای انجام نشد.

### R08/R09 — اتصال نرم‌افزاری عملیاتی، با حدود پذیرش روشن

کد `248cbcdf45f40ea9c7d4b2527be7af1b7cdf3843` روی [Windows/LocalDB43640642835](https://github.com/alirezarahimy-code/IO/actions/runs/43640642835) و [light43640642847](https://github.com/alirezarahimy-code/IO/actions/runs/43640642847) success شد. **۳۹ بررسی SQL/HTTPS/courier واقعی/صف و worker با child مصنوعی،۴۷ قرارداد/مرز process،۲۰ فایل DOM و۲۱ policy**. این برش واقعاً SQL commit store، provisioning/revocation، machine ingress پیش‌فرض خاموش، courier با TLS عادی، صف/API/UI و worker و adapter STA enrollment را وصل می‌کند؛ دیگر صرفاً interface و double مستقل نیست. migrationهای۴۹ و۵۰ اضافه شدند.

[راهنمای نصب و محدودیت‌ها](gateway-enrollment-operations.fa.md)، [شاهد exact-SHA](gateway-enrollment-operational-evidence.json). fixture خصوصی TLS فقط روی runner موقت Windows است و گواهی trust آزمایشی را پاک می‌کند؛ courier گزینهٔ insecure ندارد. Linux یا suite کامل تاریخی SQL دوباره اجرا نشده‌اند؛ SQL۱۰۳۱ رویadcb551 شاهد تاریخی می‌ماند.

**کاهش شمارنده انجام نشد:۹ فنی + H01.** courier ورودی فایل spool می‌گیرد، نه native listener؛ journal/receipt نه ثبت تردد است و نه vendor OK:n. ثبت واقعی اثرانگشت نیز به policy و همان مدل مصوب نیاز دارد؛ default-off حفظ شده است. unknown عمداً نوشتن‌های بعدی را می‌بندد و API بازکردن حدسی/پاک‌کردن آن ساخته نشده است. COM و firmware واقعی، capture فرد واقعی و بازیابی حضوری آزمایش نشده‌اند. موفقیت۳۹ تست جدید مجوز حذف این مانع‌ها یا اعلام «هر دو روی دستگاه واقعی تمام شدند» نیست.


## پذیرش نرم‌افزاری R08 و R09؛ ادامه از R10

برش `cffed82` با [Windows LocalDB/HTTPS/collector/صف/worker/بازیابی: ۶۳ بررسی](https://github.com/alirezarahimy-code/IO/actions/runs/43643001807) و۴۷ قرارداد/child پذیرفته شد. [UI ویندوز](https://github.com/alirezarahimy-code/IO/actions/runs/43642684493) روی `794be8f` موفق است؛ تمام فایل‌های مرتبط UI/DOM/checker با برش پذیرفته‌شده بایت‌به‌بایت یکسان‌اند. [شواهد و محدودیت‌ها](native-push-reviewed-recovery-evidence.json).

| شناسهٔ نرم‌افزاری پذیرفته‌شده | مسیر تکمیل‌شده | ماندهٔ استقرار در H01 |
|---|---|---|
| P3-R08 | جهت gateway منتخب: collector محلی محدود، spool بادوام، HTTPS/HMAC/SQL، ACK پس از receipt، پذیرش صریح raw با نگاشت/نسخه/اعتماد/رفع تکرار و audit تغییرناپذیر | IP/SN احراز هویت نیست؛ LAN و مدل/گزینه‌ها/ACK باید مصوب و دستگاه واقعی آزموده شود؛ native device authentication ادعا نمی‌شود |
| P3-R09 | صف تک‌اجرا/worker/bridge و UI پیش‌فرض خاموش، کنترل no-overwrite، و بازیابی فیزیکی دو نفره بدون تغییر unknown یا بازپخش | مدل و مجوز/نگهداری زیست‌سنجی، شاهد رویهٔ reset غیرحذفی و مشاهدهٔ حضوری اسلات؛ ماشین صحت این شواهد انسانی را خودکار اثبات نمی‌کند |

کاهش۹→۷ صرفاً برای **مسیر نرم‌افزاری انتخاب‌شده** است؛ endpoint خالی یا pending دلیل بستن نیست. مسیرها ساخته و در ویندوز آزموده شده‌اند. فعال‌سازی مدل نامعلوم، مجوز زیست‌سنجی، native authentication یا پذیرش دستگاه واقعی نتیجه نمی‌شود. H01 باز و part3Complete=false است؛ ترتیب بعدی R10→R16 محفوظ است. بخش‌های قبلی این سند تاریخچهٔ وضعیت همان SHAها هستند، نه وضعیت جاری.


## ادامهٔ مرتب — R10 در حال اجرا، بدون کاهش شمارنده

[برش اول انتقال اثرانگشت/چهره](r10-biometric-transfer.fa.md) روی `5a64085` با۳۱ بررسی قرارداد مصنوعی،۱۹ بررسی جدید SQL/worker (جمع یکپارچه۸۲) و۲۱ فایل DOM در Windows پذیرفته شد؛ [شاهد](r10-biometric-transfer-evidence.json). template در خروجی bridge/وب/SQL نگهداری نمی‌شود. عکس و بازیابی اختصاصی unknown هنوز پیاده نشده‌اند؛ R10 بسته نیست. ترتیب بعدی: تکمیل همین دو زیرکار و پذیرش R10، سپس R11→R16؛ شمار۷ + H01 ثابت است. هیچ تست Linux یا اجرای مجدد suite کامل تاریخی SQL در این برش انجام نشد.

## پذیرش نرم‌افزاری R10 — 22105bd؛ کاهش۷→۶

تبادل مستقیم اثرانگشت/چهره/عکس، عکس با نام ثابت و پروفایل JPEG محدود در staging خصوصی EFS روی volume کوچک NTFS، پاک‌سازی پس از خروج/kill و orphan، و بازیابی **اختصاصی** unknown با صف مشاهدهٔ فقط خواندنی و دو امضای مستقل اکنون مسیر کامل کد/SQL/UI/worker/bridge دارند. ردیف اصلی unknown تغییر نمی‌کند؛ replay، requeue و overwrite وجود ندارد. درخواست‌های عادیِ برش قبل نیز serialization سازگار حفظ می‌کنند.

کد `22105bd` روی [Windows](https://github.com/alirezarahimy-code/IO/actions/runs/43695380624) با۱۰۰ بررسی یکپارچهٔ SQL/HTTPS/صف/worker،۶۳ قرارداد/فرایند زیست‌سنجی مصنوعی،۴۷ قرارداد قبلی و EFS واقعی روی volume دورریختنی؛ و [light/UI](https://github.com/alirezarahimy-code/IO/actions/runs/43695380618) موفق شد. [شاهد قابل ردیابی](r10-photo-recovery-evidence.json)، [راهنمای نصب/بازیابی و محدودیت‌ها](r10-photo-recovery.fa.md). آزمون SDK/COM/دادهٔ زیست‌سنجی واقعی انجام نشده؛ EFS تضمین secure erase نیست. مجوز واقعی، قالب/مدل، کنترل انحصاری و provision کلید/volume حساب سرویس همچنان پیش‌شرط فعال‌سازی‌اند؛ پیش‌فرض خاموش حفظ شده است. Linux و suite کامل تاریخی SQL۱۰۳۱ دوباره اجرا نشدند.

**P3-R10 نرم‌افزاری بسته شد؛ ماندهٔ جاری۶ ردیف R11–R16 + بستهٔ مستقل H01 است.** موارد «R10 باز» در قسمت‌های تاریخی بالا متعلق به برش اول‌اند. ادامه به‌ترتیب از R11 است: حذف همهٔ تردد دستگاه با مجوز مستقل، اخذ durable و مرز توقف capture/بازیابی؛ `ClearData(machine,1)` مرجع با پاکسازی همهٔ داده‌ها (flag5) اشتباه نشود. این شروع مرحلهٔ بعد، مجوز اجرای حذف روی دستگاه واقعی نیست.

## پذیرش نرم‌افزاری R11 — d2c5d95؛ کاهش۶→۵

مسیر حذف **همهٔ تردد دستگاه** با فرمان ثابت `ClearData(machine,1)` تکمیل شد؛ flag5، factory reset و حذف بازه/کاربر/مدیر در این مسیر نیستند. آرشیو کامل و تغییرناپذیر tupleهای native، توقف فیزیکیِ capture به‌عنوان پیش‌شرط مستقل مصوب، دو شخص مستقل، دو بار تطبیق multiset زنده با حفظ duplicateها، یک‌بار clear و read-back کاملِ خالی لازم‌اند. ACK صرف موفقیت نیست؛ unknown هرگز requeue نمی‌شود. بازیابی اختصاصی با reset غیرحذفی، آرشیو خواندنی تازه و امضای مستقل، فقط fence را رفع می‌کند و original unknown ثابت می‌ماند. رابط/CSRF/نسخه و worker واقعی نیز به مسیر وصل‌اند.

کد `d2c5d95` با [Windows۲۰۲۲](https://github.com/alirezarahimy-code/IO/actions/runs/43697672956) موفق شد:۱۲۹ بررسی SQL/HTTPS/صف/worker، شامل۲۹ بررسی جدید R11 (۲۳ صف/SQL/worker و۶ HTTPS با ورود واقعی) و۲۳ قرارداد مصنوعی جدید حذف. ۶۳ قرارداد زیست‌سنجی و۴۷ قرارداد قبلی نیز سبز ماندند. [آزمون light/UI با۲۲ فایل DOM](https://github.com/alirezarahimy-code/IO/actions/runs/43697294069) روی محتوای UI و آزمون‌های بدون تغییر اجرا شد؛ تفاوت صفرِ این فایل‌ها با نسخهٔ compiled در [شاهد](r11-attendance-erase-evidence.json) بررسی شده است. تست HTTP نگاشت فراموش‌شدهٔ HTML را پیدا کرد؛ مسیر واقعی اصلاح و تست نگه داشته شد، نه تضعیف.

[راهنمای R11 و پیش‌شرط نصب](r11-attendance-erase.fa.md). **R11 نرم‌افزاری بسته شد؛ ماندهٔ جاری۵ ردیف R12–R16 + H01 است.** توقف capture از شمار ثابت یا mutex اثبات نمی‌شود؛ اگر در نصب قابل تأیید نیست، enable باید false بماند. SDK/COM، حذف واقعی یا سیاست واقعی سازمان آزموده/تصویب نشده‌اند. Linux، suite کامل تاریخی SQL۱۰۳۱ و rollback کامل تازه اجرا نشدند. ادامه به‌ترتیب از R12 و مرز زمانی دقیق آن است؛ بقیهٔ بخش۳ هنوز کامل نیست.

## پذیرش نرم‌افزاری R12 — 7787f65؛ کاهش۵→۴

مسیر مستقل حذف بازه با `DeleteAttlogBetweenTheDate(machine,from,through)`، قرارداد بستهٔ **ثانیهٔ ساعت محلی دستگاه** و تأیید مستقل مرز/دقت مدل نصب‌شده ساخته شد. بازه UTC نیست؛ هر دو occurrence ساعت تکراری عمداً انتخاب می‌شوند، legacy فقط دقیقهٔ کامل است. مجوز، allowlist، switch، API و state موفق R12 از حذف همه جدا هستند؛ خانوادهٔ دیگر نمی‌تواند قصد/نتیجه/بازیابی را retarget کند. با آرشیو کامل و حفظ duplicateها، دو تطبیق کل log و یک dispatch، فقط readback دقیق **تمام multiset خارج بازه** نتیجهٔ `range-deleted-verified` می‌دهد. unknown با بازیابی اختصاصیِ دو شخص و آرشیو پس از reset فقط fence را رفع می‌کند؛ اصل unknown تغییر یا replay نمی‌شود.

کد `7787f650f04dfe689b8c4234c6b7fbd5a7dab63c` روی [Windows۲۰۲۲](https://github.com/alirezarahimy-code/IO/actions/runs/43703583328) با۱۷۵ بررسی یکپارچهٔ SQL/HTTPS/صف/worker، شامل۴۶ بررسی جدید R12 (۴۰ SQL/worker و۶ HTTPS واقعی)،۳۴ قرارداد مصنوعی جدید بازه،۲۳ قرارداد حذف همه،۶۳ قرارداد بیومتریک/EFS و۴۷ قرارداد قبلی موفق شد. [light/UI با۲۳ فایل DOM و Chromium](https://github.com/alirezarahimy-code/IO/actions/runs/43702837985) روی55d5a47 موفق است؛ یکسانی بایت‌های UI/DOM/workflow با کد نهایی در [شاهد](r12-attendance-range-erase-evidence.json) بررسی شده است. اجرای SQL ایراد batch migration را یافت؛ فرمان افزودن ستون از constraintها داخل همان تراکنش جدا شد، بدون حذف assertionها. failure نخستِ LocalDB و failure migration، پذیرفته‌شده معرفی نمی‌شوند.

[راهنمای قرارداد/نصب/بازیابی R12](r12-attendance-range-erase.fa.md). **R12 نرم‌افزاری بسته شد؛ ماندهٔ جاری۴ ردیف R13–R16 + H01 است.** نوع‌کتابخانه و قالب رشتهٔ مرجع اثبات معنای واقعی مرز firmware نیستند؛ شاهد نصب، سیاست واقعی سازمان و توقف capture همچنان پیش‌شرط‌اند و اجرای واقعی default-off است. SDK/COM یا حذف واقعی روی دستگاه اجرا نشده؛ Linux، suite کامل تاریخی SQL۱۰۳۱ و suite کامل rollback تکرار نشدند. ادامه به‌ترتیب از R13 حذف کاربر/قالب است، نه اعلام تکمیل بخش۳.

## R13 — برش اول هسته پذیرفته، بدون کاهش شمارنده

روی `685485067012440dc93e19104dafb1e5e7f2dae5`، [Windows43708241242](https://github.com/alirezarahimy-code/IO/actions/runs/43708241242) با۴۸ بررسی مصنوعی جدید هسته/mapper تولیدی/child supervisor موفق شد؛۱۷۵ بررسی یکپارچهٔ **قبلی** نیز regression سبز ماندند، نه اینکه۱۷۵ بررسی SQL عملیاتی R13 ساخته شده باشد. [light43708241251](https://github.com/alirezarahimy-code/IO/actions/runs/43708241251) نیز موفق است؛ چون UI عوض نشده، DOM/Chromium در این اجرای docs-only scope تکرار نشده‌اند. [شاهد دقیق](r13-user-erase-core-evidence.json)، [قرارداد و ماندهٔ R13](r13-user-erase-core.fa.md).

کد۱۳ SSR و۱۱ legacy برای fingerprints جدا و کد۱۲ user ثابت است؛ تبدیل `001` به `1`، fallback، حذف هدف administrator و پروفایل دارای store اثبات‌نشده مجاز نیست. دو snapshot کامل و transient از metadata/رمز/ده اسلات همهٔ کاربران، یک فراخوانی حذف و تطبیق کامل خارج هدف لازم‌اند؛ رمز/template از child سریال، hash یا persist نمی‌شوند. خواندن material خام فقط به profile و مجوز صریح مستقل آینده وابسته است، نه مجوز توسعهٔ مجازی. نبود کاربر به‌تنهایی orphan fingerprint را پنهان نمی‌کند.

**R13 هنوز باز، شمار۴ + H01 ثابت است.** هیچ dispatcher تولیدی در bridge/worker یا endpoint وب افزوده نشده؛ برابری بایت‌های این مسیرها نسبت به۲۷۱۲۷۹۹ در شاهد بررسی شد. ادامهٔ همین ردیف: آرشیو metadata، صف immutable، preview/دو امضای مستقل، fence مشترک، بازیابی read-only پس از reset و اتصال worker/bridge/API/UI با پذیرش یکپارچهٔ Windows؛ سپس تعیین‌تکلیف روشن face/photo/door profiles و بررسی بستن نرم‌افزاری. هیچ SDK/COM، دادهٔ بیومتریک واقعی، حذف واقعی، Linux یا suite کامل SQL۱۰۳۱ اجرا نشده است. R14 هنوز شروع نمی‌شود.

## پذیرش نرم‌افزاری R13 و سپس R14 — 740dca2؛ کاهش۴→۲

R13 اکنون برای دو خانوادهٔ مستقل users و fingerprints مسیر کامل دارد: archive metadata کامل، preview متصل به همهٔ metadata، حق/allowlist جدا، دو شخص مستقل، صف immutable، fence مشترک native، worker/bridge/API/UI و readback کامل transient رمز/کارت/ده اسلات همهٔ افراد. کد۱۳ SSR و۱۱ legacy یکسان فرض نشده‌اند؛ هدف administrator در R13 رد می‌شود. سایر storeهای face/photo/palm/vein/door/hidden و حذف تک‌اسلات در این نسخه **unsupported/default-off** تعیین‌تکلیف شدند، نه پشتیبانی‌شده از روی فقدان دستگاه. این بستن فقط برای profile مصوب fingerprint-only است؛ پشتیبانی profile تازه قرارداد و پذیرش نرم‌افزاری تازه می‌خواهد.

R14 بعد از R13 در همان آزمون اجرا شد: فقط `ClearAdministrators(machine)` و تنزل privilege تمام کاربران به۰، با حفظ دقیق همهٔ افراد/رمزها/کارت‌ها/templateها؛ نه حذف اشخاص مدیر، ClearData یا factory reset. مجوز و effect approval آن مستقل است. مدل با اثر متفاوت unsupported و خاموش می‌ماند. unknown هر خانواده فقط با reset غیرحذفی، archive تازه، inspection واقعی خواندنی و امضای مستقل fence-release رفع مانع می‌شود؛ original unknown تغییر نمی‌کند. observation منقضی/کنارگذاشته‌شده با cancel نسخه‌دار به inspection-failed می‌رود، نه release؛ سپس observation تازه لازم است. هیچ فرمان حذف بازپخش نمی‌شود.

کد `740dca2f0cb52845bbd04d28d233eea146d4a82f` روی [Windows/LocalDB](https://github.com/alirezarahimy-code/IO/actions/runs/43710921010) با۲۹۶ بررسی یکپارچه موفق شد:۱۲۱ جدید (۱۰۳ SQL/صف/worker و۱۸ HTTPS واقعی)،۱۷۵ regression قبلی؛۳۴ قرارداد مصنوعی جدید protected،۴۸ هستهٔ R13،۳۴ بازه،۲۳ حذف همه،۶۳ بیومتریک/EFS و۴۷ قبلی نیز موفق‌اند. [light/UI همان SHA](https://github.com/alirezarahimy-code/IO/actions/runs/43710921014) شامل۲۴ فایل DOM و preview مستقل Chromium سبز است؛ matcher خودکار همهٔ فایل‌های DOM را اجرا می‌کند، workflow دیگری برای فایل جدید لازم نبود. [شاهد دقیق SHA/job و شمارها](r13-r14-protected-erases-evidence.json)، [راهنمای نصب/دامنه/بازیابی](r13-r14-protected-erases.fa.md).

**R13 و R14 نرم‌افزاری بسته شدند؛ ماندهٔ جاری۲ ردیف R15/R16 + H01 است.** متن‌های R13 باز/R14 شروع‌نشده در قسمت‌های بالاتر تاریخی‌اند. SDK/COM، حذف واقعی، اثر واقعی ClearAdministrators، سیاست واقعی سازمان و کنترل انحصاری فیزیکی آزموده/تصویب نشده‌اند؛ switchها همچنان default-off. Linux، suite کامل تاریخی SQL۱۰۳۱ و rollback کامل دوباره اجرا نشدند. بخش۳ کامل نیست؛ ادامه به ترتیب R15، R16 و H01 واقعی در انتهاست.

## پایان مرتب R15 و تعیین‌تکلیف R16 — کاهش۲→۱→۰؛ H01 همچنان باز

R15 ابتدا روی `f18374b` با [Windows342](https://github.com/alirezarahimy-code/IO/actions/runs/43712687348) و [UI](https://github.com/alirezarahimy-code/IO/actions/runs/43712687596) موفق شد؛ سپس R16 و regression نهایی روی `b6791a58991603d2c099414b3e1ac9a8d6e17d53` با [Windows355](https://github.com/alirezarahimy-code/IO/actions/runs/43713356207) و [UI همان SHA](https://github.com/alirezarahimy-code/IO/actions/runs/43713356176) پذیرفته شدند.۵۹ بررسی یکپارچهٔ جدید =۴۶ R15 (۳۹ SQL/worker و۷ HTTPS) +۱۳ R16 (۱۱ SQL و۲ HTTPS)؛۴۸ قرارداد مصنوعی جدید R15،۲۶ مرز پشتیبانی R16 و regressionهای قبلی نیز موفق‌اند.۲۶ فایل DOM و preview Chromium در Windows سبز است. [شاهد دقیق](r15-r16-software-evidence.json).

**R15: مسیر مستقل ClearData(machine,5) ساخته شد، نه factory reset.** دو archive کامل کاربران و تردد با حفظ native duplicateها، preview و دو انسان، صف immutable مستقل و fence تمام native queues، worker/STA bridge/API/UI، دو preflight کامل و یک dispatch؛ پس از اتصال خواندنی با همان IP/port/key، خالی‌بودن کل کاربران/تردد و ده orphan probe هر فردِ قبل لازم است. فقط profile مصوب fingerprint-only/فاقد hidden store؛ unknown بدون replay و با reset غیرحذفی/دو archive تازه/observation فقط‌خواندنی و release مستقل همان خانواده. [راهنما](r15-all-data-clear.fa.md). factory reset صریحاً unsupported و ورودیِ ادعاکنندهٔ آن رد می‌شود؛ حفظ تمام تنظیمات یا secure erase ادعا نشده است.

**R16: تعیین‌تکلیف unsupported-in-this-delivery، نه ساخت اتصال مستقیم.** Connect_USB و Connect_Com واقعاً در باینری مرجع و دو TypeLib وجود دارند؛ COM1/115200 مرجع، انتخاب/مالکیت امن endpoint نصب نیست. نسخهٔ جاری فقط شبکهٔ مصوب و فایل با قالب صریح عرضه می‌کند؛ direct USB/serial adapter/driver/port ownership ساخته نشده و با فایل یا IP ساختگی پنهان نمی‌شود. قرارداد machine-readable، GET مجوزدار `/devices/transport-support`، منع profile/hostname محلی و تزریق Transport، UI/help و آزمون‌ها اضافه شدند. علت/دامنهٔ انتخاب محافظه‌کارانه و الزامات برش نرم‌افزاری تازه در [تصمیم R16](r16-direct-transport-disposition.fa.md) است، نه ادعای ناتوانی SDK یا نبود دستگاه واقعی.

**۰ ردیف فنی حل‌نشده در این دامنه +۱ بستهٔ واقعی H01.** softwareComplete فقط همین دامنه و dispositionهای صریح است؛ fullLegacyFeatureParity=false، hardwareVerified=false و part3Complete=false. H01 از simulator/CI نتیجه نمی‌شود؛ [بستهٔ تحویل آزمایشگاه](h01-final-windows-lab-handoff.fa.md). هیچ SDK/COM، حذف واقعی یا اتصال مستقیم اجرا نشده؛ مجوز واقعی سازمان، کنترل همهٔ کانال‌ها و اثر مدل هنوز احراز نشده و default-off حفظ شده است. Linux، suite کامل تاریخی SQL۱۰۳۱ و rollback کامل دوباره اجرا نشدند. عددهای قبلی۲/۴/… در ادامه‌های تاریخی، وضعیت همان SHAها هستند.
