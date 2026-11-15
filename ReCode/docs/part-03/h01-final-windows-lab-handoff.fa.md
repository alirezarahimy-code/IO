# H01 — تحویل پذیرش پایانی واقعی Windows/SDK/چنددستگاه

**وضعیت: اجرا نشده، باز.** تکمیل نرم‌افزاری مسیرهای مصوب و تعیین‌تکلیف صریح قابلیت‌های پشتیبانی‌نشده، H01 را نمی‌بندد. هیچ heartbeat، publish، TypeLib، EFS دورریختنی، child مصنوعی، TCP private simulator یا Windows GitHub runner، جای دستگاه واقعی نصب‌شده نیست. `hardwareVerified=false` و `part3Complete=false` تا تکمیل این بسته حفظ می‌شوند. انجام این آزمایش در مخزن/محیط توسعه، بدون دستگاه و اختیار واقعی نصب و سازمان، ادعا نمی‌شود.

این سند بستهٔ اجرایی آخر کار است؛ پرسش تکراری برای ادامهٔ توسعه یا مجوز ضمنی حذف واقعی نیست. طبق تصمیم کاربر، فقط اکنون دروازهٔ نصب/دستگاه بعد از مسیرهای نرم‌افزاری می‌آید. [تفکیک دو دروازه](software-and-lab-acceptance.fa.md)، [راهنمای نصب و محدودیت‌های worker](device-worker-setup.fa.md)، [ledger جاری](remaining-count-ledger.fa.md).

## ورودی‌های غیرمحرمانه و پیش‌شرط‌های محل نصب

- حداقل دو دستگاه آزمایشگاهی مستقل با serial متمایز، product/model/firmware و قالب‌های دقیق مصوب. آزمون یک مدل به همهٔ مدل‌ها تعمیم نمی‌یابد؛ اگر SSR و legacy هر دو استقرار هدف‌اند، هر دو در ماتریس باشند.
- ماشین واقعی Windows و حساب سرویس غیرتعاملی، runtime صحیح child و SDK دارای مجوز/installer سازنده و معماری متناظر. دو DLL مرجع موجود فقط شاهد static هستند، نه installer معتبر یا مجوز redistribution.
- SQL رمز‌شده، identity فعال سرویس و grant حداقلی، ACL binary/config/runtime/key-ring، پشتیبان و نگهداری کلید مصوب. رمز، token، raw template/چهره/عکس یا فایل config واقعی به چت/Git/گزارش عمومی نرود؛ secret provider/ابزار امن استقرار استفاده شود.
- دادهٔ ساختگی/آزمایشگاهی مجاز و جداسازی از دستگاه عملیاتی و payroll/کنترل درب. هر نوشتن/حذف فقط با اختیار واقعی مسئول مجاز سازمان و قرارداد اثر/retention همان نصب؛ توافق توسعهٔ مجازی این اختیار نیست.
- همهٔ capabilityهای مخرب و biometric/PUSH/نوشتن مدل‌محور خاموش؛ فعال‌سازی فقط یکی‌یکی برای قرارداد نصب دقیق. scoped grants، دو انسان مستقل برای صف‌های مخرب و allowlist endpoint/device/version باید واقعی باشند.

## ترتیب اجرا؛ هیچ حذف زودهنگام

| گام | اجرای واقعی و شاهد لازم | معیار قبول / حالت عدم قبول |
|---|---|---|
| H01-01 نصب | publish قابل ردیابی، نصب SCM طبق راهنما با حساب و ACL واقعی، start/stop/restart و خروج child هنگام stop | فایل اجرایی/معماری/key-ring/SQL صحیح؛ ValidateOnly یا نمونهٔ unavailable DB فقط مقدماتی است |
| H01-02 دو هویت | ثبت دو دستگاه و pin مستقل، اتصال شبکه/SDK، تلاش endpoint/serial نادرست و scope محدود | فقط نصب مورد انتظار، بدون credential leakage یا cross-device result؛ TCP reachability کافی نیست |
| H01-03 دریافت | کاربران و تردد کامل هر دو دستگاه از UI→SQL→worker→SDK، duplicate/کد ناشناس/شناسه leading-zero، قالب SSR/legacy صریح | کامل‌بودن/contiguity/multiplicity و readback قابل ردیابی؛ raw SDK archive از ingestion فیلترشده جدا |
| H01-04 قطعی و تکرار خواندن | قطع واقعی یکی از دستگاه‌ها حین دریافت، حفظ کار دستگاه دوم، restart سرویس و دریافت دوباره | partial/terminal درست، حفظ اصل tuple و dedup مصوب؛ no silent mixed format یا retry نوشتن |
| H01-05 polling و فایل | pause/resume/revoke و overlap روی شبکهٔ واقعی؛ نمونهٔ فایل واقعی بی‌نام طبق layout/تقویم/zone مصوب | قرنطینهٔ زمان مبهم/خراب و حفظ bytes، نه universal USB layout یا اتصال کابل |
| H01-06 تنظیمات | snapshot/read و setter هر پارامتر جدا با اثر/دامنهٔ مدل، readback و برنامهٔ بازگشت شبکه | IP/port/MAC/mask/gateway/DHCP واقعی و lease/reconnect، DLST/الگوریتم/کلیدها فقط قرارداد همان نصب؛ عدد raw یا ACK کافی نیست |
| H01-07 کاربران عادی | ارسال مصوب فردی/چند نفر/دو دستگاه، no-overwrite، حفظ رمز/admin/group/template و خطای جزئی | هر نفر/دستگاه مستقل، late ACK unknown، بدون overwrite یا truncation خاموش |
| H01-08 PUSH/enroll/biometric | فقط اگر برای نصب مصوب‌اند، gateway احرازشده و مرز native≠gateway، enrollment و انتقال واقعی با policy/قالب/retention مستقل، EFS خصوصی/پاکسازی در صورت عکس | event واقعی enrollment، readback/حفظ داده، revoke و deadline؛ timeout موفقیت نیست؛ raw در report نرود |
| H01-09 R11/R12 | پس از اخذ کامل native و کنترل فیزیکی واقعی capture، حذف همه یا بازهٔ آزمایشگاهی مستقل؛ مرزهای محلی/DST/ثانیهٔ همان firmware | multiset/readback کامل، حفظ خارج بازه و duplicateها؛ count ثابت، CAS یا توقف capture نیست |
| H01-10 R13/R14 | profile واقعاً فاقد face/photo/door/hidden stores و ده اسلات؛ اثر flagهای API و ClearAdministrators مدل با دادهٔ ساختگی | R13 فقط هدف عادی/اسلات‌های مصوب و حفظ کامل خارج هدف؛ R14 فقط privilege۰ و حفظ رمز/کارت/template/اشخاص؛ اثر دیگر unsupported |
| H01-11 R15 آخرین حذف | فقط دستگاه آزمایشگاهی مجاز، دو archive کامل، اثر۵ و endpoint/key مصوب، دو انسان؛ پس از آن اتصال با همان endpoint/key | خالی‌بودن کامل profile کاربران/تردد و ده orphan probe هر فرد قبل؛ **نه factory reset** یا secure media erase |
| H01-12 بازیابی و revoke | سناریوی مصوب loss-of-ACK/cancel/kill روی دادهٔ آزمایشگاهی، توقف child و reset غیرحذفی، archive و observation تازه، امضای مستقل | unknown اصلی immutable، ممنوعیت replay، رفع fence فقط بعد از evidence/observation همان خانواده؛ لغو scope/policy مانع dispatch/late success شود |

