# فاز ۵ — چارت کامل و درست برنامه (تحویل اصلی)

نمودارها Mermaid هستند و در هر viewer مارک‌داون (GitHub/Arena) رندر می‌شوند. CSVهای ماشین‌خوان کنار همین فایل‌اند: `inventory.csv`, `coverage-matrix.csv`, `gap-register.csv`, `roadmap.csv`.

## نمودار ۱ — System Landscape (پنج سیستم → IO/ReCode)

```mermaid
flowchart LR
    subgraph REF["سیستم‌های مرجع"]
        TWF["TWF 4.28 / سورس آریا\n(وب کاربری، ۳۰ کنترلر،\nWorkflowEngine/LeaveBalance)"]
        T5WEB["T5Web 3.5.7\n(وب مدیریتی، ۶۴ کنترلر،\n۶۷ View، iclock PUSH)"]
        T5V3["T5V3 9.2\n(دسکتاپ WinForms،\nهستهٔ Attendance/LeaveCalcs)"]
        LINK5["Link5 4.0.4\n(پرسنل + دستگاه،\nسرویس Comm/Push)"]
        SDK["SDK 6.3.1.45 + NewPack\n(zkemkeeper COM، plcommpro، Tiny.ocx)"]
    end
    subgraph IO["IO/ReCode (net10.0)"]
        DOM["IO.Domain"]
        INF["IO.Infrastructure\n(Organization/Attendance/Calculation/Leaves/Workflows/Notifications/Security)"]
        WEB["IO.Web (۳۳ صفحه + REST مجوزدار)"]
        BOOT["IO.Bootstrap"]
        TEST["Tests (SQL/HTTP/Playwright — ۳۱۴ PASS)"]
    end
    TWF -->|"الگوی گردش درخواست‌ها\n(Request ۸ نوع، Undo، Deputy)"| INF
    TWF -->|"الگوی مانده/جانشین/پسورد"| INF
    T5WEB -->|"الگوی صفحات مدیریتی\n(Device/Gate/Report/Ramezan)"| WEB
    T5WEB -->|"پروتکل iclock PUSH (مرجع آینده)"| INF
    T5V3 -->|"هستهٔ محاسبه و مانده\n(LeaveCalcs/Attendance)"| INF
    LINK5 -->|"الگوی سرویس دستگاه + نگاشت فرد→دستگاه"| INF
    SDK -.->|"فقط در صورت تصمیم G-07"| INF
    DOM --> INF --> WEB --> BOOT
    TEST -.-> WEB
```

## نمودار ۲ — Sitemap / درخت منوی کامل IO/ReCode

```mermaid
flowchart TD
    ROOT["/login ✅"] --> WS["/workspace — پنل من ✅\n(/workspace/capabilities)"]
    WS --> EMP["پنل کارمند ✅"]
    WS --> MGR["پنل مدیر ✅"]
    WS --> ADM["پنل مدیریت ✅"]
    WS --> NOT["اعلان‌ها ✅ (unread + mark-all)"]
    EMP --> NR["/workspace/new-request ✅"]
    EMP --> MR["my-requests ✅ (فیلتر شمسی/لغو/ویرایش)"]
    EMP --> MA["my-attendance ✅ · my-schedule ✅"]
    EMP --> ML["my-leave ✅\n(دفتر + درخواست مرخصی/اضافه‌کار/مأموریت)"]
    EMP --> MREP["my-reports ✅"]
    EMP --> DLG["delegation ✅ (جانشین + اعطای تصمیم)"]
    EMP --> EH["employee-home 🟡"]
    MGR --> INB["inbox ✅ (کارتابل + picker + شمسی)"]
    MGR --> DH["decision-history ✅"]
    MGR --> TA["team-attendance ✅"]
    MGR --> MGH["manager-home 🟡"]
    ADM --> ORG["organization ✅ · employees ✅"]
    ADM --> USR["users ✅ کامل · roles ✅ · audit ✅"]
    ADM --> SCH["calendars ✅ · shifts ✅ · schedules ✅\n· employee-history ✅ · sources ✅"]
    ADM --> DAT["raw-events ✅ · mappings ✅ · conflicts ✅"]
    ADM --> CAL["calculations ✅ · rules ✅ · periods ✅"]
    ADM --> OTH["settings ✅ · workflow ✅ · leave-admin ✅\n(دفتر + کارتابل + ماندهٔ کارکنان)"]
    ADM --> REP["reports 🟡 · report-builder 🟡"]
    ADM --> EXE["executive-home ✅ ۱۲ شاخص"]
    ADM --> DEV["دستگاه/درب 🔴⚪ (G-07/G-13 — بدون تصمیم)"]
    WS --> ACC["/workspace/account ✅"]
    WS --> PNL["/panel ابزار سریع ✅"]
```

