using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TWF.Application.Leaves;
using TWF.Application.Organization;
using TWF.Application.Security;
using TWF.Application.Workflow;
using TWF.Domain.Licensing;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;
using TWF.Infrastructure.Adapters;
using TWF.Infrastructure.Configuration;
using TWF.Infrastructure.Data;
using TWF.Infrastructure.Licensing;
using TWF.Infrastructure.Localization;
using TWF.Infrastructure.Security;
using TWF.Web.Helpers;
using TWF.Web.Security;

namespace TWF.Tests
{
    public static class Program
    {
        private static int _passedTests;
        private static int _failedTests;

        public static int Main(string[] args)
        {
            Console.WriteLine("======================================================================");
            Console.WriteLine("  TWF Enterprise Rewrite - Phase 1, 2, 3, 4 & 5 Verification Suite");
            Console.WriteLine("  (100% TD5 + MyTime3 + Views Rewrite, Zero-Lock & Offline UI)");
            Console.WriteLine("======================================================================");

            // Phase 1 Tests
            RunTest("TEST-01: Zero-Lock Architecture & GlobalHelper Compatibility", TestZeroLockArchitecture);
            RunTest("TEST-02: AES-256 + HMAC-SHA256 Session Token Cryptography (BUG-02 Fix)", TestAesHmacAuthTokenService);
            RunTest("TEST-03: Web.config Settings & Cookie Security Flags (BUG-02/03/05 Fix)", TestAppSettingsAndCookieLifecycle);
            RunTest("TEST-04: Solar Hijri Calendar, Leap Years & Durations (BUG-06 Fix)", TestJalaliCalendarAndDurations);
            RunTest("TEST-05: EF Connection String Parsing & Dual-DB Health (BUG-07a Fix)", TestConnectionStringsAndHealth);
            RunTest("TEST-06: 50-Thread Concurrency Stress Test & Request DTOs (BUG-01/07b/07c Fix)", TestThreadSafetyAndDataRepository);
            RunTest("TEST-07: 260 Persian Strings, Role Authorization & Brute-Force Throttle", TestLocalizationAuthAndThrottle);

            // Phase 2 Tests
            RunTest("TEST-08: T5 Org Chart, Workflow-Aware Subordinates, Guard & Report Access", TestT5OrganizationAndAccessService);
            RunTest("TEST-09: Two-Layer Deputy System (DeputyDA + PersonnelDeputyDA)", TestTwoLayerDeputySystem);
            RunTest("TEST-10: Multi-Step Workflow (AcceptByID1 -> AcceptByID2 -> InsertData -> Undo)", TestMultiStepWorkflowEngine);
            RunTest("TEST-11: Leave Balance Engine, Linked Leaves, Limit1Leave & Leap-Year (BUG-06)", TestLeaveBalanceEngine);
            RunTest("TEST-12: Request Time Cutoffs (MaxAge, LastDay, LockDate) & Traffic Limits", TestRequestTimeAndTrafficLimits);
            RunTest("TEST-13: Password Policy Engine (Force, Periodic Rotation & Complexity - BUG-03)", TestPasswordPolicyService);

            // Phase 3 Tests
            RunTest("TEST-14: TD5 Complete Rewrite & SQL Query Builders (All 6 Controllers & Bug Fixes)", TestPhase3Td5RewriteAndQueryBuilders);
            RunTest("TEST-15: MyTime3 Zero-Lock (LockCheker, CheckLockResult & MyTime3.GlobalHelper)", TestPhase3MyTime3ZeroLock);
            RunTest("TEST-16: MyTime3.Authorization (Encryption AES-256, LoginManager & AuthorizeFilter - BUG-02)", TestPhase3MyTime3Authorization);
            RunTest("TEST-17: MyTime3.Controllers.LoginController & Brute-Force Lockout Flow", TestPhase3LoginControllerFlow);
            RunTest("TEST-18: MyTime3.Controllers.ChangePassController & Mandatory Password Rotation (BUG-03)", TestPhase3ChangePassControllerFlow);
            RunTest("TEST-19: MyTime3.Controllers.SettingsController & TitleChartClass Thread-Safety (BUG-01/03/05)", TestPhase3SettingsAndTitleChartThreadSafety);
            RunTest("TEST-20: Phase 3 Rewritten Razor Views, Offline Industrial UI & Source Completeness Audit", TestPhase3ViewsOfflineUiAndSourceAudit);

            // Phase 4 Tests
            RunTest("TEST-21: HomeController, DescriptionClass & NotificationManager (BUG-01/07a Fix)", TestPhase4HomeDescriptionAndNotification);
            RunTest("TEST-22: DailyLeaveController & RemainLeaveController (BUG-05/06 & Double-Count Fix)", TestPhase4DailyLeaveAndRemainLeave);
            RunTest("TEST-23: HourlyLeaveController (AddLeaveTypes, MaxHourlyInDay & Linked Quota Fix)", TestPhase4HourlyLeaveController);
            RunTest("TEST-24: DailyDutyController & HourlyDutyController (BUG-07 ValidHourlyFree Edit Fix)", TestPhase4DailyAndHourlyDutyControllers);
            RunTest("TEST-25: OverTimeController & ConstOTController (BUG-01 Thread-Safe & Pre-Month Fix)", TestPhase4OverTimeAndConstOTControllers);
            RunTest("TEST-26: LateController, EarlyController & RecordController (ForDate & Traffic Limit Fix)", TestPhase4LateEarlyAndRecordControllers);
            RunTest("TEST-27: RewardController (AuthorizeFilter, Direct vs Multi-Step Workflow)", TestPhase4RewardController);
            RunTest("TEST-28: RequestController Unified Hub, OverTime Edit & Mobile API (Zero-Lock)", TestPhase4UnifiedRequestController);
            RunTest("TEST-29: EditRequestController (Index, Edit Routing, Delete & CheckRequestTime)", TestPhase4EditRequestController);
            RunTest("TEST-30: Phase 4 Rewritten Razor Views (29 Views) & Offline Industrial UI Audit", TestPhase4ViewsAndControllerCompleteness);

            // Phase 5 Tests
            RunTest("TEST-31: AcceptRequestController (Multi-Step, Deputy, Requester Balance BUG-08 & Mobile Cancel BUG-09)", TestPhase5AcceptRequestController);
            RunTest("TEST-32: DeputyController (Index, Add, Delete, GetState Mobile API & Two-Layer Deputy)", TestPhase5DeputyController);
            RunTest("TEST-33: UndoController (Index, Undo, Operational Data Rollback & Null Guards)", TestPhase5UndoController);
            RunTest("TEST-34: ShowRequestController (Index, GetRequests & Single Undo Suffix Fix)", TestPhase5ShowRequestController);
            RunTest("TEST-35: ShowUsersRequestController & ViewRController (Admin Null-Safe & HTML-Encoded Status)", TestPhase5ShowUsersRequestAndViewR);
            RunTest("TEST-36: PersonnelStatusController & LeavesReportController (rType 1..4 & Null-Safe isObs)", TestPhase5PersonnelStatusAndLeavesReport);
            RunTest("TEST-37: LocationReportController & SelectAreaController (Invalid Date Fix & Offline Polygon Editor)", TestPhase5LocationReportAndSelectArea);
            RunTest("TEST-38: NotifyController (XML Serialization, RewardAndPenalty Title Fix & SetViewed)", TestPhase5NotifyController);
            RunTest("TEST-39: OverTimeReportController & ReportController (Zero-Lock API, BUG-10 OverTimeConst Fix & Export)", TestPhase5OverTimeAndReportControllers);
            RunTest("TEST-40: 100% Source & View Rewrite Completeness Audit (All 105 C# Files & All 74 Offline Views)", TestPhase5FullProjectCompletenessAudit);
            RunTest("TEST-41: Aria Offline UI Contract (Self-Contained Assets, CSS Class Coverage & Request Dialog Wiring)", TestAriaOfflineUiContract);
            RunTest("TEST-42: SchemaGuard Contract Logic (Missing Table, Missing Column, Full Match, Unreachable DB)", TestSchemaGuardEvaluation);
            RunTest("TEST-43: SchemaGuard Wiring (Startup Probe, Global Filter, Diagnostics View & Project)", TestSchemaGuardWiring);
            RunTest("TEST-44: Pinned Engine Enforcement (Pin File, Project HintPaths, Build Script)", TestPinnedEngineEnforcement);
            RunTest("TEST-45: Diagnostics Endpoints & Persian Failure Page (No Stack Traces Leaked)", TestDiagnosticsEndpoints);
            RunTest("TEST-46: Schema Manifest Conformance (No Invented Tables/Columns in Hand-Written SQL)", TestSchemaManifestConformance);
            RunTest("TEST-47: Evidence Rule (Guard May Only Block on Proven Objects)", TestGuardEvidenceRule);

            Console.WriteLine("----------------------------------------------------------------------");
            Console.WriteLine($"  Summary: {_passedTests} Passed, {_failedTests} Failed");
            Console.WriteLine("======================================================================");

            return _failedTests == 0 ? 0 : 1;
        }

