# فاز ۱ — استخراج موجودیت/فرم/پاپ‌آپ/منو از سیستم‌های مرجع

روش استخراج باینری: parser دستی PE/CLI (heap `#Strings` متادیتای .NET) + جدول Export برای native + `strings`. هیچ DLL مدیریتی obfuscate نیست (صفر مورد مشکوک در همهٔ فایل‌های اسکن‌شده).

## ۱) TWF 4.28 + سورس آریا (io-stage1-review/src/TWF) — سورس کامل

**معماری:** وب MVC روی هستهٔ T5V3 (شاهد: `TWF 4.28/App/bin`: `T5V3.Model.dll`, `T5V3.DB.dll`, `T5V3.Attendance.dll`, `T5V3.LeaveCalcs.dll`, `T5V3.Report.dll`, `T5V3.ReportGenerator.dll`, `Personnel.dll`, `TD5.dll`, `MyTime3.dll`). لایهٔ دسترسی داده با Dapper در `TD5.dll` (شاهد: `DapQuery`, `AddQuery`, `DeleteCalendar`).

**۱-۱ موجودیت‌های دامنه (سورس):** `TWF.Domain/Models/`: `LeaveBalanceModels`, `TwfDomainDtos`, `PasswordPolicyModels`, `T5OrganizationModels`, `TwfAppSettings`, `UserSessionIdentity`, `WorkflowEngineModels`; `ValueObjects/JalaliDate`, `TimeDurationHelper`; `Licensing/ILicenseService`, `LicenseStatus`; رابط‌ها: `IT5BackendAdapter`, `ITwfDataRepository`, `IAuthTokenService`, `IAppSettingsService`.

**۱-۲ فرم‌ها/صفحات:** ۳۰ پوشهٔ View در `TWF 4.28/App/Views`: AcceptRequest, ConstOT, DailyDuty, DailyLeave, Deputy, Early, EditRequest, Home, HourlyDuty, HourlyLeave, Late, LeavesReport, LocationReport, Login, OverTime, OverTimeReport, PersonnelStatus, Record, RemainLeave, Report, Request, Reward, SelectArea, Settings, ShowRequest, ShowUsersRequest, Undo, ViewR, Shared.

**۱-۳ زیرفرم/Tab:** هر پوشهٔ View شامل Index/جزئیات (74 cshtml در سورس)؛ Tabs در Settings و Record.

**۱-۴ پاپ‌آپ/دیالوگ:** به‌جای Modal — صفحات مجزا (AcceptRequest، EditRequest، Undo) و `NotifyController`/`NotificationManager.cs` برای پیام‌ها.

**۱-۵ منو/ناوبری:** `Shared/` (_Layout) با صف(actions) هر بخش؛ مسیرها = نام کنترلرها.

**۱-۶ جریان End-to-End:** ثبت درخواست (`RequestController` — ۹ اکشن، ۸ نوع درخواست: DailyLeave/HourlyLeave/DailyDuty/HourlyDuty/OverTime/ConstOT/Late/Early/Reward) → `WorkflowEngineService` (auto-finalize) → `AcceptRequestController`/`UndoController` → `LeaveBalanceService` (مرز سال شمسی) → گزارش‌ها (`ReportController`, `LeavesReportController`, `OverTimeReportController`, `RemainLeaveController`, `PersonnelStatusController`, `LocationReportController`).

**۱-۷ سرویس‌ها:** `TWF.Application/`: `Leaves/LeaveBalanceService.cs`, `Workflow/WorkflowEngineService.cs`, `Security/SessionManager.cs`, `Security/PasswordPolicyService.cs`, `Organization/T5OrganizationAccessService.cs`.

**۱-۸ امنیت:** SessionManager + IAuthTokenService + PasswordPolicyService؛ **Licensing** (`ILicenseService`)؛ بدون RBAC با پرمیششن دانه‌ریز.

**۱-۹ دستگاه:** اتصال مستقیم ندارد — از دیتابیس T5V3 می‌خواند (`IT5BackendAdapter`)؛ کارت‌ها/تردد را T5V3/Link5 پر می‌کنند.