## نمودار ۳ — سلسله‌مراتب فرم/زیرفرم/پاپ‌آپ (مرجع → معادل IO)

```mermaid
mindmap
  root(("فرم‌ها و پاپ‌آپ‌ها"))
    TWF/آریا
      Request ✅ → new-request/my-requests
      AcceptRequest ✅ → inbox تصمیم
      Undo 🔴 → R1 (G-18)
      Deputy ✅ → delegation
      DailyLeave/HourlyLeave ✅ → my-leave
      DailyDuty/HourlyDuty ✅ → my-leave Category=2
      OverTime/ConstOT ✅ → my-leave Category=1
      Late/Early 🔴 → R2 (G-01)
      Reward 🟡⚪ → تصمیم
      RemainLeave ✅ → leave-admin مانده
      PersonnelStatus 🔴 → R3 (G-02)
      LocationReport 🟡⚪ → تصمیم
      ShowUsersRequest 🔴 → R3 (G-03)
      ViewR/Report 🟡 → reports
      Notify ✅ → Notifications
    T5Web
      Dashboard ✅ → executive-home
      UserInfo/Department/EmpType ✅ → employees/organization
      Permit/Manager ✅ → roles
      Device/DeviceOperations/Gate/ChangeLock/USBManagement 🔴⚪ → G-07/G-13
      iclock ✅ الگوی PUSH → ingestion آینده
      ReportDesign/Tablix/Filter 🟡 → report-builder
      CardexReport/Card/Certificate 🔴 → R7
      Ramezan 🔴 → R7
      ImportFromExcel 🔴 → R5
      TransferRemainedLeave 🔴 → R4
      TempSchedules/GroupAssignShift 🟡 → scheduling
      ViewSystemLogs ✅ → audit
    T5V3
      Main/Settings/DBManager 🟡 → معادل وب‌محور
      LockCheker ⚪ → G-13
      AutoBackupDB 🔴 → R6 (G-11)
    Link5
      Link5.exe مدیریت دستگاه 🔴⚪
      Configurator 🟡 → sources
      Comm5Serv/PushV3 سرویس 🔴⚪ → G-07
      Bio5/Bode5 رمزنگاری ✅ الگو → تنها در صورت تصمیم
```

## نمودار ۴ — جریان End-to-End (از تردد خام تا خروجی)

