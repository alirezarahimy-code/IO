# S11 — تاریخچهٔ فرمان‌های واقعی و retry صریح خواندنی

## ساخته‌شده؛ نه پایان همهٔ S11

`DeviceCommands` و SQL view واقعی `dbo.DeviceCommandHistory` روی پنج صف موجود `DeviceConnections/DeviceOperations/DeviceUserDownloads/DeviceUserUploads/DeviceAttendanceDownloads` تاریخچهٔ یکپارچه می‌دهند؛ فرمان و نتیجهٔ تازهٔ جعلی/شبیه‌ساز ساخته نمی‌شود. metadata صف با حق مستقل قابل خواندن است؛ اصل کاربران، کارت، tuple/فایل و نتیجهٔ حساس هر قابلیت حق خودش را می‌خواهد. history تنها، حق نوشتن دستگاه نیست.

`/workspace/device-commands` دارای فیلتر DeviceId، صفحه‌بندی ۵۰تایی، lineage و لینک جزئیات همان قابلیت است. فیلتر **محدودهٔ امنیتی نیست**؛ مجوز سراسری است. لغو به صفحهٔ همان قابلیت می‌رود؛ فقط pending پیش از dispatch تضمین می‌شود. نتیجهٔ running یا فرمان فیزیکی ارسال‌شده پس گرفته نمی‌شود.

## retry امن

- `devices.commands.read`: قالب اپراتور/admin، فقط history واقعی و lineage.
- `devices.commands.retry`: قالب admin؛ اپراتور فقط با grant صریح. علاوه بر history و retry، **حق مستقل همان خواندن** لازم است؛ execute سرویس و اجرای native از این حقوق به دست نمی‌آید.
- فقط `information.read` در صف operation و استخراج S09 در صف attendance-download، فقط پس از نتیجهٔ terminal خواندنی `failed/interrupted/timed-out/bridge-failed` و به‌ترتیب `unknown/incomplete` قابل retry هستند. pending/running/succeeded و queue دلخواه قابل retry نیستند.
- confirmation برابر `RETRY-READ-ONLY-COMMAND`، UUID تلاش تازه، نسخهٔ والد و snapshot نسخهٔ دستگاه لازم است. درخواست تازه دقیقاً deadline، range، source/site/company/zone و کدهای مصوب والد را حفظ می‌کند؛ تغییر مقصد/zone/نسخه نیازمند درخواست مستقل مصوب است، نه retarget retry.
- هر والد فقط یک فرزند مستقیم دارد؛ فرزندِ دوباره شکست‌خورده می‌تواند با تصمیم تازه والد تلاش بعدی شود. `DeviceCommandRetries` با FKهای واقعی، نسخهٔ قبلی، هویت تصمیم‌گیر و زمان درخواست، همزمان با enqueue در یک PermissionTransaction ثبت می‌شود.
- replay UUID/payload یکسان، همان child را برمی‌گرداند؛ payload/actor تغییرکرده تعارض است. revoke حتی replay موفق قبلی را در همان نشست رد می‌کند. history والد/نتیجه هرگز برای retry بازنویسی نمی‌شود؛ retry، تلاش جدید است نه ادعای موفقیت native قبلی.
- trigger 51023 lineage را immutable می‌کند و پیوند ساختگی به نوشتن یا snapshot/range متفاوت را رد می‌کند. downgrade بعد از اولین retry ممنوع است. عملیات اصلی همان lease، خدمت مستقل، صف و fencing اثبات‌شدهٔ خودش را دارد.
- این مسیر **ingestion تردد را اجرا نمی‌کند**؛ دانلود تازه باید در S09 یا زمان‌بندی مصوب خودش بازبینی شود. retry خواندن خودکار و نامحدودِ فرمان حساس نیست.

## نوشتن‌های نامعلوم و مرز عملیات حساس

`clock.set` یا ارسال کاربر حتی با حق کامل history/retry از این صفحه قابل تکرار نیستند. نتیجهٔ unknown باید با snapshot تازهٔ همان دستگاه و مسیر review صریح S06/S08 بررسی شود؛ retry انتخابی ارسال کاربر، preview/نسخه/hash و حقوق خودش را دارد. پاکسازی/reset/restart/درب یا arbitrary native passthrough ساخته نشده است.

در [شاهد hashدار مرجع](zk-reference-contract.json)، بدنهٔ فقط‌خواندنی `Personnel.ZK.ZKDevice` این قراردادها دیده شدند: `GetDeviceAdvancedOptions` از `GetDeviceInfo` با کدهای 25/23/8/4/30، تنظیمات متناظر `SetDeviceInfo`، و `RemoteEnroll` با `StartEnrollEx(badgeNumber.ToString(), fingerIndex, 1)`. این‌ها **بدنهٔ decompile باینری** هستند، نه سورس اصلی یا اثبات سازگاری مدل. دامنهٔ مقدار/واحد/تأثیر تنظیمات و تأیید مدل واقعی هنوز موجود نیست. RemoteEnroll داده/عملیات بیومتریک است؛ بدون سیاست مصوب دسترسی، رضایت و نگهداری فعال نمی‌شود. بنابراین **adapter تنظیمات پیشرفته و RemoteEnroll هنوز پیاده‌سازی اجرایی نشده‌اند**؛ وجود تاریخچهٔ جدید پایان S11 نیست. payroll و کنترل درب همچنان خارج دامنه‌اند.

## پذیرش

[شاهد جزء نرم‌افزاری](s11-component-evidence.json) باید CI موفق روی کد دقیق را نشان دهد؛ تعداد تست، تاریخچهٔ SQL، HTTP grant/revoke، مرورگر و DOM جای تست SDK/مدل واقعی را نمی‌گیرد. مدل/firmware و پذیرش واقعی چنددستگاه/قطعی/تبادل/تنظیمات S12 همچنان باز است.