        private static void RunTest(string name, Action testAction)
        {
            try
            {
                testAction();
                _passedTests++;
                Console.WriteLine($"[PASS] {name}");
            }
            catch (Exception ex)
            {
                _failedTests++;
                Console.WriteLine($"[FAIL] {name}");
                Console.WriteLine($"       Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"       Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                }
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        // =====================================================================
        // PHASE 1 TESTS
        // =====================================================================

        private static void TestZeroLockArchitecture()
        {
            ILicenseService license = UnrestrictedLicenseService.Instance;
            Assert(license.IsLicenseValid(), "UnrestrictedLicenseService.IsLicenseValid must return true.");
            Assert(license.GetMaxPersonnelCount() == int.MaxValue, "MaxPersonnelCount must be int.MaxValue.");
            Assert(license.IsPersonnelCountWithinLimit(0), "0 personnel must be allowed.");
            Assert(license.IsPersonnelCountWithinLimit(500_000), "500,000 personnel must be allowed (zero lock cap).");
            Assert(license.IsTimeAppActive(), "TimeApp must be active without hardware lock.");
            Assert(license.IsFullSizeLogoAllowed(), "FullSizeLogo must be allowed without hardware lock.");

            LicenseStatus status = license.GetStatus();
            Assert(status.IsValid && status.Mode == "ZeroLock-Unrestricted", "LicenseStatus mode mismatch.");

            GlobalHelper.Configure(license);
            Assert(GlobalHelper.LockIsOK, "GlobalHelper.LockIsOK must be true.");
            Assert(GlobalHelper.PersonnelCount == int.MaxValue, "GlobalHelper.PersonnelCount must be int.MaxValue.");
            Assert(GlobalHelper.FulLogoSize, "GlobalHelper.FulLogoSize must be true.");
            Assert(GlobalHelper.WithTimeApp(), "GlobalHelper.WithTimeApp() must be true.");
        }

        private static void TestAesHmacAuthTokenService()
        {
            var tokenService = new AesHmacAuthTokenService("Test-Master-Secret-Phase1-2026");
            var now = new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
            var expires = now.AddHours(12);

            var payload = new SessionTokenPayload(
                badgeNumber: 1042,
                userName: "a.rahimi",
                role: "user",
                issuedAtUtc: now,
                expiresAtUtc: expires);

            string token1 = tokenService.ProtectToken(payload);
            string token2 = tokenService.ProtectToken(payload);

            Assert(!string.IsNullOrWhiteSpace(token1), "Protected token must not be empty.");
            Assert(token1 != token2, "Random IV must produce distinct ciphertexts for identical payloads.");

            Assert(tokenService.TryUnprotectToken(token1, now.AddHours(1), out SessionTokenPayload unpacked),
                "Valid token must decrypt and verify.");
            Assert(unpacked.BadgeNumber == 1042, "BadgeNumber mismatch.");
            Assert(unpacked.UserName == "a.rahimi", "UserName mismatch.");
            Assert(unpacked.Role == "user", "Role mismatch.");
            Assert(unpacked.IssuedAtUtc == now && unpacked.ExpiresAtUtc == expires, "Timestamps mismatch.");

            char[] chars = token1.ToCharArray();
            int mid = chars.Length / 2;
            chars[mid] = chars[mid] == 'A' ? 'B' : 'A';
            string tampered = new string(chars);
            Assert(!tokenService.TryUnprotectToken(tampered, now.AddHours(1), out _),
                "Tampered token must be rejected by constant-time HMAC verification.");

            var otherKeyService = new AesHmacAuthTokenService("Different-Master-Secret");
            Assert(!otherKeyService.TryUnprotectToken(token1, now.AddHours(1), out _),
                "Token encrypted with a different master key must be rejected.");
        }

        private static void TestAppSettingsAndCookieLifecycle()
        {
            string sourceWebConfig = Path.GetFullPath("TWF 4.28/App/Web.config");
            Assert(File.Exists(sourceWebConfig), $"Source Web.config not found at {sourceWebConfig}");

            string tempConfig = Path.Combine(Path.GetTempPath(), $"TWF_Test_Web_{Guid.NewGuid():N}.config");
            try
            {
                File.Copy(sourceWebConfig, tempConfig, overwrite: true);
                var settingsService = new AppSettingsService(tempConfig);
                TwfAppSettings initial = settingsService.GetSettings();

                Assert(initial.CookiesTime == 20, $"Expected CookiesTime=20 from Web.config, got {initial.CookiesTime}");
                Assert(initial.ValidLateEarly == true, "Expected ValidLateEarly=true");
                Assert(initial.HoursOfDays == 8, "Expected HoursOfDays=8");

                initial.CookiesSecure = true;
                initial.CookiesTime = 16;
                initial.ChangePassLimit = 45;
                initial.ChangePassForce = true;
                initial.ExportReport = false;
                settingsService.SaveSettings(initial);

                TwfAppSettings reloaded = settingsService.ReloadSettings();
                Assert(reloaded.CookiesSecure == true, "BUG-02 Regression: CookiesSecure must round-trip as true.");
                Assert(reloaded.CookiesTime == 16, "CookiesTime must round-trip as 16.");
                Assert(reloaded.ChangePassLimit == 45, "ChangePassLimit must remain 45.");
                Assert(reloaded.ChangePassForce == true, "BUG-03 Regression: ChangePassForce must round-trip as true without colliding with ChangePassLimit.");
                Assert(reloaded.ExportReport == false, "BUG-05 Regression: ExportReport must round-trip as false.");

                var tokenService = new AesHmacAuthTokenService("Session-Test-Key");
                var repo = new TwfDataRepository("Data Source=.;Initial Catalog=Personnel;", "Data Source=.;Initial Catalog=ReportT5V3;");
                var sessionManager = new SessionManager(tokenService, settingsService, repo);

                var loginTime = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);
                SessionCookieDescriptor cookie = sessionManager.CreateSessionCookie(0, "admin", "admin", loginTime);

                Assert(cookie.CookieName == "mytimeauth", "Cookie name must be mytimeauth.");
                Assert(cookie.HttpOnly == true, "BUG-02 Regression: HttpOnly must be true.");
                Assert(cookie.Secure == true, "BUG-02 Regression: Secure flag must reflect CookiesSecure=true.");
                Assert(cookie.ExpiresAtUtc == loginTime.AddHours(16), "BUG-02 Regression: ExpiresAtUtc must use CookiesTime (16h).");

                Assert(sessionManager.TryResolveSessionIdentity(cookie.Value, loginTime.AddHours(10), out UserSessionIdentity adminId),
                    "BUG-02 Regression: Session at +10h must be valid when CookiesTime=16h.");
                Assert(adminId.IsAdmin && adminId.BadgeNumber == 0, "Admin identity must resolve properly.");

                Assert(!sessionManager.TryResolveSessionIdentity(cookie.Value, loginTime.AddHours(16).AddSeconds(1), out _),
                    "Session after CookiesTime (16h+1s) must be expired.");
            }
            finally
            {
                if (File.Exists(tempConfig))
                    File.Delete(tempConfig);
            }
        }

        private static void TestJalaliCalendarAndDurations()
        {
            Assert(JalaliDateHelper.IsLeapYear(1399), "1399 must be a Solar Hijri leap year.");
            Assert(JalaliDateHelper.GetDaysInMonth(1399, 12) == 30, "Esfand 1399 must have 30 days.");
            Assert(!JalaliDateHelper.IsLeapYear(1400), "1400 must not be a leap year.");
            Assert(JalaliDateHelper.GetDaysInMonth(1400, 12) == 29, "Esfand 1400 must have 29 days.");
            Assert(JalaliDateHelper.IsLeapYear(1403), "1403 must be a Solar Hijri leap year.");
            Assert(!JalaliDateHelper.IsLeapYear(1404), "1404 must not be a leap year.");
            Assert(!JalaliDateHelper.IsLeapYear(1405), "1405 must not be a leap year.");

            DateTime esfand30_1399 = JalaliDateHelper.ParseJalali("1399/12/30");
            DateTime farvardin1_1400 = JalaliDateHelper.ParseJalali("1400/01/01");
            DateTime esfand29_1400 = JalaliDateHelper.ParseJalali("1400/12/29");

            Assert(esfand30_1399 == new DateTime(2021, 3, 20), "1399/12/30 must equal 2021-03-20.");
            Assert(farvardin1_1400 == new DateTime(2021, 3, 21), "1400/01/01 must equal 2021-03-21.");
            Assert(farvardin1_1400.DayOfYear == 80, "2021-03-21 has DayOfYear=80 (which triggered BUG-06 in legacy TWF).");

            Assert(JalaliDateHelper.GetPersianYearStart(farvardin1_1400) == new DateTime(2021, 3, 21),
                "BUG-06 Regression: GetPersianYearStart(1400/01/01) must return 2021-03-21.");
            Assert(!JalaliDateHelper.IsSamePersianYear(esfand30_1399, farvardin1_1400),
                "1399/12/30 and 1400/01/01 are in different Persian years.");
            Assert(JalaliDateHelper.IsSamePersianYear(farvardin1_1400, esfand29_1400),
                "1400/01/01 and 1400/12/29 are in the same Persian year.");

            Assert(JalaliDateHelper.TryParseJalali("۱۴۰۵/۰۸/۰۲ ۱۴:۳۰", out DateTime parsedPersian),
                "Must parse Persian digits date-time.");
            Assert(JalaliDateHelper.ToJalaliDateTimeString(parsedPersian) == "1405/08/02 14:30",
                "Formatted Jalali date-time must match 1405/08/02 14:30.");

            Assert(TimeDurationHelper.TryParseDurationMinutes("۱۴۵:۳۰", out int mins) && mins == (145 * 60 + 30),
                "Must parse multi-day duration 145:30 with Persian digits.");
            Assert(TimeDurationHelper.MinutesToTimeString(mins) == "145:30",
                "MinutesToTimeString must format 145:30.");
        }

        private static void TestConnectionStringsAndHealth()
        {
            string sourceWebConfig = Path.GetFullPath("TWF 4.28/App/Web.config");
            var (personnelConn, t5Conn) = SqlConnectionStringHelper.ReadFromWebConfig(sourceWebConfig);

            Assert(!string.IsNullOrWhiteSpace(personnelConn) && personnelConn.IndexOf("Initial Catalog=Personnel", StringComparison.OrdinalIgnoreCase) >= 0,
                $"Personnel connection string extraction failed: {personnelConn}");
            Assert(!personnelConn.StartsWith("metadata=", StringComparison.OrdinalIgnoreCase),
                "EF metadata prefix must be stripped from Personnel connection string.");

            Assert(!string.IsNullOrWhiteSpace(t5Conn) && t5Conn.IndexOf("initial catalog=ReportT5V3", StringComparison.OrdinalIgnoreCase) >= 0,
                $"T5 connection string extraction failed: {t5Conn}");
            Assert(!t5Conn.StartsWith("metadata=", StringComparison.OrdinalIgnoreCase),
                "EF metadata prefix must be stripped from T5 connection string.");

            var repo = new TwfDataRepository(personnelConn, string.Empty);
            DatabaseHealthStatus partialHealth = repo.CheckDatabaseHealth();
            Assert(partialHealth.IsPersonnelDbConnected && !partialHealth.IsT5DbConnected && !partialHealth.IsHealthy,
                "BUG-07a Regression: Missing T5 connection must report IsT5DbConnected=false and IsHealthy=false.");
        }

        private static void TestThreadSafetyAndDataRepository()
        {
            string sourceWebConfig = Path.GetFullPath("TWF 4.28/App/Web.config");
            var settingsService = new AppSettingsService(sourceWebConfig);
            var tokenService = new AesHmacAuthTokenService("Concurrency-Test-Key");
            var repo = new TwfDataRepository("Data Source=.;Initial Catalog=Personnel;", "Data Source=.;Initial Catalog=ReportT5V3;");

            DateTime targetDate = new DateTime(2026, 10, 24, 8, 30, 0);

            for (int i = 1; i <= 50; i++)
            {
                long badge = 1000 + i;
                repo.SeedUserTitleProfile(new UserTitleProfileDto
                {
                    BadgeNumber = badge,
                    UserName = $"user_{badge}",
                    FirstName = $"کاربر",
                    LastName = $"شماره {badge}",
                    TitleId = i,
                    TitleName = $"سمت {i}",
                    UpperTitleId = i == 1 ? 0 : 1,
                    UpperTitleName = "مدیر ارشد",
                    DepartmentId = 10,
                    DepartmentName = "فناوری اطلاعات",
                    IsObserver = (i % 10 == 0)
                });
                repo.SeedDescription(badge, targetDate, isHourly: true, description: $"مرخصی ساعتی کاربر {badge}");
                repo.SeedRequest(new RequestRecordDto
                {
                    RequestId = i,
                    BadgeNumber = badge,
                    RequestDate = targetDate,
                    WorkflowType = (int)TwfWorkflowType.HourlyLeave,
                    SubType = 1,
                    FromTime = targetDate,
                    ToTime = targetDate.AddHours(2),
                    RequestDurationMinutes = 120,
                    RequestState = (int)TwfRequestState.Waiting,
                    Description = $"درخواست {badge}"
                });
            }

            var sessionManager = new SessionManager(tokenService, settingsService, repo);
            var authManager = new SessionAuthManager(sessionManager);

            var tasks = new Task[50];
            for (int t = 0; t < 50; t++)
            {
                int workerIndex = t + 1;
                tasks[t] = Task.Run(() =>
                {
                    long expectedBadge = 1000 + workerIndex;
                    for (int iter = 0; iter < 40; iter++)
                    {
                        var requestItems = new Hashtable();
                        SessionCookieDescriptor cookie = authManager.SignIn(
                            expectedBadge,
                            $"user_{expectedBadge}",
                            "user",
                            requestItems);

                        UserSessionIdentity resolved = authManager.GetCurrentUser(cookie.Value, requestItems);
                        Assert(resolved != null, $"Resolved identity was null for {expectedBadge}");
                        Assert(resolved.BadgeNumber == expectedBadge,
                            $"BUG-01 Cross-thread identity leak! Expected {expectedBadge}, got {resolved.BadgeNumber}");
                        Assert(resolved.TitleId == workerIndex,
                            $"BUG-01 Cross-thread TitleId leak! Expected {workerIndex}, got {resolved.TitleId}");

                        string desc = repo.GetLeaveOrDutyDescription(expectedBadge, targetDate, isHourly: true);
                        Assert(desc == $"مرخصی ساعتی کاربر {expectedBadge}",
                            $"BUG-01b Description leak! Expected 'مرخصی ساعتی کاربر {expectedBadge}', got '{desc}'");

                        IReadOnlyList<RequestRecordDto> reqs = repo.GetRequests(
                            expectedBadge,
                            targetDate.AddDays(-1),
                            targetDate.AddDays(1),
                            TwfWorkflowType.HourlyLeave);
                        Assert(reqs.Count == 1, $"Expected 1 request for {expectedBadge}, got {reqs.Count}");

                        DateTime firstRead = reqs[0].FromTime;
                        DateTime secondRead = reqs[0].FromTime;
                        Assert(firstRead == targetDate && secondRead == targetDate,
                            "BUG-07b Regression: RequestRecordDto.FromTime mutated on getter access!");
                    }
                });
            }

            Task.WaitAll(tasks);

            var passChangeTime = new DateTime(2026, 10, 24, 12, 0, 0);
            repo.RecordPasswordChange(1025, passChangeTime);
            PasswordChangeHistoryDto history = repo.GetPasswordChangeHistory(1025);
            Assert(history != null && history.HasChangedPassword && history.LastChangeDate == passChangeTime,
                "PasswordChangeHistoryDto must record and retrieve accurately.");
        }

        private static void TestLocalizationAuthAndThrottle()
        {
            IReadOnlyDictionary<string, string> allStrings = PersianString.GetAll();
            Assert(allStrings.Count == 260, $"Expected 260 Persian strings, found {allStrings.Count}");
            Assert(PersianString.Accept == "تایید", "PersianString.Accept mismatch.");
            Assert(MyTime3.Content.PersianString.AcceptRequest == "تایید درخواست", "MyTime3.Content.PersianString facade mismatch.");

            var throttle = new LoginAttemptThrottle(captchaThreshold: 3, lockoutThreshold: 5, lockoutDuration: TimeSpan.FromMinutes(10));
            var now = DateTime.UtcNow;
            string clientKey = "192.168.1.50:a.rahimi";

            Assert(!throttle.RequiresCaptcha(clientKey, globalCaptchaSetting: false, utcNow: now), "Should not require captcha initially.");
            throttle.RecordFailure(clientKey, now);
            throttle.RecordFailure(clientKey, now.AddSeconds(5));
            Assert(!throttle.RequiresCaptcha(clientKey, globalCaptchaSetting: false, utcNow: now.AddSeconds(6)), "Should not require captcha at 2 failures.");

            throttle.RecordFailure(clientKey, now.AddSeconds(10));
            Assert(throttle.RequiresCaptcha(clientKey, globalCaptchaSetting: false, utcNow: now.AddSeconds(11)), "Must require captcha at 3 failures.");

            throttle.RecordFailure(clientKey, now.AddSeconds(15));
            throttle.RecordFailure(clientKey, now.AddSeconds(20));
            Assert(throttle.IsLockedOut(clientKey, now.AddSeconds(25), out TimeSpan rem) && rem.TotalMinutes > 9,
                "Must lock out after reaching 5 failed attempts.");

            throttle.RecordSuccess(clientKey);
            Assert(!throttle.IsLockedOut(clientKey, now.AddSeconds(30), out _), "RecordSuccess must clear lockout.");
        }

        // =====================================================================
        // PHASE 2 TESTS
        // =====================================================================

        private static InMemoryT5BackendAdapter CreateSampleEnterpriseBackend()
        {
            var backend = new InMemoryT5BackendAdapter();

            // TitlesChart Hierarchy:
            // Title 1: مدیرعامل (CEO - Root)
            //   |-- Title 2: معاون فنی (VP Engineering, Upper=1)
            //   |     |-- Title 3: سرپرست توسعه (Team Lead, Upper=2)
            //   |           |-- Title 4: کارشناس نرم‌افزار (Developer, Upper=3)
            //   |-- Title 5: مدیر منابع انسانی (HR Manager, Upper=1)
            //   |-- Title 6: انتظامات و نگهبانی (Security Guard, Upper=1, IsObserver=true)
            backend.SeedTitleNode(new OrgTitleNodeDto { TitleId = 1, TitleName = "مدیرعامل", UpperTitleId = 0, DepartmentId = 1, DepartmentName = "مدیریت عامل", IsObserver = false });
            backend.SeedTitleNode(new OrgTitleNodeDto { TitleId = 2, TitleName = "معاون فنی", UpperTitleId = 1, DepartmentId = 2, DepartmentName = "معاونت فنی", IsObserver = false });
            backend.SeedTitleNode(new OrgTitleNodeDto { TitleId = 3, TitleName = "سرپرست توسعه", UpperTitleId = 2, DepartmentId = 2, DepartmentName = "معاونت فنی", IsObserver = false });
            backend.SeedTitleNode(new OrgTitleNodeDto { TitleId = 4, TitleName = "کارشناس نرم‌افزار", UpperTitleId = 3, DepartmentId = 2, DepartmentName = "معاونت فنی", IsObserver = false });
            backend.SeedTitleNode(new OrgTitleNodeDto { TitleId = 5, TitleName = "مدیر منابع انسانی", UpperTitleId = 1, DepartmentId = 3, DepartmentName = "منابع انسانی", IsObserver = false });
            backend.SeedTitleNode(new OrgTitleNodeDto { TitleId = 6, TitleName = "انتظامات و نگهبانی", UpperTitleId = 1, DepartmentId = 4, DepartmentName = "حراست", IsObserver = true });

            // Employees:
            backend.SeedEmployee(new EmployeeSummaryDto { BadgeNumber = 100, UserName = "ceo", FirstName = "حمید", LastName = "رضایی", TitleId = 1, TitleName = "مدیرعامل", DepartmentId = 1, DepartmentName = "مدیریت عامل" });
            backend.SeedEmployee(new EmployeeSummaryDto { BadgeNumber = 200, UserName = "vp", FirstName = "کامران", LastName = "علوی", TitleId = 2, TitleName = "معاون فنی", DepartmentId = 2, DepartmentName = "معاونت فنی" });
            backend.SeedEmployee(new EmployeeSummaryDto { BadgeNumber = 300, UserName = "lead", FirstName = "سارا", LastName = "محمدی", TitleId = 3, TitleName = "سرپرست توسعه", DepartmentId = 2, DepartmentName = "معاونت فنی" });
            backend.SeedEmployee(new EmployeeSummaryDto { BadgeNumber = 400, UserName = "dev", FirstName = "علیرضا", LastName = "رحیمی", TitleId = 4, TitleName = "کارشناس نرم‌افزار", DepartmentId = 2, DepartmentName = "معاونت فنی" });
            backend.SeedEmployee(new EmployeeSummaryDto { BadgeNumber = 500, UserName = "hr", FirstName = "مریم", LastName = "کریمی", TitleId = 5, TitleName = "مدیر منابع انسانی", DepartmentId = 3, DepartmentName = "منابع انسانی" });
            backend.SeedEmployee(new EmployeeSummaryDto { BadgeNumber = 600, UserName = "guard", FirstName = "بهرام", LastName = "صادقی", TitleId = 6, TitleName = "انتظامات و نگهبانی", DepartmentId = 4, DepartmentName = "حراست" });

            // Multi-step Workflow (T5 WorkflowDA):
            // Developer (Title 4) has a 2-step workflow:
            //   Step 1 (OrderIndex=1): Team Lead (Title 3)
            //   Step 2 (OrderIndex=2): HR Manager (Title 5)
            // Note: Team Lead (Title 3) has NO custom workflow rows, so it falls back to TitlesChart.UpperTitleId = VP Engineering (Title 2)!
            backend.SeedWorkflowStep(new WorkflowStepConfigDto { WorkflowId = 1, TitleId = 4, AccepterTitleId = 3, Operation = -1, OrderIndex = 1 });
            backend.SeedWorkflowStep(new WorkflowStepConfigDto { WorkflowId = 2, TitleId = 4, AccepterTitleId = 5, Operation = -1, OrderIndex = 2 });

            // Layer-1 Allowed Deputy Titles (T5 DeputyDA):
            // HR Manager (Title 5) is allowed to delegate deputy authority to VP Engineering (Title 2)
            // VP Engineering (Title 2) is allowed to delegate deputy authority to Team Lead (Title 3)
            backend.SeedAllowedDeputyTitle(principalTitleId: 5, allowedDeputyTitleId: 2);
            backend.SeedAllowedDeputyTitle(principalTitleId: 2, allowedDeputyTitleId: 3);

            // Reports & ReportAccess:
            backend.SeedReport(new ReportDefinitionDto { ReportId = 10, ReportName = "گزارش کارکرد ماهانه", AllowedTitleIds = new List<int> { 1, 2, 3, 5 } });
            backend.SeedReport(new ReportDefinitionDto { ReportId = 20, ReportName = "گزارش مدیریتی اضافه‌کاری", AllowedTitleIds = new List<int> { 1, 5 } });

            // Leave Types:
            backend.SeedLeaveType(new LeaveTypeDefinitionDto
            {
                LeaveTypeId = 1,
                LeaveName = "مرخصی استحقاقی",
                Symbol = "س",
                EachXHoursADayMinutes = 440, // 7h 20m
                GetInHolidays = false,
                MaxMinusMinutes = 2 * 440,   // Up to 2 days negative balance allowed
                YearlyMaxMinutes = 26 * 440, // 26 days/year
                DecreaseFromLeaveId = null
            });
            backend.SeedLeaveType(new LeaveTypeDefinitionDto
            {
                LeaveTypeId = 2,
                LeaveName = "مرخصی ساعتی توافقی (کسر از استحقاقی)",
                Symbol = "ت",
                EachXHoursADayMinutes = 440,
                GetInHolidays = false,
                MaxMinusMinutes = 0,
                YearlyMaxMinutes = 0,
                DecreaseFromLeaveId = 1 // Deducts from LeaveType 1!
            });
            backend.SeedLeaveType(new LeaveTypeDefinitionDto
            {
                LeaveTypeId = 5,
                LeaveName = "مرخصی یک‌روزه خاص",
                Symbol = "خ",
                EachXHoursADayMinutes = 440,
                GetInHolidays = false,
                MaxMinusMinutes = 0,
                YearlyMaxMinutes = 12 * 440,
                DecreaseFromLeaveId = null
            });

            return backend;
        }

        private static void TestT5OrganizationAndAccessService()
        {
            InMemoryT5BackendAdapter backend = CreateSampleEnterpriseBackend();
            var orgService = new T5OrganizationAccessService(backend);

            // 1. Top-level manager detection (CEO Badge 100 and Admin Badge 0)
            Assert(orgService.IsTopLevelManager(0), "Admin (Badge 0) must be TopLevelManager.");
            Assert(orgService.IsTopLevelManager(100), "CEO (Badge 100, Title 1) must be TopLevelManager.");
            Assert(!orgService.IsTopLevelManager(200), "VP Engineering (Badge 200) is not root.");
            Assert(!orgService.IsTopLevelManager(400), "Developer (Badge 400) is not root.");

            // 2. Workflow-aware subordinate discovery:
            // HR Manager (Title 5) has no TitlesChart children, but is Step 2 approver for Developer (Title 4) in WorkflowDA!
            IReadOnlyList<int> hrSubTitles = orgService.GetSubordinateTitleIdsForTitle(5);
            Assert(hrSubTitles.Count == 1 && hrSubTitles[0] == 4,
                "HR Manager (Title 5) must discover Developer (Title 4) via T5 WorkflowDA.");

            // VP Engineering (Title 2) discovers Team Lead (Title 3) via TitlesChart fallback (since Title 3 has no custom WorkflowDA rows)
            IReadOnlyList<int> vpSubTitles = orgService.GetSubordinateTitleIdsForTitle(2);
            Assert(vpSubTitles.Contains(3), "VP Engineering (Title 2) must discover Team Lead (Title 3) via TitlesChart fallback.");

            // 3. Guard / Observer access (Security Guard Badge 600, Title 6)
            Assert(orgService.IsGuardObserver(600), "Security Guard (Badge 600) must have IsGuardObserver=true.");
            Assert(!orgService.IsGuardObserver(400), "Developer (Badge 400) must have IsGuardObserver=false.");
            IReadOnlyList<EmployeeSummaryDto> guardVisible = orgService.GetGuardAccessiblePersonnel(600);
            Assert(guardVisible.Count == 6, $"Guard must see all 6 active personnel, got {guardVisible.Count}");

            // 4. ReportAccess filtering
            Assert(orgService.GetAccessibleReports(0).Count == 2, "Admin must access all 2 reports.");
            Assert(orgService.GetAccessibleReports(500).Count == 2, "HR Manager (Title 5) must access 2 reports.");
            Assert(orgService.GetAccessibleReports(300).Count == 1, "Team Lead (Title 3) must access 1 report.");
            Assert(orgService.GetAccessibleReports(400).Count == 0, "Developer (Title 4) must access 0 reports.");
        }

        private static void TestTwoLayerDeputySystem()
        {
            InMemoryT5BackendAdapter backend = CreateSampleEnterpriseBackend();
            var orgService = new T5OrganizationAccessService(backend);

            // HR Manager (Badge 500, Title 5) only allows Title 2 (VP Engineering, Badge 200) in T5 DeputyDA
            IReadOnlyList<EmployeeSummaryDto> hrCandidates = orgService.GetCandidateDeputyEmployees(500);
            Assert(hrCandidates.Count == 1 && hrCandidates[0].BadgeNumber == 200,
                "HR Manager's candidate deputies must only contain VP Engineering (Badge 200) per T5 DeputyDA.");

            // Attempting to assign Developer (Badge 400) as deputy for HR Manager (Badge 500) must fail
            bool threwForUnauthorizedDeputy = false;
            try
            {
                orgService.AssignDeputy(500, 400);
            }
            catch (InvalidOperationException)
            {
                threwForUnauthorizedDeputy = true;
            }
            Assert(threwForUnauthorizedDeputy, "AssignDeputy must reject employees whose TitleID is not in T5 DeputyDA.");

            // Assigning VP Engineering (Badge 200) as active deputy for HR Manager (Badge 500) must succeed
            DeputyAssignmentDto assigned = orgService.AssignDeputy(500, 200);
            Assert(assigned != null && assigned.PrincipalBadgeNumber == 500 && assigned.DeputyBadgeNumber == 200,
                "Valid deputy assignment must succeed.");

            // Now VP Engineering (Badge 200) should see HR Manager's workflow subordinates (Developer Badge 400) with ViaDeputyForBadgeNumber = 500!
            IReadOnlyList<EmployeeSummaryDto> vpAccessible = orgService.GetAccessiblePersonnel(200, includeSelf: false, includeDeputySubordinates: true);
            EmployeeSummaryDto devViaDeputy = vpAccessible.FirstOrDefault(e => e.BadgeNumber == 400);
            Assert(devViaDeputy != null && devViaDeputy.ViaDeputyForBadgeNumber == 500,
                "Active deputy (VP Engineering) must inherit principal's (HR Manager's) subordinate personnel.");
        }

        private static void TestMultiStepWorkflowEngine()
        {
            InMemoryT5BackendAdapter backend = CreateSampleEnterpriseBackend();
            var orgService = new T5OrganizationAccessService(backend);

            var settings = new TwfAppSettings
            {
                MaxRequestAgeDays = 400,
                LastDayForRequest = 31,
                HoursOfDays = 440,
                Limit1LeaveId = 5
            };
            var settingsService = CreateInMemorySettingsService(settings);
            var leaveService = new LeaveBalanceService(backend, settingsService);
            var workflowEngine = new WorkflowEngineService(backend, settingsService, orgService, leaveService);

            DateTime reqStart = JalaliDateHelper.ParseJalali("1405/07/10");
            DateTime reqEnd = JalaliDateHelper.ParseJalali("1405/07/12"); // 3 days
            DateTime now = JalaliDateHelper.ParseJalali("1405/07/11");

            // Developer (Badge 400) submits a 3-day Daily Leave request
            var leaveReq = new WorkflowRequestItemDto
            {
                BadgeNumber = 400,
                WorkflowType = TwfWorkflowType.DailyLeave,
                IsDaily = true,
                LeaveTypeId = 1,
                ForDate = reqStart,
                FromTime = reqStart,
                ToTime = reqEnd,
                Descriptions = "مرخصی سه روزه پاییزی"
            };

            WorkflowActionResult submitRes = workflowEngine.SubmitRequest(leaveReq, senderBadgeNumber: 400, referenceNow: now);
            Assert(submitRes.Succeeded && submitRes.NewState == TwfRequestState.Waiting && !submitRes.IsFinalized,
                "Submitted request for Developer must enter Waiting state.");

            long reqId = submitRes.RequestId;

            // Step 1 is Team Lead (Badge 300, Title 3). HR Manager (Badge 500, Title 5) is Step 2.
            Assert(workflowEngine.GetPendingRequestsForApprover(300).Any(r => r.RequestId == reqId),
                "Request must appear in Step 1 approver's (Team Lead Badge 300) pending queue.");
            Assert(!workflowEngine.GetPendingRequestsForApprover(500).Any(r => r.RequestId == reqId),
                "Request must NOT appear in Step 2 approver's (HR Manager Badge 500) queue before Step 1 is approved.");

            // HR Manager attempting to approve out-of-order must be denied
            WorkflowActionResult outOfOrder = workflowEngine.ApproveRequest(reqId, approverBadgeNumber: 500, referenceNow: now);
            Assert(!outOfOrder.Succeeded, "Out-of-order approval by Step 2 approver must be rejected.");

            // Step 1 Approval by Team Lead (Badge 300) -> AcceptByID1 (Intermediate step)
            WorkflowActionResult step1Res = workflowEngine.ApproveRequest(reqId, approverBadgeNumber: 300, referenceNow: now);
            Assert(step1Res.Succeeded && step1Res.NewState == TwfRequestState.FirstAccept && !step1Res.IsFinalized && !step1Res.OperationalRecordInserted,
                "Step 1 approval must set state to FirstAccept without inserting operational data.");

            // Now HR Manager delegates active deputy authority to VP Engineering (Badge 200)
            orgService.AssignDeputy(principalBadgeNumber: 500, deputyBadgeNumber: 200);

            // VP Engineering (Badge 200) sees the request in their pending queue marked with ForDeputy = true!
            IReadOnlyList<WorkflowRequestItemDto> vpPending = workflowEngine.GetPendingRequestsForApprover(200);
            WorkflowRequestItemDto deputyPendingItem = vpPending.FirstOrDefault(r => r.RequestId == reqId);
            Assert(deputyPendingItem != null && deputyPendingItem.ForDeputy && deputyPendingItem.DeputyPrincipalTitleId == 5,
                "Active deputy (VP Engineering Badge 200) must see Step 2 request with ForDeputy=true.");

            // Step 2 Final Approval by Deputy (VP Engineering Badge 200) -> AcceptByID2 + InsertData
            WorkflowActionResult step2Res = workflowEngine.ApproveRequest(reqId, approverBadgeNumber: 200, referenceNow: now);
            Assert(step2Res.Succeeded && step2Res.NewState == TwfRequestState.Accept && step2Res.IsFinalized && step2Res.OperationalRecordInserted,
                "Final step approval must finalize request and insert operational data.");

            // Verify 3 DailyLeave records were inserted into T5 DailyLeaves
            IReadOnlyList<DailyLeaveRecordDto> insertedLeaves = backend.GetApprovedDailyLeaves(400, reqStart, reqEnd);
            Assert(insertedLeaves.Count == 3, $"Expected 3 DailyLeave records inserted, found {insertedLeaves.Count}");

            // Undo by VP Engineering (Badge 200) -> UndoController.Undo + DeleteData
            WorkflowActionResult undoRes = workflowEngine.UndoManagerDecision(reqId, managerBadgeNumber: 200);
            Assert(undoRes.Succeeded && undoRes.NewState == TwfRequestState.FirstAccept && !undoRes.IsFinalized,
                "Undo of Step 2 must revert request back to FirstAccept (pending Step 2).");

            IReadOnlyList<DailyLeaveRecordDto> leavesAfterUndo = backend.GetApprovedDailyLeaves(400, reqStart, reqEnd);
            Assert(leavesAfterUndo.Count == 0, "Undo must delete the operational DailyLeave records from T5.");
        }

        private static void TestLeaveBalanceEngine()
        {
            InMemoryT5BackendAdapter backend = CreateSampleEnterpriseBackend();
            var settings = new TwfAppSettings { HoursOfDays = 440, Limit1LeaveId = 5 };
            var settingsService = CreateInMemorySettingsService(settings);
            var leaveService = new LeaveBalanceService(backend, settingsService);

            // Seed approved leaves in Persian year 1400 (non-leap Gregorian year 2021 where 1400/01/01 = 2021-03-21 has DayOfYear=80)
            DateTime farvardin1_1400 = JalaliDateHelper.ParseJalali("1400/01/01");
            DateTime farvardin2_1400 = JalaliDateHelper.ParseJalali("1400/01/02");
            DateTime ordibehesht10_1400 = JalaliDateHelper.ParseJalali("1400/02/10");

            // 1. Approved Daily Leave on 1400/01/01 (which legacy BUG-06 missed because DayOfYear was 80 instead of 81!)
            backend.SeedApprovedDailyLeave(new DailyLeaveRecordDto
            {
                Id = 1,
                BadgeNumber = 400,
                LeaveDate = farvardin1_1400,
                LeaveTypeId = 1,
                IsHoliday = false
            });
            // 2. Approved Daily Leave on a holiday (GetInHolidays=false, so it must NOT be deducted!)
            backend.SeedApprovedDailyLeave(new DailyLeaveRecordDto
            {
                Id = 2,
                BadgeNumber = 400,
                LeaveDate = farvardin2_1400,
                LeaveTypeId = 1,
                IsHoliday = true
            });
            // 3. Approved Hourly Leave of LeaveType 2 (which has DecreaseFromLeaveId = 1!) for 150 minutes (2h 30m)
            backend.SeedApprovedHourlyLeave(new HourlyLeaveRecordDto
            {
                Id = 1,
                BadgeNumber = 400,
                FromTime = ordibehesht10_1400.AddHours(8),
                ToTime = ordibehesht10_1400.AddHours(10).AddMinutes(30),
                LeaveTypeId = 2
            });

            DateTime yearEnd1400 = JalaliDateHelper.GetPersianYearEnd(1400);
            LeaveBalanceSummaryDto balance = leaveService.GetLeaveBalance(400, leaveTypeId: 1, asOfDate: yearEnd1400);

            Assert(balance.PersianYear == 1400, "PersianYear must be 1400.");
            Assert(balance.DailyTakenDays == 1, $"BUG-06 & GetInHolidays check: Expected 1 non-holiday day taken (including 1400/01/01), got {balance.DailyTakenDays}");
            Assert(balance.DailyTakenMinutes == 440, "DailyTakenMinutes must be 440.");
            Assert(balance.HourlyTakenMinutes == 150, $"DecreaseFromLeaveId check: Expected 150 hourly minutes from linked LeaveType 2, got {balance.HourlyTakenMinutes}");
            Assert(balance.TotalTakenMinutes == 590, "TotalTakenMinutes must be 590 (1 day + 2h 30m).");
            Assert(balance.RemainingMinutes == (26 * 440) - 590, "RemainingMinutes calculation mismatch.");

            // Cross-year multi-day leave validation (1399/12/30 leap day -> 1400/01/02 must be rejected)
            var crossYearLeave = new WorkflowRequestItemDto
            {
                BadgeNumber = 400,
                WorkflowType = TwfWorkflowType.DailyLeave,
                IsDaily = true,
                LeaveTypeId = 1,
                ForDate = JalaliDateHelper.ParseJalali("1399/12/30"),
                ToTime = JalaliDateHelper.ParseJalali("1400/01/02")
            };
            var (crossAllowed, _) = leaveService.ValidateLeaveRequestQuota(crossYearLeave);
            Assert(!crossAllowed, "Multi-day leave crossing Persian year boundary (1399/12/30 to 1400/01/02) must be rejected.");

            // Limit1LeaveID (LeaveTypeId = 5) check:
            backend.SeedApprovedDailyLeave(new DailyLeaveRecordDto
            {
                Id = 3,
                BadgeNumber = 400,
                LeaveDate = JalaliDateHelper.ParseJalali("1400/03/05"),
                LeaveTypeId = 5,
                IsHoliday = false
            });
            var secondLimit1InSameMonth = new WorkflowRequestItemDto
            {
                BadgeNumber = 400,
                WorkflowType = TwfWorkflowType.DailyLeave,
                IsDaily = true,
                LeaveTypeId = 5,
                ForDate = JalaliDateHelper.ParseJalali("1400/03/20"),
                ToTime = JalaliDateHelper.ParseJalali("1400/03/20")
            };
            var (limit1Allowed, limit1Err) = leaveService.ValidateLeaveRequestQuota(secondLimit1InSameMonth);
            Assert(!limit1Allowed && limit1Err == PersianString.DailyLeaveLimit,
                "Second Limit1LeaveID request in the same Persian month must be rejected.");
        }

        private static void TestRequestTimeAndTrafficLimits()
        {
            InMemoryT5BackendAdapter backend = CreateSampleEnterpriseBackend();
            var orgService = new T5OrganizationAccessService(backend);

            DateTime lockCutoff = JalaliDateHelper.ParseJalali("1405/06/01");
            var settings = new TwfAppSettings
            {
                RequestsLockOADate = lockCutoff.ToOADate(),
                MaxRequestAgeDays = 30,
                LastDayForRequest = 5, // Previous month requests only allowed up to the 5th of the current month
                RequestRecordsDailyLimit = 1,
                RequestRecordsMonthlyLimit = 2
            };
            var settingsService = CreateInMemorySettingsService(settings);
            var leaveService = new LeaveBalanceService(backend, settingsService);
            var workflowEngine = new WorkflowEngineService(backend, settingsService, orgService, leaveService);

            DateTime now = JalaliDateHelper.ParseJalali("1405/08/10"); // 10th of Aban 1405

            // 1. Date before RequestsLockOADate (1405/05/25 <= 1405/06/01)
            Assert(!workflowEngine.CheckRequestTime(JalaliDateHelper.ParseJalali("1405/05/25"), now).IsValid,
                "Date on or before RequestsLockOADate must be rejected.");

            // 2. Date in previous Persian month (1405/07/28) when today is 1405/08/10 (Day 10 > LastDayForRequest=5)
            Assert(!workflowEngine.CheckRequestTime(JalaliDateHelper.ParseJalali("1405/07/28"), now).IsValid,
                "Previous Persian month request submitted after LastDayForRequest (day 10 > 5) must be rejected.");

            // 3. Date in previous Persian month when today is 1405/08/04 (Day 4 <= LastDayForRequest=5)
            DateTime earlyMonthNow = JalaliDateHelper.ParseJalali("1405/08/04");
            Assert(workflowEngine.CheckRequestTime(JalaliDateHelper.ParseJalali("1405/07/28"), earlyMonthNow).IsValid,
                "Previous Persian month request submitted on or before LastDayForRequest (day 4 <= 5) must be valid.");

            // 4. Traffic (Record) Daily & Monthly Limits
            DateTime aban2 = JalaliDateHelper.ParseJalali("1405/08/02");
            DateTime aban3 = JalaliDateHelper.ParseJalali("1405/08/03");
            DateTime aban4 = JalaliDateHelper.ParseJalali("1405/08/04");

            var rec1 = new WorkflowRequestItemDto { BadgeNumber = 400, WorkflowType = TwfWorkflowType.Record, ForDate = aban2, RecordTime = aban2.AddHours(8) };
            Assert(workflowEngine.SubmitRequest(rec1, 400, now).Succeeded, "First record request on 1405/08/02 must succeed.");

            var recSameDay = new WorkflowRequestItemDto { BadgeNumber = 400, WorkflowType = TwfWorkflowType.Record, ForDate = aban2, RecordTime = aban2.AddHours(17) };
            WorkflowActionResult sameDayRes = workflowEngine.SubmitRequest(recSameDay, 400, now);
            Assert(!sameDayRes.Succeeded && sameDayRes.Message == PersianString.RequestRecordsDailyLimit,
                "Second record request on the same day must be rejected by RequestRecordsDailyLimit=1.");

            var rec2 = new WorkflowRequestItemDto { BadgeNumber = 400, WorkflowType = TwfWorkflowType.Record, ForDate = aban3, RecordTime = aban3.AddHours(8) };
            Assert(workflowEngine.SubmitRequest(rec2, 400, now).Succeeded, "Second record request in month on 1405/08/03 must succeed.");

            var rec3ExceedMonth = new WorkflowRequestItemDto { BadgeNumber = 400, WorkflowType = TwfWorkflowType.Record, ForDate = aban4, RecordTime = aban4.AddHours(8) };
            WorkflowActionResult exceedMonthRes = workflowEngine.SubmitRequest(rec3ExceedMonth, 400, now);
            Assert(!exceedMonthRes.Succeeded && exceedMonthRes.Message == PersianString.RequestRecordsMonthlyLimit,
                "Third record request in the same Persian month must be rejected by RequestRecordsMonthlyLimit=2.");
        }

        private static void TestPasswordPolicyService()
        {
            InMemoryT5BackendAdapter backend = CreateSampleEnterpriseBackend();
            var repo = new TwfDataRepository("Data Source=.;Initial Catalog=Personnel;", "Data Source=.;Initial Catalog=ReportT5V3;");

            var settings = new TwfAppSettings
            {
                ChangePassForce = true,
                ChangePassMonthly = true,
                ChangePassLimit = 30,
                ChangePassRestriction = true
            };
            var settingsService = CreateInMemorySettingsService(settings);
            var passwordService = new PasswordPolicyService(backend, repo, settingsService);

            DateTime loginDay = new DateTime(2026, 10, 24, 9, 0, 0);

            // 1. First login: User 400 has no PassCheckProps record -> must trigger IsFirstLoginForceChange
            PasswordCheckStatus initialStatus = passwordService.EvaluatePasswordStatus(400, loginDay);
            Assert(initialStatus.RequiresImmediateChange && initialStatus.IsFirstLoginForceChange,
                "BUG-03 Fix: First login with ChangePassForce=true must require immediate password change.");

            // 2. Complexity check: password containing badge number ("400") or lacking letters/digits must be rejected
            PasswordChangeResult weak1 = passwordService.ChangePassword(400, "123", "1234567", "1234567", loginDay);
            Assert(!weak1.Succeeded, "Password with only digits must be rejected when ChangePassRestriction=true.");

            PasswordChangeResult weakBadge = passwordService.ChangePassword(400, "123", "Pass400!", "Pass400!", loginDay);
            Assert(!weakBadge.Succeeded, "Password containing BadgeNumber (400) must be rejected.");

            // 3. Valid strong password change
            PasswordChangeResult validChange = passwordService.ChangePassword(400, "123", "Secur3Pass!", "Secur3Pass!", loginDay);
            Assert(validChange.Succeeded, $"Valid password change failed: {validChange.Message}");

            // Verify new password works in backend authentication
            Assert(backend.AuthenticateEmployee("dev", "Secur3Pass!") == 400,
                "Employee password must be updated in backend.");

            // 4. Status immediately after change (+5 days): no change required, 25 days remaining
            PasswordCheckStatus day5Status = passwordService.EvaluatePasswordStatus(400, loginDay.AddDays(5));
            Assert(!day5Status.RequiresImmediateChange && day5Status.DaysRemainingBeforeExpiry == 25,
                "Password at +5 days must not require change (25 days remaining).");

            // 5. Status after 30 days (ChangePassLimit = 30): must require periodic rotation
            PasswordCheckStatus day30Status = passwordService.EvaluatePasswordStatus(400, loginDay.AddDays(30));
            Assert(day30Status.RequiresImmediateChange && day30Status.IsPeriodicExpiryChange,
                "Password at +30 days must trigger periodic expiration (ChangePassMonthly + ChangePassLimit=30).");
        }

        private static AppSettingsService CreateInMemorySettingsService(TwfAppSettings settings)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"TWF_Phase2_{Guid.NewGuid():N}.config");
            var svc = new AppSettingsService(tempPath);
            svc.SaveSettings(settings);
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            return svc;
        }

