using System.Net;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class HttpSecurityTests
{
    private const string Password = "Synthetic!Password-2026";
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIoIdentityStorage(connection);
        await using var provider = services.BuildServiceProvider();
        var self = Guid.NewGuid(); var manager = Guid.NewGuid(); var locked = Guid.NewGuid();
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var unit = Guid.NewGuid(); var otherUnit = Guid.NewGuid();
        string adminName;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var ids = scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<LoginAccount>>();
            var admin = await ids.Users.SingleAsync();
            adminName = admin.UserName!;
            await users.SetLockoutEndDateAsync(admin, DateTimeOffset.UtcNow.AddMinutes(-1));
            await users.ResetAccessFailedCountAsync(admin);
            foreach (var (id, name) in new[] { (self, "web-self"), (manager, "web-manager"), (locked, "web-locked") })
            {
                var result = await users.CreateAsync(new LoginAccount { Id = id, UserName = name, LockoutEnabled = true }, Password);
                if (!result.Succeeded) throw new InvalidOperationException("Failed to create synthetic web test account.");
            }
            var org = Guid.NewGuid(); var company = Guid.NewGuid();
            var selfRole = Guid.NewGuid(); var managerRole = Guid.NewGuid();
            var selfPermission = Guid.NewGuid(); var managerPermission = Guid.NewGuid();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            db.Organizations.Add(new() { Id = org, Name = "web test" });
            db.Companies.Add(new() { Id = company, OrganizationId = org, Name = "web company" });
            db.OrgUnits.AddRange(new OrgUnitRecord { Id = unit, CompanyId = company, Name = "allowed" },
                new OrgUnitRecord { Id = otherUnit, CompanyId = company, Name = "other" });
            db.Employees.AddRange(new EmployeeRecord { Id = first, CompanyId = company, PersonnelNumber = "WEB-1", DisplayName = "First" },
                new EmployeeRecord { Id = second, CompanyId = company, PersonnelNumber = "WEB-2", DisplayName = "Second" });
            db.EmployeeAssignments.AddRange(new EmployeeAssignmentRecord { Id = Guid.NewGuid(), EmployeeId = first, CompanyId = company, OrgUnitId = unit, ValidFrom = today.AddDays(-1) },
                new EmployeeAssignmentRecord { Id = Guid.NewGuid(), EmployeeId = second, CompanyId = company, OrgUnitId = otherUnit, ValidFrom = today.AddDays(-1) });
            db.Users.AddRange(new UserRecord { Id = self, UserName = "web-self", EmployeeId = first, IsActive = true },
                new UserRecord { Id = manager, UserName = "web-manager", IsActive = true },
                new UserRecord { Id = locked, UserName = "web-locked", IsActive = true });
            db.Roles.AddRange(new RoleRecord { Id = selfRole, Name = "web self role" }, new RoleRecord { Id = managerRole, Name = "web manager role" });
            db.Permissions.AddRange(new PermissionRecord { Id = selfPermission, Code = EmployeeAccessReader.ReadSelf },
                new PermissionRecord { Id = managerPermission, Code = EmployeeAccessReader.ReadScoped });
            db.UserRoles.AddRange(new UserRoleRecord { UserId = self, RoleId = selfRole },
                new UserRoleRecord { UserId = manager, RoleId = managerRole });
            db.RolePermissions.AddRange(new RolePermissionRecord { RoleId = selfRole, PermissionId = selfPermission },
                new RolePermissionRecord { RoleId = managerRole, PermissionId = managerPermission });
            db.UserOrgScopes.Add(new() { Id = Guid.NewGuid(), UserId = manager, OrgUnitId = unit, ValidFrom = today.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var app = IoWebHost.Build([], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["IO:EnableExperimentalIdentity"] = "true", ["ConnectionStrings:IO"] = connection });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        });
        try
        {
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            if (Environment.GetEnvironmentVariable("IO_BROWSER_TEST") != "yes")
                throw new InvalidOperationException("Disposable browser test is required in CI.");
            var browserProcess = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            browserProcess.Environment.Remove("MSSQL_SA_PASSWORD");
            browserProcess.ArgumentList.Add("tests/IO.Infrastructure.SqlTests/browser-check.cjs");
            browserProcess.ArgumentList.Add(address);
            browserProcess.ArgumentList.Add(first.ToString());
            browserProcess.ArgumentList.Add(second.ToString());
            browserProcess.ArgumentList.Add(adminName);
            using (var browser = Process.Start(browserProcess) ?? throw new InvalidOperationException("Cannot start browser test."))
            {
                var stdout = browser.StandardOutput.ReadToEndAsync();
                var stderr = browser.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try { await browser.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { browser.Kill(entireProcessTree: true); throw; }
                var output = await stdout;
                var errors = await stderr;
                if (browser.ExitCode != 0) throw new InvalidOperationException("Browser test failed: " + errors);
                check(output.Contains("Browser form scenarios passed.", StringComparison.Ordinal), "browser login, denied access, organization, calendar, overnight schedule and logout forms");
            }
            HttpClient Client() => new(new HttpClientHandler { CookieContainer = new CookieContainer(),
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true }) { BaseAddress = new Uri(address) };
            async Task<string> Csrf(HttpClient client)
            {
                using var response = await client.GetAsync("/auth/csrf");
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("token").GetString()!;
            }
            async Task<HttpResponseMessage> Login(HttpClient client, string name, string password)
            {
                var token = await Csrf(client);
                using var message = new HttpRequestMessage(HttpMethod.Post, "/auth/login");
                message.Headers.Add("X-CSRF-TOKEN", token);
                message.Content = JsonContent.Create(new { UserName = name, Password = password });
                return await client.SendAsync(message);
            }
            using var anon = Client();
            var panel = await anon.GetAsync("/panel");
            var script = await anon.GetAsync("/assets/panel.js");
            check(panel.StatusCode == HttpStatusCode.OK && script.StatusCode == HttpStatusCode.OK &&
                panel.Headers.Contains("Content-Security-Policy"), "HTTP experimental UI and security policy served");
            check((await anon.GetAsync($"/employees/{first}")).StatusCode == HttpStatusCode.Unauthorized,
                "HTTP anonymous direct employee access denied");
            check((await anon.PostAsJsonAsync("/auth/login", new { UserName = "web-self", Password })).StatusCode == HttpStatusCode.BadRequest,
                "HTTP login without antiforgery denied");
            using var own = Client();
            check((await Login(own, "web-self", "invalid-password")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await Login(own, "web-self", Password)).StatusCode == HttpStatusCode.NoContent, "HTTP login rejects wrong password and accepts correct password");
            check((await own.GetAsync($"/employees/{first}")).StatusCode == HttpStatusCode.OK &&
                  (await own.GetAsync($"/employees/{second}")).StatusCode == HttpStatusCode.NotFound, "HTTP self access prevents direct ID substitution");
            check((await own.PostAsync("/auth/logout", null)).StatusCode == HttpStatusCode.BadRequest &&
                  (await own.GetAsync("/auth/me")).StatusCode == HttpStatusCode.OK, "HTTP logout requires antiforgery token");
            using var scoped = Client();
            check((await Login(scoped, "web-manager", Password)).StatusCode == HttpStatusCode.NoContent &&
                  (await scoped.GetAsync($"/employees/{first}")).StatusCode == HttpStatusCode.OK &&
                  (await scoped.GetAsync($"/employees/{second}")).StatusCode == HttpStatusCode.NotFound, "HTTP scoped manager cannot read another unit");
            using var adminClient = Client();
            check((await Login(adminClient, adminName, Password)).StatusCode == HttpStatusCode.NoContent &&
                  (await adminClient.GetAsync($"/employees/{second}")).StatusCode == HttpStatusCode.OK, "HTTP administrator with explicit permission reads globally");
            var catalog = await adminClient.GetAsync("/admin/catalog/users?offset=0");
            var catalogText = await catalog.Content.ReadAsStringAsync();
            check(catalog.StatusCode == HttpStatusCode.OK && catalogText.Contains("web-self", StringComparison.Ordinal) &&
                !catalogText.Contains("password", StringComparison.OrdinalIgnoreCase) &&
                (await scoped.GetAsync("/admin/catalog/users?offset=0")).StatusCode == HttpStatusCode.Forbidden,
                "HTTP administration catalog requires management permission and excludes credentials");
            check((await adminClient.GetAsync("/admin/catalog/users?offset=-1")).StatusCode == HttpStatusCode.BadRequest &&
                (await adminClient.GetAsync("/admin/catalog/unknown")).StatusCode == HttpStatusCode.BadRequest &&
                (await adminClient.GetAsync("/admin/catalog/user-scopes")).StatusCode == HttpStatusCode.OK &&
                (await adminClient.GetAsync("/admin/catalog/org-units")).StatusCode == HttpStatusCode.OK,
                "HTTP administration catalog validates pages and exposes scope identifiers");
            var auditResponse = await adminClient.GetAsync("/admin/catalog/audit");
            check(auditResponse.StatusCode == HttpStatusCode.OK && (await auditResponse.Content.ReadAsStringAsync()).Contains("identity.initial-administrator.created", StringComparison.Ordinal) &&
                (await scoped.GetAsync("/admin/catalog/audit")).StatusCode == HttpStatusCode.Forbidden,
                "HTTP audit catalog restricted to identity administrators");
            await using (var scope = provider.CreateAsyncScope())
                check(await scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().SecurityAudit
                    .AnyAsync(x => x.Action.StartsWith("identity.audit.viewed:")), "HTTP audit access is itself recorded");
            async Task<HttpResponseMessage> PostAdmin(HttpClient client, string path, object? input = null)
            {
                var csrf = await Csrf(client);
                using var message = new HttpRequestMessage(HttpMethod.Post, path);
                message.Headers.Add("X-CSRF-TOKEN", csrf);
                if (input is not null) message.Content = JsonContent.Create(input);
                return await client.SendAsync(message);
            }
            check((await PostAdmin(scoped, "/org/organizations", new { Name = "unauthorized" })).StatusCode == HttpStatusCode.Forbidden &&
                (await adminClient.PostAsJsonAsync("/org/organizations", new { Name = "without csrf" })).StatusCode == HttpStatusCode.BadRequest,
                "HTTP organization writes require explicit permission and CSRF");
            async Task<Guid> CreateId(string path, object body)
            {
                using var response = await PostAdmin(adminClient, path, body);
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("result").GetGuid();
            }
            var featureRole = await CreateId("/admin/roles", new { Name = "phase five six test role" });
            foreach (var code in new[] { "organization.manage", "schedule.manage", "schedule.read.all" })
                if ((await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = featureRole, Code = code })).StatusCode != HttpStatusCode.OK)
                    throw new InvalidOperationException("Synthetic feature permission grant failed.");
            Guid globalActor;
            await using (var setupScope = provider.CreateAsyncScope())
                globalActor = await setupScope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().Users
                    .Where(x => x.UserName == adminName).Select(x => x.Id).SingleAsync();
            if ((await PostAdmin(adminClient, "/admin/user-roles", new { UserId = globalActor, RoleId = featureRole })).StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Synthetic feature role grant failed.");
            var newOrg = await CreateId("/org/organizations", new { Name = "HTTP synthetic org" });
            var newCompany = await CreateId("/org/companies", new { OrganizationId = newOrg, Name = "HTTP synthetic company" });
            var newSite = await CreateId("/org/sites", new { CompanyId = newCompany, Name = "HTTP site", TimeZoneId = "Asia/Tehran" });
            var newUnit = await CreateId("/org/units", new { CompanyId = newCompany, ParentId = (Guid?)null, Name = "HTTP unit" });
            var newEmployee = await CreateId("/org/employees", new { CompanyId = newCompany, PersonnelNumber = "HTTP-1", DisplayName = "HTTP synthetic employee" });
            var workDay = DateOnly.FromDateTime(DateTime.UtcNow);
            var initialOrgAssignment = await CreateId("/org/assignments", new { EmployeeId = newEmployee, UnitId = newUnit, ValidFrom = workDay, ValidUntilExclusive = (DateOnly?)null });
            check((await PostAdmin(adminClient, "/org/assignments", new { EmployeeId = newEmployee, UnitId = newUnit, ValidFrom = workDay, ValidUntilExclusive = (DateOnly?)null })).StatusCode == HttpStatusCode.Conflict,
                "HTTP duplicate employee assignment returns conflict");
            var newCalendar = await CreateId("/scheduling/calendars", new { CompanyId = newCompany, Name = "HTTP calendar" });
            await CreateId("/scheduling/calendar-days", new { CalendarId = newCalendar, Date = workDay, Kind = 0, Note = "synthetic" });
            var newShift = await CreateId("/scheduling/shifts", new { CompanyId = newCompany, SiteId = newSite, Name = "HTTP night", StartsAt = "22:00:00", EndsAt = "06:00:00", EndDayOffset = 1 });
            var initialWorkSchedule = await CreateId("/scheduling/assignments", new { EmployeeId = newEmployee, CalendarId = newCalendar, ShiftId = newShift, ValidFrom = workDay, ValidUntilExclusive = (DateOnly?)null });
            var scheduleResponse = await adminClient.GetAsync($"/schedules/{newEmployee}/{workDay:yyyy-MM-dd}");
            check(scheduleResponse.StatusCode == HttpStatusCode.OK && (await scheduleResponse.Content.ReadAsStringAsync()).Contains("HTTP night", StringComparison.Ordinal) &&
                (await own.GetAsync($"/schedules/{newEmployee}/{workDay:yyyy-MM-dd}")).StatusCode == HttpStatusCode.NotFound,
                "HTTP schedule read enforces explicit permission and target scope");
            check((await PostAdmin(adminClient, "/scheduling/assignments", new { EmployeeId = newEmployee, CalendarId = newCalendar, ShiftId = newShift, ValidFrom = workDay, ValidUntilExclusive = (DateOnly?)null })).StatusCode == HttpStatusCode.Conflict,
                "HTTP duplicate schedule assignment returns conflict");
            var secondOrgUnit = await CreateId("/org/units", new { CompanyId = newCompany, ParentId = newUnit, Name = "HTTP second unit" });
            await CreateId("/org/transfers", new { EmployeeId = newEmployee, AssignmentId = initialOrgAssignment, UnitId = secondOrgUnit, Effective = workDay.AddDays(1) });
            check((await PostAdmin(adminClient, "/org/transfers", new { EmployeeId = newEmployee, AssignmentId = initialOrgAssignment, UnitId = secondOrgUnit, Effective = workDay.AddDays(2) })).StatusCode == HttpStatusCode.Conflict,
                "HTTP transfer preserves history and cannot repeat closed assignment");
            await CreateId("/scheduling/replacements", new { EmployeeId = newEmployee, AssignmentId = initialWorkSchedule, CalendarId = newCalendar, ShiftId = newShift, Effective = workDay.AddDays(1) });
            check((await adminClient.GetAsync($"/schedules/{newEmployee}/{workDay:yyyy-MM-dd}")).StatusCode == HttpStatusCode.OK &&
                (await adminClient.GetAsync($"/schedules/{newEmployee}/{workDay.AddDays(1):yyyy-MM-dd}")).StatusCode == HttpStatusCode.OK &&
                (await PostAdmin(adminClient, "/scheduling/replacements", new { EmployeeId = newEmployee, AssignmentId = initialWorkSchedule, CalendarId = newCalendar, ShiftId = newShift, Effective = workDay.AddDays(2) })).StatusCode == HttpStatusCode.Conflict,
                "HTTP schedule replacement keeps old and new days and rejects repeat");
            check((await adminClient.GetAsync("/org/catalog/employees")).StatusCode == HttpStatusCode.OK &&
                (await adminClient.GetAsync("/scheduling/catalog/assignments")).StatusCode == HttpStatusCode.OK &&
                (await scoped.GetAsync("/org/catalog/employees")).StatusCode == HttpStatusCode.Forbidden &&
                (await scoped.GetAsync("/scheduling/catalog/assignments")).StatusCode == HttpStatusCode.Forbidden,
                "HTTP bounded organization and scheduling catalogs require management permissions");
            var converted = await adminClient.GetAsync("/dates/jalali/1403/1/1");
            check(converted.StatusCode == HttpStatusCode.OK && (await converted.Content.ReadAsStringAsync()).Contains("2024-03-20", StringComparison.Ordinal) &&
                (await adminClient.GetAsync("/dates/jalali/1402/12/30")).StatusCode == HttpStatusCode.BadRequest,
                "HTTP Persian date conversion validates leap boundaries");
            Guid administratorRole;
            await using (var scope = provider.CreateAsyncScope())
                administratorRole = await scope.ServiceProvider.GetRequiredService<IoDbContext>().Roles
                    .Where(x => x.Name == "Initial administrator").Select(x => x.Id).SingleAsync();
            check((await PostAdmin(scoped, "/admin/user-roles", new { UserId = manager, RoleId = administratorRole })).StatusCode == HttpStatusCode.Forbidden,
                "HTTP scoped manager cannot grant itself administrator role");
            check((await adminClient.PostAsJsonAsync("/admin/roles", new { Name = "without csrf" })).StatusCode == HttpStatusCode.BadRequest,
                "HTTP administrative mutation without antiforgery denied");
            using var newRoleResponse = await PostAdmin(adminClient, "/admin/roles", new { Name = "web created role" });
            newRoleResponse.EnsureSuccessStatusCode();
            using var roleJson = JsonDocument.Parse(await newRoleResponse.Content.ReadAsStringAsync());
            var createdRole = roleJson.RootElement.GetProperty("result").GetGuid();
            var permissionResponse = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = createdRole, Code = EmployeeAccessReader.ReadSelf });
            check(permissionResponse.StatusCode == HttpStatusCode.OK, "HTTP administrator creates role and grants explicit permission");
            check((await PostAdmin(adminClient, "/admin/users", new { UserName = "web-invalid", Password = "short", EmployeeId = first })).StatusCode == HttpStatusCode.BadRequest,
                "HTTP administrative user creation rejects weak password");
            using var newUserResponse = await PostAdmin(adminClient, "/admin/users", new { UserName = "web-created", Password, EmployeeId = first });
            newUserResponse.EnsureSuccessStatusCode();
            using var userJson = JsonDocument.Parse(await newUserResponse.Content.ReadAsStringAsync());
            var createdUser = userJson.RootElement.GetProperty("result").GetGuid();
            check((await PostAdmin(adminClient, "/admin/user-roles", new { UserId = createdUser, RoleId = createdRole })).StatusCode == HttpStatusCode.OK,
                "HTTP administrator creates user and assigns role");
            using var createdClient = Client();
            check((await Login(createdClient, "web-created", Password)).StatusCode == HttpStatusCode.NoContent &&
                (await createdClient.GetAsync($"/employees/{first}")).StatusCode == HttpStatusCode.Forbidden &&
                (await PostAdmin(createdClient, "/auth/change-password", new { CurrentPassword = Password, NewPassword = Password })).StatusCode == HttpStatusCode.BadRequest,
                "HTTP provisioned user must change temporary password before access");
            const string changedCreatedPassword = "Synthetic!Created-Password-2026";
            check((await PostAdmin(createdClient, "/auth/change-password", new { CurrentPassword = Password, NewPassword = changedCreatedPassword })).StatusCode == HttpStatusCode.NoContent &&
                (await Login(createdClient, "web-created", changedCreatedPassword)).StatusCode == HttpStatusCode.NoContent &&
                (await createdClient.GetAsync($"/employees/{first}")).StatusCode == HttpStatusCode.OK &&
                (await createdClient.GetAsync($"/employees/{second}")).StatusCode == HttpStatusCode.NotFound,
                "HTTP provisioned user gets only explicit own access after changing password");
            using var scopedRoleResponse = await PostAdmin(adminClient, "/admin/roles", new { Name = "web extra scope role" });
            scopedRoleResponse.EnsureSuccessStatusCode();
            using var scopedRoleJson = JsonDocument.Parse(await scopedRoleResponse.Content.ReadAsStringAsync());
            var scopedRoleId = scopedRoleJson.RootElement.GetProperty("result").GetGuid();
            var permissionGranted = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = scopedRoleId, Code = EmployeeAccessReader.ReadScoped });
            var roleGranted = await PostAdmin(adminClient, "/admin/user-roles", new { UserId = createdUser, RoleId = scopedRoleId });
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var invalidScope = await PostAdmin(adminClient, "/admin/user-scopes", new { UserId = createdUser, UnitId = otherUnit, ValidFrom = today, ValidUntilExclusive = today });
            check(invalidScope.StatusCode == HttpStatusCode.BadRequest, "HTTP invalid scope interval rejected");
            var validScope = await PostAdmin(adminClient, "/admin/user-scopes", new { UserId = createdUser, UnitId = otherUnit, ValidFrom = today, ValidUntilExclusive = (DateOnly?)null });
            check(permissionGranted.StatusCode == HttpStatusCode.OK && roleGranted.StatusCode == HttpStatusCode.OK &&
                validScope.StatusCode == HttpStatusCode.OK && (await createdClient.GetAsync($"/employees/{second}")).StatusCode == HttpStatusCode.OK,
                "HTTP administrator grants role and exact-unit scope to user");
            await using (var auditScope = provider.CreateAsyncScope())
            {
                var rows = auditScope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().SecurityAudit;
                var expected = "scope-grant." + otherUnit.ToString("N") + "." + today.ToString("yyyyMMdd") + ".open:" + createdUser.ToString("N");
                check(await rows.AnyAsync(x => x.Action == expected), "HTTP scope grant audit identifies user, unit and interval");
            }
            using var validScopeJson = JsonDocument.Parse(await validScope.Content.ReadAsStringAsync());
            var scopeId = validScopeJson.RootElement.GetProperty("result").GetGuid();
            check((await PostAdmin(adminClient, $"/admin/user-scopes/{scopeId}/revoke")).StatusCode == HttpStatusCode.OK &&
                (await createdClient.GetAsync($"/employees/{second}")).StatusCode == HttpStatusCode.NotFound,
                "HTTP scope revocation takes effect immediately");
            var scopeAgain = await PostAdmin(adminClient, "/admin/user-scopes", new { UserId = createdUser, UnitId = otherUnit, ValidFrom = today, ValidUntilExclusive = (DateOnly?)null });
            if (scopeAgain.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Could not re-grant test scope.");
            check((await PostAdmin(adminClient, "/admin/user-roles/revoke", new { UserId = createdUser, RoleId = scopedRoleId })).StatusCode == HttpStatusCode.OK &&
                (await createdClient.GetAsync($"/employees/{second}")).StatusCode == HttpStatusCode.NotFound,
                "HTTP role revocation takes effect immediately");
            if ((await PostAdmin(adminClient, "/admin/user-roles", new { UserId = createdUser, RoleId = scopedRoleId })).StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Could not re-grant test role.");
            check((await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = scopedRoleId, Code = EmployeeAccessReader.ReadScoped })).StatusCode == HttpStatusCode.OK &&
                (await createdClient.GetAsync($"/employees/{second}")).StatusCode == HttpStatusCode.NotFound,
                "HTTP permission revocation takes effect immediately");
            Guid administratorId;
            await using (var adminScope = provider.CreateAsyncScope())
                administratorId = await adminScope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().Users
                    .Where(x => x.UserName == adminName).Select(x => x.Id).SingleAsync();
            check((await PostAdmin(adminClient, "/admin/user-roles/revoke", new { UserId = administratorId, RoleId = administratorRole })).StatusCode == HttpStatusCode.BadRequest &&
                (await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = administratorRole, Code = InitialAdministrator.ManageIdentity })).StatusCode == HttpStatusCode.BadRequest,
                "HTTP administrator cannot revoke own management path");
            using var preservedCookie = Client();
            if ((await Login(preservedCookie, "web-created", changedCreatedPassword)).StatusCode != HttpStatusCode.NoContent)
                throw new InvalidOperationException("Second synthetic user login failed.");
            check((await PostAdmin(adminClient, $"/admin/users/{createdUser}/disable")).StatusCode == HttpStatusCode.OK &&
                (await createdClient.GetAsync("/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
                "HTTP administrator disables account and session is revoked");
            check((await PostAdmin(adminClient, $"/admin/users/{createdUser}/enable")).StatusCode == HttpStatusCode.OK &&
                (await preservedCookie.GetAsync("/auth/me")).StatusCode == HttpStatusCode.Unauthorized &&
                (await Login(createdClient, "web-created", changedCreatedPassword)).StatusCode == HttpStatusCode.NoContent,
                "HTTP re-enable does not resurrect a never-presented old session");
            await using (var auditScope = provider.CreateAsyncScope())
            {
                var audits = auditScope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().SecurityAudit;
                check(await audits.AnyAsync(x => x.Action == "identity.user.created:" + createdUser.ToString("N")) &&
                    await audits.AnyAsync(x => x.Action == "identity.user.disabled:" + createdUser.ToString("N")),
                    "HTTP sensitive administration records audit events");
            }
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
                db.UserRoles.Remove(await db.UserRoles.SingleAsync(x => x.UserId == manager));
                var selfRecord = await db.Users.SingleAsync(x => x.Id == self);
                selfRecord.IsActive = false;
                await db.SaveChangesAsync();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<LoginAccount>>();
                var login = await users.FindByNameAsync(adminName) ?? throw new InvalidOperationException();
                await users.UpdateSecurityStampAsync(login);
            }
            check((await scoped.GetAsync($"/employees/{first}")).StatusCode == HttpStatusCode.NotFound,
                "HTTP role revocation applies without re-login");
            check((await own.GetAsync("/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
                "HTTP inactive account session revoked");
            check((await adminClient.GetAsync("/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
                "HTTP security stamp change revokes session");
            using var managerSecond = Client();
            if ((await Login(managerSecond, "web-manager", Password)).StatusCode != HttpStatusCode.NoContent)
                throw new InvalidOperationException("Second synthetic manager login failed.");
            check((await PostAdmin(scoped, "/auth/change-password", new { CurrentPassword = "wrong", NewPassword = "Synthetic!Changed-Password-2026" })).StatusCode == HttpStatusCode.BadRequest &&
                (await scoped.GetAsync("/auth/me")).StatusCode == HttpStatusCode.OK, "HTTP wrong current password does not change credentials or session");
            check((await PostAdmin(scoped, "/auth/change-password", new { CurrentPassword = Password, NewPassword = "Synthetic!Changed-Password-2026" })).StatusCode == HttpStatusCode.NoContent &&
                (await scoped.GetAsync("/auth/me")).StatusCode == HttpStatusCode.Unauthorized, "HTTP password change ends current session");
            check((await managerSecond.GetAsync("/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
                "HTTP password change revokes another session");
            check((await Login(scoped, "web-manager", Password)).StatusCode == HttpStatusCode.Unauthorized &&
                (await Login(scoped, "web-manager", "Synthetic!Changed-Password-2026")).StatusCode == HttpStatusCode.NoContent,
                "HTTP new password replaces old password");
            await using (var passwordAudit = provider.CreateAsyncScope())
                check(await passwordAudit.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().SecurityAudit
                    .AnyAsync(x => x.Action == "identity.password.changed:" + manager.ToString("N")), "HTTP password change audited without credentials");
            for (var i = 0; i < 3; i++)
                if ((await PostAdmin(scoped, "/auth/change-password", new { CurrentPassword = "wrong", NewPassword = "Synthetic!Other-Password-2026" })).StatusCode != HttpStatusCode.BadRequest)
                    throw new InvalidOperationException("Wrong password change was not rejected.");
            check((await PostAdmin(scoped, "/auth/change-password", new { CurrentPassword = "wrong", NewPassword = "Synthetic!Other-Password-2026" })).StatusCode == HttpStatusCode.TooManyRequests,
                "HTTP repeated password changes are rate limited");
            using var anotherManagerSession = Client();
            if ((await Login(anotherManagerSession, "web-manager", "Synthetic!Changed-Password-2026")).StatusCode != HttpStatusCode.NoContent)
                throw new InvalidOperationException("Second synthetic manager re-login failed.");
            var token = await Csrf(scoped);
            using var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
            logout.Headers.Add("X-CSRF-TOKEN", token);
            check((await scoped.SendAsync(logout)).StatusCode == HttpStatusCode.NoContent &&
                  (await scoped.GetAsync("/auth/me")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anotherManagerSession.GetAsync("/auth/me")).StatusCode == HttpStatusCode.Unauthorized,
                  "HTTP logout ends all sessions and revokes copied cookies");
            using var lockClient = Client();
            for (var i = 0; i < 5; i++)
                if ((await Login(lockClient, "web-locked", "invalid-password")).StatusCode != HttpStatusCode.Unauthorized)
                    throw new InvalidOperationException("Failed login did not return unauthorized.");
            check((await Login(lockClient, "web-locked", Password)).StatusCode == HttpStatusCode.Unauthorized,
                "HTTP lockout blocks correct password after repeated failures");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
