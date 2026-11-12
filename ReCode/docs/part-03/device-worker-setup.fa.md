# نصب worker و سرویس سیستم‌عامل — اجزای S05–S07/S12، نه پذیرش سخت‌افزار

این worker جدا از وب روی .NET 10 اجرا می‌شود و COM را در process خود یا وب بار نمی‌کند. مسیر اولیه TCP handshake است؛ مسیر candidate ZK در child مستقلِ بخش پایانی همین سند اجرا می‌شود. `tcp-reachable` اثبات دستگاه، رمز، مدل، firmware یا پروتکل نیست. S05 و کل بخش سوم هنوز پذیرفته نشده‌اند.

## پیش‌نیاز و اعتماد

1. از DB پشتیبان بگیرید؛ تمام migrationها تا `20261112150000_DeviceLegacyUserReads` (یا آخرین نسخهٔ تأییدشدهٔ استقرار) با ابزار مهاجرت موجود اعمال شوند؛ اعمال فقط migration اتصال برای قابلیت‌های جدید کافی نیست. میزبان وب/worker خودکار migrate یا seed نمی‌کند. downgrade پس از اولین درخواست تست عمداً خطای 51016 می‌دهد؛ زنجیرهٔ downgrade اتمیک فرض نشود.
2. حساب سرویس اختصاصی سیستم‌عامل و دسترسی SQL کمینهٔ متناسب با query/transactionهای سرویس تنظیم کنید؛ اتصال رمز‌شده با گواهی معتبر و ACL برای تنظیمات لازم است. هویت بنیادینِ فعالِ جدا، بدون نیاز به حساب ورود وب، و نقش اختصاصی دارای فقط `devices.connection.execute` بسازید. هیچ قالب نقش این مجوز را نمی‌دهد. این نسخه هویت بنیادین را در هر claim/ثبت نتیجه از DB بررسی می‌کند؛ نگاشت config به هویت سیستم‌عامل/SQL مسئولیت استقرار است، نه هویت دستگاه یا گواهی دستگاه.
3. تنظیمات صریح از secret store/متغیر محیطی امن یا فایل خارج از Git با ACL بیاید. `ConnectionStrings__IO` رشتهٔ واقعی SQL است؛ رمز آن در چت یا نمونه ثبت نشود. `IO__DeviceWorker__ServiceUserId` UUID هویت بنیادین سرویس است.
4. برای هر endpoint تأییدشده، این کلیدها را تنظیم کنید: `IO__DeviceWorker__AllowedEndpoints__0__DeviceId`، `...__0__Address` و `...__0__Port`. مقدار Address باید IP دقیق و Port عدد صحیح باشد؛ نام DNS، wildcard، subnet، تغییر endpoint صرفاً از metadata و اسکن شبکه پشتیبانی نمی‌شوند. شناسه و IP/port باید با snapshot ثبت دستگاه مطابقت داشته باشند. نبود فهرست یعنی هیچ اتصال خروجی مجاز نیست.
5. `dotnet publish ReCode/src/IO.DeviceWorker -c Release -o <deployment-directory>` و سپس `dotnet <deployment-directory>/IO.DeviceWorker.dll`؛ پوشهٔ استقرار خارج از مراجع legacy و خارج Git باشد. سرویس OS را با حساب و سیاست restart مصوب نصب کنید. ابزار نصب Windows Service و الگوی systemd در بخش پایانی این سند آماده‌اند؛ نصب عملیاتی هنوز آزموده نشده و publish دو سیستم جای آن نیست.

## اجرای کنترل‌شده