## ۲) T5Web [3.5.7] — وب precompiled (Razor خوانا + DLL)

**شواهد:** `App/PrecompiledApp.config`؛ کنترلرها در `bin/T5V3Web.dll` (۱۰۱۸ identifier)؛ هستهٔ مشترک T5V3 + `TC5.dll` (پردازشگرها) + `TMisc5.dll` + `Personnel.dll` + `PersianDate.dll`.

**۲-۱ فرم‌ها/صفحات:** **۶۷ پوشهٔ View**: Account, AssignRule, AssignShift, Calendar, Card, CardexReport, Certificate, **ChangeLock**, CustomWF, DailyReport, Dashboard, DashboardItem, Department, DeptsChart, Deputy, **Device, DeviceOperations**, EditLateEarly, EmpType, ExitReport, ExitType, Export, **Gate**, GridReport, Home, ImportFromExcel, InsertDailyLeave, InsertRecord, LateEarlyReport, LeaveDescription, LeaveType, Manager, OverTimeReport, Parameters, Permit, PersonnelStatus, PersonnelStatusReport, Ramezan, Records, RemainedLeavesReport, ReportDatas, ReportDesign, ReportFilter, ReportTablix, ReportTablixColumn, ReportTextbox, ReportTitles, RequestsReport, Rule, Schedule, Shared, Shift, ShowReport, ShowUsersRequest, SumReport, TempSchedules, Title, TotalControl, **TransferRemainedLeave**, **USBManagement**, UserAddProperty, UserInfo, UserReport, ViewSystemLogs, Workflow, iclock.

**۲-۲ کنترلرها (از متادیتای `T5V3Web.dll` — ۶۴ عدد):** عین فهرست بالا + `GroupAssignShiftController`, `UserSelectionController`, **`iclockController`** (endpoint پروتکل PUSH دستگاه ZKTeco) — فهرست کامل در `inventory.csv`.

**۲-۳ پردازشگرها (`TC5.dll`):** `CreateDailyLeaveProcessor`, `CreateFreeOverTimeProcessor`, `CreateHolidayProcessor`, `CreateRecordProcessor`, `CreateRuleProcessor`, `CreateShiftProcessor`, `DutyDaily/DutyHourly`, `LeaveDaily/LeaveHourly`, `GetPersonnelStatus`, `GetFreeOverTimesLen`, `IsInRamz` (تقویم رمضان).

**۲-۴ مدل امنیت:** Account/Permit/Manager/ChangeLock — قفل‌شدن/گشت‌زنی سطح دسترسی؛ `ViewSystemLogs`.

**۲-۵ دستگاه/درب:** `Device`, `DeviceOperations`, `Gate`, `ChangeLock`, `USBManagement`, `iclock` (PUSH) — شواهد صفحات مدیریت دستگاه و کنترل درب.

## ۳) T5V3.9.2 — دسکتاپ WinForms (باینری .NET، بدون obfuscation)

**۳-۱ موجودیت‌ها (`T5V3.Model.dll`, ۶۴۶ identifier):** AreaModel, AreaPointModel (نقشه/مکان!), AppRuleModel, AppRules, Calendar, CalendarHoliday, Card, Certificate, ChartTitle, CommPassword, AutoBackupDB, AutoDeleteRecordsAfterDownload, AutoDownload, AllowDevices/AllowUsers/AllowOverTimes, BadgeNumber, EmpType, PushServer...

**۳-۲ هستهٔ محاسبه:** `T5V3.LeaveCalcs.dll`: `ADayRemain/ADayResult`, `CalculateRemain`, `DailyLeave`, `HourlyTaken`, `YearLeaveRemain`, `TotalTaken`, `InShiftExit`; `T5V3.Attendance.dll` (۳۶۴); `T5V3.DB.dll` (۶۳۱ — EntityManagers); `T5V3.Report.dll` (۸۸۳); `T5V3.ReportGenerator.dll` (۵۲۷).

