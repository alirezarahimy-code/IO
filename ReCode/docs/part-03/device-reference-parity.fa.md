# تطبیق سرتاسری بخش دستگاه با T5Web و آریا ادمین

**نتیجهٔ صریح: تطبیق موجودی کنش‌های زیر انجام شد و ایرادهای مشخص اصلاح شدند؛ تمام قابلیت‌های بخش دستگاه هنوز تکمیل نشده و سخت‌افزار واقعی آزمایش نشده است.** این گزارش نه «هیچ مشکلی باقی نیست» می‌گوید و نه ظاهر یک دکمه را اجرای دستگاه حساب می‌کند.

## مرجع دقیق؛ اصلاح ابهام نام آریا

- T5Web اصلی: باینری `T5Web [3.5.7]/App/bin/T5V3Web.dll`، چهار کنترلر `DeviceController`، `DeviceOperationsController`، `iclockController` و `USBManagementController`. **۴۴ اظهار action عمومی** شامل overloadها بررسی و نگاشت شد؛ این خروجی decompile با ILSpy9.1 است، نه سورس اصلی. هش باینری، هش خروجی، signature و شماره خط در موجودی آمده‌اند. helperها action شمرده نشده‌اند؛ متدهای forwarding و خطا/ذخیرهٔ مرتبط نیز برای تعیین رفتار خوانده شدند.
- آریا ادمین: **سورس تاریخی** `src/T5Web` در commit ثابت `2c01d1074ebf18adc3c43063dd14dbe639246e04`؛ نه `TWF 4.28`. چهار اظهار action در `T5Web.Web/Controllers/DeviceController.cs`، دو سرویس `DeviceCommandService/DeviceTransport`، دو view و راهنمای اتصال آن خوانده شدند. پنج command catalog جدا نگاشت شد. آریا کاربری `src/TWF` و TWF مرجع درخواست‌هاست؛ با آریا ادمین یکی نیست.
- یازده view قدیمی شامل Device، DeviceOperations، انتخاب دستگاه/شخص، UserInfo، Gate و USB در manifest هش‌دار آمده‌اند. موجودی۷۶۷ شاهد field/route قبلی نیز حفظ شده؛ این عدد شاهد نحوی است، نه۷۶۷ عملیات موفق.
- `Personnel.ZK.ZKDevice` و امضای `IZKEM` مرجع تکمیلی اتصال/SDK هستند؛ [شاهد قبلی](zk-reference-contract.json). هیچ EXE، DLL قدیمی، سرویس Link5 یا دیتابیس مشتری اجرا نشده و هیچ مرجع تغییر داده نشده است.

[موجودی هش‌دار](device-reference-inventory.json) · [جدول کامل۵۳ ردیفی](device-reference-crosswalk.csv).

## یافته‌های مهم و تصمیم اجرایی

