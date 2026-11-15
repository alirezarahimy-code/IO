# R16 — پیاده‌سازی واقعی مسیر SDK مستقیم؛ برش اتصال و تبادل

## تغییر دامنه با دستور جدید کاربر

کاربر صریحاً خواست نبود سخت‌افزار مانع ساخت نرم‌افزار نباشد و قابلیت‌های لازم پیاده شوند؛ بنابراین تصمیم قبلی «unsupported-in-this-delivery» USB/serial پایان کار نیست. **R16 برای پیاده‌سازی باز شده است.** این سند وضعیت جاری را بر [تصمیم تاریخی](r16-direct-transport-disposition.fa.md) مقدم می‌کند. H01 همچنان به‌دلیل آماده‌نبودن محیط واقعی معوق است؛ هیچ تأیید فیزیکی یا بستن کل بخش۳ از این برش نتیجه نمی‌شود.

## آنچه واقعاً ساخته شد

- قرارداد private IPC جدید `ZkLocalTransport` با kind صریح usb/serial، نام canonical بزرگ‌حرف ایستگاه Windows، COM number و baud صریح، reference نصب driver و انتخاب انحصاری و تأیید مستقل نصب. property ناشناخته، device path، COM متنی به‌جای station، baud حدسی، USB با port غیرصفر و تطبیق‌نداشتن endpoint رد می‌شوند. ارتباط شبکهٔ قبلی بدون این property رفتار سابق را حفظ می‌کند؛ fallback وجود ندارد.
- mapper تولیدی `ZkSdkConnection`: **Connect_USB(machine)** یا **Connect_Com(port,machine,baud)**؛ شبکه فقط Connect_Net است. flag یا انتخاب transport از فرم عملیات دریافت نمی‌شود؛ فقط config نصب قابل اعتماد و registration متناظر.
- lease در ابتدای child و پیش از ساخت SDK، روی کل عمر یک درخواست و همهٔ اتصال‌های آن: mutex سراسری Windows مستقل از UUID دستگاه، بر اساس کل selector USB ایستگاه یا شمارهٔ canonical COM. قفل non-waiting، عدم reentrancy منطقی، رد ایستگاه دیگر و USB selector مشترکِ دو دستگاه در یک child؛ خطای abandoned بدون اجرای همان درخواست رد می‌شود. این قفل فقط IOهای همکار است، **اثبات قفل سخت‌افزاری در برابر نرم‌افزار vendor/حساب دیگر یا توقف capture نیست**. انحصار واقعی محل نصب مستقل لازم است؛ SDK اجازهٔ انتخاب USB با serial دلخواه به این API نمی‌دهد.
- ثبت واقعی دو profile `zk-usb-direct` و `zk-serial-direct` در API/UI/SQL. Host نام ایستگاه است، **نه IP ساختگی**؛ Port برای USB صفر و برای serial شمارهٔ COM۱..۲۵۶ است. Baud فقط config نصب، نه port شبکه و نه fallback به COM1/115200 مرجع. برای USB فقط یک دستگاه مصوب متصل به selector SDK همان ایستگاه مجاز است؛ هویت سریال بلافاصله پس از اتصال قبل از عملیات pin می‌شود.
- migration۵۸ `20261117120000_LocalSdkTransports`: قید SQL مستقل شکل endpoint و unique index سراسری `(Host,Port)` برای local profiles، حتی registration غیرفعال؛ alias جدید نمی‌تواند fence نتیجهٔ unknown ثبت قبلی را دور بزند. downgrade با registration محلی ممنوع است؛ داده را برای downgrade حذف نکنید.
- worker چهار slot قبلی، صف/مجوز/نسخه/credential/scopes و complete/unknownهای قبلی را استفاده می‌کند. station واقعی Windows و profile/Host/Port ثبت‌شده پیش از credential/dispatch تطبیق داده می‌شوند. Web هرگز COM بار نمی‌کند. username/رمز/raw material وارد command line یا گزارش پشتیبانی نمی‌شود.

## ماتریس این برش؛ مانده‌ها پنهان نمی‌شوند

| خانواده | شبکهٔ قبلی | USB/serial این برش |
|---|---|---|
| probe و serial pin، اطلاعات، settings snapshot | برقرار | مسیر تولیدی مستقیم ساخته شد |
| ساعت و restart با حق/تأیید مستقل | برقرار | همان صف و قرارداد با اتصال مستقیم؛ اثبات reboot از ACK نتیجه نمی‌شود |
| دریافت کامل کاربران SSR/legacy | برقرار | همان streaming executor و SQL archive، قالب صریح و حفظ leading zero |
| استخراج تردد SSR/legacy، polling، دریافت مجدد/قرنطینه | برقرار | همان tuple/multiplicity/ingestion جدا؛ parser فایل همچنان مستقل |
| ارسال metadata کاربر عادی با preview و no-overwrite | برقرار | worker و mapper تولیدی مستقیم؛ پوشش مستقل نوشتن/unknown و بازبینی تازه؛ بدون حق template/admin/door ضمنی |
| setterهای تخصصی profile/raw/function-key و recovery شبکه | برقرار تحت قرارداد مستقل | **هنوز این برش مجاز نمی‌کند؛ ادامهٔ نرم‌افزاری R16 باز است** |
| enroll، انتقال biometric، حذف R11–R15 و recovery آن‌ها | برقرار تحت قرارداد مستقل | **هنوز این برش مجاز نمی‌کند**؛ private input هم transport محلی را صریحاً رد می‌کند، نه dispatch ناقص یا fallback شبکه |
| factory reset | ساخته نشده؛ ClearData۵ نیست | فرمان/اثر معتبر جدا لازم است؛ API حدسی ساخته نمی‌شود |

