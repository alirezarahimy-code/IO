# آزمایشگاه مجازی دو دستگاه — تست نرم‌افزار، نه پذیرش SDK یا مدل

## تصمیم و مرز اعتماد

طبق تصمیم ادامه با دستگاه مجازی و سپس پذیرش واقعی، یک آزمایشگاه stateful و شبکه‌ای در پروژهٔ **تست** ساخته شد. دو endpoint مستقل با سریال و port متفاوت روی loopback بالا می‌آیند؛ وضعیت تردد، کاربر و ساعت در طول تست، مستقل از عمر child process نگهداری می‌شود. پایان تست تمام listenerها و taskها را می‌بندد. داده‌ها synthetic و فقط در حافظه‌اند؛ secret یا فایل مرجع واقعی استفاده نمی‌شود.

**این آزمایشگاه JSON خصوصی است، نه پروتکل شبکهٔ ZK، نه emulator سازگار با `zkemkeeper.dll` و نه DLL جایگزین.** همان `NativeProcessSupervisor`، `NativeAttendanceReadSupervisor`، `NativeUserReadSupervisor` و executor نوشتن عادیِ محصول با child واقعی اجرا می‌شوند؛ یک relay مخصوص تست به TCP محلی وصل است. `IO.ZkBridge` و `IO.DeviceWorker` هیچ گزینهٔ انتخاب این relay ندارند. هیچ bypass مجوز، سریال یا safety flag به مسیر نصب اضافه نشده است.

این مجموعه از DB/HTTP/UI عبور نمی‌کند و آزمون صف، grant/revoke، ingestion، dedup و مرورگر نیست. آن‌ها در مجموعهٔ SQL/HTTPS/مرورگر موجود جدا آزموده می‌شوند. سناریوی دریافت تکراری این آزمایشگاه **ثبات tuple و multiplicity** را می‌سنجد، نه ثبت دوباره‌نشدن در DB. بازخوانی دیررس/receipt در SQL در `DevicePollScheduleTests.cs` آزموده می‌شود. موفقیت مجازی این دو لایه را به اتصال end-to-end دستگاه واقعی تبدیل نمی‌کند.

## اجرای تکرارپذیر

از پوشهٔ `ReCode`، روی Linux یا Windows با .NET 10:

```text
dotnet build tests/IO.DeviceRuntime.Tests --configuration Release
dotnet run --project tests/IO.DeviceRuntime.Tests --configuration Release --no-build -- --verify
dotnet run --project tests/IO.DeviceRuntime.Tests --configuration Release --no-build -- --virtual-lab
```

پروژهٔ runtime تست وابستگی NuGet خارجی ندارد. `--verify` همان ۶۱ بررسی مرزی قبلی است؛ `--virtual-lab` جداگانه ۱۹ بررسی شبکه‌ای stateful اجرا می‌کند و با اولین شکست exit غیرصفر می‌دهد. در CI، هر دو سیستم‌عامل از `verify-platform.ps1` و Linux SQL workflow این آزمایشگاه را اجرا می‌کنند. فراخوانی supervisors در این تست timeout را به چند ثانیه محدود می‌کند؛ تنظیمات timeout محصول تغییر نکرده است.

## ۱۹ بررسی قابل ردیابی

تمام assertionها در `ReCode/tests/IO.DeviceRuntime.Tests/VirtualDeviceLab.cs` هستند:

1. شناسایی دو endpoint مستقل از child processهای واقعی.
2. رد سریال و کلید synthetic اشتباه؛ این مورد ادعای امنیت پروتکل سازنده نیست.
3. تاریخچهٔ ۱۰۳ tuple شامل tuple برابر با multiplicity دو، ثابت در استخراج‌های مستقل.
4. افزودن دیررس به دستگاه اول بدون تغییر tupleهای قبلی یا دادهٔ دستگاه دوم.
5. بستن TCP پس از ۵۰ ردیف؛ حفظ batch دریافت‌شده با نتیجهٔ غیرکامل.
6. بازخوانی موفق همان تاریخچه پس از رفع خطا با child جدید.
7. رد پایان stream با count نادرست.
8. رد stream با سریال اشتباه پیش از callback اولین tuple.
9. timeout دستگاه معلق هم‌زمان با تکمیل دستگاه سالم دیگر.
10. cancellation خواندن معلق با نتیجهٔ interrupted، نه موفق.
11. مشخصات synthetic، شمار رکورد و ظرفیت unavailable بدون جعل مدل واقعی.
12. تنظیمات خام صفر، null و Int32 حدی، بدون حدس واحد/دامنه.
13. تغییر ساعت synthetic با opt-in و read-back؛ ساعت دستگاه دوم مستقل می‌ماند.
14. رد clock بدون opt-in، settings بدون opt-in و settings.write پشتیبانی‌نشده.
15. ساخت کاربر عادی synthetic و read-back مستقل از هر دو دستگاه.
16. **اعمال نوشتن سپس قطع اتصال پیش از پاسخ**: نتیجه unknown؛ read مستقل بعدی تغییر را می‌بیند و دستگاه دوم دست‌نخورده است.
17. replay snapshot قدیمی بعد از unknown، conflict می‌دهد و تعداد mutation بالا نمی‌رود.
18. snapshot تازهٔ برابر desired، unchanged می‌دهد و mutation اضافه ندارد.
19. ارتقای privilege از مسیر کاربر عادی unsupported است؛ تست مجازی هم محدودیت محصول را دور نمی‌زند.

در سناریوی timeout، سرور fault تا teardown اتصال را معلق نگه می‌دارد و teardown آن را لغو می‌کند؛ kill شدن child نتیجهٔ آزمون supervisor است، نه reboot دستگاه. حافظهٔ دستگاه بین childها حفظ می‌شود، **نه پس از restart فرآیند آزمایشگاه**. fake SDK executor همچنان ممکن است برداشت مشترک اشتباه با قرارداد داشته باشد؛ شاهد مستقل vendor/hardware لازم است.

