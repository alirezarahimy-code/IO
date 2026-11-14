# S11 — نوشتن کامل کلید عملکردی با قرارداد نصب

وضعیت این برش: **پذیرش نرم‌افزاری روی97cac12: SQL کامل و Windows/Linux/light موفق. بخش۳ کامل نیست.**

## منشأ و تفاوت آگاهانه

`Personnel.ZK.ZKDevice.SetFunctionKey` در خط۱۰۳۱ بدنهٔ فقط‌خواندنی decompile با SHA256 `58e33195ab271660e2f48b81d80167b91c693cb7986b955ee14c94d1b58b7eb5`، از باینری `Personnel.DLL` با SHA256 `185b084072a552e92ab0c8b0c2a03ca926b2aa362b5aada1ca8a39f998144be4`، فراخوانی زیر را نشان می‌دهد:

```text
SSR_SetShortkey(index, state, code, name, 0, "00:00;00:00;00:00;00:00;00:00;00:00;00:00;")
```

این باینری سورس اصلی نیست. امضای `IZKEM.SSR_SetShortkey` در خط۲۷۶/DispId223 شش ورودی دارد و machine number ندارد؛ hash رابط `49856b1ed0aa89838405cfcef3a46da2f6275a80425898cda3cc70964a5ab701`. IO دو فیلد آخر را صفر یا رشتهٔ ثابت نمی‌کند. `Function,StateCode,StateName,AutoChange,AutoChangeTime` هر دو tuple قدیم/جدید کامل هستند و دقیقاً bind و read-back می‌شوند. `Index` هم دقیق است. هیچ معنای زمانی یا کد عملکرد از نام API حدس زده نمی‌شود.

## مسیر و حقوق

`ZkFunctionKeyPlan/WriteApproval/Observation` → `DeviceOperations` و `DeviceFunctionKeyOperations` → SQL → `DeviceWorker` → child/`ZkBridge.FunctionKeyWriteSession` → `SSR_GetShortkey`/یک `SSR_SetShortkey`/read-back → تاریخچه و review.

- انسان: `devices.settings.keys.set` همراه `devices.settings.read` و `devices.settings.keys.read`، intersect با scope دستگاه. نام admin/operator کافی نیست؛ حقوق نوشتن پیش‌فرض به هیچ قالب نقشی داده نمی‌شوند.
- سرویس: `devices.operations.execute`، `devices.settings.keys.write.execute` و `devices.settings.keys.execute` برای خواندن preflight/read-back. مجوز عمومی یا فقط key-read اجازهٔ نوشتن نمی‌دهد. revoke قبل dispatch credential را قطع می‌کند.
- history کلید/plan برای settings-only مخفی است؛ شاهد اصلی SQL حذف نمی‌شود. تغییر grant روی صفحه دادهٔ cache و preview را پاک می‌کند.
- صفحهٔ واقعی `/workspace/device-settings` snapshot موفق کمتر از پنج دقیقه از همان device/version/zone/serial/product/firmware می‌گیرد. فقط index گزارش‌شدهٔ کامل می‌تواند منبع باشد؛ unavailable هرگز مقدار صفر فرض نمی‌شود.
- دکمهٔ انتخاب snapshot هر پنج فیلد قدیم را بدون trim در فرم کپی می‌کند؛ کاربر تغییر موردنظر، اثر و مرجع قرارداد را وارد می‌کند. hash تمام old/new/descriptorها را bind می‌کند؛ هر تغییر preview/consent را باطل می‌کند.
- اثر، encoding و schedule باید قرارداد همان مدل باشند. پیش‌نمایش SQL تأیید نصب worker نیست. هیچ generic command، رمز ارتباط، reset، enroll یا فرمان درب اینجا فعال نمی‌شود.

## نصب پیش‌فرض خاموش و مرز دامنه

NativeEndpoint: `AllowFunctionKeyWrite=false` و `FunctionKeyWriteApprovals=null` پیش‌فرض. برای اجرا AllowAdvancedSettingsRead و EnableOperations/EnableSettingsReads هم لازم‌اند. هر approval شامل:

```text
Product, Firmware, ApprovedStates, Effect, EvidenceReference, AttendanceOnly
```