- از `/workspace/devices` پیوند تست همان دستگاه را باز کنید؛ UUID و نسخه با query string منتقل می‌شوند، نه رمز دستگاه. مجوزهای موجودی، درخواست تست و تاریخچه مستقل‌اند.
- `devices.connection.test`: ثبت با UUID عملیات، نسخهٔ ۸بایتی و مهلت ۱ تا ۶۰ ثانیه؛ لغو فقط pending با نسخهٔ خود درخواست. `devices.connection.read`: تاریخچهٔ سراسری ۵۰تایی. این مجوزها محدودسازی به دستگاه منتخب نیستند.
- قالب تازهٔ admin حق read/test و قالب اپراتور فقط read می‌گیرند. نقش موجود/حق لغوشده خودکار اصلاح نمی‌شود. سرویس grant اجرایی جدا می‌خواهد.
- worker چهار slot انتظار شبکه دارد؛ این تعداد سقف دستگاه یا کاربر نیست. claim اتمیک DB مانع اجرای دو تست ناتمام برای یک دستگاه است. خطای یک slot دیگران را متوقف نمی‌کند. زمان صف جزو مهلت اتصال نیست؛ نبود worker به‌وضوح pending می‌ماند.
- پس از claim، snapshot IP/port همان نسخهٔ تأییدشده استفاده می‌شود؛ تغییر metadata بعدی آن را retarget نمی‌کند. در مسیر TCP رمز خوانده یا ارسال نمی‌شود؛ native رمز را فقط در claim جاری می‌خواند. مسیر نوشتن S06 به بررسی نسخه/مجوز پیش از dispatch و قفل تغییر نسخه تا تعیین نتیجه نیاز دارد.
- grant درخواست‌کننده پیش از claim و grant سرویس در هر claim/result بررسی می‌شود. لغو پس از شروع، بایت ارسال‌شده را برنمی‌گرداند؛ مسیر TCP هیچ فرمان پروتکلی نمی‌فرستد؛ native probe فقط اتصال/سریال و مسیر S06 عملیات صریح جداگانه دارد.
- قطعی/خاموشی پس از claim: کار تا دو دقیقه running می‌ماند؛ worker مجاز بعدی آن را interrupted می‌کند، بدون تکرار. اگر worker دارای مجوز در دسترس نباشد recovery نیز انجام نمی‌شود. هیچ success فرضی یا retry خودکار وجود ندارد.
- خروجی خطا کد ثابت است؛ متن exception، رمز، host یا payload وارد history/audit/stdout نمی‌شود. لاگ worker صرفاً اعلان عمومی خطاست. کارهای pending/running و تاریخچه از UI قابل پیگیری‌اند.

## ماندهٔ سخت‌افزار

مدل/firmware، روش ارتباط، محیط نصب، دستگاه آزمایشی قابل دسترس، مجوز/مستند SDK و نمونهٔ فایل بی‌نام هنوز ارائه نشده‌اند. TCP diagnostic به هیچ مدل واقعی نسبت داده نمی‌شود. adapter واقعی، اطلاعات/تنظیمات، تبادل کاربران، تردد، PUSH، فرمان‌ها و USB پذیرش جدا در S05..S12 دارند. بیومتریک بدون سیاست دسترسی/نگهداری مصوب فعال نمی‌شود؛ کنترل فیزیکی درب خارج دامنه است.

## probe بومی ZK — candidate، نه تأیید مدل واقعی

قرارداد از `Personnel.ZK.ZKDevice.Connect` و `zkemkeeper.IZKEM` در `T5V3.9.2/Personnel.DLL` با hash ثبت‌شده استخراج شد؛ [شاهد](zk-reference-contract.json). در مسیر diagnostic فقط API اتصال و خواندن سریال استفاده می‌شود؛ مسیر S06 در سند جدا است. منطق legacy license/activation و فرمان‌های درب وارد پروژه نشده‌اند. SDK باید دارای مجوز استفاده و با installer معتبر و معماری صحیح نصب شود؛ این کد installer یا ثبت خودکار COM ندارد.