بنابراین `SoftwareRouteAvailable=true` در `/devices/transport-support` به معنی خانواده‌های جدول بالاست، نه همهٔ فرمان‌های SDK روی هر transport. وضعیت `implemented-bounded-local-exchange` عمداً با پشتیبانی نامحدود متفاوت است. R16/تکمیل نرم‌افزارِ دامنهٔ گسترش‌یافته هنوز بسته نمی‌شود.

## تنظیمات نصب نمونه — غیرفعال تا قرارداد واقعی

در NativeEndpoints همان worker، علاوه بر DeviceId/کلید حفاظت‌شده/bridge و key-ring مطمئن و grantهای مستقل، endpointی با Address برابر نام ایستگاه و Port مطابق ثبت اضافه شود. برای serial مثال ساختاری زیر است؛ مقادیر مجوز/driver/reference نمونه **تأیید واقعی نیستند** و false باید تا احراز واقعی محل حفظ شود:

```json
{
  "Address": "LABHOST",
  "Port": 17,
  "MachineNumber": 7,
  "ExpectedSerial": "SET-EXACT-INSTALLED-SERIAL-PRIVATELY",
  "LocalTransport": {
    "Kind": "serial",
    "StationName": "LABHOST",
    "SerialPort": 17,
    "BaudRate": 57600,
    "DriverReference": "SET-PRIVATE-DRIVER-EVIDENCE-REFERENCE",
    "ExclusiveSelectionReference": "SET-PRIVATE-PHYSICAL-OWNERSHIP-REFERENCE",
    "ExclusiveAccessApproved": false
  }
}
```

برای USB، Kind=usb و Port/SerialPort/BaudRate همگی صفر، با شاهد انتخاب تک‌دستگاه و انحصار کل selector ایستگاه. raw serial/key/config واقعی به Git یا چت نرود. صرف نام StationName هویت رمزنگاری‌شدهٔ میزبان نیست؛ ACL و استقرار هماهنگ/حساب سرویس از راهنمای قبلی لازم‌اند. اگر Windows station عوض شد، config/registration و اختیار نصب باید صریحاً بازبینی شوند؛ اسکن/انتخاب خودکار وجود ندارد.

## آزمون و مرز پذیرش

تست‌های جدید connector تولیدی با SDK double و mutex واقعی Windows، منع مالک دوم در process مستقل، ورودی‌های نامعتبر، stream کاربران/تردد و عدم dispatch فرمان‌های هنوز پشتیبانی‌نشده را بررسی می‌کنند. تست یکپارچه نیز registry/constraint و worker واقعی/credential/child مصنوعی/SQL را برای USB و serial جدا اجرا می‌کند؛ این **اجرای driver یا دستگاه نیست**. نتیجهٔ نهایی این برش در شاهد مقید به SHA زیر ثبت شده؛ شمار تاریخی۳۵۵ به‌تنهایی جای اجرای تازه را نمی‌گیرد. Linux طبق دستور کاربر تست نمی‌شود. H01 معوق و `hardwareVerified=false` و `part3Complete=false` باقی‌اند.


## ارسال مستقیم عادی و بازیابی نتیجهٔ نامعلوم