| موضوع | شاهد مرجع | نتیجه و اصلاح در IO |
|---|---|---|
| صف بدون مصرف‌کننده | AriaAdmin `DeviceCommandService.Validate` و `DeviceTransport` و docs15 | catalog پنج‌تایی یا ردیف queue به‌تنهایی آداپتر نیست؛ worker و bridge واقعیِ بستهٔ IO حفظ شد؛ پیام SQL queued، پاسخ دستگاه نامیده نمی‌شود. |
| HTTP200 به‌جای موفقیت SDK | T5 `CallDeviceService` و متدهای Restart/Set/Delete | الگوی قدیمی کپی نشد؛ نتیجه typed/unknown و read-back مطابق هر قرارداد باقی است. Aria transport نیز GET می‌فرستد؛ برای upload قدیمی که XML و flag می‌خواهد، صرف تطبیق نام endpoint هم‌ارزی نیست. |
| هویت و اسرار | T5 Edit و UploadUsers، لیست بلند audit شامل CommPassword/DevicePassword؛ badge تبدیل long | لاگ رمز/الگو منتقل نمی‌شود؛ credential محافظت‌شده فقط برای claim دقیق. SSR رشتهٔ دقیق، legacy هویت Int32 صریح دارد؛ leading zero بی‌صدا حذف نمی‌شود. |
| تغییر claim پس از اخذ token | مرور مسیرهای Credential/Load در پنج صف IO | Host/Port، بودجه، profile/format و clock یا expected/desired user، با رکورد واقعی و دستگاه جاری تطبیق داده شدند؛ token مالکیت به‌تنهایی برای payload دست‌کاری‌شده کافی نیست. پنج گروه regression SQL اضافه شد. |
| تغییر نگاشت بعد از claim | IO user upload | نگاشت جاری، Source/Company، نام فعلی شخص و payload پیش از تحویل credential دوباره بررسی می‌شوند؛ نگاشت منقضی/تغییریافته مجوز نوشتن نیست. |
| تداخل polling و unknown | IO صف‌های clock/restart/network/idle و user upload | polling فعال قبل از هر چهار نوشتن مانع است؛ native write pending/running/unknown ایجاد یا resume schedule را مسدود می‌کند؛ unknown ساعت/restart نیز مانند شبکه/idle مانع user write است. unknown user مانع clock/restart هم شد. خواندن recovery باز می‌ماند. |
| دادهٔ ظرفیت نادرست | Personnel GetDeviceCapacity یک ref Int32 را با عدم بررسی نتیجه چند بار استفاده می‌کند | reader قابل‌آزمون با همان۹ code ثابت، null مستقل و حفظ صفر؛ سریال قبل/بعد کنترل می‌شود، snapshot بیگانه برای review پذیرفته نمی‌شود. نام vendor ثابت تولید نمی‌شود. |
| GetAdvanced2 ناقص | T5 GetAdvanced2: IP/Algorithm/Sector؛ Personnel GetAlgorithmVersion fallback9 | دو خواندن ثابت ReportedIp و AlgorithmVersionRaw اضافه شد؛ sector همان62 است. خطا null است، نه نسخه۹ یا IP پیش‌فرض. نه setter عمومی و نه ادعای سازگاری بیومتریک. |
| کلید عملکردی، نه رمز | T5 ReadKey/ChangeKey، Personnel FunctionKey و SSR_GetShortkey/SSR_SetShortkey | برداشت مبهم قدیمی در گزارش اولیه تصحیح شد؛ رمز ارتباط با function key یکی نیست. این قابلیت هنوز adapter/قرارداد مستقل ندارد و غیرفعال است. |
| دریافت تردد و ثبت جدا | T5 GetRecords/GetTimeRecords سپس Thread(SaveRecords) | صف SQL، اصل tuple، quarantine و commit پایدار جای پاسخ قبل از ثبت و خطاهای پنهان را گرفته‌اند؛ فیلتر بازه در سرور است و هم‌ارزی API بازه‌ای دستگاه ادعا نشده است. |
| USB | T5 USBManagement.ReadRecords: ASCII، RecordReader و BulkInsert | فایل اصلی و parse/preview/commit مستقل و محدود IO حفظ شد. در این بدنهٔ کنترلر بررسی مستقیم lock دیده نشد؛ این به معنی نفی تمام وابستگی‌های لایه‌های پایین نیست. حذف کلی USB با استناد صرف به نام دانگل قابل توجیه نیست. |
| PUSH | T5 iclock: SN lookup، Encrypt=0، zone ثابت210، OK حتی بدون پذیرش معتبر؛ getrequest بدون صف عملی | کد آسیب‌پذیر به‌عنوان PUSH امن منتقل نشد. احراز هویت/replay/bounds/model contract هنوز باید ساخته شود؛ polling PUSH نیست. |
| دامنهٔ اپراتور | T5 Index/DownloadUsers از GetManagerDevices استفاده می‌کند | [دامنهٔ دستگاه منتخب](s04-device-operator-scope.fa.md) با سیاست نسخه‌دار، انتساب/لغو واقعی، فیلتر دادهٔ شخصی و کنترل جاری انسان در worker پیاده و نرم‌افزاری آزموده شد. گروه metadata همچنان scope نیست؛ مدیر محدود به درخت اداری سراسری دسترسی ندارد. |
| side effectهای قدیمی | Device.Edit مقدار Push/stampها را بازتنظیم می‌کند؛ TimeSync ساعت میزبان را به دستگاه می‌برد | این تغییرهای ضمنی منتقل نشدند؛ metadata/credential مستقل، زمان UTC صریح و نسخه/تأیید لازم است. |

## وضعیت همهٔ کنش‌ها؛ نه فقط دکمه‌های فعلی