- پروژهٔ `IO.ZkBridge` را جدا publish کنید. SDK موجود مرجع x86 است؛ اجرای child با runtime Windows x86 متناسب لازم است، مگر SDK دیگری مستقلاً تأیید شود. وب و worker می‌توانند همچنان .NET 10 x64 باشند. binary مراجع را به خروجی خودکار کپی نکنید. تست نصب Windows/x86 هنوز انجام نشده است.
- `IO__DeviceWorker__BridgeDotnetPath`: مسیر کامل dotnet مناسب child؛ `IO__DeviceWorker__BridgeAssemblyPath`: مسیر کامل `IO.ZkBridge.dll` در پوشهٔ ACLدار و مورد اعتماد، نه ورودی کاربر وب.
- به‌جای AllowedEndpoints، `IO__DeviceWorker__NativeEndpoints__0__DeviceId/Address/Port/MachineNumber/ExpectedSerial` را تنظیم کنید. MachineNumber عدد SDK است، نه شمارهٔ متنی موجودی. ExpectedSerial را از برچسب/مرجع تأییدشدهٔ همان دستگاه بگیرید؛ در مسیر diagnostic، child فقط سریال را تطبیق می‌دهد و آن را در نتیجه بازنمی‌گرداند؛ snapshot دارای حق مستقل S06 سریال/مشخصات را به کاربر مجاز نشان می‌دهد. یک DeviceId نمی‌تواند هم TCP و هم native تنظیم شده باشد.
- credential محافظت‌شدهٔ دستگاه باید عدد غیرمنفی قابل تبدیل به Int32 باشد. در نبود credential، اجرا fail-closed است؛ فقط با `...__AllowDefaultCommunicationKey=true` استفادهٔ کلید پیش‌فرض SDK صریحاً مجاز می‌شود. این opt-in را روی دستگاه با کلید واقعی به‌عنوان راه‌حل خطا فعال نکنید.
- برای web و worker، `IO__DataProtection__KeyRingPath` و `IO__DataProtection__ApplicationName` را **یکسان و صریح** تنظیم کنید. مسیر باید موجود، absolute، ACLدار و دارای پشتیبان باشد. worker کلید تازه ایجاد نمی‌کند. تغییر identity/default discriminator در نصب دارای ciphertext قبلی بدون برنامهٔ migration آن رمزها را ناخوانا می‌کند؛ کلید/identity موجود را حفظ یا با مجوز رمزها را دوباره تنظیم کنید. این تنظیم opt-in است و رفتار پیش‌فرض نصب قدیمی را خودکار تغییر نمی‌دهد. حفاظت at-rest/ACL/key escrow خودکار تضمین نمی‌شود.
- فقط Windows اجازهٔ اجرای COM دارد؛ Linux وضعیت unsupported می‌گیرد و کنترل native را در process وب بار نمی‌کند. برای هستهٔ Linux و worker Windows، این نسخه اتصال مستقیم SQL رمز‌شدهٔ صریح دارد؛ تأیید نصب عملیاتی چنین ترکیبی هنوز باقی است.
- `sdk-unavailable`: SDK/COM/معماری یا فراخوانی نامعتبر؛ `sdk-identity-mismatch`: سریال پاسخ با pin نمی‌خواند؛ `credential-unavailable`: کلید/نسخه/مجوز/key ring معتبر نیست. `sdk-serial-matched` احراز هویت رمزنگاری‌شده نیست و به معنای تکمیل S06..S12 نیست. در timeout فرایند child terminate می‌شود؛ هیچ retry خودکار وجود ندارد.

## عملیات S06 (پیش‌فرض خاموش)

برای مشخصات/ظرفیت/ساعت، [راهنمای S06](s06-information-clock.fa.md) را بخوانید. `IO:DeviceWorker:EnableOperations=true` و grant مستقل `devices.operations.execute` لازم است. `AllowClockSet` روی NativeEndpoint پیش‌فرض false است؛ فقط پس از آزمون مدل در آزمایشگاه روشن شود. `devices.clock.set` مستقل از خواندن و حقوق سرویس است؛ timeout نوشتن unknown است و replay خودکار ندارد.

## دریافت کاربران S07 (پیش‌فرض خاموش)

`IO:DeviceWorker:EnableUserDownloads=true`، grant مستقل `devices.users.execute` و `AllowUserRead=true` روی NativeEndpoint لازم است. پیش‌فرض UserReadFormat برابر ssr-v1؛ legacy-int32-v1 فقط با انتخاب صریح منطبق با ReadFormat درخواست و آزمون مدل؛ هیچ fallback یا مجوز نوشتن legacy وجود ندارد. [قرارداد legacy و ارتقا](s07-legacy-users.fa.md)؛ [قرارداد و بازیابی](s07-user-download.fa.md). رمز کاربران/قالب بایومتریک ذخیره نمی‌شود. این مسیر نه مجوز ارسال کاربر است و نه پذیرش سخت‌افزار.

