# S06/S11 — تغییر صریح IPv4 و بازیابی خواندنی ZK

این برش candidate نرم‌افزاری است، نه پذیرش مدل واقعی یا پایان بخش سوم. فقط `SetDeviceIP`، `GetDeviceIP` و در صورت ACK true یک `RefreshData` دارد؛ gateway، subnet mask، DHCP، MAC، port، رمز ارتباطی، کنترل درب و factory reset را تغییر نمی‌دهد.

## منبع قرارداد

- پرتال رسمی منطقه‌ای: https://desarrollo.zktecolatinoamerica.com/sdk/set_device_ip — متن نمایه‌شده، امضای `VARIANT_BOOL SetDeviceIP(long dwMachineNumber, BSTR IPAddr)`، ارتباط با `GetDeviceIP` و نمونهٔ `RefreshData` پس از پاسخ true. دریافت مستقیم بعضی صفحات این پرتال در بررسی قبلی ناموفق بود؛ متن نمایه‌شده با دانلود کامل SDK یکسان نیست.
- metadata محلی فقط‌خواندنی `IZKEM`، `DispId(43)` برای `SetDeviceIP`؛ metadata باینری، سورس سازنده یا اثبات سازگاری firmware نامیده نمی‌شود.
- [بررسی منابع و محدودیت‌های خانواده‌های ZK](zk-vendor-research.fa.md): مدل یکسان می‌تواند firmware با پروتکل متفاوت داشته باشد. پشتیبانی مدل واقعی هنوز آزموده نشده است.

## چهار حق مستقل؛ هیچ اعطای ضمنی به قالب‌ها

| حق | کاربرد |
|---|---|
| `devices.network.ip.set` | ثبت تغییر IPv4 و بازبینی شاهد بازیابی |
| `devices.network.read` | ثبت inspection خواندنی مقصد قدیم یا جدیدِ تغییر unknown |
| `devices.network.ip.execute` | اجرای سرویسِ تغییر IPv4 |
| `devices.network.read.execute` | اجرای سرویسِ inspection خواندنی |

کاتالوگ و پنل اعطا/لغو موجود این چهار حق را می‌شناسند؛ هیچ قالب پیش‌فرض حتی admin/operator خودکار آنها را دریافت نمی‌کند. سرویس همچنان به `devices.operations.execute` و `EnableOperations=true` نیاز دارد. مشاهدهٔ تاریخچه و نتایج در صفحهٔ اطلاعات به `devices.info.read` جداگانه نیاز دارد؛ حق نوشتن/بررسی شبکه، حق خواندن ضمنی اطلاعات نیست.

## پیش‌نیاز نصب و برنامهٔ شبکه

1. وب، worker و bridge هماهنگ ارتقا و migration `20261112190000_DeviceNetworkIp` از روند موجود اعمال شوند. bridge قدیمی قرارداد جدید را اجرا نمی‌کند؛ raw TCP fallback وجود ندارد.
2. فقط برای مدل/firmware/SDK پذیرفته‌شده، روی NativeEndpoint دقیق گزینهٔ `AllowNetworkIpChange=true` و برای inspection گزینهٔ مستقل `AllowNetworkInspect=true` تنظیم شود؛ هر دو پیش‌فرض false هستند.
3. `ApprovedNetworkAddresses` باید صریحاً **هر دو** IPv4 قدیم و جدید را شامل شود؛ wildcard، DNS، subnet scan یا کشف خودکار نداریم. `Address` نصب تا بازبینی همان IP قدیم، `ExpectedSerial` همان سریال و port همان port قبلی است.
4. IP جدید با تیم شبکه رزرو و عدم تعارض، مسیر شبکه، subnet/gateway موجود و روش بازیابی محلی کنترل شود. نرم‌افزار مالکیت IP یا نبود تعارض را با ACK دستگاه ثابت نمی‌کند. آدرس شبکه/broadcast مخصوص subnet بدون داشتن mask حدس زده نمی‌شود؛ مسئولیت تأیید آن با نصب است.
5. IP باید canonical dotted IPv4 باشد؛ DNS/IPv6/فضای خالی/عدد صفرپیش‌دار، unspecified، loopback و multicast رد می‌شوند. این محدودیت قرارداد همین برش است، نه ادعای محدودیت تمام دستگاه‌های ZK.
6. زمان‌بندی‌های فعال این دستگاه را pause و unknown ارسال عادی کاربران را تعیین تکلیف کنید. سرویس پیش از دریافت credential دوباره این شرط را بررسی می‌کند. تغییر هم‌زمان دستگاه از نرم‌افزار دیگر ممنوع عملیاتی است؛ SDK تراکنش توزیع‌شده یا snapshot اتمیک ندارد.

