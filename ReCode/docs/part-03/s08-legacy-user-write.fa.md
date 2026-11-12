# S08 — ارسال عادی کاربران non-TFT با قرارداد صریح، تعارض و unknown recovery

## قرارداد مرجع و محدودهٔ مستقل

در بدنهٔ decompile فقط‌خواندنی `Personnel.ZK.ZKDevice.UploadUsers` شاخهٔ non-TFT با `SetStrCardNumber` و `SetUserInfo(machine, Int32 badge, name, password, privilege, enabled)` مشاهده شد. `zkemkeeper.IZKEM.SetUserInfo` در metadata دارای DispId41 است؛ این metadata بدنهٔ متد یا اثبات سازگاری مدل نیست. SHA256 باینری‌ها در `zk-reference-contract.json` ثبت شده است.

این برش **فقط ordinary metadata** را با طراحی مستقل پیاده می‌کند. `AccGroup`، تبدیل طولانی به Int32 با overflow، بریدن نام، تغییر admin، حذف/نوشتن template، عکس، disable دستگاه یا business/licensing مرجع کپی نشده‌اند. نام حداکثر۲۰ نویسه باید بدون بریدن معتبر باشد؛ privilege فقط۰ و نگاشت فعلی معتبر لازم است. دادهٔ اثر/چهره/عکس و کنترل فیزیکی درب همچنان خارج از این مسیرند.

## تأیید و منشأ نوع نوشتن

- `writeFormat` ورودی دلخواه client نیست: از `ReadFormat` snapshot موفق **همان هدف و نسخه** گرفته می‌شود و در preview/history/claim نشان داده می‌شود.
- `SnapshotId` intent و `ReadFormat` والد هر دو در SQL تغییرناپذیرند. hash پیش‌نمایش موجود شامل SnapshotId است؛ بنابراین همان شناسهٔ والد، قرارداد را هم ثابت می‌کند. ستون mutable یا تکراری format اضافه نشده و migration جدیدی لازم نیست؛ مهاجرت موجود `20261112150000_DeviceLegacyUserReads` باید اعمال شده باشد.
- هر هدف در یک batch می‌تواند SSR یا legacy باشد؛ انتخاب و نتیجهٔ هر نفر/هدف مستقل است. ثبت صف بدون hash دقیق preview و `UPLOAD-ORDINARY-USERS` همچنان ممنوع است. در پاسخ نامعلوم ثبت، UUIDهای batch/آیتم، snapshot و payload عیناً تکرار می‌شوند.
- snapshot عددی legacy مجوز اجرای SSR نیست؛ `legacy-int32-v1` همان GetAllUserInfo/SetUserInfo عددی را می‌خواهد. `ssr-v1` از SSR_GetAllUserInfo/SSR_SetUserInfo استفاده می‌کند. fallback یا تشخیص از نام مدل وجود ندارد.
- legacy فقط رشتهٔ canonical عدد Int32 غیرمنفی می‌پذیرد؛ `001`، `+1`، منفی و overflow به `1` تبدیل نمی‌شوند. اگر هویت واقعی این قالب را ندارد، این adapter را انتخاب نکنید. کارت همچنان رشتهٔ دقیق و صفر‌دار است؛ تبدیل عددی نمی‌شود. کارت خالی حذف کارت مصوب کاربر است.

## gate نصب، مجوز و نقش

همان حقوق مستقل موجود لازم‌اند: `devices.users.upload` برای preview/ثبت/لغو/review (قالب admin؛ operator فقط با grant صریح)، `devices.users.read` برای history (قالب operator/admin) و `devices.users.upload.execute` فقط هویت مستقل سرویس، نه قالب انسانی. خواندن snapshot و مدیریت نگاشت حقوق جدا دارند. current creator grant، نسخهٔ هدف، نگاشت و نام فعلی کارمند در claim بازبینی می‌شوند؛ CSRF و grant/revoke واقعی مسیر HTTP پابرجاست.

پیش‌فرض worker هنوز `EnableUserUploads=false` و `AttendanceOnlyUserWriteApproved=false` است. روی endpoint **موجودِ سریال‌پین‌شده** و فقط پس از پذیرش مدل attendance-only در آزمایشگاه، قرارداد ارسال جدا انتخاب می‌شود:

