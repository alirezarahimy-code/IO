# R16 — تعیین‌تکلیف اتصال مستقیم USB و serial در نسخهٔ جاری

## نتیجهٔ صریح: unsupported-in-this-delivery، نه پیاده‌سازی اتصال مستقیم

این ردیفِ ledger از ابتدا **تعیین‌تکلیف قرارداد/پشتیبانی/دامنهٔ هدف اتصال مستقیم** بوده است، نه تغییر نام parser فایل به USB. تصمیم نسخهٔ جاری: مسیر عملیاتی SDK **شبکه** با IP/port و allowlist و مسیر **فایل صریح** عرضه می‌شوند؛ اتصال کابل USB و COM/serial در این نسخه **پشتیبانی نمی‌شود**. adapter، driver discovery، transport selector، fallback شبکه→USB/serial یا امکان بازکردن پورت از فرم/config عمومی وجود ندارد.

این تصمیم محدودیت واقعی محصول و ثبت صریح عدم هم‌ارزی با مرجع است؛ «پیاده‌سازی کامل USB/serial» یا «دستگاه قابلیت ندارد» نیست. دلیل، Linux بودن محیط یا نداشتن دستگاه محلی نیست: مسیر واقعی Windows نیز مستقیم را ارائه نمی‌کند. دلیل انتخاب دامنهٔ محافظه‌کارانه، نبود قرارداد ایمن انتخاب و مالکیت endpoint فیزیکی محلی در معماری فعلی است؛ آدرس IP، scope دستگاه و pin سریالِ پس از اتصال، مجوز تصاحب هر پورت محلی یا انتخاب خودکار دستگاه USB نمی‌شوند. بازکردن صرفاً یک متد SDK، کار کامل اتصال مستقیم چنددستگاه نیست. افزودن آن نیازمند برش نرم‌افزاری مستقل است، نه تنها روشن‌کردن switch یا یک تست سخت‌افزاری H01.

## مرجع واقعاً چه دارد؟

[شاهد هش‌ثابت باینری و ABI](r15-r16-native-abi-evidence.json):

- باینری `T5V3.9.2/Personnel.DLL` با SHA256 `185b084072a552e92ab0c8b0c2a03ca926b2aa362b5aada1ca8a39f998144be4`؛
- نوع decompile‌شدهٔ `Personnel.ZK.ZKDevice` با SHA256 بدنه `58e33195ab271660e2f48b81d80167b91c693cb7986b955ee14c94d1b58b7eb5`؛ خط۲۱۶..۲۲۶ در Connect سه شاخه دارد: TCPIP→`Connect_Net(IP,Port)`؛ USB→`Connect_USB(MachineNumber)`؛ SerialPort→`Connect_Com(1,MachineNumber,115200)`؛
- دو TypeLib DLL موجود واقعاً امضای `Connect_USB(LONG machine)` و `Connect_Com(LONG port,LONG machine,LONG baud)` با خروجی bool دارند؛ ناتوانی SDK ادعا نمی‌شود.

COM1 و115200 **مقادیر ثابت مرجع** هستند، نه کشف پورت/baud نصب یا default امن نسخهٔ جدید. امضا و branch نشان نمی‌دهند کدام driver/architecture/مدل یا چند endpoint با Connect_USB انتخاب می‌شوند، یا SDK چه انحصاری روی handle محلی اعمال می‌کند. هیچ default فرضی/scan پورت/کابل یا DLL-export حدسی منتقل نشده است. در این برش باینری مرجع فقط خوانده و هرگز اجرا یا ویرایش نشده است.

## مرزهای اجرایی و رابط

| مسیر | وضعیت این نسخه | آنچه نتیجه نمی‌شود |
|---|---|---|
| sdk-network / zk-standalone | پیاده؛ عملیات فقط طبق قرارداد، serial/product/firmware/endpoint و مجوز همان خانواده | TCP handshake یا registration، تأیید SDK/firmware نیست؛ پذیرش واقعی در H01 |
| usb-file / offline-file | دریافت فایل با قالب/تقویم/zone صریح، preview/quarantine و commit مصوب | کابل USB، Connect_USB، filesystem آزاد worker یا فرمت جهانی vendor نیست |
| sdk-usb-direct | unsupported-in-this-delivery؛ profile اجرایی ندارد | این تصمیم مدل را فاقد USB اعلام نمی‌کند |
| sdk-serial-direct | unsupported-in-this-delivery؛ profile اجرایی ندارد | COM1/115200 مرجع مجوز بازکردن COM دلخواه یا default سازمان نیست |

