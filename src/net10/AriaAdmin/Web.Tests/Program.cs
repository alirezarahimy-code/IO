using System.Net;
using AriaAdmin.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using T5Web.Domain;
using T5Web.Domain.Models;
using WebHost = AriaAdmin.Web.Program;

namespace AriaAdmin.Web.Tests;

// Standalone HTTP process test: Kestrel + compiled Razor, no TestServer/NuGet,
// no SQL, no domain credentials and no fake-auth hook in the published Web DLL.
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || !File.Exists(Path.Combine(args[0], "AriaAdmin.Web.csproj")))
            throw new ArgumentException("Pass the local AriaAdmin/Web project directory (synthetic only).");
        string webRoot = Path.GetFullPath(args[0]);

        await using (var production = WebHost.CreateApp(Array.Empty<string>(), contentRoot: webRoot))
        {
            using var client = await Start(production);
            await Check("AA-WEB-01: process liveness is not readiness", async () =>
            {
                using var response = await Send(client, "/health/live");
                Equal(HttpStatusCode.OK, response.StatusCode, "liveness");
                string body = await response.Content.ReadAsStringAsync();
                Require(body.Contains("alive", StringComparison.Ordinal), "health payload");
                Require(!body.Contains("connection", StringComparison.OrdinalIgnoreCase), "no config disclosure");
            });
            await Check("AA-WEB-02: production never requests a second Windows password", async () =>
            {
                using var response = await Send(client, "/Dashboard/Index", "synthetic-admin");
                Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode, "unverified shared login must fail closed");
                Require(!response.Headers.WwwAuthenticate.Any(), "no separate Windows/Negotiate credential challenge");
                string body = await response.Content.ReadAsStringAsync();
                Require(body.Contains("ورود مشترک", StringComparison.Ordinal) &&
                    !body.Contains("synthetic-admin", StringComparison.Ordinal), "no synthetic header bypass or identity disclosure");
            });
            await Check("AA-WEB-03: no-access Core Razor page, UTF-8 RTL, offline assets", async () =>
            {
                using var response = await Send(client, "/Account/NoAccess");
                Equal(HttpStatusCode.Forbidden, response.StatusCode, "no-access status");
                string html = await response.Content.ReadAsStringAsync();
                Require(html.Contains("lang=\"fa\"", StringComparison.Ordinal) &&
                    html.Contains("dir=\"rtl\"", StringComparison.Ordinal) &&
                    html.Contains("دسترسی مجاز نیست", StringComparison.Ordinal), "compiled Persian Razor layout");
                Require(html.Contains("/assets/admin.css", StringComparison.Ordinal), "bundled stylesheet");
                Require(!html.Contains("https://", StringComparison.OrdinalIgnoreCase), "no CDN asset");
                Require(response.Headers.TryGetValues("Content-Security-Policy", out var csp) &&
                    csp.Any(x => x.Contains("default-src 'self'", StringComparison.Ordinal)), "self-only CSP");
            });
            await Check("AA-WEB-04: local CSS is served offline", async () =>
            {
                using var response = await Send(client, "/assets/admin.css");
                Equal(HttpStatusCode.OK, response.StatusCode, "offline CSS");
                Require(response.Content.Headers.ContentType?.MediaType == "text/css", "CSS content type");
                string css = await response.Content.ReadAsStringAsync();
                Require(css.Contains("--ink", StringComparison.Ordinal) &&
                    !css.Contains("url(http", StringComparison.OrdinalIgnoreCase), "local CSS without remote URL");
            });
        }

        await using (var synthetic = WebHost.CreateApp(Array.Empty<string>(), services =>
        {
            services.AddSingleton<IAdminIdentitySource, HeaderIdentitySource>();
            services.AddSingleton<IAdminDirectory, SyntheticDirectory>();
        }, webRoot))
        {
            using var client = await Start(synthetic);
            await Check("AA-WEB-05: anonymous never reaches a controller", async () =>
            {
                using var response = await Send(client, "/");
                Equal(HttpStatusCode.Unauthorized, response.StatusCode, "anonymous test-only source");
                Require(!response.Headers.WwwAuthenticate.Any(), "no second Windows password prompt");
            });
            await Check("AA-WEB-06: unknown/disabled identities are forbidden", async () =>
            {
                foreach (string user in new[] { "synthetic-unknown", "synthetic-disabled" })
                {
                    using var response = await Send(client, "/Dashboard/Index", user);
                    Equal(HttpStatusCode.Forbidden, response.StatusCode, user);
                }
            });
            await Check("AA-WEB-07: Home/Index retains redirect but dashboard has no fake data", async () =>
            {
                using var home = await Send(client, "/Home/Index", "synthetic-admin");
                Equal(HttpStatusCode.Redirect, home.StatusCode, "Home redirect");
                // MVC may shorten Dashboard/Index to "/" because both are route defaults.
                // Follow the actual local redirect rather than requiring a textual path.
                var redirect = home.Headers.Location ?? throw new InvalidOperationException("missing MVC redirect");
                Require(!redirect.IsAbsoluteUri, "redirect must stay on this host");
                using var dashboard = await Send(client, redirect.OriginalString, "synthetic-admin");
                Equal(HttpStatusCode.ServiceUnavailable, dashboard.StatusCode, "no fake operational dashboard");
                string body = await dashboard.Content.ReadAsStringAsync();
                Require(body.Contains("در دسترس نیست", StringComparison.Ordinal) &&
                    !body.Contains("System.Exception", StringComparison.Ordinal), "safe Persian 503");
            });
            await Check("AA-WEB-08: Diagnostics requires PortalAdmin and stays unavailable", async () =>
            {
                using var viewer = await Send(client, "/Diagnostics/Index", "synthetic-viewer");
                Equal(HttpStatusCode.Forbidden, viewer.StatusCode, "viewer diagnostics");
                using var admin = await Send(client, "/Diagnostics/Index", "synthetic-admin");
                Equal(HttpStatusCode.ServiceUnavailable, admin.StatusCode, "no fake DB diagnostics");
            });
            await Check("AA-WEB-09: directory failures cannot become allow/deny", async () =>
            {
                using var response = await Send(client, "/Dashboard/Index", "synthetic-error");
                Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode, "directory exception");
                string body = await response.Content.ReadAsStringAsync();
                Require(!body.Contains("CI_EXCEPTION_MARKER", StringComparison.Ordinal), "no error disclosure");
            });
            await Check("AA-WEB-10: POST endpoint is not an unprotected action", async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/Dashboard/Index");
                request.Headers.TryAddWithoutValidation("X-Synthetic-Identity", "synthetic-admin");
                using var response = await client.SendAsync(request);
                Require(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                    "POST must not dispatch a state-changing MVC action");
            });
            await Check("AA-WEB-11: no catch-all route to silently succeed", async () =>
            {
                using var response = await Send(client, "/invented-controller/Index", "synthetic-admin");
                Equal(HttpStatusCode.NotFound, response.StatusCode, "unimplemented route");
            });
            await Check("AA-WEB-12: request-scoped identity under 40 overlapping requests", async () =>
            {
                var tasks = Enumerable.Range(0, 40).Select(async i =>
                {
                    bool allowed = i % 2 == 0;
                    using var response = await Send(client, "/Dashboard/Index",
                        allowed ? "synthetic-viewer" : "synthetic-disabled");
                    Equal(allowed ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden,
                        response.StatusCode, "request " + i);
                });
                await Task.WhenAll(tasks);
            });
            await Check("AA-WEB-13: 100 distinct test-only principals retain PortalAdmin denial", async () =>
            {
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                string[] modes = ["admin", "viewer", "disabled", "zero", "error"];
                Task[] requests = Enumerable.Range(0, 100).Select(async i =>
                {
                    await start.Task;
                    string mode = modes[i % modes.Length];
                    string identity = $"synthetic-load-{mode}-{i:D3}";
                    using var response = await Send(client, "/Diagnostics/Index", identity);
                    Equal(mode is "admin" or "error" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden,
                        response.StatusCode, "synthetic principal " + i + " " + mode);
                    Require(!response.Headers.TryGetValues("Set-Cookie", out _), "no test session cookie");
                    Require(response.Headers.TryGetValues("Cache-Control", out var cache) && cache.Contains("no-store"),
                        "no cache of access decisions");
                    string body = await response.Content.ReadAsStringAsync();
                    Require(!body.Contains("synthetic-load-", StringComparison.Ordinal) &&
                        !body.Contains("CI_EXCEPTION_MARKER", StringComparison.Ordinal),
                        "no identity/exception disclosure");
                }).ToArray();
                start.SetResult(true);
                await Task.WhenAll(requests);
                // 100 negative-security requests, NOT 100 operational Windows users,
                // actual reports, an IIS/SQL load test or a latency/throughput SLA.
            });
        }

        Console.WriteLine($"Summary: {_passed} Passed, {_failed} Failed (net10.0 HTTP/Razor synthetic only)");
        return _failed == 0 && _passed == 13 ? 0 : 1;
    }

    private static async Task<HttpClient> Start(WebApplication app)
    {
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?
            .Addresses.Single(x => x.StartsWith("http://127.0.0.1:", StringComparison.Ordinal))
            ?? throw new Exception("Kestrel did not bind a synthetic loopback port");
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string path, string? user = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (user != null) request.Headers.TryAddWithoutValidation("X-Synthetic-Identity", user);
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

// These classes are compiled ONLY into AriaAdmin.Web.Tests, never into Web/publish.
internal sealed class HeaderIdentitySource : IAdminIdentitySource
{
    public Task<AccountResolution> ResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(context.Request.Headers.TryGetValue("X-Synthetic-Identity", out var identity) &&
            !string.IsNullOrWhiteSpace(identity.ToString())
                ? AccountResolution.Verified(identity.ToString())
                : AccountResolution.Anonymous());
}

internal sealed class SyntheticDirectory : IAdminDirectory
{
    public Task<DirectoryResolution> ResolveAsync(string verifiedAccountName, CancellationToken cancellationToken)
    {
        if (verifiedAccountName == "synthetic-error") throw new InvalidOperationException("CI_EXCEPTION_MARKER");
        if (verifiedAccountName.StartsWith("synthetic-load-", StringComparison.Ordinal))
        {
            string[] parts = verifiedAccountName.Split('-');
            if (parts.Length != 4 || !int.TryParse(parts[3], out int seed) || seed is < 0 or >= 100)
                return Task.FromResult(DirectoryResolution.Denied());
            string mode = parts[2];
            if (mode == "error") throw new InvalidOperationException("CI_EXCEPTION_MARKER");
            if (mode == "disabled" || mode is not ("admin" or "viewer" or "zero"))
                return Task.FromResult(DirectoryResolution.Denied());
            var session = new AdminSession {
                BadgeNumber = mode == "zero" ? 0 : 1_000 + seed,
                DisplayName = $"کاربر ساختگی {seed:D3}"
            };
            session.Grant(AdminFeature.PersonnelView, AccessLevel.ReadOnly);
            if (mode is "admin" or "zero") session.Grant(AdminFeature.PortalAdmin, AccessLevel.Allow);
            return Task.FromResult(DirectoryResolution.Allowed(session));
        }
        if (verifiedAccountName is "synthetic-disabled" or "synthetic-unknown")
            return Task.FromResult(DirectoryResolution.Denied());
        if (verifiedAccountName is "synthetic-admin" or "synthetic-viewer")
        {
            var session = new AdminSession { BadgeNumber = 1001, DisplayName = "اپراتور ساختگی" };
            session.Grant(AdminFeature.PersonnelView, AccessLevel.ReadOnly);
            if (verifiedAccountName == "synthetic-admin") session.Grant(AdminFeature.PortalAdmin, AccessLevel.Allow);
            return Task.FromResult(DirectoryResolution.Allowed(session));
        }
        return Task.FromResult(DirectoryResolution.Denied());
    }
}
