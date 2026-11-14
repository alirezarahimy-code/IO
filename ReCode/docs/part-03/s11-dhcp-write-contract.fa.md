# تکمیل قرارداد نرم‌افزاری DHCP — شاهد تازه، نه تأیید دستگاه

## تفاوت با برش قبلی

تا `aa1f31a`، R05 باز بود: نوشتن `DHCP=0|1` از setter عمومی DLL و حتی writer ثالث UDP، به‌تنهایی نشان نمی‌داد این نام واقعاً یک پارامتر شناخته‌شدهٔ firmware است. این ایراد با افزایش تعداد PASS حل نمی‌شود.

اکنون یک **مرجع سورس تاریخی firmware با انتساب ZKSoftware در سربرگ** یافت شد: repository عمومی `eric4409/jz4725`، commit ثابت `de2de02fd0224f96d65a350ecad5efbc84ab230e`. این repository انتشار رسمیِ اصالت‌سنجی‌شدهٔ vendor نیست و وجود آن تطابق با firmware دستگاه نصب‌شده را ثابت نمی‌کند. سورس برای مشاهدهٔ قرارداد خوانده شده؛ هیچ کد firmware/کنترل قفل از آن به محصول وارد یا اجرا نشده است.

## زنجیرهٔ شاهد محدود و قابل بازتولید

[چهار منبع با Git blob و SHA256 و excerpt شماره‌خط‌دار](s11-dhcp-firmware-reference-evidence.json):

1. `options.h`، فیلد `int DHCP` با توضیح enable/disable. ترتیب واقعی `TOptionsResInt`، به‌ویژه `MaxValue` و `MinValue` بررسی شده است؛ ستون سوم جدول **restore-factory flag** است، نه writable flag.
2. `options.c`، `OptionsResInt` شامل نام دقیق `DHCP`، مقدار پیش‌فرض0، `optoffset(DHCP)` و دامنهٔ0..1 است. بنابراین برخلاف نام ساختگی، این گزینه معنای شناخته‌شده در این مرجع firmware دارد.
3. `commu.h`، `CMD_OPTIONS_WRQ=12`؛ در `commu.c`، handler نام و مقدار را از `=` جدا و `SaveStr(p,value,TRUE)` را فراخوانی می‌کند. `SaveStr` آن را به فایل گزینه‌ها نوشته و flush می‌کند؛ `LoadOptions` از جدول به فیلد عددی نگاشت می‌کند.
4. [۳۱ probe دو SDK](s11-network-native-dispatch-evidence.json) قبلاً رسیدن `DHCP=0\0` و `DHCP=1\0` با command12 به مرز transport را آزموده‌اند. transport و ACK در آن آزمایش مصنوعی‌اند؛ firmware اجرا نشده است.
5. نگاشت تولیدی IO اکنون در `IO.ZkBridge/ZkComProfileSession.cs` جدا شده و **همان فایل تولیدی** در تست قابل‌حمل compile می‌شود. ۲۹ بررسی تازه، روشن/خاموش، machine number37، نام دقیق، raw value، false/exception/lost ACK، inspection خواندنی و ورودی گزارش‌نشده/غیرcanonical را می‌آزمایند؛ شیء COM در این لایه double است، نه دستگاه یا COM فعال. مجموع profile از100 به129 می‌رسد.

این زنجیره شاهد **قرارداد نرم‌افزاری source-backed** است: API ثابت COM → گزینهٔ ثابت → command12 → گزینهٔ شناخته‌شده و ذخیره‌سازی در مرجع firmware. آن را «اجرای موفق DHCP روی firmware هدف» نمی‌نامیم. شاهد عمومی UDP قبلی نیز به‌عنوان شاهد مستقل مکمل حفظ شده است.

## ذخیره‌شدن با اعمال شبکه یکی نیست

مرجع تاریخی تغییر فایل را از `CMD_REFRESHOPTION`/`LoadOptions` جدا می‌کند. در نتیجه readback یک گزینهٔ ذخیره‌شده یا ACK، اثبات گرفتن lease، آدرس مؤثر یا دسترسی پایدار نیست. IO برای DHCP هیچ refresh/reboot/disable ضمنی اضافه نمی‌کند؛ چنین عملیاتی به قرارداد اثر دقیق همان نصب نیاز دارد. هر write شبکه همچنان `unknown` باقی می‌ماند و فقط inspection خواندنیِ مقصد ازپیش‌مصوب و هویت دقیق، سپس review صریح، شاهد بعدی ثبت می‌کند؛ نتیجهٔ تاریخی unknown بازنویسی نمی‌شود.

## شروط اجرا و مجوزها

- فقط raw `0` و `1`؛ نه bool coercion، `01`، فاصله، `DHCPOn` یا setter دلخواه.
- منبع تازهٔ SQL و مقدار قبلی گزارش‌شده/معتبر؛ serial/product/firmware دقیق؛ طرح immutable با hash و هر دو حالت/اثر/endpoint.
- مجوز انسانی مستقل `devices.profile.dhcp.set` و مجوز سرویس مستقل `devices.profile.dhcp.execute`؛ پیش‌فرض‌ها همچنان بدون grant هستند. scope، revoke و audit همان مسیر پذیرفته‌شده باقی‌اند.
- approval مدل/firmware نصب و endpoint ثابت/رزرو‌شده برای بازیابی اجباری است. مرجع تاریخی قرارداد یک خانواده را مستند می‌کند، نه مجوز فعال‌کردن همهٔ مدل‌ها.
- هیچ write تکراری، scan، تغییر خودکار موجودی یا تنظیمات همزمان اضافه نشده است؛ unknown قبلی اجرای بعدی را طبق fences موجود می‌بندد.

## معیار پذیرش این برش

پس از CI کامل SHA همین برش، R05 را فقط در سطح نرم‌افزار می‌توان بست: مسیر کامل پیشین SQL/UI/worker با قرارداد مستند تازه و آزمون mapper تولیدی. آزمون واقعی Windows COM، تطبیق مدل/firmware، ماندگاری پس از راه‌اندازی، lease/آدرس مؤثر، قطع/بازیابی و چنددستگاه همگی در **H01** باقی‌اند؛ نرم‌افزار بسته به معنی H01 موفق یا تکمیل تمام بخش۳ نیست. تا CI موفق، شمارندهٔ جاری تغییر نمی‌کند.

## بازتولید ممیزی منبع

```sh
python3 ReCode/tools/audit-dhcp-firmware-reference.py
```

ابزار فقط چهار Git blob ثابت را از GitHub می‌خواند، SHA1 ساختار Git و SHA256 و تمام excerptها را تطبیق می‌دهد و ساختار جدول/نام/دامنه/handler/ذخیره‌سازی را بررسی می‌کند؛ هیچ سورس دانلودشده را build یا اجرا نمی‌کند. برای بازتولید آفلاین، فایل‌های خام در پوشه‌ای با نام‌های `options.c`, `options.h`, `commu.c`, `commu.h` و گزینهٔ `--source-dir` قابل استفاده‌اند. Contents API ممکن است متن legacy را transcode کند؛ فقط raw blob با hash دقیق پذیرفته می‌شود. CI همین ممیزی را با دسترسی فقط‌خواندنی انجام می‌دهد.
