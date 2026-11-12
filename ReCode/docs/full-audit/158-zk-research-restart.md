# پژوهش ZKTeco و راه‌اندازی مجدد صریح دستگاه — S11

**بخش سوم هنوز کامل نیست؛ SDK و دستگاه واقعی اجرا نشده‌اند.**

- منابع رسمی جهانی/منطقه‌ای، راهنمای بازنشرشده و نمونه‌های اجتماعی مقایسه و درجهٔ اتکای هر منبع در [گزارش پژوهش](../part-03/zk-vendor-research.fa.md) ثبت شدند؛ متن نمایه‌شده با دریافت کامل یکسان نامیده نشده است.
- `RestartDevice(machine)` با metadata محلی `DispId(110)` و راهنمای SDK تطبیق داده شد. فقط یک فراخوانی در bridge Windows، بعد از pin مقصد/سریال؛ بدون raw fallback، reset، ClearGLog، ClearData، حذف template یا کنترل درب.
- UI موجود اطلاعات دستگاه، قرارداد بسته، صف SQL، migration۳۹ و worker واقعی تغییر کردند؛ این برش صرفاً تست simulator نیست.
- حقوق `devices.restart` و `devices.restart.execute` جداگانه قابل grant/revoke هستند و به هیچ نقش پیش‌فرض خودکار داده نمی‌شوند؛ حق سرویس عمومی و AllowRestart پیش‌فرض false نیز لازم‌اند.
- تأیید صریح، idempotency ثبت، نسخهٔ دستگاه و انقضای صف بعد از۱۲۰ثانیه؛ لغو فقط pending. false/قطع پس از dispatch و child timeout نتیجهٔ unknown می‌دهند؛ retry عمومی مجاز نیست.
- بازبینی با information.read تازه، همان دستگاه/نسخه/timezone و `REVIEWED-RESTART-REACHABILITY` تنها دسترسی مجدد را ثبت می‌کند؛ نتیجهٔ قبلی immutable و unknown می‌ماند. پایان reboot ادعا نمی‌شود.
- تغییر metadata/credential/group تا تعیین تکلیف unknown مسدود است. تاریخچهٔ restart، downgrade ناسازگار را رد می‌کند.

## شواهد اجرای نرم‌افزاری

کد `dfc565ac01c36969fb8a1e64ec62ac7df78903d2`:

- [Windows/Linux](https://github.com/alirezarahimy-code/IO/actions/runs/43305241514): **success** هر دو OS؛۱۱ بررسی قرارداد/فرایند restart و آزمون‌های قبلی موفق.
- [SQL/HTTPS/مرورگر](https://github.com/alirezarahimy-code/IO/actions/runs/43305241546): **success،۷۰۷ integration**؛۱۷ بررسی SQL جدید و۳ بررسی HTTPS جدید؛ آزمون DOM ثبت/بازبینی نوع restart و grant-only/revoke موفق.
- ۱۱۶حق،۵۲صفحه/راهنما،۳۹migration و۶۰جدول؛۴۸Foundation،۲۶domain،۶۱portable،۱۰read،۱۲legacy-write+۴TCP و۱۹virtual قبلی پابرجا.
- خطای ثبت route اولیه و فرض seed حق سرویس در fixture اصلاح و با CI جدید بازآزمایی شدند؛ موفقیت build محلی جای CI کامل گزارش نشده است.

[شاهد ماشین‌خوان](../part-03/s11-restart-evidence.json) · [نصب و اعطای حق](../part-03/device-worker-setup.fa.md) · [ماندهٔ دقیق](../part-03/remaining-work.fa.md).

نوشتن تنظیمات/شبکه، vendor PUSH امن، RemoteEnroll/انتقال بیومتریک و reset/حذف مخرب هنوز کامل نشده‌اند. پذیرش واقعی چنددستگاه/قطعی بر عهدهٔ آزمایشگاه تیم باقی است؛ این شواهد پایان بخش سوم نیستند.
