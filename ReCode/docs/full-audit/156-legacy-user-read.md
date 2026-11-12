# S07 — خواندن legacy non-TFT با هویت عددی و انتخاب صریح

قرارداد GetAllUserInfo(ref Int32 badge...) در بدنهٔ decompile فقط‌خواندنی Personnel.ZK.ZKDevice.WriteUserIDsToList و metadata IZKEM DispId47 مشاهده شد؛ باینری سورس نامیده نشده و مرجع ویرایش نشده است.

- bridge Windows مسیر خواندنی مستقل legacy دارد؛ default SSR، بدون auto-detect یا fallback. password از session خارج نمی‌شود و card رشته‌ای حفظ می‌شود.
- ReadFormat در SQL از درخواست تا claim/history/preview ثابت و در start frame legacy اجباری است؛ نصب worker باید UserReadFormat دقیقاً برابر داشته باشد. نوع ناشناخته در تنظیم نصب رد می‌شود.
- Int32 غیرمنفی invariant است؛ 1 و001 یکی فرض نمی‌شوند. frame/service با legacy صفرگذاری‌شده، overflow یا علامت رد می‌شود. نگاشت/employee بدون اختیار/تأیید مستقل تغییر نمی‌کند.
- snapshot هدف legacy نمی‌تواند SSR upload preview یا unknown review را مجاز کند؛ worker نوشتن روی endpoint legacy را نیز مسدود می‌کند. نوشتن non-TFT هنوز ساخته نشده است.
- همان حقوق devices.users.read/download/execute، قالب operator خواندنی/admin دریافت و service مستقل بدون قالب انسانی. select فرم در busy/revoke بسته و در پاسخ نامعلوم payload ثابت می‌ماند؛ help هم‌زمان کامل شد.
- migration جدید20261112150000 با defaultSSR، CHECK نوع/طول و trigger نسخه‌دار؛ downgrade با سابقهlegacy خطای51028. schema پس از تست guardedDOWN تنظیمات دوباره latest می‌شود؛ میزبان محصول خودکار migrate نمی‌کند.

## CI

کد `50087b92919dd0ce18ae258893c91bf41bcc0cd1`:

- [SQL/HTTPS/مرورگر 43295571681](https://github.com/alirezarahimy-code/IO/actions/runs/43295571681): **success،۶۷۷ integration**؛۸ SQL و۲ HTTP جدید.
- [Windows/Linux 43295571689](https://github.com/alirezarahimy-code/IO/actions/runs/43295571689): **success هر دو OS**؛۱۰ تست قرارداد portable جدید،۶۱ مرزی قبلی و۱۹ virtual؛۴۸ Foundation و۲۶ domain و lifecycle واقعی worker CI.
- ۱۱۴حق،۵۲صفحه/مقاله،۳۸migration و۶۰جدول. مدل واقعی یا COM SDK اجرا نشده؛ part3Complete=false.

[راهنما و نصب](../part-03/s07-legacy-users.fa.md) · [شاهد](../part-03/s07-legacy-users-evidence.json) · [مانده](../part-03/remaining-work.fa.md).
