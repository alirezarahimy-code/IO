# بررسی منابع ZKTeco و قراردادهای باقیماندهٔ بخش سوم

این گزارش نتیجهٔ بررسی منابع عمومی و رسمی در این نوبت است؛ مستند API، آزمون SDK یا پذیرش سخت‌افزار محسوب نمی‌شود. مدل/firmware واقعی هنوز مشخص نشده است. منابع مرجع محلی فقط خوانده شدند؛ فایل‌های باینری «سورس سازنده» نیستند.

## منابع و درجهٔ اتکا

| مرجع | نوع و دسترسی | نتیجهٔ قابل استفاده |
|---|---|---|
| https://www.zkteco.com/en/FSeries/F18 | صفحهٔ رسمی، متن محصول خوانده شد | F18 با AC push استاندارد، standalone اختیاری و TA push اختیاری عرضه می‌شود؛ HTTPS نیز optional است. نام F18 به تنهایی قرارداد پروتکل/امنیت نیست. |
| https://desarrollo.zktecolatinoamerica.com/sdk | پرتال رسمی منطقه‌ای، متن نمایه‌شدهٔ جست‌وجو؛ دریافت مستقیم برخی صفحات ناموفق بود | Standalone SDK، Pull، کنترل دستگاه و ثبت آنلاین خانواده‌های مجزای API دارند؛ فهرست قابلیت، تأیید هر firmware نیست. |
| https://usermanual.wiki/Document/ManualSDKVersaoZkTeco.61245417/html | نسخهٔ بازنشرشدهٔ راهنمای SDK، متن نمایه‌شده؛ نه میزبان رسمی | بخش 5.5.1.6: `VARIANT_BOOL RestartDevice(LONG dwMachineNumber)` برای راه‌اندازی مجدد. امضا با metadata محلی `IZKEM`، `DispId(110)` مطابقت دارد. پاسخ SDK اثبات مستقل پایان reboot نیست. |
| https://desarrollo.zktecolatinoamerica.com/sdk/set_device_ip | رسمی منطقه‌ای، متن نمایه‌شده | `SetDeviceIP(machine, IPAddr)`؛ تغییر شبکه می‌تواند اتصال را قطع کند. باید مقصد قدیم/جدید، pin سریال و بررسی دسترسی جدا ذخیره شود؛ retry خودکار مجاز نیست. |
| https://desarrollo.zktecolatinoamerica.com/sdk/get_device_info | رسمی منطقه‌ای، متن نمایه‌شده | codeهای تنظیمات متعدد و شامل کنترل درب هستند. از مقدار خام فعلی نمی‌توان واحد/دامنهٔ مجاز نوشتن را نتیجه گرفت؛ codeهای درب به IO اضافه نمی‌شوند. |
| https://desarrollo.zktecolatinoamerica.com/sdk/clearg_log | رسمی منطقه‌ای، متن API و نمونه در جست‌وجو | `ClearGLog(machine)` همهٔ ترددها را حذف می‌کند؛ حذف بازهٔ انتخابی نیست. نمونه دستگاه را موقتاً disable می‌کند؛ این رفتار نباید بدون قرارداد عملیاتی کپی شود. |
| https://desarrollo.zktecolatinoamerica.com/sdk/clear_data | رسمی منطقه‌ای، متن نمایه‌شده | flag 5 علاوه بر اطلاعات کاربران، templateها را حذف می‌کند؛ `ClearData` معادل factory reset نیست. |
| http://desarrollo.zktecolatinoamerica.com/sdk/start_identify | رسمی منطقه‌ای، نمونهٔ نمایه‌شده | نمونه قبل از `StartEnrollEx`، `SSR_DelUserTmpExt` فراخوانی می‌کند؛ حذف template نباید بخشی پنهان از ثبت ازراه‌دور باشد. پاسخ شروع، پایان ثبت نیست. |
| https://cdn.tvc.mx/media/92185/General-Introduction-of-PUSH-SDK-Protocol.pdf | PDF سازنده روی میزبان ثالث؛ متن کامل کوتاه خوانده شد | سند قدیمی، access authentication و HTTPS را bold و برای successive releases معرفی می‌کند. بنابراین مدرک کافی برای امنیت firmware فعلی نیست. |
| https://pdfcoffee.com/securitypushcommunicationprotocol-pdf-free.html | بازنشر راهنمای Security PUSH؛ متن نمایه‌شدهٔ ژانویهٔ 2021، protocol 3.1.2 / doc 2.3 | SessionID/RegistryCode و Token و تبادل کلید مطرح‌اند؛ Security/AC PUSH را نمی‌توان بدون تطبیق به TA PUSH تعمیم داد. Token مبتنی بر MD5، به‌تنهایی امضای بدنه یا ضد replay را ثابت نمی‌کند. |
| https://github.com/0xa9u/zkteco-security-push-adms-python | پیاده‌سازی اجتماعی diagnostic، نه تأیید سازنده | خود پروژه نبود secure-exchange و full session authentication را تصریح می‌کند. پاسخ handshake و allowlist سریال، احراز هویت رمزنگاری‌شده نیست. |
| https://github.com/adrobinoga/zk-protocol | مهندسی معکوس اجتماعی | راهنمای مقایسه، نه مبنای تضمین چندمدلی. raw command جایگزین ضمنی Standalone COM نمی‌شود. |
| https://www.zkteco.com/en/Biometrics_Module_SDK/ZKFinger-SDK-for-Linux | صفحهٔ رسمی/نمایهٔ محصول | SDK اسکنر اثرانگشت را نباید به عنوان SDK مدیریت ترمینال attendance معرفی کرد. |