## نصب واقعی سرویس سیستم‌عامل: ابزار آماده، پذیرش عملیاتی هنوز باز

worker اکنون روی Windows با `AddWindowsService` و نام ثابت `IODeviceWorker` به SCM متصل می‌شود؛ اجرای console همچنان ممکن است. `ContentRootPath` پوشهٔ binary منتشرشده است، نه working directory متغیر یا `System32`. بنابراین config معتبر و ACLدار را در پوشهٔ استقرار یا secret provider مصوب نصب کنید؛ فایل محرمانه وارد Git نشود. این تغییر migration یا SDK را اجرا نمی‌کند و grant جدیدی نمی‌سازد.

### Windows / PowerShell 7

1. worker و bridge را در پوشه‌های محلی مورد اعتماد publish کنید. worker و child می‌توانند معماری متفاوت داشته باشند؛ runtime child باید با SDK نصب‌شده هم‌معماری باشد. SDK فقط با installer دارای مجوز فروشنده نصب شود؛ اسکریپت زیر COM یا DLL مرجع را ثبت/کپی نمی‌کند.
2. `ReCode/deploy/device-worker/install-windows-service.ps1 -DotnetPath <absolute-dotnet.exe> -WorkerAssemblyPath <absolute-IO.DeviceWorker.dll> -ValidateOnly` فقط وجود مسیر محلیِ کامل و فایل‌های publish را بررسی می‌کند. این گزینه **ACL، درستی SDK، اتصال SQL، نصب سرویس یا سخت‌افزار** را تأیید نمی‌کند.
3. همان فرمان بدون `-ValidateOnly` را با اختیار نصب Administrator اجرا کنید. `-WhatIf` هم موجود است. نصب با startup دستی و حساب مجازی اختصاصی `NT SERVICE\IODeviceWorker` انجام می‌شود؛ LocalSystem، گذرواژهٔ حساب، grant دیتابیس، policy بازیابی و شروع خودکار ساخته نمی‌شود. سرویس موجود هرگز overwrite نمی‌شود. خروج ناموفق `sc.exe` نصب موفق تلقی نمی‌شود.
4. پیش از `Start-Service IODeviceWorker`، مجوز read/execute پوشهٔ برنامه و runtime، read تنظیمات و key ring موجود، دسترسی SQL لازم و identity فعالِ بنیادین را برای همان حساب فراهم کنید. ACL نوشتن روی binary/runtime را به اپراتور وب ندهید. حساب مجازی مجوز خودکارِ SQL راه‌دور یا هویت دستگاه نیست؛ روش احراز هویت SQL/سرویس باید توسط استقرار تنظیم شود. برای تغییر به حساب سازمانی از ابزار امن سیستم‌عامل استفاده شود؛ password در آرگومان/فایل repo/چت نرود.
5. وضعیت شروع/توقف/راه‌اندازی مجدد را در آزمایشگاه Windows ثبت کنید. اجرای همین script در CI **فقط Parse و ValidateOnly بدون نصب/شروع** است و شاهد نصب عملیاتی نیست. حذف دستی بعد از توقف با `sc.exe delete IODeviceWorker` صرفاً registration سرویس را حذف می‌کند؛ تاریخچهٔ SQL یا اصل فایل‌ها حذف نمی‌شود.

### Linux / systemd

الگوی `ReCode/deploy/device-worker/io-device-worker.service` را پس از provision حساب کم‌اختیار `io-device-worker`، فایل‌های `/opt/io/device-worker` و محیط محرمانهٔ `/etc/io/device-worker.env` نصب کنید. فایل محیط را root با دسترسی 0600 نگه دارد؛ systemd آن را پیش از افت privilege می‌خواند. مسیر dotnet و محل نصب باید با استقرار واقعی تطبیق داده شوند. فایل محیط شناسهٔ فعال سرویس، رشتهٔ اتصال رمز‌شده و endpointهای صریح را تأمین می‌کند؛ نبود آن یا config ناقص fail-closed است.

