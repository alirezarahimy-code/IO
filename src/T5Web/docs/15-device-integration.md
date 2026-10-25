# ارتباط با دستگاه — سه راه، و اینکه کدام‌شان امروز کار می‌کند

**سؤال این سند:** دکمهٔ «اجرا» در صفحهٔ دستگاه‌ها به کجا وصل است؟

**پاسخ صادقانهٔ وضعیت قبل از این تغییر:** به هیچ‌جا. پورتال یک ردیف در
`[T5WebDeviceCommand]` می‌نوشت و پیام «سرویس دستگاه آن را برمی‌دارد» می‌داد، در حالی که
**هیچ مصرف‌کننده‌ای برای آن جدول در هیچ‌کدام از برنامه‌های روی سرور وجود ندارد** (BUG-44).

---

## ۱. چه چیزی در نسخهٔ قبلی بود (شاهد از اسمبلی خودش)

کنسول قدیمی **دو** راه ارتباط با دستگاه داشت، و هر دو در `T5V3Web.dll` هستند:

### الف) پروکسی به سرویس Link5 — ۳۲ کنش

`T5Web [3.5.7]/App/Web.config`:

```xml
<add key="Link5ServiceURL" value="http://127.0.0.1:15222/Device/" />
```

و در `DeviceOperationsController` (با متدهای `GetServiceURL` و `CallDeviceServicePost`) این
آدرس‌ها ساخته می‌شد — عیناً از رشته‌های اسمبلی:

```
downloadrecords?machineNumber=      downloadtimerecords?machineNumber=
downloadUsers?machineNumber={0}&face={1}&finger={2}
uploadUsers?machineNumber={0}&face={1}&finger={2}&userinfo={3}&photo={4}&fast={5}&over={6}
timesync?machineNumber=             timesyncdata?machineNumber=
restartdevice?   deletealldata?   deleteallrecords?   deleteadmins?   removetimerecords?
getcapacity?   getadvancedoptions?   getadvancedoptions2?   showusers?   remoteenroll?
setip?   set1_1?   set1_n?   setalghorithm?   setcardreg?   setdlst?   setrepeattime?
setsleeptime?   setsector?   changekey?   readkey?   connect?   disconnect?
```

### ب) دریافت push دستگاه داخل خودِ وب

`T5V3Web.Controllers.iclockController` با متد `cdata` — یعنی کنسول قدیمی خودش
**گیرندهٔ push دستگاه‌های ZKTeco** (مسیر `/iclock/cdata`) بود؛ همان چیزی که
`Comm5Service v4` در Link5 هم انجام می‌دهد.

به‌علاوهٔ `InsertRecordController` (ثبت دستی تردد) و `USBManagementController` (خواندن از USB).

---

## ۲. وضعیت بازنویسی

| قابلیت | نسخهٔ قبلی | بازنویسی | چرا |
| :-- | :-- | :-- | :-- |
| فهرست/تنظیم دستگاه | ✅ | ✅ `DeviceController` | — |
| دستور دستگاه | ۳۲ کنش، پروکسی HTTP | ۵ کنش بسته، از طریق transport | کنش‌های قفل/شکننده حذف؛ بقیه باید وصل شوند |
| دریافت push (`iclock`) | ✅ داخل وب | ❌ | یک اپ وب نباید نشست طولانی دستگاه را نگه دارد؛ مال سرویس ویندوز است |
| ثبت دستی تردد | ✅ | ❌ | باید از مسیر «اصلاح تردد» بازنویسی شود (موج ۴) |
| USB | ✅ | ❌ | Zero-Lock و وابستگی به دانگل |

پنج کنش باقی‌مانده و نام عملیاتی که در سرویس Link5 دارند:

| کد در پورتال | عملیات در Link5 |
| :-- | :-- |
| `sync-users` | `uploadUsers` |
| `pull-users` | `downloadUsers` |
| `pull-punches` | `downloadrecords` |
| `sync-time` | `timesyncdata` |
| `clear-punches` | `deleteallrecords` |

این نگاشت در `DeviceTransport.LegacyOperation` است و آزمون `T43` آن را با نام‌های
استخراج‌شده از اسمبلی قدیمی مقایسه می‌کند؛ اگر کسی نامی را عوض کند، بیلد می‌شکند.

---

## ۳. سه راه، و انتخاب

### راه ۱ — پروکسی به Link5 (توصیه‌شده، کم‌ترین کار)

```xml
<add key="T5WebDeviceTransport" value="link5:legacy" />
<!-- یا صریح: link5:http://192.168.21.112:15222/Device/ -->
```

پورتال همان URLی را می‌زند که کنسول قدیمی می‌زد. **مزیت:** صفر کد جدید روی سرویس، و
Link5 از قبل نصب و لایسنس‌دار است. **ریسک:** امضای دقیق پاسخ‌ها و پارامترهای
`uploadUsers` (شش پارامتر) از روی نام‌ها بازسازی شده، نه از روی یک اجرای واقعی؛ برای
`uploadUsers` باید پارامترها با دسکتاپ Link5 مقایسه شوند.

### راه ۲ — صف + عامل خارجی

```xml
<add key="T5WebDeviceTransport" value="queue" />
```

پورتال ردیف می‌نویسد و **شما** عاملی دارید که آن را برمی‌دارد (سرویس کوچک، اسکریپت
زمان‌بندی‌شده). این گزینه فقط وقتی `queue` است که آن عامل واقعاً مستقر باشد؛ وگرنه همان
دروغ قبلی است با یک کلید تنظیمات.

### راه ۳ — سرویس خودمان (بلندمدت)

یک Windows Service با `SDK 6.3.1.45` (زکتکو) که هم صف را برمی‌دارد و هم push را می‌گیرد.
این کار Link5 را از معادله خارج می‌کند، ولی یعنی بازنویسی لایهٔ دستگاه — پروژه‌ای مستقل،
نه یک قدم از این بازنویسی.

---

## ۴. آنچه واقعاً سنجیده شده

| ادعا | وضعیت |
| :-- | :-- |
| پنج کنش به پنج عملیات واقعی Link5 نگاشت می‌شوند | ✅ `T43` — با نام‌های استخراج‌شده از `T5V3Web.dll` |
| URL نهایی درست ساخته می‌شود (با/بدون `/` انتهایی) | ✅ `T43` |
| نصبِ بدون transport دستور را **رد** می‌کند و دکمه را پنهان می‌کند | ✅ `T26` + `T43` |
| پیش‌فرض `Web.config` روی `none` است | ✅ `T43` |
| تماس واقعی با `http://127.0.0.1:15222/Device/` روی یک Link5 زنده | ❌ **اثبات‌نشده** — در CI نصب Link5 وجود ندارد |
| دریافت push دستگاه در نسخهٔ جدید | ❌ پیاده نشده (عمدی؛ مال سرویس ویندوز است) |

**نتیجه:** بعد از این تغییر، دکمه یا واقعاً کاری می‌کند یا وجود ندارد. اما «واقعاً کار
می‌کند» تا وقتی روی سرور خودتان با `link5:legacy` امتحان نشود، فقط «مسیرِ درست» است، نه
«اثبات». اولین کاری که باید روی آن سرور انجام شود همین است.
