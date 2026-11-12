# S11 — تنظیم محدود زمان بیکاری با تأیید دقیق مدل

**بخش سوم کامل نیست؛ COM یا دستگاه واقعی اجرا نشده است.**

- پژوهش: کد۴ در متن منطقه‌ای SDK برحسب دقیقه و standby/power-off است؛۱..۹۹۹ در راهنمای رسمی EFace10 فقط envelope برنامه می‌دهد، نه سازگاری عمومی یا تأیید نگاشت COM. صفر/disabled و کد۸ حدس زده نشد.
- مسیر واقعی همان صفحه/صف SQL/worker/bridge؛ طرح immutable با snapshot، serial، محصول، firmware، قدیم/جدید و مرجع؛ preview/hash سرور، تأیید مستقل احتمال خاموشی، CSRF و دو حق تازه default-ungranted.
- نصب پیش‌فرض خاموش؛ approval دقیق مدل/firmware/مقادیر/مرجع. preflight زندهٔ descriptor و مقدار قدیم، حداکثر یک SetDeviceInfo کد۴، success فقط با typed read-back دقیق. false/exception/mismatch/deadline پس از dispatch unknown؛ بدون RefreshData/restart/disable/fallback/retry.
- unknown review با settings snapshot بعدی، همان نسخه/zone/serial/model/firmware و مقدار قدیم یا جدید؛ unknown و reviewer حفظ می‌شوند. migration۴۱ و trigger، تاریخچه را تغییرناپذیر نگه می‌دارند؛ downgrade داده‌بر با51031 متوقف می‌شود.

## نتیجهٔ CI

کد `9fff2cf32539e6f5028432976dc6183fdedc1bc7`:

- [Windows/Linux](https://github.com/alirezarahimy-code/IO/actions/runs/43311610211): **success** هر دو OS.
- [SQL/HTTPS/DOM](https://github.com/alirezarahimy-code/IO/actions/runs/43311610215): **success،۷۶۸ integration**؛۲۹ SQL و۳ HTTPS جدید،۲۳ قرارداد/فرایند و DOM جدید preview/invalidation/consent/exact replay/review/revoke.
- ۱۲۲حق،۵۲صفحه/راهنما،۴۱migration و۶۰جدول. CSV مجوزها نیز از کاتالوگ واقعی به‌روز شد؛ عنوان/حقوق صفحه با guide منطبق است.

[راهنمای کامل](../part-03/s11-idle-minutes.fa.md) · [شاهد ماشین‌خوان](../part-03/s11-idle-minutes-evidence.json) · [مانده](../part-03/remaining-work.fa.md).

سایر تنظیمات، PUSH امن، RemoteEnroll/بیومتریک، reset/حذف با حقوق مستقل و پذیرش واقعی چنددستگاه/قطعی/تبادل بازند.
