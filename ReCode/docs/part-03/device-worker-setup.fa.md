# نصب worker ارتباط و probe بومی candidate ZK — S05، نه پذیرش سخت‌افزار

این worker جدا از وب روی .NET 10 اجرا می‌شود و COM را در process خود یا وب بار نمی‌کند. مسیر اولیه TCP handshake است؛ مسیر candidate ZK در child مستقلِ بخش پایانی همین سند اجرا می‌شود. `tcp-reachable` اثبات دستگاه، رمز، مدل، firmware یا پروتکل نیست. S05 و کل بخش سوم هنوز پذیرفته نشده‌اند.

## پیش‌نیاز و اعتماد

1. از DB پشتیبان بگیرید؛ migration `20261112020000_DeviceConnections` با ابزار مهاجرت موجود اعمال شود. میزبان وب/worker خودکار migrate یا seed نمی‌کند. downgrade پس از اولین درخواست تست عمداً خطای 51016 می‌دهد؛ زنجیرهٔ downgrade اتمیک فرض نشود.
2. حساب سرویس اختصاصی سیستم‌عامل و دسترسی SQL کمینهٔ متناسب با query/transactionهای سرویس تنظیم کنید؛ اتصال رمز‌شده با گواهی معتبر و ACL برای تنظیمات لازم است. هویت بنیادینِ فعالِ جدا، بدون نیاز به حساب ورود وب، و نقش اختصاصی دارای فقط `devices.connection.execute` بسازید. هیچ قالب نقش این مجوز را نمی‌دهد. این نسخه هویت بنیادین را در هر claim/ثبت نتیجه از DB بررسی می‌کند؛ نگاشت config به هویت سیستم‌عامل/SQL مسئولیت استقرار است، نه هویت دستگاه یا گواهی دستگاه.
3. تنظیمات صریح از secret store/متغیر محیطی امن یا فایل خارج از Git با ACL بیاید. `ConnectionStrings__IO` رشتهٔ واقعی SQL است؛ رمز آن در چت یا نمونه ثبت نشود. `IO__DeviceWorker__ServiceUserId` UUID هویت بنیادین سرویس است.
4. برای هر endpoint تأییدشده، این کلیدها را تنظیم کنید: `IO__DeviceWorker__AllowedEndpoints__0__DeviceId`، `...__0__Address` و `...__0__Port`. مقدار Address باید IP دقیق و Port عدد صحیح باشد؛ نام DNS، wildcard، subnet، تغییر endpoint صرفاً از metadata و اسکن شبکه پشتیبانی نمی‌شوند. شناسه و IP/port باید با snapshot ثبت دستگاه مطابقت داشته باشند. نبود فهرست یعنی هیچ اتصال خروجی مجاز نیست.
5. `dotnet publish ReCode/src/IO.DeviceWorker -c Release -o <deployment-directory>` و سپس `dotnet <deployment-directory>/IO.DeviceWorker.dll`؛ پوشهٔ استقرار خارج از مراجع legacy و خارج Git باشد. سرویس OS را با حساب و سیاست restart مصوب نصب کنید. ثبت Windows Service/systemd و آزمون نصب عملیاتی هنوز انجام نشده‌اند؛ publish دو سیستم جای آن نیست.

## اجرای کنترل‌شده

- از `/workspace/devices` پیوند تست همان دستگاه را باز کنید؛ UUID و نسخه با query string منتقل می‌شوند، نه رمز دستگاه. مجوزهای موجودی، درخواست تست و تاریخچه مستقل‌اند.
- `devices.connection.test`: ثبت با UUID عملیات، نسخهٔ ۸بایتی و مهلت ۱ تا ۶۰ ثانیه؛ لغو فقط pending با نسخهٔ خود درخواست. `devices.connection.read`: تاریخچهٔ سراسری ۵۰تایی. این مجوزها محدودسازی به دستگاه منتخب نیستند.
- قالب تازهٔ admin حق read/test و قالب اپراتور فقط read می‌گیرند. نقش موجود/حق لغوشده خودکار اصلاح نمی‌شود. سرویس grant اجرایی جدا می‌خواهد.
- worker چهار slot انتظار شبکه دارد؛ این تعداد سقف دستگاه یا کاربر نیست. claim اتمیک DB مانع اجرای دو تست ناتمام برای یک دستگاه است. خطای یک slot دیگران را متوقف نمی‌کند. زمان صف جزو مهلت اتصال نیست؛ نبود worker به‌وضوح pending می‌ماند.
- پس از claim، snapshot IP/port همان نسخهٔ تأییدشده استفاده می‌شود؛ تغییر metadata بعدی آن را retarget نمی‌کند. رمز ارتباط خوانده یا ارسال نمی‌شود. برای فرمان‌های نوشتنی آینده، fencing/احراز هویت/قابلیت مدل جدا لازم است.
- grant درخواست‌کننده پیش از claim و grant سرویس در هر claim/result بررسی می‌شود. لغو پس از شروع، بایت ارسال‌شده را برنمی‌گرداند؛ این نسخه هیچ فرمان پروتکلی نمی‌فرستد.
- قطعی/خاموشی پس از claim: کار تا دو دقیقه running می‌ماند؛ worker مجاز بعدی آن را interrupted می‌کند، بدون تکرار. اگر worker دارای مجوز در دسترس نباشد recovery نیز انجام نمی‌شود. هیچ success فرضی یا retry خودکار وجود ندارد.
- خروجی خطا کد ثابت است؛ متن exception، رمز، host یا payload وارد history/audit/stdout نمی‌شود. لاگ worker صرفاً اعلان عمومی خطاست. کارهای pending/running و تاریخچه از UI قابل پیگیری‌اند.

