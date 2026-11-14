# R08 و R09 — انتخاب مسیر پیشنهادی و مرز دقیق برش جدید

## پاسخ کاربر و تصمیم ایمن

کاربر برای PUSH انتخاب را به پیشنهاد عامل سپرد و دربارهٔ سیاست RemoteEnroll گفت نمی‌داند و پیشنهاد می‌خواهد. پیشنهاد انتخاب‌شده برای PUSH، **درگاه واسط احرازشده در شبکهٔ محافظت‌شدهٔ محل** است؛ این مرز با احراز هویت بومی ZKTeco متفاوت است. برای RemoteEnroll، فقط ساخت مسیر مصنوعی/غیرفعال انتخاب شد؛ از ناآگاهی کاربر، رضایت اشخاص، اختیار سازمانی، مدت نگهداری یا مجوز ثبت بیومتریک استنتاج نشد.

این تصمیم‌ها به معنی بسته‌شدن ردیف‌ها نیستند. کاربر درخواست تکمیل دو ردیف را داشت، اما نبودِ قرارداد vendor، اتصال backend/bridge و سیاست مجاز را نمی‌توان با برچسب completed پوشاند. این فایل دقیقاً می‌گوید چه کدی ساخته شده و چه چیزی هنوز مانده است.

## R08: پروتکل جدید gateway-to-server

فایل `AuthenticatedPushGateway.cs` یک مرز قابل‌ترکیب و مستقل از HTTP/SQL معرفی می‌کند، نه پیاده‌سازی `/iclock`:

- قرارداد جدید `io-push-gateway-hmac-sha256-v1`، روش ثابت POST و مسیر قرارداد `/gateway/v1/attendance`؛ این مسیر **در وب ثبت نشده** است.
- binding قابل‌اعتمادِ خارج از درخواست: gateway/device/source/serial/timezone/key-version با کلید مستقل۳۲بایتی؛ هیچ secret پیش‌فرض یا شماره‌سریال‌محور تولید نمی‌شود. کلید binding کپی می‌شود و در ToString ظاهر نمی‌شود. provisioning، نگهداری امن/چرخش/حذف کلید هنوز متعلق به adapter آینده است؛ کلید native دستگاه نباید مجدداً به‌عنوان کلید gateway استفاده شود.
- امضای HMAC-SHA256 با مقایسهٔ زمان‌ثابت. canonical شامل رشته‌های UTF8 با length-prefix سبک BinaryWriter، UUIDهای D، زمان Unix ثانیهٔ Int64 little-endian، نشانگر یک‌بایتی nullable Stamp، طول بدنهٔ Int32 little-endian و SHA256 بایت‌های دقیق است. method/path و تمام bindingها نیز امضا می‌شوند؛ null و Stamp خالی متفاوت‌اند.
- سقف هر پیام از codec قبلی حفظ می‌شود. زمان باید UTC با ثانیهٔ کامل باشد؛ age حداکثر۱۲۰ثانیه و آینده حداکثر۳۰ثانیه **بودجهٔ پروتکل جدید gateway** هستند، نه ادعای رفتار firmware سازنده.
- golden vector مستقل با Python محاسبه شده است: gateway=`11111111-1111-1111-1111-111111111111`، device=`22222222-2222-2222-2222-222222222222`، source=`33333333-3333-3333-3333-333333333333`، transfer=`44444444-4444-4444-4444-444444444444`، serial=`SYNTHETIC-SERIAL`، zone=`UTC`، key-version=`key-1`، کلید مصنوعی بایت‌های۰تا۳۱، زمان `2026-11-08T00:00:00Z`، Stamp=null و بدنهٔ `001\t2026-11-08 08:00:00\t0\t15\t000\t0\t0\n`؛ HMAC برابر `AFE5C76730601E7791C77E1CE41CD93FE49A327A1EE81376E3E2D866A17E526E` است. این کلید صرفاً fixture است و نباید در نصب استفاده شود.
- `IPushGatewayCommitStore` یک وابستگی **اجباری** است؛ پیاده‌سازی SQL تولیدی آن هنوز ساخته نشده. قرارداد آن می‌خواهد grant/binding/key revoke در همان تراکنش کنترل شود، شناسهٔ `(gateway,device,transfer)` یکتا باشد، retry دقیق به همان رسید برسد و تغییر هر فیلد پایدار conflict شود. timestamp امضای retry می‌تواند تازه شود؛ fingerprint پایدار همچنان source/serial/zone/key-version/Stamp/body و شناسه‌ها را قفل می‌کند.
- پاسخ فقط پس از بازگشت store و تطبیق ReceiptId، scope، transfer، hash، تعداد سطر و fingerprint برمی‌گردد. snapshot و اصل بایت‌ها از یک کپی منجمد ساخته می‌شوند. store خطادار یا رسید ناسازگار، موفقیت تولید نمی‌کند.
- رسید با constructor داخلی تولید می‌شود و صریحاً `GatewayAuthenticated=true` و `DeviceIdentityVerified=false` و state=`gateway-retained-device-unverified` دارد. این **OK:n دستگاه یا رسید پردازش تردد نیست**؛ سطر خرابِ UTF8 قابل‌خواندن هم فقط شواهد قرنطینه است.

### شرطی که نباید حذف شود

