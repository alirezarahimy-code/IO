# S11 — snapshot خواندنی تنظیمات پیشرفته؛ بدون نوشتن یا RemoteEnroll

## قراردادِ شاهددار و دامنه

در بدنهٔ decompile فقط‌خواندنی `Personnel.ZK.ZKDevice.GetDeviceAdvancedOptions` و getterهای همراه، `GetDeviceInfo(machine, code, ref Int32)` دیده شد. [شاهد hashدار باینری و metadata رابط](zk-reference-contract.json)؛ این بدنه سورس اصلی نیست و وجود نام API، سازگاری مدل/firmware را ثابت نمی‌کند.

| code | نام تاریخی | قرارداد ساخته‌شده |
|---|---|---|
| 25 | OneToOne | raw nullable Int32 |
| 23 | OneToN | raw nullable Int32 |
| 8 | RepeatTime | raw nullable Int32 |
| 4 | SleepTime | raw nullable Int32 |
| 30 | RegCard | raw nullable Int32، نه Boolean حدسی |
| 62 | MifareSector | raw nullable Int32 |

`ZkAdvancedSettingsReader` فقط همین شش کد ثابت را می‌خواند؛ کد دلخواه از وب یا config گرفته نمی‌شود. فراخوانی false/exception، **گزارش نشده** است نه صفر. موفقیت با عدد منفی، صفر، غیرمعمول یا Int32 min/max عیناً محفوظ است؛ واحد، دامنه، threshold مجاز یا علامت sentinel را حدس نمی‌زنیم. شکست یک فیلد، مقدار فیلد دیگر را حذف/صفر نمی‌کند. این مجموعهٔ شش مشاهده **snapshot اتمیک نیست**؛ اگر همه unavailable باشند هم هیچ قابلیت یا مقدار موفقی جعل نمی‌شود.

`ZkAdvancedSettings` با قرارداد ثابت `zk-device-info-v1`، serial معتبر و هر شش فیلدِ nullable اجباری در JSON؛ nullable بودن با missing بودن فرق دارد. private IPC فیلدهای بیگانه/ناقص، serial متفاوت و مخلوط information/settings را رد می‌کند. bridge فقط روی Windows و پس از pin سریال وارد خواندن می‌شود؛ سریال پس از خواندن نیز دوباره بررسی می‌شود. deadline بیرونی child را terminate می‌کند؛ timeout خواندن به unknown write یا retry خودکار تبدیل نمی‌شود. pin سریال احراز هویت رمزنگاری‌شدهٔ PUSH نیست.

هیچ `SetDeviceInfo`، تغییر IP/gateway/کلید، restart/reset/clear/delete، firmware update، RemoteEnroll یا کنترل درب در این executor نیست. `AllowClockSet` نصب، حق نوشتن از صفحهٔ تنظیمات ایجاد نمی‌کند؛ dispatch بر اساس kind ثابت است و مسیر clock همان مجوز و تأیید مستقل S06 را دارد.

## DB، نقش و مرز دسترسی

- `DeviceOperations.Kind=settings.read` از صف واقعی SQL استفاده می‌کند؛ payload typed در `ResultJson` و snapshot نسخهٔ دستگاه/zone/actor/deadline در همان رکورد immutable نگه داشته می‌شود. مهاجرت `20261112110000_DeviceAdvancedSettingsReads` فقط قرارداد CHECKهای موجود را گسترش می‌دهد؛ جدول موازی/ذخیرهٔ ساختگی ندارد.
- `devices.settings.read`: حق انسانی مستقل برای درخواست، history و لغو pending خواندنی؛ قالب تازهٔ operator/admin. حقوق قدیمی `devices.settings`، `devices.info.read`، `devices.clock.set` یا نقشِ صرف، آن را اعطا نمی‌کنند. قالب‌ها نقش موجود و حق لغوشده را خودکار احیا نمی‌کنند.
- `devices.settings.read.execute`: حق اضافی سرویس **علاوه بر** `devices.operations.execute`. هیچ قالب انسانی این دو grant اجرایی را نمی‌گیرد. نداشتن grant اضافی یا opt-in، کار تنظیمات را pending نگه می‌دارد؛ صف اطلاعات/ساعت دیگر از پشت آن گرسنه نمی‌ماند.
- grant انسانی پیش از dispatch/استفاده از credential و grant اضافی سرویس در claim، credential و complete بررسی می‌شود. revoke سرویس حتی با claim متعلق به همان سرویس، credential و ثبت نتیجه را رد می‌کند؛ سرویسِ مجاز بعدی recovery را انجام می‌دهد.
- history اطلاعات عادی `ListAsync` رکوردهای settings را برنمی‌گرداند؛ `ListSettingsAsync` فقط settings با حق مستقل می‌دهد. history فرمان‌ها فقط metadata را می‌بیند، نه payload تنظیمات. تغییر kind/نسخه/target یا نتیجهٔ terminal و حذف تاریخچه با guard قبلی 51017 رد می‌شود؛ downgrade با هر تاریخچهٔ settings خطای 51024 دارد.
- این حق **سراسری** است؛ فیلتر DeviceId دامنهٔ امنیتی جدید نیست. ثبت دستگاه/گروه/رمز و دیدن اطلاعات/کاربران/تردد حقوق مستقل دارند.

## صفحه و API

`/workspace/device-settings` با پیوند مستقیم از موجودی دستگاه و از metadata فرمان‌ها؛ فرم دارای UUID، DeviceId، expectedDeviceVersion/base64، مهلت ۱–۶۰ ثانیه و تأیید `READ-ADVANCED-SETTINGS`. هیچ ورودی مقدار یا code برای نوشتن وجود ندارد. تاریخچه ۵۰تایی و فیلتر اختیاری، serial/contract و شش مقدار با تمایز صفر/گزارش‌نشده دارد. HTML از textContent استفاده می‌کند؛ revoke داده و فرم را پاک می‌کند و help/منو در سرور همان حق را بررسی می‌کنند.

