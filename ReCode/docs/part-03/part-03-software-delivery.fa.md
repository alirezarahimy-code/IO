# تحویل نرم‌افزاری بخش۳ — قراردادهای منتخب و مرز پذیرش واقعی

## وضعیت نهایی این تحویل

**R01–R15 در دامنهٔ قراردادهای مستند نرم‌افزاری پذیرفته‌اند؛ R16 تعیین‌تکلیف پشتیبانی شده است.** در همین دامنه، ماندهٔ فنی حل‌نشده۰ و بستهٔ مستقل پذیرش واقعی **H01 باز** است. این نتیجه «همهٔ قابلیت‌های مرجع بدون محدودیت ساخته شده‌اند» یا پذیرش تمام مدل‌ها نیست:

```text
softwareComplete = true   // selected contracts plus explicit unsupported dispositions only
fullLegacyFeatureParity = false
hardwareVerified = false
part3Complete = false    // real Windows/SDK/multidevice H01 remains open
```

[شمارنده و تاریخچهٔ قابل پیگیری](remaining-count-ledger.fa.md) · [شاهد نهایی SHA/job/شمار واقعی](r15-r16-software-evidence.json) · [progress ماشینی](progress-evidence.json).

## دو ردیف آخر چه شدند؟

### R15 — پیاده‌سازی کامل مسیر منتخب، نه factory reset

مسیر مستقل SQL/API/UI/worker/STA bridge برای `ClearData(machine,5)` ساخته و پذیرفته شد: دو archive کامل و بادوامِ metadata کاربران و تمام native attendance با duplicateها؛ پیش‌نمایش متصل به کل قصد و نسخه‌ها، مجوز/allowlist مستقل، دو انسان، صف immutable و fence مشترک تمام native queues؛ دو preflight کامل، یک فرمان و سپس اتصال خواندنی با همان IP/port/key، خالی‌بودن کامل کاربران/تردد و probe ده اسلات هر شناسهٔ قبلی برای orphan fingerprint. false/exception/deadline/ناقص‌بودن نتیجه، unknown بدون replay است؛ recovery صرفاً observation خواندنی و امضای مستقل رفع fence با حفظ unknown اصلی است.

دامنه فقط پروفایل صریحاً مصوب کاربران تردد/رمز/کارت/مدیران/ده اسلات fingerprint/تردد و فاقد storeهای دیگر است. **factory reset پیاده نشده و پشتیبانی نمی‌شود**؛ نام DeleteAllData در مرجع اثبات آن نیست. تغییر همهٔ تنظیمات، secure media erase و raw backup/restore نیز ادعا نشده است. [راهنمای کامل نصب/اجرا/بازیابی R15](r15-all-data-clear.fa.md).

### R16 — تعیین‌تکلیف صریح عدم پشتیبانی، نه ساخت adapter مستقیم

**اتصال مستقیم USB/serial در این نسخه پشتیبانی نمی‌شود؛ adapter آن ساخته نشده است.** متدهای واقعی Connect_USB و Connect_Com در مرجع و دو TypeLib بررسی شدند؛ COM1/115200 مرجع، انتخاب/مالکیت امن پورت نصب نیست. این تصمیم محافظه‌کارانهٔ دامنهٔ محصول است، نه ادعای فقدان قابلیت دستگاه یا ناشی از Windows/Linux بودن توسعه.

فقط شبکهٔ مصوب SDK و دریافت فایل با قالب صریح عرضه می‌شوند؛ USB-file اتصال کابل نیست. قرارداد support machine-readable، GET خواندنی مجوزدار `/devices/transport-support`، جلوگیری از profile/hostname محلی و تزریق Transport، UI/help و آزمون‌ها اضافه شدند. هر افزودن transport مستقیم به پیاده‌سازی و پذیرش نرم‌افزاری تازه نیاز دارد؛ switch فعلی یا H01 به‌تنهایی آن را ایجاد نمی‌کند. [تصمیم و شواهد و دامنهٔ R16](r16-direct-transport-disposition.fa.md).

## پذیرش واقعاً اجراشده

کد نهایی عملیاتی **`b6791a58991603d2c099414b3e1ac9a8d6e17d53`**:

