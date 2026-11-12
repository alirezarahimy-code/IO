# ادامهٔ S05/S11/S12 — سلامت SQL، retry تنظیمات و نصب واقعی سرویس در CI

**بخش سوم هنوز کامل نیست؛ نصب CI با پذیرش واقعی دستگاه تفاوت دارد.**

## تغییر اجرایی

- `DeviceHealth`: صفحهٔ `/workspace/device-health` و GET واقعی SQL، آخرین تلاش queue+id، pending/running و unknown تاریخی، آخرین خواندن موفق تردد (نه ingestion)، cursor و next/failures زمان‌بندی. missing را آنلاین/آفلاین حدس نمی‌زند؛ اصل تردد/کاربران/تنظیمات، host و credential را برنمی‌گرداند.
- `ServiceHeartbeatWorker`: hosted service مستقل از چهار slot native، هر۳۰ثانیه با ساعت سرور و UUID تازهٔ هر process. `EnableHealthReports` پیش‌فرض false؛ grant `devices.service.report` مستقل و خارج قالب انسانی. جدول instanceها و guard 51025 هویت/start، زمان غیرکاهشی و stopped terminal را حفظ می‌کنند. قدیمی‌شدن گزارش جای success/stop ساختگی نیست.
- `devices.health.read` همزمان در catalogue، قالب تازهٔ operator/admin، منو، API، UI نقش‌ها و راهنمای مستقل اضافه شد. revoke دسترسی داده و help را می‌بندد؛ متن UI safe است و link جزئیات grant مستقل می‌خواهد.
- retry `settings.read` با commands.read/retry + settings.read جاری و **دو تأیید**: `RETRY-READ-ONLY-COMMAND` و `READ-ADVANCED-SETTINGS`. فقط تلاش terminal و مقصد/نسخه/zone ثابت؛ تلاش تازه و lineage اتمیک. مهاجرت جدید guard SQL را فقط به kind خواندنی دوم گسترش داد؛ نوشتن ساعت/کاربر/پاکسازی قابل replay نیست؛ downgrade lineage تنظیمات 51026.
- آزمون واقعی نصب/اجرا/توقف worker منتشرشده در **SCM ویندوز** با حساب مجازی اختصاصی و **systemd لینوکس** با کاربر محدود اختصاصی. فقط CI جدا، DB عمداً unavailable/synthetic، بدون SDK/native endpoint/credential واقعی و پاک‌سازی منابع. این مرحله فراتر از parser/ValidateOnly است اما نصب آزمایشگاه کاربر نیست.

## CI دقیق

کد `8faefd5928a0f03be3c8d6ce7b9090524ca54403`:

- [SQL/HTTPS/مرورگر 43243793048](https://github.com/alirezarahimy-code/IO/actions/runs/43243793048): **success، ۶۵۷ بررسی integration**.
- [Windows/Linux 43243792727](https://github.com/alirezarahimy-code/IO/actions/runs/43243792727): **success**؛ **۴۸ Foundation، ۲۶ domain، ۶۱ portable** و نصب/شروع/توقف واقعی دو سرویس OS در runner.
- **۱۱۴ حق، ۵۲ صفحه/راهنما، ۳۶ migration و ۶۰ جدول**. ۱۴ سرویس/۳ HTTP سلامت و ۱۸ سرویس/۴ HTTP snapshot/retry تنظیمات در مجموعهٔ فعلی.
- [شاهد](../part-03/s12-service-health-evidence.json)، [نصب و تفسیر](../part-03/s12-service-health.fa.md)، [وضعیت تمام مراحل](../part-03/progress-evidence.json).

## ماندهٔ واقعی

نوشتن تنظیمات بدون قرارداد مدل/firmware/واحد/دامنه ساخته نشده است. RemoteEnroll بدون قرارداد مدل و سیاست مصوب بیومتریک اجرایی نیست. vendor PUSH امن تأییدشده هنوز پیاده نشده است. آزمایشگاه واقعی SDK و چنددستگاه/قطعی/دریافت تکراری/تبادل/تنظیمات S12 در دسترس این محیط نبوده و اجرا نشده است. `realSdkExecuted=false`, `hardwareVerified=false`, `part3Complete=false`. این موارد با افزایش تست شبیه‌سازی یا تغییر پرچم مستندات بسته نمی‌شوند.
