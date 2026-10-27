using Aria.Web.Security;

namespace Aria.Web;

internal static class Program
{
    // Temporary fail-closed upper bound, NOT an observed operational login TTL:
    // TWF 4.28's shipped Web.config says CookiesTime=20 minutes, but its
    // server-side legacy filter admits a manually resubmitted token for 8 hours
    // and the settings DB may override the cookie value. Never carry either
    // untrusted legacy value into a fresh session without a login/SQL golden.
    private static readonly TimeSpan MaximumUnprovenSessionLifetime = TimeSpan.FromMinutes(20);

    private static async Task Main(string[] args) => await CreateApp(args).RunAsync();

    // The injectable boundary is internal, exposed only to a separately compiled
    // friend test executable. Published Web has no HTTP/config/header test mode.
    internal static WebApplication CreateApp(string[] args,
        Action<IServiceCollection>? testOverrides = null, string? contentRoot = null)
    {
        var options = new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = contentRoot ?? Directory.GetCurrentDirectory(),
            ApplicationName = typeof(Program).Assembly.GetName().Name
        };
        var builder = WebApplication.CreateBuilder(options);
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(Program).Assembly);
        builder.Services.AddSingleton<IAriaSessionVerifier, UnavailableLegacySessionVerifier>();
        builder.Services.AddSingleton<IAriaPersonnelDirectory, UnavailablePersonnelDirectory>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        testOverrides?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseExceptionHandler(errors => errors.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("خطای داخلی سامانه.");
        }));
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; style-src 'self'; img-src 'self' data:; script-src 'self'; " +
                "font-src 'self'; connect-src 'self'; form-action 'self'; frame-ancestors 'none'";
            await next();
        });
        app.UseStaticFiles(); // Only a public bundled stylesheet exists under wwwroot.
        app.Use(async (context, next) =>
        {
            string? path = context.Request.Path.Value;
            if (string.Equals(path, "/health/live", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/Login/Login", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/Home/About", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/Diagnostics/Health", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/Diagnostics/Schema", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            SessionResolution decision;
            try
            {
                decision = await context.RequestServices.GetRequiredService<IAriaSessionVerifier>()
                    .VerifyAsync(context, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Never turn verification failures into an anonymous/allowed session.
                decision = SessionResolution.Unavailable();
            }

            if (decision == null || decision.Decision == SessionDecision.Unavailable)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("تأیید ایمن نشست و اتصال داده هنوز فراهم نیست.");
                return;
            }
            if (decision.Decision == SessionDecision.Anonymous)
            {
                // Local fixed destination only; no untrusted returnUrl/open redirect.
                context.Response.Redirect("/Login/Login");
                return;
            }
            var account = decision.Account;
            var identity = decision.Identity;
            var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
            // A session hydrated from Personnel/T5 must belong to the SAME
            // verified account and badge, not merely claim a role or user name.
            // Normalization and genuine admin rights need Aria's own original
            // golden and cannot be inferred from the linked rewrite's IsAdmin.
            if (decision.Decision != SessionDecision.Verified || account == null || identity == null ||
                account.BadgeNumber <= 0 || identity.BadgeNumber != account.BadgeNumber ||
                string.IsNullOrWhiteSpace(account.AccountName) ||
                !string.Equals(account.AccountName, identity.UserName, StringComparison.OrdinalIgnoreCase) ||
                identity.IssuedAtUtc.Kind != DateTimeKind.Utc ||
                identity.ExpiresAtUtc.Kind != DateTimeKind.Utc ||
                identity.IssuedAtUtc > now || identity.ExpiresAtUtc <= now ||
                identity.ExpiresAtUtc <= identity.IssuedAtUtc ||
                identity.ExpiresAtUtc - identity.IssuedAtUtc > MaximumUnprovenSessionLifetime)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            // The linked rewrite's IsAdmin can become true from a name/role
            // alone. Normal Personnel eligibility does not prove T5 manager rights.
            if (identity.IsAdmin)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            PersonnelResolution current;
            try
            {
                current = await context.RequestServices.GetRequiredService<IAriaPersonnelDirectory>()
                    .RecheckAsync(account, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                current = PersonnelResolution.Unavailable();
            }
            if (current == null || current.State == PersonnelState.Unavailable)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("بازبینی وضعیت پرسنلی هنوز فراهم نیست.");
                return;
            }
            if (current.State != PersonnelState.Active || current.Person == null ||
                current.Person.BadgeNumber != account.BadgeNumber ||
                !string.Equals(current.Person.AccountName, account.AccountName,
                    StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            // Only this least-privileged identity enters application controllers.
            // Token-supplied titles, department, observer/manager flags and names
            // must not silently become permissions after a Personnel-only recheck.
            AriaRequestIdentity.Set(context,
                AriaRequestIdentity.LeastPrivilege(current.Person, identity));
            await next();
        });
        app.MapGet("/health/live", () => Results.Json(new { status = "alive", product = "آریا" }));
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        return app;
    }
}
