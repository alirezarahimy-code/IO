# S10 — زمان‌بندی مصوب خواندنی و run واقعی SQL

**بخش سوم هنوز کامل نشده است.** این جزء نرم‌افزاری polling است، نه PUSH پروتکل دستگاه یا پذیرش سخت‌افزار.

- `DevicePollSchedules`, `DevicePollScheduleRecord/DevicePollRunRecord` و migration `20261112090000_DevicePollSchedules`: config مصوب immutable، cursor شامل/پایان غیرشامل، target snapshot و FK دانلود واقعی.
- tick مستقل SQL، صف S09 و worker بیرون وب/transaction؛ شش صف منصفانه و opt-in `EnableSchedules`؛ همهٔ اجازه‌های native پیش‌فرض خاموش.
- اجازهٔ سازنده در هر tick بازبینی؛ mode فقط‌خواندن یا auto-ingest صریح با commit/receive مستقل، ۵۰ tuple در هر صفحه و receipt هستهٔ موجود raw/conflict. ناشناس/خطا قرنطینه می‌ماند.
- pause pending را لغو می‌کند، running را پس نمی‌گیرد؛ run بسته و ثبت خودکار/cursor آن متوقف می‌شود. resume فقط سازنده با اختیار فعلی و همان snapshot. backoff صرفاً خواندنِ شکست‌خورده است؛ نوشتن/پاکسازی/restart/enroll انجام نمی‌شود.
- `/workspace/device-schedules` با سه حق مستقل read/manage/execute، قالب نقش شناخته‌شده، grant/revoke واقعی، کمک فیلد/دکمه و UI امن.
- SQL history guard 51022، cursor فقط با run واقعیِ موفق و در حالت auto با تمام صفحات بررسی‌شده؛ terminal evidence و history حذف/بازنویسی نمی‌شود.

## CI کد دقیق

`5477723634c0d558db0b59917d7c4f72b26f9a44`:

- SQL/HTTPS/مرورگر **43228450432 success**؛ **۵۹۹ PASS** شامل ۲۰ سرویس و ۵ HTTP جدید S10، DOM و مرورگر pause/resume/reload.
- Windows/Linux **43228450449 success**؛ **۴۵ Foundation، ۲۶ domain، ۵۵ portable**.
- در همین commit: **۱۰۸ حق، ۴۹ صفحه/مقاله، ۳۲ migration و ۵۸ جدول**.

[شاهد ماشین‌خوان](../part-03/s10-component-evidence.json) · [راهنما و نصب/نقش/بازیابی](../part-03/s10-sdk-schedules.fa.md).

PUSH احرازهویت‌شده/replay/rate پروتکل مدل، مدل/firmware و SDK واقعی، تنظیمات پیشرفته/RemoteEnroll و نصب/سناریوی چنددستگاه واقعی باقی‌اند. serial pin رمزنگاری هویت نیست؛ آمار SDK atomic snapshot نیست؛ window/lag تضمین رخداد دیررس در بازهٔ پیش‌تر تأییدشده نیست. S09 identity محتوا/رتبه است، نه رخداد فیزیکی. payroll و درب خارج دامنه‌اند.