وضعیت «پیاده‌شده» فقط نرم‌افزار است، نه پذیرش مدل واقعی. «جایگزین ایمن» یعنی تفاوت آگاهانه و مستند، نه وانمودکردن هم‌ارزی قدیمی. overloadها جدا هستند و در مجموع شمارش می‌شوند.

| وضعیت | تعداد ردیف |
|---|---:|
| بخشی ساخته؛ مانده دارد | 12 |
| پیاده‌شده؛ آزمون نرم‌افزاری | 14 |
| جایگزین ایمن؛ نه هم‌ارزی عینی | 9 |
| عملیات حساس هنوز ناتمام | 6 |
| قرارداد/آداپتر ناتمام | 9 |
| PUSH امن هنوز ناتمام | 3 |

### T5Web-binary

| کنترلر/کنش | شماره خط مرجع | وضعیت | مانده یا مرز |
|---|---:|---|---|
| `DeviceController.Index#1` | 24 | بخشی ساخته؛ مانده دارد | دامنهٔ امنیتی اپراتور به دستگاه‌های منتخب هنوز ساخته نشده؛ گروه metadata را scope ننامید. |
| `DeviceController.Edit#1` | 32 | پیاده‌شده؛ آزمون نرم‌افزاری | مدل/پروفایل ثبت‌شده اثبات قابلیت و اتصال نیست؛ Serial/USB direct adapter هنوز ندارد. |
| `DeviceController.Delete#1` | 115 | جایگزین ایمن؛ نه هم‌ارزی عینی | حذف گروهی metadata به همان رفتار legacy هم‌ارز نیست؛ درگاه مخرب عمومی اضافه نشود. |
| `DeviceController.DeleteAll#1` | 150 | جایگزین ایمن؛ نه هم‌ارزی عینی | حذف گروهی metadata به همان رفتار legacy هم‌ارز نیست؛ درگاه مخرب عمومی اضافه نشود. |
| `DeviceController.GetAllDevicesTree#1` | 213 | بخشی ساخته؛ مانده دارد | دامنهٔ امنیتی اپراتور به دستگاه‌های منتخب هنوز ساخته نشده؛ گروه metadata را scope ننامید. |
| `DeviceOperationsController.Index#1` | 42 | بخشی ساخته؛ مانده دارد | selected-device authorization و وضعیت اتصال زندهٔ دائمی legacy هم‌ارز نیست؛ پذیرفتن سخت‌افزار باقی است. |
| `DeviceOperationsController.DeviceStates#1` | 75 | بخشی ساخته؛ مانده دارد | selected-device authorization و وضعیت اتصال زندهٔ دائمی legacy هم‌ارز نیست؛ پذیرفتن سخت‌افزار باقی است. |
| `DeviceOperationsController.DownloadUsers#1` | 89 | بخشی ساخته؛ مانده دارد | اثر انگشت/چهره/عکس، حذف فرد/الگو و admin/group/password write فاقد policy/adapter مستقل؛ scope منتخب و مدل واقعی باز. |
| `DeviceOperationsController.UploadUsers#1` | 116 | بخشی ساخته؛ مانده دارد | اثر انگشت/چهره/عکس، حذف فرد/الگو و admin/group/password write فاقد policy/adapter مستقل؛ scope منتخب و مدل واقعی باز. |
| `DeviceOperationsController.UploadUsers#2` | 155 | بخشی ساخته؛ مانده دارد | اثر انگشت/چهره/عکس، حذف فرد/الگو و admin/group/password write فاقد policy/adapter مستقل؛ scope منتخب و مدل واقعی باز. |
| `DeviceOperationsController.ShowUsers#1` | 243 | بخشی ساخته؛ مانده دارد | اثر انگشت/چهره/عکس، حذف فرد/الگو و admin/group/password write فاقد policy/adapter مستقل؛ scope منتخب و مدل واقعی باز. |
| `DeviceOperationsController.ModifyUsers#1` | 296 | بخشی ساخته؛ مانده دارد | اثر انگشت/چهره/عکس، حذف فرد/الگو و admin/group/password write فاقد policy/adapter مستقل؛ scope منتخب و مدل واقعی باز. |
| `DeviceOperationsController.DeviceProperties#1` | 456 | بخشی ساخته؛ مانده دارد | selected-device authorization و وضعیت اتصال زندهٔ دائمی legacy هم‌ارز نیست؛ پذیرفتن سخت‌افزار باقی است. |
| `DeviceOperationsController.GetCapacity#1` | 465 | پیاده‌شده؛ آزمون نرم‌افزاری | اطلاعات snapshot غیراتمیک و سازگاری مدل واقعی باز. |
| `DeviceOperationsController.Restart#1` | 488 | پیاده‌شده؛ آزمون نرم‌افزاری | پذیرش restart مدل واقعی و قطعی/بازیابی محلی باز. |
| `DeviceOperationsController.DeleteAllData#1` | 522 | عملیات حساس هنوز ناتمام | سیاست داده/بیومتریک یا حذف، قرارداد مدل، مجوز/تأیید مستقل، شاهد دریافت و recovery هنوز تکمیل نشده؛ restart مجوز اینها نیست. |
| `DeviceOperationsController.DeleteAdmins#1` | 556 | عملیات حساس هنوز ناتمام | سیاست داده/بیومتریک یا حذف، قرارداد مدل، مجوز/تأیید مستقل، شاهد دریافت و recovery هنوز تکمیل نشده؛ restart مجوز اینها نیست. |
| `DeviceOperationsController.DeleteAllRecords#1` | 590 | عملیات حساس هنوز ناتمام | سیاست داده/بیومتریک یا حذف، قرارداد مدل، مجوز/تأیید مستقل، شاهد دریافت و recovery هنوز تکمیل نشده؛ restart مجوز اینها نیست. |
| `DeviceOperationsController.TimeSync#1` | 624 | جایگزین ایمن؛ نه هم‌ارزی عینی | هم‌ارزی خودکار TimeSync قدیمی ادعا نمی‌شود؛ آزمون drift/zone مدل واقعی باز. |
| `DeviceOperationsController.SyncTimeData#1` | 658 | جایگزین ایمن؛ نه هم‌ارزی عینی | هم‌ارزی خودکار TimeSync قدیمی ادعا نمی‌شود؛ آزمون drift/zone مدل واقعی باز. |
| `DeviceOperationsController.SetDLST#1` | 697 | قرارداد/آداپتر ناتمام | قرارداد مدل دقیق، مقدار/واحد/اثر و readback/unknown و مجوز مستقل لازم؛ generic setter باز نشده است. |
| `DeviceOperationsController.RemoveTimeRecords#1` | 738 | عملیات حساس هنوز ناتمام | سیاست داده/بیومتریک یا حذف، قرارداد مدل، مجوز/تأیید مستقل، شاهد دریافت و recovery هنوز تکمیل نشده؛ restart مجوز اینها نیست. |
| `DeviceOperationsController.GetAdvanced#1` | 779 | پیاده‌شده؛ آزمون نرم‌افزاری | unit/range سایر کدها و firmwareها تأیید نشده؛ این snapshot اجازه نوشتن نیست. |
| `DeviceOperationsController.Set1_1#1` | 802 | قرارداد/آداپتر ناتمام | قرارداد مدل دقیق، مقدار/واحد/اثر و readback/unknown و مجوز مستقل لازم؛ generic setter باز نشده است. |
| `DeviceOperationsController.Set1_N#1` | 836 | قرارداد/آداپتر ناتمام | قرارداد مدل دقیق، مقدار/واحد/اثر و readback/unknown و مجوز مستقل لازم؛ generic setter باز نشده است. |
| `DeviceOperationsController.SetRepeatTime#1` | 870 | قرارداد/آداپتر ناتمام | قرارداد مدل دقیق، مقدار/واحد/اثر و readback/unknown و مجوز مستقل لازم؛ generic setter باز نشده است. |
| `DeviceOperationsController.SetSleepTime#1` | 904 | پیاده‌شده؛ آزمون نرم‌افزاری | zero/disabled و سازگاری عمومی ادعا نمی‌شود؛ پذیرش مدل واقعی باز. |
| `DeviceOperationsController.SetCardReg#1` | 938 | قرارداد/آداپتر ناتمام | قرارداد مدل دقیق، مقدار/واحد/اثر و readback/unknown و مجوز مستقل لازم؛ generic setter باز نشده است. |
| `DeviceOperationsController.GetAdvanced2#1` | 972 | پیاده‌شده؛ آزمون نرم‌افزاری | IP گزارش‌شده اثبات دسترسی مقصد و algorithm raw تأیید قابلیت بیومتریک نیست. |
| `DeviceOperationsController.SetIP#1` | 1004 | پیاده‌شده؛ آزمون نرم‌افزاری | gateway/mask/DHCP/MAC/port و پذیرش مدل واقعی باز. |
| `DeviceOperationsController.SetAlgorithm#1` | 1038 | قرارداد/آداپتر ناتمام | قرارداد مدل دقیق، مقدار/واحد/اثر و readback/unknown و مجوز مستقل لازم؛ generic setter باز نشده است. |
| `DeviceOperationsController.SetSector#1` | 1072 | قرارداد/آداپتر ناتمام | قرارداد مدل دقیق، مقدار/واحد/اثر و readback/unknown و مجوز مستقل لازم؛ generic setter باز نشده است. |
| `DeviceOperationsController.RemoteEnroll#1` | 1106 | عملیات حساس هنوز ناتمام | سیاست داده/بیومتریک یا حذف، قرارداد مدل، مجوز/تأیید مستقل، شاهد دریافت و recovery هنوز تکمیل نشده؛ restart مجوز اینها نیست. |
| `DeviceOperationsController.ChangeKey#1` | 1140 | قرارداد/آداپتر ناتمام | قرارداد مدل دقیق، مقدار/واحد/اثر و readback/unknown و مجوز مستقل لازم؛ generic setter باز نشده است. |
| `DeviceOperationsController.ReadKey#1` | 1173 | قرارداد/آداپتر ناتمام | قرارداد مدل دقیق، مقدار/واحد/اثر و readback/unknown و مجوز مستقل لازم؛ generic setter باز نشده است. |
| `DeviceOperationsController.Connect#1` | 1196 | جایگزین ایمن؛ نه هم‌ارزی عینی | Disconnect صف به معنی لغو عملیات dispatch‌شده نیست؛ پایداری/قطعی مدل واقعی باز. |
| `DeviceOperationsController.Disconnect#1` | 1203 | جایگزین ایمن؛ نه هم‌ارزی عینی | Disconnect صف به معنی لغو عملیات dispatch‌شده نیست؛ پایداری/قطعی مدل واقعی باز. |
| `DeviceOperationsController.GetRecords#1` | 1210 | بخشی ساخته؛ مانده دارد | SSR و non-TFT صریح با قالب ثابت در دریافت/زمان‌بندی و worker آزموده‌اند؛ ReadTimeGLogData بومی ادعا نشده، فیلتر سرورمحور و پذیرش دستگاه واقعی باز است. |
| `DeviceOperationsController.GetTimeRecords#1` | 1244 | بخشی ساخته؛ مانده دارد | SSR و non-TFT صریح با قالب ثابت در دریافت/زمان‌بندی و worker آزموده‌اند؛ ReadTimeGLogData بومی ادعا نشده، فیلتر سرورمحور و پذیرش دستگاه واقعی باز است. |
| `iclockController.cdata#1` | 15 | PUSH امن هنوز ناتمام | PUSH امن مدل‌محور ساخته نشده؛ polling جای آن نیست؛ این کد بدون قرارداد هویت/replay/bounds قابل انتقال نیست. |
| `iclockController.cdata#2` | 31 | PUSH امن هنوز ناتمام | PUSH امن مدل‌محور ساخته نشده؛ polling جای آن نیست؛ این کد بدون قرارداد هویت/replay/bounds قابل انتقال نیست. |
| `iclockController.getrequest#1` | 106 | PUSH امن هنوز ناتمام | PUSH امن مدل‌محور ساخته نشده؛ polling جای آن نیست؛ این کد بدون قرارداد هویت/replay/bounds قابل انتقال نیست. |
| `USBManagementController.Index#1` | 22 | پیاده‌شده؛ آزمون نرم‌افزاری | فقط قالب شاهددار Personnel؛ نمونه فایل واقعی مدل‌های هدف و فساد فایل هنوز پذیرش واقعی می‌خواهد؛ وابستگی احتمالی لایه‌های پایین از نبود lock در این متد نفی نمی‌شود. |
| `USBManagementController.ReadRecords#1` | 29 | پیاده‌شده؛ آزمون نرم‌افزاری | فقط قالب شاهددار Personnel؛ نمونه فایل واقعی مدل‌های هدف و فساد فایل هنوز پذیرش واقعی می‌خواهد؛ وابستگی احتمالی لایه‌های پایین از نبود lock در این متد نفی نمی‌شود. |

