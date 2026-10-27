using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aria.Web.Content;
using Aria.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TWF.Domain.Models;
using WebHost = Aria.Web.Program;

namespace Aria.Web.Tests;

// Standalone Kestrel/Razor probe. The synthetic verifier and clock are compiled
// ONLY here, never into the published Web binary or any appsettings/HTTP flag.
internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static readonly DateTime FixedNow = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

    private static async Task<int> Main(string[] args)
    {
        if ((args.Length != 1 && (args.Length != 3 || args[1] != "--about-original-fixture")) ||
            !File.Exists(Path.Combine(args[0], "Aria.Web.csproj")) ||
            (args.Length == 3 && !File.Exists(args[2])))
            throw new ArgumentException("Pass Aria/Web and optionally a same-run original About fixture.");
        string webRoot = Path.GetFullPath(args[0]);
        string? originalAboutFixture = args.Length == 3 ? args[2] : null;

        await using (var production = WebHost.CreateApp(Array.Empty<string>(), contentRoot: webRoot))
        {
            using var client = await Start(production);
            await Check("AR-WEB-01: liveness is not DB/session readiness", async () =>
            {
                using var response = await Send(client, "/health/live");
                Equal(HttpStatusCode.OK, response.StatusCode, "live process");
                string body = await response.Content.ReadAsStringAsync();
                Require(body.Contains("alive", StringComparison.Ordinal) &&
                    !body.Contains("databaseReachable", StringComparison.OrdinalIgnoreCase), "no readiness claim");
            });
            await Check("AR-WEB-02: spoofed identity header and cookie cannot bypass production", async () =>
            {
                using var response = await Send(client, "/Home/Index", "synthetic-valid", "mytimeauth=not-a-valid-token");
                Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode, "no legacy verifier/key");
                string body = await response.Content.ReadAsStringAsync();
                Require(body.Contains("تأیید ایمن نشست", StringComparison.Ordinal) &&
                    !body.Contains("not-a-valid-token", StringComparison.Ordinal) &&
                    !body.Contains("System.Exception", StringComparison.Ordinal), "safe fail-closed response");
                Require(!response.Headers.TryGetValues("Set-Cookie", out _), "no invented session cookie");
            });
            await Check("AR-WEB-03: compiled RTL Razor login notice never collects a password", async () =>
            {
                using var response = await Send(client, "/Login/Login");
                Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode, "login disabled");
                string html = await response.Content.ReadAsStringAsync();
                Require(html.Contains("lang=\"fa\"", StringComparison.Ordinal) &&
                    html.Contains("dir=\"rtl\"", StringComparison.Ordinal) &&
                    html.Contains("ورود موقتاً در دسترس نیست", StringComparison.Ordinal), "Persian RTL Razor");
                Require(html.Contains("/assets/aria.css", StringComparison.Ordinal) &&
                    html.Contains("href=\"/Home/About\"", StringComparison.Ordinal) &&
                    !html.Contains("<form", StringComparison.OrdinalIgnoreCase) &&
                    !html.Contains("type=\"password\"", StringComparison.OrdinalIgnoreCase) &&
                    !html.Contains("https://", StringComparison.OrdinalIgnoreCase) &&
                    !html.Contains("v5.0", StringComparison.OrdinalIgnoreCase), "no credential form/CDN/version claim");
                Require(response.Headers.TryGetValues("Content-Security-Policy", out var csp) &&
                    csp.Any(x => x.Contains("default-src 'self'", StringComparison.Ordinal)), "CSP self only");
                Require(!response.Headers.TryGetValues("Set-Cookie", out _), "no cookie issued");
            });
            await Check("AR-WEB-04: original-product rewrite stylesheet is local and offline", async () =>
            {
                using var response = await Send(client, "/assets/aria.css");
                Equal(HttpStatusCode.OK, response.StatusCode, "local CSS");
                Require(response.Content.Headers.ContentType?.MediaType == "text/css", "CSS content type");
                string css = await response.Content.ReadAsStringAsync();
                Require(css.Contains("--twf-bg-canvas", StringComparison.Ordinal) &&
                    !css.Contains("url(http", StringComparison.OrdinalIgnoreCase), "no remote CSS fetch");
            });
            await Check("AR-WEB-05: public schema/health never pretend a DB probe succeeded", async () =>
            {
                using var schema = await Send(client, "/Diagnostics/Schema");
                Equal(HttpStatusCode.ServiceUnavailable, schema.StatusCode, "schema not measured");
                string html = await schema.Content.ReadAsStringAsync();
                Require(html.Contains("بررسی پایگاه‌داده در دسترس نیست", StringComparison.Ordinal) &&
                    !html.Contains("Initial Catalog", StringComparison.Ordinal), "no DB config leak");
                using var health = await Send(client, "/Diagnostics/Health");
                Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode, "no false green health");
                string json = await health.Content.ReadAsStringAsync();
                Require(json.Contains("unavailable", StringComparison.Ordinal) &&
                    !json.Contains("true", StringComparison.OrdinalIgnoreCase), "unknown DB state not success");
            });
            await Check("AR-WEB-06: no login POST/credential collector or session emission", async () =>
            {
                using var response = await client.PostAsync("/Login/Login", content: null);
                Require(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                    "unimplemented login POST must not dispatch");
                Require(!response.Headers.TryGetValues("Set-Cookie", out _), "no login session");
            });
            await Check("AR-WEB-20: original Aria About labels have a real offline public GET", async () =>
            {
                using var response = await Send(client, "/Home/About");
                Equal(HttpStatusCode.OK, response.StatusCode, "read-only public About action");
                Require(response.Content.Headers.ContentType?.MediaType == "text/html", "compiled About Razor");
                string html = await response.Content.ReadAsStringAsync();
                string renderedText = WebUtility.HtmlDecode(html);
                foreach (string label in OriginalAboutContent.Labels.Values)
                    Require(renderedText.Contains(label.Trim(), StringComparison.Ordinal),
                        "owned About label missing from rendered page");
                Require(html.Contains("lang=\"fa\"", StringComparison.Ordinal) &&
                    html.Contains("dir=\"rtl\"", StringComparison.Ordinal) &&
                    html.Contains("/assets/aria.css", StringComparison.Ordinal) &&
                    html.Contains("/Home/MyBanner?index=2", StringComparison.Ordinal) &&
                    html.Contains("/Home/MyBanner?index=3", StringComparison.Ordinal) &&
                    !html.Contains("<form", StringComparison.OrdinalIgnoreCase) &&
                    !html.Contains("LogoPath", StringComparison.OrdinalIgnoreCase) &&
                    !html.Contains("V4.1", StringComparison.OrdinalIgnoreCase) &&
                    !html.Contains("https://", StringComparison.OrdinalIgnoreCase),
                    "About is offline, encoded Razor text; no version banner or login/DB shortcut");
                Require(!response.Headers.TryGetValues("Set-Cookie", out _) &&
                    response.Headers.TryGetValues("Cache-Control", out var cache) &&
                    cache.Contains("no-store"), "no session cookie or cached dynamic response");
                if (originalAboutFixture != null)
                    AssertOriginalAboutFixture(originalAboutFixture);
                using var post = await client.PostAsync("/Home/About", null);
                Require(post.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                    "the new About action must not dispatch a POST");
                Require(!post.Headers.TryGetValues("Set-Cookie", out _), "no cookie from About POST");
            });
            await Check("AR-WEB-21: public About serves only pinned offline original-vendor images", async () =>
            {
                foreach (var (asset, sha) in new[]
                {
                    ("aria-about-arm.png", "ca5e231a2b88d60a6140cf66b47d436cad10e58ad0547bd8cfdc9beccf580d20"),
                    ("aria-about-logo.png", "bb2d2c1ebc50f02c1aae57a4e50a3f4b8d1b00641b5d1ea4660a95ffce72572e")
                })
                {
                    using var response = await Send(client, "/assets/" + asset);
                    Equal(HttpStatusCode.OK, response.StatusCode, "pinned public About image " + asset);
                    Require(response.Content.Headers.ContentType?.MediaType == "image/png" &&
                        !response.Headers.TryGetValues("Set-Cookie", out _), "offline image without identity");
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    Require(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == sha &&
                        bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47,
                            0x0d, 0x0a, 0x1a, 0x0a }),
                        "response bytes must be exactly the audited original-vendor offline asset");
                }
            });
            await Check("AR-WEB-22: original About image URLs serve only cases 2/3", async () =>
            {
                foreach (var (index, sha) in new[]
                {
                    (2, "ca5e231a2b88d60a6140cf66b47d436cad10e58ad0547bd8cfdc9beccf580d20"),
                    (3, "bb2d2c1ebc50f02c1aae57a4e50a3f4b8d1b00641b5d1ea4660a95ffce72572e")
                })
                {
                    using var response = await Send(client, "/Home/MyBanner?index=" + index);
                    Equal(HttpStatusCode.OK, response.StatusCode, "original public image URL " + index);
                    byte[] content = await response.Content.ReadAsByteArrayAsync();
                    Require(response.Content.Headers.ContentType?.MediaType == "image/png" &&
                        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant() == sha &&
                        !response.Headers.TryGetValues("Set-Cookie", out _) &&
                        response.Headers.TryGetValues("Cache-Control", out var cache) && cache.Contains("no-store"),
                        "only owned offline image bytes, no customer logo, identity or session");
                }
                foreach (string index in new[] { "0", "1", "4", "-1", "invalid" })
                {
                    using var response = await Send(client, "/Home/MyBanner?index=" + index);
                    Equal(HttpStatusCode.NotFound, response.StatusCode,
                        "unverified/customer LogoPath index must not be served");
                }
                using var post = await client.PostAsync("/Home/MyBanner?index=2", null);
                Require(post.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                    "new public image route never accepts POST");
            });
        }

        var testPersonnel = new TestPersonnelDirectory();
        await using (var synthetic = WebHost.CreateApp(Array.Empty<string>(), services =>
        {
            services.AddSingleton<IAriaSessionVerifier, TestSessionVerifier>();
            services.AddSingleton<IAriaPersonnelDirectory>(testPersonnel);
            services.AddControllersWithViews()
                .AddApplicationPart(typeof(SyntheticIdentityProbeController).Assembly);
            services.AddSingleton<TimeProvider>(new FixedClock(FixedNow));
        }, webRoot))
        {
            using var client = await Start(synthetic);
            await Check("AR-WEB-07: anonymous root redirects only to local login notice", async () =>
            {
                using var home = await Send(client, "/");
                Equal(HttpStatusCode.Redirect, home.StatusCode, "anonymous root");
                var location = home.Headers.Location ?? throw new InvalidOperationException("missing redirect");
                Require(!location.IsAbsoluteUri && location.OriginalString == "/Login/Login", "fixed local target");
                using var notice = await Send(client, location.OriginalString);
                Equal(HttpStatusCode.ServiceUnavailable, notice.StatusCode, "no dummy login success");
            });
            await Check("AR-WEB-08: verified synthetic session cannot fabricate a dashboard", async () =>
            {
                using var response = await Send(client, "/Home/Index?sort=-1&g=true", "synthetic-valid");
                Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode, "no engine/SQL-backed home data");
                string body = await response.Content.ReadAsStringAsync();
                Require(body.Contains("داشبورد آریا", StringComparison.Ordinal) &&
                    !body.Contains("System.Exception", StringComparison.Ordinal), "safe notice only");
            });
            await Check("AR-WEB-09: denied, expired or invalid sessions never enter Home", async () =>
            {
                foreach (string identity in new[] { "synthetic-disabled", "synthetic-expired", "synthetic-unknown" })
                {
                    using var response = await Send(client, "/Home/Index", identity);
                    Equal(HttpStatusCode.Forbidden, response.StatusCode, identity);
                }
            });
            await Check("AR-WEB-10: verifier exception fails closed without disclosure", async () =>
            {
                using var response = await Send(client, "/Home/Index", "synthetic-error");
                Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode, "backend verifier error");
                Require(!(await response.Content.ReadAsStringAsync()).Contains("CI_EXCEPTION_MARKER", StringComparison.Ordinal),
                    "no stack/secret leak");
            });
            await Check("AR-WEB-11: no unported POST, recheck or catch-all action", async () =>
            {
                using var post = new HttpRequestMessage(HttpMethod.Post, "/Home/Index");
                post.Headers.TryAddWithoutValidation("X-Synthetic-Session", "synthetic-valid");
                using var posted = await client.SendAsync(post);
                Require(posted.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                    "no state-changing Home POST");
                using var recheck = await Send(client, "/Diagnostics/Recheck", "synthetic-valid");
                Equal(HttpStatusCode.NotFound, recheck.StatusCode, "no public DB recheck side effect");
                using var missing = await Send(client, "/invented-controller/Index", "synthetic-valid");
                Equal(HttpStatusCode.NotFound, missing.StatusCode, "no fake catch-all success");
            });
            await Check("AR-WEB-12: 40 overlapping requests keep decision request-scoped", async () =>
            {
                var tasks = Enumerable.Range(0, 40).Select(async i =>
                {
                    bool valid = i % 2 == 0;
                    using var response = await Send(client, "/Home/Index",
                        valid ? "synthetic-valid" : "synthetic-disabled");
                    Equal(valid ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden,
                        response.StatusCode, "overlap " + i);
                });
                await Task.WhenAll(tasks);
            });
            await Check("AR-WEB-13: 100 distinct synthetic identities burst with zero-badge denial", async () =>
            {
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                string[] modes = ["valid", "zero", "disabled", "expired", "error"];
                Task[] requests = Enumerable.Range(0, 100).Select(async i =>
                {
                    await start.Task;
                    string mode = modes[i % modes.Length];
                    string identity = $"synthetic-load-{mode}-{i:D3}";
                    using var response = await Send(client, "/Home/Index", identity);
                    Equal(mode is "valid" or "error" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden,
                        response.StatusCode, "burst " + i + " " + mode);
                    Require(!response.Headers.TryGetValues("Set-Cookie", out _), "burst must not issue cookies");
                    Require(response.Headers.TryGetValues("Cache-Control", out var cache) && cache.Contains("no-store"),
                        "burst response must not be cached");
                    string body = await response.Content.ReadAsStringAsync();
                    Require(!body.Contains("synthetic-load-", StringComparison.Ordinal) &&
                        !body.Contains("CI_EXCEPTION_MARKER", StringComparison.Ordinal),
                        "burst response must not disclose an identity/error");
                }).ToArray();
                start.SetResult(true); // release all 100 test-only requests at once
                await Task.WhenAll(requests);
                // This is a fail-closed Kestrel security burst, NOT 100 active users,
                // original sessions, a report workload, IIS/SQL load or a latency SLA.
            });
            await Check("AR-WEB-14: verified account must match session name and badge", async () =>
            {
                foreach (string name in new[] { "synthetic-other-badge", "synthetic-other-name",
                    "synthetic-empty-account", "synthetic-missing-account", "synthetic-future-issued" })
                {
                    using var response = await Send(client, "/Home/Index", name);
                    Equal(HttpStatusCode.Forbidden, response.StatusCode, name);
                    Require(!response.Headers.TryGetValues("Set-Cookie", out _), "no cookie from inconsistent identity");
                }
            });
            await Check("AR-WEB-15: personnel recheck can revoke or fail closed", async () =>
            {
                foreach (var (name, expected) in new[]
                {
                    ("synthetic-revoked", HttpStatusCode.Forbidden),
                    ("synthetic-profile-error", HttpStatusCode.ServiceUnavailable),
                    ("synthetic-profile-mismatch", HttpStatusCode.Forbidden),
                    ("synthetic-admin-role", HttpStatusCode.Forbidden)
                })
                {
                    using var response = await Send(client, "/Home/Index", name);
                    Equal(expected, response.StatusCode, name);
                    Require(!response.Headers.TryGetValues("Set-Cookie", out _), "no cookie for invalid profile");
                    string body = await response.Content.ReadAsStringAsync();
                    Require(!body.Contains(name, StringComparison.Ordinal) &&
                        !body.Contains("PRIVATE_CONNECTION_MARKER", StringComparison.Ordinal),
                        "no identity or driver leak");
                }
            });
            await Check("AR-WEB-16: session role/title claims cannot become permissions", async () =>
            {
                var forged = new UserSessionIdentity(1001, "synthetic-user", "Forged", "Manager",
                    "user", 999, "خارج از سامانه", 999, "خارج از سامانه", 99, "خارج از سامانه",
                    true, true, FixedNow.AddMinutes(-5), FixedNow.AddMinutes(10));
                var rechecked = new VerifiedAriaAccount("synthetic-user", 1001);
                var safe = AriaRequestIdentity.LeastPrivilege(rechecked, forged);
                Require(safe.BadgeNumber == 1001 && safe.UserName == rechecked.AccountName &&
                    safe.Role == "user" && !safe.IsAdmin && !safe.IsObserver &&
                    !safe.IsTopLevelManager && safe.TitleId == 0 && safe.UpperTitleId == 0 &&
                    safe.DepartmentId == 0 && safe.FirstName == "" && safe.LastName == "" &&
                    safe.IssuedAtUtc == forged.IssuedAtUtc && safe.ExpiresAtUtc == forged.ExpiresAtUtc,
                    "only rechecked name/badge and verified timestamps survive");
                try
                {
                    _ = AriaRequestIdentity.LeastPrivilege(
                        new VerifiedAriaAccount("synthetic-other", 1001), forged);
                    throw new InvalidOperationException("mismatched person was accepted");
                }
                catch (ArgumentException) { /* expected */ }

                // This route exists ONLY in Web.Tests. Verify that the actual
                // Kestrel middleware installs the stripped identity per request.
                using var response = await Send(client, "/test-only/session", "synthetic-forged-claims");
                Equal(HttpStatusCode.OK, response.StatusCode, "test-only identity inspection route");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var delivered = json.RootElement;
                Require(delivered.GetProperty("badgeNumber").GetInt64() == 1001 &&
                    delivered.GetProperty("role").GetString() == "user" &&
                    delivered.GetProperty("titleId").GetInt32() == 0 &&
                    delivered.GetProperty("departmentId").GetInt32() == 0 &&
                    !delivered.GetProperty("observer").GetBoolean() &&
                    !delivered.GetProperty("topLevelManager").GetBoolean() &&
                    delivered.GetProperty("firstName").GetString() == "", "forged claims stripped on real HTTP");
                Require(!response.Headers.TryGetValues("Set-Cookie", out _),
                    "test-only HTTP must not create an auth cookie");
            });
            await Check("AR-WEB-17: original credential and password-change POST names stay unavailable", async () =>
            {
                // TWF 4.28's public Login/TestUserPass and Login/Change POSTs
                // are original-binary metadata, NOT proof of safe net10 auth.
                // Even an otherwise verified test-only session must not dispatch
                // a password collector/change route before the same-account
                // verifier, antiforgery, permissions and revocation are proven.
                foreach (string path in new[] { "/Login/TestUserPass", "/Login/Change",
                    "/Login/ChangePassword", "/Login/ChangePasswordForce" })
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, path);
                    request.Headers.TryAddWithoutValidation("X-Synthetic-Session", "synthetic-valid");
                    request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["userName"] = "fixture-only",
                        ["password"] = "FIXED_TEST_PASSWORD_NEVER_REAL",
                        ["newPassword"] = "FIXED_TEST_NEW_PASSWORD_NEVER_REAL"
                    });
                    using var response = await client.SendAsync(request);
                    Require(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                        path + " must not dispatch a credential operation");
                    Require(!response.Headers.TryGetValues("Set-Cookie", out _) &&
                        response.Headers.TryGetValues("Cache-Control", out var cache) &&
                        cache.Contains("no-store"), "no session or cacheable credential response");
                    string body = await response.Content.ReadAsStringAsync();
                    Require(!body.Contains("FIXED_TEST_PASSWORD_NEVER_REAL", StringComparison.Ordinal) &&
                        !body.Contains("FIXED_TEST_NEW_PASSWORD_NEVER_REAL", StringComparison.Ordinal),
                        "no fixture credential echo");
                }
            });
            await Check("AR-WEB-18: no fake logout or legacy-cookie continuation", async () =>
            {
                // Logout must eventually revoke a fresh session, not just return
                // success or expire an unauthenticated legacy DES cookie. Until
                // approved, NO logout endpoint is published in the new host.
                foreach (HttpMethod method in new[] { HttpMethod.Get, HttpMethod.Post })
                {
                    using var request = new HttpRequestMessage(method, "/Login/Logout");
                    request.Headers.TryAddWithoutValidation("X-Synthetic-Session", "synthetic-valid");
                    request.Headers.TryAddWithoutValidation("Cookie", "mytimeauth=FIXED_INVALID_LEGACY_FIXTURE");
                    using var response = await client.SendAsync(request);
                    Require(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                        method + " must not pretend to sign out");
                    Require(!response.Headers.TryGetValues("Set-Cookie", out _) &&
                        response.Headers.TryGetValues("Cache-Control", out var cache) &&
                        cache.Contains("no-store"), "no fake session expiry or cacheable response");
                    string body = await response.Content.ReadAsStringAsync();
                    Require(!body.Contains("FIXED_INVALID_LEGACY_FIXTURE", StringComparison.Ordinal),
                        "no old token echoed");
                }
            });
            await Check("AR-WEB-19: UTC-only short synthetic session cannot outlive policy cap", async () =>
            {
                // Only in this test executable: the production verifier is
                // Unavailable and does NOT issue or parse any fresh/legacy cookie.
                int before = testPersonnel.Calls;
                foreach (string mode in new[] { "old-issued", "long-expiry",
                    "unspecified-issue", "local-expiry", "min-issued", "max-expiry",
                    "over-cap-tick" })
                {
                    string user = "synthetic-time-" + mode;
                    using var response = await Send(client, "/Home/Index", user);
                    Equal(HttpStatusCode.Forbidden, response.StatusCode, mode);
                    Require(!response.Headers.TryGetValues("Set-Cookie", out _) &&
                        response.Headers.TryGetValues("Cache-Control", out var cache) &&
                        cache.Contains("no-store"), "invalid session never issues a cookie or cacheable response");
                }
                Require(testPersonnel.Calls == before,
                    "invalid timestamps must be rejected BEFORE Personnel is consulted");
                foreach (string mode in new[] { "within-cap", "at-cap" })
                {
                    using var response = await Send(client, "/Home/Index", "synthetic-time-" + mode);
                    Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode,
                        "test-only in-window identity reaches no-data dashboard, not a fake success");
                    Require(!response.Headers.TryGetValues("Set-Cookie", out _),
                        "no fresh session even at the tested boundary");
                }
                Require(testPersonnel.Calls == before + 2,
                    "in-window test-only sessions still require a fresh Personnel check");
            });
        }

        Console.WriteLine($"Summary: {_passed} Passed, {_failed} Failed (net10.0 Aria Kestrel/Razor; original About labels only when same-run fixture supplied)");
        return _passed == 22 && _failed == 0 ? 0 : 1;
    }

    private static void AssertOriginalAboutFixture(string path)
    {
        string[] lines = File.ReadAllLines(path);
        Require(lines.Length == OriginalAboutContent.Labels.Count + 1 &&
            lines[0] == "source_sha256=edba04ccafb9f111870f9050f86e9059e92389188bbd3115678afb21691ddd6b",
            "About fixture must come from the pinned original binary in this Windows run");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in lines.Skip(1))
        {
            int equals = line.IndexOf('=');
            Require(equals > 0, "invalid original About fixture entry");
            string key = line[..equals];
            Require(seen.Add(key), "duplicate original About label");
            Require(OriginalAboutContent.Labels.TryGetValue(key, out string? expected),
                "unknown original About label");
            string actual = Encoding.UTF8.GetString(Convert.FromBase64String(line[(equals + 1)..]));
            Require(string.Equals(expected, actual, StringComparison.Ordinal),
                "original About resource differs from owned net10 label: " + key);
        }
        Console.WriteLine("[ORIGINAL-COMPARED] 7 public About labels from same-run TWF 4.28 DLL (NOT original HTML/IIS parity)");
    }

    private static async Task<HttpClient> Start(WebApplication app)
    {
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?
            .Addresses.Single(x => x.StartsWith("http://127.0.0.1:", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Kestrel did not bind a synthetic loopback port");
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(15)
        };
    }
    private static Task<HttpResponseMessage> Send(HttpClient client, string path, string? user = null,
        string? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (user != null) request.Headers.TryAddWithoutValidation("X-Synthetic-Session", user);
        if (cookie != null) request.Headers.TryAddWithoutValidation("Cookie", cookie);
        return client.SendAsync(request);
    }
    private static async Task Check(string name, Func<Task> test)
    {
        try { await test(); _passed++; Console.WriteLine("[PASS] " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("[FAIL] " + name + ": " + ex.Message); }
    }
    private static void Equal<T>(T expected, T actual, string what) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{what}: expected {expected}, got {actual}");
    }
    private static void Require(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException(what);
    }
}

