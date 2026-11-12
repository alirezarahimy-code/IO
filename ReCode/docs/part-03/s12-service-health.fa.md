# S05/S12 — سلامت صف و heartbeat سرویس؛ نه گواه اتصال دستگاه

## رفتار اجرایی

`DeviceHealth` از SQL واقعی موجودی، view پنج صف `DeviceCommandHistory`، دریافت‌های موفق S09 و زمان‌بندی S10 می‌خواند. دستگاه‌ها ۵۰تایی با ترتیب پایدار شماره/شناسه و فیلتر اختیاری UUID هستند؛ سرویس‌ها صفحه‌بندی ۵۰تایی مستقل دارند. تاریخچهٔ کامل کاربران/tuple/تنظیمات، host/port یا credential در خروجی نیست.

برای هر دستگاه: metadata نام/شماره/enabled، آخرین تلاش با هویت **queue+id** و kind/state/زمان، تعداد فعلی pending/running، شمار unknown **تاریخی نه الزاماً حل‌نشده**، آخرین زمان خواندن کامل موفق تردد **نه زمان ingestion** و آخرین config زمان‌بندی (اولویت با enabled) همراه cursor/next/failures/outcome. فیلتر یا صف خالی نه اتصال ساختگی می‌سازد، نه timestamp حدسی. «فعال در موجودی» وضعیت network نیست. queryها bounded هستند؛ اصل payload یا یک query جدا برای هر دستگاه خوانده نمی‌شود. این مجموعه ادعای snapshot اتمیکِ وضعیت شبکه یا همهٔ صف‌ها ندارد.

`ServiceHeartbeatWorker` یک hosted service جدا از چهار slot native است. با UUID تازه برای هر اجرای process و ساعت **سرور** در SQL گزارش می‌دهد. جدول `DeviceServiceHeartbeats` برای هر instance یک row جاری دارد: serviceUserId، startedAtUtc تغییرناپذیر، lastSeenAtUtc غیرکاهشی، state=running/stopped و rowversion. این جدول telemetry تک‌تک تیک‌ها نیست؛ metadata حسابرسی در PermissionTransaction ثبت می‌شود. چند instance از یک service identity جدا باقی می‌مانند؛ این heartbeat lease انحصاری اجرای دستگاه نیست و جای claim/fencing واقعی صف‌ها را نمی‌گیرد.

زمان heartbeat از `TimeProvider` فرایند worker و زمان تفسیر از `TimeProvider` میزبان API است، نه ساعت ارسالی مرورگر یا ادعای ساعت مستقل SQL Server. ساعت میزبان‌ها باید با NTP همگام باشد؛ اختلاف آن‌ها ممکن است گزارش را قدیمی/clock-anomaly نشان دهد و اثبات قطعی نیست.

هر ۳۰ ثانیه گزارش running؛ توقف graceful یک گزارش stopped با فرصت حداکثر ۵ ثانیه می‌فرستد. crash، قطع DB، انقضای/لغو دسترسی ممکن است stopped ننویسند؛ نبود آن موفقیت یا ادامهٔ کار فرض نمی‌شود. خطای گزارش heartbeat executor را مختل نمی‌کند و exception/configuration/secret در stdout چاپ نمی‌شود. اثر observable، قدیمی‌شدن آخرین timestamp موفق SQL است.

## تفسیر درست

- `recent-heartbeat`: حداکثر ۹۰ ثانیه از ساعت SQL-request server؛ **فقط گزارش تازهٔ process**، نه نصب SDK، رسیدن به device یا اجرای موفق command.
- `stale-heartbeat`: گزارش جدید نداریم؛ خاموشی worker یا دستگاه اثبات نشده است.
- `stopped`: همان instance توقف را واقعاً در SQL ثبت کرده؛ reboot بعدی UUID دیگری می‌گیرد.
- `clock-anomaly`: lastSeen از ساعت فعلی سرور جلوتر است؛ آن را fresh فرض نمی‌کنیم.
- instance وجود ندارد: «گزارش نشده»؛ تست اتصال خودکار یا offline حدسی ساخته نمی‌شود.

## حق، نقش و نصب

`devices.health.read` حق انسانی **سراسری** مستقل است؛ قالب تازهٔ operator/admin. فیلتر DeviceId دامنهٔ امنیتی نیست. این حق grant دیدن اصل تردد/کاربران/تنظیمات، مدیریت زمان‌بندی یا retry ایجاد نمی‌کند. پیوند جزئیات فقط با حق خواندن همان قابلیت نمایش داده می‌شود و مقصد دوباره سرور را بررسی می‌کند. revoke در endpoint/راهنما/کاتالوگ اعمال و UI پاک می‌شود؛ نقش‌های موجود یا حقوق لغوشده خودکار reconcile نمی‌شوند.

