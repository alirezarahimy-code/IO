# R15 — پاکسازی دادهٔ پروفایل مصوب دستگاه؛ صریحاً نه factory reset

## قرارداد بسته و اثر

مرجع واقعی `Personnel.ZK.ZKDevice.DeleteAllData` خط۷۰۸..۷۱۶ فقط `ClearData(MachineNumber,5)` را نشان می‌دهد. در همان مرجع، ClearData۱ برای تردد و ClearAdministrators برای تنزل مدیران جدا هستند. [شاهد باینری و دو TypeLib هش‌ثابت](r15-r16-native-abi-evidence.json). نام DeleteAllData یا flag۵ **اثبات بازگشت کارخانه نیست**.

در این نسخه، R15 تنها پروفایل مصوبِ کاربران تردد با شناسهٔ عددی، رمز/کارت/privilege، ده اسلات fingerprint۰..۹ و تمام تردد native را پاک می‌کند. این بار همهٔ کاربران از جمله مدیران عمداً هدف‌اند؛ حق R13/R14/R11 جایگزین حق R15 نیست. هیچ کارمند، نگاشت، تردد SQL، archive یا سابقهٔ سامانه حذف نمی‌شود. raw template/password در SQL آرشیو یا hash نمی‌شوند؛ خواندن آن‌ها صرفاً transient در child است و تصمیم حذف **غیرقابل‌بازیابی** باید مصوب باشد. backup/restore خودکار یا secure media erase نداریم.

face/photo/palm/vein/اسلات بیشتر، access group/door، audit/encryption/admin-log/hidden stores خارج از این profile و **unsupported** هستند. مدل دارای چنین storeهایی نباید NoOtherStores یا FingerprintOnlyAttendanceProfile را true کند. این اعلام دامنه، ادعای فقدان آن‌ها روی دستگاه ناشناخته نیست. اگر ClearData۵ در نصب، IP/port/key را تغییر می‌دهد یا اثر متفاوت دارد، این قرارداد قابل فعال‌سازی نیست؛ هیچ restore/reset/fallback برای «اصلاح» آن اضافه نشده است.

### تعیین‌تکلیف factory reset

factory reset قابلیت پشتیبانی‌شدهٔ این نسخه نیست: route، flag عمومی، reset button یا اجازهٔ ضمنی ندارد. `family=factory-reset`، property اضافی `factoryReset` در intent و نتیجهٔ خانوادهٔ دیگر رد می‌شوند؛ با واژهٔ «reset» نمی‌توان ClearData۵ را به آن تبدیل کرد. reset **غیرحذفی** فیزیکی برای پاک‌شدن فرمان معلق در بازیابی، روال خارجی دقیق همان نصب است، نه فرمان مخرب جدید نرم‌افزار. افزودن factory reset واقعی نیازمند قرارداد اثر/تنظیمات/credential/بازیابی و پذیرش نرم‌افزاری جدا است؛ نبود این feature پنهان نمی‌شود.

## اجزای عملیاتی

- جدول مستقل `DeviceAllDataErases`، migration `20261116120000_AllDataErases` (شماره۵۷)؛ ۷۱ جدول domain. intent، هر دو archive commitment و شواهد immutable، rowversion، FK هر دو archive و parent، active unique و released-parent unique؛ trigger۵۱۰۴۵ با منع delete/retarget/terminal update/replay. با سابقه، downgrade ممنوع است.
- API مستقل `/devices/all-data-erases`: GET تاریخچه، POST `/preview`، POST proposal، POST `/{id}/approve`، POST `/{id}/cancel`؛ همه authenticated، تغییرها CSRF و bounded JSON. intent فیلد ناشناخته را قبول نمی‌کند.
- صفحهٔ فارسی `/workspace/device-all-data-erases` و help/embedded JS؛ هیچ پنجرهٔ JavaScript یا رشتهٔ approval به‌تنهایی قابلیت را فعال نمی‌کند.
- Worker مستقل Windows، child STA یک‌درخواست، mapper `ZkComAllDataEraseSession` و supervisor `RunAllDataEraseAsync`. emulator از config تولید قابل انتخاب نیست؛ child مصنوعی فقط fixture CI است.

حقوق مستقل زیر پیشوند `devices.all-data.erase.`: `read`, `propose`, `approve`, `execute`, `recover.propose`, `recover.approve`. هیچ role template پیش‌فرض آن‌ها را ندارد. پیشنهاددهنده و بازبین باید هر دو `devices.users.read` و `devices.attendance.read` و scope همان دستگاه را داشته باشند؛ ایجاد/اجرای دریافت هر archive حقوق قبلی جدا دارد. execute سرویس، لغو grant/scope انسان‌ها را دور نمی‌زند.

## فعال‌سازی نصب — همه پیش‌فرض خاموش

```text
IO:AllDataErase:all-data:Enabled = false
IO:AllDataErase:all-data:EnableRecovery = false
IO:AllDataErase:all-data:Approvals = exact AllDataEraseApproval allowlist
```

