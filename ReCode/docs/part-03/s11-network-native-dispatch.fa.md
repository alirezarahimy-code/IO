# شاهد نوشتن شبکه در مرز SDK — نه پذیرش firmware

## هدف و روش

برای رفع ابهام «وجود رشته فقط برای read است یا setter هم دارد»، ابزار `ReCode/tools/inspect-zkem-network-dispatch.py` دستورهای x86 تابع exportشدهٔ `Z_SetSysOption` دو DLL مرجع را با Unicorn اجرا می‌کند. فایل‌ها فقط خوانده می‌شوند؛ hash کامل و آدرس export پیش از آزمون تطبیق می‌شود. DLL در Windows بارگذاری نمی‌شود، `DllMain` و COM اجرا نمی‌شوند. تمام importها به trap هدایت شده‌اند؛ هر import بررسی‌نشده خطا می‌دهد. `sprintf`، `strlen`، `memset` و helperهای بررسی stack/cookie جایگزین محدود دارند. transport واقعی `commpro` هرگز اجرا نمی‌شود؛ payload و command آن ثبت و فقط ACK مصنوعی برگردانده می‌شود. بودجهٔ هر probe صد هزار دستور و دو ثانیه است.

این شاهد **باینری و اجرای مجازی دستورها** است، نه سورس vendor، آزمایش native روی Windows، ACK واقعی، ماندگاری تنظیم یا سازگاری مدل.

## نتیجهٔ قابل بازتولید

۳۱ probe در [خروجی دقیق](s11-network-native-dispatch-evidence.json):