## ثبت تغییر از صفحهٔ واقعی

مسیر موجود `/workspace/device-information`، نوع `network.ip.set`:

- شناسهٔ ثابت عملیات، UUID دستگاه فعال با پروفایل `zk-standalone`، rowversion هشت‌بایتی دستگاه و مهلت ۱ تا۶۰ثانیه؛
- طرح `NetworkPlan`: `OldAddress` مطابق Host موجودی، `NewAddress` متفاوت، `Serial` دقیق پین‌شده، `ChangeId=null` و `ObserveNewAddress=false`؛
- `TargetUtc=null` و تأیید دقیق `SET-DEVICE-IP`.

ثبت در `/device-operations` فقط ورود به SQL است. تکرار با همان شناسه و همان داده همان intent را بازمی‌گرداند؛ عوض‌کردن مقصد/سریال/نسخه/actor زیر همان شناسه تعارض است. طرح شبکه در SQL immutable است. فقط pending با نسخهٔ تازه قابل لغو تضمینی است. درخواست تغییر IP قدیمی‌تر از۱۲۰ثانیه در صف منقضی می‌شود.

## مرز اجرای بومی

worker مقصد قدیم DB را با pin نصب، port و سریال مقایسه می‌کند؛ علاوه بر حق مستقل سرویس، هر دو مقصد باید در allowlist همان NativeEndpoint باشند. credential فقط برای claim جاری و **همان طرح immutable** صادر می‌شود؛ طرح جعلیِ claim credential دریافت نمی‌کند. native child نه DB را می‌خواند و نه از آرگومان shell رمز می‌گیرد.

bridge بعد از اتصال و تطبیق سریال، ابتدا `GetDeviceIP` را می‌خواند. اگر مقدار گزارش‌شده نامعتبر، ناموجود یا متفاوت از IP قدیم باشد، پیش از mutation متوقف می‌شود؛ این برش NAT/DHCP یا تنظیم عوض‌شده را بی‌صدا overwrite نمی‌کند.

فقط یک `SetDeviceIP(new)` فراخوانی می‌شود. در پاسخ true، یک `RefreshData`؛ در false/exception/قطع پاسخ هیچ retry نیست. **پس از ورود به فراخوانی نوشتن، حتی ACK true هم `unknown` است**؛ رسیدن واقعی به IP جدید باید جدا مشاهده شود. timeout/خروج نامعتبر child و claim رهاشده نیز unknown می‌شوند. `succeeded` برای خود نوشتن IP در سرویس و SQL مجاز نیست.

## inspection تازه، نه تکرار فرمان

برای یک تغییر پایان‌یافتهٔ unknown و هنوز بازبینی‌نشده:

1. نوع `network.inspect` با تأیید `INSPECT-DEVICE-IP` انتخاب شود. دکمهٔ تاریخچه، طرح قدیم/جدید/سریال و شناسهٔ parent را پر می‌کند؛ هنوز چیزی ثبت یا اجرا نمی‌کند. نسخهٔ جاری دستگاه و تأیید تازه لازم است.
2. طرح همان parent را دارد؛ `ChangeId` شناسهٔ آن تغییر است. `ObserveNewAddress=true` فقط مقصد جدید را می‌خواند؛ false فقط مقصد قدیم را. هیچ آدرس سومی مجاز نیست.
3. worker به مقصد انتخابی از همان allowlist متصل و سریال را pin می‌کند. bridge فقط `GetDeviceIP` می‌خواند؛ `SetDeviceIP` یا `RefreshData` در inspection اجرا نمی‌شود.
4. snapshot واقعی SQL شامل `Serial`، `ConnectedAddress` و `DeviceIp` است. رسیدن به مقصدی که IP دیگری گزارش می‌کند به‌تنهایی مجوز بازبینی موفق نیست.
5. اگر هیچ‌کدام پاسخ ندهند، نتیجه ناموفق/قطع/timeout خواندنی ثبت می‌شود؛ unknown اصلی باز می‌ماند. بازیابی محلی توسط تیم شبکه/دستگاه لازم است؛ فرمان معکوس کور یا جست‌وجوی شبکه نداریم.