هیچ گام مخربی برای اثبات «فقط ارتباط» لازم نیست. اگر اختیار/مدل/profile لازم در یک گام وجود ندارد، **not-run/unsupported با علت واقعی** ثبت و قابلیت آن نصب خاموش بماند؛ pass اختراعی یا پذیرش کلی از روی یک زیرگام ممنوع است. بازگرداندن raw template از archive SQL در R13/R15 ممکن نیست و روال backup/restore جدید نباید فرض شود.

## دامنهٔ صریحاً پشتیبانی‌نشدهٔ این تحویل

- factory reset فرمان پیاده‌شده ندارد؛ ClearData۵ را به آن تغییر نام ندهید.
- [USB/serial مستقیم R16](r16-direct-transport-disposition.fa.md) در این نسخه unsupported-in-this-delivery است؛ SDK متد واقعی دارد ولی driver/physical endpoint ownership/adapter این محصول ساخته نشده. parser فایل، اتصال مستقیم یا fallback به COM1 نیست. این یک برش نرم‌افزاری تازه می‌خواهد، نه صرفاً ثبت pass در H01.
- storeهای face/photo/palm/vein/door/hidden و اسلات اضافه در قراردادهای broad deletion R13–R15 نیستند؛ مسیر انتقال بیومتریک R10 این حذف را مجاز نمی‌کند.
- native cryptographic PUSH authentication از gateway-HMAC نتیجه نمی‌شود؛ کنترل درب و payroll هم اضافه نشده‌اند.

## ابزار آماده‌سازی و بررسی نمایهٔ شواهد

[ابزار آفلاین H01](h01-evidence-index-tool.fa.md) و [template خالی](h01-evidence-index.template.json) آماده‌اند:۱۲ سناریو برای هر یک از حداقل دو دستگاه، زیرچک‌ها، رسیدها، مجوزهای مستقل و reference شواهد خصوصی را ثبت/بررسی می‌کنند. ابزار فقط ساختار را بررسی می‌کند؛ نه SDK/دستگاه را اجرا، نه اصالت شواهد را تأیید و نه H01 را می‌بندد. اطلاعات واقعی را در Git یا چت وارد نکنید. صفر بودن کد خروج صرفاً آمادگی index برای بازبین انسانی است؛ وضعیت واقعی این بسته همچنان «اجرا نشده، باز» است.

## رسید هر سناریو و شرط بستن بسته

برای هر گام: build SHA و hash بستهٔ نصب، شناسهٔ سناریو، زمان UTC، نسخهٔ SDK/runtime/Windows و مدل/firmware و شناسهٔ مستعار هر دستگاه، UUID صف/نسخه/receiptهای نرم‌افزار، pre/post count و state، هویت نقشِ اجرا/بازبین مستقل، نتیجهٔ pass/fail/not-run/unsupported و علت؛ بدون رمز یا raw biometrics. شاهد اثر فیزیکی/سیاست قانونی و مجوز retention باید در سامانهٔ امن سازمان نگهداری و فقط reference آن ثبت شود؛ فیلد text نرم‌افزار اصالت آن را ثابت نمی‌کند.

تا تمام زیرسناریوهای لازم برای ماتریس نصب تعیین‌تکلیف، تفاوت‌ها رفع/unsupported معتبر، بازیابی و کلید/ACL واقعی پذیرفته و مسئولان مجاز امضا نکرده‌اند، H01 باز و part3Complete=false است. پس از آن شاهد جدید جدا ثبت شود؛ هیچ شاهد CI قبلی را برای ادعای اجرای واقعی بازنویسی نکنید. تست Linux طبق خواست کاربر در این تحویل اجرا نشده؛ H01 هم آزمون Windows واقعی است.