- `SDK 6.3.1.45/zkemsdk.dll`، SHA256 `890596687008389d88eabb15ae0d02b9b24cc19b6caf861f84dfd13c515cec91`؛ `Z_SetSysOption` در `0x1002f4a0`.
- `NewPack Dlls 10.06.1398/zkemsdk.dll`، SHA256 `52879682f7c7e5cb78bcae5ad9c9f8b4fe1c51f4df3688b10836d62a80f29384`؛ `Z_SetSysOption` در `0x1001d440`.
- هر دو نسخه `TCPPort=4371\0` را با command عددی `12` به مرز transport می‌رسانند. دو مرز معتبر port یعنی `1` و `65535` نیز بررسی شده‌اند. ACK مصنوعی `2000` خروجی SDK برابر `1` و ACKهای `2001`/`0` خروجی `-2` دارند. context تهی و نام گزینهٔ تهی هیچ write ارسال نمی‌کنند.
- در نسخهٔ SDK6، `Z_SetDeviceInfo` در `0x10023bc0` با **کد70** payload `TCPPort=4371\0` را از مسیر نوشتن تولید می‌کند. کد69 در همین DLL **ControlMinute** است، نه port؛ این کنترل منفی هم اجرا و ثبت شده است. بنابراین شمارهٔ69 [جدول عمومی reverse-engineered](https://github.com/adrobinoga/zk-protocol/blob/abe68e85f3a0acfc53d27720af29edf79102e6e8/sections/terminal.md) به این DLL تعمیم داده نمی‌شود. پیاده‌سازی IO همچنان نام دقیق `TCPPort` را از `SetSysOption` می‌فرستد، نه شمارهٔ نسخه‌وابستهٔ `SetDeviceInfo`.
- هر دو نسخه `DHCP=0\0` و `DHCP=1\0` را هم عبور می‌دهند؛ **اما** گزینهٔ ساختگی `IO_UNSUPPORTED_PROBE=1\0` نیز عبور می‌کند. پس عبور از setter عمومی شاهد معنای DHCP یا پشتیبانی firmware نیست. نبود literal DHCP در DLL هم اثبات unsupported نیست.

## اثر روی پذیرش

این شاهد تازه ابهام setter باینری `TCPPort` را برطرف می‌کند؛ مسیر نرم‌افزاری کامل port از قبل روی `6e8f9d9` با SQL۹۹۱ و Windows/Linux پذیرفته شده است. R07 پس از موفقیت CI همین شاهد روی85347c2 (SQL۹۹۱/Windows/Linux/light) در ledger نرم‌افزاری بسته و جمع به۱۰ فنی باز رسید. هنوز نه موفقیت از ACK شبکه استنتاج می‌شود و نه اتصال/بازیابی مدل واقعی تأیید شده است؛ آن‌ها در H01 می‌مانند.

در snapshot همین برش85347c2، R05 هنوز باز بود؛ وضعیت فعلی بستهٔ نرم‌افزاری5e1f332 در انتهای سند آمده است: نام و معنای قابل اتکای DHCP برای خانوادهٔ firmware هدف باید از مرجع SDK/firmware یا نمونهٔ مستقل واقعی تثبیت شود. approval نصاب، USB config، شبیه‌ساز و forwarding یک رشتهٔ دلخواه چنین شاهدی تولید نمی‌کنند. هیچ fallback حدسی به `DHCPOn`/گزینهٔ دیگر، scan، reboot یا retry اضافه نشده است.

### شاهد عمومی تازهٔ DHCP و حد دقیق آن

با گسترش جست‌وجو از COM به `CMD_OPTIONS_WRQ`، یک **writer واقعی در سورس عمومی ثالث** پیدا شد، نه صرفاً رشتهٔ config: [ZKTerminal.setDHCP](https://github.com/virtualheart/ZKteco-biomatric-lib/blob/b35d78d93f906e53dc9e9f8015e5fa772fc40f1b/src/main/java/com/zkteco/terminal/ZKTerminal.java#L1515-L1534) payload `DHCP=` را با `OnOffenum` می‌سازد و به UDP می‌فرستد؛ getter آن هم `DHCP` است. در همان commit، `ON(1)`/`OFF(0)` و `CMD_OPTIONS_WRQ(12)` صریح‌اند. [شناسهٔ commit، Git blob، SHA256 و excerptهای شماره‌خط‌دار](s11-dhcp-public-reference-evidence.json) ثبت شد. هیچ بسته یا سورس کامل ثالث به محصول وارد نشده است.

این شاهد نگاشت کاندید `DHCP`/`0|1` را تقویت می‌کند و با مرز setter همین دو SDK سازگار است؛ اما نویسنده گزارش آزمون همان مدل/firmware یا COM ارائه نکرده و README نیز کتابخانهٔ پروتکل جایگزین را توصیف می‌کند. مسیر UDP ثالث **گواه اجراشدن setter COM روی firmware هدف نیست**. به همین دلیل در85347c2 این کشف فقط زیرکار تحقیق بود، نه مبنای کاهش R05. محدودیت قدیمی «هیچ نمونهٔ writer پیدا نشده» دیگر درست نیست؛ محدودیت باقی‌مانده دقیقاً پشتیبانی مدل/firmware در مسیر COM است.

## اجرای مجدد

```sh
python3 -m pip install pefile==2024.8.26 unicorn==2.1.4
python3 ReCode/tools/inspect-zkem-network-dispatch.py --verify
```

بدون `--verify` گزارش JSON تولید می‌شود. با آن، خروجی کامل با شاهد ثبت‌شده برابر می‌شود؛ تغییر DLL یا payload باید fail شود. workflow سبک همین بررسی را مستقل از UI اجرا می‌کند. این تغییر هیچ مجوز، schema، worker، bridge یا سیاست اجرایی را عوض نمی‌کند؛ همان مجوزهای مستقل set/execute و طرح تأییدشدهٔ endpoint/recovery برقرارند.

## شاهد تکمیلی پس از85347c2

[قرارداد DHCP5e1f332](s11-dhcp-write-contract.fa.md) علاوه بر شواهد عمومی بالا، نام/دامنه شناخته‌شده در منبع firmware و handler/ذخیره/بارگذاری را با چهار blob دقیق ثبت کرد؛ mapper واقعی محصول هم۲۹ بررسی تازه دارد. CI کامل موفق و R05 در سطح قرارداد نرم‌افزاری بسته شد؛ ابهام firmware نصب و COM واقعی همچنان در H01 است. بنابراین وضعیت R05 باز در بخش‌های بالا تاریخچهٔ برش قبلی است. این نتیجه از پذیرش هر نام دلخواه توسط setter عمومی به‌دست نیامده است.
