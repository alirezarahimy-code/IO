# ادامهٔ S11 — تاریخچهٔ واقعی صف‌ها و retry صریح فقط خواندن

**بخش سوم = S04–S12 و هنوز کامل نشده است.** این برش، نرم‌افزار history/retry را می‌سازد، نه adapter تنظیمات پیشرفته یا RemoteEnroll، نه PUSH و نه پذیرش واقعی دستگاه.

## ساخته‌شده

- migration `20261112100000_DeviceCommandHistory`: view واقعی `dbo.DeviceCommandHistory` روی پنج صف موجود و جدول `DeviceCommandRetries` با FKهای واقعی، یک فرزند مستقیم برای هر تلاش، نسخهٔ والد/مقصد، actor و زمان درخواست، trigger 51023 و downgrade guard.
- `DeviceCommands`: history metadata سراسری با فیلتر اختیاری/صفحه‌بندی و lineage؛ نه اصل دادهٔ شخصی. فقط تلاش terminal خواندنی information.read و استخراج S09 قابلیت retry با تأیید `RETRY-READ-ONLY-COMMAND` دارند.
- retry جدید و lineage در یک transaction؛ مقصد، نسخه، range، کدها، deadline و zone تغییر نمی‌کنند. replay نامعلوم، همان child را برمی‌گرداند؛ تغییر payload/actor یا child دوم تعارض است. تلاش شکست‌خورده و نتیجهٔ والد overwrite نمی‌شوند.
- حق‌های `devices.commands.read/retry` همزمان در catalogue، نقش شناخته‌شده، UI نقش‌ها، منو، help و API واقعی ساخته شدند؛ read قالب اپراتور/admin، retry قالب admin و برای اپراتور فقط grant صریح. history+retry+حق خواندن مستقل لازم‌اند؛ execute سرویس هر قابلیت جدا می‌ماند.
- `/workspace/device-commands`: پنج صف واقعی، lineage، retry با تأیید و پیوند جزئیات/لغو همان قابلیت. UUID تلاش از متن lineage مستقل است؛ فیلتر خالی به‌جای ارسال GUID خالی از query حذف می‌شود. خطای شبکه موفقیت فرضی نمی‌سازد، revoke داده و فرم را پاک می‌کند و متن با DOM امن نمایش می‌یابد.
- clock.set، ارسال کاربر، پاکسازی، reset/restart، درب و enroll از retry عمومی قابل اجرا نیستند. unknown نوشتن همچنان به شاهد تازه و review S06/S08 نیاز دارد؛ cancellation فقط پیش از dispatch تضمین می‌شود.

## CI دقیق و موفق

کد **`e3c751f058b754005abccd44ba5b217ae6adf42e`**:

- **SQL/HTTPS/مرورگر واقعی `43232418581`: success**؛ **۶۱۸ PASS**، شامل ۱۵ بررسی سرویس و ۴ HTTP جدید S11؛ ۲۰ سرویس و ۵ HTTP S10 هم مجدداً گذشتند.
- **Windows/Linux `43232418391`: success**؛ **۴۶ Foundation، ۲۶ domain، ۵۵ portable**؛ build/test/publish و smoke میزبان منتشرشده.
- **۱۱۰ حق، ۵۰ صفحه و مقالهٔ راهنما، ۳۳ migration/۵۹ جدول**؛ SQL view به تعداد جدول‌ها اضافه شمرده نشده است.
- مرورگر واقعی history را از SQL خواند، تلاش اطلاعات شکست‌خورده را با تأیید retry کرد، lineage و outcome قبلی را پس از reload دید و child pending را در صفحهٔ قابلیت لغو کرد. DOM آفلاین نوشتن unknown را فاقد retry نگه داشت، همان UUID/payload پاسخ نامعلوم را تکرار و revoke/XSS-safe text را بررسی کرد.
- آزمون بازگشتی UUID یکسان در دو صف واقعی: history فیلترشدهٔ دستگاه تردد، lineage تلاش اطلاعات دستگاه دیگر را دریافت نمی‌کند؛ UI هم پیوند را با queue+id می‌بندد نه UUID تنها.
- شواهد: [S11](../part-03/s11-component-evidence.json)، [S10](../part-03/s10-component-evidence.json)، [وضعیت همهٔ مراحل](../part-03/progress-evidence.json)، [قرارداد/نقش/مرز حساس](../part-03/s11-command-history.fa.md).

## ماندهٔ صریح، نه ادعای پایان

PUSH امنِ مدل تأییدشده هنوز پیاده نشده است؛ serial pin احراز هویت رمزنگاری‌شده نیست. `GetDeviceAdvancedOptions/SetDeviceInfo/StartEnrollEx` در باینری فقط‌خواندنی مشاهده شدند، ولی واحد/دامنهٔ مقدار، مدل/firmware و سیاست بیومتریک تأیید نشده‌اند؛ اجرایی کردن آن‌ها از روی حدس مجاز نیست و adapter تنظیمات پیشرفته/RemoteEnroll هنوز ساخته نشده است. [شاهد hashدار مرجع](../part-03/zk-reference-contract.json).

هیچ SDK/دستگاه واقعی اجرا نشده، نصب واقعی Windows/Linux سرویس انجام نشده و سناریوی واقعی چند دستگاه/قطعی/دریافت تکراری/تبادل کاربران/تنظیمات S12 همچنان باز است. `realSdkExecuted=false`, `hardwareVerified=false`, `part3Complete=false`. کنترل درب و payroll خارج دامنه‌اند؛ simulator کافی نیست.


ادامهٔ بعدی: [گزارش 152 — snapshot خواندنی شش تنظیم پیشرفته](152-s11-settings-read.md) با حق مستقل و CI موفق؛ اعداد بالا خط مبنای تاریخی این برش‌اند. نوشتن تنظیمات و RemoteEnroll همچنان پیاده نشده‌اند.
