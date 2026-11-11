# فاز ۳ — ماتریس پوشش

نشانه‌ها: ✅ کامل · 🟡 جزئی · 🔴 غایب · ⚪ نیازمند تصمیم/خارج از محدوده با قید کاربر. شواهد در `coverage-matrix.csv` و فاز ۱. نسخهٔ ماشین‌خوان: `coverage-matrix.csv`.

| ماژول | TWF/آریا | T5Web | T5V3 | Link5 | IO/ReCode |
|---|---|---|---|---|---|
| مدیریت پرسنل | 🔴 قرائت از T5V3 فقط | ✅ UserInfo/Department/EmpType/UserAddProperty | ✅ PersonnelSystem + T5V3.Model.dll | ✅ Link5.exe AllUserTable + Personnel.sql | ✅ Employees.html + Organization.html |
| مدیریت دستگاه و اتصال | 🔴 اتصال ندارد (IT5BackendAdapter) | ✅ Device/DeviceOperations/iclock | ✅ DeviceDA + HTTPPushServer | ✅ ActiveDevices + Comm5Serv v3.exe + Device.dll | 🟡 EventSources/Mappings/RawEvents — اتصال ⚪ بدون تصمیم |
| دریافت تردد خام | 🔴 | ✅ iclockController PUSH + Records | ✅ AutoDownload/Push | ✅ ConnectAndReadAllRecords + GetDeviceData | ✅ AttendanceIngestion.AppendAsync (API مجوزدار) |
| محاسبهٔ حضور و کارکرد | 🟡 فقط LeaveBalance/Workflow | ✅ TC5.dll پردازشگرها | ✅ T5V3.Attendance.dll + LeaveCalcs | 🟡 ATTCheck در Link5.exe | ✅ ObservedCalculation + DailyCalculationRuns paired-v1 |
| شیفت و تقویم کاری | 🟡 ADayShift/ProcessedSchedule | ✅ Calendar/Shift/AssignShift/GroupAssignShift/TempSchedules | ✅ ScheduleDA + AutoScheduleDA | ✅ Link5.Configurator.exe | ✅ ScheduleManagement + ۳ صفحهٔ scheduling |
| مرخصی DailyLeave/HourlyLeave | ✅ DailyLeave/HourlyLeave/Request | ✅ InsertDailyLeave/LeaveType/LeaveDescription | ✅ LeaveCalcs DailyLeave | 🔴 | ✅ LeaveRequests Category=0 + کارتابل + کسر دفتر |
| اضافه‌کاری OverTime | ✅ OverTime/ConstOT/OverTimeReport | ✅ OverTimeReportController | ✅ AllowOverTimes + FreeOverTime | 🔴 | ✅ Category=1 گردش؛ محاسبهٔ خودکار از تردد 🔴 (G-18) |
| مأموریت DailyDuty/HourlyDuty | ✅ DailyDuty/HourlyDuty | 🟡 TC5 DutyDaily/DutyHourly فقط محاسبه | ✅ DutyDaily/DutyHourly | 🔴 | ✅ Category=2 گردش |
| پاداش/تنبیه Reward | 🟡 RewardController (۲ اکشن) | 🔴 | 🟡 CategoryName/Value در Model | 🔴 | ⚪ نیازمند تصمیم (قید کاربر) |
| ماندهٔ مرخصی RemainLeave | ✅ RemainLeaveController | ✅ RemainedLeavesReport + TransferRemainedLeave | ✅ CalculateRemain/YearLeaveRemain | 🔴 | ✅ ListBalancesAsync + UI مانده در leave-admin |
| وضعیت پرسنل PersonnelStatus | ✅ PersonnelStatusController | ✅ PersonnelStatus + PersonnelStatusReport | ✅ TC5 GetPersonnelStatus | 🔴 | 🔴 (G-02) |
| گزارش مکان LocationReport | 🟡 LocationReportController (۴ اکشن) | 🔴 | ✅ AreaModel/AreaPointModel | 🟡 AddADR/Area در Link5.exe | ⚪ نیازمند تصمیم (قید کاربر) |
| کنترل تردد و درب | 🔴 | ✅ Gate/ChangeLock/TotalControl | 🟡 LockCheker (لایسنس، نه درب) | ✅ Link5.CommandListener + ExtraCommands | ⚪ خارج از محدوده — بدون تصمیم (سند رسمی) |
| گزارش‌ها و خروجی Excel/PDF | 🟡 Report/ViewR/LeavesReport | ✅ ReportDesign/Tablix/GridReport/Export + ReportViewer | ✅ T5V3.Report + ReportGenerator + ReportViewer | 🟡 گزارش‌های خود برنامه | 🟡 ReportBuilder preflight + ExecutiveHome؛ فایل Excel/PDF 🔴 (G-08) |
| مدیریت کاربران/نقش‌ها/مجوزها | 🟡 SessionManager سطحی | ✅ Account/Permit/Manager | ✅ AccessLevel/AllowUsers | ✅ ADManage | ✅ RBAC ~۶۰ مجوز + UI اعطا/لغو + دامنه + ریست گذرواژه |
| API / یکپارچه‌سازی | 🔴 | ✅ iclockController PUSH | ✅ HTTPPushServer/ConfigPushServer | ✅ سرویس‌های ویندوز | ✅ REST JSON کامل + rate limit؛ device-push 🔴 (G-07) |
| رمزنگاری و امنیت | 🟡 PasswordPolicy/AuthToken | 🟡 | ✅ AES در T5V3.exe | ✅ Bio5Securityv2 AES کامل + Bode5Crypto Rfc2898 | ✅ Identity hash + dataProtection AES + CSRF + CSP |
| لاگ و Audit | 🟡 DiagnosticsController | ✅ ViewSystemLogsController | 🟡 | 🟡 CommandHistory | ✅ SecurityAudit + Audit.html + evidence |
| تنظیمات سیستم | ✅ SettingsController + TwfAppSettings | ✅ ParametersController | ✅ SettingsManager/ParameterDA | ✅ Link5.Configurator.exe | ✅ CorrectionSettings + سقف‌ها + evidence |
| Backup / Restore | 🔴 | 🔴 | ✅ AutoBackupDB | ✅ BACKUP/BackupDevcie | 🔴 (G-12) |
| دستگاه‌های بیومتریک Bio5 | 🔴 | 🟡 صفحات DeviceOperations | 🟡 | ✅ Bio5Securityv2 + AxARM + Tiny.ocx | ⚪ خارج از محدوده — بدون تصمیم |
| SDK / لایهٔ سخت‌افزار | 🔴 | 🟡 SqlServerTypes فقط | ✅ داخلی | ✅ zkemkeeper COM + plcommpro native | 🔴 مدل داده فقط (G-07) |
| گردش درخواست/تأیید | ✅ WorkflowEngineService + AcceptRequest/Undo | ✅ Workflow/CustomWF/ShowUsersRequest | 🟡 | 🔴 | ✅ CorrectionWorkflow + LeaveRequests + ویرایش معلق |
| جانشینی Deputy | ✅ DeputyController | ✅ DeputyController | 🟡 | 🔴 | ✅ Delegation + grant + تصمیمِ جانشین + evidence |
| تأخیر/تعجیل Late/Early | ✅ Late/EarlyController | ✅ LateEarlyReport/EditLateEarly | ✅ | 🔴 | 🔴 (G-01) |
| داشبورد | 🔴 | ✅ Dashboard/DashboardItem | 🔴 | 🔴 | ✅ ExecutiveHome ۱۲ شاخص + پنل نقش |
| ورود از Excel Import | 🔴 | ✅ ImportFromExcelController | 🟡 | 🟡 | 🔴 (G-10) |
| اعلان‌ها | ✅ NotifyController/NotificationManager | 🔴 | 🟡 | 🔴 | ✅ Notifications + پنل + mark-read |
| لایسنس/قفل نرم‌افزاری | 🟡 ILicenseService/LicenseStatus | 🟡 ChangeLock | ✅ CheckLockDB/LockCheker/DeviceCountLimit/PersonnelCountLimit + AES | ✅ InitLock/CheckAuthenticate | ⚪ بدون تصمیم — قید کاربر (G-13) |
| انتقال ماندهٔ سال | 🔴 | ✅ TransferRemainedLeaveController | 🟡 LeaveCalcs | 🔴 | 🔴 (G-04) |
| کاردکس/کارت/تصدیق‌نامه | 🔴 | ✅ CardexReport/Card/Certificate | ✅ CardexReport در Model | 🔴 | 🔴 (G-14) |
| تقویم رمضان | 🟡 IsInRamz در TC5 | ✅ RamezanController | 🟡 | 🔴 | 🔴 (G-15) |

