# IO

مخزن دو بازنویسی، در کنار نسخه‌های اصلی برای مرجع:

| محصول | نام ثبتی | کد | مسیر | در برابر |
| :-- | :-- | :-- | :-- | :-- |
| سامانه حضور و غیاب و کارتابل | **آریا** | `ARIA` v5.0 | `src/TWF/` | `TWF 4.28` (`MyTime3.dll` + `TD5.dll`) |
| کنسول مدیریت سازمانی | **آریا ادمین** | `ARIA-ADMIN` v5.0 | `src/T5Web/` | `T5Web [3.5.7]` (`T5V3Web.dll`) |

هر دو روی `SQL Server` و `ASP.NET MVC 5 / .NET Framework` اجرا می‌شوند، ۱۰۰٪ آفلاین هستند،
هیچ قفل/لایسنسی ندارند، و موتور T5 را در همان بیلد قفل‌شده مصرف می‌کنند
(`src/TWF/engine/engine-pin.json`).

* ممیزی مقایسه‌ای دو بازنویسی با منابع: `docs/rewrite-audit-aria-vs-t5web.md`
* آریا: `src/TWF/README-ARIA.md` · آریا ادمین: `src/T5Web/README.md`
