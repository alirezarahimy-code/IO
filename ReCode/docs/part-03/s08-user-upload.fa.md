# S08 — candidate ارسال عادی کاربران، نه پذیرش دستگاه واقعی

`/workspace/device-user-uploads` و `/device-user-uploads` به جدول `DeviceUserUploads` متصل‌اند. هر سطر یک intent برای یک کارمند/هویت دقیق/دستگاه است؛ `BatchId` گروه آن‌هاست و موفقیت یک نفر نتیجهٔ دیگری را عوض نمی‌کند. هر صفحه ۱–۵۰ آیتم مستقل دارد؛ این محدودیت پردازش است نه سقف تعداد کاربران یا دستگاه‌های محصول.

## پیش‌نمایش، تأیید و حقوق

۱. snapshot موفق S07 از **هدف** همان نسخه لازم است؛ شخص تکراری یا snapshot ناقص رد می‌شود. هر شخص باید نگاشت جاری صریح به همان کارمند و شرکت داشته باشد؛ mapping/کارمند خودکار ایجاد نمی‌شود.

۲. preview سرور `expected` واقعی هدف و `desired` را برمی‌گرداند؛ نام از کارمند فعلی خوانده می‌شود. دادهٔ حضور و غیاب عادی: شناسهٔ عددی دقیق با صفر اول، نام بدون truncate تا ۲۰ نویسه، کارت رشتهٔ عددی یا خالی و enabled صریح. شخص admin و privilege غیرصفر پشتیبانی نمی‌شود. کارت خالی عمداً به معنی پاک‌کردن کارت و ایجاد شخص جدید عمداً بدون password است؛ هر دو پیش از صف قابل مشاهده/تأییدند.

۳. درخواست صف به اثر دقیق preview و عبارت `UPLOAD-ORDINARY-USERS` بسته است. تغییر نام کارمند/داده بین preview و صف به preview تازه نیاز دارد. retry پاسخ نامعلوم ثبت، همان batch، آیتم‌ها و hash را نگه می‌دارد؛ تغییر آن تعارض است. فایل request، password یا template در SQL ذخیره نمی‌شود؛ فقط intent عادی و نتیجه نگهداری می‌شود.

`devices.users.upload` مستقل از `devices.users.read` و `devices.users.upload.execute` است. admin تازه upload می‌گیرد، operator تازه فقط read موجود؛ هیچ قالب انسانی execute نمی‌گیرد. grant/revoke و منو/help سرور واقعی‌اند. مجوزها سراسری‌اند؛ محدودسازی به دستگاه منتخب ادعا نمی‌شود.

## اجرای مستقل و حفاظت

worker پیش‌فرض ارسال را خاموش نگه می‌دارد. `IO:DeviceWorker:EnableUserUploads=true` و grant مستقل execute لازم‌اند. `AttendanceOnlyUserWriteApproved=true` روی NativeEndpoint فقط برای مدل **حضور و غیاب بدون قابلیت کنترل دسترسی فیزیکی** پس از آزمون آزمایشگاه مجاز است؛ flag اثبات سازگاری نیست. مدل/firmware واقعی هنوز تأیید نشده و این opt-in در استقرار پیش‌فرض false است. غیرWindows unsupported است.

هر دستگاه حداکثر یک upload running دارد؛ صفحهٔ چندنفره pendingهای مستقل را به ترتیب claim می‌کند. exclusion مشترک با دریافت/diagnostic/عملیات و تغییر هویت/credential اعمال می‌شود. شناسه/نسخه و grant پیش از dispatch دوباره بررسی می‌شوند. یک child خصوصی با serial/IP/port pin و deadline خارجی برای هر نفر اجرا می‌شود؛ COM در وب/worker بارگذاری نمی‌شود.