internal sealed class FixedClock(DateTime utc) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utc);
}

internal sealed class TestSessionVerifier : IAriaSessionVerifier
{
    private static readonly DateTime FixedNow = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

    public Task<SessionResolution> VerifyAsync(HttpContext context, CancellationToken cancellationToken)
    {
        string user = context.Request.Headers.TryGetValue("X-Synthetic-Session", out var identity)
            ? identity.ToString() : string.Empty;
        if (user == "synthetic-error") throw new InvalidOperationException("CI_EXCEPTION_MARKER");
        if (user.Length == 0) return Task.FromResult(SessionResolution.Anonymous());
        if (user is "synthetic-disabled" or "synthetic-unknown")
            return Task.FromResult(SessionResolution.Denied());
        if (user == "synthetic-forged-claims")
        {
            var session = new UserSessionIdentity(1001, user, "Forged", "Manager", "user",
                999, "Spoofed title", 999, "Spoofed upper title", 99, "Spoofed department",
                true, true, FixedNow.AddMinutes(-5), FixedNow.AddMinutes(10));
            return Task.FromResult(SessionResolution.Verified(
                new VerifiedAriaAccount(user, 1001), session));
        }
        if (user is "synthetic-revoked" or "synthetic-profile-error" or
            "synthetic-profile-mismatch" or "synthetic-admin-role")
        {
            var session = new UserSessionIdentity(1001, user, "کاربر", "آزمایشی",
                user == "synthetic-admin-role" ? "admin" : "user",
                11, "سمت آزمایشی", 0, "", 10, "واحد آزمایشی", false, false,
                FixedNow.AddMinutes(-5), FixedNow.AddMinutes(10));
            return Task.FromResult(SessionResolution.Verified(
                new VerifiedAriaAccount(user, 1001), session));
        }
        if (user is "synthetic-other-badge" or "synthetic-other-name" or
            "synthetic-empty-account" or "synthetic-missing-account" or "synthetic-future-issued")
        {
            var session = new UserSessionIdentity(1001, "synthetic-user", "مریم", "آزمایشی", "user",
                11, "سمت آزمایشی", 0, "", 10, "واحد آزمایشی", false, false,
                user == "synthetic-future-issued" ? FixedNow.AddMinutes(5) : FixedNow.AddMinutes(-5),
                FixedNow.AddMinutes(10));
            var account = user switch
            {
                "synthetic-other-badge" => new VerifiedAriaAccount("synthetic-user", 1002),
                "synthetic-other-name" => new VerifiedAriaAccount("synthetic-other", 1001),
                "synthetic-empty-account" => new VerifiedAriaAccount("", 1001),
                "synthetic-missing-account" => null,
                _ => new VerifiedAriaAccount("synthetic-user", 1001)
            };
            return Task.FromResult(new SessionResolution(SessionDecision.Verified, account, session));
        }
        if (user.StartsWith("synthetic-time-", StringComparison.Ordinal))
        {
            // Fixed fake verifier, never present in the publishable Web.
            // These values are deliberately adversarial hydrated sessions,
            // NOT cookies issued by a working same-password login.
            if (user is not ("synthetic-time-old-issued" or "synthetic-time-long-expiry" or
                "synthetic-time-unspecified-issue" or "synthetic-time-local-expiry" or
                "synthetic-time-min-issued" or "synthetic-time-max-expiry" or
                "synthetic-time-over-cap-tick" or "synthetic-time-within-cap" or
                "synthetic-time-at-cap"))
                return Task.FromResult(SessionResolution.Denied());
            DateTime issued = user switch
            {
                "synthetic-time-old-issued" => FixedNow.AddMinutes(-25),
                "synthetic-time-unspecified-issue" =>
                    DateTime.SpecifyKind(FixedNow.AddMinutes(-5), DateTimeKind.Unspecified),
                "synthetic-time-min-issued" => DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc),
                "synthetic-time-over-cap-tick" => FixedNow.AddMinutes(-10).AddTicks(-1),
                "synthetic-time-at-cap" => FixedNow.AddMinutes(-10),
                "synthetic-time-within-cap" => FixedNow.AddMinutes(-9),
                _ => FixedNow.AddMinutes(-5)
            };
            DateTime expires = user switch
            {
                "synthetic-time-old-issued" => FixedNow.AddMinutes(1),
                "synthetic-time-long-expiry" => FixedNow.AddMinutes(30),
                "synthetic-time-local-expiry" =>
                    DateTime.SpecifyKind(FixedNow.AddMinutes(10), DateTimeKind.Local),
                "synthetic-time-max-expiry" => DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
                _ => FixedNow.AddMinutes(10)
            };
            var session = new UserSessionIdentity(1001, "synthetic-user", "", "", "user",
                0, "", 0, "", 0, "", false, false, issued, expires);
            return Task.FromResult(SessionResolution.Verified(
                new VerifiedAriaAccount("synthetic-user", 1001), session));
        }
        if (user.StartsWith("synthetic-load-", StringComparison.Ordinal))
        {
            string[] parts = user.Split('-');
            if (parts.Length != 4 || !int.TryParse(parts[3], out int seed) || seed is < 0 or >= 100)
                return Task.FromResult(SessionResolution.Denied());
            string mode = parts[2];
            if (mode == "error") throw new InvalidOperationException("CI_EXCEPTION_MARKER");
            if (mode == "disabled" || mode is not ("valid" or "zero" or "expired"))
                return Task.FromResult(SessionResolution.Denied());
            var session = new UserSessionIdentity(mode == "zero" ? 0 : 1_000 + seed,
                $"synthetic-{seed:D3}", "کاربر", "آزمایشی", "user", 11, "سمت آزمایشی",
                0, "", 10, "واحد آزمایشی", false, false, FixedNow.AddMinutes(-5),
                mode == "expired" ? FixedNow.AddMinutes(-1) : FixedNow.AddMinutes(10));
            return Task.FromResult(SessionResolution.Verified(
                new VerifiedAriaAccount(session.UserName, session.BadgeNumber), session));
        }
        if (user is "synthetic-valid" or "synthetic-expired")
        {
            DateTime expires = user == "synthetic-expired" ? FixedNow.AddMinutes(-1) : FixedNow.AddMinutes(10);
            var session = new UserSessionIdentity(1001, "synthetic-user", "مریم", "آزمایشی", "user",
                11, "سمت آزمایشی", 0, "", 10, "واحد آزمایشی", false, false,
                FixedNow.AddMinutes(-5), expires);
            return Task.FromResult(SessionResolution.Verified(
                new VerifiedAriaAccount(session.UserName, session.BadgeNumber), session));
        }
        return Task.FromResult(SessionResolution.Denied());
    }
}