        // =====================================================================
        // PHASE 3 TESTS
        // =====================================================================

        private static void TestPhase3Td5RewriteAndQueryBuilders()
        {
            // 1. Verify DapQuery DELETE bug fix (legacy DapQuery omitted case DapType.DELETE in AddQuery)
            var deleteQuery = new TD5.Model.DapQuery<TD5.Model.Request>("DELETE FROM [Request] ", null, TD5.Model.DapType.DELETE);
            deleteQuery.AddQuery("[RequestID] = @RequestID");
            Assert(deleteQuery.Query.Trim() == "DELETE FROM [Request] WHERE [RequestID] = @RequestID",
                $"TD5.Model.DapQuery DELETE query failed to append WHERE clause: {deleteQuery.Query}");

            // 2. Verify TD5.Model.Request FromTime getter does NOT mutate FromTime by +1 day (BUG-07b fix)
            var sampleTime = new DateTime(2026, 10, 24, 14, 30, 0);
            var td5Req = new TD5.Model.Request
            {
                RequestID = 77,
                BadgeNumber = 400,
                FromTime = sampleTime,
                ToTime = sampleTime.AddHours(2),
                ForDate = sampleTime.Date,
                OverTimeType = (int)T5V3.Model.AllowType.OverTimeAfter,
                WofkflowType = (int)T5V3.Model.WorkflowOperations.AllowOverTimes
            };
            Assert(td5Req.FromTime == sampleTime && td5Req.FromTime == sampleTime,
                "TD5.Model.Request.FromTime getter must not mutate FromTime by +1 day.");

            T5V3.Model.Request mappedT5 = TD5.Controllers.RequestController.GetT5Model(td5Req);
            Assert(mappedT5.FromTime == sampleTime && mappedT5.OverTimeAllowType == T5V3.Model.AllowType.OverTimeAfter,
                "TD5.Controllers.RequestController.GetT5Model must preserve FromTime and map OverTimeAllowType.");

            // 3. Verify DepartmentRuleControllers.FromDate & UserRuleControllers.FromDate check [FromDate] >= @FromDate
            var deptCtrl = TD5.Controllers.DepartmentRuleControllers.FromDate(
                TD5.Controllers.DepartmentRuleControllers.GetDepartmentRule(), sampleTime.Date);
            Assert(deptCtrl.Query.Contains("[FromDate] >= @FromDate"),
                $"DepartmentRuleControllers.FromDate must filter on [FromDate], got: {deptCtrl.Query}");

            var userRuleCtrl = TD5.Controllers.UserRuleControllers.FromDate(
                TD5.Controllers.UserRuleControllers.GetUserRule(), sampleTime.Date);
            Assert(userRuleCtrl.Query.Contains("[FromDate] >= @FromDate"),
                $"UserRuleControllers.FromDate must filter on [FromDate], got: {userRuleCtrl.Query}");

            // 4. Verify LeaveTypeControllers.Id binds @LeaveID (legacy bound @ID while SQL used @LeaveID)
            var ltCtrl = TD5.Controllers.LeaveTypeControllers.Id(
                TD5.Controllers.LeaveTypeControllers.GetLeaveType(), 5);
            Assert(ltCtrl.Query.Contains("[LeaveID] = @LeaveID"),
                $"LeaveTypeControllers.Id must filter on [LeaveID] = @LeaveID, got: {ltCtrl.Query}");

            // 5. Verify UserInfoController sanitizes datetimeOperator against SQL injection
            var uiCtrl = TD5.Controllers.UserInfoController.HiredDate(
                TD5.Controllers.UserInfoController.GetUserInfo(), sampleTime.Date, "1=1; DROP TABLE UserInfo;--");
            Assert(uiCtrl.Query.Contains("[HiredDate] = @HiredDate") && !uiCtrl.Query.Contains("DROP TABLE"),
                $"UserInfoController.HiredDate failed to sanitize operator: {uiCtrl.Query}");
        }