child به‌صورت read-only هویت دقیق فرد را دوباره پیدا می‌کند؛ شمارندهٔ ناقص/شناسهٔ تکراری یا تفاوت با expected باعث عدم overwrite می‌شود. password موجود فقط در حافظهٔ همان child خوانده و عیناً به SDK بازگردانده می‌شود؛ وارد API/IPC خروجی/SQL/log نمی‌شود. فیلد جدید password از وب پذیرفته/اعمال نمی‌شود. هیچ setter مربوط به AccGroup/privilege غیرصفر، disable کل دستگاه، batch overwrite، delete، finger/face/template/photo یا فرمان درب فراخوانی نمی‌شود. پس از write، نام/کارت/enabled/privilege و حفظ password read-back می‌شوند؛ تفاوت، موفقیت اعلام نمی‌شود.

**محدودیت مهم:** enumeration با شمارندهٔ پایدار snapshot اتمیک یا compare-and-swap سخت‌افزاری نیست. مدیریت خارجی هم‌زمان دستگاه باید در نصب مصوب منع شود. تضمین حفظ سایر metadata/عملکرد مدل از امضای SDK به دست نمی‌آید و نیازمند آزمون واقعی است؛ این candidate برای مدل کنترل درب مجاز نیست.

## قطعی، partial و retry

لغو تضمینی فقط pending و نسخه‌دار است. نتیجهٔ هر فرد `succeeded`/`unchanged`، تعارض پیش‌نویس، unsupported، failed-before-write یا unknown جدا می‌ماند. پس از dispatch، timeout، خروجی ناقص یا حتی پاسخ false از write، unknown است؛ هیچ retry خودکار وجود ندارد. recovery پس از deadline کامل child به‌اضافهٔ دو دقیقه grace، running رهاشده را unknown می‌کند.

unknown همان فرد/هدف را مسدود می‌کند. ابتدا snapshot S07 موفقِ همان نسخه با زمان درخواست **پس از پایان نوشتن** ایجاد و وضعیت واقعی فرد (از جمله نبود فرد) را بررسی کنید؛ سپس با نسخهٔ نتیجه و `REVIEWED-USER` شاهد را ثبت کنید. نتیجهٔ قدیمی همچنان unknown و immutable می‌ماند. retry انتخابی با intent و snapshot تازه است؛ افراد موفق نباید دوباره انتخاب شوند. تغییر هویت/credential مقصد تا حل کار/unknown مسدود است.

## آزمون و پذیرش

آزمون‌های SQL، HTTP و DOM و تست مستقل state machine/IPC اضافه شده‌اند؛ کد `ce81149` در SQL/HTTPS/browser `43218632179` و Windows/Linux `43218632286` موفق شد: ۵۵۱ integration، ۴۳ Foundation، ۴۵ portable، ۱۰۱ حق، ۴۷ صفحه/مقاله، ۳۰ migration و ۵۴ جدول. آزمون mock، SDK یا نصب مدل واقعی نیست. بایومتریک بدون سیاست مصوب همچنان غیرفعال است؛ این مانع ارسال عادی نیست. S09–S11 و سناریوی واقعی چند دستگاه/قطعی/تبادل/تنظیمات باقی‌اند و بخش سوم کامل نشده است.

[شاهد ماشین‌خوان و مرزهای پذیرش](s08-component-evidence.json). آزمون مرورگر واقعی شامل preview نام/دادهٔ SQL، تأیید، صف، لغو و reload است؛ تست HTTP جداگانه envelope واقعی preview، hash و چرخهٔ صف/لغو را نیز بررسی می‌کند. این شاهد نرم‌افزاری است، نه تبادل واقعی SDK.


## ادامهٔ non-TFT

[ارسال عادی legacy با UserWriteFormat مستقل](s08-legacy-user-write.fa.md) اضافه شده است: snapshot هدف immutable نوع را تعیین می‌کند؛ legacy پیام خصوصی جدا دارد، unknown review قرارداد یکسان می‌خواهد، null password به خالی تبدیل نمی‌شود و مدل با password masked قابل تأیید نیست. متن SSR بالا قرارداد اولیه است؛ نوشتن non-TFT اکنون candidate نرم‌افزاری است، نه تأیید مدل واقعی.