این inspection با parent، دستگاه، نسخه، طرح و زمان درخواست پس از پایان unknown محدود می‌شود؛ serial یا مقصد جدید را نمی‌توان در retry تغییر داد. دکمهٔ retry عمومی فرمان‌ها این نوشتن را تکرار نمی‌کند؛ inspection تازه از فرم خودش ثبت می‌شود.

## بازبینی و هماهنگ‌کردن موجودی

`/device-operations/{changeId}/resolve-network` با نسخهٔ تازهٔ عملیات unknown، شناسهٔ inspection موفق و متأخر و تأیید `REVIEWED-DEVICE-IP`:

- دستگاه/نسخه/timezone همان قبلی، parent و دو مقصد و سریال همان طرح قبلی؛
- سریال مشاهده‌شده همان pin و `DeviceIp == ConnectedAddress` در مقصد انتخابی؛
- حق انسانی نوشتن IP دوباره بررسی می‌شود؛ بازبینی، مجوز ضمنی برای mutation نیست.

بازبینی فقط شاهد/actor/time را اضافه می‌کند و نتیجهٔ اصلی **unknown** را تغییر نمی‌دهد. مشاهده در مقصد قدیم نیز ممکن است عدم تغییرِ قابل مشاهده را نشان دهد؛ آن را موفقیت SetDeviceIP نمی‌نامیم. Host موجودی، credential و pin نصب **خودکار عوض نمی‌شوند**. بعد از بررسی، به‌روزرسانی هماهنگ موجودی با rowversion تازه و pin نصب به مقصد مشاهده‌شده، با حقوق جدا انجام می‌شود؛ در صورت شکست، به مرحلهٔ بازیابی عملیاتی برگردید.

تا پیش از بازبینی unknown، تغییر metadata/credential/group، clock/restart/IP write تازه و ارسال عادی کاربران مسدود است؛ خواندن برای بازیابی مجاز می‌ماند. تاریخچهٔ دارای طرح شبکه، downgrade ناسازگار را متوقف می‌کند.

## شاهد و مرز پذیرش

کد `a5decf9726cb4b27e605bea21c76a8243283bb9e`: [Windows/Linux](https://github.com/alirezarahimy-code/IO/actions/runs/43308489449) و [SQL/HTTPS/مرورگر](https://github.com/alirezarahimy-code/IO/actions/runs/43308489543) هر دو **success**. suite کامل۷۳۶ بررسی integration دارد؛۲۶ SQL و۳ HTTPS جدید،۱۶ بررسی قرارداد/فرایند شبکه و آزمون DOM موفق‌اند.۱۲۰حق،۵۲صفحه/راهنما،۴۰migration و۶۰جدول. [شاهد ماشین‌خوان](s06-network-ip-evidence.json).

آزمون‌ها از session مصنوعی و child واقعی فرایند استفاده می‌کنند؛ نه COM واقعی، نه emulator پروتکل ZK و نه پذیرش سخت‌افزاری. آزمایشگاه تیم باید با حداقل دو دستگاه مستقل، IP رزروشده، قطع قبل/بعد ACK، شکست GetDeviceIP، دسترسی در مقصد قدیم/جدید، عدم پاسخ هر دو مقصد، جلوگیری از retry و به‌روزرسانی هماهنگ pin/موجودی را اجرا کند. هیچ تضمین «بدون اشکال روی همهٔ مدل‌ها» داده نشده است.
