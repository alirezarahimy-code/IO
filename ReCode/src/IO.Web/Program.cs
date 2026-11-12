using IO.Infrastructure.Devices;
using Microsoft.AspNetCore.DataProtection;
using IO.Infrastructure.Security;
using IO.Infrastructure.Organization;
using IO.Infrastructure.Attendance;
using IO.Infrastructure.Workflows;
using IO.Infrastructure.Periods;
using IO.Infrastructure.Calculation;
using IO.Domain.Attendance;
using IO.Infrastructure.Scheduling;
using IO.Domain.Scheduling;
using IO.Infrastructure.Storage;
using IO.Infrastructure.Reports;
using IO.Infrastructure.Leaves;
using IO.Infrastructure.Notifications;
using IO.Infrastructure.Delegation;
using IO.Infrastructure.Settings;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Globalization;
using System.Text.RegularExpressions;
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
            var keyRing = builder.Configuration["IO:DataProtection:KeyRingPath"];
            var keyIdentity = builder.Configuration["IO:DataProtection:ApplicationName"];
            if (keyRing is not null || keyIdentity is not null)
            {
                if (string.IsNullOrWhiteSpace(keyRing) || string.IsNullOrWhiteSpace(keyIdentity) || !Path.IsPathFullyQualified(keyRing) || !Directory.Exists(keyRing))
                    throw new InvalidOperationException("An existing absolute key-ring path and explicit application identity are both required.");
                builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRing)).SetApplicationName(keyIdentity);
            }

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
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
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
                context.Request.Path != "/assets/access.js" && context.Request.Path != "/assets/theme.js" && context.Request.Path != "/assets/theme.css" &&
                context.Request.Path != "/workspace/account" && context.Request.Path != "/assets/account.js" &&
                context.Request.Path != "/assets/employees.css" && context.Request.Path != "/login" && context.Request.Path != "/assets/login.js" &&
                context.Request.Path != "/health/live" && context.Request.Path != "/workspace/help" &&
                context.Request.Path != "/workspace/help/login" && context.Request.Path != "/assets/help.js")
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next(context);
        });
        app.UseAuthorization();

        app.MapGet("/panel", () => Results.Content(Resource("IO.Web.Ui.Panel.html"), "text/html; charset=utf-8")).AllowAnonymous();
        app.MapGet("/login", () => Results.Content(Resource("IO.Web.Ui.Login.html"), "text/html; charset=utf-8")).AllowAnonymous();
        app.MapGet("/assets/login.js", () => Results.Content(Resource("IO.Web.Ui.login.js"), "text/javascript; charset=utf-8")).AllowAnonymous();
        app.MapGet("/workspace", () => Results.Content(Resource("IO.Web.Ui.Index.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/index.js", () => Results.Content(Resource("IO.Web.Ui.index.js"), "text/javascript; charset=utf-8"));
        // The role panel is chosen by effective server-side permissions, never by
        // client claims; a flag here only reveals which tool sections may render.
        app.MapGet("/workspace/capabilities", async (HttpContext context, IoDbContext db) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
            var permissions = await WorkspaceCatalog.EffectiveAsync(actor, db, context.RequestAborted);
            var pages = WorkspaceCatalog.Pages.Where(p => WorkspaceCatalog.CanView(p, permissions)).ToArray();
            var operatorRole = PermissionCatalog.Templates.Single(t => t.Key == "operator").Id;
            var isOperator = await (from user in db.Users join ur in db.UserRoles on user.Id equals ur.UserId
                where user.Id == actor && user.IsActive && ur.RoleId == operatorRole select ur.UserId).AnyAsync(context.RequestAborted);
            return Results.Ok(new {
                employee = pages.Any(p => p.Group == "employee"), manager = pages.Any(p => p.Group == "manager"),
                admin = pages.Any(p => p.Group == "admin"), executive = pages.Any(p => p.Group == "executive"),
                @operator = isOperator, permissions = permissions.Order(StringComparer.Ordinal),
                pages = pages.Select(p => new { p.Route, p.Title, p.Group })
            });
        });
        app.MapGet("/workspace/permission-catalog", () => Results.Ok(PermissionCatalog.All));
        app.MapPost("/admin/role-templates", (HttpContext c, IAntiforgery csrf, IdentityAdministration admin) =>
            Admin(c, csrf, actor => admin.CreateTemplatesAsync(actor, c.RequestAborted)));
        var helpArticles = SystemHelp.Parse(Resource("IO.Web.Ui.HelpContent.json"));
        app.MapGet("/workspace/help", () => Results.Content(Resource("IO.Web.Ui.Help.html"), "text/html; charset=utf-8")).AllowAnonymous();
        app.MapGet("/workspace/help/{topic}", () => Results.Content(Resource("IO.Web.Ui.Help.html"), "text/html; charset=utf-8")).AllowAnonymous();
        app.MapGet("/assets/help.js", () => Results.Content(Resource("IO.Web.Ui.help.js"), "text/javascript; charset=utf-8")).AllowAnonymous();
        app.MapGet("/help/topics", async (HttpContext c, IoDbContext db) =>
        {
            var signedIn = Guid.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor);
            var permissions = signedIn ? await WorkspaceCatalog.EffectiveAsync(actor, db, c.RequestAborted) : new HashSet<string>(StringComparer.Ordinal);
            return Results.Ok(helpArticles.Where(a => SystemHelp.CanRead(a, signedIn, permissions)).Select(a =>
                new { a.Slug, a.Title, a.Group, search = a.Title + " " + a.Group + " " + a.Overview + " " + string.Join(" ", a.Steps) }));
        }).AllowAnonymous();
        app.MapGet("/help/topic/{slug}", async (string slug, HttpContext c, IoDbContext db) =>
        {
            var article = helpArticles.SingleOrDefault(a => a.Slug == slug);
            if (article is null) return Results.NotFound();
            var signedIn = Guid.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor);
            var permissions = signedIn ? await WorkspaceCatalog.EffectiveAsync(actor, db, c.RequestAborted) : new HashSet<string>(StringComparer.Ordinal);
            if (!SystemHelp.CanRead(article, signedIn, permissions)) return Results.StatusCode(403);
            var page = WorkspaceCatalog.Pages.Single(p => p.Route == article.Route);
            return Results.Ok(new { article.Slug, article.Route, article.Title, article.Group, article.Overview,
                article.Steps, article.Example, article.Fields, article.Actions, article.Errors, article.Limitations,
                permissions = page.AnyPermissions });
        }).AllowAnonymous();
        app.MapGet("/workspace/account", () => Results.Content(Resource("IO.Web.Ui.Account.html"), "text/html; charset=utf-8"));
        app.MapGet("/workspace/device-files", () => Results.Content(Resource("IO.Web.Ui.DeviceFiles.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/device-files.js", () => Results.Content(Resource("IO.Web.Ui.device-files.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/device-user-uploads", () => Results.Content(Resource("IO.Web.Ui.DeviceUserUploads.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/device-user-uploads.js", () => Results.Content(Resource("IO.Web.Ui.device-user-uploads.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/device-settings", () => Results.Content(Resource("IO.Web.Ui.DeviceSettings.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/device-settings.js", () => Results.Content(Resource("IO.Web.Ui.device-settings.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/device-commands", () => Results.Content(Resource("IO.Web.Ui.DeviceCommands.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/device-commands.js", () => Results.Content(Resource("IO.Web.Ui.device-commands.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/device-schedules", () => Results.Content(Resource("IO.Web.Ui.DeviceSchedules.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/device-schedules.js", () => Results.Content(Resource("IO.Web.Ui.device-schedules.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/device-attendance", () => Results.Content(Resource("IO.Web.Ui.DeviceAttendance.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/device-attendance.js", () => Results.Content(Resource("IO.Web.Ui.device-attendance.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/device-users", () => Results.Content(Resource("IO.Web.Ui.DeviceUsers.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/device-users.js", () => Results.Content(Resource("IO.Web.Ui.device-users.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/device-information", () => Results.Content(Resource("IO.Web.Ui.DeviceInformation.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/device-information.js", () => Results.Content(Resource("IO.Web.Ui.device-information.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/device-connection", () => Results.Content(Resource("IO.Web.Ui.DeviceConnection.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/device-connection.js", () => Results.Content(Resource("IO.Web.Ui.device-connection.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/device-groups", () => Results.Content(Resource("IO.Web.Ui.DeviceGroups.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/device-groups.js", () => Results.Content(Resource("IO.Web.Ui.device-groups.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/devices", () => Results.Content(Resource("IO.Web.Ui.Devices.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/devices.js", () => Results.Content(Resource("IO.Web.Ui.devices.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/assets/account.js", () => Results.Content(Resource("IO.Web.Ui.account.js"), "text/javascript; charset=utf-8"));
        // Shell and assets are authenticated; all data operations additionally
        // enforce organization.manage in the service, never merely in the UI.
        foreach (var page in new[] { "/workspace/calendars", "/workspace/shifts", "/workspace/schedules", "/workspace/employee-history", "/workspace/sources" })
            app.MapGet(page, () => Results.Content(Resource("IO.Web.Ui.Scheduling.html"), "text/html; charset=utf-8"));
        app.MapGet("/workspace/roles", () => Results.Content(Resource("IO.Web.Ui.Roles.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/roles.js", () => Results.Content(Resource("IO.Web.Ui.roles.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/users", () => Results.Content(Resource("IO.Web.Ui.Users.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/users.js", () => Results.Content(Resource("IO.Web.Ui.users.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/audit", () => Results.Content(Resource("IO.Web.Ui.Audit.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/audit.js", () => Results.Content(Resource("IO.Web.Ui.audit.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/executive-home", () => Results.Content(Resource("IO.Web.Ui.ExecutiveHome.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/executive-home.js", () => Results.Content(Resource("IO.Web.Ui.executive-home.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/manager-home", () => Results.Content(Resource("IO.Web.Ui.ManagerHome.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/manager-home.js", () => Results.Content(Resource("IO.Web.Ui.manager-home.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/employee-home", () => Results.Content(Resource("IO.Web.Ui.EmployeeHome.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/employee-home.js", () => Results.Content(Resource("IO.Web.Ui.employee-home.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/workflow", () => Results.Content(Resource("IO.Web.Ui.Workflow.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/workflow.js", () => Results.Content(Resource("IO.Web.Ui.workflow.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/report-builder", () => Results.Content(Resource("IO.Web.Ui.ReportBuilder.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/report-builder.js", () => Results.Content(Resource("IO.Web.Ui.report-builder.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/reports", () => Results.Content(Resource("IO.Web.Ui.Reports.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/reports.js", () => Results.Content(Resource("IO.Web.Ui.reports.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/my-reports", () => Results.Content(Resource("IO.Web.Ui.MyReports.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/my-reports.js", () => Results.Content(Resource("IO.Web.Ui.my-reports.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/team-attendance", () => Results.Content(Resource("IO.Web.Ui.TeamAttendance.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/team-attendance.js", () => Results.Content(Resource("IO.Web.Ui.team-attendance.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/leave-admin", () => Results.Content(Resource("IO.Web.Ui.LeaveAdmin.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/leave-admin.js", () => Results.Content(Resource("IO.Web.Ui.leave-admin.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/settings", () => Results.Content(Resource("IO.Web.Ui.Settings.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/settings.js", () => Results.Content(Resource("IO.Web.Ui.settings.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/delegation", () => Results.Content(Resource("IO.Web.Ui.Delegation.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/delegation.js", () => Results.Content(Resource("IO.Web.Ui.delegation.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/my-leave", () => Results.Content(Resource("IO.Web.Ui.MyLeave.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/my-leave.js", () => Results.Content(Resource("IO.Web.Ui.my-leave.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/my-attendance", () => Results.Content(Resource("IO.Web.Ui.MyAttendance.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/my-attendance.js", () => Results.Content(Resource("IO.Web.Ui.my-attendance.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/my-schedule", () => Results.Content(Resource("IO.Web.Ui.MySchedule.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/my-schedule.js", () => Results.Content(Resource("IO.Web.Ui.my-schedule.js"), "text/javascript; charset=utf-8"));
        foreach (var page in new[] { "/workspace/inbox", "/workspace/decision-history" })
            app.MapGet(page, () => Results.Content(Resource("IO.Web.Ui.ManagerRequests.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/manager-requests.js", () => Results.Content(Resource("IO.Web.Ui.manager-requests.js"), "text/javascript; charset=utf-8"));
        foreach (var page in new[] { "/workspace/my-requests", "/workspace/new-request" })
            app.MapGet(page, () => Results.Content(Resource("IO.Web.Ui.MyRequests.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/my-requests.js", () => Results.Content(Resource("IO.Web.Ui.my-requests.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/periods", () => Results.Content(Resource("IO.Web.Ui.Periods.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/periods.js", () => Results.Content(Resource("IO.Web.Ui.periods.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/calculations", () => Results.Content(Resource("IO.Web.Ui.Calculations.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/calculations.js", () => Results.Content(Resource("IO.Web.Ui.calculations.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/conflicts", () => Results.Content(Resource("IO.Web.Ui.Conflicts.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/conflicts.js", () => Results.Content(Resource("IO.Web.Ui.conflicts.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/raw-events", () => Results.Content(Resource("IO.Web.Ui.RawEvents.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/raw-events.js", () => Results.Content(Resource("IO.Web.Ui.raw-events.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/mappings", () => Results.Content(Resource("IO.Web.Ui.Mappings.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/mappings.js", () => Results.Content(Resource("IO.Web.Ui.mappings.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/rules", () => Results.Content(Resource("IO.Web.Ui.Rules.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/rules.js", () => Results.Content(Resource("IO.Web.Ui.rules.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/assets/scheduling.js", () => Results.Content(Resource("IO.Web.Ui.scheduling.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/organization", () => Results.Content(Resource("IO.Web.Ui.Organization.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/organization.js", () => Results.Content(Resource("IO.Web.Ui.organization.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/workspace/employees", () => Results.Content(Resource("IO.Web.Ui.Employees.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/employees.js", () => Results.Content(Resource("IO.Web.Ui.employees.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/assets/employees.css", () => Results.Content(Resource("IO.Web.Ui.employees.css"), "text/css; charset=utf-8")).AllowAnonymous();
        app.MapGet("/assets/panel.js", () => Results.Content(Resource("IO.Web.Ui.panel.js"), "text/javascript; charset=utf-8")).AllowAnonymous();
        app.MapGet("/assets/access.js", () => Results.Content(Resource("IO.Web.Ui.access.js"), "text/javascript; charset=utf-8")).AllowAnonymous();
        app.MapGet("/assets/theme.js", () => Results.Content(Resource("IO.Web.Ui.theme.js"), "text/javascript; charset=utf-8")).AllowAnonymous();
        app.MapGet("/assets/theme.css", () => Results.Content(Resource("IO.Web.Ui.theme.css"), "text/css; charset=utf-8")).AllowAnonymous();
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
        app.MapGet("/auth/me", (ClaimsPrincipal user) => Results.Ok(new { id = user.FindFirstValue(ClaimTypes.NameIdentifier),
            mustChangePassword = user.HasClaim(IdentityServices.MustChangePasswordClaim, "1") }));
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
        app.MapGet("/schedules/own/{workDate}", async (DateOnly workDate, HttpContext context, ScheduleReader reader) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
            try
            {
                var day = await reader.FindOwnAsync(actor, workDate, DateOnly.FromDateTime(DateTime.UtcNow), context.RequestAborted);
                return day is null ? Results.NotFound() : Results.Ok(day);
            }
            catch (InvalidOperationException) { return Results.Conflict(); }
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
            catch (ArgumentOutOfRangeException) { return Results.BadRequest(); }
        });
        app.MapGet("/workspace/reversals", () => Results.Content(Resource("IO.Web.Ui.Reversals.html"), "text/html; charset=utf-8"));
        app.MapGet("/assets/reversals.js", () => Results.Content(Resource("IO.Web.Ui.reversals.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/corrections/reversals", (int? offset, HttpContext c, DecisionReversals service) =>
            AdminRead(c, actor => service.ListCorrectionsAsync(actor, offset ?? 0, c.RequestAborted)));
        app.MapGet("/leave/reversals", (int? offset, HttpContext c, DecisionReversals service) =>
            AdminRead(c, actor => service.ListLeavesAsync(actor, offset ?? 0, c.RequestAborted)));
        app.MapGet("/corrections/requests/{id:guid}/history", (Guid id, int? offset, HttpContext c, DecisionReversals service) =>
            AdminRead(c, actor => service.OwnCorrectionHistoryAsync(actor, id, offset ?? 0, c.RequestAborted)));
        app.MapGet("/leave/requests/{id:guid}/history", (Guid id, int? offset, HttpContext c, DecisionReversals service) =>
            AdminRead(c, actor => service.OwnLeaveHistoryAsync(actor, id, offset ?? 0, c.RequestAborted)));
        app.MapPost("/corrections/requests/{id:guid}/reverse", (Guid id, DecisionReversalInput input, HttpContext c, IAntiforgery csrf, DecisionReversals service) =>
            Admin(c, csrf, actor => service.ReverseCorrectionAsync(actor, id, input.ExpectedDecisionId, input.OperationId, input.Reason, c.RequestAborted)));
        app.MapPost("/leave/requests/{id:guid}/reverse", (Guid id, DecisionReversalInput input, HttpContext c, IAntiforgery csrf, DecisionReversals service) =>
            Admin(c, csrf, actor => service.ReverseLeaveAsync(actor, id, input.ExpectedDecisionId, input.OperationId, input.Reason, c.RequestAborted)));
        app.MapGet("/settings/correction/{companyId:guid}", (Guid companyId,int? offset,HttpContext c,CorrectionSettings service) =>
            AdminRead(c,actor => service.ReadAsync(actor,companyId,offset ?? 0,c.RequestAborted)));
        app.MapGet("/settings/correction-own", (HttpContext c,CorrectionSettings service) =>
            AdminRead(c,actor => service.ReadOwnAsync(actor,c.RequestAborted)));
        app.MapPost("/settings/correction", (CorrectionSettingsInput input,HttpContext c,IAntiforgery csrf,CorrectionSettings service) =>
            Admin(c,csrf,actor => service.SetAsync(actor,input.OperationId,input.CompanyId,input.ExpectedRevision ?? -1,input.MaxRequestAgeDays ?? -1,input.Reason,c.RequestAborted,input.DailyRequestLimit ?? -1,input.MonthlyRequestLimit ?? -1)));
        app.MapGet("/delegations/candidates", (int? offset,HttpContext c,DelegationRegistry service) =>
            AdminRead(c, actor => service.CandidatesAsync(actor,offset ?? 0,c.RequestAborted)));
        app.MapGet("/delegations/own", (int? offset,HttpContext c,DelegationRegistry service) =>
            AdminRead(c, actor => service.ListAsync(actor,offset ?? 0,c.RequestAborted)));
        app.MapPost("/delegations", (DelegationInput input,HttpContext c,IAntiforgery csrf,DelegationRegistry service) =>
            Admin(c,csrf,actor => service.AssignAsync(actor,input.OperationId,input.DelegateUserId,input.ValidFrom,input.ValidUntilExclusive,c.RequestAborted)));
        app.MapPost("/delegations/{id:guid}/revoke", (Guid id,DelegationRevokeInput input,HttpContext c,IAntiforgery csrf,DelegationRegistry service) =>
            Admin(c,csrf,actor => service.RevokeAsync(actor,id,input.Reason,c.RequestAborted)));
        app.MapGet("/device-file-imports", (HttpContext c,DeviceFileImports service,Guid? deviceId,int offset=0)=>AdminRead(c,actor=>service.ListAsync(actor,deviceId,offset,c.RequestAborted)));
        app.MapGet("/device-file-imports/{id:guid}/rows", (Guid id,HttpContext c,DeviceFileImports service,int after=0)=>AdminRead(c,actor=>service.RowsAsync(actor,id,after,c.RequestAborted)));
        app.MapGet("/device-file-imports/{id:guid}/original", (Guid id,HttpContext c,DeviceFileImports service)=>AdminRead(c,actor=>service.OriginalAsync(actor,id,c.RequestAborted)));
        app.MapPost("/device-file-imports", (DeviceFileImports.Input input,HttpContext c,IAntiforgery csrf,DeviceFileImports service)=>Admin(c,csrf,actor=>service.CreateAsync(actor,input,c.RequestAborted)));
        app.MapPost("/device-file-imports/{id:guid}/commit-page", (Guid id,DeviceFileCommit input,HttpContext c,IAntiforgery csrf,DeviceFileImports service)=>Admin(c,csrf,actor=>service.CommitPageAsync(actor,id,input.ExpectedVersion,input.After,input.Confirmation,c.RequestAborted)));
        app.MapGet("/device-user-uploads", (HttpContext c,DeviceUserUploads service,Guid? batchId,int offset=0)=>AdminRead(c,actor=>service.ListAsync(actor,batchId,offset,c.RequestAborted)));
        app.MapPost("/device-user-uploads/preview", (DeviceUserUploads.Batch input,HttpContext c,IAntiforgery csrf,DeviceUserUploads service)=>Admin(c,csrf,actor=>service.PreviewAsync(actor,input,c.RequestAborted)));
        app.MapPost("/device-user-uploads", (DeviceUserUploads.Batch input,HttpContext c,IAntiforgery csrf,DeviceUserUploads service)=>Admin(c,csrf,actor=>service.RequestAsync(actor,input,c.RequestAborted)));
        app.MapPost("/device-user-uploads/{id:guid}/cancel", (Guid id,DeviceOperationVersion input,HttpContext c,IAntiforgery csrf,DeviceUserUploads service)=>Admin(c,csrf,actor=>service.CancelAsync(actor,id,input.ExpectedVersion,c.RequestAborted)));
        app.MapPost("/device-user-uploads/{id:guid}/review", (Guid id,DeviceUserUploadReview input,HttpContext c,IAntiforgery csrf,DeviceUserUploads service)=>Admin(c,csrf,actor=>service.ReviewAsync(actor,id,input.ExpectedVersion,input.SnapshotId,input.Confirmation,c.RequestAborted)));
        app.MapGet("/device-commands", (HttpContext c,DeviceCommands service,Guid? deviceId,int offset=0)=>AdminRead(c,actor=>service.ListAsync(actor,deviceId,offset,c.RequestAborted)));
        app.MapPost("/device-commands/retries", (DeviceCommands.RetryInput input,HttpContext c,IAntiforgery csrf,DeviceCommands service)=>Admin(c,csrf,actor=>service.RetryAsync(actor,input,c.RequestAborted)));
        app.MapGet("/device-poll-schedules", (HttpContext c,DevicePollSchedules service,int offset=0)=>AdminRead(c,actor=>service.ListAsync(actor,offset,c.RequestAborted)));
        app.MapGet("/device-poll-schedules/{id:guid}/runs", (Guid id,HttpContext c,DevicePollSchedules service,int offset=0)=>AdminRead(c,actor=>service.RunsAsync(actor,id,offset,c.RequestAborted)));
        app.MapPost("/device-poll-schedules", (DevicePollInput input,HttpContext c,IAntiforgery csrf,DevicePollSchedules service)=>Admin(c,csrf,actor=>service.CreateAsync(actor,new(input.Id,input.DeviceId,input.ExpectedDeviceVersion,AttendanceUtc(input.StartUtc),input.EverySeconds,input.WindowSeconds,input.LagSeconds,input.TimeoutSeconds,input.InCode,input.OutCode,input.AutoCommit,input.Confirmation),c.RequestAborted)));
        app.MapPost("/device-poll-schedules/{id:guid}/enabled", (Guid id,DevicePollToggle input,HttpContext c,IAntiforgery csrf,DevicePollSchedules service)=>Admin(c,csrf,actor=>service.SetEnabledAsync(actor,id,input.ExpectedVersion,input.Enabled,input.Confirmation,c.RequestAborted)));
        app.MapGet("/device-attendance-downloads", (HttpContext c,DeviceAttendanceDownloads service,Guid? deviceId,int offset=0)=>AdminRead(c,actor=>service.ListAsync(actor,deviceId,offset,c.RequestAborted)));
        app.MapGet("/device-attendance-downloads/{id:guid}/rows", (Guid id,HttpContext c,DeviceAttendanceDownloads service,long after=0)=>AdminRead(c,actor=>service.RowsAsync(actor,id,after,c.RequestAborted)));
        app.MapPost("/device-attendance-downloads", (DeviceAttendanceInput input,HttpContext c,IAntiforgery csrf,DeviceAttendanceDownloads service)=>Admin(c,csrf,actor=>service.RequestAsync(actor,input.OperationId,input.DeviceId,input.ExpectedDeviceVersion,input.TimeoutSeconds,AttendanceUtc(input.FromUtc),AttendanceUtc(input.UntilUtcExclusive),input.InCode,input.OutCode,c.RequestAborted)));
        app.MapPost("/device-attendance-downloads/{id:guid}/cancel", (Guid id,DeviceOperationVersion input,HttpContext c,IAntiforgery csrf,DeviceAttendanceDownloads service)=>Admin(c,csrf,actor=>service.CancelAsync(actor,id,input.ExpectedVersion,c.RequestAborted)));
        app.MapPost("/device-attendance-downloads/{id:guid}/commit-page", (Guid id,DeviceAttendanceCommit input,HttpContext c,IAntiforgery csrf,DeviceAttendanceDownloads service)=>Admin(c,csrf,actor=>service.CommitPageAsync(actor,id,input.ExpectedVersion,input.After,input.Confirmation,c.RequestAborted)));
        app.MapGet("/device-user-downloads", (HttpContext c,DeviceUserDownloads service,Guid? deviceId,int offset=0)=>AdminRead(c,actor=>service.ListAsync(actor,deviceId,offset,c.RequestAborted)));
        app.MapGet("/device-user-downloads/{id:guid}/preview", (Guid id,HttpContext c,DeviceUserDownloads service,long after=0)=>AdminRead(c,actor=>service.PreviewAsync(actor,id,after,c.RequestAborted)));
        app.MapPost("/device-user-downloads", (DeviceConnectionInput input,HttpContext c,IAntiforgery csrf,DeviceUserDownloads service)=>Admin(c,csrf,actor=>service.RequestAsync(actor,input.OperationId,input.DeviceId,input.ExpectedDeviceVersion,input.TimeoutSeconds,c.RequestAborted)));
        app.MapPost("/device-user-downloads/{id:guid}/cancel", (Guid id,DeviceOperationVersion input,HttpContext c,IAntiforgery csrf,DeviceUserDownloads service)=>Admin(c,csrf,actor=>service.CancelAsync(actor,id,input.ExpectedVersion,c.RequestAborted)));
        app.MapGet("/device-settings-snapshots", (HttpContext c,DeviceOperations service,Guid? deviceId,int offset=0)=>AdminRead(c,actor=>service.ListSettingsAsync(actor,deviceId,offset,c.RequestAborted)));
        app.MapPost("/device-settings-snapshots", (DeviceSettingsInput input,HttpContext c,IAntiforgery csrf,DeviceOperations service)=>Admin(c,csrf,actor=>service.RequestAsync(actor,input.OperationId,input.DeviceId,input.ExpectedDeviceVersion,"settings.read",input.TimeoutSeconds,null,input.Confirmation,c.RequestAborted)));
        app.MapGet("/device-operations", (HttpContext c, DeviceOperations service, Guid? deviceId, int offset=0) =>
            AdminRead(c,actor=>service.ListAsync(actor,deviceId,offset,c.RequestAborted)));
        app.MapPost("/device-operations", (DeviceOperationInput input,HttpContext c,IAntiforgery csrf,DeviceOperations service) =>
            Admin(c,csrf,actor=>service.RequestAsync(actor,input.OperationId,input.DeviceId,input.ExpectedDeviceVersion,input.Kind,input.TimeoutSeconds,input.TargetUtc,input.Confirmation,c.RequestAborted)));
        app.MapPost("/device-operations/{id:guid}/cancel", (Guid id,DeviceOperationVersion input,HttpContext c,IAntiforgery csrf,DeviceOperations service) =>
            Admin(c,csrf,actor=>service.CancelAsync(actor,id,input.ExpectedVersion,c.RequestAborted)));
        app.MapPost("/device-operations/{id:guid}/resolve-clock", (Guid id,DeviceClockResolution input,HttpContext c,IAntiforgery csrf,DeviceOperations service) =>
            Admin(c,csrf,actor=>service.ResolveClockAsync(actor,id,input.ExpectedVersion,input.InformationId,input.Confirmation,c.RequestAborted)));
        app.MapGet("/device-connection", (HttpContext c, DeviceConnections service, Guid? deviceId, int offset = 0) =>
            AdminRead(c, actor => service.ListAsync(actor, deviceId, offset, c.RequestAborted)));
        app.MapPost("/device-connection", (DeviceConnectionInput input, HttpContext c, IAntiforgery csrf, DeviceConnections service) =>
            Admin(c, csrf, actor => service.RequestAsync(actor, input.OperationId, input.DeviceId, input.ExpectedDeviceVersion, input.TimeoutSeconds, c.RequestAborted)));
        app.MapPost("/device-connection/{id:guid}/cancel", (Guid id, DeviceConnectionCancel input, HttpContext c, IAntiforgery csrf, DeviceConnections service) =>
            Admin(c, csrf, actor => service.CancelAsync(actor, id, input.ExpectedVersion, c.RequestAborted)));
        app.MapGet("/device-groups", (HttpContext c, DeviceGroups service, Guid? companyId, Guid? parentId, int offset = 0) =>
            AdminRead(c, actor => service.ListAsync(actor, companyId, parentId, offset, c.RequestAborted)));
        app.MapPost("/device-groups", (DeviceGroupCreateInput input, HttpContext c, IAntiforgery csrf, DeviceGroups service) =>
            Admin(c, csrf, actor => service.CreateAsync(actor, input.OperationId, input.Group, c.RequestAborted)));
        app.MapPost("/device-groups/{id:guid}", (Guid id, DeviceGroupUpdateInput input, HttpContext c, IAntiforgery csrf, DeviceGroups service) =>
            Admin(c, csrf, actor => service.UpdateAsync(actor, id, input.ExpectedVersion, input.Group, c.RequestAborted)));
        app.MapPost("/device-groups/{id:guid}/delete", (Guid id, DeviceGroupDeleteInput input, HttpContext c, IAntiforgery csrf, DeviceGroups service) =>
            Admin(c, csrf, actor => service.DeleteAsync(actor, id, input.ExpectedVersion, c.RequestAborted)));
        app.MapPost("/devices/{id:guid}/group", (Guid id, DeviceGroupAssignInput input, HttpContext c, IAntiforgery csrf, DeviceGroups service) =>
            Admin(c, csrf, actor => service.AssignAsync(actor, id, input.ExpectedVersion, input.GroupId, c.RequestAborted)));
        app.MapGet("/devices", (HttpContext c, DeviceRegistry service, Guid? companyId, int offset = 0) =>
            AdminRead(c, actor => service.ListAsync(actor, companyId, offset, c.RequestAborted)));
        app.MapGet("/devices/locations", (HttpContext c, DeviceRegistry service, int offset = 0) =>
            AdminRead(c, actor => service.LocationsAsync(actor, offset, c.RequestAborted)));
        app.MapGet("/devices/{id:guid}", (Guid id, HttpContext c, DeviceRegistry service) =>
            AdminRead(c, actor => service.GetAsync(actor, id, c.RequestAborted)));
        app.MapGet("/devices/{id:guid}/capabilities", (Guid id, HttpContext c, DeviceRegistry service) =>
            AdminRead(c, actor => service.CapabilitiesAsync(actor, id, c.RequestAborted)));
        app.MapPost("/devices", (DeviceCreateInput input, HttpContext c, IAntiforgery csrf, DeviceRegistry service) =>
            Admin(c, csrf, actor => service.CreateAsync(actor, input.OperationId, input.Device, c.RequestAborted)));
        app.MapPost("/devices/{id:guid}", (Guid id, DeviceUpdateInput input, HttpContext c, IAntiforgery csrf, DeviceRegistry service) =>
            Admin(c, csrf, actor => service.UpdateAsync(actor, id, input.ExpectedVersion, input.Device, c.RequestAborted)));
        app.MapPost("/devices/{id:guid}/credential", (Guid id, DeviceCredentialInput input, HttpContext c, IAntiforgery csrf, DeviceRegistry service) =>
            Admin(c, csrf, actor => service.SetCredentialAsync(actor, id, input.ExpectedVersion, input.Secret, c.RequestAborted)));

        app.MapPost("/leave/types", (LeaveTypeInput input, HttpContext c, IAntiforgery csrf, LeaveLedger service) =>
            Admin(c, csrf, actor => service.CreateTypeAsync(actor,input.CompanyId,input.Name,input.Symbol,input.MinutesPerDay,input.CarryOverAllowed,c.RequestAborted)));
        app.MapGet("/leave/types/{companyId:guid}", (Guid companyId, int? offset, HttpContext c, LeaveLedger service) =>
            AdminRead(c, actor => service.ListTypesAsync(actor,companyId,offset ?? 0,c.RequestAborted)));
        app.MapGet("/leave/own-types", (int? offset, HttpContext c, LeaveLedger service) =>
            AdminRead(c, actor => service.ListOwnTypesAsync(actor,offset ?? 0,c.RequestAborted)));
        app.MapPost("/leave/entries", (LeaveEntryInput input, HttpContext c, IAntiforgery csrf, LeaveLedger service) =>
            Admin(c, csrf, actor => service.AddEntryAsync(actor,input.OperationId,input.EmployeeId,input.LeaveTypeId,input.EffectiveDate,input.Kind,input.Minutes,input.DocumentNumber,input.Description,c.RequestAborted)));
        app.MapGet("/leave/entries/{employeeId:guid}/{leaveTypeId:guid}", (Guid employeeId, Guid leaveTypeId, DateOnly asOf, int? offset, HttpContext c, LeaveLedger service) =>
            AdminRead(c, actor => service.ListEntriesAsync(actor,employeeId,leaveTypeId,asOf,offset ?? 0,c.RequestAborted)));
        app.MapGet("/leave/balance/{employeeId:guid}/{leaveTypeId:guid}", (Guid employeeId, Guid leaveTypeId, DateOnly asOf, HttpContext c, LeaveLedger service) =>
            AdminRead(c, async actor => await service.GetBalanceAsync(actor,employeeId,leaveTypeId,asOf,c.RequestAborted)));
        app.MapGet("/leave/own-balance/{leaveTypeId:guid}", (Guid leaveTypeId, DateOnly asOf, HttpContext c, LeaveLedger service) =>
            AdminRead(c, async actor => await service.GetOwnBalanceAsync(actor,leaveTypeId,asOf,c.RequestAborted)));
        app.MapPost("/leave/requests", (LeaveRequestInput input, HttpContext c, IAntiforgery csrf, LeaveRequests service) =>
            Admin(c, csrf, actor => service.SubmitAsync(actor, input.OperationId, input.LeaveTypeId, input.Category, input.Kind,
                input.WorkDate, input.FromAtUtc is null ? null : ParseExplicitTime(input.FromAtUtc),
                input.UntilAtUtcExclusive is null ? null : ParseExplicitTime(input.UntilAtUtcExclusive), input.Reason, c.RequestAborted)));
        app.MapGet("/leave/requests", (int? offset, HttpContext c, LeaveRequests service) =>
            AdminRead(c, async actor => await service.ListOwnAsync(actor, offset ?? 0, c.RequestAborted)));
        app.MapPost("/leave/requests/{id:guid}/cancel", (Guid id, LeaveCancelInput input, HttpContext c, IAntiforgery csrf, LeaveRequests service) =>
            Admin(c, csrf, actor => service.CancelOwnAsync(actor, id, input.Reason, c.RequestAborted)));
        app.MapGet("/leave/inbox", (int? offset, HttpContext c, LeaveRequests service) =>
            AdminRead(c, async actor => await service.ListInboxAsync(actor, offset ?? 0, c.RequestAborted)));
        app.MapPost("/leave/requests/{id:guid}/decision", (Guid id, LeaveDecisionInput input, HttpContext c, IAntiforgery csrf, LeaveRequests service) =>
            Admin(c, csrf, actor => service.DecideAsync(actor, id, input.Decision, input.Reason, c.RequestAborted)));
        app.MapGet("/leave/balances/{companyId:guid}", (Guid companyId, DateOnly asOf, int? offset, HttpContext c, LeaveLedger service) =>
            AdminRead(c, async actor => await service.ListBalancesAsync(actor, companyId, asOf, offset ?? 0, c.RequestAborted)));
        app.MapGet("/notifications", (int? offset, HttpContext c, Notifications service) =>
            AdminRead(c, async actor => await service.ListAsync(actor, offset ?? 0, c.RequestAborted)));
        app.MapPost("/notifications/read-all", (HttpContext c, IAntiforgery csrf, Notifications service) =>
            Admin(c, csrf, actor => service.MarkAllReadAsync(actor, c.RequestAborted)));
        app.MapGet("/executive/companies", (HttpContext c, ExecutiveOverview service) =>
            AdminRead(c, async actor => (object)await service.CompaniesAsync(actor, c.RequestAborted)));
        app.MapGet("/executive/overview/{companyId:guid}", (Guid companyId, string fromUtc, string untilUtcExclusive, HttpContext c, ExecutiveOverview service) =>
            AdminRead(c, async actor => await service.InspectAsync(actor, companyId, ParseExplicitTime(fromUtc), ParseExplicitTime(untilUtcExclusive), c.RequestAborted)));
        app.MapGet("/calculations/own-report", (DateOnly start, DateOnly endExclusive, HttpContext c, PersonalReports service) =>
            AdminRead(c, async actor => await service.InspectAsync(actor, start, endExclusive, c.RequestAborted)));
        app.MapGet("/attendance/team", (string fromUtc, string untilUtcExclusive, int? offset, HttpContext c, TeamAttendance service) =>
            AdminRead(c, async actor => await service.ListAsync(actor, DateOnly.FromDateTime(DateTime.UtcNow), ParseExplicitTime(fromUtc), ParseExplicitTime(untilUtcExclusive), offset ?? 0, c.RequestAborted)));
        app.MapGet("/attendance/own", (string fromUtc, string untilUtcExclusive, int? offset, HttpContext c, PersonalAttendance service) =>
            AdminRead(c, async actor => await service.ListAsync(actor, ParseExplicitTime(fromUtc), ParseExplicitTime(untilUtcExclusive), offset ?? 0, c.RequestAborted)));
        app.MapGet("/attendance/raw/{sourceId:guid}", (Guid sourceId, int? offset, HttpContext c, AttendanceIngestion service) =>
            AdminRead(c, actor => service.ListAsync(actor, sourceId, offset ?? 0, c.RequestAborted)));
        app.MapGet("/attendance/mapping-changes/{sourceId:guid}", (Guid sourceId, int? offset, HttpContext c, AttendanceIngestion service) =>
            AdminRead(c, actor => service.ListMappingChangesAsync(actor, sourceId, offset ?? 0, c.RequestAborted)));
        app.MapPost("/attendance/mapping-replacements", (SourceMappingReplacementInput input, HttpContext c, IAntiforgery csrf, AttendanceIngestion service) =>
            Admin(c, csrf, actor => service.ReplaceMappingAsync(actor, input.SourceId, input.SourcePersonId, input.MappingId, input.EmployeeId,
                ParseExplicitTime(input.EffectiveAt).ToUniversalTime(), ParseVersion(input.ExpectedVersion), input.Reason, c.RequestAborted)));
        app.MapGet("/attendance/mappings/{sourceId:guid}", (Guid sourceId, int? offset, HttpContext c, AttendanceIngestion service) =>
            AdminRead(c, actor => service.ListMappingsAsync(actor, sourceId, offset ?? 0, c.RequestAborted)));
        app.MapGet("/attendance/resolution/{sourceId:guid}", (Guid sourceId, int? offset, HttpContext c, AttendanceIngestion service) =>
            AdminRead(c, actor => service.ListResolutionAsync(actor, sourceId, offset ?? 0, c.RequestAborted)));
        app.MapGet("/attendance/conflicts/{sourceId:guid}", (Guid sourceId, int? offset, HttpContext c, AttendanceIngestion service) =>
            AdminRead(c, actor => service.ListConflictsAsync(actor, sourceId, offset ?? 0, c.RequestAborted)));
        app.MapGet("/attendance/conflict/{id:guid}", (Guid id, HttpContext c, AttendanceIngestion service) =>
            AdminRead(c, actor => service.GetConflictAsync(actor, id, c.RequestAborted)));
        app.MapGet("/attendance/conflict/{id:guid}/notes", (Guid id, int? offset, HttpContext c, AttendanceIngestion service) =>
            AdminRead(c, actor => service.ListConflictNotesAsync(actor, id, offset ?? 0, c.RequestAborted)));
        app.MapPost("/attendance/conflict/{id:guid}/notes", (Guid id, ConflictNoteInput input, HttpContext c, IAntiforgery csrf, AttendanceIngestion service) =>
            Admin(c, csrf, actor => service.AddConflictNoteAsync(actor, id, input.Note, c.RequestAborted)));
        app.MapPost("/attendance/mappings", (SourceMappingInput input, HttpContext c, IAntiforgery csrf, AttendanceIngestion service) =>
            Admin(c, csrf, actor => service.AddMappingAsync(actor, input.SourceId, input.SourcePersonId, input.EmployeeId,
                ParseExplicitTime(input.ValidFrom).ToUniversalTime(), input.ValidUntilExclusive is null ? null : ParseExplicitTime(input.ValidUntilExclusive).ToUniversalTime(), c.RequestAborted)));
        app.MapGet("/attendance/sources", (int? offset, HttpContext c, AttendanceIngestion service) =>
            AdminRead(c, actor => service.ListSourcesAsync(actor, offset ?? 0, c.RequestAborted)));
        app.MapPost("/attendance/sources", (AttendanceSourceInput input, HttpContext c, IAntiforgery csrf, AttendanceIngestion service) =>
            Admin(c, csrf, actor => service.CreateSourceAsync(actor, input.SiteId, input.Name, c.RequestAborted)));
        app.MapPost("/attendance/simulated-events", (SimulatedEventInput input, HttpContext c, IAntiforgery csrf, AttendanceIngestion service) =>
            ReceiveEvent(c, csrf, service, input));
        app.MapGet("/calculations/revision/{id:guid}", (Guid id, HttpContext c, ObservedCalculation service) =>
            AdminRead(c, actor => service.AssessDatabaseChangesAsync(actor, id, c.RequestAborted)));
        app.MapGet("/calculations/compare/{firstId:guid}/{secondId:guid}", (Guid firstId, Guid secondId, HttpContext c, ObservedCalculation service) =>
            AdminRead(c, actor => service.CompareAsync(actor, firstId, secondId, c.RequestAborted)));
        app.MapGet("/calculations/rules/{companyId:guid}", (Guid companyId, int? offset, HttpContext c, ObservedCalculation service) =>
            AdminRead(c, actor => service.ListRulesAsync(actor, companyId, offset ?? 0, c.RequestAborted)));
        app.MapGet("/calculations/runs/{employeeId:guid}", (Guid employeeId, int? offset, HttpContext c, ObservedCalculation service) =>
            AdminRead(c, actor => service.ListRunsAsync(actor, employeeId, offset ?? 0, c.RequestAborted)));
        app.MapPost("/calculations/rules", (CalculationRuleInput input, HttpContext c, IAntiforgery csrf, ObservedCalculation service) =>
            Admin(c, csrf, actor => service.CreateRuleAsync(actor, input.CompanyId, input.Name, input.BeforeMinutes, input.AfterMinutes, c.RequestAborted)));
        app.MapPost("/calculations/runs", (CalculationRunInput input, HttpContext c, IAntiforgery csrf, ObservedCalculation service) =>
            Admin(c, csrf, actor => service.CalculateAsync(actor, input.EmployeeId, input.WorkDate, input.SourceId, input.RuleVersionId, c.RequestAborted)));
        app.MapGet("/corrections/configured-policies/{companyId:guid}", (Guid companyId, int? offset, HttpContext c, CorrectionWorkflow service) =>
            AdminRead(c, actor => service.ListConfiguredPoliciesAsync(actor, companyId, offset ?? 0, c.RequestAborted)));
        app.MapGet("/corrections/policies", (int? offset, HttpContext c, CorrectionWorkflow service) =>
            AdminRead(c, actor => service.ListPoliciesAsync(actor, offset ?? 0, c.RequestAborted)));
        app.MapGet("/corrections/own", (int? offset, DateOnly? fromDate, DateOnly? toDate, int? state, HttpContext c, CorrectionWorkflow service) =>
            AdminRead(c, actor => service.ListOwnAsync(actor, offset ?? 0, c.RequestAborted, fromDate, toDate, state)));
        app.MapGet("/corrections/decisions", (int? offset, DateOnly? fromDate, DateOnly? toDate, HttpContext c, CorrectionWorkflow service) =>
            AdminRead(c, actor => service.ListDecisionsAsync(actor, offset ?? 0, c.RequestAborted, fromDate, toDate)));
        app.MapGet("/corrections/inbox/employees", (int? offset, HttpContext c, CorrectionWorkflow service) =>
            AdminRead(c, actor => service.InboxEmployeesAsync(actor, offset ?? 0, c.RequestAborted)));
        app.MapGet("/corrections/inbox", (int? offset, Guid? employeeId, string? sort, DateOnly? fromDate, DateOnly? toDate, HttpContext c, CorrectionWorkflow service) =>
            AdminRead(c, actor => service.InboxAsync(actor, offset ?? 0, c.RequestAborted, employeeId, sort, fromDate, toDate)));
        app.MapPost("/corrections/policies", (CorrectionPolicyInput input, HttpContext c, IAntiforgery csrf, CorrectionWorkflow service) =>
            Admin(c, csrf, actor => service.CreatePolicyAsync(actor, input.CompanyId, input.ApproverUserId, input.Name, c.RequestAborted)));
        app.MapPost("/corrections/requests", (CorrectionRequestInput input, HttpContext c, IAntiforgery csrf, CorrectionWorkflow service) =>
            Admin(c, csrf, actor => service.SubmitAsync(actor, input.PolicyId, input.WorkDate, ParseExplicitTime(input.ProposedAt).ToUniversalTime(), input.PunchKind, input.Reason, c.RequestAborted, input.OperationId)));
        app.MapPost("/corrections/delegation-grants", (CorrectionDelegationGrantInput input, HttpContext c, IAntiforgery csrf, CorrectionWorkflow service) =>
            Admin(c, csrf, actor => service.GrantCorrectionDelegationAsync(actor, input.DelegationId, c.RequestAborted)));
        app.MapPost("/corrections/requests/{id:guid}/decision", (Guid id, CorrectionDecisionInput input, HttpContext c, IAntiforgery csrf, CorrectionWorkflow service) =>
            Admin(c, csrf, actor => service.DecideAsync(actor, id, input.Approve, input.Reason, c.RequestAborted)));
        app.MapPost("/corrections/requests/{id:guid}/edit", (Guid id, CorrectionEditInput input, HttpContext c, IAntiforgery csrf, CorrectionWorkflow service) =>
            Admin(c, csrf, actor => service.EditAsync(actor, id, ParseExplicitTime(input.ProposedAt).ToUniversalTime(), input.PunchKind, input.Reason, c.RequestAborted)));
        app.MapPost("/corrections/requests/{id:guid}/cancel", (Guid id, CorrectionCancelInput input, HttpContext c, IAntiforgery csrf, CorrectionWorkflow service) =>
            Admin(c, csrf, actor => service.CancelAsync(actor, id, input.Reason, c.RequestAborted)));
        app.MapPost("/periods/drafts/{draftId:guid}/diagnostics", (Guid draftId, HttpContext c, IAntiforgery csrf, PeriodDiagnostics service) =>
            Admin(c, csrf, actor => service.RecordAsync(actor, draftId, c.RequestAborted)));
        app.MapGet("/periods/drafts/{draftId:guid}/diagnostics", (Guid draftId, int? offset, HttpContext c, PeriodDiagnostics service) =>
            AdminRead(c, actor => service.ListAsync(actor, draftId, offset ?? 0, c.RequestAborted)));
        app.MapGet("/periods/diagnostics/{id:guid}", (Guid id, HttpContext c, PeriodDiagnostics service) =>
            AdminRead(c, actor => service.AssessAsync(actor, id, c.RequestAborted)));
        app.MapGet("/periods/drafts/{employeeId:guid}", (Guid employeeId, int? offset, HttpContext c, PeriodDrafts service) =>
            AdminRead(c, actor => service.ListAsync(actor, employeeId, offset ?? 0, c.RequestAborted)));
        app.MapPost("/periods/drafts", (PeriodDraftInput input, HttpContext c, IAntiforgery csrf, PeriodDrafts service) =>
            Admin(c, csrf, actor => service.CreateAsync(actor, input.EmployeeId, input.Start, input.EndExclusive, c.RequestAborted)));
        app.MapGet("/periods/preflight/{employeeId:guid}", (Guid employeeId, DateOnly start, DateOnly endExclusive, HttpContext c, PeriodPreflight service) =>
            AdminRead(c, actor => service.InspectAsync(actor, employeeId, start, endExclusive, c.RequestAborted)));
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
        app.MapPost("/org/employees/{id:guid}/rename", (Guid id, EmployeeRenameInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.RenameEmployeeAsync(actor, id, input.DisplayName, ParseVersion(input.ExpectedVersion), c.RequestAborted)));
        app.MapPost("/org/units/{id:guid}/reparent", (Guid id, ReparentUnitInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.ReparentUnitAsync(actor, input.CompanyId, id, input.ParentId, ParseVersion(input.ExpectedVersion), c.RequestAborted)));
        app.MapPost("/org/assignments", (AssignmentInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.AddAssignmentAsync(actor, input.EmployeeId, input.UnitId, input.ValidFrom, input.ValidUntilExclusive, c.RequestAborted)));
        app.MapPost("/org/site-assignments", (EmployeeSiteInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.AddSiteAssignmentAsync(actor, input.EmployeeId, input.SiteId, input.ValidFrom, input.ValidUntilExclusive, c.RequestAborted)));
        app.MapPost("/org/site-transfers", (EmployeeSiteTransferInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.MoveSiteAsync(actor, input.EmployeeId, input.AssignmentId, input.SiteId, input.Effective, c.RequestAborted)));
        app.MapPost("/org/employment-profiles", (EmploymentInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.AddEmploymentProfileAsync(actor, input.EmployeeId, input.StatusCode, input.Title, input.ValidFrom, input.ValidUntilExclusive, c.RequestAborted)));
        app.MapPost("/org/employment-changes", (EmploymentChangeInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.ChangeEmploymentProfileAsync(actor, input.EmployeeId, input.ProfileId, input.StatusCode, input.Title, input.Effective, c.RequestAborted)));
        app.MapPost("/org/transfers", (TransferInput input, HttpContext c, IAntiforgery csrf, OrganizationManagement service) =>
            Admin(c, csrf, actor => service.TransferAsync(actor, input.EmployeeId, input.AssignmentId, input.UnitId, input.Effective, c.RequestAborted)));
        app.MapPost("/scheduling/calendars", (CalendarInput input, HttpContext c, IAntiforgery csrf, ScheduleManagement service) =>
            Admin(c, csrf, actor => service.CreateCalendarAsync(actor, input.CompanyId, input.Name, c.RequestAborted)));
        app.MapPost("/scheduling/calendars/{id:guid}/fork", (Guid id, CalendarForkInput input, HttpContext c, IAntiforgery csrf, ScheduleManagement service) =>
            Admin(c, csrf, actor => service.ForkCalendarAsync(actor, id, input.Name, c.RequestAborted)));
        app.MapPost("/scheduling/calendar-days/revise", (CalendarDayInput input, HttpContext c, IAntiforgery csrf, ScheduleManagement service) =>
            Admin(c, csrf, actor => service.ReviseCalendarDayAsync(actor, input.CalendarId, input.Date, input.Kind, input.Note, c.RequestAborted)));
        app.MapPost("/scheduling/calendar-days", (CalendarDayInput input, HttpContext c, IAntiforgery csrf, ScheduleManagement service) =>
            Admin(c, csrf, actor => service.AddCalendarDayAsync(actor, input.CalendarId, input.Date, input.Kind, input.Note, c.RequestAborted)));
        app.MapPost("/scheduling/shifts", (ShiftInput input, HttpContext c, IAntiforgery csrf, ScheduleManagement service) =>
            Admin(c, csrf, actor => service.CreateShiftAsync(actor, input.CompanyId, input.SiteId, input.Name, input.StartsAt, input.EndsAt, input.EndDayOffset, c.RequestAborted)));
        app.MapPost("/scheduling/assignments", (ScheduleInput input, HttpContext c, IAntiforgery csrf, ScheduleManagement service) =>
            Admin(c, csrf, actor => service.AssignAsync(actor, input.EmployeeId, input.CalendarId, input.ShiftId, input.ValidFrom, input.ValidUntilExclusive, c.RequestAborted)));
        app.MapPost("/scheduling/replacements", (ScheduleReplacementInput input, HttpContext c, IAntiforgery csrf, ScheduleManagement service) =>
            Admin(c, csrf, actor => service.RescheduleAsync(actor, input.EmployeeId, input.AssignmentId, input.CalendarId, input.ShiftId, input.Effective, c.RequestAborted)));
        app.MapGet("/admin/catalog/{kind}", async (string kind, int? offset, string? q, HttpContext context, IdentityAdministration admin) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
            try { return Results.Ok(await admin.ListAsync(actor, kind, offset ?? 0, q, context.RequestAborted)); }
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
        app.MapGet("/admin/users/{id:guid}", (Guid id, HttpContext context, IdentityAdministration admin) =>
            AdminRead(context, actor => admin.GetUserAsync(actor, id, context.RequestAborted)));
        app.MapPost("/admin/users/{id:guid}/password", (Guid id, ResetPasswordInput input, HttpContext context, IAntiforgery csrf, IdentityAdministration admin) =>
            Admin(context, csrf, actor => admin.ResetPasswordAsync(actor, id, input.NewPassword, context.RequestAborted)));
        return app;
    }

    private static string Resource(string name)
    {
        using var stream = typeof(IoWebHost).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Embedded UI resource missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] ParseVersion(string value)
    {
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new ArgumentException("Invalid rowversion."); }
        catch (ArgumentNullException) { throw new ArgumentException("Missing rowversion."); }
    }

    private static DateTimeOffset ParseExplicitTime(string value)
    {
        if (value is null || !Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant) ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ArgumentException("Timestamp must have an explicit offset.");
        return parsed;
    }

    private static async Task<IResult> ReceiveEvent(HttpContext context, IAntiforgery antiforgery, AttendanceIngestion service, SimulatedEventInput input)
    {
        if (!await ValidCsrf(context, antiforgery)) return Results.BadRequest();
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
        try
        {
            DateTimeOffset? occurred = input.OccurredAt is null ? null : ParseExplicitTime(input.OccurredAt);
            var result = await service.AppendAsync(actor, input.SourceId, input.ExternalId, input.PersonId,
                occurred, input.SourceZone, input.EventType, input.Payload, context.RequestAborted);
            return result.Outcome == RawEventAppendResult.ConflictingExternalIdentity
                ? Results.Conflict(new { result }) : Results.Ok(new { result });
        }
        catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
        catch (DomainConflictException) { return Results.Conflict(); }
        catch (ArgumentException) { return Results.BadRequest(); }
    }

    private static DateTimeOffset AttendanceUtc(string? value)
    {
        if(value is null||value.Length!=20||!DateTimeOffset.TryParseExact(value,"yyyy-MM-dd'T'HH:mm:ss'Z'",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal|System.Globalization.DateTimeStyles.AdjustToUniversal,out var result))throw new ArgumentException("Explicit UTC whole seconds ending in Z required.");return result;
    }

    private static async Task<IResult> AdminRead(HttpContext context, Func<Guid, Task<object>> action)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
        try { return Results.Ok(await action(actor)); }
        catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
        catch (DomainConflictException) { return Results.Conflict(); }
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
public sealed record ResetPasswordInput(string NewPassword);
public sealed record LeaveRequestInput(Guid OperationId, Guid LeaveTypeId, int Category, int Kind, DateOnly WorkDate, string? FromAtUtc, string? UntilAtUtcExclusive, string Reason);
public sealed record LeaveDecisionInput(int Decision, string? Reason);
public sealed record LeaveCancelInput(string? Reason);
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
public sealed record EmployeeRenameInput(string DisplayName, string ExpectedVersion);
public sealed record ReparentUnitInput(Guid CompanyId, Guid? ParentId, string ExpectedVersion);
public sealed record CalendarForkInput(string Name);
public sealed record EmployeeSiteInput(Guid EmployeeId, Guid SiteId, DateOnly ValidFrom, DateOnly? ValidUntilExclusive);
public sealed record EmployeeSiteTransferInput(Guid EmployeeId, Guid AssignmentId, Guid SiteId, DateOnly Effective);
public sealed record EmploymentInput(Guid EmployeeId, string StatusCode, string? Title, DateOnly ValidFrom, DateOnly? ValidUntilExclusive);
public sealed record EmploymentChangeInput(Guid EmployeeId, Guid ProfileId, string StatusCode, string? Title, DateOnly Effective);

public sealed record AttendanceSourceInput(Guid? SiteId, string Name);
public sealed record SimulatedEventInput(Guid SourceId, string? ExternalId, string? PersonId, string? OccurredAt, string? SourceZone, string? EventType, byte[] Payload);

public sealed record SourceMappingInput(Guid SourceId, string SourcePersonId, Guid EmployeeId, string ValidFrom, string? ValidUntilExclusive);

public sealed record CalculationRuleInput(Guid CompanyId, string Name, int BeforeMinutes, int AfterMinutes);
public sealed record CalculationRunInput(Guid EmployeeId, DateOnly WorkDate, Guid SourceId, Guid RuleVersionId);

public sealed record CorrectionPolicyInput(Guid CompanyId, Guid ApproverUserId, string Name);
public sealed record CorrectionRequestInput(Guid PolicyId, DateOnly WorkDate, string ProposedAt, int PunchKind, string Reason, Guid OperationId);
public sealed record CorrectionDecisionInput(bool Approve, string? Reason);
public sealed record CorrectionCancelInput(string Reason);
public sealed record CorrectionEditInput(string ProposedAt, int PunchKind, string Reason);
public sealed record CorrectionDelegationGrantInput(Guid DelegationId);

public sealed record PeriodDraftInput(Guid EmployeeId, DateOnly Start, DateOnly EndExclusive);

public sealed record ConflictNoteInput(string Note);

public sealed record SourceMappingReplacementInput(Guid SourceId, string SourcePersonId, Guid MappingId, Guid EmployeeId, string EffectiveAt, string ExpectedVersion, string Reason);

public sealed record LeaveTypeInput(Guid CompanyId,string Name,string Symbol,int MinutesPerDay,bool CarryOverAllowed);
public sealed record LeaveEntryInput(Guid OperationId,Guid EmployeeId,Guid LeaveTypeId,DateOnly EffectiveDate,int Kind,int Minutes,string DocumentNumber,string Description);

public sealed record DelegationInput(Guid OperationId,Guid DelegateUserId,DateOnly ValidFrom,DateOnly ValidUntilExclusive);
public sealed record DelegationRevokeInput(string Reason);

public sealed record CorrectionSettingsInput(Guid OperationId,Guid CompanyId,int? ExpectedRevision,int? MaxRequestAgeDays,string Reason,int? DailyRequestLimit,int? MonthlyRequestLimit);

public sealed record DecisionReversalInput(Guid OperationId, Guid ExpectedDecisionId, string Reason);

public sealed record DeviceCreateInput(Guid OperationId, DeviceRegistry.Input Device);
public sealed record DeviceUpdateInput(byte[] ExpectedVersion, DeviceRegistry.Input Device);
public sealed record DeviceCredentialInput(byte[] ExpectedVersion, [property: System.Text.Json.Serialization.JsonRequired] string? Secret);

public sealed record DeviceGroupCreateInput(Guid OperationId, DeviceGroups.Input Group);
public sealed record DeviceGroupUpdateInput(byte[] ExpectedVersion, DeviceGroups.Input Group);
public sealed record DeviceGroupDeleteInput(byte[] ExpectedVersion);
public sealed record DeviceGroupAssignInput(byte[] ExpectedVersion, [property: System.Text.Json.Serialization.JsonRequired] Guid? GroupId);

public sealed record DeviceConnectionInput(Guid OperationId, Guid DeviceId, byte[] ExpectedDeviceVersion, int TimeoutSeconds);
public sealed record DeviceConnectionCancel(byte[] ExpectedVersion);

public sealed record DeviceOperationInput(Guid OperationId,Guid DeviceId,byte[] ExpectedDeviceVersion,string Kind,int TimeoutSeconds,DateTimeOffset? TargetUtc,string? Confirmation);
public sealed record DeviceOperationVersion(byte[] ExpectedVersion);
public sealed record DeviceClockResolution(byte[] ExpectedVersion,Guid InformationId,string Confirmation);

public sealed record DeviceFileCommit(byte[] ExpectedVersion,int After,string Confirmation);

public sealed record DeviceUserUploadReview(byte[] ExpectedVersion,Guid SnapshotId,string Confirmation);

public sealed record DeviceAttendanceInput(Guid OperationId,Guid DeviceId,byte[] ExpectedDeviceVersion,int TimeoutSeconds,[property:System.Text.Json.Serialization.JsonRequired]string FromUtc,[property:System.Text.Json.Serialization.JsonRequired]string UntilUtcExclusive,[property:System.Text.Json.Serialization.JsonRequired]int InCode,[property:System.Text.Json.Serialization.JsonRequired]int OutCode);
public sealed record DeviceAttendanceCommit(byte[] ExpectedVersion,long After,string Confirmation);

public sealed record DevicePollInput(Guid Id,Guid DeviceId,byte[] ExpectedDeviceVersion,[property:System.Text.Json.Serialization.JsonRequired]string StartUtc,int EverySeconds,int WindowSeconds,int LagSeconds,int TimeoutSeconds,[property:System.Text.Json.Serialization.JsonRequired]int InCode,[property:System.Text.Json.Serialization.JsonRequired]int OutCode,[property:System.Text.Json.Serialization.JsonRequired]bool AutoCommit,string Confirmation);
public sealed record DevicePollToggle(byte[] ExpectedVersion,[property:System.Text.Json.Serialization.JsonRequired]bool Enabled,string Confirmation);

public sealed record DeviceSettingsInput(Guid OperationId,Guid DeviceId,byte[] ExpectedDeviceVersion,int TimeoutSeconds,string Confirmation);