## ماندهٔ سخت‌افزار

مدل/firmware، روش ارتباط، محیط نصب، دستگاه آزمایشی قابل دسترس، مجوز/مستند SDK و نمونهٔ فایل بی‌نام هنوز ارائه نشده‌اند. TCP diagnostic به هیچ مدل واقعی نسبت داده نمی‌شود. adapter واقعی، اطلاعات/تنظیمات، تبادل کاربران، تردد، PUSH، فرمان‌ها و USB پذیرش جدا در S05..S12 دارند. بیومتریک بدون سیاست دسترسی/نگهداری مصوب فعال نمی‌شود؛ کنترل فیزیکی درب خارج دامنه است.

## probe بومی ZK — candidate، نه تأیید مدل واقعی

قرارداد از `Personnel.ZK.ZKDevice.Connect` و `zkemkeeper.IZKEM` در `T5V3.9.2/Personnel.DLL` با hash ثبت‌شده استخراج شد؛ [شاهد](zk-reference-contract.json). فقط API اتصال و خواندن سریال استفاده می‌شود؛ منطق legacy license/activation و فرمان‌های درب وارد پروژه نشده‌اند. SDK باید دارای مجوز استفاده و با installer معتبر و معماری صحیح نصب شود؛ این کد installer یا ثبت خودکار COM ندارد.

- پروژهٔ `IO.ZkBridge` را جدا publish کنید. SDK موجود مرجع x86 است؛ اجرای child با runtime Windows x86 متناسب لازم است، مگر SDK دیگری مستقلاً تأیید شود. وب و worker می‌توانند همچنان .NET 10 x64 باشند. binary مراجع را به خروجی خودکار کپی نکنید. تست نصب Windows/x86 هنوز انجام نشده است.
- `IO__DeviceWorker__BridgeDotnetPath`: مسیر کامل dotnet مناسب child؛ `IO__DeviceWorker__BridgeAssemblyPath`: مسیر کامل `IO.ZkBridge.dll` در پوشهٔ ACLدار و مورد اعتماد، نه ورودی کاربر وب.
- به‌جای AllowedEndpoints، `IO__DeviceWorker__NativeEndpoints__0__DeviceId/Address/Port/MachineNumber/ExpectedSerial` را تنظیم کنید. MachineNumber عدد SDK است، نه شمارهٔ متنی موجودی. ExpectedSerial را از برچسب/مرجع تأییدشدهٔ همان دستگاه بگیرید؛ child فقط سریال را تطبیق می‌دهد و آن را در نتیجه بازنمی‌گرداند. یک DeviceId نمی‌تواند هم TCP و هم native تنظیم شده باشد.
- credential محافظت‌شدهٔ دستگاه باید عدد غیرمنفی قابل تبدیل به Int32 باشد. در نبود credential، اجرا fail-closed است؛ فقط با `...__AllowDefaultCommunicationKey=true` استفادهٔ کلید پیش‌فرض SDK صریحاً مجاز می‌شود. این opt-in را روی دستگاه با کلید واقعی به‌عنوان راه‌حل خطا فعال نکنید.
- برای web و worker، `IO__DataProtection__KeyRingPath` و `IO__DataProtection__ApplicationName` را **یکسان و صریح** تنظیم کنید. مسیر باید موجود، absolute، ACLدار و دارای پشتیبان باشد. worker کلید تازه ایجاد نمی‌کند. تغییر identity/default discriminator در نصب دارای ciphertext قبلی بدون برنامهٔ migration آن رمزها را ناخوانا می‌کند؛ کلید/identity موجود را حفظ یا با مجوز رمزها را دوباره تنظیم کنید. این تنظیم opt-in است و رفتار پیش‌فرض نصب قدیمی را خودکار تغییر نمی‌دهد. حفاظت at-rest/ACL/key escrow خودکار تضمین نمی‌شود.
- فقط Windows اجازهٔ اجرای COM دارد؛ Linux وضعیت unsupported می‌گیرد و کنترل native را در process وب بار نمی‌کند. برای هستهٔ Linux و worker Windows، این نسخه اتصال مستقیم SQL رمز‌شدهٔ صریح دارد؛ تأیید نصب عملیاتی چنین ترکیبی هنوز باقی است.
- `sdk-unavailable`: SDK/COM/معماری یا فراخوانی نامعتبر؛ `sdk-identity-mismatch`: سریال پاسخ با pin نمی‌خواند؛ `credential-unavailable`: کلید/نسخه/مجوز/key ring معتبر نیست. `sdk-serial-matched` احراز هویت رمزنگاری‌شده نیست و به معنای تکمیل S06..S12 نیست. در timeout فرایند child terminate می‌شود؛ هیچ retry خودکار وجود ندارد.

## عملیات S06 (پیش‌فرض خاموش)

برای مشخصات/ظرفیت/ساعت، [راهنمای S06](s06-information-clock.fa.md) را بخوانید. `IO:DeviceWorker:EnableOperations=true` و grant مستقل `devices.operations.execute` لازم است. `AllowClockSet` روی NativeEndpoint پیش‌فرض false است؛ فقط پس از آزمون مدل در آزمایشگاه روشن شود. `devices.clock.set` مستقل از خواندن و حقوق سرویس است؛ timeout نوشتن unknown است و replay خودکار ندارد.