- `GET /device-settings-snapshots?deviceId=...&offset=...`: با حق مستقل settings؛ فیلتر خالی از query حذف می‌شود نه ارسال GUID خالی.
- `POST /device-settings-snapshots`: kind ثابت settings.read؛ CSRF، نسخه و تأیید صریح؛ ایجاد دوباره با همان UUID/payload idempotent و payload تغییرکرده تعارض است. پاسخ mutation همان envelope `result` است.
- `POST /device-operations/{id}/cancel`: مجوز بر اساس kind واقعی ذخیره‌شده تعیین می‌شود؛ settings read نمی‌تواند clock را لغو کند. نسخهٔ تازه و state=pending لازم‌اند.
- POST generic قدیمی عملیات نیز فقط allowlist سرور و همین مجوز/تأیید را قبول می‌کند؛ فرمان آزاد SDK از ورودی ساخته نمی‌شود.

## نصب، بازیابی و مانده

1. مهاجرت‌های تأییدشده تا `20261112110000_DeviceAdvancedSettingsReads` یا نسخهٔ بعدیِ پذیرفته‌شده اعمال شوند؛ وب/worker خودکار migrate نمی‌کنند.
2. در worker، `EnableOperations=true` و **`EnableSettingsReads=true`**؛ دومی پیش‌فرض false.
3. در NativeEndpoint دقیقِ مصوب، **`AllowAdvancedSettingsRead=true`**؛ پیش‌فرض false. pin کامل، مسیر absolute/ACLدار bridge و runtime سازگار با SDK Windows، key ring موجود و credential همان راهنمای [نصب](device-worker-setup.fa.md) است. flag نصب، اثبات سازگاری مدل نیست.
4. grantهای سرویس مشترک و اضافی هر دو صریح provision شوند. settings از همان صف عملیات در شش صف worker استفاده می‌کند، نه یک صف دورزنندهٔ مجوز یا slot شبکهٔ نامحدود.
5. requester revoked یا نسخهٔ دستگاه/zone تغییرکرده، pending را متوقف می‌کند. خواندن رهاشده پس از دو دقیقه به interrupted تبدیل می‌شود؛ نتیجهٔ دیررس رد می‌شود و خودکار بازاجرا نمی‌شود. لغو running تضمین برگرداندن کار نیست.
6. retry عمومی S11 اکنون خواندن تنظیمات را فقط با `devices.commands.read/retry`، حق جاری `devices.settings.read` و تأیید دوم `SettingsConfirmation=READ-ADVANCED-SETTINGS` می‌پذیرد. parent terminal، مقصد/نسخه/zone ثابت، UUID تازه و lineage اتمیک لازم‌اند؛ خودکار نیست و قبلی حذف/بازنویسی نمی‌شود. مهاجرت `20261112130000_DeviceSettingsReadRetries` باید اعمال شود؛ guard SQL فقط دو kind خواندنی information/settings را می‌پذیرد و downgrade با lineage تنظیمات خطای 51026 دارد.

نوشتن این تنظیمات به قرارداد واقعی مدل/firmware، واحد/دامنه/اثر تغییر و بازبینی مستقل نیاز دارد و **هنوز پیاده نشده است**. RemoteEnroll همچنان بدون قرارداد مدل و سیاست مصوب دسترسی/رضایت/نگهداری بیومتریک اجرایی نشده است. PUSH امن، نصب واقعی و پذیرش چنددستگاه/قطعی/تبادل/تنظیمات واقعی باقی‌اند؛ بخش سوم کامل نیست و هیچ SDK واقعی اجراشده ادعا نمی‌شود.


## شاهد CI تأییدشده

کد `12db64abb159fa122bee621afd5ecfff1662e28c`: SQL/HTTPS/مرورگر واقعی `43235930669` و Windows/Linux `43235930789` هر دو **success**؛ ۶۳۸ بررسی integration (۱۶ سرویس و ۴ HTTP اضافه‌شده)، ۴۷ Foundation، ۲۶ domain و ۶۱ portable. کاتالوگ ۱۱۲ حق، ۵۱ صفحه/مقاله، ۳۴ migration و ۵۹ جدول. [شاهد با جزئیات job/step](s11-settings-read-evidence.json).

آزمون‌های افزوده استقلال حق انسانی/سرویس، تأیید و نسخه، replay دقیق، opt-in claim، عدم انسداد صف اطلاعات، revoke پیش از credential/complete، raw صفر/null/منفی و RegCard غیرBoolean، فیلد JSON ناقص/بیگانه، pin و سریال، recovery بدون replay و guard SQL/downgrade را پوشش می‌دهند. مرورگر واقعی درخواست را در SQL ثبت و پس از reload مشاهده و pending را با نسخه لغو کرد؛ DOM آفلاین پاسخ نامعلوم، تأیید، نمایش safe متن و پاک‌سازی revoke را سنجید. هیچ‌کدام شاهد اجرای واقعی COM/SDK نیستند.

## افزودهٔ بعدی: نوشتن مستقل زمان بیکاری

مرز «بدون نوشتن» این سند مربوط به executor خواندن است و همچنان برقرار است. صفحه اکنون مسیر **جداگانهٔ** [تنظیم محدود کد۴](s11-idle-minutes.fa.md) با مجوز مستقل، preview و approval دقیق مدل/firmware دارد؛ مجوز خواندن هیچ setter را فعال نمی‌کند. snapshot خواندنی Product/Firmware nullable را نیز می‌خواند؛ unavailable نمی‌تواند مدل مصوب بسازد.
