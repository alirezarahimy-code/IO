using AriaAdmin.Web.Security;

namespace AriaAdmin.Web;

internal static class Program
{
    private static async Task Main(string[] args) => await CreateApp(args).RunAsync();

    // Test overrides are callable only from the separately built friend test
    // assembly, not through HTTP, appsettings, headers or a deployed flag.
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
        builder.Services.AddSingleton<IAdminIdentitySource, IisWindowsIdentitySource>();
        builder.Services.AddSingleton<IAdminDirectory, UnavailableAdminDirectory>();
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
        app.UseStaticFiles(); // Public offline CSS only; never serve source/config as static files.
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;
            if (string.Equals(path, "/health/live", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/Account/NoAccess", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            string? windowsUser = context.RequestServices
                .GetRequiredService<IAdminIdentitySource>().GetWindowsUser(context);
            if (string.IsNullOrWhiteSpace(windowsUser))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers["WWW-Authenticate"] = "Negotiate";
                return;
            }
            DirectoryResolution decision;
            try
            {
                decision = await context.RequestServices.GetRequiredService<IAdminDirectory>()
                    .ResolveAsync(windowsUser, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Never convert a failed directory read into a successful or denied user.
                decision = DirectoryResolution.Unavailable();
            }
            if (decision.State == DirectoryState.Unavailable)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("اتصال ایمن به پایگاه‌داده و مجوزها هنوز فراهم نیست.");
                return;
            }
            if (decision.State != DirectoryState.Allowed || decision.Session == null ||
                decision.Session.BadgeNumber <= 0 || decision.Session.Levels.Count == 0)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            PortalSession.Set(context, decision.Session);
            await next();
        });
        app.MapGet("/health/live", () => Results.Json(new { status = "alive", product = "آریا ادمین" }));
        app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");
        return app;
    }
}