        private static void TestPhase3MyTime3ZeroLock()
        {
            MyTime3.Lock.CheckLockResult lockResult = MyTime3.Lock.LockCheker.Check();
            Assert(lockResult != null, "LockCheker.Check() must not return null.");
            Assert(lockResult.LockResult == MyTime3.Lock.LockResult.OK, "LockCheker.Check() must return LockResult.OK.");
            Assert(lockResult.PersonnelCount == int.MaxValue, "LockCheker.Check().PersonnelCount must be int.MaxValue.");
            Assert(lockResult.IsLogoFree, "LockCheker.Check().IsLogoFree must be true.");
            Assert(string.IsNullOrEmpty(lockResult.TimeApp), "LockCheker.Check().TimeApp must be empty.");

            Assert(MyTime3.GlobalHelper.LockIsOK, "MyTime3.GlobalHelper.LockIsOK must be true.");
            Assert(MyTime3.GlobalHelper.PersonnelCount == int.MaxValue, "MyTime3.GlobalHelper.PersonnelCount must be int.MaxValue.");
            Assert(MyTime3.GlobalHelper.FulLogoSize, "MyTime3.GlobalHelper.FulLogoSize must be true.");
            Assert(!MyTime3.GlobalHelper.WithTimeApp(), "MyTime3.GlobalHelper.WithTimeApp() must be false (never expired).");
        }