`Type=simple` و توقف SIGTERM از lifetime استاندارد .NET استفاده می‌کنند؛ `KillMode=control-group` childهای باقیمانده را نیز در stop می‌بندد. `Restart=on-failure` فقط میزبان را برمی‌گرداند، نه replay کار running یا نوشتن unknown. واحد با `ProtectSystem=strict` فایل برنامه/کلیدهای موجود را read-only می‌بیند؛ پیکربندی باید از پیش provision شده باشد. `systemctl daemon-reload`، `systemctl start io-device-worker` و enable فقط پس از آزمون نصب مجازند. COM بومی ZK روی Linux پشتیبانی نمی‌شود؛ این واحد آن را شبیه‌سازی یا تأیید نمی‌کند.

تا اجرای واقعی نصب Windows، چند دستگاه، قطع ارتباط، تکرار دریافت و تبادل کاربران/تنظیمات، **S12 و بخش سوم همچنان بازند**.

## ارسال عادی S08 (پیش‌فرض خاموش)

`IO:DeviceWorker:EnableUserUploads=true`، grant مستقل `devices.users.upload.execute` و `AttendanceOnlyUserWriteApproved=true` روی NativeEndpoint لازم‌اند. SSR یا legacy Int32 فقط attendance-only و **نه ترمینال کنترل درب**، پس از آزمون مدل واقعی؛ این flag اثبات سازگاری نیست. [راهنمای preview/تأیید/بازیابی/partial](s08-user-upload.fa.md). عملیات عادی به سیاست بایومتریک وابسته نیست؛ password قدیمی فقط در حافظهٔ child حفظ می‌شود. صف مشترک چهار slot را بین دریافت کاربران/ارسال عادی/عملیات/diagnostic/استخراج تردد عادلانه تقسیم می‌کند؛ چهار slot سقف تعداد دستگاه نیست.

## استخراج خواندنی S09 (پیش‌فرض خاموش)

`IO:DeviceWorker:EnableAttendanceDownloads=true` و grant مستقل `devices.attendance.execute` برای سرویس، همراه `AllowAttendanceRead=true` روی NativeEndpoint پس از آزمون مدل لازم است. فقط SSR و تقویم Gregorian صریح؛ Linux native unsupported است. بدون DisableDevice/clear/delete/نوشتن ساعت یا درب. کل log با backpressure خوانده می‌شود؛ بازه پس از حفظ tupleها اعمال می‌شود. read ناقص قابل commit نیست؛ worker حق ورود به attendance را از execute به دست نمی‌آورد. [راهنما، بازیابی، trace و محدودیت هویت tuple/multiplicity](s09-attendance-extraction.fa.md).

هر endpoint فیزیکی SDK باید تنها یک DeviceId در نصب مصوب داشته باشد؛ چند alias به همان دستگاه، exclusion مبتنی بر DeviceId را دور می‌زند و سناریوی پشتیبانی‌شده/آزموده‌شده نیست. قبل از پذیرش چند دستگاه، pin سریال و نبود endpoint alias را مستقلاً بررسی کنید.


## زمان‌بندی S10 (پیش‌فرض خاموش)

`IO:DeviceWorker:EnableSchedules=true` و grant مستقل `devices.schedules.execute` لازم‌اند؛ در کنار مسیر استخراج S09 و `EnableAttendanceDownloads/AllowAttendanceRead`، نه جای آن‌ها. سرویس شش صف را نوبتی بررسی می‌کند؛ چهار slot سقف دستگاه نیست. زمان‌بندی زیر اختیار فعلی سازنده، range/cursor و تأیید صریح خودش کار می‌کند؛ ثبت خودکار پیش‌فرض خاموش است و grant execute سرویس به‌تنهایی به attendance نمی‌نویسد. [دامنه/نقش/قطعی/توقف و محدودیت PUSH](s10-sdk-schedules.fa.md).

