# دستگاه مجازی stateful و اصلاح fail-fast مرز native

**بخش سوم کامل نیست. تست مجازی طبق تصمیم کاربر اکنون اجرا شد؛ پذیرش دستگاه واقعی توسط تیم در مرحلهٔ بعد باز است.**

## ساخته و اجراشده

- `VirtualDeviceLab.cs` در پروژهٔ تست: دو endpoint TCP مستقل و loopback با دادهٔ synthetic در حافظه؛ state بین child processها می‌ماند.
- ۱۹ assertion: تاریخچهٔ تکراری و multiplicity، دیررس، disconnect پس از batch، بازیابی، count نادرست، سریال اشتباه، timeout یک دستگاه در کنار موفقیت دیگری، cancellation، مشخصات/تنظیمات خام و ساعت opt-in، کاربران عادی و **اعمال نوشتن پیش از قطع پاسخ** با نتیجه unknown و read-back تازه.
- همان supervisorهای واقعی محصول استفاده می‌شوند؛ child relay مخصوص تست است. این JSON خصوصی **پروتکل ZK یا emulator سازگار با DLL نیست**. worker تولیدی به این relay وصل نمی‌شود. DB/HTTP/UI به این آزمایشگاه متصل نیستند؛ تست‌های مستقل موجود آن لایه‌ها جدا باقی‌اند.
- نقش/مجوز انسانی یا صفحه/endpoint/migration جدید ایجاد نشد؛ هیچ safety gate و حق سرور دور زده نشد.

## ایراد واقعی و اصلاح

Windows نشان داد parser سریال غلط را رد می‌کند اما Task.WhenAll ممکن است منتظر childِ blockشده روی pipe بدون reader بماند و به timeout برسد. `NativeInteraction.RunAsync` با اولین fault/cancel خارج می‌شود تا finally مالک supervisor child را kill کند؛ خطاهای aggregate بعدی observed هستند. در generic/attendance/user-read supervisor اعمال شد. نوشتن پس از dispatch همچنان unknown می‌ماند و automatic retry ندارد.

تست regression پس از header سریال غلط ۸۱۹۲ frame می‌فرستد؛ با الگوی قبلی واقعاً در Linux هم شکست خورد و با اصلاح، bridge-failed با صفر callback می‌دهد. انتظار تست به timeout ضعیف نشده است.

## شاهد نهایی کد

کد `b1df7dd3b590dc038ee576b6044c682c8b8414c4`:

- [Windows/Linux 43290520920](https://github.com/alirezarahimy-code/IO/actions/runs/43290520920): **success هر دو OS**؛ ۱۹ virtual +۶۱ portable قبلی، ۴۸ Foundation و۲۶ domain؛ lifecycle واقعی worker CI نیز سبز.
- [SQL/HTTPS/مرورگر 43290520893](https://github.com/alirezarahimy-code/IO/actions/runs/43290520893): **success**؛ ۶۶۷ integration موجود؛ آزمایشگاه مجازی در step مستقل نیز موفق.
- ۱۱۴ حق،۵۲ صفحه/راهنما،۳۷ migration،۶۰ جدول؛ عدد integration با۱۹ بررسی مجازی جمع یا جایگزین نشده است.

[راهنمای اجرای مجدد و چک‌لیست واقعی](../part-03/virtual-device-lab.fa.md) · [شاهد ماشین‌خوان](../part-03/virtual-device-lab-evidence.json) · [ماندهٔ همهٔ مراحل](../part-03/remaining-work.fa.md).

مدل/firmware/SDK واقعی، نوشتن تنظیمات پیشرفته و فرمان‌های حساس، RemoteEnroll/سیاست بیومتریک و vendor PUSH هنوز مانده‌اند. هیچ تضمین صفرخطایی، اجرای SDK یا پذیرش سخت‌افزار ساخته نشده است.
