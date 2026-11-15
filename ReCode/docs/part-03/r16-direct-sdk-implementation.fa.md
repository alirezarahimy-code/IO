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
| ارسال metadata کاربر عادی با preview و no-overwrite | برقرار | worker/mapper مستقیم و همان قرارداد حفاظت؛ بدون حق template/admin/door ضمنی |
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

تست‌های جدید connector تولیدی با SDK double و mutex واقعی Windows، منع مالک دوم در process مستقل، ورودی‌های نامعتبر، stream کاربران/تردد و عدم dispatch فرمان‌های هنوز پشتیبانی‌نشده را بررسی می‌کنند. تست یکپارچه نیز registry/constraint و worker واقعی/credential/child مصنوعی/SQL را برای USB و serial جدا اجرا می‌کند؛ این **اجرای driver یا دستگاه نیست**. نتیجهٔ نهایی CI باید به SHA دقیق همین برش مقید ثبت شود؛ شمار تاریخی۳۵۵ جای اجرای تازه را نمی‌گیرد. Linux طبق دستور کاربر تست نمی‌شود. H01 معوق و `hardwareVerified=false` و `part3Complete=false` باقی‌اند.
