# ادامهٔ S11 — snapshot خواندنی شش تنظیم پیشرفته

**بخش سوم = S04–S12 و هنوز کامل نیست.** این برش فقط خواندن شاهددار شش `GetDeviceInfo` را به نرم‌افزار اضافه می‌کند؛ نه نوشتن تنظیمات، RemoteEnroll یا پذیرش سخت‌افزار.

## ساخته‌شده

- `ZkAdvancedSettingsContract` با `zk-device-info-v1`: کدهای ثابت 25/23/8/4/30/62 و فیلدهای OneToOne/OneToN/RepeatTime/SleepTime/RegCard/MifareSector؛ هر شش raw nullable Int32. صفر با unavailable فرق دارد؛ علامت و مقادیر غیرBoolean حفظ می‌شوند، واحد و دامنه حدس زده نمی‌شوند. شش فراخوانی snapshot اتمیک نیستند.
- bridge جدا از وب، pin سریال قبل/بعد، IPC بسته و اعتبارسنجی payload؛ `SetDeviceInfo` یا `StartEnrollEx` اجرا نمی‌شوند. هر دو gate نصب worker/endpoint پیش‌فرض خاموش‌اند.
- SQL queue موجود با kind ثابت `settings.read`، مهاجرت 34، نتیجهٔ immutable و downgrade guard 51024؛ حق انسانی `devices.settings.read` برای قالب تازهٔ اپراتور/admin و grant سرویس اضافی `devices.settings.read.execute` در کنار `devices.operations.execute`. revoke در claim/credential/complete اعمال می‌شود؛ قالب انسانی grant اجرایی ندارد.
- صفحه و help `/workspace/device-settings`: فرم درخواست با UUID/نسخه/تأیید `READ-ADVANCED-SETTINGS`، history و شش مقدار واقعی DB، لغو نسخه‌دار فقط pending. تاریخچهٔ اطلاعات عادی snapshot تنظیمات را نشت نمی‌دهد. API، role UI، منو و پیوند فرمان/دستگاه همزمان اضافه شدند.
- recovery خروجی interrupted را حفظ می‌کند و خودکار تکرار نمی‌کند؛ generic command retry فعلاً تنظیمات را نمی‌پذیرد. نوشتن unknown همچنان تابع review مستقل S06/S08 است.

## CI دقیق و موفق

کد **`12db64abb159fa122bee621afd5ecfff1662e28c`**:

- [SQL/HTTPS/مرورگر واقعی 43235930669](https://github.com/alirezarahimy-code/IO/actions/runs/43235930669): **success، ۶۳۸ بررسی integration**؛ ۱۶ سرویس و ۴ HTTP تازهٔ تنظیمات.
- [Windows/Linux 43235930789](https://github.com/alirezarahimy-code/IO/actions/runs/43235930789): **success**؛ **۴۷ Foundation، ۲۶ domain، ۶۱ portable** و build/publish/smoke هر دو سیستم.
- **۱۱۲ حق، ۵۱ صفحه و مقالهٔ راهنما، ۳۴ migration/۵۹ جدول**؛ schema/model مطابقت دارند.
- مرورگر واقعی درخواست تنظیمات را با نسخه/تأیید ثبت کرد، پس از reload آن را از SQL دید و pending را لغو کرد؛ DOM آفلاین uncertain replay، raw signed/null/zero، متن امن و پاک‌سازی revoke را سنجید.
- [شاهد ماشین‌خوان برش](../part-03/s11-settings-read-evidence.json)، [راهنمای کامل](../part-03/s11-settings-read.fa.md)، [وضعیت تمام مراحل](../part-03/progress-evidence.json)، [شاهد باینری فقط‌خواندنی](../part-03/zk-reference-contract.json).

## مانده و مرز ادعا

هیچ SDK/دستگاه واقعی اجرا نشده؛ تطابق مدل/firmware و واحد/دامنهٔ این فیلدها تأیید نشده‌اند. نوشتن تنظیمات پیشرفته و RemoteEnroll پیاده نشده‌اند؛ enroll بدون سیاست مصوب بیومتریک فعال نمی‌شود. PUSH احراز هویت‌شده/ضدتکرار و نصب واقعی Windows/Linux و پذیرش واقعی چند دستگاه/قطعی/دریافت تکراری/تبادل/تنظیمات S12 همچنان بازند. `realSdkExecuted=false`, `hardwareVerified=false`, `part3Complete=false`. شبیه‌ساز کافی نیست؛ payroll و کنترل درب خارج دامنه‌اند.
