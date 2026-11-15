# IO

بازنویسی سامانهٔ IO؛ وضعیت تحویل و پذیرش بخش دستگاه:

- [گزارش جاری بخش۳ و مرز قابلیت‌های پشتیبانی‌شده](ReCode/docs/part-03/part-03-software-delivery.fa.md)
- [دفتر ثابت مانده‌ها و تاریخچهٔ پذیرش](ReCode/docs/part-03/remaining-count-ledger.fa.md)
- [شواهد نهایی Windows و UI، مقید به SHA](ReCode/docs/part-03/r15-r16-software-evidence.json)
- [بستهٔ باز پذیرش واقعی Windows/SDK/چنددستگاه — H01](ReCode/docs/part-03/h01-final-windows-lab-handoff.fa.md)

قراردادهای نرم‌افزاری منتخب پذیرفته‌اند؛ این به معنی هم‌ارزی کامل مرجع یا پذیرش سخت‌افزار نیست. پاکسازی R15، بازنشانی کارخانه نیست. USB/serial مستقیم در این نسخه پشتیبانی نمی‌شود و adapter آن ساخته نشده است. H01 باز و `part3Complete=false` است؛ قابلیت‌های مخرب/مدل‌محور نصب واقعی پیش‌فرض خاموش می‌مانند.

**دستور جدید تکمیل نرم‌افزار:** [پیاده‌سازی مستقیم USB/serial و مانده‌های R16](ReCode/docs/part-03/r16-direct-sdk-implementation.fa.md) جای disposition قبلی را می‌گیرد؛ دامنه گسترش یافته و نرم‌افزار هنوز کامل اعلام نمی‌شود. H01 به‌دلیل آماده‌نبودن محیط واقعی معوق است، نه مانع توسعه.
