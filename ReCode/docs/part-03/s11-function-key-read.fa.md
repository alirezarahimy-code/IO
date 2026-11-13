# S11 — خواندن مصوب کلید عملکردی؛ نه رمز و نه نوشتن

## شاهد مرجع و اصلاح رفتار

- `T5Web [3.5.7]/App/bin/T5V3Web.dll`، بدنهٔ decompile‌شدهٔ `DeviceOperationsController.ReadKey` در خط۱۱۷۳: درخواست `readkey?machineNumber=...&key=...` و دریافت مدل `FunctionKey`؛ باینری سورس اصلی نیست.
- `T5V3.9.2/Personnel.DLL`، `Personnel.ZK.ZKDevice.GetFunctionKey` در خط۱۰۱۵ و فراخوانی خط۱۰۲۲ در `PersonnelZKDevice.decompiled.cs` با SHA256 برابر `58e33195ab271660e2f48b81d80167b91c693cb7986b955ee14c94d1b58b7eb5`.
- امضای `zkemkeeper.IZKEM.SSR_GetShortkey` در خط۲۷۹ِ `IZKEM.decompiled.cs` با SHA256 برابر `49856b1ed0aa89838405cfcef3a46da2f6275a80425898cda3cc70964a5ab701`: index و پنج خروجی `ShortKeyFun/StateCode/StateName/AutoChange/AutoChangeTime`؛ **پارامتر machine ندارد**.
- مرجع نتیجهٔ bool خواندن را کنترل نمی‌کرد و دو خروجی AutoChange را در مدل برنمی‌گرداند. IO در صورت false/exception، مقدار اولیهٔ صفر را دادهٔ خوانده‌شده معرفی نمی‌کند و در موفقیت هر پنج خروجی را نگه می‌دارد.
- `SetFunctionKey` مرجع در خط۱۰۳۱ از `SSR_SetShortkey` استفاده می‌کند؛ این setter منتقل یا قابل dispatch نشده است. کلید عملکردی، رمز ارتباط/کاربر، دسترسی درب یا رضایت بیومتریک نیست.

هیچ فایل مرجع تغییر نکرده و COM/مدل سازمان در این برش اجرا نشده است.

## نصب پیش‌فرض خاموش و محصول دقیق

همان `EnableOperations/EnableSettingsReads` و `AllowAdvancedSettingsRead` و pin سریال/IP/port/machine قبلی لازم‌اند. فیلد اختیاری جدید هر NativeEndpoint:

```json
"FunctionKeyReadApproval": {
  "Product": "EXACT-APPROVED-PRODUCT",
  "Firmware": "EXACT-APPROVED-FIRMWARE",
  "Indices": [0, 3],
  "EvidenceReference": "YOUR-MODEL-READ-CONTRACT-REFERENCE"
}
```

این نمونه تأیید مدل یا شماره‌کلید واقعی نیست؛ مقادیر باید از قرارداد همان مدل تعیین شوند. پیش‌فرض `null` یعنی **هیچ فراخوانی کلیدی** انجام نمی‌شود. approval نامعتبر، بدون AllowAdvancedSettingsRead، فهرست خالی/تکراری/منفی یا بیش از۸ شماره، در startup رد می‌شود. عدد۸ بودجهٔ درخواست برنامه است، نه تعداد یا محدودهٔ سخت‌افزار؛ IO کلیدهای ۰..۷ را پیمایش یا از روی مدل حدس نمی‌زند.

فهرست خصوصی IPC فقط از نصب worker می‌آید؛ اپراتور وب index یا متد COM دلخواه ارسال نمی‌کند. پیش از هر خواندن کلید، محصول/firmware زنده باید دقیقاً با approval برابر باشند؛ عدم تطبیق به unsupported بدون تماس با API کلید می‌رسد. همان instance تازه، متصل و serial-pinned استفاده می‌شود؛ سریال پس از کل مجموعه دوباره کنترل می‌شود. تغییر هویت، snapshot موفق تولید نمی‌کند. خواندن‌های مستقل، snapshot اتمیک دستگاه نیستند.

## مجوز و مسیر اپراتور

`/workspace/device-settings` همان صف واقعی `settings.read` را استفاده می‌کند:

