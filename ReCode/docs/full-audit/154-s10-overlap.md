# ادامهٔ S10 — بازخوانی هم‌پوشان برای تردد دیررس

**بخش سوم هنوز کامل نشده است؛ این برش فقط polling را تکمیل‌تر می‌کند، نه vendor PUSH یا پذیرش مدل.**

- `OverlapSeconds` در فرم/API/SQL با پیش‌فرض۰؛ legacy scope خودکار بزرگ نمی‌شود. مقدار صریح ۰ تا WindowSeconds با تأیید همان schedule، سپس immutable؛ تغییر نیازمند زمان‌بندی تازه است.
- range هر run از `max(StartUtc,cursor-overlap)` تا حد جلوخوانی window/lag است؛ cursor منطقی هرگز عقب نمی‌رود و فقط روی read کامل و review لازم جلو می‌رود. clamp در پایین‌ترین تاریخ UTC نیز در SQL/application آزموده شد.
- دیررس داخل lookback وارد مسیر raw/conflict موجود می‌شود؛ tuple اصلی تکراری به RawEventId قبلی اشاره می‌کند و payload اصلی دست‌نخورده است. tuple دیرتر ولی قدیمی‌تر از lookback قرنطینهٔ outside-range می‌گیرد؛ همهٔ گذشته تضمین نشده است.
- همان مجوزهای مستقل S10 و S09، همان نقش operator خواندنی/admin مدیریت، current creator reauthorization و gateهای worker. grant یا صف اجرایی پنهان اضافه نشده است.
- مهاجرت `20261112140000_DevicePollOverlap`، default۰، CHECK و trigger نسخه‌دار؛ downgrade با هر overlap مصوب غیرصفر خطای51027. migration قبلی و مرجع‌ها ویرایش نشده‌اند.

## CI

کد `0f31066f2b1cd8d7ac156fad56026ed831c96267`:

- [SQL/HTTPS/مرورگر 43255522942](https://github.com/alirezarahimy-code/IO/actions/runs/43255522942): **success، ۶۶۷ integration**؛ S10 اکنون ۲۸ سرویس و ۷ HTTP دارد (۸ سرویس/۲ HTTP افزوده).
- [Windows/Linux 43255522608](https://github.com/alirezarahimy-code/IO/actions/runs/43255522608): **success**؛ ۴۸ Foundation، ۲۶ domain، ۶۱ portable و lifecycle واقعی worker در SCM/systemd CI.
- ۱۱۴ حق، ۵۲ صفحه/راهنما، ۳۷ migration و ۶۰ جدول؛ این برش همان صفحات/حقوق واقعی را توسعه داد نه صفحهٔ mock.
- [شاهد](../part-03/s10-overlap-evidence.json)، [قرارداد](../part-03/s10-overlap.fa.md)، [ماندهٔ دقیق همهٔ مراحل](../part-03/remaining-work.fa.md).

نوشتن تنظیمات/شبکه و فرمان‌های حساس مدل‌محور، RemoteEnroll با قرارداد/سیاست بیومتریک، PUSH امنِ مدل پشتیبان، پوشش مدل واقعی و آزمایشگاه چنددستگاه/قطعی/دریافت/تبادل/تنظیمات بازند. هیچ SDK/دستگاه واقعی در این محیط اجرا نشده و `part3Complete=false` باقی است.
