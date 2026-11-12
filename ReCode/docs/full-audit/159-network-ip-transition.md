# تغییر محدود IPv4 و بازیابی خواندنی مقصد قدیم/جدید — S06/S11

**پایان بخش سوم یا پذیرش مدل/SDK واقعی نیست.**

- منبع رسمی منطقه‌ای SetDeviceIP/GetDeviceIP/RefreshData و metadata محلی فقط‌خواندنی بررسی شدند؛ واحد/دامنهٔ کدهای arbitrary یا gateway/mask/DHCP/port حدس زده نشد.
- مسیر واقعی صفحهٔ اطلاعات → قرارداد بسته → صف SQL → worker → bridge مستقل Windows؛ یک ستون immutable طرح شبکه و migration۴۰، بدون جدول/صف/صفحهٔ اضافی.
- چهار حق مستقل human write/read و service write/read، قابل grant/revoke؛ هیچ قالب پیش‌فرض خودکار آنها را نمی‌گیرد. گزینه‌های AllowNetworkIpChange/AllowNetworkInspect پیش‌فرض false و allowlist هر دو مقصد اجباری‌اند.
- طرح دقیق old/new/serial و parent inspection با نسخه/شناسهٔ ثابت؛ عدم تطبیق Host قدیم، serial نصب، allowlist یا claim دست‌کاری‌شده از اجرا/credential جلوگیری می‌کند.
- قبل از mutation، GetDeviceIP برابر IP قدیم لازم است؛ فقط یک SetDeviceIP و در ACK true یک RefreshData. حتی ACK true پس از فراخوانی unknown است؛ replay وجود ندارد.
- inspection فقط یک مقصد مصوب قدیم یا جدید با همان سریال را می‌خواند؛ هیچ mutation ندارد. نتیجهٔ IP متفاوت از مقصد اتصال نمی‌تواند review موفق ایجاد کند.
- unknown/recovery immutable، بازبینی صریح، pending-only cancel، expiry۱۲۰ثانیه و جلوگیری از retarget metadata/credential/group و ارسال کاربران تا بازبینی؛ downgrade با تاریخچهٔ شبکه رد می‌شود.
- Host/pin نصب خودکار عوض نمی‌شوند؛ پس از مشاهده و بازبینی، هماهنگی آنها با حقوق و نسخهٔ تازه جدا انجام می‌شود. عدم پاسخ هر دو مقصد نیازمند بازیابی محلی است؛ subnet scan و فرمان معکوس خودکار نداریم.

## CI نهایی

کد `a5decf9726cb4b27e605bea21c76a8243283bb9e`:

- [Windows/Linux](https://github.com/alirezarahimy-code/IO/actions/runs/43308489449): **success** هر دو OS، با۱۶ بررسی قرارداد/فرایند شبکه و آزمون‌های قبلی.
- [SQL/HTTPS/مرورگر](https://github.com/alirezarahimy-code/IO/actions/runs/43308489543): **success،۷۳۶ integration**؛۲۶ SQL و۳ HTTPS جدید؛ DOM طرح/تأیید/inspection/بازبینی/revoke موفق.
- ۱۲۰حق،۵۲صفحه/راهنما،۴۰migration و۶۰جدول؛۴۸Foundation،۲۶domain،۶۱portable،۱۱restart،۱۰read،۱۲legacy-write+۴TCP و۱۹virtual قبلی موفق.
- آزمون downgrade قدیمی قبل از ثبت تاریخچهٔ جدید اجرا و migrationهای خالی بعد از شکستِ مورد انتظار دوباره اعمال می‌شوند؛ snapshot/model تطابق دارد.

[راهنمای کامل](../part-03/s06-network-ip.fa.md) · [شاهد](../part-03/s06-network-ip-evidence.json) · [مانده](../part-03/remaining-work.fa.md).

اجرای COM یا دستگاه واقعی رخ نداده است. سایر نوشتن تنظیمات، vendor PUSH امن، RemoteEnroll/بیومتریک و reset/حذف مخرب همچنان بازند.