**پانویس شواهد ✅/🟡 (گزیده):**
- ReCode محاسبه: `IO.Infrastructure/Calculation/ObservedCalculation.cs` + `DailyCalculationRunRecord`؛ مرخصی: `Leaves/LeaveRequests.cs`؛ مانده: `LeaveLedger.ListBalancesAsync`؛ اعلان: `Notifications/Notifications.cs`؛ RBAC: `Security/IdentityAdministration.cs` (وایت‌لیست مجوزها) + `Ui/users.js`.
- TWF: کنترلرها در `io-stage1-review/src/TWF/TWF.Web/Controllers/`؛ سرویس‌ها در `TWF.Application/`.
- T5Web: ۶۴ کنترلر از متادیتای `App/bin/T5V3Web.dll` + پوشه‌های `App/Views/`.
- T5V3: متادیتای `T5V3.exe`, `T5V3.Model.dll`, `T5V3.LeaveCalcs.dll`, `T5V3.Attendance.dll`.
- Link5: `Link5.exe`, `Bio5Securityv2.dll`, `Bode5Crypto.dll`, `Device.dll`, `Comm5Serv v3.exe`, `Link5.sql`.
- SDK: `zkemkeeper.dll` (COM CZKEM), `plcommpro.dll` (ConnectExt/GetDeviceData*).