HMAC به‌تنهایی replay protection ماندگار نیست. dictionary داخل تست فقط double مصنوعی است، نه ذخیرهٔ تولیدی یا شاهد تراکنش SQL. تا ساخت و آزمون store واقعی، recheck لغو کلید/حق در تراکنش، provisioning امن، HTTPS، endpoint محدود و فرستندهٔ onsite، R08 کامل نیست و مسیر عمومی فعال نمی‌شود. حتی پس از آن هم مرز اعتماد gateway به دستگاه باید مصوب/محافظت‌شده باشد؛ native device authentication ادعا نمی‌شود.

## R09: چرخهٔ RemoteEnroll مستقل، فعلاً فقط مصنوعی

`RemoteEnrollWorkflow.cs` و doubleهای تست ساخته شدند؛ **هیچ مسیر COM، worker، صف SQL یا endpoint وب آن را اجرا نمی‌کند**.

- شناسهٔ شخص رشتهٔ عددی دقیق می‌ماند؛ `000123` به `123` یا long تبدیل نمی‌شود.
- approval پیش‌فرض `AllowEnrollment=false` است. pin serial/product/firmware، finger-index، action-code موفقیتِ مخصوص قرارداد مدل و مراجع model/authorization/retention-deletion لازم‌اند. وجود رشتهٔ مرجع به‌تنهایی صحت حقوقی یا سازمانی آن را ثابت نمی‌کند؛ trusted caller آینده باید اعتبار و دامنهٔ آن را کنترل کند.
- دامنهٔ بستهٔ این candidate فقط اسلات‌های انگشت۰تا۹ است، نه face، palm یا پشتیبانی همهٔ مدل‌ها. flag=1 صرفاً فراخوانی مشاهده‌شدهٔ مرجع است؛ معنای عام آن برای دستگاه نامعلوم فرض نمی‌شود.
- قبل از dispatch، identity زنده و presence اسلات بررسی می‌شود. null به معنی unavailable است، نه اسلات خالی؛ اسلات موجود باعث رد می‌شود و اجازهٔ overwrite الگو نداریم.
- یک dispatch؛ true از StartEnrollEx فقط `awaiting-subject` است، نه completed. false یا exception پس از dispatch، `unknown` می‌ماند؛ تکرار Start همان intent دیگر dispatch نمی‌کند.
- completion فقط برای همان operation/person/finger و action-code صریحِ مدل بررسی می‌شود. طول الگو باید مثبت و identity تازه pinned و اسلات مستقلاً present باشد؛ هیچ بایت template از session بیرون نمی‌آید. این مسیر در CI صرفاً double است، نه getter معتبر نصب‌شده.
- cancel پیش از dispatch واقعاً جلوگیری می‌کند. cancel پس از dispatch حتی با ACK=true، unknown می‌ماند؛ نتیجهٔ دیررس تاریخچهٔ نامعلوم را بازنویسی نمی‌کند. deadline هم unknown می‌دهد و retry کور ندارد. timeout سخت child/process و قفل انحصاری دستگاه هنوز باید در adapter واقعی ساخته شوند؛ lock درون این کلاس جای deadline سخت process نیست.

### شاهد باینری تازه

[شاهد دو TypeLib](s09-remote-enroll-typelib-evidence.json) از دو DLL hash-pinned و بدون load/COM استخراج شده است:

- `StartEnrollEx`: DispId203، UserID=BSTR، FingerID/Flag=LONG، خروجی VARIANT_BOOL.
- `CancelOperation`: DispId91 و VARIANT_BOOL.
- `OnEnrollFingerEx`: DispId18 با EnrollNumber=BSTR، FingerIndex/ActionResult/TemplateLength=LONG؛ **GUID عملیات و بایت template در این callback نیست**.

بنابراین OperationId در lifecycle یک correlation داخلی موردنیاز adapter آینده است، نه فیلد اختراعی native. اتصال callback به child/claim انحصاری، جلوگیری از callback قدیمی، صحت readback presence، معنی action-code و اثر cancel هنوز نیازمند قرارداد واقعی‌اند. استخراج signature به‌تنهایی این‌ها یا مجوز ثبت اشخاص را ثابت نمی‌کند.

## تست و پوشش

پرچم `--gateway-enroll-contracts` در اجرای Windows قراردادهای HMAC/tamper/time/retry/commit/cancel و چرخهٔ enrollment با doubles مصنوعی را اجرا می‌کند. auditor امضاهای دو DLL نیز به light ویندوز اضافه شده است. تست Linux و SQL تازه اجرا نمی‌شود. شمار۱۰۳۱ SQL مربوط به نسخهٔ تاریخی adcb551 است و با تست‌های جدید مخلوط نمی‌شود. build محلی فقط کامپایل بود، بدون اجرای تست Linux؛ صفر warning/error.

در زمان نگارش اولیه، CI جدید هنوز پذیرفته نشده است. نتیجهٔ دقیق Windows باید جدا ثبت شود. **R08 و R09 بازند؛ شمار۹ فنی + H01 ثابت است.** policy واقعی بیومتریک، production gateway store/HTTPS/provisioning، RemoteEnroll SQL/permissions/API/UI/worker/bridge و آزمون‌های یکپارچهٔ تازه هنوز باقی‌اند.