// Only this executable registers a synthetic status source. Published Web still
// registers UnavailablePersonnelDirectory and has no test header/identity path.
internal sealed class TestPersonnelDirectory : IAriaPersonnelDirectory
{
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);

    public Task<PersonnelResolution> RecheckAsync(VerifiedAriaAccount account,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        PersonnelResolution result = account.AccountName switch
        {
            "synthetic-revoked" => PersonnelResolution.Denied(),
            "synthetic-profile-error" => PersonnelResolution.Unavailable(),
            "synthetic-profile-mismatch" => PersonnelResolution.Active(
                new VerifiedAriaAccount(account.AccountName, account.BadgeNumber + 1)),
            _ => PersonnelResolution.Active(account)
        };
        return Task.FromResult(result);
    }
}

// Not referenced by the publishable Aria.Web assembly. Explicitly added to
// the test host's MVC ApplicationParts only; it can inspect synthetic identity
// after middleware without inventing a production route or real session.
[ApiController]
[Route("test-only/session")]
public sealed class SyntheticIdentityProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var identity = AriaRequestIdentity.Get(HttpContext);
        if (identity == null) return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(new
        {
            identity.BadgeNumber,
            identity.Role,
            identity.TitleId,
            identity.DepartmentId,
            Observer = identity.IsObserver,
            TopLevelManager = identity.IsTopLevelManager,
            identity.FirstName
        });
    }
}