### AriaAdmin-source

| کنترلر/کنش | شماره خط مرجع | وضعیت | مانده یا مرز |
|---|---:|---|---|
| `DeviceController.Index#1` | 15 | بخشی ساخته؛ مانده دارد | دامنهٔ امنیتی اپراتور به دستگاه‌های منتخب هنوز ساخته نشده؛ گروه metadata را scope ننامید. |
| `DeviceController.Edit#1` | 31 | پیاده‌شده؛ آزمون نرم‌افزاری | مدل/پروفایل ثبت‌شده اثبات قابلیت و اتصال نیست؛ Serial/USB direct adapter هنوز ندارد. |
| `DeviceController.Edit#2` | 46 | پیاده‌شده؛ آزمون نرم‌افزاری | مدل/پروفایل ثبت‌شده اثبات قابلیت و اتصال نیست؛ Serial/USB direct adapter هنوز ندارد. |
| `DeviceController.Command#1` | 71 | بخشی ساخته؛ مانده دارد | clear-punches و داده بیومتریک/مدل واقعی باز؛ هر دستور بستهٔ واقعی IO مسیر مستقل خود دارد. |

### AriaAdmin-catalog

| کنترلر/کنش | شماره خط مرجع | وضعیت | مانده یا مرز |
|---|---:|---|---|
| `DeviceCommandService.sync-users#1` | 38 | بخشی ساخته؛ مانده دارد | اثر انگشت/چهره/عکس، حذف فرد/الگو و admin/group/password write فاقد policy/adapter مستقل؛ scope منتخب و مدل واقعی باز. |
| `DeviceCommandService.sync-time#1` | 45 | جایگزین ایمن؛ نه هم‌ارزی عینی | هم‌ارزی خودکار TimeSync قدیمی ادعا نمی‌شود؛ آزمون drift/zone مدل واقعی باز. |
| `DeviceCommandService.pull-punches#1` | 52 | بخشی ساخته؛ مانده دارد | SSR و non-TFT صریح با قالب ثابت در دریافت/زمان‌بندی و worker آزموده‌اند؛ ReadTimeGLogData بومی ادعا نشده، فیلتر سرورمحور و پذیرش دستگاه واقعی باز است. |
| `DeviceCommandService.pull-users#1` | 59 | بخشی ساخته؛ مانده دارد | اثر انگشت/چهره/عکس، حذف فرد/الگو و admin/group/password write فاقد policy/adapter مستقل؛ scope منتخب و مدل واقعی باز. |
| `DeviceCommandService.clear-punches#1` | 66 | عملیات حساس هنوز ناتمام | سیاست داده/بیومتریک یا حذف، قرارداد مدل، مجوز/تأیید مستقل، شاهد دریافت و recovery هنوز تکمیل نشده؛ restart مجوز اینها نیست. |