`DeviceTransportSupport` قرارداد machine-readable ثابت را ارائه می‌دهد. `GET /devices/transport-support` پشت login و حق موجود `devices.read`، no-store و audit خواندنی است؛ هیچ command، device id، credential یا مسیر raw برنمی‌گرداند. حق اجرای سرویس به‌تنهایی حق خواندن این گزارش نیست؛ پاسخ صرفاً دربارهٔ **دامنهٔ محصول** است، نه شناسایی یا گواهی دستگاه‌های ثبت‌شده.

صفحهٔ `/workspace/devices` و help دامنهٔ نسخه را صریح نشان می‌دهند و کنترل مستقیم جعلی ندارند. registry، profileهای `zk-usb-direct`, `zk-serial-direct`, `usb`, `serial` را رد می‌کند؛ COM1/COMn، USB/serial، مسیرهای Windows device و `/dev/...` نیز نمی‌توانند به‌عنوان hostname شبکه ذخیره شوند. `usb-gateway.example.test` اگر واقعاً یک میزبان شبکه باشد به‌صرف نام رد نمی‌شود، اما allowlist IP و قرارداد شبکهٔ worker همچنان لازم است؛ gateway/tunnel به معنی پشتیبانی transport محلی یا native device authentication نیست.

`ZkProbeInput` فقط IP واقعی/port دارد؛ path یا COM پذیرفته نمی‌شود. property تزریقی `Transport="usb"` با JSON strict رد می‌شود، نه اینکه بی‌صدا از شبکه استفاده کند. سایر executorها هم هیچ transport selector از این قرارداد استخراج نمی‌کنند. USB-file parser مسیر مستقل است و با این کار تغییر معنا نکرده است.

## اگر نسخهٔ بعد واقعاً مستقیم را اضافه کند

این‌ها پیش‌نیازهای **پیاده‌سازی جدید** هستند، نه مواردی که با H01 فعلی خودکار حل شوند:

1. شناسهٔ ایستگاه Windows و SDK/driver/architecture مجاز، هویت endpoint فیزیکی و روش انتخاب USB بدون جابه‌جایی ناشناختهٔ دو دستگاه؛ serial port و baud صریح همان نصب، بدون COM1 حدسی یا scan؛
2. مالکیت انحصاری چندپردازه/چند worker روی تمام aliasهای پورت/USB، ACL حساب سرویس، جلوگیری از دسترسی کاربر به device path دلخواه؛ pin سریال بعد از اتصال همچنان لازم است ولی جای این مرز را نمی‌گیرد؛
3. قرارداد، مجوز و صف durable و cancellation/deadline/child isolation transport-specific؛ مسیرهای نوشتنِ IPمحور را نباید با null/fake-IP دوباره استفاده کرد؛
4. ماتریس عملیات مجاز برای همان transport؛ supported connection به‌تنهایی مجوز read/upload/delete نیست؛ سپس آزمون مصنوعی کامل و Windows نصب واقعی/دو دستگاه/تعویض کابل/پورت/قطع حین command.

## پذیرش محدود و صادقانه

[شاهد Windows/UI](r15-r16-software-evidence.json) آزمون‌های policy/registry/strict IPC، گزارش HTTPS با مجوز و DOM شفافیت دامنه را ثبت می‌کند. آن‌ها **تست Connect_USB، Connect_Com، driver یا اتصال مستقیم نیستند**. بستن R16 صرفاً disposition نسخه است؛ factory reset، direct USB/serial و storeهای خارج profile، قابلیت ساخته‌شده اعلام نمی‌شوند. نبود نیاز فوری به اتصال مستقیم، مجوز گسترش به مدل ناشناخته یا حذف خام نیست. H01 روی مسیرهای پشتیبانی‌شدهٔ شبکه/فایل و نصب واقعی آخر کار باز می‌ماند.