`AllDataEraseApproval` از این فیلدها ساخته می‌شود:

- `Family="all-data"`؛
- `Inventory`: عین UserEraseApproval معتبر R13 با `Mode="ordinary-user"`، هویت Serial/Product/Firmware، قالب صریح SSR یا legacy، profile ده‌اسلاتی فاقد store دیگر، اثر نصب، مجوز قانونی/نگهداری و تصمیم irrecoverable، کنترل همهٔ کانال‌ها، reset غیرحذفی، کد دقیق absence و تمام flagهای خواندن protected/حذف؛ mode در اینجا قرارداد catalogue است و هدف را به کاربر عادی محدود نمی‌کند؛
- `Attendance`: AttendanceEraseApproval معتبر با **همان هویت نصب**، قالب native صریح، حفظ duplicateها، retention/legal/capture-stop/reset مصوب، bounds؛
- `EffectReference` اثر ClearData۵ با حفظ دسترسی همان endpoint/key؛ `AllowClearAllData`, `PreserveEndpointAndCredential`, `NoOtherStores` همگی صریحاً true پس از احراز خارجی.

NativeEndpoint worker نیز `AllDataEraseApprovals` با دقیقاً یک قرارداد برابر، `AllowUserRead=true`, `AllowAttendanceRead=true`، هر دو قالب مطابق قرارداد، IP/port/serial و machine صحیح، کلید ارتباط و bridge مطلقِ نصب مطمئن لازم دارد. ورودی فرم هیچ‌کدام از این تنظیم‌ها را نمی‌نویسد. نگهداری key-ring، ACL و حساب سرویس طبق راهنمای نصب قبلی مستقل است؛ raw memory در برابر local-admin محافظت اثبات‌شده ندارد.

## دو archive بعد از کنترل فیزیکی، سپس دو انسان

۱. polling و هر کار native رقیب متوقف شود؛ کنترل انحصاری واقعی همهٔ کانال‌های capture، ثبت/ویرایش کاربر و biometric برقرار و تا پایان readback حفظ شود. mutex، دو scan و count ثابت، CAS یا اثبات توقف فیزیکی نیستند.
۲. پس از حداقل۱۰ثانیه settling برای metadata و حداقل settling مصوب Attendance، **دو دریافت کامل** بسازید: کاربران از `/workspace/device-users` و کل تردد SDK از `/workspace/device-attendance`. ingestion فیلترشده یا تعداد raw SQL جای archive تمام native tupleها نیست؛ duplicate، کد ناشناس و حتی سطر قرنطینه‌شدهٔ native در commitment تردد حفظ می‌شوند.
۳. archive کاربران حداکثر۱۵دقیقه، تردد حداکثر age مصوب (پیش‌فرض۹۰۰ثانیه، hard۱۸۰۰)، همان دستگاه/source/company/site/zone و device-version، succeeded و کامل/contiguous باشد. آغاز کنترل حداکثر۳۰دقیقه پیش و UTC است. سه نسخهٔ مستقل DeviceVersion/ArchiveVersion/AttendanceArchiveVersion لازم است.
۴. در فرم UUID ثابت intent و UUID/rowversion هر دو archive، زمان کنترل و مرجع شواهد و approval دقیق را وارد کنید. preview فقط DB است؛ hash به تمام intent، metadata/count کاربران و digest/count **تمام multiset تردد** متصل می‌شود. هیچ password/template وارد preview، receipt یا hash نمی‌شود.
۵. `DELETE-DEVICE-ALL-DATA-IRREVERSIBLY` فقط proposed می‌سازد و endpoint را رزرو می‌کند. شخص دوم مستقل با scope و دو حق خواندن archive و حق approve، اثر و هر دو archive/شواهد را بررسی می‌کند. self-approval ممنوع؛ proposal/authority اجرا حداکثر۲دقیقه. request UUID برای retry دقیق است، نه فرمان حذف جدید؛ تغییر intent با همان UUID conflict است.

## native execution و معنای دقیق نتیجه

