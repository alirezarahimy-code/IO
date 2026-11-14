# S11 — خواندن خام DLST با شاهد TypeLib

**پیاده‌سازی نرم‌افزاری خواندن؛ نه تکمیل نوشتن DLST یا بخش۳.** آزمایش محلی ۲۰ قرارداد و فرایند child موفق است؛ پنج بررسی جدید SQL/worker برای CI همین commit تعریف شده‌اند و تا سبزشدن ادعای اجرای موفق ندارند. شاهد SDK/دستگاه واقعی همچنان برای انتها باقی است.

## منشأ قابل بازتولید، نه سورس اصلی

- باینری فقط‌خواندنی `SDK 6.3.1.45/zkemkeeper.dll` با SHA256 `33b5d535cffdd14dd123d22d8a069004add468c71a7c8411a906f641b7ef2399`، منبع embedded `TYPELIB/1/1033`، قالب MSFT، ۵۸۳۱۶ بایت.
- SHA256 TypeLib: `f7b8fba2f3d5352b9482a0f6394b916f998aac469ec08c0bd175f7b5c2fbd395`.
- `IZKEM.SetDaylight` با DispId178 در offset41188؛ `IZKEM.GetDaylight` با DispId179 در offset41280. getter: machine یک LONG با flag1؛ Support یک PTR LONG و BeginTime/EndTime دو PTR BSTR با flag3 یعنی in/out؛ pVal یک PTR VARIANT_BOOL با flag10 یعنی out/retval. امضای Automation: `bool GetDaylight(int machine, ref int support, ref string begin, ref string end)`.
- `python3 ReCode/tools/inspect-zkem-daylight-typelib.py --verify` بدون dependency خارجی یا اجرای DLL، دادهٔ PE/MSFT و hashها را دوباره بررسی می‌کند. خروجی کامل در `s11-daylight-typelib-evidence.json` است. این ابزار خواندنی است، نه decompile سورس یا ادعای سازگاری همهٔ مدل‌ها.
- رابط managed قدیمی با SHA256 `49856b1ed0aa89838405cfcef3a46da2f6275a80425898cda3cc70964a5ab701` فقط setter داشت؛ نبود getter در آن، دیگر مانع عمومی خواندن محسوب نمی‌شود.