```mermaid
flowchart LR
    DEV["دستگاه تردد\n(T5V3/Link5/iclock)"] -->|"⚪ G-07 تصمیم"| RAW
    MAN["منبع تردد + نگاشت\n✅ sources/mappings"] --> RAW["رویداد خام ✅\nRawEvents + AppendAsync"]
    RAW --> CON["تعارض ✅\nConflicts + note"]
    CON --> CALC["محاسبهٔ جفت‌سازی ✅\nDailyCalculationRuns + revision"]
    CALC --> PRD["دوره‌ها ✅\npreflight/draft/diagnostic"]
    RAW --> CRQ["اصلاح تردد ✅\n(Submit/Inbox/Decide/Edit/Cancel)"]
    CRQ --> DEC["تصمیم مدیر ✅\n+ جانشین ✅ + اعلان ✅"]
    CRQ --> UND["بازگشت تصمیم 🔴 R1"]
    PRD --> ST["وضعیت پرسنل 🔴 R3"]
    LEV["مرخصی/اضافه‌کار/مأموریت ✅\n(my-leave → کارتابل → decide)"] --> LEDG["دفتر مرخصی ✅\nکسر در تأیید"]
    LEDG --> BAL["مانده ✅ (RemainLeave)"]
    BAL --> TRN["انتقال سال 🔴 R4"]
    CALC --> LATE["تأخیر/تعجیل/اضافه‌کار خودکار 🔴 R2"]
    PRD --> REP["گزارش‌ها 🟡"]
    BAL --> REP
    REP --> OUT["خروجی Excel/PDF 🔴 R5"]
    OUT --> PAY["حقوق ⚪ خارج از محدوده"]
    DEC --> NOTIF["اعلان ✅"] --> WS["پنل نقش ✅"]
    ST --> DASH["داشبورد اجرایی ✅"]
```

## نمودار ۵ — Module Map (لایه‌های IO/ReCode)

```mermaid
flowchart TB
    subgraph WEB["IO.Web"]
        UI["Ui/ — ۳۳ صفحه + index.js role-panel + theme"]
        API["Program.cs — minimal APIs (~۱۰۰ endpoint) + CSP + CSRF + RateLimit"]
    end
    subgraph INF["IO.Infrastructure"]
        SEC["Security: IdentityAdministration · PermissionTransaction · PersonalCredentials · EmployeeAccessReader"]
        ORG["Organization · Scheduling"]
        ATT["Attendance · Calculation · Periods"]
        WRK["Workflows(CorrectionWorkflow) · Delegation"]
        LEV["Leaves(LeaveLedger+LeaveRequests) · Notifications"]
        REP["Reports(ExecutiveOverview/Personal) · Settings"]
        ST["Storage: IoDbContext + ۴۲ جدول + ۲۱ migration + تریگرها"]
    end
    DOM["IO.Domain: ارزش‌ها/ثابت‌ها (PersianDates...)"]
    BOOT["IO.Bootstrap: host/ورود ادمین اولیه"]
    T["Tests: SqlTests(۳۱۴ PASS) · Domain.Tests · browser-check.cjs"]
    UI --> API --> SEC & WRK & LEV & ATT & ORG & REP
    SEC --> ST
    WRK --> SEC & ST
    LEV --> SEC & ST
    ATT --> ST
    INF --> DOM
    BOOT --> WEB
    T -.-> BOOT
```

## نمودار ۶ — Roadmap فازهای باقی‌مانده (متصل به Gap Register)

```mermaid
gantt
    dateFormat YYYY-MM-DD
    title نقشهٔ ادامهٔ کار (هر فاز = برش سبز CI)
    section گردش
    R1 بازگشت/Undo امن (G-18)          :r1, 2026-11-09, 3d
    R2 Late/Early + اضافه‌کار خودکار (G-01,G-12) :r2, after r1, 6d
    section گزارش و وضعیت
    R3 PersonnelStatus + ShowUsersRequest + SumReport (G-02,G-03,G-19) :r3, after r2, 5d
    R4 انتقال ماندهٔ سال (G-04)         :r4, after r3, 3d
    section خروجی
    R5 Excel/PDF + گزارش‌ساز دوم + Import (G-08,G-09,G-10) :r5, after r4, 6d
    R6 Backup/Restore (G-11)          :r6, after r5, 3d
    R7 تکمیلی‌ها (G-14..G-17,G-20,G-15) :r7, after r6, 5d
    section منتظر تصمیم
    D1 پاداش/مکان/دستگاه/لایسنس (G-05,G-06,G-07,G-13) :d1, 2026-11-09, 30d
```
