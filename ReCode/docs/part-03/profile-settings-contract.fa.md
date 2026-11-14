# قرارداد هفت تنظیم مصوب — P3-R01 تا P3-R07

این سند ادعای پذیرش دستگاه یا پایان بخش۳ نیست. واحد شمارش همان ledger ثابت است؛ اضافه‌شدن adapter، مجوز، کنترل پیش‌فرض خاموش یا تعداد تست، ردیفی را نمی‌بندد.

## مسیر اجرا

اپراتور دارای `devices.settings.read` ← snapshot موفق SQL کمتر از پنج دقیقه با `ProfileReadApprovals` نصب ← انتخاب مقدار کامل قدیم ← پیش‌نمایش سرور از مدل/firmware/zone/نسخه و endpoint واقعی ثبت‌شده ← تأیید صریح plan/hash ← قصد immutable در SQL ← سرویس مستقل با حق پارامتر ← allowlist نصب worker ← child ایزوله ← تطبیق serial/product/firmware و تمام حالت قدیم ← یک dispatch.

کاربر نمی‌تواند با preview مجوز نصب را ایجاد کند. هیچ permission جدیدی به template نقش‌ها خودکار داده نشده است. برای هر یک از `daylight`, `algorithm`, `mask`, `gateway`, `dhcp`, `mac`, `port`، دو حق مستقل `devices.profile.{parameter}.set` و `devices.profile.{parameter}.execute` وجود دارد. سرویس همچنین به `devices.operations.execute` نیاز دارد. مشاهده مستقل از نوشتن است؛ inspection به `settings.read.execute` و approval دقیق نصب نیاز دارد ولی flag نوشتن لازم ندارد.

### قرارداد نصب `NativeEndpoint`

- خواندن: `AllowAdvancedSettingsRead` و `ProfileReadApprovals` با محصول، firmware، پارامتر و مرجع شاهد دقیق.
- نوشتن: `AllowProfileWrite` و `ProfileWriteApprovals` با محصول، firmware، zone سایت، پارامتر، دو حالت کامل در `ApprovedStates`، اثر در `Effect`، مرجع در `EvidenceReference` و مقصدهای literal در `ApprovedEndpoints`.
- `EffectsReviewed=true` فقط بیان تأیید نصاب است؛ خودِ SDK یا سازگاری مدل را ثابت نمی‌کند.
- برای الگوریتم، `TemplatesPreserved=true` باید بر شاهد سازگاری/حفظ template در همان مدل تکیه کند. هیچ انتقال، تبدیل یا حذف بیومتریک در این عملیات مجاز نیست.
- برای DLST، `AnnualInterpretation` باید تفسیر سالانه و zone را با شاهد مدل توضیح دهد. قالب هشت‌رقمی `MMddHHmm` امضای همین DLL است، نه سیاست مدل برای نیمکره/سال یا زمان متقاطع.
- نمونهٔ approval ساختگی نباید به نصب واقعی کپی شود؛ fixtureها عمداً `synthetic` نام دارند.

## نگاشت و حدود شاهد

| ردیف | پارامتر | مسیر بسته | آنچه هنوز معادل پذیرش نیست |
|---|---|---|---|
| P3-R01 | `daylight` | `GetDaylight`/`SetDaylight` همراه `DLSTMode`, `CurTimeMode`, `DaylightSavingTimeOn` | تفسیر سالانه، firmware و رفتار سخت‌افزار |
| P3-R02 | `algorithm` | `GetSysOption`/`SetSysOption`, `~ZKFPVersion`؛ مرجع PersonnelZKDevice | حفظ template و اثر مدل واقعی |
| P3-R03 | `mask` | `NetMask`؛ setter مرجع PersonnelZKDevice | اثر روی اتصال و مدل واقعی |
| P3-R04 | `gateway` | `GATEIPAddress`؛ setter مرجع PersonnelZKDevice | اثر روی اتصال و مدل واقعی |
| P3-R05 | `dhcp` | `DHCP` کاندید، مدل‌مقید | شاهد کافی برای write از COM نداریم؛ رشته/USB config اثبات نیست |
| P3-R06 | `mac` | `GetDeviceMAC`/`SetDeviceMAC` و `RefreshData` | رفتار مدل واقعی و بازیابی شبکه |
| P3-R07 | `port` | `TCPPort` کاندید؛ نه `CommPort` سریال | رشته DLL/read option اثبات SetSysOption مدل نیست |

