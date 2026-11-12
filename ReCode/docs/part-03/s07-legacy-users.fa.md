# S07 — دریافت خواندنی کاربران legacy non-TFT با قرارداد صریح

## مبنا و دامنه

در بدنهٔ decompile فقط‌خواندنی `Personnel.ZK.ZKDevice.WriteUserIDsToList` دو شاخه مشاهده شد: `SSR_GetAllUserInfo` با شناسهٔ رشته‌ای، و `GetAllUserInfo(machine, ref int, ref name, ref password, ref privilege, ref enabled)` برای non-TFT. در metadata رابط `zkemkeeper.IZKEM`، `GetAllUserInfo` دارای DispId47 است. این‌ها مشاهدهٔ قرارداد از باینری‌اند، نه سورس اصلی و نه اثبات پشتیبانی مدل. SHA256های مرجع در `zk-reference-contract.json` محفوظ‌اند.

این برش مسیر دوم **فقط برای خواندن** را به bridge مستقل Windows اضافه می‌کند. از `DisableDevice`، تغییر گروه دسترسی، کپی password، مجوز admin، فعال‌سازی/لایسنس یا انتقال بیومتریک مرجع استفاده نمی‌کند. همهٔ داده‌های تست synthetic هستند؛ SDK/مدل واقعی هنوز پذیرفته نشده‌اند.

## قرارداد هویت و ایمنی

| مقدار `readFormat` | مسیر SDK | هویت |
|---|---|---|
| `ssr-v1`، پیش‌فرض قدیم و جدید | `SSR_GetAllUserInfo` | رشتهٔ دقیق، از جمله صفرهای ابتدایی؛ هیچ تبدیل عددی انجام نمی‌شود |
| `legacy-int32-v1`، فقط با انتخاب صریح | `GetAllUserInfo` | Int32 غیرمنفی SDK به رشتهٔ دهدهی invariant بدون padding؛ صفر ابتدایی از این API قابل بازیابی نیست |

- fallback، `auto` یا تشخیص از نام مدل وجود ندارد. شکست legacy به SSR تبدیل نمی‌شود؛ نتیجهٔ ناقص/failed جای snapshot موفق نمی‌نشیند.
- کارت همچنان `GetStrCardNumber` رشته‌ای است؛ صفرهای اول آن حفظ می‌شود. شکست خواندن کارت، null «گزارش نشده» است، نه صفر.
- `LegacyIdentity` مقدار منفی را رد می‌کند؛ مرز parser و append سرویس نیز فقط متن canonical عدد غیرمنفی Int32 می‌پذیرند. `01`، `+1`، مقدار خارج Int32 و padding، provenance legacy معتبر نیستند.
- عدد `1` از legacy با شناسهٔ SSR مانند `001` یا شمارهٔ پرسنلی صفر‌دار برابر فرض نمی‌شود. نگاشت همچنان دقیق، شرکت‌محور، دارای بازهٔ اعتبار و تأیید مستقل است؛ دانلود هیچ employee یا mapping را تغییر نمی‌دهد.
- شمارنده قبل/بعد و تعداد ردیف، deadline و serial pin حفظ شده‌اند؛ serial پس از enumeration هم کنترل می‌شود. شمارندهٔ ثابت snapshot اتمیک را ثابت نمی‌کند.
- `ZkUserReadExecutor` مشترک، رفتار counter/serial/format/EOF را قابل آزمون می‌کند؛ session بومی فقط فراخوانی COM انتخاب‌شده را اجرا می‌کند و password را در همان متد کنار می‌گذارد. password در frame، SQL، UI و log وجود ندارد.

## SQL، API و UI واقعی

`ReadFormat` در `DeviceUserDownloads` از زمان درخواست immutable است؛ در replay با UUID قبلی باید عیناً یکسان باشد. claim، history و preview همین مقدار را حمل می‌کنند. مهاجرت جدید `20261112150000_DeviceLegacyUserReads` ستون default `ssr-v1`، CHECK دقیق با collation/طول و نسخهٔ تازهٔ trigger قبلی را اضافه می‌کند؛ مهاجرت اعمال‌شدهٔ قدیمی تغییر نکرده است. DOWN با هر سابقهٔ legacy حتی cancelled، خطای **51028** می‌دهد تا معنای هویت از history حذف نشود.

فرم `/workspace/device-users` انتخاب قرارداد دارد؛ پیش‌فرض SSR است. پاسخ نامعلوم با **همان UUID، format و payload** تکرار می‌شود؛ تغییر format در این حالت رد می‌شود. فیلد select در busy یا revoke غیرفعال/پاک می‌شود. مقدار پس از reload از SQL در history/preview دیده می‌شود.