        private static void TestPhase3MyTime3Authorization()
        {
            // 1. Encryption AES-256 + HMAC-SHA256 verification
            string plain = "2026/10/25 10:15:00;400;user";
            string cipher1 = MyTime3.Authorization.Encryption.Encrypt(plain);
            string cipher2 = MyTime3.Authorization.Encryption.Encrypt(plain);
            Assert(cipher1 != cipher2, "Encryption.Encrypt must use a random IV per call.");
            Assert(MyTime3.Authorization.Encryption.Decrypt(cipher1) == plain, "Encryption.Decrypt failed to recover plaintext.");
            Assert(string.IsNullOrEmpty(MyTime3.Authorization.Encryption.Decrypt(cipher1 + "tamper")),
                "Encryption.Decrypt must reject tampered ciphertext.");

            // 2. LoginManager & AuthorizeFilter verification (BUG-02 fix)
            InMemoryT5BackendAdapter backend = CreateSampleEnterpriseBackend();
            var settings = new TwfAppSettings
            {
                CookiesTime = 30,
                CookiesSecure = true
            };
            var settingsService = CreateInMemorySettingsService(settings);
            MyTime3.Controllers.DBInitializer.EnableTestMode(backend, settingsService);

            try
            {
                var httpContext = new System.Web.HttpContextWrapper(new System.Web.HttpContext());
                MyTime3.Authorization.LoginManager.Login(400L, "user", httpContext);

                var authCookie = httpContext.Response.Cookies.Get(MyTime3.Authorization.LoginManager.AuthCookieName);
                Assert(authCookie != null && authCookie.HttpOnly && authCookie.Secure,
                    "BUG-02 Fix: LoginManager.Login must set HttpOnly=true and honor CookiesSecure=true (typeof(bool)).");

                Assert(!MyTime3.Authorization.LoginManager.IsExpired(httpContext), "Freshly logged-in cookie must not be expired.");
                Assert(MyTime3.Authorization.LoginManager.GetBadgeNumber(httpContext) == "400", "GetBadgeNumber must return '400'.");
                Assert(MyTime3.Authorization.LoginManager.GetRole(httpContext) == "user", "GetRole must return 'user'.");

                var userFilter = new MyTime3.AuthorizeFilter { Roles = "admin,user" };
                var adminOnlyFilter = new MyTime3.AuthorizeFilter { Roles = "admin" };
                Assert(userFilter.CheckAuthorized(httpContext), "User must be authorized for 'admin,user'.");
                Assert(!adminOnlyFilter.CheckAuthorized(httpContext), "Standard user must not be authorized for 'admin'.");

                MyTime3.Authorization.LoginManager.Logout(httpContext);
                Assert(MyTime3.Authorization.LoginManager.IsExpired(httpContext), "Logged-out context must report IsExpired=true.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase3LoginControllerFlow()
        {
            InMemoryT5BackendAdapter backend = CreateSampleEnterpriseBackend();
            var settings = new TwfAppSettings
            {
                CookiesTime = 20,
                Captcha = false,
                ChangePassForce = false,
                ChangePassMonthly = false,
                ChangePassRestriction = false
            };
            var settingsService = CreateInMemorySettingsService(settings);
            var repo = new TwfDataRepository("Data Source=.;Initial Catalog=Personnel;", "Data Source=.;Initial Catalog=ReportT5V3;");
            MyTime3.Controllers.DBInitializer.EnableTestMode(backend, settingsService, repo);

            try
            {
                var controller = CreateControllerWithContext<MyTime3.Controllers.LoginController>("192.168.1.50");

                // 1. Valid employee login -> Redirect to Home/Index
                var loginRes = controller.Login(new MyTime3.Models.LoginModel { UserName = "dev", Password = "123" }) as System.Web.Mvc.RedirectToRouteResult;
                Assert(loginRes != null &&
                       string.Equals(loginRes.RouteValues["controller"]?.ToString(), "Home", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(loginRes.RouteValues["action"]?.ToString(), "Index", StringComparison.OrdinalIgnoreCase),
                    "Valid employee login must redirect to Home/Index.");

                // 2. Valid admin login -> Redirect to Settings/Index
                var adminRes = controller.Login(new MyTime3.Models.LoginModel { UserName = "admin", Password = "admin" }) as System.Web.Mvc.RedirectToRouteResult;
                Assert(adminRes != null &&
                       string.Equals(adminRes.RouteValues["controller"]?.ToString(), "Settings", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(adminRes.RouteValues["action"]?.ToString(), "Index", StringComparison.OrdinalIgnoreCase),
                    "Valid admin login must redirect to Settings/Index.");

                // 3. Brute-force lockout after 5 failed attempts
                var bruteController = CreateControllerWithContext<MyTime3.Controllers.LoginController>("10.20.30.40");
                for (int i = 0; i < 5; i++)
                {
                    var failView = bruteController.Login(new MyTime3.Models.LoginModel { UserName = "dev", Password = "wrong" }) as System.Web.Mvc.ViewResult;
                    Assert(failView != null, "Failed login must return Login ViewResult.");
                }

                // 6th attempt even with right password must be blocked by lockout
                var lockedView = bruteController.Login(new MyTime3.Models.LoginModel { UserName = "dev", Password = "123" }) as System.Web.Mvc.ViewResult;
                var lockedModel = lockedView?.Model as MyTime3.Models.LoginModel;
                Assert(lockedModel != null && lockedModel.Message.Contains("تلاش‌های ناموفق"),
                    "Brute-force throttle must block 6th login attempt from locked IP/user.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase3ChangePassControllerFlow()
        {
            InMemoryT5BackendAdapter backend = CreateSampleEnterpriseBackend();
            var settings = new TwfAppSettings
            {
                ChangePassForce = true,
                ChangePassMonthly = true,
                ChangePassLimit = 3,
                ChangePassRestriction = true
            };
            var settingsService = CreateInMemorySettingsService(settings);
            var repo = new TwfDataRepository("Data Source=.;Initial Catalog=Personnel;", "Data Source=.;Initial Catalog=ReportT5V3;");
            MyTime3.Controllers.DBInitializer.EnableTestMode(backend, settingsService, repo);

            try
            {
                // 1. Before any password change: ChangePassCheck must return false with message = 1 (ChangePassForce - BUG-03 fix)
                bool okBefore = MyTime3.Controllers.ChangePassController.ChangePassCheck(400L, out int msgCode);
                Assert(!okBefore && msgCode == 1,
                    $"BUG-03 Fix: ChangePassController.ChangePassCheck must return false with message=1 when ChangePassForce=true, got ok={okBefore}, msg={msgCode}.");

                // 2. Login with ChangePassForce=true must redirect to ChangePasswordForce with Message=1
                var controller = CreateControllerWithContext<MyTime3.Controllers.LoginController>("192.168.1.99");
                var forceRedirect = controller.Login(new MyTime3.Models.LoginModel { UserName = "dev", Password = "123" }) as System.Web.Mvc.RedirectToRouteResult;
                Assert(forceRedirect != null &&
                       string.Equals(forceRedirect.RouteValues["action"]?.ToString(), "ChangePasswordForce", StringComparison.OrdinalIgnoreCase) &&
                       Equals(forceRedirect.RouteValues["Message"], 1),
                    "Login with ChangePassForce=true must redirect to ChangePasswordForce(Message=1).");

                // 3. Attempting to change password to a short/non-alphanumeric password must fail when ChangePassRestriction=true
                var weakRes = controller.ChangePasswordForce(new MyTime3.Models.ChangePasswordModel
                {
                    OldPassword = "123",
                    NewPassword = "12345",
                    ConfirmPassword = "12345"
                }) as System.Web.Mvc.ViewResult;
                Assert(weakRes != null, "Numeric-only password must be rejected when ChangePassRestriction=true.");

                // 4. Changing to a strong alphanumeric password must succeed and redirect to Home/Index
                var strongRes = controller.ChangePasswordForce(new MyTime3.Models.ChangePasswordModel
                {
                    OldPassword = "123",
                    NewPassword = "StrongPass99",
                    ConfirmPassword = "StrongPass99"
                }) as System.Web.Mvc.RedirectToRouteResult;
                Assert(strongRes != null &&
                       string.Equals(strongRes.RouteValues["controller"]?.ToString(), "Home", StringComparison.OrdinalIgnoreCase),
                    "Valid password change in ChangePasswordForce must redirect to Home/Index.");

                // 5. Now ChangePassCheck must return true (message = 0)
                bool okAfter = MyTime3.Controllers.ChangePassController.ChangePassCheck(400L, out int msgAfter);
                Assert(okAfter && msgAfter == 0, "ChangePassCheck must pass after recording password change.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase3SettingsAndTitleChartThreadSafety()
        {
            InMemoryT5BackendAdapter backend = CreateSampleEnterpriseBackend();
            var settings = new TwfAppSettings
            {
                ChangePassForce = false,
                ChangePassLimit = 3,
                ExportReport = true
            };
            var settingsService = CreateInMemorySettingsService(settings);
            MyTime3.Controllers.DBInitializer.EnableTestMode(backend, settingsService);

            try
            {
                // 1. Verify SettingsController GET & POST (BUG-03 & BUG-05 fix)
                var settingsController = CreateControllerWithContext<MyTime3.Controllers.SettingsController>("127.0.0.1");
                var getView = settingsController.Index() as System.Web.Mvc.ViewResult;
                var loadedModel = getView?.Model as MyTime3.Models.SettingsModel;
                Assert(loadedModel != null && !loadedModel.ChangePassForce && loadedModel.ChangePassLimit == 3,
                    "SettingsController.LoadSettings must load ChangePassForce=false and ChangePassLimit=3.");

                loadedModel.ChangePassForce = true;
                loadedModel.ChangePassMonthly = true;
                loadedModel.ChangePassLimit = 6;
                loadedModel.ExportReport = false;
                loadedModel.HoursOfDays = 7;

                var postView = settingsController.Index(loadedModel) as System.Web.Mvc.ViewResult;
                var savedModel = postView?.Model as MyTime3.Models.SettingsModel;
                Assert(savedModel != null && savedModel.OverAllMessage == MyTime3.Content.PersianString.InsertSuccess,
                    "SettingsController POST must return InsertSuccess.");

                var reloaded = settingsController.LoadSettings();
                Assert(reloaded.ChangePassForce && reloaded.ChangePassLimit == 6 && !reloaded.ExportReport && reloaded.HoursOfDays == 7,
                    "BUG-03 Fix: SettingsController must persist and reload ChangePassForce=true independently of ChangePassLimit=6.");

                // 2. Verify TitleChartClass [ThreadStatic] isolation across 30 concurrent threads (BUG-01 fix)
                var tasks = Enumerable.Range(1, 30).Select(i => Task.Run(() =>
                {
                    long badge = 1000 + i;
                    MyTime3.Models.TitleChartClass.user = new MyTime3.Models.UserInfoModel
                    {
                        BadgeNumber = badge,
                        FirstName = $"User{i}",
                        LastName = "Test"
                    };
                    Assert(MyTime3.Models.TitleChartClass.user.BadgeNumber == badge,
                        $"ThreadStatic isolation failed on TitleChartClass.user: expected {badge}.");
                })).ToArray();
                Task.WaitAll(tasks);
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase3ViewsOfflineUiAndSourceAudit()
        {
            string twfRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));

            // 1. Verify OriginalDecompiled reference inventory (13 TD5 + 91 MyTime3 .cs files + MyTime3.csproj)
            int td5OrigCount = Directory.GetFiles(Path.Combine(twfRoot, "OriginalDecompiled", "TD5"), "*.cs", SearchOption.AllDirectories).Length;
            int myTime3OrigCount = Directory.GetFiles(Path.Combine(twfRoot, "OriginalDecompiled", "MyTime3"), "*.cs", SearchOption.AllDirectories).Length;
            Assert(td5OrigCount == 13, $"Expected 13 decompiled TD5 .cs files, found {td5OrigCount}.");
            Assert(myTime3OrigCount == 91, $"Expected 91 decompiled MyTime3 .cs files, found {myTime3OrigCount}.");

            // 2. Verify rewritten TD5 project (13 .cs files)
            int td5RewrittenCount = Directory.GetFiles(Path.Combine(twfRoot, "TD5"), "*.cs", SearchOption.AllDirectories).Length;
            Assert(td5RewrittenCount == 13, $"Expected 13 rewritten TD5 .cs files, found {td5RewrittenCount}.");

            // 3. Verify offline CSS, JS, and Phase 3 Razor views exist and have zero external URLs
            string webRoot = Path.Combine(twfRoot, "TWF.Web");
            string cssPath = Path.Combine(webRoot, "Content", "twf-industrial-ui.css");
            string jsPath = Path.Combine(webRoot, "Scripts", "twf-core.js");
            Assert(File.Exists(cssPath), "Offline Industrial UI stylesheet (twf-industrial-ui.css) must exist.");
            Assert(File.Exists(jsPath), "Offline core script (twf-core.js) must exist.");

            string[] requiredViews =
            {
                "Views/_ViewStart.cshtml",
                "Views/Shared/_Layout.cshtml",
                "Views/Shared/_Layout1.cshtml",
                "Views/Shared/_LogedInLayout.cshtml",
                "Views/Shared/_Menu.cshtml",
                "Views/Shared/_Toolbar.cshtml",
                "Views/Shared/_ToolbarSettings.cshtml",
                "Views/Shared/_CurrentTime.cshtml",
                "Views/Shared/_UserName.cshtml",
                "Views/Shared/Error.cshtml",
                "Views/Shared/LockPage.cshtml",
                "Views/Shared/LockExpires.cshtml",
                "Views/Shared/EditorTemplates/DescriptionCombo.cshtml",
                "Views/Shared/EditorTemplates/LeaveDescriptionCombo.cshtml",
                "Views/Shared/EditorTemplates/DutyDescriptionCombo.cshtml",
                "Views/Shared/EditorTemplates/TimeSpanTemplate.cshtml",
                "Views/Shared/EditorTemplates/TimeSpanTemplate2.cshtml",
                "Views/Login/Login.cshtml",
                "Views/Login/ChangePassword.cshtml",
                "Views/Login/ChangePasswordForce.cshtml",
                "Views/Settings/Index.cshtml"
            };

            foreach (string relView in requiredViews)
            {
                string fullPath = Path.Combine(webRoot, relView);
                Assert(File.Exists(fullPath), $"Required Phase 3 Razor view missing: {relView}");
                string content = File.ReadAllText(fullPath);
                Assert(!content.Contains("http://") && !content.Contains("https://"),
                    $"100% Offline violation: {relView} contains an external http/https URL.");
            }

            // 4. Verify BUG-03 fix in Views/Settings/Index.cshtml
            string settingsRazor = File.ReadAllText(Path.Combine(webRoot, "Views/Settings/Index.cshtml"));
            Assert(settingsRazor.Contains("<input type=\"hidden\" name=\"ChangePassForce\" value=\"false\" />"),
                "Views/Settings/Index.cshtml must have hidden input name=\"ChangePassForce\" (BUG-03 fix).");
            Assert(!settingsRazor.Contains("<input type=\"hidden\" name=\"ChangePassLimit\" value=\"false\" />"),
                "Views/Settings/Index.cshtml must not contain legacy broken hidden input name=\"ChangePassLimit\" value=\"false\".");
        }

        private static TController CreateControllerWithContext<TController>(string userHostAddress)
            where TController : System.Web.Mvc.Controller, new()
        {
            var httpContext = new System.Web.HttpContext();
            httpContext.Request.UserHostAddress = userHostAddress;
            var wrapper = new System.Web.HttpContextWrapper(httpContext);
            var controller = new TController();
            controller.ControllerContext = new System.Web.Mvc.ControllerContext(
                wrapper,
                new System.Web.Routing.RouteData(),
                controller);
            return controller;
        }

        private static TController CreateAuthenticatedController<TController>(long badgeNumber, string role = "user")
            where TController : System.Web.Mvc.Controller, new()
        {
            var controller = CreateControllerWithContext<TController>("127.0.0.1");
            MyTime3.Authorization.LoginManager.Login(badgeNumber, role, controller.HttpContext);
            var authCookie = controller.HttpContext.Response.Cookies.Get(MyTime3.Authorization.LoginManager.AuthCookieName);
            if (authCookie != null)
            {
                controller.HttpContext.Request.Cookies.Set(authCookie);
            }
            return controller;
        }

        private static InMemoryT5BackendAdapter CreateStandardPhase4Backend()
        {
            var adapter = new InMemoryT5BackendAdapter();
            adapter.SeedTitleNode(new OrgTitleNodeDto { TitleId = 1, TitleName = "مدیر عامل", UpperTitleId = 0 });
            adapter.SeedTitleNode(new OrgTitleNodeDto { TitleId = 2, TitleName = "مدیر فنی", UpperTitleId = 1 });
            adapter.SeedTitleNode(new OrgTitleNodeDto { TitleId = 3, TitleName = "کارشناس نرم‌افزار", UpperTitleId = 2 });
            adapter.SeedTitleNode(new OrgTitleNodeDto { TitleId = 4, TitleName = "انتظامات", UpperTitleId = 1, IsObserver = true });

            adapter.SeedEmployee(new EmployeeSummaryDto
            {
                BadgeNumber = 50,
                UserName = "director",
                FirstName = "مهدی",
                LastName = "راد",
                TitleId = 1,
                TitleName = "مدیر عامل",
                DepartmentId = 10,
                IsActive = true
            }, "Dir@1234");

            adapter.SeedEmployee(new EmployeeSummaryDto
            {
                BadgeNumber = 100,
                UserName = "manager",
                FirstName = "رضا",
                LastName = "علوی",
                TitleId = 2,
                TitleName = "مدیر فنی",
                DepartmentId = 10,
                IsActive = true
            }, "Mgr@1234");

            adapter.SeedEmployee(new EmployeeSummaryDto
            {
                BadgeNumber = 200,
                UserName = "emp1",
                FirstName = "سارا",
                LastName = "محمدی",
                TitleId = 3,
                TitleName = "کارشناس نرم‌افزار",
                DepartmentId = 10,
                IsActive = true
            }, "Emp@1234");

            adapter.SeedEmployee(new EmployeeSummaryDto
            {
                BadgeNumber = 201,
                UserName = "emp2",
                FirstName = "امیر",
                LastName = "کریمی",
                TitleId = 3,
                TitleName = "کارشناس نرم‌افزار",
                DepartmentId = 10,
                IsActive = true
            }, "Emp@1234");

            adapter.SeedEmployee(new EmployeeSummaryDto
            {
                BadgeNumber = 300,
                UserName = "guard",
                FirstName = "حسین",
                LastName = "نگهبان",
                TitleId = 4,
                TitleName = "انتظامات",
                DepartmentId = 10,
                IsActive = true
            }, "Grd@1234");

            adapter.SeedAllowedDeputyTitle(principalTitleId: 1, allowedDeputyTitleId: 2);

            foreach (TwfWorkflowType op in new[]
            {
                TwfWorkflowType.DailyLeave,
                TwfWorkflowType.HourlyLeave,
                TwfWorkflowType.DailyDuty,
                TwfWorkflowType.HourlyDuty,
                TwfWorkflowType.Record,
                TwfWorkflowType.Late,
                TwfWorkflowType.Early,
                TwfWorkflowType.OverTime,
                TwfWorkflowType.RewardPenalty
            })
            {
                adapter.SeedWorkflowStep(new WorkflowStepConfigDto
                {
                    WorkflowId = 10 + (int)op,
                    TitleId = 3,
                    Operation = (int)op,
                    OrderIndex = 1,
                    AccepterTitleId = 2
                });
                adapter.SeedWorkflowStep(new WorkflowStepConfigDto
                {
                    WorkflowId = 100 + (int)op,
                    TitleId = 3,
                    Operation = (int)op,
                    OrderIndex = 2,
                    AccepterTitleId = 1
                });
            }

            adapter.SeedLeaveType(new LeaveTypeDefinitionDto
            {
                LeaveTypeId = 1,
                LeaveName = "مرخصی استحقاقی",
                Symbol = "م.ا",
                IsDuty = false,
                IsHourly = false,
                EachXHoursADayMinutes = 440,
                YearlyMaxMinutes = 26 * 440,
                MaxMinusMinutes = 2 * 440
            });
            adapter.SeedLeaveType(new LeaveTypeDefinitionDto
            {
                LeaveTypeId = 2,
                LeaveName = "مرخصی ساعتی استحقاقی",
                Symbol = "م.س",
                IsDuty = false,
                IsHourly = true,
                DecreaseFromLeaveId = 1,
                EachXHoursADayMinutes = 440
            });
            adapter.SeedLeaveType(new LeaveTypeDefinitionDto
            {
                LeaveTypeId = 3,
                LeaveName = "مرخصی یک‌روزه ماهانه",
                Symbol = "م.۱",
                IsDuty = false,
                IsHourly = false,
                EachXHoursADayMinutes = 440,
                YearlyMaxMinutes = 12 * 440
            });
            adapter.SeedLeaveType(new LeaveTypeDefinitionDto
            {
                LeaveTypeId = 10,
                LeaveName = "ماموریت روزانه",
                Symbol = "م.ر",
                IsDuty = true,
                IsHourly = false,
                EachXHoursADayMinutes = 440
            });
            adapter.SeedLeaveType(new LeaveTypeDefinitionDto
            {
                LeaveTypeId = 11,
                LeaveName = "ماموریت ساعتی",
                Symbol = "م.س.ا",
                IsDuty = true,
                IsHourly = true,
                EachXHoursADayMinutes = 440
            });

            int curYear = JalaliDate.FromDateTime(DateTime.Now).Year;
            adapter.SeedAnnualLeaveEntitlement(200, 1, curYear, 26 * 440);
            adapter.SeedAnnualLeaveEntitlement(200, 3, curYear, 12 * 440);
            adapter.SeedAnnualLeaveEntitlement(100, 1, curYear, 26 * 440);

            return adapter;
        }

        private static void TestPhase4HomeDescriptionAndNotification()
        {
            var adapter = CreateStandardPhase4Backend();
            var settings = new TwfAppSettings
            {
                RequestsLockOADate = 45000.5
            };
            var settingsSvc = CreateInMemorySettingsService(settings);
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                // 1. Thread-safety of HomeController.BadgNumber across 30 concurrent threads (BUG-01 fix)
                var tasks = Enumerable.Range(1, 30).Select(i => Task.Run(() =>
                {
                    long expected = 5000 + i;
                    MyTime3.Controllers.HomeController.BadgNumber = expected;
                    Assert(MyTime3.Controllers.HomeController.BadgNumber == expected,
                        $"HomeController.BadgNumber [ThreadStatic] isolation failed for {expected}.");
                })).ToArray();
                Task.WaitAll(tasks);

                // 2. HomeController.Index, About, MyBanner(1) single-PNG fix, and RequestLockValuee double OA-date fix
                var homeCtrl = CreateAuthenticatedController<MyTime3.Controllers.HomeController>(100, "user");
                var indexView = homeCtrl.Index() as System.Web.Mvc.ViewResult;
                var homeModel = indexView?.Model as MyTime3.Models.HomeModel;
                Assert(homeModel != null && MyTime3.Models.TitleChartClass.user != null && MyTime3.Models.TitleChartClass.user.BadgeNumber == 100,
                    "HomeController.Index must build HomeModel and thread-safe TitleChartClass.user for logged-in user 100.");

                var bannerRes = homeCtrl.MyBanner(1) as System.Web.Mvc.FileContentResult;
                Assert(bannerRes != null && bannerRes.ContentType == "image/png" && bannerRes.FileContents != null && bannerRes.FileContents.Length == 8,
                    "HomeController.MyBanner(1) must return valid single-PNG FileContentResult (without duplicate bitmap save).");

                double lockOaVal = MyTime3.Controllers.HomeController.RequestLockValuee();
                Assert(Math.Abs(lockOaVal - 45000.5) < 0.01,
                    $"HomeController.RequestLockValuee must parse OA-date double values without FormatException, got {lockOaVal}.");
                settings.RequestsLockOADate = -1;

                // 3. GetDailyDetailsReport & GetStatusList with Zero-Lock
                var dailyJson = homeCtrl.GetDailyDetailsReport("emp1", "Emp@1234", "0", "7");
                Assert(dailyJson != null && dailyJson.Data != null,
                    "HomeController.GetDailyDetailsReport must return JsonResult without hardware lock.");

                var statusJson = homeCtrl.GetStatusList("manager", "Mgr@1234");
                Assert(statusJson != null && statusJson.Data != null,
                    "HomeController.GetStatusList must return subordinate status JsonResult.");

                // 4. NotificationManager & DescriptionClass
                MyTime3.Controllers.NotificationManager.ClearQueuedNotifications();
                adapter.CreateRequest(new WorkflowRequestItemDto
                {
                    BadgeNumber = 200,
                    ForDate = DateTime.Now.Date,
                    FromTime = DateTime.Now.Date,
                    ToTime = DateTime.Now.Date,
                    IsDaily = true,
                    IsSuspend = true,
                    LeaveTypeId = 1,
                    WorkflowType = TwfWorkflowType.DailyLeave,
                    Descriptions = "توضیحات مرخصی روزانه"
                }, out _, out long createdId);

                var sampleReq = MyTime3.Controllers.EditRequestController.MapDtoToT5Request(adapter.GetRequestById(createdId));
                MyTime3.Controllers.NotificationManager.NewRequestAdded(sampleReq);
                var pendingNotifs = MyTime3.Controllers.NotificationManager.GetQueuedNotifications(100);
                Assert(pendingNotifs.Count > 0, "NotificationManager must store and return pending notifications for accepter 100.");

                string desc = MyTime3.Controllers.DescriptionClass.GetDescriptionInDate(200, DateTime.Now.Date);
                Assert(desc == "توضیحات مرخصی روزانه", $"DescriptionClass.GetDescriptionInDate unexpected result: {desc}");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase4DailyLeaveAndRemainLeave()
        {
            var adapter = CreateStandardPhase4Backend();
            var settings = new TwfAppSettings
            {
                Limit1LeaveId = 3
            };
            var settingsSvc = CreateInMemorySettingsService(settings);
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();
                string tomorrowJalali = JalaliDate.FromDateTime(DateTime.Now.AddDays(1)).ToString();

                // 1. Add Daily Leave
                string addMsg = MyTime3.Controllers.DailyLeaveController.AddRequest(200, new MyTime3.Models.DailyExitRequest
                {
                    LeaveID = 1,
                    LeaveDescriptionID = 1,
                    ForDateHijri = todayJalali,
                    ToDateHijri = todayJalali,
                    Descriptions = "مرخصی یک روزه"
                }, null);
                Assert(addMsg == MyTime3.Content.PersianString.InsertSuccess,
                    $"DailyLeaveController.AddRequest should succeed, got: {addMsg}");

                var empReqs = adapter.GetEmployeeRequests(200, suspendedOnly: true);
                Assert(empReqs.Count == 1, "Expected 1 pending daily leave request.");
                long reqId = empReqs[0].RequestId;

                // 2. Verify DailyLeaveController.Edit(long id) populates ToDateHijri (legacy bug fix)
                var dlCtrl = CreateAuthenticatedController<MyTime3.Controllers.DailyLeaveController>(200, "user");
                var editGetRes = dlCtrl.Edit(reqId) as System.Web.Mvc.ViewResult;
                var editModel = editGetRes?.Model as MyTime3.Models.DailyExitRequest;
                Assert(editModel != null && editModel.ToDateHijri == todayJalali,
                    "DailyLeaveController.Edit GET must populate ToDateHijri.");

                // 3. Edit Daily Leave
                string editMsg = MyTime3.Controllers.DailyLeaveController.EditRequestDaily(200, new MyTime3.Models.DailyExitRequest
                {
                    RequestID = reqId,
                    LeaveID = 1,
                    LeaveDescriptionID = 1,
                    ForDateHijri = todayJalali,
                    ToDateHijri = tomorrowJalali,
                    Descriptions = "مرخصی دو روزه ویرایش شده"
                });
                Assert(editMsg == MyTime3.Content.PersianString.InsertSuccess,
                    $"DailyLeaveController.EditRequestDaily should succeed, got: {editMsg}");

                // 4. Limit1LeaveID enforcement (at most 1 day per month for LeaveID=3)
                string limit1MultiDay = MyTime3.Controllers.DailyLeaveController.AddRequest(201, new MyTime3.Models.DailyExitRequest
                {
                    LeaveID = 3,
                    LeaveDescriptionID = 1,
                    ForDateHijri = todayJalali,
                    ToDateHijri = tomorrowJalali,
                    Descriptions = "تست سقف ۱ روز در ماه"
                }, null);
                Assert(limit1MultiDay == MyTime3.Content.PersianString.DailyLeaveLimit,
                    $"Limit1LeaveID multi-day must return DailyLeaveLimit, got: {limit1MultiDay}");

                // 5. RemainLeaveController.GetRemain (verifies pending days & no double-counting)
                var remainCtrl = CreateAuthenticatedController<MyTime3.Controllers.RemainLeaveController>(200, "user");
                var remain = remainCtrl.GetRemain(200, 1, DateTime.Now);
                Assert(remain != null && Math.Abs(remain.Requested - 2.0) < 0.01 && Math.Abs(remain.Remain - 26.0) < 0.01,
                    $"RemainLeaveController.GetRemain expected Requested=2.0 and Remain=26.0, got Requested={remain?.Requested}, Remain={remain?.Remain}");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase4HourlyLeaveController()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings());
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();

                // 1. Add Hourly Leave (linked to LeaveID=1 via DecreaseFromLeaveId=1)
                string msg = MyTime3.Controllers.HourlyLeaveController.AddRequest(200, new MyTime3.Models.HourlyExitRequest
                {
                    LeaveID = 2,
                    LeaveDescriptionID = 1,
                    ForDateHijri = todayJalali,
                    FromTimeStr = "09:00",
                    ToTimeStr = "11:00",
                    Descriptions = "مرخصی ساعتی پزشکی"
                });
                Assert(msg == MyTime3.Content.PersianString.InsertSuccess,
                    $"HourlyLeaveController.AddRequest should succeed, got: {msg}");

                // 2. Overlap detection on same day
                string overlapMsg = MyTime3.Controllers.HourlyLeaveController.AddRequest(200, new MyTime3.Models.HourlyExitRequest
                {
                    LeaveID = 2,
                    LeaveDescriptionID = 1,
                    ForDateHijri = todayJalali,
                    FromTimeStr = "10:00",
                    ToTimeStr = "12:00",
                    Descriptions = "تداخل ساعتی"
                });
                Assert(overlapMsg == MyTime3.Content.PersianString.RequestDailyForDateExists,
                    $"Overlapping hourly leave must be rejected, got: {overlapMsg}");

                // 3. Edit Hourly Leave via EditRequestHourly
                long reqId = adapter.GetEmployeeRequests(200, suspendedOnly: true)[0].RequestId;
                string editMsg = MyTime3.Controllers.HourlyLeaveController.EditRequestHourly(200, new MyTime3.Models.HourlyExitRequest
                {
                    RequestID = reqId,
                    LeaveID = 2,
                    LeaveDescriptionID = 1,
                    ForDateHijri = todayJalali,
                    FromTimeStr = "09:30",
                    ToTimeStr = "11:30",
                    Descriptions = "ویرایش مرخصی ساعتی"
                });

                Assert(editMsg == MyTime3.Content.PersianString.InsertSuccess,
                    $"HourlyLeaveController.EditRequestHourly should succeed, got: {editMsg}");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase4DailyAndHourlyDutyControllers()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings { ValidHourlyFree = true });
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();

                // 1. DailyDuty Add & Edit
                string ddAdd = MyTime3.Controllers.DailyDutyController.AddRequest(200, new MyTime3.Models.DailyExitRequest
                {
                    LeaveID = 10,
                    LeaveDescriptionID = 2,
                    ForDateHijri = todayJalali,
                    ToDateHijri = todayJalali,
                    Descriptions = "ماموریت روزانه درون شهری"
                });
                Assert(ddAdd == MyTime3.Content.PersianString.InsertSuccess, $"DailyDuty add failed: {ddAdd}");

                long ddReqId = adapter.GetEmployeeRequests(200, suspendedOnly: true)
                    .First(r => r.WorkflowType == TwfWorkflowType.DailyDuty).RequestId;

                string ddEdit = MyTime3.Controllers.DailyDutyController.EditRequestDaily(200, new MyTime3.Models.DailyExitRequest
                {
                    RequestID = ddReqId,
                    LeaveID = 10,
                    LeaveDescriptionID = 2,
                    ForDateHijri = todayJalali,
                    ToDateHijri = todayJalali,
                    Descriptions = "ماموریت روزانه ویرایش شده"
                });
                Assert(ddEdit == MyTime3.Content.PersianString.InsertSuccess, $"DailyDuty edit failed: {ddEdit}");

                // 2. HourlyDuty Add & Edit with ValidHourlyFree = true (BUG-07 fix!)
                string hdAdd = MyTime3.Controllers.HourlyDutyController.AddRequest(200, new MyTime3.Models.HourlyExitRequest
                {
                    LeaveID = 11,
                    LeaveDescriptionID = 2,
                    ForDateHijri = todayJalali,
                    FromTimeStr = "13:00",
                    ToTimeStr = "15:00",
                    Descriptions = "ماموریت ساعتی"
                });
                Assert(hdAdd == MyTime3.Content.PersianString.InsertSuccess, $"HourlyDuty add failed: {hdAdd}");

                long hdReqId = adapter.GetEmployeeRequests(200, suspendedOnly: true)
                    .First(r => r.WorkflowType == TwfWorkflowType.HourlyDuty).RequestId;

                string hdEdit = MyTime3.Controllers.HourlyDutyController.EditRequestHourly(200, new MyTime3.Models.HourlyExitRequest
                {
                    RequestID = hdReqId,
                    LeaveID = 11,
                    LeaveDescriptionID = 2,
                    ForDateHijri = todayJalali,
                    FromTimeStr = "13:30",
                    ToTimeStr = "15:30",
                    Descriptions = "ماموریت ساعتی ویرایش شده"
                });
                Assert(hdEdit == MyTime3.Content.PersianString.InsertSuccess,
                    $"BUG-07 Fix: HourlyDutyController.EditRequestHourly with ValidHourlyFree=true must return InsertSuccess and save, got: '{hdEdit}'");

                var updatedHd = adapter.GetRequestById(hdReqId);
                Assert(updatedHd.Descriptions == "ماموریت ساعتی ویرایش شده" && updatedHd.FromTime.TimeOfDay == new TimeSpan(13, 30, 0),
                    "BUG-07 Fix: HourlyDutyController.EditRequestHourly must persist updated time and description.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase4OverTimeAndConstOTControllers()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings());
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();

                // 1. OverTime Add & Update (preserving Descriptions)
                string otAdd = MyTime3.Controllers.OverTimeController.RequestOverTime(200, new MyTime3.Models.OverTimeRequest
                {
                    ForDateHijri = todayJalali,
                    OverTimeType = 1,
                    ValidValue = "02:30",
                    Descriptions = "اضافه‌کاری استقرار نسخه جدید"
                });
                Assert(otAdd == MyTime3.Content.PersianString.InsertSuccess, $"OverTime add failed: {otAdd}");

                // Updating existing OverTime on the same date preserves or updates Descriptions without duplicate creation
                string otEdit = MyTime3.Controllers.OverTimeController.RequestOverTime(200, new MyTime3.Models.OverTimeRequest
                {
                    ForDateHijri = todayJalali,
                    OverTimeType = 1,
                    ValidValue = "03:15",
                    Descriptions = ""
                });
                Assert(otEdit == MyTime3.Content.PersianString.InsertSuccess, $"OverTime update failed: {otEdit}");

                var otReqs = adapter.GetEmployeeRequests(200, suspendedOnly: true)
                    .Where(r => r.WorkflowType == TwfWorkflowType.OverTime).ToList();
                Assert(otReqs.Count == 1 && otReqs[0].AllowDurationMinutes == 195 && otReqs[0].Descriptions == "اضافه‌کاری استقرار نسخه جدید",
                    "OverTimeController.RequestOverTime must update existing same-day request and preserve existing non-empty Descriptions.");

                // 2. ConstOTController Index, Edit, EditAll, and pre=true month boundary
                var constCtrl = CreateAuthenticatedController<MyTime3.Controllers.ConstOTController>(100, "user");
                var indexRes = constCtrl.Index(pre: true) as System.Web.Mvc.ViewResult;
                var sumList = indexRes?.Model as IEnumerable<MyTime3.Models.SumConstOT>;
                Assert(sumList != null && sumList.Any(s => s.BadgeNumber == 200),
                    "ConstOTController.Index(pre: true) must list subordinate employees without ArgumentOutOfRangeException.");

                constCtrl.EditAll(new[] { "200" }, new[] { "01:45" }, pre: false);
                var editRes = constCtrl.Edit(200, error: "", pre: false) as System.Web.Mvc.ViewResult;
                var dailyConst = (editRes?.Model as IEnumerable<MyTime3.Models.OTConstDaily>)?.ToList();
                Assert(dailyConst != null && dailyConst.Count >= 29 && dailyConst[0].ValueDaily == new TimeSpan(1, 45, 0),
                    "ConstOTController.EditAll must save constant overtime minutes for subordinate 200.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase4LateEarlyAndRecordControllers()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings
            {
                RequestRecordsDailyLimit = 2,
                RequestRecordsMonthlyLimit = 5
            });
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();
                string yesterdayJalali = JalaliDate.FromDateTime(DateTime.Now.AddDays(-1)).ToString();

                // 1. LateController Add & Edit
                string lateAdd = MyTime3.Controllers.LateController.RequestLate(200, new MyTime3.Models.LateEarlyRequest
                {
                    ForDateStr = todayJalali,
                    Descriptions = "تاخیر مجاز ترافیک"
                });
                Assert(lateAdd == MyTime3.Content.PersianString.InsertSuccess, $"Late add failed: {lateAdd}");

                long lateId = adapter.GetEmployeeRequests(200, suspendedOnly: true)
                    .First(r => r.WorkflowType == TwfWorkflowType.Late).RequestId;

                string lateEdit = MyTime3.Controllers.LateController.EditRequestLate(new MyTime3.Models.LateEarlyRequest
                {
                    RequestID = lateId,
                    ForDateStr = yesterdayJalali,
                    Descriptions = "تاخیر مجاز ویرایش شده"
                });
                Assert(lateEdit == MyTime3.Content.PersianString.InsertSuccess, $"Late edit failed: {lateEdit}");

                // 2. EarlyController Add & Edit
                string earlyAdd = MyTime3.Controllers.EarlyController.RequestEarly(200, new MyTime3.Models.LateEarlyRequest
                {
                    ForDateStr = todayJalali,
                    Descriptions = "تعجیل مجاز"
                });
                Assert(earlyAdd == MyTime3.Content.PersianString.InsertSuccess, $"Early add failed: {earlyAdd}");

                // 3. RecordController Add, Edit (verifying ForDate update alongside RecordTime), and Daily Limit
                string rec1 = MyTime3.Controllers.RecordController.RequestForRecord(new MyTime3.Models.RecordRequest
                {
                    ForDateStr = todayJalali,
                    RecordTimeStr = "08:05",
                    Descriptions = "فراموشی ثبت ورود"
                }, 200);
                Assert(rec1 == MyTime3.Content.PersianString.InsertSuccess, $"Record 1 add failed: {rec1}");

                string rec2 = MyTime3.Controllers.RecordController.RequestForRecord(new MyTime3.Models.RecordRequest
                {
                    ForDateStr = todayJalali,
                    RecordTimeStr = "16:45",
                    Descriptions = "فراموشی ثبت خروج"
                }, 200);
                Assert(rec2 == MyTime3.Content.PersianString.InsertSuccess, $"Record 2 add failed: {rec2}");

                // 3rd record on same day must hit RequestRecordsDailyLimit = 2
                string rec3 = MyTime3.Controllers.RecordController.RequestForRecord(new MyTime3.Models.RecordRequest
                {
                    ForDateStr = todayJalali,
                    RecordTimeStr = "12:00",
                    Descriptions = "تردد سوم"
                }, 200);
                Assert(rec3 == MyTime3.Content.PersianString.RequestRecordsDailyLimit,
                    $"Expected RequestRecordsDailyLimit on 3rd daily record, got: {rec3}");

                // Edit 1st record to yesterday and verify ForDate is updated along with RecordTime
                long rec1Id = adapter.GetEmployeeRequests(200, suspendedOnly: true)
                    .First(r => r.WorkflowType == TwfWorkflowType.Record).RequestId;
                string recEdit = MyTime3.Controllers.RecordController.EditRequestRecord(200, new MyTime3.Models.RecordRequest
                {
                    RequestID = rec1Id,
                    ForDateStr = yesterdayJalali,
                    RecordTimeStr = "08:15",
                    Descriptions = "ویرایش تاریخ تردد"
                });
                Assert(recEdit == MyTime3.Content.PersianString.InsertSuccess, $"Record edit failed: {recEdit}");
                var updatedRec = adapter.GetRequestById(rec1Id);
                Assert(updatedRec.ForDate.Date == updatedRec.RecordTime.Date,
                    "RecordController.EditRequestRecord must update ForDate alongside RecordTime.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase4RewardController()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings());
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                // Verify AuthorizeFilter attribute is present on RewardController.Add
                var addMethods = typeof(MyTime3.Controllers.RewardController).GetMethods()
                    .Where(m => m.Name == "Add").ToList();
                Assert(addMethods.All(m => m.GetCustomAttributes(typeof(MyTime3.AuthorizeFilter), true).Length > 0),
                    "RewardController.Add GET/POST must be protected by [AuthorizeFilter].");

                var rewardCtrl = CreateAuthenticatedController<MyTime3.Controllers.RewardController>(100, "user");
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();

                var postRes = rewardCtrl.Add(new MyTime3.Models.RewardPenaltyRequest
                {
                    ForBadgeNumber = 200,
                    ForDateHijri = todayJalali,
                    RewardPenaltyType = 7,
                    ValidValue = "02:00",
                    Descriptions = "تشویق عملکرد"
                }) as System.Web.Mvc.ViewResult;

                Assert(postRes != null && (postRes.ViewBag.Message as string) == MyTime3.Content.PersianString.InsertSuccess,
                    "RewardController.Add POST must succeed for subordinate 200.");

                var rewardReq = adapter.GetEmployeeRequests(200, suspendedOnly: true)
                    .FirstOrDefault(r => r.WorkflowType == TwfWorkflowType.RewardPenalty);
                Assert(rewardReq != null && rewardReq.AcceptedSteps.Count == 1 && rewardReq.AcceptedSteps[0].AccepterBadgeNumber == 100,
                    "Multi-step RewardPenalty request must record manager 100 as first approval step.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase4UnifiedRequestController()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings());
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                var reqCtrl = CreateAuthenticatedController<MyTime3.Controllers.RequestController>(200, "user");
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();

                // 1. Add OverTime via Unified RequestController.GeneralRequestWeb
                var addOtJson = reqCtrl.GeneralRequestWeb(new MyTime3.Models.GeneralRequest
                {
                    IsNew = true,
                    RequestID = -1,
                    RequestType = 7,
                    LeaveTypeID = 1,
                    ForDateHijri = todayJalali,
                    OvertimeValue = "01:30",
                    Descriptions = "اضافه‌کار از درگاه یکپارچه وب"
                });
                Assert(addOtJson != null && addOtJson.Data != null,
                    "RequestController.GeneralRequestWeb(OverTime) must return JsonResult.");

                var otReqs = adapter.GetEmployeeRequests(200, suspendedOnly: true)
                    .Where(r => r.WorkflowType == TwfWorkflowType.OverTime).ToList();
                Assert(otReqs.Count == 1, "Expected 1 OverTime request created via GeneralRequestWeb.");

                // 2. GeneralRequest (Zero-Lock mobile endpoint) & SendPosition null-guard
                var mobileJson = reqCtrl.GeneralRequest(new MyTime3.Models.GeneralRequest
                {
                    IsNew = true,
                    RequestID = -1,
                    UserName = "emp1",
                    Password = "Emp@1234",
                    RequestType = 5,
                    ForDateHijri = todayJalali,
                    Descriptions = "تاخیر موبایل"
                });
                Assert(mobileJson != null && mobileJson.Data != null,
                    "RequestController.GeneralRequest must succeed with Zero-Lock.");

                var locJson = reqCtrl.SendPosition(new MyTime3.Models.GeneralRequest
                {
                    UserName = "emp1",
                    Password = "Emp@1234",
                    Latitude = 35.6892,
                    Longitude = 51.3890
                });
                Assert(locJson != null && locJson.Data != null,
                    "RequestController.SendPosition must succeed without NullReferenceException.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase4EditRequestController()
        {
            var adapter = CreateStandardPhase4Backend();
            var settings = new TwfAppSettings();
            var settingsSvc = CreateInMemorySettingsService(settings);
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();
                MyTime3.Controllers.DailyLeaveController.AddRequest(200, new MyTime3.Models.DailyExitRequest
                {
                    LeaveID = 1,
                    LeaveDescriptionID = 1,
                    ForDateHijri = todayJalali,
                    ToDateHijri = todayJalali,
                    Descriptions = "مرخصی قابل ویرایش"
                }, null);
                MyTime3.Controllers.LateController.RequestLate(200, new MyTime3.Models.LateEarlyRequest
                {
                    ForDateStr = todayJalali,
                    Descriptions = "تاخیر قابل حذف"
                });

                var editCtrl = CreateAuthenticatedController<MyTime3.Controllers.EditRequestController>(200, "user");

                // 1. Index lists editable requests
                var indexView = editCtrl.Index() as System.Web.Mvc.ViewResult;
                var list = (indexView?.Model as IEnumerable<MyTime3.Models.Request>)?.ToList();
                Assert(list != null && list.Count == 2, $"Expected 2 editable requests in EditRequestController.Index, got {list?.Count}.");

                // 2. EditByID(requestID) routes to RedirectToRouteResult
                long firstId = list[0].RequestID;
                var editRouteRes = editCtrl.EditByID(firstId);
                Assert(editRouteRes != null, "EditRequestController.EditByID must return a valid ActionResult.");

                // 3. Delete(requestID) removes editable request
                editCtrl.Delete(firstId);
                var remaining = adapter.GetEmployeeRequests(200, suspendedOnly: true);
                Assert(remaining.Count == 1, "EditRequestController.Delete must delete the specified editable request.");

                // 4. CheckRequestTime enforces RequestsLock OA-date cutoff
                settings.RequestsLockOADate = DateTime.Now.AddDays(5).ToOADate();
                settingsSvc.SaveSettings(settings);
                string lockCheck = MyTime3.Controllers.EditRequestController.CheckRequestTime(DateTime.Now.Date);
                Assert(lockCheck == MyTime3.Content.PersianString.RequestsLockInfo,
                    $"EditRequestController.CheckRequestTime must block requests on or before RequestsLock OA date, got: {lockCheck}");
                settings.RequestsLockOADate = -1;
                settingsSvc.SaveSettings(settings);
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase4ViewsAndControllerCompleteness()
        {
            string twfRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            string webRoot = Path.Combine(twfRoot, "TWF.Web");

            string[] phase4Views =
            {
                "Views/Home/Index.cshtml",
                "Views/Home/About.cshtml",
                "Views/Home/NoUserInfoError.cshtml",
                "Views/Home/WorkReport.cshtml",
                "Views/Home/WorkReport2.cshtml",
                "Views/Request/New.cshtml",
                "Views/Request/FromUser.cshtml",
                "Views/EditRequest/Index.cshtml",
                "Views/DailyLeave/Add.cshtml",
                "Views/DailyLeave/Edit.cshtml",
                "Views/HourlyLeave/Add.cshtml",
                "Views/HourlyLeave/Edit.cshtml",
                "Views/DailyDuty/Add.cshtml",
                "Views/DailyDuty/Edit.cshtml",
                "Views/HourlyDuty/Add.cshtml",
                "Views/HourlyDuty/Edit.cshtml",
                "Views/OverTime/Add.cshtml",
                "Views/OverTime/Edit.cshtml",
                "Views/ConstOT/Index.cshtml",
                "Views/ConstOT/Edit.cshtml",
                "Views/Late/Add.cshtml",
                "Views/Late/Edit.cshtml",
                "Views/Early/Add.cshtml",
                "Views/Early/Edit.cshtml",
                "Views/Record/Add.cshtml",
                "Views/Record/Edit.cshtml",
                "Views/Reward/Add.cshtml",
                "Views/RemainLeave/Index.cshtml",
                "Views/RemainLeave/AllUsersRemainLeave.cshtml"
            };

            foreach (string relView in phase4Views)
            {
                string fullPath = Path.Combine(webRoot, relView);
                Assert(File.Exists(fullPath), $"Required Phase 4 Razor view missing: {relView}");
                string content = File.ReadAllText(fullPath);
                Assert(!content.Contains("http://") && !content.Contains("https://"),
                    $"100% Offline violation: {relView} contains an external http/https URL.");
            }

            // Verify HourlyLeave/Edit.cshtml uses ViewBag.LeaveDescriptions (fixing legacy ViewBag.DutyDescriptions bug)
            string hourlyLeaveEdit = File.ReadAllText(Path.Combine(webRoot, "Views/HourlyLeave/Edit.cshtml"));
            Assert(hourlyLeaveEdit.Contains("ViewBag.LeaveDescriptions") && !hourlyLeaveEdit.Contains("ViewBag.DutyDescriptions"),
                "Views/HourlyLeave/Edit.cshtml must bind ViewBag.LeaveDescriptions instead of ViewBag.DutyDescriptions.");
        }

        private static void TestPhase5AcceptRequestController()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings());
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();

                // 1. Create a 2-step Daily Leave request from employee 200 (Title 30 -> Step 1: Title 20 [Badge 100], Step 2: Title 10 [Badge 50])
                MyTime3.Controllers.DailyLeaveController.AddRequest(200, new MyTime3.Models.DailyExitRequest
                {
                    LeaveID = 1,
                    LeaveDescriptionID = 1,
                    ForDateHijri = todayJalali,
                    ToDateHijri = todayJalali,
                    Descriptions = "مرخصی جهت تایید دو مرحله‌ای"
                }, null);

                long reqId = adapter.GetEmployeeRequests(200, suspendedOnly: true)[0].RequestId;

                // 2. Manager 100 (Title 20) sees the request in AcceptRequestController.Index
                var mgr1Ctrl = CreateAuthenticatedController<MyTime3.Controllers.AcceptRequestController>(100, "user");
                var idx1View = mgr1Ctrl.Index() as System.Web.Mvc.ViewResult;
                var idx1List = (idx1View?.Model as IEnumerable<MyTime3.Models.Request>)?.ToList();
                Assert(idx1List != null && idx1List.Count == 1 && idx1List[0].RequestID == reqId && !idx1List[0].ForDeputy,
                    "Manager 100 must see pending request in AcceptRequestController.Index.");

                // 3. Step 1 approval by Manager 100 (even if Manager 100 had negative leave quota, BUG-08 fix checks requester 200's quota!)
                adapter.SeedAnnualLeaveEntitlement(100, 1, JalaliDate.FromDateTime(DateTime.Now).Year, -50000);
                var accept1Res = mgr1Ctrl.Accept(reqId, null);
                Assert(accept1Res != null, "Step 1 Accept must succeed.");
                Assert(adapter.GetRequestById(reqId).IsSuspended, "After Step 1 approval, 2-step request must remain suspended awaiting Step 2.");

                // 4. Assign Manager 100 as deputy for Director 50 (Title 10) and verify ForDeputy=true in Index
                adapter.AssignPersonnelDeputy(50, 100);
                var idx2View = mgr1Ctrl.Index() as System.Web.Mvc.ViewResult;
                var idx2List = (idx2View?.Model as IEnumerable<MyTime3.Models.Request>)?.ToList();
                Assert(idx2List != null && idx2List.Count == 1 && idx2List[0].ForDeputy,
                    "Manager 100 acting as deputy for Director 50 must see Step 2 request with ForDeputy=true.");

                // 5. Final approval (Step 2) via deputy 100 -> inserts operational DailyLeave and sets IsSuspended=false
                mgr1Ctrl.Accept(reqId, null);
                var finalReq = adapter.GetRequestById(reqId);
                Assert(!finalReq.IsSuspended, "After final approval, request must have IsSuspended=false.");
                Assert(adapter.GetApprovedDailyLeaves(200, DateTime.Now.Date, DateTime.Now.Date).Count == 1,
                    "Final approval must insert DailyLeave into operational table.");

                // 6. Verify Mobile API CancelRequest records actual accepter badgeNumber (BUG-09 fix!)
                string tomorrowJalali = JalaliDate.FromDateTime(DateTime.Now.AddDays(1)).ToString();
                MyTime3.Controllers.DailyDutyController.AddRequest(200, new MyTime3.Models.DailyExitRequest
                {
                    LeaveID = 10,
                    LeaveDescriptionID = 2,
                    ForDateHijri = tomorrowJalali,
                    ToDateHijri = tomorrowJalali,
                    Descriptions = "ماموریت جهت تست رد موبایلی"
                });
                long dutyReqId = adapter.GetEmployeeRequests(200, suspendedOnly: true)[0].RequestId;

                var unauthMobileCtrl = new MyTime3.Controllers.AcceptRequestController();
                var cancelJson = unauthMobileCtrl.CancelRequest("manager", "Mgr@1234", dutyReqId);
                Assert(cancelJson != null, "Mobile CancelRequest must return JsonResult.");
                var history = adapter.GetRequestAcceptHistory(dutyReqId);
                Assert(history.Count == 1 && history[0].IsCancel && history[0].AccepterBadgeNumber == 100,
                    $"BUG-09 Fix: Mobile CancelRequest must record AccepterBadgeNumber=100, got {history.FirstOrDefault()?.AccepterBadgeNumber}.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase5DeputyController()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings());
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                var depCtrl = CreateAuthenticatedController<MyTime3.Controllers.DeputyController>(50, "user");

                // 1. Initially no deputy
                var idxView1 = depCtrl.Index() as System.Web.Mvc.ViewResult;
                var model1 = idxView1?.Model as MyTime3.Models.Deputy;
                Assert(model1 != null && !model1.IsAnyDeputy && model1.ValidList.Count > 0,
                    "DeputyController.Index must list valid deputy candidates from DeputyDA.");

                // 2. Add deputy 100
                depCtrl.Add(100);
                var idxView2 = depCtrl.Index() as System.Web.Mvc.ViewResult;
                var model2 = idxView2?.Model as MyTime3.Models.Deputy;
                Assert(model2 != null && model2.IsAnyDeputy && model2.CurrentDeputy.BadgeNumber == 100,
                    "DeputyController.Add must assign active deputy 100.");

                // 3. Mobile API GetState
                var stateJson = depCtrl.GetState("director", "Dir@1234", "false", "-1");
                var stateModel = stateJson?.Data as MyTime3.Models.Deputy;
                Assert(stateModel != null && stateModel.IsAnyDeputy && stateModel.CurrentDeputy.BadgeNumber == 100,
                    "DeputyController.GetState must return active deputy state.");

                // 4. Delete deputy
                depCtrl.Delete(false);
                Assert(!adapter.GetActiveDeputyForPrincipal(50).HasValue,
                    "DeputyController.Delete must remove active deputy assignment.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase5UndoController()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings());
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();

                // Create and fully approve a Record request (Employee 200 -> Manager 100 -> Director 50)
                MyTime3.Controllers.RecordController.RequestForRecord(new MyTime3.Models.RecordRequest
                {
                    ForDateStr = todayJalali,
                    RecordTimeStr = "08:15",
                    Descriptions = "تردد فراموش شده"
                }, 200);
                long recReqId = adapter.GetEmployeeRequests(200, suspendedOnly: true)[0].RequestId;

                var accept1Ctrl = CreateAuthenticatedController<MyTime3.Controllers.AcceptRequestController>(100, "user");
                accept1Ctrl.Accept(recReqId, null);
                var accept2Ctrl = CreateAuthenticatedController<MyTime3.Controllers.AcceptRequestController>(50, "user");
                accept2Ctrl.Accept(recReqId, null);
                Assert(!adapter.GetRequestById(recReqId).IsSuspended, "Record request should be approved.");
                Assert(adapter.GetOperationalRecordsCount(200, TwfWorkflowType.Record, DateTime.Now.Date) == 1,
                    "Approved record request must exist in operational table.");

                // Director 50 views UndoController.Index and performs Undo
                var undoCtrl = CreateAuthenticatedController<MyTime3.Controllers.UndoController>(50, "user");
                var undoIdx = undoCtrl.Index() as System.Web.Mvc.ViewResult;
                var undoList = (undoIdx?.Model as IEnumerable<MyTime3.Models.Request>)?.ToList();
                Assert(undoList != null && undoList.Any(r => r.RequestID == recReqId),
                    "UndoController.Index must list the approved request for accepter 50.");

                undoCtrl.Undo(recReqId);
                var undoneReq = adapter.GetRequestById(recReqId);
                Assert(undoneReq.IsSuspended && undoneReq.Descriptions.Contains("بازگشت داده شده"),
                    "UndoController.Undo must revert request to IsSuspended=true and append undo note.");
                Assert(adapter.GetOperationalRecordsCount(200, TwfWorkflowType.Record, DateTime.Now.Date) == 0,
                    "UndoController.Undo must remove operational Record entry.");

                // Calling Undo on non-existent request ID must not throw NullReferenceException
                undoCtrl.Undo(999999L);
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase5ShowRequestController()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings { RequestForUsers = true });
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();

                MyTime3.Controllers.DailyLeaveController.AddRequest(200, new MyTime3.Models.DailyExitRequest
                {
                    LeaveID = 1,
                    LeaveDescriptionID = 1,
                    ForDateHijri = todayJalali,
                    ToDateHijri = todayJalali,
                    Descriptions = "مرخصی اول - بازگشت داده شده"
                }, null);

                var showCtrl = CreateAuthenticatedController<MyTime3.Controllers.ShowRequestController>(200, "user");
                var viewRes = showCtrl.Index(todayJalali, todayJalali) as System.Web.Mvc.ViewResult;
                var model = viewRes?.Model as MyTime3.Models.ShowRequestModel;
                Assert(model != null && model.Requests.Count == 1,
                    "ShowRequestController.Index must return requests within the Hijri date range.");

                // Verify legacy bug fix: "بازگشت داده شده توسط" is appended ONCE, not twice!
                string waitText = model.Requests[0].WaitForTitle ?? "";
                int firstIdx = waitText.IndexOf("بازگشت داده شده توسط", StringComparison.Ordinal);
                int lastIdx = waitText.LastIndexOf("بازگشت داده شده توسط", StringComparison.Ordinal);
                Assert(firstIdx >= 0 && firstIdx == lastIdx,
                    $"ShowRequestController.Index must append 'بازگشت داده شده توسط' exactly once, got: {waitText}");

                // Mobile API GetRequests
                var mobileJson = showCtrl.GetRequests("emp1", "Emp@1234", 0);
                var mobileList = mobileJson?.Data as List<MyTime3.Models.Request>;
                Assert(mobileList != null && mobileList.Count == 1,
                    "ShowRequestController.GetRequests must return current month requests.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase5ShowUsersRequestAndViewR()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings());
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                // 1. Admin calls ShowUsersRequestController.Index (where GetLoggedInUserInfo is null for admin)
                var adminCtrl = CreateAuthenticatedController<MyTime3.Controllers.ShowUsersRequestController>(0, "admin");
                var idxRes = adminCtrl.Index(200) as System.Web.Mvc.ViewResult;
                Assert(idxRes != null && (long)idxRes.Model == 200L,
                    "ShowUsersRequestController.Index must not throw NullReferenceException when logged in as admin.");

                // 2. GetForUser returns HTML-encoded status table
                adminCtrl.Request.Params["badg"] = "200";
                var contentRes = adminCtrl.GetForUser();
                Assert(contentRes != null && contentRes.Content.Contains("مشخصات کاربر") && contentRes.Content.Contains("مدیر فنی"),
                    $"ShowUsersRequestController.GetForUser must return user org-chart status HTML, got: {contentRes?.Content}");

                // 3. ViewRController.Index
                var viewRCtrl = new MyTime3.Controllers.ViewRController();
                Assert(viewRCtrl.Index() is System.Web.Mvc.ViewResult, "ViewRController.Index must return ViewResult.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase5PersonnelStatusAndLeavesReport()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings());
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();

                // Insert 1 record for employee 200 (odd record -> PresentOddRecord) and 0 records for 201 (AbsentWORecord)
                adapter.InsertApprovedOperationalData(new WorkflowRequestItemDto
                {
                    BadgeNumber = 200,
                    WorkflowType = TwfWorkflowType.Record,
                    ForDate = DateTime.Now.Date,
                    RecordTime = DateTime.Now.Date.AddHours(8)
                }, 100);

                var psCtrl = CreateAuthenticatedController<MyTime3.Controllers.PersonnelStatusController>(100, "user");
                var allRes = (psCtrl.Index(todayJalali, 1) as System.Web.Mvc.ViewResult)?.Model as MyTime3.Models.PerosnnelStatusModel;
                var oddRes = (psCtrl.Index(todayJalali, 3) as System.Web.Mvc.ViewResult)?.Model as MyTime3.Models.PerosnnelStatusModel;
                var woRes = (psCtrl.Index(todayJalali, 2) as System.Web.Mvc.ViewResult)?.Model as MyTime3.Models.PerosnnelStatusModel;

                Assert(allRes != null && allRes.Rows.Count >= 2, "PersonnelStatusController rType=1 must return all subordinate rows.");
                Assert(oddRes != null && oddRes.Rows.Count == 1 && oddRes.Rows[0].BadgeNumber == 200,
                    "PersonnelStatusController rType=3 must return employee 200 with odd record count.");
                Assert(woRes != null && woRes.Rows.Any(r => r.BadgeNumber == 201),
                    "PersonnelStatusController rType=2 must return employee 201 without records.");

                // LeavesReportController (Guard Observer = Badge 300, Title 40)
                adapter.InsertApprovedOperationalData(new WorkflowRequestItemDto
                {
                    BadgeNumber = 200,
                    WorkflowType = TwfWorkflowType.DailyLeave,
                    IsDaily = true,
                    LeaveTypeId = 1,
                    ForDate = DateTime.Now.Date,
                    FromTime = DateTime.Now.Date,
                    ToTime = DateTime.Now.Date,
                    Descriptions = "مرخصی روزانه جهت گزارش انتظامات"
                }, 50);

                var obsCtrl = CreateAuthenticatedController<MyTime3.Controllers.LeavesReportController>(300, "user");
                Assert(MyTime3.Controllers.LeavesReportController.isObs(obsCtrl.HttpContext),
                    "LeavesReportController.isObs must return true for observer title 40.");
                var lvView = obsCtrl.Index() as System.Web.Mvc.ViewResult;
                var lvList = (lvView?.Model as IEnumerable<MyTime3.Controllers.LeavesReportController.LV>)?.ToList();
                Assert(lvList != null && lvList.Any(l => l.BadgeNumber == 200),
                    "LeavesReportController.Index must list today's leaves for guard observer.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase5LocationReportAndSelectArea()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings());
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();

                // 1. LocationReportController.GetReport with invalid date must NOT throw InvalidOperationException (legacy flag=true bug fix!)
                var locCtrl = CreateAuthenticatedController<MyTime3.Controllers.LocationReportController>(100, "user");
                var badDateJson = locCtrl.GetReport("emp1", "Emp@1234", "invalid-date", todayJalali, new List<long> { 200 }, true);
                Assert(badDateJson != null && badDateJson.Data != null,
                    "LocationReportController.GetReport must return error JSON on invalid date instead of crashing on nullable.Value.");

                var validLocJson = locCtrl.GetReport("emp1", "Emp@1234", todayJalali, todayJalali, new List<long> { 200 }, true);
                var locData = validLocJson?.Data as List<T5V3.Model.UserPosition>;
                Assert(locData != null && locData.Count == 1 && locData[0].BadgeNumber == 200,
                    "LocationReportController.GetReport must return UserPosition list for valid dates.");

                // 2. SelectAreaController polygon CRUD
                var areaCtrl = CreateAuthenticatedController<MyTime3.Controllers.SelectAreaController>(100, "admin");
                var tooFewPts = areaCtrl.InsertNew(200, new List<MyTime3.Models.LatLng>
                {
                    new MyTime3.Models.LatLng { lat = 35.68, lng = 51.38 }
                });
                Assert(tooFewPts != null, "SelectAreaController.InsertNew with <3 points must return error JSON.");

                var addAreaJson = areaCtrl.InsertNew(200, new List<MyTime3.Models.LatLng>
                {
                    new MyTime3.Models.LatLng { lat = 35.6892, lng = 51.3890 },
                    new MyTime3.Models.LatLng { lat = 35.6902, lng = 51.3900 },
                    new MyTime3.Models.LatLng { lat = 35.6882, lng = 51.3910 }
                });
                var areasList = (areaCtrl.GetAreas()?.Data as List<T5V3.Model.AreaModel>);
                Assert(areasList != null && areasList.Count >= 1 && areasList[0].Points.Count == 3,
                    "SelectAreaController.InsertNew and GetAreas must persist and return 3-point polygon area.");

                int areaId = areasList[0].AreaID;
                var singleArea = areaCtrl.GetArea(areaId)?.Data as T5V3.Model.AreaModel;
                Assert(singleArea != null && singleArea.AreaID == areaId, "SelectAreaController.GetArea must return area by ID.");

                areaCtrl.DeleteArea(areaId);
                var afterDelete = areaCtrl.GetAreas()?.Data as List<T5V3.Model.AreaModel>;
                Assert(afterDelete != null && afterDelete.All(a => a.AreaID != areaId),
                    "SelectAreaController.DeleteArea must delete the specified area.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase5NotifyController()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings());
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();

                MyTime3.Controllers.DailyLeaveController.AddRequest(200, new MyTime3.Models.DailyExitRequest
                {
                    LeaveID = 1,
                    LeaveDescriptionID = 1,
                    ForDateHijri = todayJalali,
                    ToDateHijri = todayJalali,
                    Descriptions = "مرخصی جهت تست اعلان XML"
                }, null);

                var notifyCtrl = new MyTime3.Controllers.NotifyController();
                string xml = notifyCtrl.Index("manager", "Mgr@1234");
                Assert(!string.IsNullOrEmpty(xml) && xml.Contains("<ArrayOfNotification") && xml.Contains(MyTime3.Content.PersianString.RequestDailyLeave),
                    $"NotifyController.Index must return valid XML containing pending request notifications, got: {xml}");

                notifyCtrl.SetViewed("manager", "Mgr@1234", 1);
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase5OverTimeAndReportControllers()
        {
            var adapter = CreateStandardPhase4Backend();
            var settingsSvc = CreateInMemorySettingsService(new TwfAppSettings());
            MyTime3.Controllers.DBInitializer.EnableTestMode(adapter, settingsSvc);
            try
            {
                string todayJalali = JalaliDate.FromDateTime(DateTime.Now).ToString();

                // 1. OverTimeReportController
                var otRptCtrl = CreateAuthenticatedController<MyTime3.Controllers.OverTimeReportController>(100, "user");
                Assert(MyTime3.Controllers.OverTimeReportController.HasAnyUsers(otRptCtrl.HttpContext),
                    "OverTimeReportController.HasAnyUsers must return true for manager 100.");

                var showRes = otRptCtrl.Show(new[] { "200" }, todayJalali, todayJalali, true) as System.Web.Mvc.ViewResult;
                var showList = (showRes?.Model as IEnumerable<MyTime3.Models.OverTimeForDay>)?.ToList();
                Assert(showList != null && showList.Count == 1 && showList[0].BadgeNumber == 200,
                    "OverTimeReportController.Show must return daily overtime rows.");

                // 2. ReportController (Zero-Lock Mobile API + BUG-10 OverTimeConst Fix + Export)
                adapter.UpsertConstantOvertime(200, DateTime.Now.Date, 120); // 2.0 hours constant overtime

                var rptCtrl = CreateAuthenticatedController<MyTime3.Controllers.ReportController>(100, "user");
                var persJson = rptCtrl.GetPersonnel(new MyTime3.Models.LoginModel { UserName = "manager", Password = "Mgr@1234" });
                var persList = persJson?.Data as List<MyTime3.Models.PersonnelModel>;
                Assert(persList != null && persList.Count >= 2,
                    "ReportController.GetPersonnel must work without hardware lock (Zero-Lock).");

                var totalJson = rptCtrl.TotalCalcsReport("emp1", "Emp@1234", "200", todayJalali, todayJalali);
                var totalList = totalJson?.Data as List<MyTime3.Models.TotalCalcsModel>;
                Assert(totalList != null && totalList.Count == 1 && Math.Abs(totalList[0].OverTimeConst - 2.0) < 0.01,
                    $"BUG-10 Fix: ReportController.TotalCalcsReport must populate OverTimeConst=2.0, got {totalList?.FirstOrDefault()?.OverTimeConst}.");

                var dailyJson = rptCtrl.DailyReport("emp1", "Emp@1234", "200", todayJalali, todayJalali);
                var dailyList = dailyJson?.Data as List<MyTime3.Controllers.ReportController.DayResult>;
                Assert(dailyList != null && dailyList.Count == 1,
                    "ReportController.DailyReport must return daily attendance summary without hardware lock.");

                var exportFile = rptCtrl.Report(new MyTime3.Models.ReportModel
                {
                    ReportID = 1,
                    FromDateHijri = todayJalali,
                    ToDateHijri = todayJalali,
                    IsSelf = true
                }, MyTime3.Content.PersianString.DownloadExportFile, "EXCEL") as System.Web.Mvc.FileContentResult;
                Assert(exportFile != null && exportFile.FileDownloadName == "report.xls" && exportFile.FileContents.Length > 20,
                    "ReportController.Report export action must generate downloadable report file offline.");
            }
            finally
            {
                MyTime3.Controllers.DBInitializer.DisableTestMode();
            }
        }

        private static void TestPhase5FullProjectCompletenessAudit()
        {
            string twfRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            string repoRoot = Path.GetFullPath(Path.Combine(twfRoot, "../.."));
            string webRoot = Path.Combine(twfRoot, "TWF.Web");
            string td5Root = Path.Combine(twfRoot, "TD5");
            string origTd5Root = Path.Combine(twfRoot, "OriginalDecompiled", "TD5");
            string origMyTimeRoot = Path.Combine(twfRoot, "OriginalDecompiled", "MyTime3");
            string legacyViewsRoot = Path.Combine(repoRoot, "TWF 4.28", "App", "Views");

            // 1. Verify all 13 TD5 C# files exist in rewritten TD5 project
            int td5Count = Directory.GetFiles(td5Root, "*.cs", SearchOption.AllDirectories).Length;
            int origTd5Count = Directory.GetFiles(origTd5Root, "*.cs", SearchOption.AllDirectories).Length;
            Assert(td5Count == 13 && td5Count == origTd5Count,
                $"TD5 rewrite must have all 13 C# files (found {td5Count}).");

            // 2. Verify all 34 Controllers and all 33 Models exist in rewritten TWF.Web project
            int rewrittenControllers = Directory.GetFiles(Path.Combine(webRoot, "Controllers"), "*.cs").Length;
            int origControllers = Directory.GetFiles(Path.Combine(origMyTimeRoot, "MyTime3.Controllers"), "*.cs").Length;
            // Aria adds exactly two files on top of the 34 legacy controllers:
            //   ConnectionStringResolver (config helper) and DiagnosticsController (schema self-check).
            Assert(rewrittenControllers == origControllers + 2,
                $"TWF.Web/Controllers must contain the 34 legacy controllers plus 2 Aria files (found {rewrittenControllers} vs {origControllers}).");
            foreach (string legacyController in Directory.GetFiles(Path.Combine(origMyTimeRoot, "MyTime3.Controllers"), "*.cs"))
            {
                string controllerName = Path.GetFileName(legacyController);
                Assert(File.Exists(Path.Combine(webRoot, "Controllers", controllerName)),
                    $"Missing rewritten controller for legacy file '{controllerName}'.");
            }

            int rewrittenModels = Directory.GetFiles(Path.Combine(webRoot, "Models"), "*.cs").Length;
            int origModels = Directory.GetFiles(Path.Combine(origMyTimeRoot, "MyTime3.Models"), "*.cs").Length;
            Assert(rewrittenModels >= origModels && origModels == 33,
                $"TWF.Web/Models must contain all 33 original models (found {rewrittenModels} vs {origModels}).");

            // 2b. Additive-only guarantee: every legacy model file has a rewritten counterpart,
            //     and the Aria layer may only add new files on top (e.g. NewRequestForm).
            foreach (string legacyModel in Directory.GetFiles(Path.Combine(origMyTimeRoot, "MyTime3.Models"), "*.cs"))
            {
                string modelName = Path.GetFileName(legacyModel);
                Assert(File.Exists(Path.Combine(webRoot, "Models", modelName)),
                    $"Missing rewritten model for legacy file '{modelName}'.");
            }
            Assert(File.Exists(Path.Combine(webRoot, "Models", "NewRequestForm.cs")),
                "Aria request dialog requires Models/NewRequestForm.cs.");

            // 3. Verify zero TINYLib files exist in TWF.Web (Zero-Lock mandate)
            Assert(!Directory.Exists(Path.Combine(webRoot, "TINYLib")),
                "Zero-Lock violation: TINYLib directory must not exist in rewritten TWF.Web.");

            // 4. Verify all 60 legacy views in TWF 4.28/App/Views exist in rewritten TWF.Web/Views and all 74 views are 100% offline
            var legacyViews = Directory.GetFiles(legacyViewsRoot, "*.*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Substring(legacyViewsRoot.Length).TrimStart('/', '\\'))
                .ToList();
            Assert(legacyViews.Count == 60, $"Expected 60 legacy views in TWF 4.28/App/Views, found {legacyViews.Count}.");

            foreach (string relPath in legacyViews)
            {
                string rewrittenPath = Path.Combine(webRoot, "Views", relPath);
                Assert(File.Exists(rewrittenPath), $"Missing rewritten view for legacy view: {relPath}");
            }

            var allRewrittenViews = Directory.GetFiles(Path.Combine(webRoot, "Views"), "*.*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert(allRewrittenViews.Count >= 74, $"Expected at least 74 rewritten views in TWF.Web/Views, found {allRewrittenViews.Count}.");

            foreach (string viewFile in allRewrittenViews)
            {
                string content = File.ReadAllText(viewFile);
                Assert(!content.Contains("http://") && !content.Contains("https://") && !content.Contains("maps.googleapis.com"),
                    $"100% Offline violation in view {Path.GetFileName(viewFile)}: external URL or Google Maps script detected.");
            }
        }
    
        /// <summary>
        /// TEST-41 — Guards the ARIA UI contract:
        ///  1. No external HTTP(S) asset reference anywhere in Views/Scripts/Content.
        ///  2. Every twf-* CSS class used by the rewritten views is actually defined.
        ///  3. The vanilla request-dialog engine exposes the globals the views call.
        ///  4. The unified request form model/actions exist and are wired.
        /// </summary>
        private static void TestAriaOfflineUiContract()
        {
            string twfRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            string webRoot = Path.Combine(twfRoot, "TWF.Web");
            string viewsRoot = Path.Combine(webRoot, "Views");
            string scriptsRoot = Path.Combine(webRoot, "Scripts");
            string contentRoot = Path.Combine(webRoot, "Content");

            // 1. Zero external asset references (100% offline mandate).
            var externalRefs = new List<string>();
            foreach (string root in new[] { viewsRoot, scriptsRoot, contentRoot })
            {
                foreach (string file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    if (!file.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) &&
                        !file.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase) &&
                        !file.EndsWith(".js", StringComparison.OrdinalIgnoreCase) &&
                        !file.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (string line in File.ReadAllLines(file))
                    {
                        if (line.Contains("http://") || line.Contains("https://") || line.Contains("//cdn."))
                        {
                            // XML namespace declarations are not asset fetches.
                            if (line.Contains("www.w3.org") || line.Contains("schemas.microsoft.com") ||
                                line.Contains("schemas.openxmlformats.org"))
                            {
                                continue;
                            }
                            externalRefs.Add(file.Substring(webRoot.Length + 1) + " -> " + line.Trim());
                        }
                    }
                }
            }
            Assert(externalRefs.Count == 0,
                "Offline violation, external asset reference found: " + string.Join(" | ", externalRefs.Take(5)));

            // 2. CSS class coverage — every twf-* class used by a view must be styled.
            string css = File.ReadAllText(Path.Combine(contentRoot, "twf-industrial-ui.css"));
            var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(css, @"\.(twf-[A-Za-z0-9_-]+)"))
            {
                defined.Add(m.Groups[1].Value);
            }

            // CSS custom properties (--twf-xxx) are variables, not classes: they must be
            // defined in :root instead, and are validated separately below.
            var definedVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(css, @"(--twf-[A-Za-z0-9_-]+)\s*:"))
            {
                definedVars.Add(m.Groups[1].Value);
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string view in Directory.GetFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(view);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"(--twf-[A-Za-z0-9_-]+)"))
                {
                    usedVars.Add(m.Groups[1].Value);
                }
                // Strip the variable references so they are not mistaken for class names.
                text = System.Text.RegularExpressions.Regex.Replace(text, @"--twf-[A-Za-z0-9_-]+", " ");
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"twf-[A-Za-z0-9_-]+"))
                {
                    used.Add(m.Value);
                }
            }

            var undefinedVars = usedVars.Where(v => !definedVars.Contains(v)).OrderBy(v => v).ToList();
            Assert(undefinedVars.Count == 0,
                "Views reference undefined CSS variables: " + string.Join(", ", undefinedVars));
            // Script handles / file names are not CSS classes.
            used.Remove("twf-core");
            used.Remove("twf-industrial-ui");
            used.Remove("twf-industrial");

            var unstyled = used.Where(c => !defined.Contains(c)).OrderBy(c => c).ToList();
            Assert(unstyled.Count == 0, "Unstyled twf-* classes referenced by views: " + string.Join(", ", unstyled));

            // 3. Vanilla JS engine contract (no jQuery / Kendo dependency).
            string core = File.ReadAllText(Path.Combine(scriptsRoot, "twf-core.js"));
            foreach (string required in new[]
                     {
                         "window.showRequest", "window.sendRequest", "window.showValidTypes",
                         "TWF.ajaxJson", "TWF.toggleSelectAll", "TWF.openModal", "TWF.closeModal"
                     })
            {
                Assert(core.Contains(required), "twf-core.js must expose '" + required + "'.");
            }
            Assert(!core.Contains("$.ajax") && !core.Contains("jQuery(") && !core.Contains("$("),
                "twf-core.js must stay framework-free (no jQuery calls).");

            string layout = File.ReadAllText(Path.Combine(viewsRoot, "Shared", "_Layout1.cshtml"));
            Assert(layout.Contains("~/Content/twf-industrial-ui.css") && layout.Contains("~/Scripts/twf-core.js"),
                "_Layout1 must load exactly the local industrial stylesheet and core script.");
            foreach (string forbidden in new[] { "bootstrap", "jquery", "kendo" })
            {
                Assert(!layout.ToLowerInvariant().Contains(forbidden),
                    "_Layout1 must not reference the legacy vendor asset '" + forbidden + "'.");
            }

            // 4. Unified request form wiring (model + GET form partial + POST targets).
            Assert(File.Exists(Path.Combine(webRoot, "Models", "NewRequestForm.cs")),
                "NewRequestForm model is required by the unified request screen.");
            string requestView = File.ReadAllText(Path.Combine(viewsRoot, "Request", "New.cshtml"));
            Assert(requestView.Contains("@model MyTime3.Models.NewRequestForm"),
                "Views/Request/New.cshtml must bind to the rewritten NewRequestForm model.");
            Assert(requestView.Contains("AddRequest"), "New request form must post to Request/AddRequest.");
            string requestController = File.ReadAllText(Path.Combine(webRoot, "Controllers", "RequestController.cs"));
            foreach (string action in new[] { "ActionResult AddRequest(NewRequestForm", "ActionResult AddRequestForUser(NewRequestForm", "ActionResult Form()" })
            {
                Assert(requestController.Contains(action), "RequestController must expose '" + action + "...'.");
            }

            // 5. Branding is registered as ARIA across the product identity layer.
            string branding = File.ReadAllText(Path.Combine(webRoot, "Content", "AriaBranding.cs"));
            Assert(branding.Contains("ARIA") && branding.Contains("آریا"),
                "AriaBranding must register the product under the ARIA (آریا) name.");
        }

        // =====================================================================
        // SCHEMA GUARD / ENGINE PIN TESTS (item 1 + item 2 of the delivery)
        // =====================================================================

        /// <summary>
        /// TEST-42 — The schema contract is only useful if it fails for the right
        /// reasons. This executes the real comparison used in production.
        /// </summary>
        private static void TestSchemaGuardEvaluation()
        {
            var contract = MyTime3.Security.SchemaGuard.Contract;
            Assert(contract.Count >= 16,
                "SchemaGuard must cover at least the 16 contracted tables, found " + contract.Count + ".");
            foreach (string required in new[]
            {
                "Request", "RequestAccept", "Workflow", "HourlyLeave", "Record",
                "Deputy", "PersonnelDeputy", "UserInfo", "UserInfoTitles", "TitlesChart",
                "Department", "Calendar", "LeaveTypes", "UserRules", "DepartmentRules",
                "Area", "Position", "PassCheckProps"
            })
            {
                Assert(contract.ContainsKey(required), "SchemaGuard contract is missing table '" + required + "'.");
            }

            // Objects without shipped evidence must never block start-up: they live in
            // AdvisoryContract, which is reported but cannot make the portal incompatible.
            foreach (string advisory in new[] { "DailyLeave", "OverTime" })
            {
                Assert(!contract.ContainsKey(advisory),
                    "'" + advisory + "' has no schema evidence and must not block the portal.");
                Assert(MyTime3.Security.SchemaGuard.AdvisoryContract.ContainsKey(advisory),
                    "'" + advisory + "' must still be reported as an advisory.");
            }

            // Real column names, not the legacy view-model names (BUG: [Description],
            // [NationalCode], [RequestDate] and [SubType] do not exist in T5).
            Assert(contract["Request"].Contains("WofkflowType"),
                "Request.WofkflowType is the real (misspelled) column; Aria must not rename it.");
            Assert(contract["Request"].Contains("Descriptions"),
                "Request.Descriptions is the real column name.");

            // 1. Full contract present -> compatible.
            var complete = new List<string>();
            foreach (var entry in contract)
            {
                complete.Add(entry.Key);
                foreach (string column in entry.Value)
                {
                    complete.Add(entry.Key + "." + column);
                }
            }
            // A healthy database also contains the advisory tables.
            foreach (var entry in MyTime3.Security.SchemaGuard.AdvisoryContract)
            {
                complete.Add(entry.Key);
                foreach (string column in entry.Value)
                {
                    complete.Add(entry.Key + "." + column);
                }
            }
            var ok = MyTime3.Security.SchemaGuard.Evaluate(complete);
            Assert(ok.WasChecked && ok.IsCompatible && ok.DatabaseReachable && ok.Problems.Count == 0,
                "A database satisfying the whole contract must be reported compatible.");

            // 2. One table missing -> incompatible, and the message names that table.
            var missingTable = new List<string>(complete);
            missingTable.RemoveAll(x => x == "Record" || x.StartsWith("Record.", StringComparison.OrdinalIgnoreCase));
            var noRecord = MyTime3.Security.SchemaGuard.Evaluate(missingTable);
            Assert(!noRecord.IsCompatible, "A missing 'Record' table must be flagged.");
            Assert(noRecord.Problems.Any(p => p.Contains("Record")),
                "The problem list must name the missing table 'Record'.");
            Assert(noRecord.Message.Contains("ناسازگار"), "The failure message must stay in Persian for operators.");

            // 3. Table present but a required column missing -> incompatible.
            var missingColumn = new List<string>(complete);
            missingColumn.Remove("Request.IsSuspend");
            var noSuspend = MyTime3.Security.SchemaGuard.Evaluate(missingColumn);
            Assert(!noSuspend.IsCompatible, "A missing 'Request.IsSuspend' column must be flagged.");
            Assert(noSuspend.Problems.Count == 1,
                "Exactly one problem expected for a single missing column, got " + noSuspend.Problems.Count + ".");
            Assert(noSuspend.Problems[0].Contains("IsSuspend"),
                "The problem list must name the missing column 'IsSuspend'.");

            // 4. Unreachable database / missing connection string -> reported, never thrown.
            var unreachable = MyTime3.Security.SchemaGuard.Validate(cs => null, null);
            Assert(unreachable.WasChecked && !unreachable.IsCompatible && !unreachable.DatabaseReachable,
                "A missing connection string must produce an 'unreachable' verdict.");

            var thrown = MyTime3.Security.SchemaGuard.Validate(
                cs => { throw new InvalidOperationException("network down"); }, "Server=nowhere;");
            Assert(!thrown.IsCompatible && !thrown.DatabaseReachable && thrown.Problems.Count > 0,
                "A connect failure must be converted into a verdict, not propagated.");

            // 5. Advisories are reported yet never change the verdict.
            Assert(ok.Advisories.Count == 0,
                "A database that satisfies the whole contract must not report advisories.");

            var withoutDailyLeave = new List<string>(complete.Where(x => x != "DailyLeave" && !x.StartsWith("DailyLeave.", StringComparison.OrdinalIgnoreCase)));
            var advisoryOnly = MyTime3.Security.SchemaGuard.Evaluate(withoutDailyLeave);
            Assert(advisoryOnly.IsCompatible,
                "A missing advisory table must not block the portal.");
            Assert(advisoryOnly.Advisories.Count == 1,
                "The missing advisory table must still be reported (got " + advisoryOnly.Advisories.Count + ").");

            // 6. Case sensitivity must not create false alarms (SQL Server is CI).
            var lower = complete.Select(x => x.ToLowerInvariant()).ToList();
            Assert(MyTime3.Security.SchemaGuard.Evaluate(lower).IsCompatible,
                "Schema comparison must be case-insensitive like SQL Server's default collation.");
        }

        /// <summary>
        /// TEST-43 — The guard is worthless if it is never invoked. These are the
        /// wiring points that only break at runtime, so they are asserted here.
        /// </summary>
        private static void TestSchemaGuardWiring()
        {
            string twfRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            string webRoot = Path.Combine(twfRoot, "TWF.Web");
            string viewsRoot = Path.Combine(webRoot, "Views");

            // 1. Startup probe: called once from the application entry point.
            string app = File.ReadAllText(Path.Combine(webRoot, "MyTime3", "MvcApplication.cs"));
            Assert(app.Contains("SchemaGuard.EnsureValidated()"),
                "MvcApplication must probe the schema contract at start-up.");
            Assert(app.Contains("using MyTime3.Security;"),
                "MvcApplication must import the schema guard namespace.");

            // 2. Global filter registered so an incompatible schema never renders a 500.
            string filters = File.ReadAllText(Path.Combine(webRoot, "MyTime3", "FilterConfig.cs"));
            Assert(filters.Contains("new SchemaGuardFilter()"),
                "FilterConfig must register SchemaGuardFilter globally.");
            Assert(filters.Contains("HandleErrorAttribute"),
                "The original HandleErrorAttribute registration must be preserved.");

            // 3. The filter must not be able to redirect to itself (loop) and must
            //    not swallow the diagnostics page it redirects to.
            string filter = File.ReadAllText(Path.Combine(webRoot, "Security", "SchemaGuardFilter.cs"));
            Assert(filter.Contains("DiagnosticsControllerName"),
                "SchemaGuardFilter must exempt the diagnostics controller to avoid a redirect loop.");

            // 4. The view the controller renders must exist, otherwise a schema
            //    problem turns into a missing-view exception - the exact failure
            //    this feature is meant to prevent.
            string viewPath = Path.Combine(viewsRoot, "Diagnostics", "Schema.cshtml");
            Assert(File.Exists(viewPath), "Views/Diagnostics/Schema.cshtml is missing.");
            string view = File.ReadAllText(viewPath);
            Assert(view.Contains("@model MyTime3.Security.SchemaCheckResult"),
                "The diagnostics view must bind to SchemaCheckResult.");
            Assert(view.Contains("Model.Problems"), "The diagnostics view must list the concrete problems.");
            Assert(!view.Contains("Exception.StackTrace") && !view.Contains("ex.StackTrace"),
                "The failure page must never render a stack trace to end users.");

            // 5. Project shape: the new files are compiled and views are published.
            string csproj = File.ReadAllText(Path.Combine(webRoot, "TWF.Web.csproj"));
            Assert(csproj.Contains("Security\\*.cs"),
                "TWF.Web.csproj must compile the whole Security folder (guard + filter).");
            Assert(csproj.Contains("Views\\**\\*.cshtml"),
                "TWF.Web.csproj must publish Razor views with the app.");
        }

        /// <summary>
        /// TEST-44 — A pin is only real when the build consumes it. Anything that
        /// still points at the legacy TWF 4.28 binaries is treated as a regression.
        /// </summary>
        private static void TestPinnedEngineEnforcement()
        {
            string twfRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            string repoRoot = Path.GetFullPath(Path.Combine(twfRoot, "..", ".."));

            string pinPath = Path.Combine(twfRoot, "engine", "engine-pin.json");
            Assert(File.Exists(pinPath), "engine/engine-pin.json is missing; the engine is not pinned.");
            string pin = File.ReadAllText(pinPath);
            Assert(pin.Contains("t5web-3.5.7"), "The pin must name the t5web-3.5.7 build.");
            Assert(pin.Contains("T5V3.DB.dll"), "The pin must record T5V3.DB.dll.");
            Assert(pin.Contains("T5V3.LeaveCalcs.dll"),
                "The pin must record T5V3.LeaveCalcs.dll (leave arithmetic must be verifiable).");

            // 1. Every engine reference resolves into the pinned folder.
            string pinnedFolder = Path.Combine(repoRoot, "T5Web [3.5.7]", "App", "bin");
            Assert(Directory.Exists(pinnedFolder), "Pinned engine folder 'T5Web [3.5.7]/App/bin' is missing.");

            foreach (string project in new[] { "TD5", "TWF.Application", "TWF.Infrastructure", "TWF.Web" })
            {
                string csprojPath = Path.Combine(twfRoot, project, project + ".csproj");
                if (!File.Exists(csprojPath))
                {
                    continue;
                }
                string csproj = File.ReadAllText(csprojPath);
                Assert(!csproj.Contains("TWF 4.28\\App\\bin"),
                    project + ".csproj still references the legacy TWF 4.28 engine binaries.");
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                    csproj, @"<HintPath>([^<]+)</HintPath>"))
                {
                    string hint = m.Groups[1].Value.Replace("/", "\\");
                    Assert(hint.Contains("T5Web [3.5.7]\\App\\bin", StringComparison.OrdinalIgnoreCase),
                        project + ".csproj has a HintPath outside the pinned engine: " + m.Groups[1].Value);
                    Assert(!hint.Contains("TWF 4.28", StringComparison.OrdinalIgnoreCase),
                        project + ".csproj still points at legacy binaries: " + m.Groups[1].Value);
                }
            }

            // 2. The build script compiles against the pin, not a hard-coded legacy path.
            string build = File.ReadAllText(Path.Combine(twfRoot, "build-and-test.sh"));
            Assert(build.Contains("engine-pin.json"),
                "build-and-test.sh must derive the engine folder from engine-pin.json.");
            Assert(!build.Contains("BIN_DIR=\"$REPO_ROOT/TWF 4.28/App/bin\""),
                "build-and-test.sh must not hard-code the legacy TWF 4.28 binary folder.");
        }

        /// <summary>
        /// TEST-45 — Self-diagnosis endpoints must stay reachable while the database
        /// is broken, and must not require a login (an operator with a broken
        /// database cannot authenticate).
        /// </summary>
        private static void TestDiagnosticsEndpoints()
        {
            string twfRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            string webRoot = Path.Combine(twfRoot, "TWF.Web");
            string controllerPath = Path.Combine(webRoot, "Controllers", "DiagnosticsController.cs");
            Assert(File.Exists(controllerPath), "Controllers/DiagnosticsController.cs is missing.");
            string controller = File.ReadAllText(controllerPath);

            Assert(controller.Contains("[AllowAnonymous]"),
                "DiagnosticsController must be anonymous: login itself depends on the database.");
            foreach (string action in new[] { "ActionResult Schema()", "ActionResult Recheck()", "JsonResult Health()" })
            {
                Assert(controller.Contains(action), "DiagnosticsController must expose '" + action + "'.");
            }
            Assert(controller.Contains("SchemaGuard.EnsureValidated(force: true)"),
                "Recheck must force a fresh probe instead of returning the cached verdict.");
            Assert(controller.Contains("JsonRequestBehavior.AllowGet"),
                "Health must be callable by a monitoring GET.");
        }

        /// <summary>
        /// TEST-46 — The failure this feature exists for: hand-written SQL that names an
        /// object the database does not have. It does not fail at build time, it fails at
        /// 21:00 on a shift. The manifest + tools/verify-sql-schema.py catch it statically;
        /// this test pins the four concrete regressions that were found and fixed.
        /// </summary>
        private static void TestSchemaManifestConformance()
        {
            string twfRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            string manifestPath = Path.Combine(twfRoot, "db", "schema-manifest.json");
            Assert(File.Exists(manifestPath),
                "db/schema-manifest.json is missing; run tools/build-schema-manifest.py.");
            string manifest = File.ReadAllText(manifestPath);

            foreach (string table in new[]
            {
                "\"UserInfo\"", "\"Department\"", "\"TitlesChart\"", "\"Request\"",
                "\"RequestAccept\"", "\"LeaveTypes\"", "\"Record\"", "\"Workflow\""
            })
            {
                Assert(manifest.Contains(table), "schema manifest is missing table " + table + ".");
            }
            Assert(manifest.Contains("\"WofkflowType\""),
                "The manifest must record the real (misspelled) Request column 'WofkflowType'.");
            Assert(manifest.Contains("\"NationalNo\""),
                "The manifest must record UserInfo.NationalNo.");

            string repo = File.ReadAllText(Path.Combine(twfRoot, "TWF.Infrastructure", "Data", "TwfDataRepository.cs"));
            foreach (string wrong in new[]
            {
                "[UserInfos]", "[Departments]", "u.[NationalCode]", "uit.[TitleID]",
                "tc.[DepartmentID]", "[Description]", "[RequestDate]", "[RequestDuration]",
                "[RequestState]", "[SubType]", "[AccepterDescription]", "[SenderBadgeNumber]",
                "[SenderTitleID]"
            })
            {
                Assert(!repo.Contains(wrong),
                    "TwfDataRepository still references the non-existent identifier '" + wrong + "'.");
            }

            // The corrected join path: person -> UserInfoTitles -> TitlesChart -> Department.
            Assert(repo.Contains("FROM [UserInfo] u") && repo.Contains("uit.[TitleChartID] = tc.[TitleID]"),
                "The profile query must join UserInfoTitles via TitleChartID.");
            Assert(repo.Contains("LEFT JOIN [Department] d ON u.[DeptID] = d.[DepartmentID]"),
                "The profile query must resolve the department through UserInfo.DeptID.");
            Assert(repo.Contains("FROM [RequestAccept]"),
                "The request list must derive workflow state from RequestAccept rows.");
        }

        /// <summary>
        /// TEST-47 — A guard that blocks on a guess is worse than no guard: it takes a
        /// healthy system offline. Every blocking entry must be provable.
        /// </summary>
        private static void TestGuardEvidenceRule()
        {
            string twfRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            string guard = File.ReadAllText(Path.Combine(twfRoot, "TWF.Web", "Security", "SchemaGuard.cs"));

            int requiredAt = guard.IndexOf("IReadOnlyDictionary<string, string[]> Contract", StringComparison.Ordinal);
            int advisoryAt = guard.IndexOf("AdvisoryContract", StringComparison.Ordinal);
            Assert(requiredAt > 0 && advisoryAt > requiredAt,
                "SchemaGuard must declare a required contract and a separate advisory contract.");

            string requiredBlock = guard.Substring(requiredAt, advisoryAt - requiredAt);
            foreach (string unproven in new[] { "\"DailyLeave\"", "\"OverTime\"" })
            {
                Assert(!requiredBlock.Contains(unproven),
                    unproven + " has no schema evidence and must stay in AdvisoryContract.");
            }

            string tool = File.ReadAllText(Path.Combine(twfRoot, "tools", "verify-sql-schema.py"));
            Assert(tool.Contains("def check_guard_contract"),
                "tools/verify-sql-schema.py must verify the guard contract against the manifest.");
            Assert(tool.Contains("AdvisoryContract"),
                "The verifier must explain the required/advisory split when it fails.");
        }
}
}
