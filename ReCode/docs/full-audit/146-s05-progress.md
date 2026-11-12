# ادامهٔ بخش سوم — S05 تا S12، گزارش پیشرفت

## دستور و مرز پایان

دستور جاری تکمیل **تمام S05 تا S12 در بخش سوم** است. این گزارش پایان بخش سوم یا حتی پذیرش نهایی S05 نیست. ساخت آداپتر واقعی و آزمون چند دستگاه/قطعی/دریافت/ارسال روی سخت‌افزار واقعی لازم است؛ شبیه‌ساز و TCP handshake جای آن را نمی‌گیرند.

## کد افزودهٔ نخستین جزء S05

- `DeviceConnections.cs`: صف پایدار درخواست‌های نسخه‌دار، retry دقیق، read/test مستقل، لغو فقط pending، claim سرویس فعال با مجوز مستقل، بررسی مجدد حق درخواست‌کننده/نسخه دستگاه، اتمام با claim token، تاریخچهٔ terminal تغییرناپذیر و recoveryِ interrupted بدون replay.
- مدل/مهاجرت `20261112020000_DeviceConnections`: یک جدول، FK/CHECK/index یک درخواست ناتمام برای دستگاه، trigger حفاظت تاریخچه، رد downgrade پس از داده. هیچ داده/منبع/رمز قدیمی تغییر نمی‌کند.
- `IO.DeviceWorker`: executable مستقل بدون COM/native در وب، چهار slot شبکه برای جداسازی دستگاه‌ها، اتصال خروجی فقط به ترکیب دقیق UUID/IP/port تأییدشده توسط تنظیمات worker. هویت بنیادین جدا و DB grant صریح؛ provisioning OS/SQL مسئولیت نصب است. بدون token یا رمز در پاسخ و log.
- `TcpConnectionDiagnostic.cs`: timeout اتصال ۱..۶۰ ثانیه، قطع socket و نتیجهٔ امن؛ بدون DNS، اسکن، رمز، payload یا فرمان پروتکلی. نتیجهٔ tcp-reachable به معنی احراز هویت دستگاه یا تأیید مدل نیست.
- `/workspace/device-connection`: فرم، تاریخچهٔ DB و لغو نسخه‌دار، لینک از موجودی دستگاه، retry ثابت هنگام قطع پاسخ، پاک‌کردن داده پس از لغو مجوز. راهنمای دقیق صفحه و [راهنمای نصب](../part-03/device-worker-setup.fa.md).
- سه مجوز مستقل `devices.connection.read/test/execute`، admin تازه read/test، operator تازه فقط read، هیچ template دارای execute نیست. نقش/حق لغوشدهٔ قبلی بازگردانده نمی‌شود. همهٔ این مجوزها سراسری‌اند، نه محدودهٔ امنیتی دستگاه منتخب.

## آزمون‌ها و وضعیت اثبات

- ۲۷ check سرویس/transport افزوده، شامل مجوز مستقل، retry، نسخه، دو claim موازی، لغو، recovery با clock کنترل‌شده، منع late result، DB history guard، مهلت بیرونی برای connector بی‌اعتنا به cancellation و عدم مسدودکردن connector مستقل، TCP روی listener لوپ‌بک خود تست، endpoint-denied و service revocation. listener محلی دستگاه واقعی نیست.
- ۴ HTTPS check برای grant/revoke روی نشست واقعی، CSRF، read جدا، retry و لغو.
- Playwright از لینک موجودی به صفحهٔ تست می‌رود، ثبت pending و لغو را بررسی می‌کند؛ موفقیت شبکه جعل نمی‌شود. DOM mockِ CSRF/retry/cancel/revoke موفق محلی است.
- gate مورد انتظار اکنون ۴۵۸ PASS، Foundation ۳۸، کاتالوگ ۹۰ permission و ۴۳ صفحه/مقاله، ۲۶ migration/۴۸ جدول است. **تا موفقیت CI این اعداد انتظارند، نه نتیجهٔ اجرا.**
- SDK محلی موجود است، اما build کامل به NU1301/SSL EOF در NuGet محدود است؛ TLS غیرفعال نشده. parser آفلاین C# و syntax JS گذشتند و جایگزین build/SQL نیستند.

## ماندهٔ واقعی، نه حذف دامنه

S05 هنوز driver پروتکل/مدل تأییدشده و آزمون واقعی اتصال/قطعی ندارد. S06..S12 همچنان باقی‌اند: اطلاعات/ظرفیت/تنظیم؛ کاربران و mapping؛ ارسال فرد/چند دستگاه با نتیجهٔ هر نفر؛ دریافت raw و dedup/قرنطینه؛ زمان‌بندی/PUSH با احراز هویت؛ صف فرمان واقعی و عملیات حساس؛ فایل/USB و پذیرش سخت‌افزار. صف این سند فقط diagnostic است، نه صف فرمان کامل S11.

برای این مراحل مدل/firmware و پروتکل، OS/architecture سرویس، دستگاه آزمایشی مجزا از تولید، مستند/مجوز SDK و نمونهٔ attlog بی‌نام لازم است. رمز در چت لازم نیست. نبود این موارد مانع ساخت اجزای مستقل نیست؛ ولی بدون آن‌ها سازگاری/قالب واقعی یا اتمام بخش سوم ادعا نمی‌شود.

## نتیجهٔ نخستین جزء و ادامهٔ ZK

