# فاز ۲ — وضعیت فعلی IO/ReCode (شاخهٔ arena/5496cceb-io، CI 43025915628 سبز، ۳۱۴ بررسی)

نشانه‌ها: ✅ کامل و آزموده · 🟡 جزئی/اسکلت · 🔴 غایب · 📝 در حال ساخت · ⚪ نیازمند تصمیم

## ۱) موجودیت‌های دامنه
✅ `IO.Infrastructure/Storage/`: FoundationRecords (Organization/Company/Site/OrgUnit/Employee/Assignment/EmploymentProfile)، AttendanceRecords، CalculationRecords، CorrectionRecords، **LeaveRecords (LeaveType/Entry/Request/Decision — با Category: مرخصی/اضافه‌کار/مأموریت)**، DelegationRecords، SettingsRecords، **NotificationRecords**. ۲۱ مهاجرت + تریگرهای تغییرناپذیری. ✅ امنیتی: `UserRecord/RoleRecord/PermissionRecord/UserRoleRecord/RolePermissionRecord/UserOrgScopeRecord`.
🔴 کارت/بایومتریک/دستگاه فیزیکی ⚪ پاداش-جریمه/مکان (تصمیم‌دار).

## ۲) فرم‌ها/صفحات اصلی
✅ ۳۳ صفحهٔ HTML در `IO.Web/Ui/` + هاب `/workspace` (پنل نقش‌محور). کامل‌ترین‌ها: Employees, Organization, Users (با جزئیات/جست‌وجو/ریست‌گذرواژه/دامنه), Roles, Audit, Scheduling (calendars/shifts/schedules), RawEvents, Mappings, Conflicts, Calculations, Periods, Settings, Workflow, MyRequests, Inbox, DecisionHistory, TeamAttendance, MyAttendance, MyLeave (درخواست مرخصی+اضافه‌کار+مأموریت), LeaveAdmin (دفتر+کارتابل+مانده)، Delegation, ExecutiveHome (۱۲ شاخص)، Index (پنل نقش + اعلان‌ها).
🟡 ReportBuilder (preflight)، Reports 🟡، EmployeeHome/ManagerHome 🟡.
🔴 Device/Gate/ChangeLock/USBManagement/Permit ⚪ (بدون تصمیم — سند ثبت شده).

## ۳) زیرفرم/Tab
✅ انتخاب کارمند صفحه‌ای کارتابل (picker ۵۰تایی)، فیلتر شمسی مشترک، جزئیات کاربر، builder گزارش.
🟡 Tabهای داخل صفحات تک‌بخشی.

## ۴) پاپ‌آپ/دیالوگ
✅ الگوی «صفحهٔ مجزا + notice aria-live» به‌جای Modal؛ پیام‌های فارسی ۴۰۱/۴۰۳/۴۰۰/۴۰۹/۴۲۹ در همهٔ JSها.
🔴 Modal دسکتاپی نداریم (عمدی — وب).

## ۵) منو/ناوبری
✅ پنل نقش‌محور (`Ui/Index.html` با employee/manager/admin sections از `/workspace/capabilities`) + page-nav هر صفحه + سایدبار پنل ابزار (`/panel`) — بازگردانده‌شده طبق تصحیح کاربر.

## ۶) جریان End-to-End
✅ منبع تردد→نگاشت→رویداد خام→تعارض→محاسبهٔ جفت‌سازی (`ObservedCalculation`/`DailyCalculationRuns` با revision)→دوره‌ها (preflight/draft/diagnostic) → اصلاح تردد با گردش کامل (submit/inbox/decide/دلگیت/ویرایش/لغو) → اعلان → داشبورد اجرایی.
✅ مرخصی/اضافه‌کار/مأموریت: submit→کارتابل دامنه→decide→کسر دفتر (فقط مرخصی)→اعلان.
🟡 خروجی حقوق/Excel — ReportBuilder فقط preflight.
⚪ تبدیل به فایل حقوق (خارج از محدودهٔ فعلی).

## ۷) سرویس‌ها/API
✅ همهٔ endpointها در `IO.Web/Program.cs` (minimal APIs) با مجوز دانه‌ریز (~۶۰ مجوز `*.manage/*.read*/leave.request.*` و…) + CSRF + rate limiting + audit. سرویس‌ها: Security (IdentityAdministration/PermissionTransaction/PersonalCredentials/EmployeeAccessReader)، Organization، Attendance، Scheduling، Calculation، Periods، Reports، Leaves (LeaveLedger+LeaveRequests)، Workflows (CorrectionWorkflow)، Delegation، Settings، Notifications، ExecutiveOverview.
📝 خودکار: nothing in flight.

## ۸) امنیت
✅ Cookie auth + SecurityStamp rotation (ابطال فوری)، lockout ۵تایی، گذرواژهٔ موقت با claim + صفحهٔ اجباری، AES-protected dataProtection، CSP، audit جدول `SecurityAudit`، لغو/اعطای زندهٔ نقش‌ها/مجوزها/دامنه‌ها از UI.
🔴 رمزنگاری بایومتریک Bio5/Bode5 معادل لازم ندارد (دستگاه بیرون از محدوده) ⚪.

## ۹) دستگاه
🟡 منابع تردد + نگاشت + رویداد خام + conflicts (مدل داده کامل)؛ ingestion API با مجوز `attendance.ingest`.
🔴 اتصال واقعی دستگاه/سرویس Comm/Push — «خارج از محدوده، بدون تصمیم» (سند `Comprehensive-Scenario.fa.md`).

## آزمون‌ها (شاهد: `ReCode/tests/`)
✅ IO.Domain.Tests؛ IO.Infrastructure.SqlTests: **۳۱۴ PASS گیت‌شده با phase03-report-tests.py** — migration/rollback ۲۱ مهاجرت/۴۲ جدول، HTTP security (~۱۰۰)، مرورگر Playwright (۳۷ مسیر + پنل + جست‌وجوی کاربر)، workflow/leave/delegation/notifications/balance.