DLST فقط `SetDaylight` را صدا می‌زند؛ اثر native تغییر Mode در فعال‌سازی و CurrentMode در غیرفعال‌سازی در حالت مطلوب پیش‌نمایش الزام شده است. adapter تنظیمات جانبی را جداگانه و کور نمی‌نویسد. برای غیرشبکه فقط readback دقیق تمام حالت و هویت، موفقیت است. برای تمام پارامترهای شبکه، حتی ACK مثبت `unknown` است.

## قطع ارتباط و بازبینی

۱. هیچ retry خودکار write، اسکن شبکه، restart، disable/enable یا تغییر metadata دستگاه وجود ندارد.
۲. در پاسخ ایجاد نامعلوم، UI همان UUID و بدنه را نگه می‌دارد؛ replay همان قصد dispatch جدید نمی‌سازد.
۳. `settings.profile.inspect` فقط برای parent همان دستگاه، نسخه، مدل، plan و `unknown` حل‌نشده پذیرفته می‌شود. مقصد اصلی یا بازیابی باید از قبل در allowlist نصب باشد. برای DHCP مقصد ثابت/رزرو صریح لازم است.
۴. child جدید فقط می‌خواند و هویت و تمام حالت قدیم یا جدید را تأیید می‌کند. ping یا یک فیلد منفرد کافی نیست.
۵. بازبینی صریح `REVIEWED-EXACT-PROFILE` شاهد inspection را ثبت می‌کند، ولی نتیجهٔ تاریخی `unknown` را به موفقیت تبدیل نمی‌کند و write را تکرار نمی‌کند.
۶. لغو فقط pending؛ write همزمان با صف استخراج/تبادل یا schedule فعال یا unknown حل‌نشده رد می‌شود. downgrade با سابقهٔ profile مجاز نیست.

## شواهد نرم‌افزاری

- `ProfileSettingTests`: ۱۰۰ بررسی قابل‌حمل، شامل هر هفت dispatch، preflight، اثر جانبی، false ACK/exception، readback، inspection و child واقعی با session ساختگی.
- `device-profiles-ui-check.cjs`: هفت جریان DOM، منبع SQL، preview، تأیید checkbox، replay همان UUID پس از پاسخ نامعلوم، متن امن و پاک‌سازی پس از revoke؛ fetch mock است، SQL نیست.
- `DeviceProfileSettingTests`: آزمون یکپارچه SQL برای هر هفت پارامتر، مجوز مستقل، لغو زنده، trigger immutable، نتیجه و بازبینی. تا CI موفق دقیق SHA، پذیرفته محسوب نمی‌شود.
- `VirtualWorkerTests`: مسیر HTTPS/SQL/worker/child/TCP خصوصی stateful و ACK ازدست‌رفته؛ تغییر option در fixture جابه‌جایی شبکهٔ واقعی نیست و جای SDK نیست.

وضعیت اجرایی و SHA/CI در گزارش پیشرفت ثبت می‌شود؛ SDK ویندوز/دستگاه واقعی طبق تصمیم قبلی در انتها، و خلأ شاهد COM دو گزینه DHCP/TCPPort جداگانه باز است.

## پذیرش نرم‌افزار ثبت‌شده

`6e8f9d9`: SQL۹۹۱ و Windows/Linux همان SHA موفق؛ [شاهد دقیق](s11-profile-settings-evidence.json). پنج شناسه R01/R02/R03/R04/R06 بستهٔ نرم‌افزاری شدند، نه هفت. R05/R07 هنوز شاهد کافی writable COM ندارند؛ H01 واقعی باز است. حداکثر ورودی Unicode ترکیب همه خواندن‌ها اکنون با stdin محدود32KB عبور می‌کند؛ سقف خروجی settings و نتیجهٔ SQL افزایش نیافته و round-trip دقیق آزموده شده است.
