# S10 — بازخوانی مصوب تردد دیررس با پنجرهٔ هم‌پوشان

## مسئله و قرارداد

cursor زمان‌بندی قبلی تنها رو به جلو می‌رفت. ترددی که پس از acknowledge در زمان گذشته به دستگاه برسد، در range بعدی نبود. `OverlapSeconds` اکنون گزینهٔ صریح همان زمان‌بندی SQL است؛ نه PUSH، نوشتن دستگاه یا ادعای بازخوانی تمام گذشته.

- پیش‌فرض **۰** در API، فرم و upgrade؛ زمان‌بندی موجود خودکار دامنهٔ خواندن بزرگ‌تر نمی‌گیرد.
- مقدار صحیح بین ۰ و `WindowSeconds`، با حداکثر ۸۶۴۰۰ ثانیه. تأیید schedule شامل این مقدار است؛ پس از ایجاد immutable است. برای تغییر: pause قبلی و ساخت schedule تازه با UUID/نسخه/اختیار/تأیید تازه.
- `from = max(StartUtc, CursorUtc - OverlapSeconds)`؛ `until = min(now - lag, CursorUtc + WindowSeconds)` با ثانیهٔ UTC کامل و فقط وقتی until از cursor جلوتر است.
- اندازهٔ بخش جلوخوانی حداکثر window و بخش عقب‌خوانی حداکثر overlap است؛ range تفسیرشده در هر run حداکثر دو window است. SDK همچنان all-log را در child می‌خواند و اصل tuple پیش از range filtering محفوظ می‌ماند؛ ادعای ranged native SDK نداریم.
- cursor منطقی **عقب نمی‌رود**. با read کامل موفق و review لازمِ تمام ردیف‌های run به until جلو می‌رود؛ incomplete/timeout/قطع/revoke/paused از روی بخشی از range آن را جلو نمی‌برند.
- retry خواندنی پس از خطا از همان cursor و همان overlap شروع می‌شود. این بازخوانی نه نوشتن است، نه retry نامحدود فرمان مخرب.

## تکراری، دیررس و محدودیت هویت

tupleهای هم‌پوشان از همان ingestion و receiptهای S09 می‌گذرند. tuple برابر با همان رتبهٔ multiplicity به raw قبلی اشاره می‌کند؛ اصل payload و outcome والد overwrite نمی‌شوند. tuple تازهٔ داخل range با مجوز فعلی actor و autoCommit مصوب وارد raw می‌شود؛ ناشناس/نامعتبر/بیرون range در قرنطینه محفوظ است. دریافت به‌تنهایی ورود به هسته نیست.

این هویت **محتوا + رتبهٔ تکرار** است، نه شناسهٔ رخداد فیزیکی SDK. کاهش multiplicity، پاک‌شدن/جابجایی تاریخچهٔ دستگاه یا تغییر ساعت و data correction تضمین فیزیکی جدیدی ایجاد نمی‌کند. دیررس قدیمی‌تر از lookback همچنان تضمین نشده و به استخراج بازه‌ای مستقل مصوب نیاز دارد. بزرگ‌کردن lookback مجوز حذف یا تعمیر tupleها نیست.

## مجوز، نقش و UI

از مجوزهای مستقل موجود استفاده می‌شود: `devices.schedules.manage` و `devices.attendance.extract` برای تصویب؛ در autoCommit علاوه بر آن‌ها commit و receive لازم‌اند. `devices.schedules.read` برای history/help، `devices.schedules.execute` برای worker و grant اجرایی S09 برای dispatch باقی‌اند. admin قالب مدیریت را دارد؛ operator قالب خواندن دارد و برای manage/extract/commit باید grant صریح بگیرد. این برش حق سرویس به قالب انسانی اضافه نمی‌کند. grant سازنده در هر tick دوباره ارزیابی می‌شود.

فرم `/workspace/device-schedules` فیلد ثانیهٔ بازخوانی با پیش‌فرض۰ و توضیح محدودیت دارد. پاسخ نامعلوم با همان UUID و **همان overlap** replay می‌شود؛ تغییر آن در پاسخ نامعلوم رد می‌شود. مقدار از SQL پس از reload در history و سلامت سرویس دیده می‌شود. cancel/pause همچنان اجرای running را برنمی‌گرداند.

## SQL و ارتقا

مهاجرت جدید `20261112140000_DevicePollOverlap` ستون default۰، CHECK بازه و نسخهٔ تازهٔ trigger schedule را اضافه می‌کند؛ مهاجرت اعمال‌شدهٔ S10 ویرایش نشده است. config با overlap immutable می‌ماند؛ ActiveRun باید from دقیق clamped و until جلوتر از cursor با بودجهٔ window داشته باشد. cursor فقط با همان run کامل/موفق و receiptهای لازم جلو می‌رود. clamp پیش از subtraction در پایین‌ترین تاریخ UTC مانع underflow است.

DOWN با هر schedule دارای overlap غیرصفر **51027** می‌دهد تا معنای تاریخی خواندن از بین نرود؛ صفرها به قرارداد قدیم برمی‌گردند. وب/worker خودکار migrate نمی‌کنند. مهاجرت latest تأییدشده را طبق راهنمای نصب دستی اعمال کنید.

## ماندهٔ بخش سوم

این برش مشکل نرم‌افزاری دیررس در lookback مصوب را پوشش می‌دهد؛ vendor PUSH امن، نوشتن تنظیمات/شبکه و فرمان‌های حساس مدل‌محور، RemoteEnroll با سیاست بیومتریک، پوشش واقعی مدل‌ها و سناریوی واقعی چنددستگاه/قطعی/تبادل/تنظیمات هنوز با این ویژگی پذیرفته نمی‌شوند. `part3Complete=false` و `hardwareVerified=false` باقی‌اند.


## CI تأییدشده

کد `0f31066f2b1cd8d7ac156fad56026ed831c96267` با SQL/HTTPS/مرورگر واقعی `43255522942` و Windows/Linux `43255522608` هر دو **success**؛ **۶۶۷ integration، ۴۸ Foundation، ۲۶ domain و ۶۱ portable**. ۲۸ بررسی سرویس/۷ HTTP زمان‌بندی، شامل ۸ سرویس و ۲ HTTP جدید overlap. **۱۱۴ حق، ۵۲ صفحه/مقاله، ۳۷ migration و ۶۰ جدول**. lifecycle واقعی worker در SCM/systemd CI نیز مجدداً گذشت؛ این دادهٔ آزمایشگاه یا native SDK نیست. [شاهد ماشین‌خوان](s10-overlap-evidence.json).