**۳-۳ فرم‌ها (`T5V3.exe`, ۱۶۷ identifier):** `Form`, `ShowSplash`, `Program/Main`, `SettingsManager`, `DBManager`, `HTTPPushServer`, `ConfigPushServer`, `Push`, `PersonnelSystem`, `Device`.

**۳-۴ قفل نرم‌افزاری (شاهد صریح):** `CheckLockDB`, `CheckLockResult`, `LockCheker`, `LockBlocking`, `LockIsBlocked`, `LockNotFound`, `LockResult`, `BadLockData`, `ErrorLock`, `DeviceCountLimit`, `PersonnelCountLimit` + رمزگشایی AES (`AesManaged`, `DecryptStringFromBytes`).

## ۴) Link5 v4.0.4 — پرسنل/دستگاه (باینری .NET + سرویس)

**۴-۱ برنامهٔ اصلی `Link5.exe` (۱۰۸۲ identifier):** مدیریت دستگاه و کاربر: `ActiveDevices`, `AllDeviceData`, `AllUserTable`, `ADManage`, `AttReq*` (درخواست تردد), `AddCommandGroup/History/List/Sch`, `AddUploadCommand`, `AddMapping`, `BadgeNumber`, رشته‌های hash-مانند AES (کلیدهای هاردکد ۶۴-hex).

**۴-۲ سرویس‌ها:** `Comm5Serv v3.exe` (۸۸۳ — سرویس ارتباط دستگاه), `Link5.CommandListener.dll` (۹۹), `ServiceManager.dll` (۱۷۵), `Link5.Configurator.exe` (۲۳۸), `Link5.Service.exe` + سه InstallLog (`Link5.Service`, `PushServicev2`, `PushV3 Service`).

**۴-۳ ابزار دستگاه `Device.dll` (۲۰۸):** `EnableDevice`, `ConnectUDPAndReadAllRecords`, `ConnectAndReadAllRecords`, `GenerateDataAndCheckSum`, `GenerateComCodeCheckSum`, `CommandOrder`, `ExtraCommands`, `ClearTimeRecords`, `ExtractRecordFromBlock`, `MachineNumber`.

**۴-۴ امنیت/بایومتریک:** `Bio5Securityv2.dll` (۱۸۲): پیاده‌سازی کامل AES (KeyExpansion, AddRoundKey, InvMixColumns, InvShiftRows, InvSubBytes), `CheckAuthenticate`, `NetAuthenticate`, `CreateUserKey`, `GetSerialNumber`, `DecodeQueryResponseAES`, `AxARM` (Interop ماژول ARM)؛ `Bode5Crypto.dll` (۷۰): `Bode5EncryptS/Bode5DecryptS`, `Rfc2898DeriveBytes`, `ToPersianEncode/ToEnglishEncode`.

**۴-۵ اسکیمای دیتابیس:** `Link5.sql`, `Personnel.sql` (۲ فایل SQL متنی — خوانا).

## ۵) SDK 6.3.1.45 و NewPack — native/COM

- `zkemkeeper.dll`: **COM** (شاهد رشتهٔ `zkemkeeper.ZKEM`, `CZKEM Object`, `.?AVCZKEM@@` — ATL)؛ ZKTeco StandAlone COM SDK.
- `plcommpro.dll`: native؛ Exportهای خوانده‌شده: `ConnectExt`, `ConnectParam`, `GetDeviceData`, `GetDeviceDataCount`, `GetDeviceFileData`, `GetDeviceFileDataToPath`, `GetDeviceParam`, `GetAttLogFileData` (+ واریان‌های pl/tcp/usb/rs).
- `Tiny.ocx` / `Tinyx64(ver5.6.8).ocx`: کنترل ActiveX بایومتریک؛ `Interop.TINYLib.dll` managed interop در NewPack.
- فایل‌های native با CLR متادیتا نیستند → «از طریق Export/رشته استخراج شد».

## جمع شواهد

همهٔ ادعاهای بالا با نام فایل/کلاس/Export پشتیبانی شده‌اند؛ لیست کامل هر DLL در خروجی raw اسکن (ابزار موقت `/tmp`) و چکیدهٔ معنادار در `inventory.csv` ثبت می‌شود.
