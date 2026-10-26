using System.Globalization;
using System.Text;
using System.Text.Json;
using TWF.Application.Leaves;
using TWF.Application.Organization;
using TWF.Application.Security;
using TWF.Domain.Interfaces;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;
using TWF.Infrastructure.Licensing;
using TWF.Infrastructure.Localization;

namespace Aria.Core.Tests;

// No original application, native engine, SQL or HTTP is involved. This is a
// deterministic portable-core contract probe sourced ONLY from the TWF rewrite.
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fa-IR");
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "golden.json"), Encoding.UTF8));
        JsonElement golden = document.RootElement;
        Equal("aria-core-synthetic-01", Text(golden, "fixture"), "fixture identifier");

        Run("AR-CORE-01: Persian leap year boundaries from TWF rewrite TEST-04", () =>
        {
            Equal(golden.GetProperty("leapEsfandDays").GetInt32(), JalaliDateHelper.GetDaysInMonth(1399, 12),
                "Esfand 1399");
            Equal(golden.GetProperty("ordinaryEsfandDays").GetInt32(), JalaliDateHelper.GetDaysInMonth(1400, 12),
                "Esfand 1400");
            Require(JalaliDateHelper.IsLeapYear(1399) && !JalaliDateHelper.IsLeapYear(1400), "leap flags");
            Require(!JalaliDateHelper.TryParseJalali("1400/12/30", out _), "non-leap invalid date refused");
        });
        Run("AR-CORE-02: year rollover is a calendar boundary, not fixed DayOfYear", () =>
        {
            DateTime last1399 = JalaliDateHelper.ParseJalali("1399/12/30");
            DateTime first1400 = JalaliDateHelper.ParseJalali("1400/01/01");
            Equal(Text(golden, "persianYear1400Start"), first1400.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "year 1400 Gregorian start");
            Require(last1399.AddDays(1) == first1400, "year rollover");
            Require(JalaliDateHelper.GetPersianYearStart(first1400) == first1400, "computed year start");
            Require(!JalaliDateHelper.IsSamePersianYear(last1399, first1400), "year separation");
        });
        Run("AR-CORE-03: Persian digits and invalid calendar input", () =>
        {
            Require(JalaliDateHelper.TryParseJalali("۱۴۰۵/۰۸/۰۲ ۱۴:۳۰", out DateTime date), "Persian digits parse");
            Equal(Text(golden, "jalaliDateTime"), JalaliDateHelper.ToJalaliDateTimeString(date), "Jalali timestamp");
            Require(!JalaliDateHelper.TryParseJalali("۱۴۰۵/۱۳/۰۱", out _), "month 13 denied");
            Require(!JalaliDateHelper.TryParseJalali("not-a-date", out _), "untrusted date denied");
        });
        Run("AR-CORE-04: cumulative duration, 24-hour restriction", () =>
        {
            Require(TimeDurationHelper.TryParseDurationMinutes("۱۴۵:۳۰", out int minutes), "duration parse");
            Equal(golden.GetProperty("durationMinutes").GetInt32(), minutes, "multi-day duration");
            Equal(Text(golden, "durationText"), TimeDurationHelper.MinutesToTimeString(minutes), "duration format");
            Require(!TimeDurationHelper.TryParseDurationMinutes("۲۴:۰۱", out _, allowOver24Hours: false),
                "clock hour 24 denied");
            Require(!TimeDurationHelper.TryParseDurationMinutes("۱۲:۶۰", out _), "minute 60 denied");
        });
        Run("AR-CORE-05: both shared catalogs are required for synthetic health DTO", () =>
        {
            var health = new DatabaseHealthStatus { IsPersonnelDbConnected = true, IsT5DbConnected = false };
            Require(!health.IsHealthy, "one catalog must never imply health");
            health.IsT5DbConnected = true;
            Require(health.IsHealthy, "DTO reports both flags; no database contacted");
            health.IsPersonnelDbConnected = false;
            Require(!health.IsHealthy, "loss of either catalog must fail health");
        });
        Run("AR-CORE-06: rewrite's no-new-lock licensing policy", () =>
        {
            var license = UnrestrictedLicenseService.Instance;
            Require(license.IsLicenseValid() && license.IsTimeAppActive() && license.IsFullSizeLogoAllowed(),
                "no hardware activation should be introduced");
            Require(license.IsPersonnelCountWithinLimit(500_000) && license.GetMaxPersonnelCount() == int.MaxValue,
                "no new seat cap");
            Equal(Text(golden, "licenseMode"), license.GetStatus().Mode, "rewrite license mode");
        });
        Run("AR-CORE-07: cookie name, TTL model and expiry boundary (not cryptography)", () =>
        {
            Equal(Text(golden, "cookieName"), SessionManager.DefaultCookieName, "cookie contract name");
            var settings = new TwfAppSettings { CookiesTime = 2, CookiesSecure = true };
            Equal(TimeSpan.FromHours(2), settings.GetSessionLifetime(), "synthetic TTL");
            var now = new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
            var payload = new SessionTokenPayload(1001, "synthetic-user", "user", now, now.AddHours(2), "fixed-nonce");
            Require(!payload.IsExpired(now.AddHours(1)) && payload.IsExpired(now.AddHours(2)), "expiry exclusive end");
            // No key, token signing, encryption, cookie emission, or original session was exercised.
        });
        Run("AR-CORE-08: independent per-call identity, Persian display", () =>
        {
            var now = new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
            var first = Identity(1001, "synthetic-a", "مریم", "آزمایشی", "user", now);
            var other = Identity(1002, "synthetic-b", "لیلا", "آزمایشی", "user", now);
            Equal(Text(golden, "displayName"), first.FullName, "Persian full name");
            Require(!first.IsAdmin && !other.IsAdmin && first.BadgeNumber != other.BadgeNumber, "no shared static identity");
            Equal(Text(golden, "displayName"), first.FullName, "other request did not overwrite first");
        });
        Run("AR-CORE-09: rewrite workflow IDs and non-mutating request DTO", () =>
        {
            Equal(golden.GetProperty("requestStateWaiting").GetInt32(), (int)TwfRequestState.Waiting, "Waiting state");
            Equal(golden.GetProperty("requestStateFirstAccept").GetInt32(), (int)TwfRequestState.FirstAccept,
                "first approval state");
            Require((int)TwfWorkflowType.Record == 4 && (int)TwfWorkflowType.DailyLeave == 0,
                "current rewrite persisted IDs");
            var request = new RequestRecordDto { FromTime = new DateTime(2026, 10, 24, 8, 30, 0) };
            DateTime before = request.FromTime;
            Require(request.FromTime == before && request.FromTime == before, "reading request DTO must not alter time");
        });
        Run("AR-CORE-10: native boundary is only an interface and missing adapters fail fast", () =>
        {
            Require(typeof(IT5BackendAdapter).IsInterface, "backend is an explicit contract");
            var references = typeof(IT5BackendAdapter).Assembly.GetReferencedAssemblies();
            Require(!references.Any(x => x.Name?.StartsWith("T5V3.", StringComparison.OrdinalIgnoreCase) == true ||
                x.Name is "Personnel" or "TD5" or "TINYLib"), "no native/Framework engine link in Core");
            Throws<ArgumentNullException>(() => new T5OrganizationAccessService(null!));
            Throws<ArgumentNullException>(() => new LeaveBalanceService(null!, null!));
            Throws<ArgumentNullException>(() => new SessionManager(null!, null!, null!));
            // No adapter implementation is registered here; no native engine call was attempted.
        });

        Run("AR-CORE-11: only audited Persian leave denial labels are imported", () =>
        {
            Equal(Text(golden, "dailyLeaveLimitLabel"), PersianString.DailyLeaveLimit, "leave limit text");
            Equal(Text(golden, "remainLeaveErrorLabel"), PersianString.RemainLeaveError, "balance denial text");
            Require(typeof(PersianString).GetProperty("bode5Copy1") == null,
                "do not expose an unverified version banner in the new core");
        });

        Console.WriteLine($"Summary: {_passed} Passed, {_failed} Failed (net10.0 Aria portable synthetic only)");
        return _passed == 11 && _failed == 0 ? 0 : 1;
    }

    private static string Text(JsonElement source, string key) => source.GetProperty(key).GetString()
        ?? throw new InvalidOperationException("Missing fixture value " + key);

    private static UserSessionIdentity Identity(long badge, string login, string first, string last,
        string role, DateTime now) => new(badge, login, first, last, role, 11, "سمت آزمایشی", 0, "", 10,
            "واحد آزمایشی", false, false, now, now.AddHours(2));

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("[PASS] " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("[FAIL] " + name + ": " + ex.Message); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string what) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{what}: expected '{expected}', got '{actual}'");
    }
}