## آنچه برای تکمیل واقعی هنوز باید انجام شود

این موارد با «نام تابع موجود است»، «تطبیق تمام شد» یا «CI سبز است» بسته نمی‌شوند:

1. **دامنهٔ دستگاه منتخب برای اپراتور**: مدل grant/revoke و اعمال یکپارچه روی تمام درخواست‌ها، history، health، worker و retry؛ گروه metadata فعلی جای آن نیست.
2. **قراردادهای تنظیمات باقی‌مانده**: آستانه1:1/1:N، duplicate interval، card mode، sector، الگوریتم، DLST و کلید عملکردی؛ مقدار/اثر/حریم خصوصی و read-back دقیق هر مدل، نه setter عمومی.
3. **عملیات حساس**: حذف تردد/بازه/کاربر/الگو/مدیر و reset، هرکدام مجوز/تأیید/شاهد دریافت و unknown recovery مستقل؛ RemoteEnroll و جابه‌جایی دادهٔ بیومتریک فقط با سیاست دسترسی/رضایت/نگهداری و قرارداد مدل. دستور کلی «تکمیل» سیاست بیومتریک یا اجازهٔ کنترل درب محسوب نمی‌شود.
4. **PUSH امن** برای مدل پشتیبان؛ SN ساده/Encrypt=0 قدیمی کافی نیست. مدل‌هایی که قرارداد امن ندارند باید صریح unsupported بمانند، نه endpoint ظاهراً موفق.
5. **پوشش مدل/فرمت**: تردد native non-TFT هنوز adapter جدا ندارد، حتی اگر کاربران legacy خوانده/نوشته شوند؛ Serial/USB اتصال مستقیم یا پروتکل‌های دیگر با واردکردن فایل یکی نیستند. پشتیبانی Virdi/UNIS نیز مشروط و اثبات‌نشده باقی است.
6. **پذیرش عملیاتی**: نصب SDK واقعی در Windows، حداقل دو دستگاه با firmware مشخص، رفت‌وبرگشت کاربر/کارت، دانلود تکراری، قطعی قبل/بعد dispatch، recovery، clock/idle/IP/restart و فایل واقعی از UI/worker، نه صرف heartbeat یا simulator.