مرجع `Personnel.ZK.ZKDevice.SetDLST` در بدنهٔ decompile خطوط۱۰۹۴–۱۱۰۲ رشتهٔ `MMddHHmm` می‌سازد؛ این رفتار، قرارداد همگانی دستگاه نیست. نسخه‌های بازنشرشدهٔ دفترچه و wrapper ثالث نیز قالب‌های یکسانی نشان نمی‌دهند: [2](https://dokumen.tips/documents/zkemsdk-manual-v612.html)، [4](https://github.com/ks55259980/ZkProject/blob/master/src/main/java/com/wemarklinks/method/ZkemSDK.java)، [5](https://idoc.tips/zkteco-standalone-sdk-development-manual-v21-a2-en-5-pdf-free.html). این‌ها سند تأییدشدهٔ مدل نصب‌شده نیستند. بنابراین هیچ قالب یا تبدیل تاریخ از اینترنت وارد مسیر production نشده است. خروجی getter فقط raw است.

## مسیر اجرایی و حقوق واقعی

`/workspace/device-settings` → درخواست تأییدشدهٔ `settings.read` → SQL و claim → `DeviceWorker` → child محدودشده → `ZkBridge.DaylightSession` → یک `GetDaylight` → pin نهایی سریال → completion/history در SQL → نمایش امن DOM.

- انسان: `devices.settings.read` و scope همان دستگاه. restricted بدون grant دستگاه یعنی صفر دستگاه؛ مجوز عمومی information یا نام نقش به‌تنهایی کافی نیست.
- سرویس: `devices.operations.execute` و `devices.settings.read.execute`؛ `EnableOperations` و `EnableSettingsReads` و `AllowAdvancedSettingsRead` نصب هم لازم‌اند. revokeها، نسخهٔ دستگاه، مالکیت claim و credential از همان کنترل سرور استفاده می‌کنند.
- خواندن DLST، مشابه شش عدد خام و گزارش شبکه، زیر مجوز خواندن تنظیمات است؛ مجوز مستقل **نوشتن** ساعت/کلید/تنظیمات یا اطلاعات حساس کاربر اعطا نمی‌کند. برای این خواندن غیرحساس permission جدید ساخته نشده است؛ مجموع permissionها۱۲۹ می‌ماند.
- نصب پیش‌فرض: `DaylightReadApproval=null`، پس getter اصلاً فراخوانی نمی‌شود. approval وبی نیست؛ مدیر نصب باید محصول، firmware و مرجع شاهد را دقیق در NativeEndpoint وارد کند. mismatch یا نبود descriptor هیچ getterی اجرا نمی‌کند و نتیجه unsupported این درخواست است، نه ادعای unsupported بودن کل مدل.

نمونهٔ **قالب** تنظیم نصب، نه تأیید هیچ مدل واقعی:

```json
"DaylightReadApproval": {
  "Product": "EXACT-OBSERVED-PRODUCT",
  "Firmware": "EXACT-OBSERVED-FIRMWARE",
  "EvidenceReference": "installation-model-read-evidence"
}
```

worker و bridge باید هماهنگ منتشر شوند. bridge قدیمی که مشاهدهٔ درخواست‌شده را برنگرداند موفق شمرده نمی‌شود. approval این خواندن هرگز در درخواست نوشتن/clock/restart حمل نمی‌شود.

## اصل داده و حالات

- `ZkDaylightSnapshot`: قرارداد `zk-daylight-read-v1`، مرجع approval، `Reported` اجباری و `Value` کامل یا null.
- `Support` یک Int32 خام است؛ صفر، منفی یا مقدار ناشناخته حذف یا true/false نمی‌شود. `BeginTime/EndTime` هرکدام حداکثر۱۲۸ کاراکتر، Unicode معتبر و بدون control؛ رشتهٔ خالی و whitespace عیناً حفظ می‌شوند.
- SDK false، exception یا خروجی خراب → `Reported=false,Value=null`؛ مقادیر initializeشده موفقیت نیستند. سایر خواندن‌های مستقل تنظیمات ادامه دارند.
- بدون approval → `Daylight=null`، یعنی «درخواست نشده»؛ این حالت با «گزارش نشده» و Support=0 فرق دارد.
- سریال قبل و بعد snapshot pin می‌شود. snapshot مجموعهٔ مشاهده‌های مستقل است، نه snapshot اتمیک یا طرح آمادهٔ نوشتن.
- خروجی child باید با مدل/firmware/مرجع درخواست منطبق باشد؛ دادهٔ ناخواسته رد می‌شود. مهلت فرایند و سقف pipe موجود حفظ شده؛ hang نتیجهٔ موفق تولید نمی‌کند.
- JSON در همان ResultJson تاریخچهٔ غیرقابل حذف/بازنویسی ذخیره می‌شود. ستون یا kind جدیدی لازم نیست، پس migration جدید ایجاد نشده است؛ migration46 و سقف۶۵۵۳۶بایت settings موجود کافی‌اند. parse typed وجود/نوع/محدودیت داده را کنترل می‌کند.
- DOM از textContent استفاده می‌کند؛ متن خام، HTML نمی‌شود. revoke خواندن، history و cache صفحه را پاک می‌کند.

## آزمون و ماندهٔ دقیق

۲۰ بررسی portable: تأیید نصب، mismatch بدون native call، یک getter، signed/zero/Unicode/whitespace، false/exception/malformed، JSON ناقص، منع دادهٔ ناخواسته، bind مرجع/firmware، child واقعی، bridge ناقص و deadline. این‌ها SDK واقعی را اجرا نکرده‌اند.

دو بررسی SQL تازه: malformed/متناقض در completion رد می‌شود و raw صفر/whitespace همراه مرجع در تاریخچهٔ typed واقعی حفظ می‌شود. snapshot fixture اصلی نیز DLST دارد تا کنترل revoke/credential/history موجود روی همین داده اجرا شود. سه سناریوی worker تازه: مسیر واقعی HTTPS→SQL→worker→child→TCP، history HTTP و unavailable مستقل بدون نوشتن. شمار موردانتظار کل SQL از۹۱۰ به۹۱۵ می‌رسد؛ تا نتیجهٔ CI «انتظار» است. آزمون DOM موجود نیز raw امن، صفر، unavailable، not-requested و پاک‌شدن پس از revoke را می‌سنجد.

**نوشتن هنوز باز است:** encoding واقعی مدل (مرجع `MMddHHmm` در برابر خروجی واقعی getter)، دامنه/معنای Support، سالانه/یک‌باره، timezone، اثر تغییر و زمان‌های مبهم/ناموجود باید در قرارداد مدل مشخص شوند. سپس preview کامل old/new، مجوز مستقل، یک dispatch، read-back دقیق و recovery بدون replay لازم است. پیدا شدن getter مجوز حدس این قرارداد یا اجرای SetDaylight نیست. `SetDLST` همچنان در dispatch عمومی بسته است.