```json
{
  "AllowUserRead": true,
  "UserReadFormat": "legacy-int32-v1",
  "UserWriteFormat": "legacy-int32-v1",
  "AttendanceOnlyUserWriteApproved": true
}
```

این fragment مجوز روشن‌کردن خودکار در تولید نیست و جای DeviceId/IP/port/serial/machine/key/ACL موجود را نمی‌گیرد. `UserWriteFormat` حذف‌شده برابر **ssr-v1** است؛ بنابراین تأیید قبلی فقطِ خواندن legacy خودکار مجوز نوشتن legacy نمی‌شود. هر دو `UserReadFormat` و `UserWriteFormat` باید با `claim.WriteFormat` عیناً یکسان باشند؛ اختلاف قبل از خواندن credential، unsupported است. نوع ناشناخته در startup رد می‌شود. کلید تنها برای claim متعلق و همان قرارداد قابل دریافت است؛ clone claim با format متفاوت key نمی‌گیرد.

## خواندن هدف، حفظ password و نتیجه

1. bridge Windows با serial pin وصل می‌شود. read-exact کاربران با count قبل/بعد و serial قبل/بعد، از API **همان قرارداد** انجام می‌شود. enumeration ناقص، duplicate هویت هدف، کارت غیرقابل‌خواندن یا تغییر serial در این مرحله، مجوز فرض غیبت و ساخت کاربر نمی‌دهد.
2. وضعیت هدف باید دقیقاً برابر expected snapshot مصوب باشد؛ تفاوت، conflict بدون نوشتن است. اگر desired از قبل برابر هدف باشد، unchanged و بدون mutation است.
3. password موجودِ همان هدف فقط در حافظهٔ child حفظ می‌شود؛ مقدار null به معنی خالی فرض نمی‌شود و قبل از نوشتن رد می‌شود؛ هیچ password از API/SQL یا دستگاه مبدأ به مقصد منتقل نمی‌شود. ساخت فرد غایب صریحاً بدون password است.
4. SetStrCardNumber سپس SetUserInfo یا SSR_SetUserInfo طبق نوع مصوب اجرا می‌شود؛ privilege۰، نام بدون truncation، enabled صریح. پس از آن read-exact مجدد، metadata و password حافظه‌ای را تطبیق می‌دهد.
5. false SDK، exception بعد از شروع mutation، تغییر read-back، timeout، قطع pipe یا پاسخ گم‌شده همگی **unknown** هستند، نه failed-before-write و نه اجازهٔ retry خودکار. حتی acknowledgement موفق SDK اثبات مدل/اثر واقعی نیست؛ پذیرش آزمایشگاهی لازم است.

اگر SDK مدل password را masked یا به‌صورت خالیِ غیرقابل‌تشخیص از password موجود برمی‌گرداند، این adapter برای آن مدل نباید تأیید یا فعال شود؛ صرف پاسخ موفق GetAllUserInfo اثبات دسترسی به password واقعی نیست. این موضوع باید در آزمایشگاه با حساب synthetic و password معلوم بررسی شود.

شمارندهٔ ثابت snapshot اتمیک یا قفل روی مدیر خارجی ایجاد نمی‌کند؛ هیچ ادعای حذف race روی سخت‌افزار نداریم. برای پذیرش و استقرار باید مدیریت موازی خارجی منع و رفتار مدل بررسی شود.

## IPC و ارتقا

پیام legacy با discriminator مستقل **WriteLegacyUser** و بدون کلید **WriteUser** ارسال می‌شود؛ بنابراین bridge قدیمی نمی‌تواند آن را درخواست قدیمی SSR تفسیر و SSR mutation اجرا کند. bridge جدید هم legacy را از شاخهٔ WriteUser قبول نمی‌کند. مسیر SSR پیش‌فرض سازگاری خود را حفظ می‌کند.

ارتقای هماهنگ worker/bridge ضروری است. bridge قدیمی ممکن است در fallback عمومی پاسخ incompatible بدهد؛ supervisor این را حداکثر unknown تلقی می‌کند، نه مجوز تکرار. خروجی و ورودی همچنان bounded و private pipe هستند؛ secret آرگومان shell نیست و به environment فرزند پیکربندی DB داده نمی‌شود. شبیه‌ساز تست در worker تولیدی قابل انتخاب نیست.