POST `/device-user-downloads` همان ورودی قبلی را به‌اضافهٔ `readFormat` اختیاری می‌پذیرد. حذف فیلد همچنان SSR است؛ null/رشتهٔ ناشناخته ۴۰۰، replay با قرارداد متفاوت ۴۰۹ است. snapshotها و ردیف‌های ناقص immutable می‌مانند؛ دانلود دوباره درخواست جدید است، نه ادامهٔ snapshot فرضی.

## حقوق و نقش‌ها

حق تازه‌ای اضافه نشده است: `devices.users.download` برای درخواست/لغو (قالب admin؛ operator فقط با grant صریح)، `devices.users.read` برای history/preview/help (قالب operator/admin) و `devices.users.execute` برای سرویس مستقل، بدون قالب انسانی. اعطا/لغو واقعی سرور و بررسی مجدد اختیار سازنده پابرجاست. `attendance.mappings.manage` مستقل است؛ دریافت legacy آن را اعطا نمی‌کند.

## نصب و قابلیت‌های ارسال

مهاجرت را با ابزار bootstrap و روی نسخهٔ پشتیبان‌گیری‌شده اعمال کنید؛ وب/worker خودکار migrate نمی‌کنند. همهٔ اجزای وب/worker/bridge باید به نسخهٔ سازگار ارتقا یابند. برای native endpoint **موجود و pin‌شده** فقط پس از بررسی قرارداد مدل، این گزینه‌ها در تنظیم نصب قابل انتخاب‌اند:

```json
{
  "AllowUserRead": true,
  "UserReadFormat": "legacy-int32-v1",
  "AttendanceOnlyUserWriteApproved": false
}
```

این fragment جای DeviceId، IP/port، serial، machine number، حفاظت key و تنظیمات موجود نیست. `EnableUserDownloads` و grant سرویس هم لازم‌اند. UserReadFormat حذف‌شده در worker، `ssr-v1` است؛ برای dispatch باید با ReadFormat immutable درخواست دقیقاً برابر باشد، وگرنه unsupported؛ هیچ credential پیش از این gate خوانده نمی‌شود.

start frame جدید format را حمل می‌کند؛ supervisor درخواست legacy را از child قدیمیِ بدون format نمی‌پذیرد. برای SSR، start قدیمی بدون format همچنان پذیرفته می‌شود تا قرارداد سابق حفظ شود.

**نوشتن non-TFT در این برش ساخته نشده است.** مسیر ordinary user write فعلی فقط SSR است: preview و unknown review ارسال، snapshot هدف legacy را رد می‌کنند و worker روی endpoint با UserReadFormat غیرSSR نوشتن انجام نمی‌دهد؛ حتی اگر flag ارسال اشتباه true شده باشد. دریافت legacy مجوز ساخت یا تکرار نوشتن نیست.

## اجرای تست قابل تکرار

از `ReCode`:

```text
dotnet build tests/IO.DeviceRuntime.Tests --configuration Release
dotnet run --project tests/IO.DeviceRuntime.Tests --configuration Release --no-build -- --user-read-contracts
```

۱۰ بررسی جدید قرارداد، علاوه بر ۶۱ portable قبلی و ۱۹ آزمایشگاه TCP موجود، جدا گزارش می‌شوند. شمار integration شامل این ۱۰ بررسی نیست. تست pure session فراخوانی واقعی COM نیست؛ اجرای Windows bridge روی مدل واقعی و اثبات non-TFT بودن/خواندن card هنوز لازم است. این ویژگی پایان S07 یا بخش سوم روی سخت‌افزار را اعلام نمی‌کند.


## CI تأییدشده

کد `50087b92919dd0ce18ae258893c91bf41bcc0cd1`: [SQL/HTTPS/مرورگر 43295571681](https://github.com/alirezarahimy-code/IO/actions/runs/43295571681) و [Windows/Linux 43295571689](https://github.com/alirezarahimy-code/IO/actions/runs/43295571689) هر دو **success**. **۶۷۷ integration** شامل ۸ بررسی SQL و۲ HTTP جدید؛ ۱۰ بررسی جدید قرارداد portable جدا از۶۱ قبلی و۱۹ آزمایشگاه TCP گزارش می‌شود. ۴۸ Foundation،۲۶ domain،۱۱۴ حق،۵۲ صفحه/راهنما،۳۸ migration و۶۰ جدول. نصب/توقف واقعی worker در SCM/systemd CI هم سبز ماند؛ SDK یا مدل واقعی اجرا نشده است. [شاهد ماشین‌خوان](s07-legacy-users-evidence.json).


## ادامهٔ بعدی S08

محدودیت تاریخی «نوشتن non-TFT ساخته نشده» در این برش خواندن، با [candidate مستقل ارسال عادی legacy](s08-legacy-user-write.fa.md) تکمیل‌تر شد. snapshot legacy همچنان مجوز اجرای SSR نیست؛ فرمت نوشتن جدا، pin نصب، تأیید preview و same-format recovery لازم‌اند. مدل واقعی هنوز تأیید نشده است.