1. نقش‌های شناخته‌شدهٔ operator/admin، طبق کاتالوگ موجود حق `devices.settings.read` دارند؛ اعطا/لغو سروری و scope دستگاه همچنان اعمال می‌شوند.
2. سرویس باید `devices.operations.execute` و حق مستقل `devices.settings.read.execute` را داشته باشد؛ نقش انسانی مجوز سرویس ایجاد نمی‌کند.
3. تأیید خواندن صفحه اکنون شش کد ثابت، مشاهدات اضافی ثابت و کلیدهای **صرفاً مصوب نصب** را توضیح می‌دهد. این تأیید هیچ نوشتن کلید یا تنظیم دلخواهی را مجاز نمی‌کند.
4. worker پس از بررسی اختیار جاری، نسخه و مقصد، credential را فقط از مسیر claim قبلی می‌گیرد. مدل/فهرست نصب در IPC خصوصی ارسال و نتیجه در مرز child با همان مدل/مرجع/ترتیب index اعتبارسنجی می‌شود.
5. تاریخچه، فیلدهای خام و نتیجهٔ مستقل هر شماره را با `textContent` نمایش می‌دهد. revoke داده‌های صفحه را پاک و دسترسی بعدی سرور را قطع می‌کند.

مجوز و جدول یا migration جدیدی اضافه نشده است؛ همان تأیید نصب خواندن پیشرفته، scope و حق مستقل قبلی استفاده می‌شوند. نوشتن کد۴، IPv4 و restart به مجوزهای مستقل خود نیاز دارند.

## دادهٔ اصلی و سازگاری

فیلد اختیاری `ZkAdvancedSettings.FunctionKeys` با قرارداد `zk-ssr-shortkey-read-v1` شامل مرجع approval و فهرست indexهای مصوب است. snapshotهای قبلی با null همچنان خوانده می‌شوند؛ null یعنی درخواست‌نشدن، نه اثبات نبود پشتیبانی مدل.

- `Reported=true`: هر سه عدد `Function/StateCode/AutoChange` با دقت Int32، از جمله صفر/منفی/مقدار غیر boolean، و دو رشتهٔ خام حفظ می‌شوند. `AutoChangeTime` به ساعت/روز/timezone تفسیر نمی‌شود.
- false/exception، index اشتباه یا خروجی malformed: همان index با `Reported=false` و خروجی‌های null؛ خطا از عدم پشتیبانی تفکیک نشده و صفر ساخته نمی‌شود.
- نام/زمان خام تا۱۲۸ کاراکتر بدون کنترل و با UTF-8 معتبر پذیرفته می‌شوند. رشتهٔ بلند/خراب کوتاه یا اصلاح حدسی نمی‌شود؛ آن مشاهده unavailable است. رشتهٔ خالی واقعاً گزارش‌شده مجاز است.
- child قدیمی بدون دادهٔ کلیدیِ درخواست‌شده، مرجع/مدل/ترتیب index متفاوت یا دادهٔ unsolicited رد می‌شود. خطای timeout هرگز snapshot موفق نیست و نوشتنی retry نمی‌شود.
- سقف stdout فقط برای settings.read دارای approval معتبر از۴۰۹۶ به۳۲۷۶۸ کاراکتر افزایش یافته تا دادهٔ bounded هشت کلید و escape یونیکد جا شود؛ stderr و سایر عملیات همان۴۰۹۶ می‌مانند. خروجی بیش از سقف همچنان fail-closed است.
- JSON همان ResultJson غیرقابل‌تغییر عملیات SQL را استفاده می‌کند؛ raw attendance و canonical/dedup تردد تغییر نکرده‌اند.

## آزمون و حدود ادعا

- `FunctionKeyReadTests.cs`: ۲۱ بررسی مستقیم قرارداد، مدل/فهرست/خروجی اشتباه، مقدار خام/خالی/خراب، فرایند child واقعی، snapshot بزرگ یونیکد، missing/overflow و deadline؛ محلی موفق، بدون SDK.
- `device-settings-ui-check.cjs`: نمایش امن نام/مرجع حاوی HTML، صفر/منفی، AutoChange غیر boolean، unavailable و حفظ retry/CSRF/revoke؛ محلی موفق.
- دو assertion جدید `DeviceAdvancedSettingsTests` برای منع unavailable جعلی و حفظ فهرست خام در تاریخچهٔ SQL؛ و یک assertion جدید آزمایشگاه worker با فهرست[۰،۳]، یک کلید موجود و دیگری unavailable. مرورگر واقعی نیز نمایش همان snapshot تولیدشده توسط worker را بررسی می‌کند.
- برای تغییرات مشترک supervisor، تست‌های موجود مرز native(۶۱)، تطبیق مرجع(۳۳) و آزمایشگاه TCP(۱۹) محلی دوباره موفق شدند؛ build bridge/runtime موفق است.

نتیجهٔ SQL/HTTPS/worker و CI ویندوز/لینوکس این برش باید روی SHA جدید جدا ثبت شود؛ تست محلی جای آن نیست. تعداد مورد انتظار مجموعهٔ کامل پس از سه assertion جدید۸۵۴ است، نه شاهد اجرای آن. اجرای SDK/دستگاه واقعی در پذیرش نهایی باقی می‌ماند. تغییر کلید، سایر تنظیمات، PUSH، RemoteEnroll و عملیات مخرب با این برش تکمیل نمی‌شوند.
