# S11 — تغییر محدود زمان بیکاری؛ کد۴، دقیقه

**این برش تکمیل بخش سوم، پذیرش دستگاه واقعی یا مجوز کنترل درب نیست.** فقط `settings.idle.set` ساخته شده است؛ ورودی عمومی برای `SetDeviceInfo` وجود ندارد.

## مبنای پژوهش و مرز استنتاج

- [GetDeviceInfo در مرجع منطقه‌ای ZKTeco](https://desarrollo.zktecolatinoamerica.com/sdk/get_device_info): کد۴ مدت بیکاری برحسب **دقیقه** است؛ پس از آن دستگاه ممکن است standby شود یا خاموش شود. بنابراین رابط کاربر فقط «خواب» را وعده نمی‌دهد. متن از نمایهٔ جست‌وجو بررسی شد؛ دریافت مستقیم این صفحه در محیط پژوهش موفق نبود.
- [راهنمای رسمی EFace10، ژانویهٔ۲۰۲۲، نسخه۱٫۰](https://zkteco.eu/wp-content/uploads/downloads/legacy/eface10/eface10%C2%A0user%C2%A0manual-v1.0-20220120.pdf): گزینهٔ Idle Time to Sleep در رابط این مدل قابل غیرفعال‌کردن یا تنظیم۱..۹۹۹دقیقه است. **این دامنه، اثبات نگاشت COM و سازگاری تمام مدل‌ها/firmwareها نیست.** برنامه آن را فقط به‌عنوان سقف محافظه‌کارانه پذیرفته است؛ نصب باید مدل دقیق و تک‌تک مقادیر قابل نوشتن را جدا تأیید کند. پشتیبانی EFace10 برای این adapter ادعا نمی‌شود.
- بدنهٔ decompile فقط‌خواندنی `PersonnelZKDevice.decompiled.cs` و metadata رابط `IZKEM` فراخوانی تاریخی `SetDeviceInfo(machine,4,value)` را نشان می‌دهند. این باینری، سورس اصلی نیست؛ وجود setter، واحد/دامنه یا پذیرش دستگاه واقعی را به‌تنهایی ثابت نمی‌کند. شاهد مرجع: [zk-reference-contract.json](zk-reference-contract.json).
- معنی encoding غیرفعال/صفر تأیید نشده؛ هم مقدار قدیم و هم جدید باید۱..۹۹۹ و متفاوت باشند. کد۸ و دیگر تنظیمات خام همچنان بدون واحد/دامنهٔ حدسی خوانده می‌شوند. از دامنهٔ رابط EFace10 برای کد۸ یا همهٔ firmwareها نتیجه‌گیری نشده است.

## مسیر صفحه و نقش‌ها

همان صفحهٔ `/workspace/device-settings`، با تاریخچهٔ واقعی SQL و راهنمای نقش‌محور:

1. اپراتور مجاز با `devices.settings.read`، snapshot موفق بگیرد. خواندن شش کد، مستقل و غیراتمیک است؛ Product و Firmware نیز nullable خوانده می‌شوند. false/exception همان «گزارش نشده» است، نه مقدار پیش‌فرض.
2. برای پیش‌نمایش/نوشتن/بازبینی، `devices.settings.idle.set` **به‌علاوهٔ** `devices.settings.read` لازم است. حق خواندن یا نام نقش admin/operator مجوز نوشتن نیست. حق تازه به هیچ قالب پیش‌فرض اضافه نشده است.
3. snapshot موفق کمتر از پنج دقیقه، از همین دستگاه/نسخه/zone، با serial/product/firmware و مقدار قدیم معتبر را انتخاب کند. مقدار جدید و مرجع تأیید مدل نصب‌شده را وارد کند.
4. «ساخت پیش‌نمایش بدون نوشتن» را بزند. سرور طرح `zk-idle-minutes-v1` را از SQL می‌سازد و SHA256 طرح را برمی‌گرداند؛ این hash امضای سازنده یا احراز سازگاری مدل نیست. `installedModelApprovalNotProven=true` عمداً نمایش داده می‌شود.
5. serial/product/firmware، قدیم/جدید و احتمال خواب یا **خاموشی/قطع دریافت** را بررسی و جدا تأیید کند. تغییر ورودی، پیش‌نمایش و تأیید را باطل می‌کند. فقط همان طرح/hash همراه `SET-IDLE-MINUTES-MAY-POWER-OFF` ثبت می‌شود.
6. پاسخ نامعلوم HTTP، با همان شناسه و payload ثبت دوباره می‌شود؛ قصد SQL idempotent است و فرمان تازه ساخته نمی‌شود. نتیجهٔ `unknown` دستگاه مسیر متفاوتی دارد و قابل retry کور نیست.

`POST /device-settings/idle-preview`، `POST /device-operations` و `POST /device-operations/{id}/resolve-idle` همگی session، CSRF و grant جاری سمت سرور دارند. لغو مجوز در نشست موجود مؤثر است؛ UI داده/طرح را پاک و فرم را مخفی می‌کند. لغو انسانی پس از صف نیز پیش از claim/credential بررسی می‌شود. مشاهدهٔ تاریخچه حق read می‌خواهد؛ لغو pending هر kind، حق همان عملیات را می‌خواهد.

## نصب worker و تأیید دقیق مدل

وب/worker/bridge هماهنگ ارتقا یابند و migration `20261112200000_DeviceIdleSetting` اعمال شود. این migration ستون `IdlePlanJson`، CHECK و triggerهای هویت/نتیجه/review را به همان جدول اضافه می‌کند؛ جدول یا صف موازی ندارد. downgrade پس از ثبت هر طرح idle ممنوع است.

سرویس همچنان به `EnableOperations=true` و `devices.operations.execute` نیاز دارد؛ **علاوه بر آن** `devices.settings.idle.execute` باید صریحاً اعطا شود. اجرای خواندن و بازیابی snapshot، مجوز مستقل `devices.settings.read.execute` و opt-in خواندن را می‌خواهد؛ حق اجرای idle آن را نمی‌دهد.

نمونهٔ ساختار **غیرفعال و synthetic** برای یک endpoint از قبل ثبت‌شده؛ این نمونه مجوز هیچ مدل واقعی نیست:

```json
{
  "AllowIdleMinutesWrite": false,
  "IdleMinutesApproval": {
    "Product": "EXACT-OBSERVED-PRODUCT",
    "Firmware": "EXACT-OBSERVED-FIRMWARE",
    "ApprovedMinutes": [10],
    "EvidenceReference": "TEAM-APPROVED-LAB-EVIDENCE-ID",
    "Effect": "standby-or-power-off"
  }
}
```

`AllowIdleMinutesWrite` پیش‌فرض false است. approval باید دقیقاً محصول، firmware، مقدار جدید و مرجع طرح را تطبیق دهد؛ allowlist بین۱و۶۴ مقدار صحیح یکتا از۱..۹۹۹ دارد. wildcard، حدس مدل از DLL، مقادیر min/max عمومی یا fallback نداریم. شماره دستگاه، Host/Port و serial pin همان endpoint نصب‌شده باقی می‌مانند. این flag مجوز ساعت، restart، شبکه، RemoteEnroll یا درب نیست.

پیش از ثبت، polling همان دستگاه متوقف و unknown ارسال کاربران بازبینی شود. pending/running سایر صف‌ها مانع کار هم‌زمان است. دسترسی credential نیز وضعیت جاری schedule/unknown و طرح دقیق claim را دوباره بررسی می‌کند. درخواست queued پس از۱۲۰ثانیه expire می‌شود؛ لغو فقط پیش از dispatch تضمین می‌شود.

## اجرای بسته و نتیجه

`ZkIdleSettingExecutor` فقط پس از اتصال و pin سریال وارد می‌شود:

- preflight زنده: `GetProductCode`، `GetFirmwareVersion` و `GetDeviceInfo(machine,4,ref value)`؛ مدل/firmware و مقدار قدیم باید دقیقاً برابر طرح باشند. failure/exception قبل از نوشتن، `failed` و صفر mutation است.
- حداکثر یک `SetDeviceInfo(machine,4,newMinutes)`.
- فقط true همراه بازخوانی مقدار جدید و مدل/firmware دقیق، `succeeded` typed با `ZkIdleObservation` می‌دهد. ACK تنها کافی نیست.
- false، exception، قطع ارتباط یا عدم تطبیق read-back **پس از dispatch**، `unknown` است؛ process deadline/خروج خراب نیز unknown می‌شود. نه failure امن برای retry و نه success ساختگی.
- هیچ `RefreshData`، DisableDevice، restart، اسکن، rollback، setter دیگر یا retry خودکار در این executor وجود ندارد. این‌که دستگاه خوابیده/خاموش شده است را ACK اثبات نمی‌کند؛ فقط read-back گزارش می‌شود.

## بازیابی unknown

1. فرمان نوشتن را تکرار نکنید. اگر دستگاه پاسخ نمی‌دهد، بازیابی محلی/شبکه طبق رویهٔ آزمایشگاه لازم است؛ نرم‌افزار وعدهٔ بیدارکردن یا روشن‌کردن از راه دور نمی‌دهد.
2. snapshot `settings.read` **پس از پایان unknown** با همان دستگاه/نسخه/zone/serial/product/firmware بگیرید. مقدار کد۴ باید دقیقاً قدیم یا جدید باشد؛ مقدار سوم/descriptor متفاوت شاهد پذیرفتنی نیست.
3. در تاریخچهٔ idle، شناسهٔ snapshot و عبارت `REVIEWED-IDLE-MINUTES` را پس از بررسی شاهد وارد کنید. سرور و trigger SQL رابطه را بررسی می‌کنند.
4. reviewer/time/snapshot ثبت می‌شود، اما state اصلی **unknown باقی می‌ماند**؛ مشاهدهٔ بعدی، موفقیت قطعی فرمان گذشته را جعل نمی‌کند. هیچ تغییر خودکار pin/موجودی یا تکرار نوشتن انجام نمی‌شود.

تا پیش از بازبینی، نوشتن‌های حساس بعدی و تغییر metadata/credential/group و ارسال کاربر fence می‌شوند؛ خواندن بازیابی باز می‌ماند. snapshot اتمیک نیست؛ هر نوشتن بعدی نیز preflight زندهٔ خودش را لازم دارد.

## آزمون و پذیرش

[شاهد نسخه‌دار این برش](s11-idle-minutes-evidence.json) وضعیت commit/CI را ثبت می‌کند. آزمون‌ها شامل قرارداد بسته و process واقعی بدون SDK، SQL واقعی و downgrade، grant/revoke و HTTP/CSRF، DOM پیش‌نمایش/تأیید/بازبینی/پاک‌سازی هستند. آزمون مصنوعی، اجرای COM و پذیرش سخت‌افزار نیست.

پذیرش آزمایشگاه برای هر مدل/firmware: مقدار قدیم/جدید مصوب، اثر خواب/خاموشی و روش بازیابی محلی، قطع پیش/پس از dispatch، read-back و unknown، لغو مجوز و چنددستگاه مستقل واقعاً آزمایش و ثبت شوند. تا آن زمان `realSdkExecuted=false`، `hardwareVerified=false` و `part3Complete=false` باقی می‌مانند. PUSH امن، سایر تنظیمات، RemoteEnroll/بیومتریک و reset/حذف مستقل همچنان بازند.
