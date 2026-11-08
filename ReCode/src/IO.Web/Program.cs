using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.RateLimiting;

var app = IoWebHost.Build(args);
await app.RunAsync();

/// <summary>Builds the host without creating databases or seeding accounts.</summary>
public static class IoWebHost
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        var enabled = builder.Configuration.GetValue<bool>("IO:EnableExperimentalIdentity");
        if (enabled)
        {
            var connection = builder.Configuration.GetConnectionString("IO");
            if (string.IsNullOrWhiteSpace(connection))
                throw new InvalidOperationException("Explicit IO connection configuration is required.");
            builder.Services.AddIoIdentityStorage(connection);
            builder.Services.AddScoped<EmployeeAccessReader>();
            builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
            builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
            builder.Services.ConfigureApplicationCookie(o =>
            {
                o.Cookie.Name = "__Host-IO.Session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.Path = "/";
                o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                o.SlidingExpiration = false;
                o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
                o.Events.OnValidatePrincipal = async c =>
                {
                    await SecurityStampValidator.ValidatePrincipalAsync(c);
                    if (c.Principal?.Identity?.IsAuthenticated != true) return;
                    var id = c.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
                    var db = c.HttpContext.RequestServices.GetRequiredService<IoDbContext>();
                    if (!Guid.TryParse(id, out var userId) ||
                        !await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive, c.HttpContext.RequestAborted))
                    {
                        c.RejectPrincipal();
                        await c.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                    }
                };
            });
            builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(
                new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
            builder.Services.AddAntiforgery(o =>
            {
                o.HeaderName = "X-CSRF-TOKEN";
                o.Cookie.Name = "__Host-IO.Antiforgery";
                o.Cookie.Path = "/";
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Strict;
            });
            builder.Services.AddRateLimiter(o => o.AddPolicy("login", context =>
                RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        }
        var app = builder.Build();
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
        app.MapGet("/", () => Results.Ok(new { service = "IO", status = "foundation-only" })).AllowAnonymous();
        if (!enabled) return app;

        // No forwarded headers are trusted automatically. A deployment must
        // configure a trusted TLS terminator deliberately before enabling this.
        app.Use(async (context, next) =>
        {
            if (!context.Request.IsHttps) { context.Response.StatusCode = 400; return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await next(context);
        });
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken })).AllowAnonymous();
        app.MapPost("/auth/login", async (LoginInput input, HttpContext context, IAntiforgery antiforgery,
            SignInManager<LoginAccount> signIn, IoDbContext db) =>
        {
            if (!await ValidCsrf(context, antiforgery)) return Results.BadRequest();
            if (string.IsNullOrWhiteSpace(input.UserName) || input.UserName.Length > 200 ||
                string.IsNullOrEmpty(input.Password) || input.Password.Length > 256) return Results.Unauthorized();
            var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, lockoutOnFailure: true);
            if (!result.Succeeded) return Results.Unauthorized();
            var login = await signIn.UserManager.FindByNameAsync(input.UserName);
            if (login is null || !await db.Users.AsNoTracking().AnyAsync(u => u.Id == login.Id && u.IsActive, context.RequestAborted))
            {
                await signIn.SignOutAsync();
                return Results.Unauthorized();
            }
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("login");
        app.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery, SignInManager<LoginAccount> signIn) =>
        {
            if (!await ValidCsrf(context, antiforgery)) return Results.BadRequest();
            await signIn.SignOutAsync();
            return Results.NoContent();
        });
        app.MapGet("/auth/me", (ClaimsPrincipal user) => Results.Ok(new { id = user.FindFirstValue(ClaimTypes.NameIdentifier) }));
        app.MapGet("/employees/{id:guid}", async (Guid id, ClaimsPrincipal principal, EmployeeAccessReader reader,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
            // Temporary policy: UTC business date until D09/D10 establish the
            // trusted organizational time-zone and historical access rules.
            var date = DateOnly.FromDateTime(DateTime.UtcNow);
            var employee = await reader.FindAuthorizedAsync(actor, id, date, cancellationToken);
            return employee is null ? Results.NotFound() : Results.Ok(employee);
        });
        return app;
    }

    private static async Task<bool> ValidCsrf(HttpContext context, IAntiforgery antiforgery)
    {
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }
}

public sealed record LoginInput(string UserName, string Password);
