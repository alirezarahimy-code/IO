# S11 — پنج پارامتر عددی با قرارداد دقیق نصب

وضعیت: **پیاده‌سازی و آزمون محلی قرارداد/DOM؛ Windows/Linux کد d3290fc موفق؛ SQL این SHA و آزمون followup مجوز/مسیر واقعی جدید هنوز تأیید نشده‌اند. تکمیل بخش سوم نیست.**

## منشأ، نه حدس واحد یا دامنه

بدنهٔ decompile فقط‌خواندنی `Personnel.ZK.ZKDevice` در `Personnel.DLL`، SHA256 باینری `185b084072a552e92ab0c8b0c2a03ca926b2aa362b5aada1ca8a39f998144be4` و بدنه `58e33195ab271660e2f48b81d80167b91c693cb7986b955ee14c94d1b58b7eb5`:

| روش مرجع و خط | قرارداد بسته IO | تست |
|---|---|---|
| `SetOneToOne` 991 → `SetDeviceInfo(machine,25,value)` | `one-to-one` | `RawSettingTests` نگاشت بسته/approval/readback |
| `SetOneToN` 1003 → code23 | `one-to-n` | همان تست، نه infer threshold |
| `SetRepeatTime` 1139 → code8 | `repeat-time` | مقدار signed، واحد فقط قرارداد نصب |
| `SetCardReg` 1159 → code30 | `card-registration` | بدون تبدیل bool و بدون generic option |
| `SetMifareSector` 962 → code62 | `mifare-sector` | بدون تغییر کلید/دادهٔ کارت یا فرض محدوده |

باینری سورس نیست. این پنج setter اثبات سازگاری هیچ دستگاهی نیستند. سند مدل/firmware برای مقادیر، واحد و اثر هنوز باید در نصب واقعی تعیین شود؛ هیچ مقدار نمونه، قرارداد تولید نیست.

## مسیر کامل نرم‌افزاری

`ZkRawSettingContract` → `DeviceOperations.PreviewRawAsync/RequestAsync` → plan غیرقابل‌تغییر SQL → claim/credential سرویس → `DeviceWorker` با approval نصب → child process → `ZkRawSettingExecutor`/`ZkBridge.RawSettingSession` → readback → تاریخچه واقعی `/workspace/device-settings`.

- `devices.settings.raw.set` و `devices.settings.read` و scope دستگاه برای انسان؛ `devices.operations.execute` و `devices.settings.raw.execute` برای سرویس. هیچ قالب نقشی این دو حق نوشتن را پیش‌فرض ندارد. revoke قبل dispatch دسترسی credential را قطع می‌کند.
- snapshot موفق همان device/version/zone کمتر از پنج دقیقه، serial/product/firmware و مقدار قدیم واقعی لازم است. preview شامل hash تمام descriptorهاست؛ تغییر داده preview را باطل می‌کند. preview تأیید نصب worker نیست.
- نصب endpoint: `AllowRawSettingsWrite=false` پیش‌فرض؛ برای فعال‌سازی `AllowAdvancedSettingsRead`، `EnableOperations` و `EnableSettingsReads` و `RawSettingsApprovals` لازم‌اند. هر approval شامل `Product,Firmware,Parameter,ApprovedValues,Unit,Effect,EvidenceReference` است. مقادیر مجاز باید صریح، یکتا و ۲ تا ۶۴ مقدار باشند؛ هم قدیم هم جدید در فهرست باشند. مثال فعال تولید ارائه نمی‌شود چون مدل/firmware معلوم نیست.
- هیچ generic code، fallback، RefreshData، restart یا تنظیم الگوریتم/بیومتریک/کلید عملکردی فعال نمی‌شود. واحد و اثر دقیقاً با approval نصب تطبیق دارند، نه با برچسب دلخواه UI.
- preflight زنده serial/model/fw/old؛ یک dispatch؛ موفقیت فقط با مقدار جدید و repin تمام هویت. false/exception/mismatch پس از dispatch → unknown، بدون retry. deadline خارجی child را terminate می‌کند؛ تضمین لغو فقط pending است.
- polling و unknownهای قبلی write/upload مانع نوشتن‌اند؛ recovery read مجاز می‌ماند. بازبینی unknown فقط با snapshot بعدی same-model و مقدار قدیم یا جدید و تأیید `REVIEWED-APPROVED-RAW-SETTING` است. unknown به success تبدیل نمی‌شود.
- migration45 `DeviceApprovedRawSettings`، plan bounded16384bytes و result bounded16384bytes این نوع؛ سایر انواع budget قبلی. triggerها source/readback/immutability و retention را مستقل از API حفاظت می‌کنند؛ downgrade با تاریخچهٔ raw خطای51034 می‌دهد.

## آزمون و مانده

`RawSettingTests`: ۲۲ بررسی موفق محلی، از جمله child واقعی و deadline؛ session مصنوعی نه SDK. `device-raw-ui-check.cjs`: پیش‌نمایش/تأیید، replay دقیق پاسخ نامعلوم، safe text و پاکسازی revoke موفق. `DeviceRawSettingTests`: ۱۵ بررسی SQL/مجوز/revoke/credential/review/retention افزوده شده، اجرای CI لازم است. Windows/Linux و SQL کامل همان SHA شرط پذیرش نرم‌افزاری است. دستگاه واقعی و چنددستگاه/قطعی همچنان برای انتها باقی‌اند.

### تکمیل شاهد مسیر واقعی در برش بعدی

دو regression به VirtualWorkerTests افزوده شد: HTTPS مصدق → preview/hash → SQL → production worker/credential → child واقعی → endpoint مصنوعی TCP، lost ACK بعد از یک mutation و سپس snapshot/read-only و review بدون replay. این سناریو دو endpoint مستقل را کنترل می‌کند؛ پروتکل خصوصی تست است، نه ZK/SDK/سخت‌افزار. اجرای آن هنوز وابسته به CI کد followup است.