### بازتولید بررسی

```bash
python ReCode/tools/device-reference-audit.py
# بازتولید اختیاری decompile؛ فقط ILSpy runner محلی از منبع رسمی، بدون اجرای legacy:
IO_ILSPY_RUNNER=/absolute/path/runner.dll IO_DOTNET=/absolute/path/dotnet \
  python ReCode/tools/device-reference-audit.py --inventory
```

validator هش دقیق Git blob منابع ثابت (و برابری worktree؛ فقط تفاوت LF/CRLF فایل view هنگام checkout ویندوز نادیده گرفته می‌شود) و پوشش یک‌به‌یک اظهارهای action و پنج catalog را بررسی می‌کند؛ تطبیق رفتار، تصمیم انسانی جدول است، نه اثبات خودکار معادل‌بودن کد. دادهٔ حساس یا بدنهٔ کامل باینری در گزارش کپی نشده است.

## آزمون و تحویل این اصلاحات

کد `aca96d7f55b1963dc3e57a3efd1e4f04e3773746` در [Windows/Linux](https://github.com/alirezarahimy-code/IO/actions/runs/43315707982) و [SQL/HTTPS](https://github.com/alirezarahimy-code/IO/actions/runs/43315707957) **success** شد؛ جزئیات و اجراهای ناموفق قبلی در [شاهد تحویل](device-reference-parity-evidence.json) ثبت شدند. suite جدید شامل۳۳ بررسی portable قرارداد/خواندن، پنج گروه SQL claim-binding، هشت regression بین صف‌ها و یک round-trip SQL برای GetAdvanced2 است. suite یکپارچه با کنترل سخت تعداد۷۸۲ PASS و نشانگر پایان موفق شد. مسیرهای DOM قبلی settings/idle نیز محلی دوباره موفق شدند.

**`part3Complete=false`، `realSdkExecuted=false` و `hardwareVerified=false`؛ این گزارش پایان تطبیق موجودیِ مشخص است، نه پایان تمام قسمت دستگاه.**

## دریافت تردد non-TFT منتقل‌شده

[انتقال GetGeneralLogData مرجع](s09-legacy-attendance.fa.md) با ReadFormat ثابت، دقت دقیقه، دو خروجی machine، namespace مستقل canonical/multiplicity، جلوگیری از ingestion خاموش قالب مختلط، lineage retry و فرم واقعی Chromium/SQL/worker ساخته و آزموده شد. فیلتر بازه همچنان سرورمحور است، نه ادعای ReadTimeGLogData بومی؛ [زمان‌بندی legacy با قالب صریح و ثابت](s10-legacy-polling.fa.md) نیز اکنون در SQL/مرورگر/worker آزموده شده است. شاهد دقیق CI در [سند این برش](s09-legacy-attendance-evidence.json) است؛ snapshot تطبیق اولیه تاریخی باقی می‌ماند.

### ادامهٔ S10 — polling با قالب ثابت

کد `468e02a` و [شاهد CI مستقل](s10-legacy-polling-evidence.json): ۸۵۱ بررسی نام‌دار SQL، ۱۴ بررسی قرارداد زمان‌بندی legacy، ۲۱ بررسی محیط واقعی worker با دستگاه مجازی، و CI ویندوز/لینوکس سبز. اعداد و جدول snapshot اولیه در بالا تاریخی‌اند؛ دسته‌بندی مانده‌ها و عدم پذیرش سخت‌افزار تغییر نکرده است.
