# S08 — ارسال عادی non-TFT، قرارداد مستقل و بازیابی unknown

**پایان بخش سوم یا پذیرش مدل واقعی ادعا نمی‌شود.** فهرست مانده‌ها پیش از اجرا اعلام و بعد از این برش در remaining-work به‌روز شد.

- قرارداد SetUserInfo(Int32...) از بدنهٔ decompile فقط‌خواندنی Personnel.ZK.ZKDevice.UploadUsers و metadata IZKEM DispId41 مشاهده شد؛ باینری سورس اصلی نامیده نشده و مرجع تغییر نکرده است.
- bridge مستقل legacy فقط ordinary metadata: canonical Int32، بدون truncation/admin/AccGroup/بیومتریک/delete؛ password هدف فقط در حافظه، null قبل از mutation رد و read-back دقیق لازم است. مدل masked-password نباید تأیید شود.
- نوع از ReadFormat snapshot هدف و SnapshotId هر دو SQL immutable مشتق است؛ hash موجود SnapshotId را تثبیت می‌کند. preview/history/claim نوع را نشان می‌دهند؛ ستون mutable، migration، حق یا endpoint جدید اضافه نشده است.
- نصب UserWriteFormat جدا با defaultSSR می‌خواهد؛ نوع خواندن/نوشتن هر دو با claim برابر و flag مستقل ارسال پس از پذیرش مدل attendance-only لازم‌اند. read approval به‌تنهایی write approval نیست. credential برای claim با نوع جعلی صادر نمی‌شود.
- legacy با WriteLegacyUser مستقل و بدون WriteUser ارسال می‌شود؛ bridge قدیمی نمی‌تواند شاخهٔ SSR write را برای این پیام انتخاب کند. ارتقای هماهنگ لازم است؛ پاسخ incompatible مجوز retry نمی‌دهد.
- همان devices.users.upload/read/upload.execute و قالب‌های شناخته‌شده؛ grant/revoke جاری، CSRF، preview/hash و تأیید، conflict و pending cancellation پابرجا.
- نتیجه‌های batch مخلوط مستقل‌اند؛ unknown با شاهد تازهٔ قرارداد متفاوت review نمی‌شود. همان قرارداد جدید فقط evidence ثبت می‌کند، outcome قدیمی unknown می‌ماند و retry انتخابی intent تازه می‌خواهد.

## CI

کد `cce03b0ec2de6e5f5ac20f08cf676c3ea7472ff9`:

- [SQL/HTTPS/مرورگر 43300044652](https://github.com/alirezarahimy-code/IO/actions/runs/43300044652): **success،۶۸۷ integration**؛۱۰ SQL جدید.
- [Windows/Linux 43300044555](https://github.com/alirezarahimy-code/IO/actions/runs/43300044555): **success هر دو OS**؛۱۲ تست قرارداد و۴ تست stateful TCP جدید؛۶۱ portable قبلی،۱۰ خواندن،۱۹ virtual قبلی و۴۸ Foundation/۲۶ domain نیز سبز.
- ۱۱۴حق،۵۲صفحه/راهنما،۳۸migration و۶۰جدول؛ lifecycle worker در CI همچنان موفق.

TCP خصوصی JSON است، نه پروتکل ZK یا اجرای COM؛ به UI/SQL/worker انتهابه‌انتها متصل نیست. تنظیمات/شبکه و فرمان‌های حساس مدل‌محور، RemoteEnroll/بیومتریک، vendor PUSH امن و پذیرش واقعی چنددستگاه بازند.

[راهنما و پذیرش مدل](../part-03/s08-legacy-user-write.fa.md) · [شاهد](../part-03/s08-legacy-user-write-evidence.json) · [مانده](../part-03/remaining-work.fa.md).