`devices.service.report` حق گزارش worker است؛ **هیچ قالب انسانی این حق را نمی‌گیرد**. این grant به‌تنهایی حق اجرای diagnostic، operation، download یا schedule نمی‌دهد. report برای instance متعلق به سرویس دیگر رد می‌شود. مجوز report در هر فراخوانی دوباره بررسی می‌شود؛ پس از revoke حتی stop ساختگی هم ثبت نمی‌شود.

1. migration `20261112120000_DeviceServiceHealth` یا نسخهٔ بعدیِ تأییدشده را اعمال کنید؛ وب/worker خودکار migrate نمی‌کنند.
2. در تنظیمات worker، `IO:DeviceWorker:EnableHealthReports=true` بگذارید؛ **پیش‌فرض false** است.
3. service identity موجود باید grant صریح `devices.service.report` بگیرد؛ حق‌های اجرایی قابلیت‌ها و pin/SDK/کلید مشترک طبق [راهنمای نصب](device-worker-setup.fa.md) مستقل‌اند.
4. `/workspace/device-health` یا `GET /device-health?offset=0&serviceOffset=0` را با حق health بخوانید؛ هیچ POST/دکمهٔ اجرای فرمان از این صفحه وجود ندارد.
5. برای پیگیری، همان queue/id را در history و اصل قابلیت ببینید. فقط وضعیت nativeِ قدیمی را «اتصال زنده» ننامید.

مهاجرت ۳۵ با جدول شصتم؛ trigger `51025` تغییر هویت/start، عقب‌بردن lastSeen، احیای stopped یا حذف تاریخچه را رد می‌کند. downgrade با هر heartbeat retained ممنوع است. SQL roleهای عملیاتی نباید اجازهٔ دورزدن trigger/migration داشته باشند؛ پاک‌سازی مستقیم جای سیاست retention مصوب نیست.

## مرز پایان بخش سوم

این برش جای نصب واقعی Windows/Linux، آزمون واقعی چنددستگاه/قطعی/دریافت تکراری/ارسال کاربر/تغییر تنظیمات را نمی‌گیرد. نوشتن تنظیمات پیشرفته با قرارداد مدل، RemoteEnroll با سیاست بیومتریک و vendor PUSH امن همچنان اجرایی/پذیرفته نشده‌اند. `part3Complete=false`؛ heartbeat تازه هیچ‌کدام از این مانده‌ها را خودکار قبول نمی‌کند.


## شاهد CI دقیق و موفق

کد `8faefd5928a0f03be3c8d6ce7b9090524ca54403`:

- SQL/HTTPS/مرورگر واقعی `43243793048`: **success، ۶۵۷ بررسی integration**؛ ۱۴ بررسی سرویس و ۳ HTTP سلامت، ۱۸ سرویس و ۴ HTTP snapshot/retry تنظیمات.
- Windows/Linux `43243792727`: **success**؛ ۴۸ Foundation، ۲۶ domain، ۶۱ portable و build/publish/smoke.
- **نصب/اجرا/توقف واقعی SCM و systemd** روی runnerهای CI با worker منتشرشده، حساب مجازی/کاربر اختصاصی و پاک‌سازی پس از آزمون. DB عمداً unavailable و synthetic است؛ هیچ endpoint بومی، SDK، credential واقعی، grant یا migration provision نشده است. unit لینوکس از template سخت‌سازی‌شده با نام/مسیر جدا و `Restart=no` برای آشکارماندن crash در smoke ساخته می‌شود؛ installer ویندوز همان manual-start واقعی است.
- کاتالوگ **۱۱۴ حق، ۵۲ صفحه/مقاله، ۳۶ migration و ۶۰ جدول**. migration ۳۶ guard retry تنظیمات است؛ migration ۳۵ سلامت است.
- [شاهد ماشین‌خوان و job/stepها](s12-service-health-evidence.json). `windowsServiceInstalled=false/linuxServiceInstalled=false` در progress دربارهٔ **آزمایشگاه کاربر** است؛ `serviceLifecycleCi` جداگانه نصب واقعی runnerها را ثبت می‌کند. هیچ‌کدام پذیرش دستگاه واقعی نیستند.