| بررسی | نتیجهٔ واقعی |
|---|---|
| [Windows/LocalDB/HTTPS/worker](https://github.com/alirezarahimy-code/IO/actions/runs/43713356207) |۳۵۵ بررسی یکپارچه موفق؛ ۲۹۶ regression قبلی +۴۶ R15 (۳۹ SQL/worker و۷ HTTPS) +۱۳ R16 (۱۱ SQL و۲ HTTPS) |
| mapper/executor/supervisor R15 |۴۸ بررسی مصنوعی جدید، production mapper با flag۵؛ بدون COM/SDK یا حذف واقعی |
| مرز پشتیبانی R16 |۲۶ بررسی جدید policy/strict IPC؛ نه تست driver یا اتصال مستقیم |
| regression قراردادهای قبلی |۳۴ protected،۴۸ هستهٔ R13،۳۴ بازه،۲۳ حذف همهٔ تردد،۶۳ بیومتریک/EFS و۴۷ gateway/enroll موفق |
| [UI همان SHA](https://github.com/alirezarahimy-code/IO/actions/runs/43713356176) |۲۶ فایل DOM و preview مستقل Chromium و syntax/audit در Windows موفق؛ مرورگر این preview، اجرای واقعی دستگاه نیست |
| ترتیب |R15 ابتدا در f18374b با Windows۳۴۲ و UI موفق شد؛ سپس disposition/کنترل‌های R16 با۱۳ بررسی یکپارچهٔ بیشتر پذیرفته شد |

Linux طبق دستور کاربر اجرا نشد. suite کامل تاریخی SQL۱۰۳۱ و rollback کامل دوباره اجرا نشدند؛ شمار تاریخی به تست تازه نسبت داده نمی‌شود. SDK/COM، دادهٔ زیست‌سنجی واقعی، حذف واقعی یا دستگاه واقعی اجرا نشده‌اند. TypeLib/hash و doubleها، اثر firmware نصب‌شده را ثابت نمی‌کنند.

## قابلیت‌هایی که نباید از شمار۰ استنتاج شوند

- factory reset و مستقیم USB/serial ساخته نشده‌اند؛ R16 صرفاً disposition مستند است.
- حذف broad برای face/photo/palm/vein/door/hidden stores یا اسلات اضافه در قرارداد R13–R15 نیست؛ مسیر انتقال face/photo در R10، مجوز حذف آن‌ها نیست.
- gateway احرازشده، native cryptographic PUSH authentication یا مجوز خودکار مدل/سیاست raw نیست.
- کنترل انحصاری واقعی، توقف capture همهٔ کانال‌ها، اثر native، مجوز قانونی/retention/حذف غیرقابل‌بازیابی و صلاحیت نصب، از دو scan یا فیلد متن اثبات نمی‌شوند.
- هیچ capability مخرب/biometric/مدل‌محور برای نصب واقعی خودکار روشن نشده؛ **default-off حفظ شده**. نقش‌های پیش‌فرض حقوق مخرب تازه را ارث نمی‌برند.

## تنها بستهٔ بازِ پذیرش نهایی: H01

[تحویل مرحله‌به‌مرحلهٔ آزمایشگاه Windows/SDK/دو دستگاه واقعی](h01-final-windows-lab-handoff.fa.md) آماده است: هویت/معماری/کلید و ACL، نصب SCM، دو pin مستقل، دریافت و تبادل، قطعی و restart/revoke، polling و فایل واقعی، تنظیمات مصوب، سپس حذف‌های آزمایشگاهی و unknown recovery واقعی. بدون اجرای آن و امضای مسئولان مجاز، **بخش۳ کامل یا آمادهٔ حذف عملیاتی اعلام نمی‌شود**. در این محیط این آزمایش انجام نشده است؛ گزارش و سابقهٔ CI فقط شاهد نرم‌افزارند.


### ادامهٔ آماده‌سازی H01

[ابزار آفلاین نمایهٔ شواهد](h01-evidence-index-tool.fa.md) و template دو دستگاه اضافه شده‌اند. آن‌ها بدون دسترسی شبکه/SDK، کامل‌بودن ساختاری سناریوها/رسیدها/تأییدها را بررسی می‌کنند و هیچ pass سخت‌افزاری یا پذیرش خودکار تولید نمی‌کنند؛ H01 همچنان باز است.