## ایراد واقعی کشف‌شده و regression

اجرای اولیهٔ Windows سناریوی سریال stream اشتباه را رد کرد: parser خطا می‌داد، اما `Task.WhenAll` هنوز منتظر taskهای خروجی/خروج child می‌ماند؛ child می‌توانست روی pipe دیگر خوانده‌نشده block شود و نتیجه به‌جای خطای فوری، timeout شود. این مشکل فقط تست نبود: همان الگوی انتظار در supervisorهای نصب‌شده وجود داشت.

`NativeInteraction.RunAsync` اکنون با اولین fault/cancel، انتظار مشترک را متوقف می‌کند تا `finally` مالک process آن را kill کند؛ خطاهای aggregate بعدی نیز observed می‌شوند. در `NativeProcessSupervisor`، `NativeAttendanceReadSupervisor` و `NativeUserReadSupervisor` اعمال شده است. نوشتن پس از dispatch همچنان unknown می‌ماند و retry خودکار ندارد.

برای regression، دستگاه تست بعد از header با سریال غلط ۸۱۹۲ frame اضافه می‌فرستد تا buffer سیستم‌عامل قطعاً مسئله را پنهان نکند. اجرای کنترل با الگوی قدیمی `Task.WhenAll` در Linux نیز همان assertion را واقعاً شکست داد؛ پس از restore اصلاح، ۱۹ بررسی جدید و ۶۱ بررسی قبلی موفق‌اند. انتظار تست از bridge-failed به timeout تغییر نکرده است.

## نقش و امنیت

این آزمایشگاه ابزار توسعه/CI است، صفحه یا قابلیت کاربری نصب‌شده نیست؛ نقش و مجوز انسانی جدیدی ندارد. نبود مجوز در تست واحد به معنی مجازشدن عملیات واقعی نیست. در اجرای واقعی همان حقوق مدیریت/استخراج/ثبت/ارسال و execute سرویس، CSRF، تأیید مستقل، endpoint approval و serial pin لازم‌اند. JSON این ابزار به درگاه عمومی یا browser route وصل نمی‌شود؛ listener فقط loopback و port تصادفی است، relay هم non-loopback را رد می‌کند.

## مرحلهٔ بعد: آزمایش واقعی توسط تیم

آزمایش واقعی باید از **وب + SQL + worker + bridge + SDK مجاز** و دو دستگاه واقعاً در دسترس انجام شود، نه از relay تست:

- مدل/firmware، نسخه و معماری SDK، شناسهٔ دستگاه و serial pin واقعی در پروندهٔ پذیرش ثبت شوند؛ credential در خروجی و گفتگو ثبت نشود.
- مشخصات و ساعت/zone قبل و بعد از آزمون ثبت شود؛ settingهای فاقد قرارداد/دامنه و قابلیت‌های unsupported را فعال نکنید.
- خواندن هر دو دستگاه، قطع شبکهٔ یکی و ادامهٔ دیگری، اتصال مجدد و تکمیل دانلود از صف واقعی آزموده شود.
- دانلود تکراری و دیررس داخل overlap با raw ID/receipt SQL بررسی شود؛ tuple خارج lookback تضمین‌نشده است.
- کاربر synthetic عادیِ بدون بیومتریک با preview و تأیید از دستگاه اول به دوم منتقل و read-back شود؛ snapshot تغییرکرده و partial success بررسی شوند.
- timeout/گم‌شدن پاسخ نوشتن در محیط کنترل‌شده باید unknown بماند؛ پیش از retry snapshot تازه بگیرید. برای تولید fault مخرب یا قطع برق به دستگاه عملیاتی آسیب نزنید.
- restart worker/بازیابی صف و grant/revoke انسانی و سرویس از UI واقعی آزموده شود؛ نتیجهٔ هر مرحله با شناسهٔ operation/run/download و evidence بدون secret ثبت شود.

عبور این مجموعه یا اجرای تست واقعیِ محدود تضمین «هیچ خطایی باقی نیست» نمی‌دهد. `realSdkExecuted=false`، `hardwareVerified=false` و `part3Complete=false` تا ورود شواهد واقعی معتبر باقی می‌مانند. قابلیت‌های هنوز ساخته‌نشده با تست مجازی موجود ساخته‌شده تلقی نمی‌شوند؛ [ماندهٔ دقیق](remaining-work.fa.md) پابرجاست.


## نتیجهٔ تأییدشدهٔ CI

کد `b1df7dd3b590dc038ee576b6044c682c8b8414c4`:

- [Windows/Linux 43290520920](https://github.com/alirezarahimy-code/IO/actions/runs/43290520920): هر دو job **success**؛ ۱۹ بررسی آزمایشگاه مجازی، ۶۱ بررسی مرزی قبلی، ۴۸ Foundation و ۲۶ domain؛ lifecycle واقعی worker در محیط CI هم موفق.
- [SQL/HTTPS/مرورگر 43290520893](https://github.com/alirezarahimy-code/IO/actions/runs/43290520893): **success**؛ اجرای آزمایشگاه مجازی هم در یک step مستقل و مجموعهٔ ۶۶۷ integration موجود موفق. عدد ۶۶۷ شامل ۱۹ بررسی جدید مجازی نیست.
- ۱۱۴ حق، ۵۲ صفحه/راهنما، ۳۷ migration و ۶۰ جدول؛ این برش migration/حق/صفحهٔ نصب‌شدهٔ جدید ندارد.

[شاهد ماشین‌خوان و مشخصات regression](virtual-device-lab-evidence.json).
