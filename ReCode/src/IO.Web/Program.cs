using IO.Infrastructure.Security;
using IO.Infrastructure.Organization;
using IO.Infrastructure.Scheduling;
using IO.Domain.Scheduling;
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
            builder.Services.AddRateLimiter(o =>
            {
                o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
                o.AddPolicy("password-change", context => RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            });
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
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; connect-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
            await next(context);
        });
        app.UseAuthentication();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            // Provisioned accounts may only change their temporary credential or
            // sign out until they have selected a new password of their own.
            if (context.User.HasClaim(IdentityServices.MustChangePasswordClaim, "1") &&
                context.Request.Path != "/auth/change-password" && context.Request.Path != "/auth/logout" &&
                context.Request.Path != "/auth/csrf" && context.Request.Path != "/auth/me" &&
                context.Request.Path != "/panel" && context.Request.Path != "/assets/panel.js" &&
                context.Request.Path != "/health/live")
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next(context);
        });
        app.UseAuthorization();

        app.MapGet("/panel", () => Results.Content(Resource("IO.Web.Ui.Panel.html"), "text/html; charset=utf-8")).AllowAnonymous();
        app.MapGet("/assets/panel.js", () => Results.Content(Resource("IO.Web.Ui.panel.js"), "text/javascript; charset=utf-8")).AllowAnonymous();
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
            // Cookies are stateless: rotating the stamp revokes copied cookies,
            // at the cost of signing out this account's other sessions as well.
            var login = await signIn.UserManager.GetUserAsync(context.User);
            if (login is null) return Results.Unauthorized();
            var updated = await signIn.UserManager.UpdateSecurityStampAsync(login);
            await signIn.SignOutAsync();
            return updated.Succeeded ? Results.NoContent() : Results.StatusCode(503);
        });
        app.MapGet("/auth/me", (ClaimsPrincipal user) => Results.Ok(new { id = user.FindFirstValue(ClaimTypes.NameIdentifier) }));
        app.MapPost("/auth/change-password", async (ChangePasswordInput input, HttpContext context, IAntiforgery csrf,
            PersonalCredentials credentials, SignInManager<LoginAccount> signIn) =>
        {
            if (!await ValidCsrf(context, csrf)) return Results.BadRequest();
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
            try
            {
                await credentials.ChangePasswordAsync(actor, input.CurrentPassword, input.NewPassword, context.RequestAborted);
                await signIn.SignOutAsync();
                return Results.NoContent();
            }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            catch (ArgumentException) { return Results.BadRequest(); }
        }).RequireRateLimiting("password-change");
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
        app.MapGet("/dates/jalali/{year:int}/{month:int}/{day:int}", (int year, int month, int day) =>
        {
            try { return Results.Ok(new { date = PersianDates.ToGregorian(year, month, day) }); }
            catch (ArgumentOutOfRangeException) { return Results.BadRequest(); }
        });
        app.MapGet("/dates/gregorian/{date}", (DateOnly date) =>
        {
            try { var j = PersianDates.FromGregorian(date); return Results.Ok(new { j.Year, j.Month, j.Day }); }
            catch (ArgumentOutOfRangeException) { return Results.BadRequest(); }
        });
        app.MapGet("/schedules/{employeeId:guid}/{workDate}", async (Guid employeeId, DateOnly workDate, HttpContext context, ScheduleReader reader) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
            try
            {
                var day = await reader.FindAsync(actor, employeeId, workDate, DateOnly.FromDateTime(DateTime.UtcNow), context.RequestAborted);
                return day is null ? Results.NotFound() : Results.Ok(day);
            }
            catch (InvalidOperationException) { return Results.Conflict(); }
        });
        app.MapGet("/org/catalog/{kind}", (string kind, int? offset, HttpContext context, OrganizationManagement service) =>
            AdminRead(context, actor => service.ListAsync(actor, kind, offset ?? 0, context.RequestAborted)));
        app.MapGet("/scheduling/catalog/{kind}", (string kind, int? offset, HttpContext context, ScheduleManagement service) =>
            AdminRead(context, actor => service.ListAsync(actor, kind, offset ?? 0, context.RequestAborted)));
        app.MapPost("/org/organizations", (OrganizationInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.CreateOrganizationAsync(actor, input.Name, c.RequestAborted)));
        app.MapPost("/org/companies", (CompanyInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.CreateCompanyAsync(actor, input.OrganizationId, input.Name, c.RequestAborted)));
        app.MapPost("/org/sites", (SiteInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.CreateSiteAsync(actor, input.CompanyId, input.Name, input.TimeZoneId, c.RequestAborted)));
        app.MapPost("/org/units", (UnitInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.CreateUnitAsync(actor, input.CompanyId, input.ParentId, input.Name, c.RequestAborted)));
        app.MapPost("/org/employees", (EmployeeInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.CreateEmployeeAsync(actor, input.CompanyId, input.PersonnelNumber, input.DisplayName, c.RequestAborted)));
        app.MapPost("/org/assignments", (AssignmentInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.AddAssignmentAsync(actor, input.EmployeeId, input.UnitId, input.ValidFrom, input.ValidUntilExclusive, c.RequestAborted)));
        app.MapPost("/org/transfers", (TransferInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.TransferAsync(actor, input.EmployeeId, input.AssignmentId, input.UnitId, input.Effective, c.RequestAborted)));
        app.MapPost("/scheduling/calendars", (CalendarInput input, HttpContext c, IAntiforgery csrf, ScheduleManagement service) =>
            Admin(c, csrf, actor => service.CreateCalendarAsync(actor, input.CompanyId, input.Name, c.RequestAborted)));
        app.MapPost("/scheduling/calendar-days", (CalendarDayInput input, HttpContext c, IAntiforgery csrf, ScheduleManagement service) =>
            Admin(c, csrf, actor => service.AddCalendarDayAsync(actor, input.CalendarId, input.Date, input.Kind, input.Note, c.RequestAborted)));
        app.MapPost("/scheduling/shifts", (ShiftInput input, HttpContext c, IAntiforgery csrf, ScheduleManagement service) =>
            Admin(c, csrf, actor => service.CreateShiftAsync(actor, input.CompanyId, input.SiteId, input.Name, input.StartsAt, input.EndsAt, input.EndDayOffset, c.RequestAborted)));
        app.MapPost("/scheduling/assignments", (ScheduleInput input, HttpContext c, IAntiforgery csrf, ScheduleManagement service) =>
            Admin(c, csrf, actor => service.AssignAsync(actor, input.EmployeeId, input.CalendarId, input.ShiftId, input.ValidFrom, input.ValidUntilExclusive, c.RequestAborted)));
        app.MapPost("/scheduling/replacements", (ScheduleReplacementInput input, HttpContext c, IAntiforgery csrf, ScheduleManagement service) =>
            Admin(c, csrf, actor => service.RescheduleAsync(actor, input.EmployeeId, input.AssignmentId, input.CalendarId, input.ShiftId, input.Effective, c.RequestAborted)));
        app.MapGet("/admin/catalog/{kind}", async (string kind, int? offset, HttpContext context, IdentityAdministration admin) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
            try { return Results.Ok(await admin.ListAsync(actor, kind, offset ?? 0, context.RequestAborted)); }
            catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
            catch (ArgumentException) { return Results.BadRequest(); }
        });
        app.MapPost("/admin/users", (NewUserInput input, HttpContext context, IAntiforgery csrf, IdentityAdministration admin) =>
            Admin(context, csrf, actor => admin.CreateUserAsync(actor, input.UserName, input.Password, input.EmployeeId, context.RequestAborted)));
        app.MapPost("/admin/roles", (NewRoleInput input, HttpContext context, IAntiforgery csrf, IdentityAdministration admin) =>
            Admin(context, csrf, actor => admin.CreateRoleAsync(actor, input.Name, context.RequestAborted)));
        app.MapPost("/admin/user-roles", (UserRoleInput input, HttpContext context, IAntiforgery csrf, IdentityAdministration admin) =>
            Admin(context, csrf, actor => admin.GrantUserRoleAsync(actor, input.UserId, input.RoleId, context.RequestAborted)));
        app.MapPost("/admin/role-permissions", (RolePermissionInput input, HttpContext context, IAntiforgery csrf, IdentityAdministration admin) =>
            Admin(context, csrf, actor => admin.GrantPermissionAsync(actor, input.RoleId, input.Code, context.RequestAborted)));
        app.MapPost("/admin/user-scopes", (UserScopeInput input, HttpContext context, IAntiforgery csrf, IdentityAdministration admin) =>
            Admin(context, csrf, actor => admin.GrantScopeAsync(actor, input.UserId, input.UnitId, input.ValidFrom, input.ValidUntilExclusive, context.RequestAborted)));
        app.MapPost("/admin/user-roles/revoke", (UserRoleInput input, HttpContext context, IAntiforgery csrf, IdentityAdministration admin) =>
            Admin(context, csrf, actor => admin.RevokeUserRoleAsync(actor, input.UserId, input.RoleId, context.RequestAborted)));
        app.MapPost("/admin/role-permissions/revoke", (RolePermissionInput input, HttpContext context, IAntiforgery csrf, IdentityAdministration admin) =>
            Admin(context, csrf, actor => admin.RevokePermissionAsync(actor, input.RoleId, input.Code, context.RequestAborted)));
        app.MapPost("/admin/user-scopes/{id:guid}/revoke", (Guid id, HttpContext context, IAntiforgery csrf, IdentityAdministration admin) =>
            Admin(context, csrf, actor => admin.RevokeScopeAsync(actor, id, context.RequestAborted)));
        app.MapPost("/admin/users/{id:guid}/disable", (Guid id, HttpContext context, IAntiforgery csrf, IdentityAdministration admin) =>
            Admin(context, csrf, actor => admin.DisableUserAsync(actor, id, context.RequestAborted)));
        app.MapPost("/admin/users/{id:guid}/enable", (Guid id, HttpContext context, IAntiforgery csrf, IdentityAdministration admin) =>
            Admin(context, csrf, actor => admin.EnableUserAsync(actor, id, context.RequestAborted)));
        return app;
    }

    private static string Resource(string name)
    {
        using var stream = typeof(IoWebHost).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Embedded UI resource missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<IResult> AdminRead(HttpContext context, Func<Guid, Task<object>> action)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
        try { return Results.Ok(await action(actor)); }
        catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
        catch (ArgumentException) { return Results.BadRequest(); }
    }

    private static async Task<IResult> Admin<T>(HttpContext context, IAntiforgery antiforgery, Func<Guid, Task<T>> action)
    {
        if (!await ValidCsrf(context, antiforgery)) return Results.BadRequest();
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
        try { return Results.Ok(new { result = await action(actor) }); }
        catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
        catch (DomainConflictException) { return Results.Conflict(); }
        catch (ArgumentException) { return Results.BadRequest(); }
    }

    private static async Task<bool> ValidCsrf(HttpContext context, IAntiforgery antiforgery)
    {
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }
}