- mapper واقعی نوشتن از بدنهٔ Program به `IO.ZkBridge/ZkComUserWriter.cs` استخراج شد؛ bridge تولیدی و تست source-linked دقیقاً همین کد را اجرا می‌کنند. اتصال با `ZkSdkConnection` و lease قبلی است؛ SSR فقط `SSR_SetUserInfo` و legacy فقط `SetUserInfo` دارد، بدون fallback.
- هر نوشتن همچنان snapshot مصوب/قالب تغییرناپذیر، preview/hash دقیق، مجوز انسانی و سرویس، mapping جاری و تأیید مستقل نصب `AttendanceOnlyUserWriteApproved` می‌خواهد. قالب‌های `UserReadFormat` و `UserWriteFormat` باید با snapshot برابر باشند؛ روشن‌کردن اتصال مستقیم، تأیید نوشتن نیست.
- پیش از mutation، serial و شمارش کامل اسکن دوباره کنترل می‌شوند؛ conflict، duplicate، اسکن ناقص یا password نامعلوم نمی‌تواند به «کاربر وجود ندارد» یا پاک‌کردن رمز تبدیل شود. رمز موجود فقط در حافظهٔ child حفظ می‌شود؛ گروه، template، administrator و door قابل ویرایش در این مسیر نیستند. کاربر جدید عمداً رمز خالی دارد.
- Worker در مدت اجرای child هر۵۰۰ میلی‌ثانیه اختیار جاری/نسخه/endpoint/نگاشت/نام مصوب و fenceهای ایمنی را دوباره بررسی می‌کند؛ تغییر یا خطای بررسی باعث لغو و کشتن child می‌شود. هنگام Complete نیز اعتبار جاری کنترل می‌شود و نتیجهٔ دیگر معتبرنبودن، `unknown` است. **این polling تضمین اتمی یا rollbackِ SDK نیست**؛ نوشتن ممکن است پیش از دریافت لغو اتفاق افتاده باشد.
- پاسخ گم‌شده پس از نوشتن یا قطع child پس از dispatch، نتیجهٔ نامعلوم است؛ replay خودکار ممنوع. snapshot موفقِ جدیدتر با همان نسخه و قالب + `REVIEWED-USER` فقط شاهد بازبینی را ثبت می‌کند و unknown قبلی را موفق نمی‌کند. درخواست تازه پس از بازبینی مستقل است؛ اگر حالت مشاهده‌شده همان desired باشد، `unchanged` هیچ نوشتن دیگری انجام نمی‌دهد.
- fixture متغیر فقط در assembly تست و با دادهٔ کاملاً مصنوعی است؛ مسیر فایل GUID-only و ایجاد اولیه فقط در CI. child تولیدی همچنان allowlist محدود محیط را دارد؛ برای fixture، متغیر CI یا DB به محیط child اضافه نشده است. در اولین اجرای تست، وابستگی نادرست fixture به متغیر پاک‌شده آشکار و فقط خود fixture اصلاح شد.

## شاهد پذیرفته‌شدهٔ جاری

کد `f7b661861d8896f36600e082bc28abd7909ab842` در [Windows run43749746581](https://github.com/alirezarahimy-code/IO/actions/runs/43749746581) موفق شد:

- **۴۲۷ بررسی یکپارچه =۳۸۳ قبلی +۴۴ جدید آپلود مستقیم/unknown**؛ مسیر واقعی registry/SQL/صف/credential/worker/child و mapper تولیدی با SDK double متغیر، برای USB و serial جدا. گم‌شدن پاسخ بعد از mutation، منع retry کور، رد snapshot قدیمی، review تازه و حفظ unknown، retry تازهٔ بدون نوشتن مجدد و لغو نگاشت حین اجرای child پوشش دارند.
- **۴۰ بررسی mapper تولیدی نوشتن** برای ترکیب USB/serial × SSR/legacy: انتخاب دقیق API، حفظ رمز/گروه/fingerprint و administrator دست‌نخورده، unchanged، conflict، null password، اسکن ناقص، duplicate، serial اشتباه و ایجاد کاربر عادی جدید.
- **۳۰ بررسی اتصال و mutex واقعی Windows**، شامل منع مالک دوم process مستقل، و **۲۶ بررسی مرز transport**؛ regressionهای قبلی نگه داشته شدند.

پوشش عملیاتی اختصاصی آپلود صف→worker→SQL در این برش **SSR روی هر دو transport** است؛ legacy مستقیم در سطح mapper واقعی با double برای هر دو transport پوشش دارد، نه اجرای عملیاتی جداگانهٔ legacy یا SDK واقعی. شاهد قبلی اتصال/دریافت پایه روی [ee4b071](https://github.com/alirezarahimy-code/IO/actions/runs/43747192483) نیز در [شاهد ماشینی SHA/job و دامنهٔ دقیق](r16-direct-sdk-software-evidence.json) نگه داشته شده است.

UI در این برش تغییر نکرده و اجرای تازهٔ DOM/Chromium به SHA آپلود نسبت داده نمی‌شود. [DOM رابط](https://github.com/alirezarahimy-code/IO/actions/runs/43746623088) روی fa770c0 موفق بود؛ source رابط/تست DOM تا SHA جاری عیناً تغییر نکرده است. Chromium مستقل در آن run به‌علت فیلتر دامنه skip شد؛ آخرین اجرای پیشین روی e92a82a در [run43746283437](https://github.com/alirezarahimy-code/IO/actions/runs/43746283437) موفق بود، نه اثبات تغییر HTML بعدی.

**هیچ SDK نصب‌شده، driver یا دستگاه واقعی اجرا نشد؛ Linux تست نشد.** مجموعهٔ تاریخی کامل SQL۱۰۳۱ و rollback کامل نیز در این SHA اجرا نشده‌اند. R16/تکمیل نرم‌افزارِ دامنهٔ گسترش‌یافته برای خانواده‌های تخصصی جدول هنوز باز است: `softwareComplete=false`، `hardwareVerified=false` و `part3Complete=false`؛ H01 مستقل معوق می‌ماند. مورد بعدی به‌ترتیب، قرارداد تنظیمات تخصصی مستقیم و بازیابی آن‌هاست؛ تنظیمات شبکه نباید صرفاً با حذف fence روی transport محلی مجاز شوند.
