using Aria.Web.Security;

namespace Aria.Web;

internal static class Program
{
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

            if (decision.Decision == SessionDecision.Unavailable)
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
            var identity = decision.Identity;
            if (decision.Decision != SessionDecision.Verified || identity == null ||
                identity.BadgeNumber < 0 || identity.ExpiresAtUtc <=
                    context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            AriaRequestIdentity.Set(context, identity); // request-scoped; no ThreadStatic/HttpContext.Current.
            await next();
        });
        app.MapGet("/health/live", () => Results.Json(new { status = "alive", product = "آریا" }));
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        return app;
    }
}