## برش پیاده‌سازی‌شده در این نوبت: `device.restart`

- قرارداد بستهٔ `ZkOperationInput` و اجرای دقیقاً یک `RestartDevice` در bridge مستقل Windows، پس از اتصال و تطبیق سریال.
- حق انسانی `devices.restart` و حق سرویس `devices.restart.execute` جدا از clock/read؛ هیچ نقش پیش‌فرض خودکار این دو حق را دریافت نمی‌کند. اعطا/لغو از پنل حقوق موجود انجام می‌شود.
- `AllowRestart=false` پیش‌فرض مقصد نصب؛ علاوه بر آن `EnableOperations` و `devices.operations.execute` لازم است. pin مقصد و سریال موجود حفظ شده است.
- تأیید `RESTART-DEVICE`، شناسهٔ immutable، نسخهٔ دستگاه، مهلت محدود و انقضای صف بعد از دو دقیقه.
- پاسخ true صرفاً `succeeded` با معنای تأیید SDK است؛ false یا exception بعد از فراخوانی و timeout/خروج نامعتبر فرایند، `unknown` می‌شوند؛ تکرار خودکار وجود ندارد.
- تکرار ثبت با همان شناسه/داده، همان intent را بازمی‌گرداند؛ درخواست نوشتن تازه تا بازبینی unknown مسدود است. صفحهٔ retry عمومی فقط خواندن را تکرار می‌کند، نه restart.
- بازبینی `REVIEWED-RESTART-REACHABILITY` با خواندن اطلاعات موفقی که بعد از پایان unknown درخواست شده، برای همان دستگاه/نسخه/timezone انجام می‌شود. این بازبینی فقط مشاهدهٔ دسترسی مجدد را ثبت می‌کند؛ وقوع reboot را ثابت و نتیجهٔ اصلی unknown را به success تبدیل نمی‌کند.
- تغییر metadata/credential/group در pending/running یا unknown حل‌نشده مسدود است. لغو فقط پیش از dispatch تضمین دارد. تاریخچه و evidence در SQL immutable باقی می‌مانند؛ migration downgrade با تاریخچهٔ restart رد می‌شود.

مسیرهای اصلی: `ZkRestartExecutor.cs`، `ZkOperationContract.cs`، `IO.ZkBridge/Program.cs`، `Devices/DeviceOperations.cs`، `20261112180000_DeviceRestart.cs` و صفحهٔ موجود `DeviceInformation.html`.

## موارد هنوز باز؛ نتیجهٔ پژوهش، نه پیاده‌سازی کامل

1. **تنظیمات/شبکه:** APIها شناسایی شدند. مدل باید قرارداد code/واحد/دامنه/read-back و مقصد قدیم/جدید داشته باشد؛ نوشتن generic arbitrary code اضافه نشده است.
2. **PUSH احراز هویت‌شده:** خانوادهٔ TA/AC، نسخهٔ واقعی پروتکل و جزئیات secure exchange، bootstrap trust و ضد replay باید تطبیق داده شوند. endpoint عمومی با SN یا یک HMAC ساختگی که firmware نمی‌فرستد، راه‌حل نیست.
3. **RemoteEnroll/انتقال بیومتریک:** API شروع شناخته شد، ولی success شروع با تکمیل ثبت متفاوت است. بدون حذف ضمنی template، قرارداد event/result و سیاست مصوب دسترسی/نگهداری لازم است. template خام در log/گفتگو ذخیره نشده است.
4. **reset/حذف تردد/کاربر/مدیر:** دامنهٔ حذف و آثار template باید پیش‌نمایش، snapshot/backup، حقوق مستقل و unknown recovery داشته باشند؛ `RestartDevice` این عملیات را پیاده‌سازی یا مجاز نمی‌کند.

`part3Complete=false`، `realSdkExecuted=false` و `hardwareVerified=false` باقی می‌مانند. نتایج آزمون این برش در evidence جدا ثبت می‌شوند؛ وضعیت CI در زمان نگارش اولیه هنوز نهایی نشده است.