- هویت کامل قبل/بعد scan؛ catalogue کامل transient metadata/password/card/ده اسلات **تمام کاربران** و scan تمام تردد، counts قبل/بعد با enumeration کامل برابر؛ metadata و digest/count log عین دو archive باشند.
- دو observation کامل باید یکسان باشند: protected users با تمام رمز/templateها نیز برابر، نه فقط metadata/count. جایگزینی هم‌شمار، read ناقص، تغییر هویت یا سقف، اجازهٔ clear نمی‌دهد. فقط خطای absence دقیق و تمیز مصوب نصب، غیبت fingerprint است.
- دقیقاً یک `ClearData(machine,5)`؛ هیچ flag از کاربر، fallback، ClearAttendance، ClearAdministrators، reboot یا factory reset اجرا نمی‌شود.
- پس از true، Disconnect و **یک اتصال خواندنی با همان IP/port/key اصلی**؛ credential پیش‌فرض یا مقصد دیگر امتحان نمی‌شود. هویت دوباره pin، enumeration کامل کاربران و تردد باید هر دو واقعاً خالی باشند. برای هر شناسهٔ قبل، ده اسلات دوباره probe می‌شوند تا orphan fingerprint قابل‌خواندن پنهان نماند.
- فقط این حالت `all-data-cleared-verified` است. این state اثبات تنظیمات کارخانه، secure erase، فقدان store اعلام‌نشده یا حفظ همهٔ تنظیمات نیست؛ صرفاً empty readback پروفایل مصوب و دسترسی/هویت با همان endpoint/key است. ادعای حفظ endpoint/key واقعی نیز از نصب شرط گرفته و با reconnect آزموده می‌شود، نه از نام API.
- اگر دو archive/live از ابتدا هر دو خالی باشند، `already-no-profile-data` بدون command است؛ موفقیت پاکسازی قبلی یا نبود store پنهان نتیجه نمی‌شود.
- false/exception، loss of endpoint/key، remaining users/logs/orphans، deadline، crash یا خروجی نامعتبر بعد از dispatch: unknown بدون retry/restore/requeue. لغو running فقط authority را می‌گیرد؛ late success به unknown تبدیل می‌شود. اصل unknown ثابت می‌ماند. grant/scope/config/versions در claim/credential، هر۵۰۰ms و قبل از durable completion بازبینی می‌شوند.

حدها: users پیش‌فرض۱۰۰/hard۱۰۰۰؛ هر template و طول native حداکثر۶۵۵۳۶؛ مجموع template chars پیش‌فرض۱Mi/hard۸Mi؛ logs hard۱۰۰۰۰۰؛ deadline child حداکثر۶۰ثانیه. حد فنی به معنی توان انجام سه scan و orphan probes در۶۰ثانیه روی همهٔ مدل‌ها نیست؛ دامنه‌ای که جا نمی‌شود unsupported می‌ماند، نه حذف split یا timeout حدسی. SQL authority-check نیز دو archive را دوباره تطبیق می‌دهد؛ نصب‌های بزرگ باید زمان/اندازهٔ واقعی را قبل از فعال‌سازی بسنجند.

## بازیابی اختصاصی unknown

تمام native writes رقیب با unknown یا active/observed این صف fence می‌شوند. دریافت خواندنی برای ساخت archive بازیابی پس از توقف child قبلی مجاز است. child/worker قبلی را متوقف، روال reset **غیرحذفی** مصوب برای پاک‌شدن command معلق را اجرا و کنترل انحصاری/settling را دوباره برقرار کنید؛ سپس **هر دو archive تازه پس از آن** بسازید.

`RecoveryOfId` باید unknown اصلیِ همین نصب/نسخه/approval باشد؛ حق recover.propose و switch recovery مستقل لازم‌اند. این proposal pending فقط inspection می‌سازد؛ child InspectOnly از Users/Log خواندنی فراتر نمی‌رود و ClearData/ reconnect command path ندارد. موفقیت observation هر دو archive فقط `observed` است. شخص مستقل در۲دقیقه با نسخهٔ تازه و `RELEASE-ALL-DATA-FENCE-NOT-DELETE-SUCCESS` آن را بررسی کند تا رکورد بازیابی released و fence رفع شود؛ unknown اصلی هرگز موفق یا حذف نمی‌شود. مشاهدهٔ فعلی اثبات نتیجهٔ command قبلی نیست.

observation منقضی/نامطلوب با cancel نسخه‌دار توسط recover.propose به inspection-failed می‌رود؛ زمان و شواهد آن حفظ و original unknown مسدود می‌ماند. سپس archive/inspection تازه لازم است. release یک خانوادهٔ دیگر یا یک bare snapshot نمی‌تواند R15 را unlock کند. تمام تاریخچه از delete/retarget/replay مصونیت نرم‌افزاری/SQL دارد؛ DB admin یا local-admin خارج از این تضمین‌اند.

## پذیرش و عدم ادعای آزمایش واقعی

[شاهد نهایی Windows و UI](r15-r16-software-evidence.json) SHA/job و شمار واقعی را ثبت می‌کند. آزمون mapper/executor/supervisor مصنوعی، SQL/permission/rowversion/immutability/fence، worker واقعی Windows با child مصنوعی، recovery observation→discard→fresh observation→independent release، HTTPS/TLS/login/CSRF/default-off/live-revoke و DOM دو archive را پوشش می‌دهد. SDK/COM، دادهٔ بیومتریک واقعی و حذف واقعی اجرا نشده؛ Linux، suite کامل تاریخی SQL۱۰۳۱ و rollback کامل نیز دوباره اجرا نشده‌اند. فعال‌سازی واقعی تا H01 و تصمیم مجاز سازمان خاموش است.