## نتیجهٔ نامعلوم و partial retry

- موفقیت هدف دیگر، unknown را تغییر نمی‌دهد؛ نتیجه‌ها مستقل می‌مانند.
- unknown برای همان فرد تا review صریح، enqueue تازه را مسدود می‌کند. snapshot موفق تازه باید **پس از پایان نوشتن و از همان قرارداد اولیه** باشد. snapshot جدید SSR جای شاهد legacy را نمی‌گیرد، حتی اگر badge ظاهراً یکسان باشد.
- review نتیجهٔ قدیمی را succeeded نمی‌کند؛ فقط شاهد تازه و actor/time ثبت می‌شوند. retry انتخابی، intent/UUID/preview/hash تازه و expected جدید می‌خواهد. افراد موفق را دوباره ارسال نکنید.
- لغو فقط pending تضمین دارد؛ generic read retry همچنان نوشتن کاربران را اجرا نمی‌کند.

## تست قابل تکرار

از `ReCode` با .NET10:

```text
dotnet build tests/IO.DeviceRuntime.Tests --configuration Release
dotnet run --project tests/IO.DeviceRuntime.Tests --configuration Release --no-build -- --legacy-user-writes
```

۱۲ بررسی قرارداد/child و۴ بررسی TCP stateful جدید اجرا می‌شوند: مسیر متمایز IPC، bounds و هویت، حفظ password حافظه‌ای، ساخت بدون password، تعارض/admin/name، false/read-back/timeout نامعلوم؛ دو endpoint عددی مستقل، موفقیت جزئی با قطع واقعی TCP پس از اعمال تغییر، read-back تازه و عدم mutation در replay قدیمی.

پروتکل TCP این ابزار **JSON خصوصی آزمایشگاه** است، نه پروتکل vendor ZK یا اجرای `zkemkeeper.dll`. تست‌های SQL جدید batch مخلوط، provenance، grant/revoke، claim credential، unknown و cross-format review را جدا می‌سنجند؛ دو endpoint مجازی به UI/worker/SQL انتهابه‌انتها وصل نیستند. ۶۱ portable قبلی،۱۰ قرارداد خواندن و۱۹ آزمایشگاه قبلی مستقل باقی‌اند. هیچ تستی تضمین صفرخطایی نمی‌دهد.

## ماندهٔ پذیرش

اجرای واقعی COM/SDK، مدل/firmware، کارت صفر‌دار و حدود نام/شناسه، حفظ password واقعی هدف، عدم اثر روی privilege/template/access، قطعی واقعی دو دستگاه و read-back از UI/worker واقعی باید توسط تیم در Windows lab پذیرفته شوند. نتیجهٔ نرم‌افزاریِ این برش پایان بخش سوم نیست: settings/network writes مدل‌محور، PUSH امن، RemoteEnroll/سیاست بیومتریک و فرمان‌های حساس هنوز بازند. `realSdkExecuted=false`، `hardwareVerified=false` و `part3Complete=false` پابرجاست.


## CI تأییدشده

کد `cce03b0ec2de6e5f5ac20f08cf676c3ea7472ff9`: [SQL/HTTPS/مرورگر 43300044652](https://github.com/alirezarahimy-code/IO/actions/runs/43300044652) و [Windows/Linux 43300044555](https://github.com/alirezarahimy-code/IO/actions/runs/43300044555) هر دو **success**. **۶۸۷ integration** شامل۱۰ SQL جدید؛۱۲ بررسی قرارداد نوشتن و۴ TCP جدید جدا از۶۱ portable قبلی،۱۰ قرارداد خواندن و۱۹ virtual قبلی‌اند. ۴۸ Foundation،۲۶ domain،۱۱۴ حق،۵۲ صفحه/راهنما،۳۸ migration و۶۰ جدول. lifecycle واقعی worker CI هم سبز است، اما SDK/مدل اجرا نشده. [شاهد ماشین‌خوان](s08-legacy-user-write-evidence.json).
