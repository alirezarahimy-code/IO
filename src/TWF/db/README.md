# پایگاه‌داده سامانه آریا — نوع، مالکیت و راه‌اندازی

## پاسخ کوتاه

**خیر، من دیتابیس نساختم — و به‌درستی هم نباید می‌ساختم.**
سامانه آریا **مالک اسکیمای دیتابیس نیست**؛ دیتابیس متعلق به `T5V3` است و اسکیمای آن توسط
نصب‌کننده T5V3 ساخته/ارتقا داده می‌شود. کاری که در بازنویسی انجام شد، اتصال به همان دیتابیس موجود است.

## نوع دیتابیس

| مورد | مقدار |
| :--- | :--- |
| **نوع** | **Microsoft SQL Server** (T-SQL) |
| **درایور** | `System.Data.SqlClient` (ADO.NET) |
| **ORM / لایه دسترسی** | **Dapper** در پروژه `src/TWF/TD5` + کلاس‌های DA وابسته به `T5V3.DB.dll` |
| **تعداد کاتالوگ** | ۲ عدد |
| **EF (Entity Framework)** | در نسخه قدیمی استفاده می‌شد؛ در بازنویسی **حذف شده** (فقط رشته‌های EF قدیمی به‌صورت سازگار خوانده می‌شوند) |

### دو کاتالوگ

| کاتالوگ | نقش | کلید پیکربندی |
| :--- | :--- | :--- |
| `ReportT5V3` | داده اصلی T5: تردد، درخواست‌ها، گردش کار، تقویم، مرخصی، جانشینی، محدوده جغرافیایی | `T5CnnStr` |
| `Personnel` | ساختار سازمانی و پرسنل: `UserInfo`، `TitlesChart`، `Department`، `UserInfoTitles` | `PersonnelCnnStr` |

پیکربندی در `TWF.Web/Web.config`:

```xml
<connectionStrings>
  <add name="T5CnnStr"        connectionString="Data Source=.;Initial Catalog=ReportT5V3;Integrated Security=True;MultipleActiveResultSets=True;" providerName="System.Data.SqlClient" />
  <add name="PersonnelCnnStr" connectionString="Data Source=.;Initial Catalog=Personnel;Integrated Security=True;MultipleActiveResultSets=True;" providerName="System.Data.SqlClient" />
</connectionStrings>
```

> در سرور واقعی، `Data Source` را به نمونه SQL Server و احراز هویت را به `Integrated Security=True`
> یا `User ID=...;Password=...` تنظیم کنید. نام‌های قدیمی `Personnel` و `PersonnelCnnStr` هر دو پشتیبانی می‌شوند.

## مالکیت جداول

| گروه | جداول | مالک | آریا چه می‌کند |
| :--- | :--- | :--- | :--- |
| هسته T5 | `Request`, `RequestAccept`, `Workflow`, `Record`, `DailyLeave`, `HourlyLeave`, `OverTime`, `Deputy`, `PersonnelDeputy`, `Area`, `Position`, `UserPositionData`, `Parameter`, `Schedule`, `YearRemain`, `LeavesAdded`, `ReportAccess`, `Report`, `ReportData`, `SystemLog`, `AllowValue`, `EditedValue`, `DepartmentRule` | T5V3 | خواندن/نوشتن طبق قرارداد T5 |
| مشترک (T5 DA) | `Calendar`, `DepartmentRules`, `LeaveTypes`, `UserInfo`, `UserRules` | T5V3 | از طریق `TD5` (Dapper) بازنویسی‌شده |
| سازمانی | `UserInfo`, `TitlesChart`, `UserInfoTitles`, `Department`, `EmpType` | T5V3 (کاتالوگ Personnel) | فقط خواندن |
| **متعلق به اپ** | **`PassCheckProps`** | **آریا** | تاریخچه تغییر کلمه عبور |

اسکریپت‌های `Link5.sql` و `Personnel.sql` در پوشه `Link5 v4.0.4/` متعلق به **فروشنده** هستند (UTF-16،
مربوط به `Link5` و `Personnel`) و بخشی از تحویل آریا نیستند.

## راه‌اندازی روی سرور آفلاین

۱. **SQL Server** را نصب کنید (Express کافی است) — ترجیحاً با `Mixed Mode` یا `Integrated Security`.
۲. **T5V3** را نصب/بازیابی کنید تا `ReportT5V3` و `Personnel` با اسکیمای کامل ساخته شوند
   (یا فایل پشتیبان دیتابیس موجود سازمان را Restore کنید).
۳. **تنها شیء متعلق به آریا** را بسازید:

```bat
sqlcmd -S . -E -i db\01-app-objects.sql
```

این اسکریپت **idempotent** است: اگر جدول موجود باشد دست نمی‌زند و فقط وضعیت را گزارش می‌کند.
هیچ جدول متعلق به T5 را تغییر نمی‌دهد.

۴. **صحت راه‌اندازی** را بررسی کنید:

```bat
sqlcmd -S . -d ReportT5V3 -E -i db\verify-schema.sql
sqlcmd -S . -d Personnel   -E -i db\verify-schema.sql
```

خروجی، سه بخش دارد: حضور جداول لازم، تعداد رکورد جداول کلیدی، و صحت قرارداد `PassCheckProps`.

## اگر دیتابیس T5 در دسترس نباشد

برای تست/دمو بدون SQL Server، سامانه حالت **In-Memory** دارد:

- `TWF.Infrastructure/Adapters/InMemoryT5BackendAdapter.cs` — پیاده‌سازی کامل `IT5BackendAdapter`
- `DBInitializer.EnableTestMode(...)` — فعال‌سازی این حالت
- تمام ۴۱ سوئیت تست (`TEST-01`..`TEST-41`) روی همین آداپتور اجرا می‌شوند و **بدون دیتابیس** پاس می‌شوند.

## دو نقص دیتابیسی که در این مرحله رفع شد

| نقص | وضعیت قبلی | اصلاح |
| :--- | :--- | :--- |
| **رشته اتصال** | کد از `AppSettings["T5CnnStr"]` می‌خواند، ولی مقادیر در `<connectionStrings>` قرار داشتند → روی سرور واقعی `null` و کرش در `Init.Start` | افزودن `ConnectionStringResolver` (اولویت `connectionStrings` → `appSettings` + عادی‌سازی رشته EF) و هدایت تمام نقاط خواندن به آن (`DBInitializer`, `MvcApplication`, `ChangePassController`, `TitleChartClass`, `DescriptionClass`) با خطای پیکربندی گویا |
| **تاریخچه کلمه عبور** | سه نقطه کد سه چیز متفاوت می‌خواستند: `PassCheck` (کاتالوگ Personnel، ستون `DateChange`) در برابر `PassCheckProps` در برابر ستون `ChangedPass` — هیچ‌کدام با اسکیمای واقعی T5 نمی‌خواند | یکسان‌سازی روی قرارداد قدیمی و واقعی T5: `[ReportT5V3].[dbo].[PassCheckProps]([ID],[Badge],[LastChangeDate])` در `ChangePassController` و `TwfDataRepository` + اسکریپت ساخت idempotent |

> **نکته صداقتی:** این دو اصلاح به‌صورت ایستا و با مقایسه با کد دی‌کامپایل‌شده نسخه ۴.۲۸ انجام شده‌اند.
> اجرای مجدد ۴۱ سوئیت تست در این محیط ممکن نشد، چون زنجیره ابزار بیلد (`csc` / `dotnet`) پس از
> ری‌استارت سندباکس پاک شده است. پس از بازسازی زنجیره، `./src/TWF/build-and-test.sh` باید اجرا شود.