## تاریخچه و retry S11

migration `20261112100000_DeviceCommandHistory` view پنج صف و lineage immutable تکرارِ صریح خواندن را می‌سازد. config اجرایی یا سرویس هویت تازه‌ای برای تاریخچه ساخته نمی‌شود؛ تلاش فرزند از صف و execute مستقل همان قابلیت استفاده می‌کند. [قرارداد و مرز تنظیمات پیشرفته/RemoteEnroll](s11-command-history.fa.md). این فرمان‌های حساس هنوز adapter مدل/سیاست مصوب ندارند و روشن‌کردن worker آن‌ها را فعال نمی‌کند. نصب Windows/Linux و آزمایش واقعی همچنان انجام نشده است.


## snapshot خواندنی تنظیمات پیشرفته S11 (پیش‌فرض خاموش)

`EnableOperations=true` و `EnableSettingsReads=true` برای worker و `AllowAdvancedSettingsRead=true` برای NativeEndpoint دقیق لازم‌اند؛ دو flag جدید false هستند. هویت سرویس افزون بر `devices.operations.execute` به `devices.settings.read.execute` نیاز دارد. اشتراک صف به معنی مجوز ضمنی نیست: سرویس بدون grant اضافی کار تنظیمات را claim/recover/credential/complete نمی‌کند و کارهای عادی پشت آن متوقف نمی‌شوند. همان شش صف و چهار slot باقی‌اند؛ صف پنهان یا اجرای COM در وب اضافه نشده است.

[راهنمای snapshot/حقوق/بازیابی و تفاوت raw signed/null](s11-settings-read.fa.md). فقط GetDeviceInfo کدهای ثابت مرجع، با pin سریال قبل/بعد و حفظ دادهٔ گزارش‌نشده در SQL؛ نه واحد/دامنهٔ حدسی، SetDeviceInfo، تنظیم شبکه، firmware، پاکسازی، درب یا RemoteEnroll. نصب واقعی SDK/مدل و پذیرش سخت‌افزار همچنان انجام نشده است.


## سلامت صف و heartbeat واقعی سرویس

`IO:DeviceWorker:EnableHealthReports=true` (پیش‌فرض false) همراه grant مستقل `devices.service.report`، report هر۳۰ثانیه را فعال می‌کند؛ این grant اجرایی native نیست و در قالب انسانی وجود ندارد. `/workspace/device-health` با `devices.health.read` زمان‌بندی/صف‌ها و instanceهای واقعی SQL را نشان می‌دهد. [تفسیر missing/stale/stopped، migration، مجوز و بازیابی](s12-service-health.fa.md). heartbeat اثبات نصب SDK یا آنلاین دستگاه نیست.


### آزمون نصب واقعی CI در برابر نصب آزمایشگاه

`tools/test-worker-windows-service.ps1` و `tools/test-worker-linux-service.sh` فقط با opt-in صریح و محیط GitHub runner جدا اجازهٔ نصب/شروع/توقف می‌دهند؛ از محیط production اجرا نمی‌شوند و سرویس موجود را overwrite نمی‌کنند. worker منتشرشده با هویت اختصاصی، DB synthetic عمداً unavailable و بدون pin/endpoint native اجرا می‌شود و منابع موقت پاک می‌شوند. هر دو روی commit تأییدشدهٔ [شاهد سلامت](s12-service-health-evidence.json) موفق شدند؛ این نتیجه نصب SDK/آزمایشگاه کاربر یا پذیرش مدل نیست. `ValidateOnly` installer همچنان واقعاً read-only است؛ smoke نصب مرحلهٔ جداگانه و صریح CI است.


### بازخوانی دیررس زمان‌بندی

`OverlapSeconds` در schedule جدید صریح تصویب می‌شود؛ پیش‌فرض۰ قرارداد قبلی را حفظ می‌کند. تنظیم endpoint جدید یا grant خودکار اضافه نشده است؛ همان اختیار سازنده/سرویس S10 و auto-ingest S09 پابرجاست. [پنجره، clamp، dedup و downgrade 51027](s10-overlap.fa.md). نوشتن دستگاه و PUSH از این flag به دست نمی‌آیند.