`ApprovedStates` شامل۲ تا۸ tuple کامل، یکتا و مربوط به یک index است؛ هم حالت قدیم هم جدید باید عیناً در فهرست باشند. `AttendanceOnly=true` فقط پس از شاهد معتبر تیم نصب برای اینکه این حالت‌های دقیق کلید در این مدل/firmware صرفاً عملکرد تردد هستند و درب/گیت یا ثبت بیومتریک را اجرا نمی‌کنند قابل تأیید است. مقدار پیش‌فرض false است؛ هیچ کد عددی عمومی به‌عنوان «قطعاً تردد» حدس زده نمی‌شود. این flag به‌تنهایی گواه دستگاه واقعی نیست و از وب قابل فعال‌کردن نیست. بدون قرارداد و شاهد نصب، هیچ نمونهٔ فعال تولید پیشنهاد نمی‌شود.

## یک dispatch و نتیجهٔ نامعلوم

preflight دقیق serial/product/firmware و **تمام tuple قدیم**؛ سپس فقط یک dispatch. bool false، exception، drift هویت یا کوچک‌ترین تفاوت read-back پس از dispatch → `unknown`، بدون retry. موفقیت فقط exact tuple جدید و هویت دوباره pin‌شده است. polling و unknown قبلی مانع write هستند؛ recovery read باز می‌ماند. فقط cancel پیش از اجرا تضمین می‌شود؛ deadline child را می‌کشد، نه اینکه عدم اعمال نوشتن را ثابت کند.

review با `REVIEWED-APPROVED-FUNCTION-KEY` و snapshot موفق **بعد** از unknown، همان هویت/نسخه/zone و tuple کامل قدیم یا جدید است. نتیجه unknown حفظ می‌شود؛ review مجوز replay نوشتن نیست.

## SQL و تست

migration46 `DeviceFunctionKeyWrites`: plan/result این نوع حداکثر۱۶۳۸۴ بایت، سایر bounds قبلی محفوظ. CHECK/triggerهای مستقل source snapshot، exact tuple شامل schedule، immutable identity و retention را حفاظت می‌کنند. DOWN با تاریخچهٔ key خطای51035 می‌دهد؛ داده برای عبور از guard حذف نمی‌شود. تست guardهای قدیمی polling/payload/raw قبل از ایجاد تاریخچهٔ key اجرا و آخرین migration پیش از worker برگردانده می‌شود.

- `FunctionKeyWriteTests`:۲۳ بررسی محلی موفق، شامل session مصنوعی، child واقعی و deadline؛ اجرای SDK نیست.
- `device-key-write-ui-check.cjs`: preview، حفظ raw whitespace/schedule، explicit consent، replay همان intent نامعلوم، CSRF، safe text، review و revoke موفق.
- `DeviceFunctionKeyWriteTests`:۲۰ بررسی SQL/حقوق/credential/revoke/tuple/readback/history/downgrade افزوده شده.
- `VirtualWorkerTests`:۲ سناریوی HTTPS/SQL/production-worker/child/TCP برای lost ACK بعد از یک mutation با schedule محفوظ و سپس recovery/review بدون replay افزوده شده. TCP خصوصی آزمایشگاهی است، نه پروتکل ZK یا COM.
- suite کامل روی97cac12 با guard شمار دقیق۹۱۰ PASS موفق شد؛۴۶ migration و۱۲۹ permission. [SQL](https://github.com/alirezarahimy-code/IO/actions/runs/43583286620)، [Windows/Linux](https://github.com/alirezarahimy-code/IO/actions/runs/43583286608)، [light](https://github.com/alirezarahimy-code/IO/actions/runs/43583286621).

SDK/مدل و چنددستگاه واقعی طبق دستور کاربر برای انتها باقی‌اند. الگوریتم، DLST و شبکهٔ باقی‌مانده، PUSH امن، حذف و سیاست بیومتریک با این برش بسته نمی‌شوند.

سقف هشت state بودجهٔ هر approval/IPC است، نه تعداد کلید یا ظرفیت مدل/دستگاه. ترکیب حداکثر متن‌های Unicode و هشت state در child واقعی آزموده شد؛ input محدود۳۲۷۶۸ و output۸۱۹۲ کاراکتر، بدون trim/truncation. رد مجوز خواندن انسان و سرویس، redaction هر دو plan/observation و رد review با schedule متفاوت نیز regressionهای مستقل دارند.