جزء TCP/صف روی `b34b831` با Windows/Linux `43121684281` و SQL/HTTPS/browser `43121684354` موفق شد: ۴۵۳ PASS یکپارچه، Foundation ۳۸؛ این نتیجه به native تازه تعمیم داده نمی‌شود. نخستین build روی `dd442a0` به‌دلیل نام متغیر تکراری fixture متوقف شد و روی `c25ce61` اصلاح شد؛ هیچ guard یا assertion حذف نشد.

کاربر مسیر ZK و آزمایش Windows را انتخاب کرده و به منابع موجود ارجاع داده است؛ مدل/firmware دقیق و گزارش اجرای واقعی هنوز ارائه نشده‌اند. این بار ابزار decompiler از سورس رسمی MITِ ILSpy v9.1 در scratch، بدون NuGet، ساخته و فقط به‌صورت static روی `Personnel.DLL` اجرا شد. برخلاف ممیزی اولیه، بدنهٔ متدهای منتخب اکنون مشاهده شده است؛ این بازسازی باینری است، نه سورس اصلی. [شاهد hash/کلاس/امضا](../part-03/zk-reference-contract.json). هیچ مرجع legacy تغییر نکرده و منطق license/activation قدیمی کپی یا اجرا نمی‌شود.

`IO.ZkBridge`: فرایند STA جدا با COM CLSID مشاهده‌شده، SetCommPassword/Connect_Net/GetSerialNumber/Disconnect و سریال دقیق پین‌شده. فقط probe اتصال خواندنی؛ نه تنظیم یا دریافت/ارسال. parent ورودی محدود را از stdin می‌فرستد، نه command line؛ محیط DB/DP به child منتقل نمی‌شود، stdout/stderr سقف دارند و نتیجه فقط allowlist کد ثابت است. `sdk-serial-matched` فقط پاسخ SDK و تطبیق سریال را ثابت می‌کند، نه authentication رمزنگاری‌شده یا قابلیت همهٔ مدل‌ها. SDK مجاز باید جدا با معماری درست نصب شده باشد؛ DLL خودکار ثبت/بارگذاری یا از مرجع کپی نمی‌شود.

رمز فقط برای claim معتبر، دستگاه بدون تغییر، service grant و requester grant جاری درون سرویس unprotect می‌شود؛ عدد Int32 برای COM لازم است. نبود رمز فقط با opt-in صریح مدیر نصب به کلید پیش‌فرض قابل قبول است. web/worker برای این حالت باید key ring و application identity یکسان و صریح داشته باشند؛ worker کلید تازه نمی‌سازد. هیچ مقدار credential در response، audit یا log نیست. آزمون‌های SQLِ credential/purpose/revocation و guard downgrade افزوده شده‌اند.

۱۱ آزمون portableِ مرز فرایند محلی موفق است: IPC محدود، عدم وراثت DB config، رد خروجی جعلی، timeout بیرونی، اثبات terminate شدن child و منع اجرای جدید پس از cancellation. build محلی پروژه‌های بدون NuGet با صفر warning/error گذشت. این آزمون‌ها در هر دو OS به CI افزوده شده‌اند؛ native COM واقعی هنوز اجرا نشده است. gate جدید ۴۵۸ و migration ۲۶ صرفاً انتظار CI تازه است.

## پذیرش جزء نرم‌افزاری native و parser مرجع

کد `4d14a7f` در Windows/Linux `43124031572` و SQL/HTTPS/browser `43124031392` موفق شد: ۴۵۸ PASS یکپارچه، Foundation ۳۸ و ۱۱ تست portable مرز فرایند. [شاهد ماشینی](../part-03/s05-native-component-evidence.json). **هیچ COM/SDK واقعی در این تست‌ها اجرا نشده و پذیرش سخت‌افزار یا پایان S05 ادعا نمی‌شود.**

از قرارداد مشاهده‌شدهٔ `Personnel.USBFiles.RecordReader`، parser مستقل `PersonnelUsbPreview` در لایهٔ contract افزوده شد. تقویم Gregorian/Persian صریح است؛ UTF-8 سخت‌گیرانه، layout پنج‌ستونی صریح، حفظ صفر اول شناسه، تطبیق machine، رد DST مبهم/نامعتبر، نگه‌داشتن سطرهای تکراری برای review و منع حدس device-native event id دارد. فایل با قالب دیگر (از جمله شش ستون) خودکار به این قالب نسبت داده نمی‌شود. raw line در JSON پیش‌فرض و ToString افشا نمی‌شود. این parser هنوز endpoint/UI/مجوز import و commit DB ندارد و **S12 ساخته/پذیرفته محسوب نمی‌شود**؛ زیرساخت داخلی برای ساخت importer است، نه صفحهٔ نمایشی یا import وانمودی. ۱۹ تست pure شامل این parser و مرز فرایند محلی گذشتند؛ CI آن در commit تازه بررسی می‌شود.

## بازاجرای جاری و مرز مانده

`6c1652c` در Windows/Linux `43124834036` و SQL/HTTPS/browser `43124834043` موفق شد: **۴۵۸ PASS**، Domain ۲۶، Foundation ۳۸ و **۱۹ تست portable** مرز فرایند و parser در هر دو سیستم. [وضعیت ماشین‌خوان تمام S04 تا S12](../part-03/progress-evidence.json) صریحاً `part3Complete=false` دارد. S06 تا S11 هنوز پیاده نشده‌اند؛ S12 فقط parser داخلی دارد، نه importer کامل. این‌ها صرفاً به‌علت نبود سخت‌افزار «تمام‌شده» فرض نمی‌شوند و از دستور ساخت حذف نشده‌اند. دسترسی به Windows آزمایشگاه/دستگاه واقعی از این محیط برقرار نشده؛ برای پذیرش واقعی باید worker در همان شبکه اجرا و نتایج ثبت شود.