public sealed record LoginInput(string UserName, string Password);
public sealed record ChangePasswordInput(string CurrentPassword, string NewPassword);
public sealed record NewUserInput(string UserName, string Password, Guid? EmployeeId);
public sealed record NewRoleInput(string Name);
public sealed record UserRoleInput(Guid UserId, Guid RoleId);
public sealed record RolePermissionInput(Guid RoleId, string Code);
public sealed record UserScopeInput(Guid UserId, Guid UnitId, DateOnly ValidFrom, DateOnly? ValidUntilExclusive);

public sealed record OrganizationInput(string Name);
public sealed record CompanyInput(Guid OrganizationId, string Name);
public sealed record SiteInput(Guid CompanyId, string Name, string TimeZoneId);
public sealed record UnitInput(Guid CompanyId, Guid? ParentId, string Name);
public sealed record EmployeeInput(Guid CompanyId, string PersonnelNumber, string DisplayName);
public sealed record AssignmentInput(Guid EmployeeId, Guid UnitId, DateOnly ValidFrom, DateOnly? ValidUntilExclusive);
public sealed record TransferInput(Guid EmployeeId, Guid AssignmentId, Guid UnitId, DateOnly Effective);
public sealed record CalendarInput(Guid CompanyId, string Name);
public sealed record CalendarDayInput(Guid CalendarId, DateOnly Date, int Kind, string? Note);
public sealed record ShiftInput(Guid CompanyId, Guid SiteId, string Name, TimeOnly StartsAt, TimeOnly EndsAt, int EndDayOffset);
public sealed record ScheduleInput(Guid EmployeeId, Guid CalendarId, Guid ShiftId, DateOnly ValidFrom, DateOnly? ValidUntilExclusive);
public sealed record ScheduleReplacementInput(Guid EmployeeId, Guid AssignmentId, Guid CalendarId, Guid ShiftId, DateOnly Effective);
