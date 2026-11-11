# فاز ۴ — Gap Register (ثبت شکاف‌ها)

هیچ شکافی بدون شاهد یا اولویت نیست. نسخهٔ ماشین‌خوان: `gap-register.csv`.

| شناسه | شکاف | سیستم مرجع | شاهد در مرجع | وضعیت در IO/ReCode | شاهد در IO/ReCode | شدت | فاز پیشنهادی |
|---|---|---|---|---|---|---|---|
| G-01 | گردش و گزارش تأخیر/تعجیل (Late/Early) | TWF | LateController.cs, EarlyController.cs (۴ اکشن هرکدام), T5Web LateEarlyReportController | 🔴 غایب | — | بحرانی | R2 |
| G-02 | وضعیت لحظه‌ای پرسنل (حاضر/غایب/در مأموریت) | TWF+T5Web | PersonnelStatusController.cs (هر دو), TC5.dll GetPersonnelStatus | 🔴 غایب | — | بالا | R3 |
| G-03 | کارتابل مرکزی درخواست‌های همهٔ کاربران | TWF+T5Web | ShowUsersRequestController.cs (هر دو) | 🔴 غایب | — | متوسط | R3 |
| G-04 | انتقال ماندهٔ مرخصی بین سال | T5Web | TransferRemainedLeaveController, T5V3.LeaveCalcs YearLeaveRemain | 🔴 غایب | — | متوسط | R4 |
| G-05 | پاداش/جریمه | TWF | RewardController.cs (۲ اکشن), View Reward | ⚪ نیازمند تصمیم | — | متوسط | تصمیم کاربر |
| G-06 | گزارش مکان/ناحیه | TWF+T5V3 | LocationReportController.cs, T5V3.Model AreaModel/AreaPointModel | ⚪ نیازمند تصمیم | — | متوسط | تصمیم کاربر |
| G-07 | اتصال واقعی دستگاه/درب + PUSH | T5Web+Link5 | iclockController, GateController, Comm5Serv v3.exe, Device.dll, zkemkeeper/plcommpro | ⚪ بدون تصمیم (سند رسمی) | مدل داده کامل: EventSources/Mappings/RawEvents | بحرانی* | تصمیم کاربر |
| G-08 | خروجی فایل Excel/PDF | T5Web+T5V3 | ExportController, ReportTablix*, T5V3.ReportGenerator.dll, ReportViewer DLLها | 🟡 فقط داده | ReportBuilder preflight | بالا | R5 |
| G-09 | گزارش‌ساز داینامیک کاربر-تعریف | T5Web | ReportDesignController, ReportTablixController, ReportFilterController, ReportDatasController | 🟡 اسکلت | ReportBuilder.html preflight | متوسط | R5 |
| G-10 | ورود گروهی از Excel | T5Web | ImportFromExcelController | 🔴 غایب | — | متوسط | R5 |
| G-11 | Backup/Restore خودکار | T5V3+Link5 | T5V3.Model AutoBackupDB, Personnel.dll BACKUP/BackupDevcie | 🔴 غایب | — | بالا | R6 |
| G-12 | محاسبهٔ خودکار اضافه‌کار/کارکرد از تردد | TC5(T5V3) | TC5.dll CreateFreeOverTimeProcessor, GetFreeOverTimesLen, InLessWork | 🟡 فقط گردش دستی | LeaveRequests Category=1 | بالا | R2 |
| G-13 | لایسنس/قفل نرم‌افزاری/سقف دستگاه-پرسنل | T5V3+Link5+TWF | T5V3.exe CheckLockDB/LockCheker/DeviceCountLimit/PersonnelCountLimit, Bio5Securityv2 InitLock, TWF ILicenseService | ⚪ بدون تصمیم — قید صریح کاربر | — | — | تصمیم کاربر |
| G-14 | کاردکس/کارت/تصدیق‌نامه | T5Web | CardexReportController, CardController, CertificateController | 🔴 غایب | — | پایین | R7 |
| G-15 | تقویم رمضان و شیفت ویژه | T5Web | RamezanController, TC5 IsInRamz | 🔴 غایب | — | پایین | R7 |
| G-16 | شیفت موقت/گروهی | T5Web | TempSchedulesController, GroupAssignShiftController | 🟡 جایگزین و انتساب داریم | scheduling/replacements | پایین | R7 |
| G-17 | فیلد دلخواه کاربر (UserAddProperty) | T5Web | UserAddPropertyController | 🔴 غایب | — | پایین | R7 |
| G-18 | بازگشت/Undo امن تصمیم | TWF | UndoController.cs (۲ اکشن), View Undo — الگوی GET تقلید ممنوع | 🔴 غایب | CorrectionDecisions یکتا؛ طرح چندتصمیمی لازم | بالا | R1 |
| G-19 | گزارش خروج/مجموع روزانه | T5Web | DailyReportController, ExitReportController, SumReportController | 🟡 | PersonalReports/ExecutiveOverview | متوسط | R3 |
| G-20 | اعلان بلادرنگ (Push) | TWF | NotifyController.cs, NotificationManager.cs | 🟡 polling | Notifications + شمارندهٔ پنل | پایین | R7 |
| G-21 | بستهٔ استقرار و عملیات اجرا (publish/سرویس/TLS/پایش فرایند) | — | — | 🔴 غایب | CI فقط build/test می‌کند؛ خروجی deployable نداریم | بالا | R8 |
| G-22 | ورود اطلاعات اولیهٔ سازمان واقعی (seed) | T5Web | ImportFromExcelController | 🔴 غایب | جز fixtureهای آزمون | بالا | R8 |

**یادداشت شدت‌ها:** G-07 با ستارهٔ بحرانی* یعنی مدل داده و API دریافت در ReCode آماده است و فقط «تصمیم دستگاه/اتصال» کار را فعال می‌کند؛ G-13 طبق قید صریح کاربر («فعلاً هیچ تصمیمی برای قفل سخت‌افزاری یا نرم‌افزاری») خارج از نقشه است. G-18 (Undo) نیازمند تغییر طرح «یک تصمیم یکتا» به «تاریخچهٔ تصمیم‌ها» است. G-21/G-22 برای «رساندن سامانه به حالت اجرایی سازمانی» اضافه شدند — استقرار و ورود دادهٔ واقعی هنوز ساخته نشده و باید فاز R8 شود.