### قرارداد مستقل ارسال legacy

UserWriteFormat پیش‌فرض ssr-v1 است. ارسال legacy به UserWriteFormat=legacy-int32-v1 و UserReadFormat منطبق، به‌علاوهٔ EnableUserUploads، حق execute مستقل و AttendanceOnlyUserWriteApproved پس از پذیرش واقعی نیاز دارد. فرمت از snapshot هدف تغییرناپذیر است؛ نوع client نیست. مدل با password masked/خالیِ مبهم نباید تأیید شود؛ null password هدف پیش از mutation رد می‌شود. [راهنما، ارتقای هماهنگ bridge و unknown recovery](s08-legacy-user-write.fa.md). schema جدید نیاز نیست؛ مهاجرت موجود ReadFormat لازم است.


## راه‌اندازی مجدد صریح Standalone؛ جدا از reset/حذف

[بررسی منابع ZKTeco و قرارداد restart](zk-vendor-research.fa.md). وب، worker و bridge را هماهنگ ارتقا دهید و migration `20261112180000_DeviceRestart` را با روند migration موجود اعمال کنید؛ وجود تاریخچهٔ restart، downgrade ناسازگار را متوقف می‌کند.

- `EnableOperations=true` و حق سرویس `devices.operations.execute` مانند قبل لازم‌اند. **افزون بر آن**، `devices.restart.execute` باید صریحاً به هویت worker اعطا شود.
- روی همان NativeEndpoint پین‌شده، `AllowRestart` پیش‌فرض **false** است؛ فقط برای firmware/SDK پذیرفته‌شده در آزمایشگاه فعال شود. این گزینه روی همهٔ مدل‌ها به شکل جمعی روشن نشود. هیچ تغییر IP/port/serial یا استفاده از raw protocol fallback انجام نشده است.
- حق انسانی `devices.restart` با پنل مدیریت نقش/حق اعطا می‌شود؛ هیچ نقش پیش‌فرض از جمله admin خودکار این حق را نمی‌گیرد. مشاهدهٔ تاریخچه و اطلاعات همچنان `devices.info.read` می‌خواهد؛ مجوز restart حق خواندن ضمنی نیست.
- در صفحهٔ `/workspace/device-information` نوع `device.restart`، شناسه/نسخهٔ دستگاه و تأیید صریح را انتخاب کنید. API همان `/device-operations` با `Confirmation=RESTART-DEVICE` و `TargetUtc=null` است.
- `succeeded` فقط پاسخ true SDK است، نه اثبات پایان reboot. false/قطع فرایند/قطع پاسخ پس از dispatch می‌تواند unknown باشد. هر درخواست صفِ بیش از دو دقیقه منقضی می‌شود؛ تنها pending قابل لغو تضمینی است.
- بعد از unknown، ابتدا `information.read` تازه از همان دستگاه/نسخه/منطقه اجرا کنید؛ پس از مشاهدهٔ snapshot از فرم بازبینی نوع restart و `REVIEWED-RESTART-REACHABILITY` استفاده کنید. مسیر API `/device-operations/{id}/resolve-restart` است؛ نسخهٔ عملیات unknown و InformationId تازه لازم‌اند. این کار نتیجهٔ قدیمی را unknown نگه می‌دارد؛ نه فرمانی تکرار می‌کند و نه وقوع reboot را ثابت می‌کند.
- retry عمومی فرمان‌ها هیچ restart را تکرار نمی‌کند. اعطای read-retry، clock یا read-settings مجوز restart نیست. لغو حق انسان پیش از dispatch و لغو حق سرویس پیش از دریافت credential دوباره بررسی می‌شوند.
- این برش `PowerOffDevice`، `ClearGLog`، `ClearData`، `ClearAdministrators`، reset کارخانه، تغییر تنظیم شبکه، ثبت بیومتریک و کنترل درب را فعال نمی‌کند. آزمون واقعی چنددستگاه/قطعی همچنان با تیم است.
