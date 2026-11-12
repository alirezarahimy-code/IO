using System.Net;
using System.Diagnostics;
using System.Globalization;
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
            // Synthetic DB snapshot fixture only: never an SDK/hardware acceptance test.
            await using(var uploadScope=provider.CreateAsyncScope())
            {
                var uploadDb=uploadScope.ServiceProvider.GetRequiredService<IoDbContext>();
                var uploadDevice=await uploadDb.Devices.AsNoTracking().Where(d=>(d.Number=="S08-A"||d.Number=="S08-B")&&!uploadDb.DeviceUserUploads.Any(u=>u.DeviceId==d.Id&&(u.State=="pending"||u.State=="running"||(u.State=="unknown"&&u.ResolutionSnapshotId==null)))).FirstAsync();
                var uploadSnapshot=await uploadDb.DeviceUserDownloads.AsNoTracking().Where(x=>x.DeviceId==uploadDevice.Id&&x.State=="succeeded").OrderByDescending(x=>x.RequestedAtUtc).FirstAsync();
                var uploadEmployee=await uploadDb.SourceEmployeeMappings.Where(x=>x.SourceId==uploadDevice.SourceId&&x.SourcePersonId=="0001").Select(x=>x.EmployeeId).SingleAsync();
                browserProcess.ArgumentList.Add(JsonSerializer.Serialize(new{id=Guid.NewGuid(),deviceId=uploadDevice.Id,expectedDeviceVersion=uploadDevice.Version,snapshotId=uploadSnapshot.Id,employeeId=uploadEmployee,personIdentifier="0001",card="789",enabled=true,timeoutSeconds=30}));
            }
            await using(var logScope=provider.CreateAsyncScope())
            {
                var logDb=logScope.ServiceProvider.GetRequiredService<IoDbContext>();
                var logFixture=await logDb.DeviceAttendanceDownloads.AsNoTracking().Where(x=>logDb.Devices.Any(d=>d.Id==x.DeviceId&&d.Number=="S09-BROWSER")&&x.State=="succeeded").Select(x=>x.Id).SingleAsync();
                browserProcess.ArgumentList.Add(logFixture.ToString());
            }
            await using(var commandScope=provider.CreateAsyncScope())
            {
                var commandDb=commandScope.ServiceProvider.GetRequiredService<IoDbContext>();
                var commandFixture=await commandDb.DeviceOperations.AsNoTracking().Where(x=>commandDb.Devices.Any(d=>d.Id==x.DeviceId&&d.Number=="S11-A")&&x.State=="failed").OrderByDescending(x=>x.RequestedAtUtc).Select(x=>x.Id).FirstAsync();
                browserProcess.ArgumentList.Add(commandFixture.ToString());
            }
            using (var browser = Process.Start(browserProcess) ?? throw new InvalidOperationException("Cannot start browser test."))
            {
                var stdout = browser.StandardOutput.ReadToEndAsync();
                var stderr = browser.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(240));
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
            using var helpList = await anon.GetAsync("/help/topics");
            using var helpJson = JsonDocument.Parse(await helpList.Content.ReadAsStringAsync());
            check(helpList.StatusCode == HttpStatusCode.OK && helpJson.RootElement.GetArrayLength() == 1 &&
                  (await anon.GetAsync("/help/topic/login")).StatusCode == HttpStatusCode.OK &&
                  (await anon.GetAsync("/help/topic/roles")).StatusCode == HttpStatusCode.Forbidden &&
                  (await anon.GetAsync("/help/topic/does-not-exist")).StatusCode == HttpStatusCode.NotFound &&
                  (await anon.GetAsync("/workspace/permission-catalog")).StatusCode == HttpStatusCode.Unauthorized,
                  "HTTP help filters anonymous topics and denies direct protected article and permission catalogue");
            var panel = await anon.GetAsync("/panel");
            var script = await anon.GetAsync("/assets/panel.js");
            var themeScript = await anon.GetAsync("/assets/theme.js");
            var themeStyle = await anon.GetAsync("/assets/theme.css");
            check(panel.StatusCode == HttpStatusCode.OK && script.StatusCode == HttpStatusCode.OK &&
                themeScript.StatusCode == HttpStatusCode.OK && themeStyle.StatusCode == HttpStatusCode.OK &&
                themeStyle.Content.Headers.ContentType?.MediaType == "text/css" &&
                panel.Headers.GetValues("Content-Security-Policy").Single().Contains("style-src 'self'", StringComparison.Ordinal),
                "HTTP experimental UI shared theme assets and security policy served");
            check((await anon.GetAsync($"/employees/{first}")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/employees")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/organization")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/employee-history")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/rules")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/sources")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/mappings")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/raw-events")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/conflicts")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/calculations")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/periods")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/my-schedule")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/my-attendance")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/leave-admin")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/delegation")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/settings")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/settings.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/settings/correction-own")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/delegation.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/delegations/own")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/my-leave")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/leave-admin.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/my-leave.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/leave/own-types")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/team-attendance")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/team-attendance.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/attendance/team?fromUtc=2027-03-01T00%3A00%3A00Z&untilUtcExclusive=2027-03-02T00%3A00%3A00Z")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/my-reports")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/reports")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/report-builder")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/report-builder.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/reports.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/my-reports.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/calculations/own-report?start=2027-03-01&endExclusive=2027-03-02")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/workflow")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/employee-home")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/manager-home")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/executive-home")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/executive-home.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync($"/executive/overview/{Guid.NewGuid()}?fromUtc=2027-03-01T00%3A00%3A00Z&untilUtcExclusive=2027-03-02T00%3A00%3A00Z")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/manager-home.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/employee-home.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/workflow.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync($"/corrections/configured-policies/{Guid.NewGuid()}")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/my-attendance.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/attendance/own?fromUtc=2027-03-01T00%3A00%3A00Z&untilUtcExclusive=2027-03-02T00%3A00%3A00Z")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/audit")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/users")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/roles")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/roles.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/users.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/audit.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/schedules/own/2027-03-01")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/account")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/account.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/login")).StatusCode == HttpStatusCode.OK &&
                  (await anon.GetAsync("/assets/login.js")).StatusCode == HttpStatusCode.OK &&
                  (await anon.GetAsync("/workspace/my-requests")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/new-request")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/my-requests.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/inbox")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/decision-history")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/manager-requests.js")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/attendance/sources")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/calendars")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/shifts")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/workspace/schedules")).StatusCode == HttpStatusCode.Unauthorized &&
                  (await anon.GetAsync("/assets/employees.js")).StatusCode == HttpStatusCode.Unauthorized,
                "HTTP anonymous direct employee and workspace access denied");
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
            // The login rate limiter allows 30 attempts per IP per minute; the dense
            // tail of synthetic logins can exhaust one window, so re-authentications retry.
            async Task<HttpResponseMessage> LoginWithRetry(HttpClient client, string name, string password)
            {
                for (var attempt = 0; ; attempt++)
                {
                    var response = await Login(client, name, password);
                    if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= 3) return response;
                    await Task.Delay(TimeSpan.FromSeconds(25));
                }
            }
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
            var granularRole = await CreateId("/admin/roles", new { Name = "foundation HTTP granular role" });
            (await PostAdmin(adminClient, "/admin/user-roles", new { UserId = self, RoleId = granularRole })).EnsureSuccessStatusCode();
            (await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = granularRole, Code = "organization.read" })).EnsureSuccessStatusCode();
            check((await own.GetAsync("/org/catalog/companies")).StatusCode == HttpStatusCode.OK &&
                  (await PostAdmin(own, "/org/organizations", new { Name = "must not create" })).StatusCode == HttpStatusCode.Forbidden &&
                  (await own.GetAsync("/help/topic/organization")).StatusCode == HttpStatusCode.OK,
                  "HTTP granular organization reader can read page help but cannot create data");
            (await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = granularRole, Code = "organization.read" })).EnsureSuccessStatusCode();
            check((await own.GetAsync("/org/catalog/companies")).StatusCode == HttpStatusCode.Forbidden &&
                  (await own.GetAsync("/help/topic/organization")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP granular revoke removes data and help on the same authenticated session");
            (await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = granularRole, Code = "executive.dashboard.read" })).EnsureSuccessStatusCode();
            using var granularCaps = JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities"));
            (await PostAdmin(adminClient, "/admin/user-roles/revoke", new { UserId = self, RoleId = granularRole })).EnsureSuccessStatusCode();
            using var revokedCaps = JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities"));
            check(granularCaps.RootElement.GetProperty("executive").GetBoolean() &&
                  !revokedCaps.RootElement.GetProperty("executive").GetBoolean() &&
                  (await own.GetAsync("/help/topic/executive-home")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP role revoke removes executive menu and protected help without re-login");
            var operatorTemplate = PermissionCatalog.Templates.Single(t => t.Key == "operator");
            (await PostAdmin(adminClient, "/admin/role-templates", new { })).EnsureSuccessStatusCode();
            (await PostAdmin(adminClient, "/admin/user-roles", new { UserId = self, RoleId = operatorTemplate.Id })).EnsureSuccessStatusCode();
            using var operatorCaps = JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities"));
            check(operatorCaps.RootElement.GetProperty("operator").GetBoolean() &&
                  operatorCaps.RootElement.GetProperty("permissions").EnumerateArray().Where(x => x.GetString()!.StartsWith("devices.", StringComparison.Ordinal)).Select(x => x.GetString()).OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(new[] { "devices.attendance.read", "devices.commands.read", "devices.connection.read", "devices.files.read", "devices.groups.read", "devices.health.read", "devices.info.read", "devices.read", "devices.schedules.read", "devices.settings.read", "devices.users.read" }),
                  "HTTP operator shell identifies role without granting unimplemented device operations");
            (await PostAdmin(adminClient, "/admin/user-roles/revoke", new { UserId = self, RoleId = operatorTemplate.Id })).EnsureSuccessStatusCode();
            var featureRole = await CreateId("/admin/roles", new { Name = "phase five six test role" });
            foreach (var code in new[] { "organization.manage", "schedule.manage", "schedule.read.all", "attendance.ingest", "attendance.calculate", "correction.configure", "period.preflight", "period.draft.manage", "period.diagnostic.manage" })
                if ((await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = featureRole, Code = code })).StatusCode != HttpStatusCode.OK)
                    throw new InvalidOperationException("Synthetic feature permission grant failed.");
            Guid globalActor;
            await using (var setupScope = provider.CreateAsyncScope())
                globalActor = await setupScope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().Users
                    .Where(x => x.UserName == adminName).Select(x => x.Id).SingleAsync();
            if ((await PostAdmin(adminClient, "/admin/user-roles", new { UserId = globalActor, RoleId = featureRole })).StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Synthetic feature role grant failed.");
            Guid testCompany, testSelfRole, testManagerRole;
            await using (var correctionScope = provider.CreateAsyncScope())
            {
                var db = correctionScope.ServiceProvider.GetRequiredService<IoDbContext>();
                testCompany = (await db.Employees.SingleAsync(x => x.Id == first)).CompanyId;
                testSelfRole = (await db.Roles.SingleAsync(x => x.Name == "web self role")).Id;
                testManagerRole = (await db.Roles.SingleAsync(x => x.Name == "web manager role")).Id;
            }
            var deviceSite = await CreateId("/org/sites", new { CompanyId = testCompany, Name = "HTTP device site", TimeZoneId = "UTC" });
            var deviceRole = await CreateId("/admin/roles", new { Name = "HTTP device operator" });
            (await PostAdmin(adminClient,"/admin/user-roles",new{UserId=self,RoleId=deviceRole})).EnsureSuccessStatusCode();
            var deviceOperation = Guid.NewGuid();
            var deviceInput = new IO.Infrastructure.Devices.DeviceRegistry.Input(deviceSite,"HTTP-S04-A","HTTP device","unverified","offline-file",null,null,true);
            var devicePayload = new { OperationId=deviceOperation, Device=deviceInput };
            check((await anon.GetAsync("/devices")).StatusCode==HttpStatusCode.Unauthorized &&
                  (await own.GetAsync("/devices")).StatusCode==HttpStatusCode.Forbidden &&
                  (await own.PostAsJsonAsync("/devices",devicePayload)).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP device endpoints deny anonymous ungranted and missing-CSRF requests");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.create"})).EnsureSuccessStatusCode();
            using(var createdDevice=await PostAdmin(own,"/devices",devicePayload))
            using(var replayDevice=await PostAdmin(own,"/devices",devicePayload))
                check(createdDevice.StatusCode==HttpStatusCode.OK&&replayDevice.StatusCode==HttpStatusCode.OK &&
                      (await own.GetAsync("/devices")).StatusCode==HttpStatusCode.Forbidden &&
                      (await PostAdmin(own,"/devices",new{OperationId=Guid.NewGuid(),Device=deviceInput})).StatusCode==HttpStatusCode.Conflict,
                      "HTTP device create-only grant supports exact retry and denies duplicate number without granting inventory read");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.read"})).EnsureSuccessStatusCode();
            async Task<byte[]> DeviceVersion(){using var d=JsonDocument.Parse(await own.GetStringAsync("/devices/"+deviceOperation));return d.RootElement.GetProperty("version").GetBytesFromBase64();}
            using(var deviceView=JsonDocument.Parse(await own.GetStringAsync("/devices/"+deviceOperation)))
                check(deviceView.RootElement.GetProperty("connectionStatus").GetString()=="not-tested"&&
                      (await own.GetAsync("/help/topic/devices")).StatusCode==HttpStatusCode.OK,
                      "HTTP device read grant exposes protected help and truthful untested connection state");
            var deviceVersion=await DeviceVersion();
            var deviceEdit=new{ExpectedVersion=deviceVersion,Device=deviceInput with{Name="HTTP renamed device"}};
            var noDeviceEdit=(await PostAdmin(own,"/devices/"+deviceOperation,deviceEdit)).StatusCode;
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.update"})).EnsureSuccessStatusCode();
            check(noDeviceEdit==HttpStatusCode.Forbidden&&
                  (await PostAdmin(own,"/devices/"+deviceOperation,deviceEdit)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/devices/"+deviceOperation,deviceEdit)).StatusCode==HttpStatusCode.Conflict,
                  "HTTP device edit requires independent permission and rejects stale optimistic replay");
            deviceVersion=await DeviceVersion();
            const string deviceSecret="Synthetic HTTP device secret!";
            var credentialInput=new{ExpectedVersion=deviceVersion,Secret=deviceSecret};
            var noDeviceSecret=(await PostAdmin(own,"/devices/"+deviceOperation+"/credential",credentialInput)).StatusCode;
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.credentials.manage"})).EnsureSuccessStatusCode();
            var changedDeviceSecret=await PostAdmin(own,"/devices/"+deviceOperation+"/credential",credentialInput);
            var deviceResponse=await own.GetStringAsync("/devices/"+deviceOperation);
            check(noDeviceSecret==HttpStatusCode.Forbidden&&changedDeviceSecret.StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/devices/"+deviceOperation+"/credential",new{ExpectedVersion=await DeviceVersion()})).StatusCode==HttpStatusCode.BadRequest&&
                  !deviceResponse.Contains(deviceSecret,StringComparison.Ordinal)&&!deviceResponse.Contains("protectedValue",StringComparison.OrdinalIgnoreCase)&&
                  JsonDocument.Parse(deviceResponse).RootElement.GetProperty("hasCredential").GetBoolean(),
                  "HTTP device credential write is independently authorized and never returned in metadata response");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.receive"})).EnsureSuccessStatusCode();
            using(var capsDevice=JsonDocument.Parse(await own.GetStringAsync("/devices/"+deviceOperation+"/capabilities")))
                check(capsDevice.RootElement.GetProperty("operations").EnumerateArray().All(o=>!o.GetProperty("available").GetBoolean()),
                      "HTTP device future command grant never reports an available unverified adapter");
            (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code="devices.credentials.manage"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/devices/"+deviceOperation+"/credential",new{ExpectedVersion=await DeviceVersion(),Secret=(string?)null})).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP device credential permission revocation rejects mutation on the same authenticated session");
            var groupRoot=Guid.NewGuid();var groupChild=Guid.NewGuid();
            var groupInput=new IO.Infrastructure.Devices.DeviceGroups.Input(testCompany,null,"HTTP group root");
            var groupPayload=new{OperationId=groupRoot,Group=groupInput};
            check((await anon.GetAsync("/device-groups")).StatusCode==HttpStatusCode.Unauthorized&&
                  (await own.GetAsync("/device-groups")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.PostAsJsonAsync("/device-groups",groupPayload)).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP reference group endpoints require authentication independent group permission and CSRF");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.groups.manage"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-groups",groupPayload)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-groups",groupPayload)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-groups",new{OperationId=Guid.NewGuid(),Group=groupInput})).StatusCode==HttpStatusCode.Conflict&&
                  (await own.GetAsync("/device-groups")).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP reference group management supports create retry and sibling uniqueness without implicitly granting read");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.groups.read"})).EnsureSuccessStatusCode();
            (await PostAdmin(own,"/device-groups",new{OperationId=groupChild,Group=groupInput with{ParentId=groupRoot,Name="HTTP group child"}})).EnsureSuccessStatusCode();
            async Task<byte[]> GroupVersion(Guid id,Guid? parentId=null){using var json=JsonDocument.Parse(await own.GetStringAsync("/device-groups?companyId="+testCompany+(parentId is null?"":"&parentId="+parentId)));return json.RootElement.GetProperty("items").EnumerateArray().Single(g=>g.GetProperty("id").GetGuid()==id).GetProperty("version").GetBytesFromBase64();}
            using(var branch=JsonDocument.Parse(await own.GetStringAsync("/device-groups?companyId="+testCompany+"&parentId="+groupRoot)))
                check(branch.RootElement.GetProperty("parent").GetProperty("id").GetGuid()==groupRoot&&branch.RootElement.GetProperty("items").EnumerateArray().Any(g=>g.GetProperty("id").GetGuid()==groupChild)&&
                      (await own.GetAsync("/help/topic/device-groups")).StatusCode==HttpStatusCode.OK,
                      "HTTP reference tree reads expose the selected branch and independently authorized context help");
            var groupVersion=await GroupVersion(groupRoot);var groupEdit=new{ExpectedVersion=groupVersion,Group=groupInput with{Name="HTTP group root renamed"}};
            (await PostAdmin(own,"/device-groups/"+groupRoot,groupEdit)).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-groups/"+groupRoot,groupEdit)).StatusCode==HttpStatusCode.Conflict&&
                  (await PostAdmin(own,"/device-groups/"+groupRoot,new{ExpectedVersion=await GroupVersion(groupRoot),Group=groupInput with{ParentId=groupChild}})).StatusCode==HttpStatusCode.Conflict,
                  "HTTP reference group edits reject stale versions and ancestor cycles");
            var groupAssignment=new{ExpectedVersion=await DeviceVersion(),GroupId=(Guid?)groupRoot};
            var deniedGroupAssignment=(await PostAdmin(own,"/devices/"+deviceOperation+"/group",groupAssignment)).StatusCode;
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.groups.assign"})).EnsureSuccessStatusCode();
            var assignedGroup=(await PostAdmin(own,"/devices/"+deviceOperation+"/group",groupAssignment)).StatusCode;
            var occupiedGroupDelete=(await PostAdmin(own,"/device-groups/"+groupRoot+"/delete",new{ExpectedVersion=await GroupVersion(groupRoot)})).StatusCode;
            var childGroupVersion=await GroupVersion(groupChild,groupRoot);
            var childDeletion=new{ExpectedVersion=childGroupVersion};
            check(deniedGroupAssignment==HttpStatusCode.Forbidden&&assignedGroup==HttpStatusCode.OK&&occupiedGroupDelete==HttpStatusCode.Conflict&&
                  (await PostAdmin(own,"/devices/"+deviceOperation+"/group",new{ExpectedVersion=await DeviceVersion(),GroupId=(Guid?)null})).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-groups/"+groupChild+"/delete",childDeletion)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-groups/"+groupChild+"/delete",childDeletion)).StatusCode==HttpStatusCode.OK,
                  "HTTP device assignment is independent and populated groups cannot be deleted while empty-leaf delete retries preserve history");
            foreach(var code in new[]{"devices.groups.read","devices.groups.manage","devices.groups.assign"})
                (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code=code})).EnsureSuccessStatusCode();
            using(var revokedGroupCaps=JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities")))
                check((await own.GetAsync("/device-groups")).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/help/topic/device-groups")).StatusCode==HttpStatusCode.Forbidden&&
                      (await PostAdmin(own,"/device-groups/"+groupChild+"/delete",childDeletion)).StatusCode==HttpStatusCode.Forbidden&&
                      (await PostAdmin(own,"/devices/"+deviceOperation+"/group",new{ExpectedVersion=await DeviceVersion(),GroupId=(Guid?)groupRoot})).StatusCode==HttpStatusCode.Forbidden&&
                      !revokedGroupCaps.RootElement.GetProperty("pages").EnumerateArray().Any(p=>p.GetProperty("route").GetString()=="/workspace/device-groups"),
                      "HTTP reference group revocation removes data help menu assignment and historical delete replay on the existing session");
            var connectionDiagnosticId=Guid.NewGuid();
            var diagnosticPayload=new{OperationId=connectionDiagnosticId,DeviceId=deviceOperation,ExpectedDeviceVersion=await DeviceVersion(),TimeoutSeconds=1};
            check((await anon.GetAsync("/device-connection")).StatusCode==HttpStatusCode.Unauthorized&&
                  (await own.GetAsync("/device-connection")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.PostAsJsonAsync("/device-connection",diagnosticPayload)).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP connection diagnostics require authentication independent permission and CSRF");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.connection.test"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-connection",diagnosticPayload)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-connection",diagnosticPayload)).StatusCode==HttpStatusCode.OK&&
                  (await own.GetAsync("/device-connection")).StatusCode==HttpStatusCode.Forbidden&&
                  (await PostAdmin(own,"/device-connection",new{OperationId=connectionDiagnosticId,DeviceId=deviceOperation,ExpectedDeviceVersion=diagnosticPayload.ExpectedDeviceVersion,TimeoutSeconds=2})).StatusCode==HttpStatusCode.Conflict,
                  "HTTP connection request grant permits exact retry but grants neither history nor changed retry");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.connection.read"})).EnsureSuccessStatusCode();
            using(var diagnosticPage=JsonDocument.Parse(await own.GetStringAsync("/device-connection?deviceId="+deviceOperation)))
            {
                var item=diagnosticPage.RootElement.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==connectionDiagnosticId);
                var cancelPayload=new{ExpectedVersion=item.GetProperty("version").GetBytesFromBase64()};
                check(item.GetProperty("state").GetString()=="pending"&&
                      (await own.GetAsync("/help/topic/device-connection")).StatusCode==HttpStatusCode.OK&&
                      (await PostAdmin(own,"/device-connection/"+connectionDiagnosticId+"/cancel",cancelPayload)).StatusCode==HttpStatusCode.OK&&
                      (await PostAdmin(own,"/device-connection/"+connectionDiagnosticId+"/cancel",cancelPayload)).StatusCode==HttpStatusCode.Conflict,
                      "HTTP connection history and protected help expose truthful pending status and versioned pre-execution cancellation");
            }
            foreach(var code in new[]{"devices.connection.read","devices.connection.test"})
                (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code=code})).EnsureSuccessStatusCode();
            using(var diagnosticCaps=JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities")))
                check((await own.GetAsync("/device-connection")).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/help/topic/device-connection")).StatusCode==HttpStatusCode.Forbidden&&
                      (await PostAdmin(own,"/device-connection",diagnosticPayload)).StatusCode==HttpStatusCode.Forbidden&&
                      !diagnosticCaps.RootElement.GetProperty("pages").EnumerateArray().Any(x=>x.GetProperty("route").GetString()=="/workspace/device-connection"),
                      "HTTP connection revoke removes history help menu and retry on the same authenticated session");
            var s06Device=Guid.NewGuid();
            (await PostAdmin(own,"/devices",new{OperationId=s06Device,Device=deviceInput with{Number="HTTP-S06",Profile="zk-standalone",Host="127.0.0.1",Port=4370}})).EnsureSuccessStatusCode();
            byte[] s06Version;
            using(var json=JsonDocument.Parse(await own.GetStringAsync("/devices/"+s06Device)))s06Version=json.RootElement.GetProperty("version").GetBytesFromBase64();
            var downloadId=Guid.NewGuid();var downloadPayload=new{OperationId=downloadId,DeviceId=s06Device,ExpectedDeviceVersion=s06Version,TimeoutSeconds=30};
            check((await anon.GetAsync("/device-user-downloads")).StatusCode==HttpStatusCode.Unauthorized&&
                  (await own.GetAsync("/device-user-downloads")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.PostAsJsonAsync("/device-user-downloads",downloadPayload)).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S07 user downloads require authentication independent permission and CSRF");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.users.download"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-user-downloads",downloadPayload)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-user-downloads",downloadPayload)).StatusCode==HttpStatusCode.OK&&
                  (await own.GetAsync("/device-user-downloads")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.GetAsync("/device-user-downloads/"+downloadId+"/preview")).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S07 download grant permits exact retry without granting retained user or mapping preview reads");
            check((await PostAdmin(own,"/device-user-downloads",new DeviceUserDownloadInput(Guid.NewGuid(),s06Device,s06Version,30,"auto"))).StatusCode==HttpStatusCode.BadRequest&&
                  (await PostAdmin(own,"/device-user-downloads",new DeviceUserDownloadInput(downloadId,s06Device,s06Version,30,"legacy-int32-v1"))).StatusCode==HttpStatusCode.Conflict,
                  "HTTP S07 read format must be explicit and cannot change under an existing default-SSR intent");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.users.read"})).EnsureSuccessStatusCode();
            using(var downloads=JsonDocument.Parse(await own.GetStringAsync("/device-user-downloads?deviceId="+s06Device)))
            {
                var download=downloads.RootElement.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==downloadId);
                check((await own.GetAsync("/help/topic/device-users")).StatusCode==HttpStatusCode.OK&&
                      (await own.GetAsync("/device-user-downloads/"+downloadId+"/preview")).StatusCode==HttpStatusCode.OK&&
                      (await PostAdmin(own,"/device-user-downloads/"+downloadId+"/cancel",new{ExpectedVersion=download.GetProperty("version").GetBytesFromBase64()})).StatusCode==HttpStatusCode.OK,
                      "HTTP S07 read grant enables protected help and real preview while cancellation is versioned pending-only");
            }
            var legacyDownloadId=Guid.NewGuid();
            (await PostAdmin(own,"/device-user-downloads",new DeviceUserDownloadInput(legacyDownloadId,s06Device,s06Version,30,"legacy-int32-v1"))).EnsureSuccessStatusCode();
            using(var legacyDownloads=JsonDocument.Parse(await own.GetStringAsync("/device-user-downloads?deviceId="+s06Device)))
            {
                var legacyRow=legacyDownloads.RootElement.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==legacyDownloadId);
                check(legacyRow.GetProperty("readFormat").GetString()=="legacy-int32-v1"&&
                      (await PostAdmin(own,"/device-user-downloads/"+legacyDownloadId+"/cancel",new{ExpectedVersion=legacyRow.GetProperty("version").GetBytesFromBase64()})).StatusCode==HttpStatusCode.OK,
                      "HTTP S07 legacy read approval is durable queryable and pending-only cancellable with no implied upload grant");
            }
            foreach(var code in new[]{"devices.users.read","devices.users.download"})
                (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code=code})).EnsureSuccessStatusCode();
            using(var downloadCaps=JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities")))
                check((await own.GetAsync("/device-user-downloads/"+downloadId+"/preview")).StatusCode==HttpStatusCode.Forbidden&&
                      (await PostAdmin(own,"/device-user-downloads",downloadPayload)).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/help/topic/device-users")).StatusCode==HttpStatusCode.Forbidden&&
                      !downloadCaps.RootElement.GetProperty("pages").EnumerateArray().Any(x=>x.GetProperty("route").GetString()=="/workspace/device-users"),
                      "HTTP S07 grant revocation removes data help menu and request replay on the existing session");
            var uploadPayload=new IO.Infrastructure.Devices.DeviceUserUploads.Batch(Guid.NewGuid(),[],"UPLOAD-ORDINARY-USERS");
            check((await anon.GetAsync("/device-user-uploads")).StatusCode==HttpStatusCode.Unauthorized&&
                  (await own.GetAsync("/device-user-uploads")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.PostAsJsonAsync("/device-user-uploads/preview",uploadPayload)).StatusCode==HttpStatusCode.BadRequest&&
                  (await PostAdmin(own,"/device-user-uploads",uploadPayload)).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S08 upload preview and queue require authentication independent upload permission and CSRF");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.users.upload"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-user-uploads/preview",uploadPayload)).StatusCode==HttpStatusCode.BadRequest&&
                  (await own.GetAsync("/device-user-uploads")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.GetAsync("/help/topic/device-user-uploads")).StatusCode==HttpStatusCode.OK,
                  "HTTP S08 upload grant exposes protected help but still validates intents and never grants retained history reads");
            var validUploadItem=JsonSerializer.Deserialize<IO.Infrastructure.Devices.DeviceUserUploads.Item>(browserProcess.ArgumentList[5],new JsonSerializerOptions{PropertyNameCaseInsensitive=true})! with{Id=Guid.NewGuid()};
            var validUploadBatch=new IO.Infrastructure.Devices.DeviceUserUploads.Batch(Guid.NewGuid(),[validUploadItem],"UPLOAD-ORDINARY-USERS");
            var validUploadPreview=await PostAdmin(own,"/device-user-uploads/preview",validUploadBatch);validUploadPreview.EnsureSuccessStatusCode();
            using(var validPreviewJson=JsonDocument.Parse(await validUploadPreview.Content.ReadAsStringAsync()))
            {
                var plan=validPreviewJson.RootElement.GetProperty("result");
                validUploadBatch=validUploadBatch with{PreviewHash=plan.GetProperty("previewHash").GetString()};
                var queuedUpload=await PostAdmin(own,"/device-user-uploads",validUploadBatch);
                await using var uploadReceiptScope=provider.CreateAsyncScope();
                var uploadReceipt=await uploadReceiptScope.ServiceProvider.GetRequiredService<IoDbContext>().DeviceUserUploads.AsNoTracking().SingleAsync(x=>x.Id==validUploadItem.Id);
                check(plan.GetProperty("items")[0].GetProperty("desired").GetProperty("name").GetString()=="Ordinary employee"&&
                      queuedUpload.StatusCode==HttpStatusCode.OK&&uploadReceipt.State=="pending"&&
                      (await PostAdmin(own,"/device-user-uploads/"+validUploadItem.Id+"/cancel",new{ExpectedVersion=uploadReceipt.Version})).StatusCode==HttpStatusCode.OK,
                      "HTTP S08 preview returns the actual authenticated response envelope and exact confirmed plan queues and cancels a real SQL intent");
            }
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.users.read"})).EnsureSuccessStatusCode();
            check((await own.GetAsync("/device-user-uploads")).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-user-uploads/"+Guid.NewGuid()+"/review",new{ExpectedVersion=s06Version,SnapshotId=downloadId,Confirmation="not-confirmed"})).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S08 read grant enables real SQL history while unknown review retains explicit confirmation validation");
            foreach(var code in new[]{"devices.users.read","devices.users.upload"})
                (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code=code})).EnsureSuccessStatusCode();
            using(var uploadCaps=JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities")))
                check((await own.GetAsync("/device-user-uploads")).StatusCode==HttpStatusCode.Forbidden&&
                      (await PostAdmin(own,"/device-user-uploads/preview",uploadPayload)).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/help/topic/device-user-uploads")).StatusCode==HttpStatusCode.Forbidden&&
                      !uploadCaps.RootElement.GetProperty("pages").EnumerateArray().Any(x=>x.GetProperty("route").GetString()=="/workspace/device-user-uploads"),
                      "HTTP S08 grant revocation removes personal history help menu and preview on the existing session");
            var logId=Guid.NewGuid();var logPayload=new DeviceAttendanceInput(logId,s06Device,s06Version,30,"2026-11-08T00:00:00Z","2026-11-09T00:00:00Z",4,5);
            check((await anon.GetAsync("/device-attendance-downloads")).StatusCode==HttpStatusCode.Unauthorized&&
                  (await own.GetAsync("/device-attendance-downloads")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.PostAsJsonAsync("/device-attendance-downloads",logPayload)).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S09 SDK extraction requires authentication independent permission and CSRF");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.attendance.extract"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-attendance-downloads",logPayload with{FromUtc="2026-11-08T00:00:00"})).StatusCode==HttpStatusCode.BadRequest&&
                  (await PostAdmin(own,"/device-attendance-downloads",logPayload)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-attendance-downloads",logPayload)).StatusCode==HttpStatusCode.OK&&
                  (await own.GetAsync("/device-attendance-downloads/"+logId+"/rows")).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S09 extraction permission grants exact queue retry but never retained personal tuple reads");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.attendance.read"})).EnsureSuccessStatusCode();
            using(var logHistory=JsonDocument.Parse(await own.GetStringAsync("/device-attendance-downloads?deviceId="+s06Device)))
            {
                var logJob=logHistory.RootElement.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==logId);
                check((await own.GetAsync("/help/topic/device-attendance")).StatusCode==HttpStatusCode.OK&&
                      (await own.GetAsync("/device-attendance-downloads/"+logId+"/rows")).StatusCode==HttpStatusCode.OK&&
                      (await PostAdmin(own,"/device-attendance-downloads/"+logId+"/cancel",new{ExpectedVersion=logJob.GetProperty("version").GetBytesFromBase64()})).StatusCode==HttpStatusCode.OK,
                      "HTTP S09 read grant exposes the real retained preview help and versioned pending cancellation");
            }
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.attendance.commit"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-attendance-downloads/"+logId+"/commit-page",new{ExpectedVersion=s06Version,After=0,Confirmation="INGEST-SDK-ROWS"})).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S09 commit permission alone cannot bypass independent attendance receive authority");
            foreach(var code in new[]{"devices.attendance.read","devices.attendance.extract","devices.attendance.commit"})
                (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code=code})).EnsureSuccessStatusCode();
            using(var logCaps=JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities")))
                check((await own.GetAsync("/device-attendance-downloads/"+logId+"/rows")).StatusCode==HttpStatusCode.Forbidden&&
                      (await PostAdmin(own,"/device-attendance-downloads",logPayload)).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/help/topic/device-attendance")).StatusCode==HttpStatusCode.Forbidden&&
                      !logCaps.RootElement.GetProperty("pages").EnumerateArray().Any(x=>x.GetProperty("route").GetString()=="/workspace/device-attendance"),
                      "HTTP S09 revocation removes original tuples help menu and request retries without re-login");
            var pollId=Guid.NewGuid();var pollPayload=new DevicePollInput(pollId,s06Device,s06Version,"2026-11-08T00:00:00Z",300,3600,60,30,4,5,false,"ENABLE-SCHEDULED-SDK-READ",900);
            check((await anon.GetAsync("/device-poll-schedules")).StatusCode==HttpStatusCode.Unauthorized&&
                  (await own.GetAsync("/device-poll-schedules")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.PostAsJsonAsync("/device-poll-schedules",pollPayload)).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S10 schedule endpoints require authentication separate server grant and CSRF");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.schedules.manage"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-poll-schedules",pollPayload)).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S10 manage alone cannot approve extraction or automatic attendance ingestion");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.attendance.extract"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-poll-schedules",pollPayload with{OverlapSeconds=-1})).StatusCode==HttpStatusCode.BadRequest&&
                  (await PostAdmin(own,"/device-poll-schedules",pollPayload with{OverlapSeconds=3601})).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S10 explicit overlap rejects negative or larger-than-window reads before schedule creation");
            check((await PostAdmin(own,"/device-poll-schedules",pollPayload with{StartUtc="2026-11-08T00:00:00"})).StatusCode==HttpStatusCode.BadRequest&&
                  (await PostAdmin(own,"/device-poll-schedules",pollPayload)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-poll-schedules",pollPayload)).StatusCode==HttpStatusCode.OK&&
                  (await own.GetAsync("/device-poll-schedules")).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S10 exact approved schedule replay queues once and manage never implies history read");
            check((await PostAdmin(own,"/device-poll-schedules",pollPayload with{OverlapSeconds=0})).StatusCode==HttpStatusCode.Conflict,
                  "HTTP S10 unknown-response replay cannot silently change an approved overlap contract");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.schedules.read"})).EnsureSuccessStatusCode();
            using(var polls=JsonDocument.Parse(await own.GetStringAsync("/device-poll-schedules")))
            {
                var poll=polls.RootElement.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==pollId);
                check(poll.GetProperty("overlapSeconds").GetInt32()==900&&(await own.GetAsync("/help/topic/device-schedules")).StatusCode==HttpStatusCode.OK&&
                      (await own.GetAsync("/device-poll-schedules/"+pollId+"/runs")).StatusCode==HttpStatusCode.OK&&
                      (await PostAdmin(own,"/device-poll-schedules/"+pollId+"/enabled",new DevicePollToggle(poll.GetProperty("version").GetBytesFromBase64(),false,"PAUSE-SCHEDULE"))).StatusCode==HttpStatusCode.OK,
                      "HTTP S10 independent read exposes retained SQL history help and versioned pause");
            }
            foreach(var code in new[]{"devices.schedules.read","devices.schedules.manage","devices.attendance.extract"})
                (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code=code})).EnsureSuccessStatusCode();
            using(var pollCaps=JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities")))
                check((await own.GetAsync("/device-poll-schedules")).StatusCode==HttpStatusCode.Forbidden&&
                      (await PostAdmin(own,"/device-poll-schedules",pollPayload)).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/help/topic/device-schedules")).StatusCode==HttpStatusCode.Forbidden&&
                      !pollCaps.RootElement.GetProperty("pages").EnumerateArray().Any(x=>x.GetProperty("route").GetString()=="/workspace/device-schedules"),
                      "HTTP S10 existing session loses schedule menu help retained history and creation after server revoke");
            var commandInput=new IO.Infrastructure.Devices.DeviceCommands.RetryInput(Guid.NewGuid(),"operation",Guid.NewGuid(),s06Version,s06Version,"RETRY-READ-ONLY-COMMAND");
            check((await anon.GetAsync("/device-commands")).StatusCode==HttpStatusCode.Unauthorized&&
                  (await own.GetAsync("/device-commands")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.PostAsJsonAsync("/device-commands/retries",commandInput)).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S11 command endpoints require authentication independent grants and CSRF");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.commands.retry"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-commands/retries",commandInput)).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S11 generic retry alone cannot bypass command history or typed read authority");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.commands.read"})).EnsureSuccessStatusCode();
            check((await own.GetAsync("/device-commands?deviceId="+s06Device)).StatusCode==HttpStatusCode.OK&&
                  (await own.GetAsync("/help/topic/device-commands")).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-commands/retries",commandInput with{Queue="users-upload"})).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S11 independent read exposes actual history and help but arbitrary or write queue retry is rejected");
            foreach(var code in new[]{"devices.commands.read","devices.commands.retry"})
                (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code=code})).EnsureSuccessStatusCode();
            using(var commandCaps=JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities")))
                check((await own.GetAsync("/device-commands")).StatusCode==HttpStatusCode.Forbidden&&
                      (await PostAdmin(own,"/device-commands/retries",commandInput)).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/help/topic/device-commands")).StatusCode==HttpStatusCode.Forbidden&&
                      !commandCaps.RootElement.GetProperty("pages").EnumerateArray().Any(x=>x.GetProperty("route").GetString()=="/workspace/device-commands"),
                      "HTTP S11 revoke removes existing session command history retry menu and help");
            check((await anon.GetAsync("/device-health")).StatusCode==HttpStatusCode.Unauthorized&&
                  (await own.GetAsync("/device-health")).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S12 health observations require an authenticated independent server grant");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.health.read"})).EnsureSuccessStatusCode();
            using(var health=JsonDocument.Parse(await own.GetStringAsync("/device-health?deviceId="+s06Device)))
                check(health.RootElement.GetProperty("items").GetArrayLength()==1&&
                      (await own.GetAsync("/device-health?serviceOffset=-1")).StatusCode==HttpStatusCode.BadRequest&&
                      (await own.GetAsync("/help/topic/device-health")).StatusCode==HttpStatusCode.OK,
                      "HTTP S12 health returns actual filtered SQL metadata validates both page cursors and exposes grant-filtered help");
            (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code="devices.health.read"})).EnsureSuccessStatusCode();
            check((await own.GetAsync("/device-health")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.GetAsync("/help/topic/device-health")).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S12 health grant revocation denies retained metadata and protected help on the next request");
            var settingsId=Guid.NewGuid();var settingsPayload=new DeviceSettingsInput(settingsId,s06Device,s06Version,30,"READ-ADVANCED-SETTINGS");
            check((await anon.GetAsync("/device-settings-snapshots")).StatusCode==HttpStatusCode.Unauthorized&&
                  (await own.GetAsync("/device-settings-snapshots")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.PostAsJsonAsync("/device-settings-snapshots",settingsPayload)).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S11 advanced settings need authentication separate server permission and CSRF");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.settings.read"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-settings-snapshots",settingsPayload with{Confirmation="guess"})).StatusCode==HttpStatusCode.BadRequest&&
                  (await PostAdmin(own,"/device-settings-snapshots",settingsPayload)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-settings-snapshots",settingsPayload)).StatusCode==HttpStatusCode.OK&&
                  (await own.GetAsync("/help/topic/device-settings")).StatusCode==HttpStatusCode.OK,
                  "HTTP S11 explicit settings read confirmation queues one actual SQL intent on uncertain exact replay and exposes role-filtered help");
            using(var settingsHistory=JsonDocument.Parse(await own.GetStringAsync("/device-settings-snapshots?deviceId="+s06Device)))
            {
                var settingsRow=settingsHistory.RootElement.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==settingsId);
                check((await PostAdmin(own,"/device-operations/"+settingsId+"/cancel",new DeviceOperationVersion(settingsRow.GetProperty("version").GetBytesFromBase64()))).StatusCode==HttpStatusCode.OK&&
                      (await own.GetAsync("/device-operations")).StatusCode==HttpStatusCode.Forbidden,
                      "HTTP S11 settings permission supports versioned pending cancellation but never grants ordinary information or clock rights");
            }
            (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code="devices.settings.read"})).EnsureSuccessStatusCode();
            using(var settingsCaps=JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities")))
                check((await own.GetAsync("/device-settings-snapshots")).StatusCode==HttpStatusCode.Forbidden&&
                      (await PostAdmin(own,"/device-settings-snapshots",settingsPayload)).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/help/topic/device-settings")).StatusCode==HttpStatusCode.Forbidden&&
                      !settingsCaps.RootElement.GetProperty("pages").EnumerateArray().Any(x=>x.GetProperty("route").GetString()=="/workspace/device-settings"),
                      "HTTP S11 revoke removes advanced settings data requests protected help and menu without re-login");
            var idlePreviewPayload=new DeviceIdlePreview(s06Device,s06Version,settingsId,10,"http-model-evidence");
            check((await PostAdmin(own,"/device-settings/idle-preview",idlePreviewPayload)).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.PostAsJsonAsync("/device-settings/idle-preview",idlePreviewPayload)).StatusCode==HttpStatusCode.BadRequest&&
                  (await PostAdmin(own,"/device-operations/"+settingsId+"/resolve-idle",new DeviceIdleResolution(s06Version,settingsId,"REVIEWED-IDLE-MINUTES"))).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S11 idle preview and review require separate human grant and CSRF");
            foreach(var code in new[]{"devices.settings.read","devices.settings.idle.set"})
                (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code=code})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-settings/idle-preview",idlePreviewPayload)).StatusCode==HttpStatusCode.Conflict&&
                  (await PostAdmin(own,"/device-settings/idle-preview",idlePreviewPayload with{Minutes=0})).StatusCode==HttpStatusCode.BadRequest&&
                  (await own.GetAsync("/help/topic/device-settings")).StatusCode==HttpStatusCode.OK,
                  "HTTP S11 idle grant cannot turn a cancelled read or disabled-zero guess into an approved preview");
            foreach(var code in new[]{"devices.settings.read","devices.settings.idle.set"})
                (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code=code})).EnsureSuccessStatusCode();
            using(var idleCaps=JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities")))
                check((await PostAdmin(own,"/device-settings/idle-preview",idlePreviewPayload)).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/help/topic/device-settings")).StatusCode==HttpStatusCode.Forbidden&&
                      !idleCaps.RootElement.GetProperty("pages").EnumerateArray().Any(x=>x.GetProperty("route").GetString()=="/workspace/device-settings"),
                      "HTTP S11 idle revoke removes preview protected help and menu on the same session");
            var fileId=Guid.NewGuid();var filePayload=new IO.Infrastructure.Devices.DeviceFileImports.Input(fileId,s06Device,s06Version,"gregorian",1,4,5,Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("009	2026-11-08 08:00:00	1	4	1")));
            check((await anon.GetAsync("/device-file-imports")).StatusCode==HttpStatusCode.Unauthorized&&
                  (await own.GetAsync("/device-file-imports")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.PostAsJsonAsync("/device-file-imports",filePayload)).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S12 file import endpoints require authentication independent grants and CSRF");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.files.preview"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-file-imports",filePayload)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-file-imports",filePayload)).StatusCode==HttpStatusCode.OK&&
                  (await own.GetAsync("/device-file-imports/"+fileId+"/original")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.GetAsync("/device-file-imports/"+fileId+"/rows")).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S12 preview grant supports exact retry without granting original file or retained row reads");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.files.read"})).EnsureSuccessStatusCode();
            byte[] fileVersion;using(var fileRows=JsonDocument.Parse(await own.GetStringAsync("/device-file-imports/"+fileId+"/rows")))fileVersion=fileRows.RootElement.GetProperty("version").GetBytesFromBase64();
            check((await own.GetAsync("/help/topic/device-files")).StatusCode==HttpStatusCode.OK&&
                  (await own.GetAsync("/device-file-imports/"+fileId+"/original")).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-file-imports/"+fileId+"/commit-page",new{ExpectedVersion=fileVersion,After=0,Confirmation="IMPORT-READY-ROWS"})).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S12 read and help expose the real retained file without granting commit authority");
            foreach(var code in new[]{"devices.files.read","devices.files.preview"})
                (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code=code})).EnsureSuccessStatusCode();
            using(var fileCaps=JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities")))
                check((await own.GetAsync("/device-file-imports/"+fileId+"/original")).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/device-file-imports/"+fileId+"/rows")).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/help/topic/device-files")).StatusCode==HttpStatusCode.Forbidden&&
                      (await PostAdmin(own,"/device-file-imports",filePayload)).StatusCode==HttpStatusCode.Forbidden&&
                      !fileCaps.RootElement.GetProperty("pages").EnumerateArray().Any(x=>x.GetProperty("route").GetString()=="/workspace/device-files"),
                      "HTTP S12 revocation removes original bytes parsed data help menu and preview retries on the existing session");
            var infoOperation=Guid.NewGuid();
            var infoPayload=new{OperationId=infoOperation,DeviceId=s06Device,ExpectedDeviceVersion=s06Version,Kind="information.read",TimeoutSeconds=5,TargetUtc=(DateTimeOffset?)null,Confirmation=(string?)null};
            check((await anon.GetAsync("/device-operations")).StatusCode==HttpStatusCode.Unauthorized&&
                  (await own.GetAsync("/device-operations")).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.PostAsJsonAsync("/device-operations",infoPayload)).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S06 operation endpoints require authentication independent permission and antiforgery");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.info.read"})).EnsureSuccessStatusCode();
            var clockOperation=Guid.NewGuid();
            var s06ClockPayload=new{OperationId=clockOperation,DeviceId=s06Device,ExpectedDeviceVersion=s06Version,Kind="clock.set",TimeoutSeconds=5,TargetUtc=new DateTimeOffset(2026,11,8,10,0,0,TimeSpan.Zero),Confirmation="SET-CLOCK"};
            check((await PostAdmin(own,"/device-operations",infoPayload)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-operations",infoPayload)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-operations",s06ClockPayload)).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.GetAsync("/help/topic/device-information")).StatusCode==HttpStatusCode.OK,
                  "HTTP S06 information grant enables exact read retry and protected help without clock-setting rights");
            byte[] s06InfoVersion;
            using(var json=JsonDocument.Parse(await own.GetStringAsync("/device-operations?deviceId="+s06Device)))s06InfoVersion=json.RootElement.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==infoOperation).GetProperty("version").GetBytesFromBase64();
            (await PostAdmin(own,"/device-operations/"+infoOperation+"/cancel",new{ExpectedVersion=s06InfoVersion})).EnsureSuccessStatusCode();
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.clock.set"})).EnsureSuccessStatusCode();
            var missingClockAck=(await PostAdmin(own,"/device-operations",new{OperationId=clockOperation,DeviceId=s06Device,ExpectedDeviceVersion=s06Version,Kind="clock.set",TimeoutSeconds=5,TargetUtc=s06ClockPayload.TargetUtc,Confirmation=""})).StatusCode;
            (await PostAdmin(own,"/device-operations",s06ClockPayload)).EnsureSuccessStatusCode();
            byte[] s06ClockVersion;
            using(var json=JsonDocument.Parse(await own.GetStringAsync("/device-operations?deviceId="+s06Device)))s06ClockVersion=json.RootElement.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==clockOperation).GetProperty("version").GetBytesFromBase64();
            var firstCancel=(await PostAdmin(own,"/device-operations/"+clockOperation+"/cancel",new{ExpectedVersion=s06ClockVersion})).StatusCode;
            check(missingClockAck==HttpStatusCode.BadRequest&&firstCancel==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-operations/"+clockOperation+"/cancel",new{ExpectedVersion=s06ClockVersion})).StatusCode==HttpStatusCode.Conflict,
                  "HTTP S06 clock grant still requires explicit confirmation and only current pending work can be cancelled");
            var restartId=Guid.NewGuid();
            var restartPayload=new{OperationId=restartId,DeviceId=s06Device,ExpectedDeviceVersion=s06Version,Kind="device.restart",TimeoutSeconds=5,TargetUtc=(DateTimeOffset?)null,Confirmation="RESTART-DEVICE"};
            check((await PostAdmin(own,"/device-operations",restartPayload)).StatusCode==HttpStatusCode.Forbidden&&
                  (await anon.GetAsync("/device-operations")).StatusCode==HttpStatusCode.Unauthorized&&
                  (await own.PostAsJsonAsync("/device-operations",restartPayload)).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S11 clock and read grants never imply restart and antiforgery remains required");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.restart"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-operations",new{OperationId=restartId,DeviceId=s06Device,ExpectedDeviceVersion=s06Version,Kind="device.restart",TimeoutSeconds=5,TargetUtc=(DateTimeOffset?)null,Confirmation="RESET-DEVICE"})).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S11 independently granted restart still requires its exact confirmation");
            (await PostAdmin(own,"/device-operations",restartPayload)).EnsureSuccessStatusCode();
            byte[] restartVersion;
            using(var json=JsonDocument.Parse(await own.GetStringAsync("/device-operations?deviceId="+s06Device)))restartVersion=json.RootElement.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==restartId).GetProperty("version").GetBytesFromBase64();
            (await PostAdmin(own,"/device-operations/"+restartId+"/cancel",new{ExpectedVersion=restartVersion})).EnsureSuccessStatusCode();
            (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code="devices.restart"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-operations",restartPayload)).StatusCode==HttpStatusCode.Forbidden&&
                  (await PostAdmin(own,"/device-operations/"+restartId+"/resolve-restart",new{ExpectedVersion=restartVersion,InformationId=infoOperation,Confirmation="REVIEWED-RESTART-REACHABILITY"})).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S11 revoking human restart grant blocks old registration replay and recovery on the existing session");
            var networkDevice=Guid.NewGuid();
            (await PostAdmin(own,"/devices",new{OperationId=networkDevice,Device=deviceInput with{Number="HTTP-S06-IP",Profile="zk-standalone",Host="10.40.50.60",Port=4370}})).EnsureSuccessStatusCode();
            byte[] networkDeviceVersion;
            using(var json=JsonDocument.Parse(await own.GetStringAsync("/devices/"+networkDevice)))networkDeviceVersion=json.RootElement.GetProperty("version").GetBytesFromBase64();
            var networkId=Guid.NewGuid();var networkPlan=new IO.DeviceContracts.ZkNetworkPlan("10.40.50.60","10.40.50.61","http-network-fixture");
            var networkPayload=new DeviceOperationInput(networkId,networkDevice,networkDeviceVersion,"network.ip.set",5,null,"SET-DEVICE-IP",networkPlan);
            check((await PostAdmin(own,"/device-operations",networkPayload)).StatusCode==HttpStatusCode.Forbidden&&
                  (await own.PostAsJsonAsync("/device-operations",networkPayload)).StatusCode==HttpStatusCode.BadRequest,
                  "HTTP S06 network write is not implied by information clock or restart and requires antiforgery");
            (await PostAdmin(adminClient,"/admin/role-permissions",new{RoleId=deviceRole,Code="devices.network.ip.set"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-operations",networkPayload with{Confirmation="SET-CLOCK"})).StatusCode==HttpStatusCode.BadRequest&&
                  (await PostAdmin(own,"/device-operations",networkPayload)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-operations",networkPayload)).StatusCode==HttpStatusCode.OK&&
                  (await PostAdmin(own,"/device-operations",networkPayload with{NetworkPlan=networkPlan with{NewAddress="10.40.50.62"}})).StatusCode==HttpStatusCode.Conflict,
                  "HTTP S06 approved network change requires exact consent and immutable old new serial plan across registration replay");
            byte[] networkVersion;
            using(var json=JsonDocument.Parse(await own.GetStringAsync("/device-operations?deviceId="+networkDevice)))networkVersion=json.RootElement.GetProperty("items")[0].GetProperty("version").GetBytesFromBase64();
            (await PostAdmin(own,"/device-operations/"+networkId+"/cancel",new{ExpectedVersion=networkVersion})).EnsureSuccessStatusCode();
            (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code="devices.network.ip.set"})).EnsureSuccessStatusCode();
            check((await PostAdmin(own,"/device-operations",networkPayload)).StatusCode==HttpStatusCode.Forbidden&&
                  (await PostAdmin(own,"/device-operations/"+networkId+"/resolve-network",new{ExpectedVersion=networkVersion,InformationId=infoOperation,Confirmation="REVIEWED-DEVICE-IP"})).StatusCode==HttpStatusCode.Forbidden&&
                  (await PostAdmin(own,"/device-operations",networkPayload with{OperationId=Guid.NewGuid(),Kind="network.inspect",Confirmation="INSPECT-DEVICE-IP",NetworkPlan=networkPlan with{ChangeId=networkId}})).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP S06 revoke removes old network replay and review and write grant never implies recovery-read rights");
            foreach(var code in new[]{"devices.info.read","devices.clock.set"})
                (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new{RoleId=deviceRole,Code=code})).EnsureSuccessStatusCode();
            using(var capabilities=JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities")))
                check((await own.GetAsync("/device-operations")).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/help/topic/device-information")).StatusCode==HttpStatusCode.Forbidden&&
                      (await PostAdmin(own,"/device-operations",s06ClockPayload)).StatusCode==HttpStatusCode.Forbidden&&
                      (await PostAdmin(own,"/device-operations/"+clockOperation+"/resolve-clock",new{ExpectedVersion=s06ClockVersion,InformationId=infoOperation,Confirmation="REVIEWED-CLOCK"})).StatusCode==HttpStatusCode.Forbidden&&
                      !capabilities.RootElement.GetProperty("pages").EnumerateArray().Any(x=>x.GetProperty("route").GetString()=="/workspace/device-information"),
                      "HTTP S06 revocation removes data help menu clock replay and review on the existing session");
            (await PostAdmin(adminClient,"/admin/user-roles/revoke",new{UserId=self,RoleId=deviceRole})).EnsureSuccessStatusCode();
            using(var noDeviceCaps=JsonDocument.Parse(await own.GetStringAsync("/workspace/capabilities")))
                check((await own.GetAsync("/devices")).StatusCode==HttpStatusCode.Forbidden&&
                      (await own.GetAsync("/help/topic/devices")).StatusCode==HttpStatusCode.Forbidden&&
                      !noDeviceCaps.RootElement.GetProperty("pages").EnumerateArray().Any(p=>p.GetProperty("route").GetString()=="/workspace/devices"),
                      "HTTP device role revocation removes data help and menu without re-login");
            foreach (var (roleId, code) in new[] { (testSelfRole, "correction.submit.self"), (testManagerRole, "correction.decide"), (testManagerRole, "correction.decide.scoped") })
                if ((await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = roleId, Code = code })).StatusCode != HttpStatusCode.OK)
                    throw new InvalidOperationException("Synthetic correction permission grant failed.");
            var policyInput = new { CompanyId = testCompany, ApproverUserId = manager, Name = "HTTP correction policy" };
            check((await PostAdmin(own, "/corrections/policies", policyInput)).StatusCode == HttpStatusCode.Forbidden &&
                  (await adminClient.PostAsJsonAsync("/corrections/policies", policyInput)).StatusCode == HttpStatusCode.BadRequest,
                "HTTP correction policy requires permission and antiforgery");
            var correctionPolicy = await CreateId("/corrections/policies", policyInput);
            var correctionInput = new { OperationId = Guid.NewGuid(), PolicyId = correctionPolicy, WorkDate = "2027-05-05", ProposedAt = "2027-05-05T08:00:00Z", PunchKind = 0, Reason = "test" };
            check((await own.PostAsJsonAsync("/corrections/requests", correctionInput)).StatusCode == HttpStatusCode.BadRequest &&
                  (await PostAdmin(own, "/corrections/requests", new { OperationId = Guid.NewGuid(), PolicyId = correctionPolicy, WorkDate = "2027-05-05", ProposedAt = "2027-05-05T08:00:00", PunchKind = 0, Reason = "test" })).StatusCode == HttpStatusCode.BadRequest,
                "HTTP correction requires antiforgery and an explicit timestamp offset");
            using var correctionResponse = await PostAdmin(own, "/corrections/requests", correctionInput);
            correctionResponse.EnsureSuccessStatusCode();
            using var correctionJson = JsonDocument.Parse(await correctionResponse.Content.ReadAsStringAsync());
            var correctionId = correctionJson.RootElement.GetProperty("result").GetGuid();
            using var retryResponse = await PostAdmin(own, "/corrections/requests", correctionInput);
            using var retryJson = JsonDocument.Parse(await retryResponse.Content.ReadAsStringAsync());
            check(retryResponse.StatusCode == HttpStatusCode.OK && retryJson.RootElement.GetProperty("result").GetGuid() == correctionId &&
                  (await PostAdmin(own, "/corrections/requests", new { correctionInput.OperationId, correctionInput.PolicyId, correctionInput.WorkDate, correctionInput.ProposedAt, correctionInput.PunchKind, Reason = "changed" })).StatusCode == HttpStatusCode.Conflict &&
                  (await PostAdmin(own, "/corrections/requests", new { correctionInput.PolicyId, correctionInput.WorkDate, correctionInput.ProposedAt, correctionInput.PunchKind, correctionInput.Reason })).StatusCode == HttpStatusCode.BadRequest,
                  "HTTP correction requires operation id and rejects changed idempotent replay");
            using var cancellableResponse = await PostAdmin(own, "/corrections/requests", new { OperationId = Guid.NewGuid(), PolicyId = correctionPolicy, WorkDate = "2027-05-07", ProposedAt = "2027-05-07T08:00:00Z", PunchKind = 0, Reason = "cancel independently" });
            cancellableResponse.EnsureSuccessStatusCode();
            using var cancellableJson = JsonDocument.Parse(await cancellableResponse.Content.ReadAsStringAsync());
            var cancellableId = cancellableJson.RootElement.GetProperty("result").GetGuid();
            async Task<Guid> SubmitSplitRequest(string day)
            {
                using var response = await PostAdmin(own, "/corrections/requests", new { OperationId = Guid.NewGuid(), PolicyId = correctionPolicy, WorkDate = day, ProposedAt = day + "T08:00:00Z", PunchKind = 0, Reason = "split decision" });
                response.EnsureSuccessStatusCode();
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return body.RootElement.GetProperty("result").GetGuid();
            }
            var splitApprovalId = await SubmitSplitRequest("2027-05-08");
            var splitRejectionId = await SubmitSplitRequest("2027-05-09");
            var readGrant = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testSelfRole, Code = "correction.requests.read.self" });
            var submitRevoke = await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = testSelfRole, Code = "correction.submit.self" });
            // The browser fixture also grants submit through a separate role; revoke it
            // temporarily to exercise union semantics without changing the login session.
            Guid browserSelfRole;
            await using (var scope = provider.CreateAsyncScope())
                browserSelfRole = (await scope.ServiceProvider.GetRequiredService<IoDbContext>().Roles.SingleAsync(x => x.Name == "browser correction self role")).Id;
            var browserRevoke = await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = browserSelfRole, Code = "correction.submit.self" });
            using var readOnlyResponse = await own.GetAsync("/corrections/own?employeeId=" + second);
            var readOnlyJson = await readOnlyResponse.Content.ReadAsStringAsync();
            var readOnlySubmit = await PostAdmin(own, "/corrections/requests", correctionInput);
            var readOnlyCancel = await PostAdmin(own, $"/corrections/requests/{correctionId}/cancel", new { Reason = "denied" });
            var readOnlyPolicies = await own.GetAsync("/corrections/policies");
            var cancelGrant = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testSelfRole, Code = "correction.cancel.self" });
            using var cancelEnabled = await own.GetAsync("/corrections/own");
            var cancelEnabledText = await cancelEnabled.Content.ReadAsStringAsync();
            var cancelWithoutSubmit = await PostAdmin(own, $"/corrections/requests/{cancellableId}/cancel", new { Reason = "cancel independently" });
            var stillNoSubmit = await PostAdmin(own, "/corrections/requests", correctionInput);
            var cannotCancelOthers = await PostAdmin(own, $"/corrections/requests/{Guid.NewGuid()}/cancel", new { Reason = "not mine" });
            var cancelRevoke = await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = testSelfRole, Code = "correction.cancel.self" });
            var cancellationAfterRevoke = await PostAdmin(own, $"/corrections/requests/{correctionId}/cancel", new { Reason = "revoked" });
            check(cancelGrant.StatusCode == HttpStatusCode.OK && cancelEnabled.StatusCode == HttpStatusCode.OK &&
                  cancelEnabledText.Contains("\"canCancel\":true", StringComparison.Ordinal) && cancelWithoutSubmit.StatusCode == HttpStatusCode.OK &&
                  stillNoSubmit.StatusCode == HttpStatusCode.Forbidden && cannotCancelOthers.StatusCode == HttpStatusCode.Forbidden &&
                  cancelRevoke.StatusCode == HttpStatusCode.OK && cancellationAfterRevoke.StatusCode == HttpStatusCode.Forbidden,
                  "HTTP cancellation grant acts on own requests without submission and revoke is immediate");
            var restore = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testSelfRole, Code = "correction.submit.self" });
            var browserRestore = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = browserSelfRole, Code = "correction.submit.self" });
            check(readGrant.StatusCode == HttpStatusCode.OK && submitRevoke.StatusCode == HttpStatusCode.OK && browserRevoke.StatusCode == HttpStatusCode.OK &&
                  readOnlyResponse.StatusCode == HttpStatusCode.OK && readOnlyJson.Contains(correctionId.ToString(), StringComparison.OrdinalIgnoreCase) &&
                  readOnlyJson.Contains("\"canCancel\":false", StringComparison.Ordinal) && readOnlySubmit.StatusCode == HttpStatusCode.Forbidden &&
                  readOnlyCancel.StatusCode == HttpStatusCode.Forbidden && readOnlyPolicies.StatusCode == HttpStatusCode.Forbidden &&
                  restore.StatusCode == HttpStatusCode.OK && browserRestore.StatusCode == HttpStatusCode.OK,
                  "HTTP own correction read-only grant cannot submit, cancel or list policies");
            using var filteredOwn = await own.GetAsync("/corrections/own?fromDate=2027-05-05&toDate=2027-05-05&state=0&employeeId=" + second);
            using var excludedOwn = await own.GetAsync("/corrections/own?fromDate=2027-05-06&toDate=2027-05-06&state=0");
            using var filteredJson = JsonDocument.Parse(await filteredOwn.Content.ReadAsStringAsync());
            using var excludedJson = JsonDocument.Parse(await excludedOwn.Content.ReadAsStringAsync());
            check(filteredOwn.StatusCode == HttpStatusCode.OK && filteredJson.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == correctionId) &&
                  filteredJson.RootElement.GetProperty("items").EnumerateArray().All(x => x.GetProperty("employeeId").GetGuid() == first) &&
                  excludedOwn.StatusCode == HttpStatusCode.OK && !excludedJson.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == correctionId) &&
                  (await own.GetAsync("/corrections/own?fromDate=2027-05-05")).StatusCode == HttpStatusCode.BadRequest &&
                  (await own.GetAsync("/corrections/own?fromDate=2027-05-06&toDate=2027-05-05")).StatusCode == HttpStatusCode.BadRequest &&
                  (await own.GetAsync("/corrections/own?state=4")).StatusCode == HttpStatusCode.BadRequest &&
                  (await own.GetAsync("/corrections/own?fromDate=not-a-date&toDate=2027-05-05")).StatusCode == HttpStatusCode.BadRequest &&
                  (await scoped.GetAsync("/corrections/own?fromDate=2027-05-05&toDate=2027-05-05")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP correction history filters are validated and never select another employee");
            var readInboxGrant = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testManagerRole, Code = "correction.inbox.read" });
            var decideRevoke = await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = testManagerRole, Code = "correction.decide" });
            using var filteredInbox = await scoped.GetAsync($"/corrections/inbox?employeeId={first}");
            using var deniedTargetInbox = await scoped.GetAsync($"/corrections/inbox?employeeId={second}");
            using var inboxJson = JsonDocument.Parse(await filteredInbox.Content.ReadAsStringAsync());
            using var deniedTargetJson = JsonDocument.Parse(await deniedTargetInbox.Content.ReadAsStringAsync());
            var readOnlyDecision = await PostAdmin(scoped, $"/corrections/requests/{correctionId}/decision", new { Approve = true, Reason = (string?)null });
            var readOnlyDecisionHistory = await scoped.GetAsync("/corrections/decisions");
            using var pickerResponse = await scoped.GetAsync("/corrections/inbox/employees?offset=0");
            using var pickerJson = JsonDocument.Parse(await pickerResponse.Content.ReadAsStringAsync());
            var pickerItems = pickerJson.RootElement.GetProperty("items").EnumerateArray().ToArray();
            check(pickerResponse.StatusCode == HttpStatusCode.OK && pickerItems.Count(x => x.GetProperty("id").GetGuid() == first) == 1 &&
                  pickerItems.All(x => x.GetProperty("id").GetGuid() != second) &&
                  pickerItems.Single(x => x.GetProperty("id").GetGuid() == first).GetProperty("displayName").GetString() == "First" &&
                  (await own.GetAsync("/corrections/inbox/employees")).StatusCode == HttpStatusCode.Forbidden &&
                  (await scoped.GetAsync("/corrections/inbox/employees?offset=-1")).StatusCode == HttpStatusCode.BadRequest,
                  "HTTP inbox employee picker exposes only assigned scoped personnel");
            var invalidInbox = await scoped.GetAsync("/corrections/inbox?employeeId=00000000-0000-0000-0000-000000000000");
            var malformedInbox = await scoped.GetAsync("/corrections/inbox?employeeId=invalid");
            var approvalGrant = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testManagerRole, Code = "correction.approve" });
            using var approvalOnlyResponse = await scoped.GetAsync("/corrections/inbox");
            using var approvalOnlyJson = JsonDocument.Parse(await approvalOnlyResponse.Content.ReadAsStringAsync());
            var rejectWithoutGrant = await PostAdmin(scoped, $"/corrections/requests/{splitRejectionId}/decision", new { Approve = false, Reason = "denied" });
            var approvedWithoutLegacy = await PostAdmin(scoped, $"/corrections/requests/{splitApprovalId}/decision", new { Approve = true, Reason = (string?)null });
            var approvalRevoke = await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = testManagerRole, Code = "correction.approve" });
            var rejectionGrant = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testManagerRole, Code = "correction.reject" });
            using var rejectionOnlyResponse = await scoped.GetAsync("/corrections/inbox");
            using var rejectionOnlyJson = JsonDocument.Parse(await rejectionOnlyResponse.Content.ReadAsStringAsync());
            var approveWithoutGrant = await PostAdmin(scoped, $"/corrections/requests/{splitRejectionId}/decision", new { Approve = true, Reason = (string?)null });
            var rejectedWithoutLegacy = await PostAdmin(scoped, $"/corrections/requests/{splitRejectionId}/decision", new { Approve = false, Reason = "not accepted" });
            var rejectionRevoke = await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = testManagerRole, Code = "correction.reject" });
            var historyGrant = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testManagerRole, Code = "correction.decisions.read.self" });
            using var separateHistory = await scoped.GetAsync("/corrections/decisions");
            var separateHistoryText = await separateHistory.Content.ReadAsStringAsync();
            var historyRevoke = await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = testManagerRole, Code = "correction.decisions.read.self" });
            var historyAfterRevoke = await scoped.GetAsync("/corrections/decisions");
            check(approvalGrant.StatusCode == HttpStatusCode.OK && approvalOnlyResponse.StatusCode == HttpStatusCode.OK &&
                  approvalOnlyJson.RootElement.GetProperty("canApprove").GetBoolean() && !approvalOnlyJson.RootElement.GetProperty("canReject").GetBoolean() &&
                  rejectWithoutGrant.StatusCode == HttpStatusCode.Forbidden && approvedWithoutLegacy.StatusCode == HttpStatusCode.OK && approvalRevoke.StatusCode == HttpStatusCode.OK &&
                  rejectionGrant.StatusCode == HttpStatusCode.OK && rejectionOnlyResponse.StatusCode == HttpStatusCode.OK &&
                  !rejectionOnlyJson.RootElement.GetProperty("canApprove").GetBoolean() && rejectionOnlyJson.RootElement.GetProperty("canReject").GetBoolean() &&
                  approveWithoutGrant.StatusCode == HttpStatusCode.Forbidden && rejectedWithoutLegacy.StatusCode == HttpStatusCode.OK && rejectionRevoke.StatusCode == HttpStatusCode.OK &&
                  historyGrant.StatusCode == HttpStatusCode.OK && separateHistory.StatusCode == HttpStatusCode.OK &&
                  separateHistoryText.Contains(splitApprovalId.ToString(), StringComparison.OrdinalIgnoreCase) && separateHistoryText.Contains(splitRejectionId.ToString(), StringComparison.OrdinalIgnoreCase) &&
                  historyRevoke.StatusCode == HttpStatusCode.OK && historyAfterRevoke.StatusCode == HttpStatusCode.Forbidden,
                  "HTTP approval, rejection and decision history permissions are independent and revocable");
            var restoredDecide = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testManagerRole, Code = "correction.decide" });
            var removedReadInbox = await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = testManagerRole, Code = "correction.inbox.read" });
            check(readInboxGrant.StatusCode == HttpStatusCode.OK && decideRevoke.StatusCode == HttpStatusCode.OK && filteredInbox.StatusCode == HttpStatusCode.OK &&
                  inboxJson.RootElement.GetProperty("canDecide").GetBoolean() == false &&
                  inboxJson.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == correctionId && x.GetProperty("employeeName").GetString() == "First") &&
                  deniedTargetInbox.StatusCode == HttpStatusCode.OK && !deniedTargetJson.RootElement.GetProperty("items").EnumerateArray().Any() &&
                  readOnlyDecision.StatusCode == HttpStatusCode.Forbidden && readOnlyDecisionHistory.StatusCode == HttpStatusCode.Forbidden &&
                  invalidInbox.StatusCode == HttpStatusCode.BadRequest && malformedInbox.StatusCode == HttpStatusCode.BadRequest &&
                  restoredDecide.StatusCode == HttpStatusCode.OK && removedReadInbox.StatusCode == HttpStatusCode.OK,
                  "HTTP inbox read-only grant preserves employee scope and cannot decide");
            using var sortedInbox = await scoped.GetAsync($"/corrections/inbox?employeeId={first}&sort=work-desc");
            using var sortedInboxJson = JsonDocument.Parse(await sortedInbox.Content.ReadAsStringAsync());
            check(sortedInbox.StatusCode == HttpStatusCode.OK && sortedInboxJson.RootElement.GetProperty("sort").GetString() == "work-desc" &&
                  sortedInboxJson.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == correctionId) &&
                  (await scoped.GetAsync("/corrections/inbox?sort=invalid")).StatusCode == HttpStatusCode.BadRequest &&
                  (await own.GetAsync("/corrections/inbox?sort=work-asc")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP inbox sort is validated and remains permissioned");
            using var datedInbox = await scoped.GetAsync("/corrections/inbox?fromDate=2027-05-05&toDate=2027-05-05");
            using var datedInboxJson = JsonDocument.Parse(await datedInbox.Content.ReadAsStringAsync());
            using var datedOutsideInbox = await scoped.GetAsync("/corrections/inbox?fromDate=2027-05-06&toDate=2027-05-30");
            using var datedOutsideJson = JsonDocument.Parse(await datedOutsideInbox.Content.ReadAsStringAsync());
            check(datedInbox.StatusCode == HttpStatusCode.OK &&
                  datedInboxJson.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == correctionId) &&
                  datedOutsideInbox.StatusCode == HttpStatusCode.OK && !datedOutsideJson.RootElement.GetProperty("items").EnumerateArray().Any() &&
                  (await scoped.GetAsync("/corrections/inbox?fromDate=2027-05-05")).StatusCode == HttpStatusCode.BadRequest &&
                  (await scoped.GetAsync("/corrections/inbox?fromDate=2027-05-06&toDate=2027-05-05")).StatusCode == HttpStatusCode.BadRequest &&
                  (await scoped.GetAsync("/corrections/inbox?fromDate=not-a-date&toDate=2027-05-05")).StatusCode == HttpStatusCode.BadRequest &&
                  (await own.GetAsync("/corrections/inbox?fromDate=2027-05-05&toDate=2027-05-05")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP inbox date filter narrows pending work dates and rejects invalid ranges");
            var editGrant = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testSelfRole, Code = "correction.edit.self" });
            var submitRevokeForEdit = await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = testSelfRole, Code = "correction.submit.self" });
            Guid browserSelfRoleForEdit;
            await using (var scope = provider.CreateAsyncScope())
                browserSelfRoleForEdit = (await scope.ServiceProvider.GetRequiredService<IoDbContext>().Roles.SingleAsync(x => x.Name == "browser correction self role")).Id;
            var browserRevokeForEdit = await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = browserSelfRoleForEdit, Code = "correction.submit.self" });
            using var editOnlyResponse = await PostAdmin(own, $"/corrections/requests/{correctionId}/edit", new { ProposedAt = "2027-05-05T09:30:00Z", PunchKind = 1, Reason = "edited once" });
            var editRevoke = await PostAdmin(adminClient, "/admin/role-permissions/revoke", new { RoleId = testSelfRole, Code = "correction.edit.self" });
            var editAfterRevoke = await PostAdmin(own, $"/corrections/requests/{correctionId}/edit", new { ProposedAt = "2027-05-05T10:30:00Z", PunchKind = 0, Reason = "revoked edit" });
            var submitRestoreForEdit = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testSelfRole, Code = "correction.submit.self" });
            var browserRestoreForEdit = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = browserSelfRoleForEdit, Code = "correction.submit.self" });
            var badEdit = await PostAdmin(own, $"/corrections/requests/{correctionId}/edit", new { ProposedAt = "2027-05-05T09:30:00", PunchKind = 1, Reason = "no offset" });
            check(editGrant.StatusCode == HttpStatusCode.OK && submitRevokeForEdit.StatusCode == HttpStatusCode.OK &&
                  browserRevokeForEdit.StatusCode == HttpStatusCode.OK && editOnlyResponse.StatusCode == HttpStatusCode.OK &&
                  editRevoke.StatusCode == HttpStatusCode.OK && editAfterRevoke.StatusCode == HttpStatusCode.Forbidden &&
                  submitRestoreForEdit.StatusCode == HttpStatusCode.OK && browserRestoreForEdit.StatusCode == HttpStatusCode.OK &&
                  badEdit.StatusCode == HttpStatusCode.BadRequest,
                  "HTTP own pending edit is independent, revocable and validated");
            var inboxStatus = (await scoped.GetAsync("/corrections/inbox")).StatusCode;
            var ownDecisionStatus = (await PostAdmin(own, $"/corrections/requests/{correctionId}/decision", new { Approve = true, Reason = (string?)null })).StatusCode;
            var managerDecisionStatus = (await PostAdmin(scoped, $"/corrections/requests/{correctionId}/decision", new { Approve = true, Reason = (string?)null })).StatusCode;
            var lateCancelStatus = (await PostAdmin(own, $"/corrections/requests/{correctionId}/cancel", new { Reason = "late" })).StatusCode;
            var ownHistoryStatus = (await own.GetAsync("/corrections/own")).StatusCode;
            check(inboxStatus == HttpStatusCode.OK && ownDecisionStatus == HttpStatusCode.Forbidden && managerDecisionStatus == HttpStatusCode.OK &&
                  lateCancelStatus == HttpStatusCode.Conflict && ownHistoryStatus == HttpStatusCode.OK,
                $"HTTP scoped approval and own history enforce state and identity ({inboxStatus}, {ownDecisionStatus}, {managerDecisionStatus}, {lateCancelStatus}, {ownHistoryStatus})");
            var editDecided = await PostAdmin(own, $"/corrections/requests/{correctionId}/edit", new { ProposedAt = "2027-05-05T09:30:00Z", PunchKind = 1, Reason = "late edit" });
            check(editDecided.StatusCode == HttpStatusCode.Conflict, "HTTP editing a decided request conflicts");
            Guid expectedCorrectionDecision;
            await using(var reversalScope=provider.CreateAsyncScope())
                expectedCorrectionDecision=await reversalScope.ServiceProvider.GetRequiredService<IoDbContext>().CorrectionDecisions.Where(d=>d.RequestId==correctionId).Select(d=>d.Id).SingleAsync();
            var reversalPayload=new {OperationId=Guid.NewGuid(),ExpectedDecisionId=expectedCorrectionDecision,Reason="HTTP reconsider correction"};
            check((await PostAdmin(scoped,$"/corrections/requests/{correctionId}/reverse",reversalPayload)).StatusCode==HttpStatusCode.Forbidden &&
                  (await scoped.PostAsJsonAsync($"/corrections/requests/{correctionId}/reverse",reversalPayload)).StatusCode==HttpStatusCode.BadRequest &&
                  (await anon.GetAsync("/corrections/reversals")).StatusCode==HttpStatusCode.Unauthorized,
                  "HTTP reversal requires independent permission, authenticated session and antiforgery token");
            (await PostAdmin(adminClient,"/admin/role-permissions",new {RoleId=testManagerRole,Code="correction.reverse"})).EnsureSuccessStatusCode();
            (await PostAdmin(scoped,$"/corrections/requests/{correctionId}/reverse",reversalPayload)).EnsureSuccessStatusCode();
            (await PostAdmin(scoped,$"/corrections/requests/{correctionId}/reverse",reversalPayload)).EnsureSuccessStatusCode();
            using(var correctionHistoryResponse=await own.GetAsync($"/corrections/requests/{correctionId}/history"))
            using(var correctionHistoryJson=JsonDocument.Parse(await correctionHistoryResponse.Content.ReadAsStringAsync()))
                check(correctionHistoryResponse.IsSuccessStatusCode && correctionHistoryJson.RootElement.GetProperty("items")[0].GetProperty("reversalReason").GetString()==reversalPayload.Reason &&
                      (await PostAdmin(scoped,$"/corrections/requests/{correctionId}/decision",new {Approve=true,Reason="after reversal"})).StatusCode==HttpStatusCode.OK,
                      "HTTP reversal retry preserves own history and allows a fresh correction decision");
            (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new {RoleId=testManagerRole,Code="correction.reverse"})).EnsureSuccessStatusCode();
            check((await PostAdmin(scoped,$"/corrections/requests/{correctionId}/reverse",reversalPayload)).StatusCode==HttpStatusCode.Forbidden &&
                  (await scoped.GetAsync("/corrections/reversals")).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP reversal permission revocation denies replay and history on an existing session");
            var delegationToday = DateOnly.FromDateTime(DateTime.UtcNow);
            async Task<JsonDocument> ParseOrThrow(HttpResponseMessage response)
            {
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException("Deputy flow HTTP " + (int)response.StatusCode + " " + response.RequestMessage?.RequestUri + ": " + text);
                return JsonDocument.Parse(text);
            }
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
                var httpCompany = (await db.Employees.SingleAsync(x => x.Id == second)).CompanyId;
                var httpManagerEmployee = new EmployeeRecord { Id = Guid.NewGuid(), CompanyId = httpCompany, PersonnelNumber = "WEB-4", DisplayName = "Http Manager" };
                db.Employees.Add(httpManagerEmployee);
                (await db.Users.SingleAsync(x => x.Id == manager)).EmployeeId = httpManagerEmployee.Id;
                await db.SaveChangesAsync();
            }
            var receiveGrantHttp = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testManagerRole, Code = "delegation.receive" });
            var manageGrantHttp = await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testManagerRole, Code = "delegation.manage.self" });
            var deputyName = "web-deputy-" + Guid.NewGuid().ToString("N")[..8];
            using var deputyUserResponse = await PostAdmin(adminClient, "/admin/users", new { UserName = deputyName, Password = "Deputy!Password-2026x", EmployeeId = second });
            using var deputyUserJson = await ParseOrThrow(deputyUserResponse);
            var deputyUserId = deputyUserJson.RootElement.GetProperty("result").GetGuid();
            var deputyRoleGrant = await PostAdmin(adminClient, "/admin/user-roles", new { UserId = deputyUserId, RoleId = testManagerRole });
            var deputyScopeGrant = await PostAdmin(adminClient, "/admin/user-scopes", new { UserId = deputyUserId, UnitId = unit, ValidFrom = delegationToday.AddDays(-1), ValidUntilExclusive = (DateTime?)null });
            using var deputyPendingResponse = await PostAdmin(own, "/corrections/requests", new { OperationId = Guid.NewGuid(), PolicyId = correctionPolicy, WorkDate = "2027-05-10", ProposedAt = "2027-05-10T08:00:00Z", PunchKind = 0, Reason = "deputy flow" });
            using var deputyTargetJson = await ParseOrThrow(deputyPendingResponse);
            var deputyTarget = deputyTargetJson.RootElement.GetProperty("result").GetGuid();
            using var postDelegation = await PostAdmin(scoped, "/delegations", new { OperationId = Guid.NewGuid(), DelegateUserId = deputyUserId, ValidFrom = delegationToday, ValidUntilExclusive = delegationToday.AddDays(7) });
            using var delegationJson = await ParseOrThrow(postDelegation);
            var delegationId = delegationJson.RootElement.GetProperty("result").GetGuid();
            using var deputyClient = Client();
            await Login(deputyClient, deputyName, "Deputy!Password-2026x");
            var deputyPasswordChange = await PostAdmin(deputyClient, "/auth/change-password", new { CurrentPassword = "Deputy!Password-2026x", NewPassword = "Deputy!Password-2027x" });
            await Login(deputyClient, deputyName, "Deputy!Password-2027x");
            var preGrantInbox = await deputyClient.GetAsync("/corrections/inbox");
            var grantResponse = await PostAdmin(scoped, "/corrections/delegation-grants", new { DelegationId = delegationId });
            var grantResponseAgain = await PostAdmin(scoped, "/corrections/delegation-grants", new { DelegationId = delegationId });
            using var secondDeputyTargetResponse = await PostAdmin(own, "/corrections/requests", new { OperationId = Guid.NewGuid(), PolicyId = correctionPolicy, WorkDate = "2027-05-11", ProposedAt = "2027-05-11T08:00:00Z", PunchKind = 1, Reason = "after revoke" });
            using var secondDeputyTargetJson = await ParseOrThrow(secondDeputyTargetResponse);
            var secondDeputyTarget = secondDeputyTargetJson.RootElement.GetProperty("result").GetGuid();
            using var deputyInboxJson = await ParseOrThrow(await deputyClient.GetAsync("/corrections/inbox"));
            var deputyDecision = await PostAdmin(deputyClient, $"/corrections/requests/{deputyTarget}/decision", new { Approve = true, Reason = (string?)null });
            using var deputyHistoryJson = await ParseOrThrow(await deputyClient.GetAsync("/corrections/decisions"));
            var delegationRevoke = await PostAdmin(scoped, $"/delegations/{delegationId}/revoke", new { Reason = "http revoke" });
            var decisionAfterRevoke = await PostAdmin(deputyClient, $"/corrections/requests/{secondDeputyTarget}/decision", new { Approve = true, Reason = (string?)null });
            var deputyInboxHasTarget = deputyInboxJson.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == deputyTarget);
            var deputyHistoryHasTarget = deputyHistoryJson.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("requestId").GetGuid() == deputyTarget);
            check(receiveGrantHttp.StatusCode == HttpStatusCode.OK && manageGrantHttp.StatusCode == HttpStatusCode.OK &&
                  deputyUserResponse.StatusCode == HttpStatusCode.OK && deputyRoleGrant.StatusCode == HttpStatusCode.OK && deputyScopeGrant.StatusCode == HttpStatusCode.OK &&
                  deputyPasswordChange.StatusCode == HttpStatusCode.NoContent &&
                  deputyPendingResponse.StatusCode == HttpStatusCode.OK && secondDeputyTargetResponse.StatusCode == HttpStatusCode.OK && postDelegation.StatusCode == HttpStatusCode.OK &&
                  preGrantInbox.StatusCode == HttpStatusCode.OK && grantResponse.StatusCode == HttpStatusCode.OK && grantResponseAgain.StatusCode == HttpStatusCode.OK &&
                  deputyInboxHasTarget && deputyDecision.StatusCode == HttpStatusCode.OK && deputyHistoryHasTarget &&
                  delegationRevoke.StatusCode == HttpStatusCode.OK && decisionAfterRevoke.StatusCode == HttpStatusCode.Forbidden,
                  $"HTTP delegated approver grant authorizes the named deputy and revocation is immediate " +
                  $"(recv={receiveGrantHttp.StatusCode}, manage={manageGrantHttp.StatusCode}, user={deputyUserResponse.StatusCode}, role={deputyRoleGrant.StatusCode}, " +
                  $"scope={deputyScopeGrant.StatusCode}, pwd={deputyPasswordChange.StatusCode}, t1={deputyPendingResponse.StatusCode}, t2={secondDeputyTargetResponse.StatusCode}, " +
                  $"delegation={postDelegation.StatusCode}, preInbox={preGrantInbox.StatusCode}, grant={grantResponse.StatusCode}, grant2={grantResponseAgain.StatusCode}, " +
                  $"inboxHas={deputyInboxHasTarget}, decision={deputyDecision.StatusCode}, historyHas={deputyHistoryHasTarget}, revoke={delegationRevoke.StatusCode}, after={decisionAfterRevoke.StatusCode})");
            using var datedHistory = await scoped.GetAsync("/corrections/decisions?fromDate=2027-05-08&toDate=2027-05-08");
            using var datedHistoryJson = JsonDocument.Parse(await datedHistory.Content.ReadAsStringAsync());
            using var rejectionDayHistory = await scoped.GetAsync("/corrections/decisions?fromDate=2027-05-09&toDate=2027-05-09");
            using var rejectionDayJson = JsonDocument.Parse(await rejectionDayHistory.Content.ReadAsStringAsync());
            check(datedHistory.StatusCode == HttpStatusCode.OK &&
                  datedHistoryJson.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("requestId").GetGuid() == splitApprovalId) &&
                  !datedHistoryJson.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("requestId").GetGuid() == splitRejectionId) &&
                  rejectionDayHistory.StatusCode == HttpStatusCode.OK &&
                  rejectionDayJson.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("requestId").GetGuid() == splitRejectionId) &&
                  !rejectionDayJson.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("requestId").GetGuid() == splitApprovalId) &&
                  (await scoped.GetAsync("/corrections/decisions?fromDate=2027-05-08")).StatusCode == HttpStatusCode.BadRequest &&
                  (await scoped.GetAsync("/corrections/decisions?fromDate=2027-05-09&toDate=2027-05-08")).StatusCode == HttpStatusCode.BadRequest &&
                  (await own.GetAsync("/corrections/decisions?fromDate=2027-05-08&toDate=2027-05-08")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP decision history date filter narrows work dates and rejects invalid ranges");
            using var trackedResponse = await own.GetAsync("/corrections/own?fromDate=2027-05-05&toDate=2027-05-05");
            using var trackedJson = JsonDocument.Parse(await trackedResponse.Content.ReadAsStringAsync());
            var tracked = trackedJson.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == correctionId);
            var initiallyPending = filteredJson.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == correctionId);
            check(trackedResponse.StatusCode == HttpStatusCode.OK && tracked.GetProperty("policyName").GetString() == "HTTP correction policy" &&
                  tracked.GetProperty("approverUserId").GetGuid() == manager && tracked.GetProperty("decisionKind").GetInt32() == 1 &&
                  tracked.GetProperty("decisionActorUserId").GetGuid() == manager && tracked.GetProperty("decisionAtUtc").ValueKind == JsonValueKind.String &&
                  initiallyPending.GetProperty("decisionKind").ValueKind == JsonValueKind.Null &&
                  initiallyPending.GetProperty("decisionActorUserId").ValueKind == JsonValueKind.Null &&
                  !tracked.TryGetProperty("userName", out _) && !tracked.TryGetProperty("password", out _),
                  "HTTP own tracking exposes only the assigned approver and recorded decision");
            using var decisionHistory = await scoped.GetAsync("/corrections/decisions");
            check(decisionHistory.StatusCode == HttpStatusCode.OK &&
                  (await decisionHistory.Content.ReadAsStringAsync()).Contains(correctionId.ToString(), StringComparison.OrdinalIgnoreCase) &&
                  (await own.GetAsync("/corrections/decisions")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP approver decision history is scoped and permissioned");
            var newOrg = await CreateId("/org/organizations", new { Name = "HTTP synthetic org" });
            var newCompany = await CreateId("/org/companies", new { OrganizationId = newOrg, Name = "HTTP synthetic company" });
            var newSite = await CreateId("/org/sites", new { CompanyId = newCompany, Name = "HTTP site", TimeZoneId = "Asia/Tehran" });
            var rawSource = await CreateId("/attendance/sources", new { SiteId = newSite, Name = "HTTP synthetic source" });
            using var sourceListing = await adminClient.GetAsync("/attendance/sources?offset=0");
            var sourceListingText = await sourceListing.Content.ReadAsStringAsync();
            var rawEvent = new { SourceId = rawSource, ExternalId = "http-1", PersonId = "test-person", OccurredAt = "2026-01-01T08:00:00Z", SourceZone = "Asia/Tehran", EventType = "in", Payload = Convert.ToBase64String(new byte[] { 1, 2 }) };
            check((await PostAdmin(scoped, "/attendance/simulated-events", rawEvent)).StatusCode == HttpStatusCode.Forbidden &&
                  (await adminClient.PostAsJsonAsync("/attendance/simulated-events", rawEvent)).StatusCode == HttpStatusCode.BadRequest &&
                  (await scoped.GetAsync($"/attendance/raw/{rawSource}")).StatusCode == HttpStatusCode.Forbidden &&
                  (await scoped.GetAsync("/attendance/sources")).StatusCode == HttpStatusCode.Forbidden &&
                  (await adminClient.GetAsync("/attendance/sources?offset=-1")).StatusCode == HttpStatusCode.BadRequest &&
                  sourceListing.StatusCode == HttpStatusCode.OK && sourceListingText.Contains(rawSource.ToString(), StringComparison.OrdinalIgnoreCase),
                "HTTP raw ingestion and source listing require permission and CSRF");
            using var firstRaw = await PostAdmin(adminClient, "/attendance/simulated-events", rawEvent);
            using var retryRaw = await PostAdmin(adminClient, "/attendance/simulated-events", rawEvent);
            using var firstRawJson = JsonDocument.Parse(await firstRaw.Content.ReadAsStringAsync());
            using var retryRawJson = JsonDocument.Parse(await retryRaw.Content.ReadAsStringAsync());
            check(firstRaw.StatusCode == HttpStatusCode.OK && retryRaw.StatusCode == HttpStatusCode.OK &&
                  firstRawJson.RootElement.GetProperty("result").GetProperty("outcome").GetInt32() == 0 &&
                  retryRawJson.RootElement.GetProperty("result").GetProperty("outcome").GetInt32() == 1 &&
                  firstRawJson.RootElement.GetProperty("result").GetProperty("eventId").GetGuid() == retryRawJson.RootElement.GetProperty("result").GetProperty("eventId").GetGuid() &&
                  (await adminClient.GetAsync($"/attendance/raw/{rawSource}")).StatusCode == HttpStatusCode.OK,
                "HTTP raw event retry reports same recorded event and authorized list");
            check((await PostAdmin(adminClient, "/attendance/simulated-events", new { SourceId = rawSource, ExternalId = "http-1", PersonId = "test-person", OccurredAt = "2026-01-01T08:00:00Z", SourceZone = "Asia/Tehran", EventType = "in", Payload = Convert.ToBase64String(new byte[] { 9 }) })).StatusCode == HttpStatusCode.Conflict &&
                  (await PostAdmin(adminClient, "/attendance/simulated-events", new { SourceId = rawSource, ExternalId = "http-2", OccurredAt = "2026-01-01T08:00:00", Payload = "" })).StatusCode == HttpStatusCode.BadRequest,
                "HTTP raw identity collision and timestamp without offset fail closed");
            using var repeatedConflict = await PostAdmin(adminClient, "/attendance/simulated-events", new { SourceId = rawSource, ExternalId = "http-1", PersonId = "test-person", OccurredAt = "2026-01-01T08:00:00Z", SourceZone = "Asia/Tehran", EventType = "in", Payload = Convert.ToBase64String(new byte[] { 9 }) });
            using var conflictJson = JsonDocument.Parse(await repeatedConflict.Content.ReadAsStringAsync());
            var conflictId = conflictJson.RootElement.GetProperty("result").GetProperty("conflictId").GetGuid();
            using var conflictDetail = await adminClient.GetAsync($"/attendance/conflict/{conflictId}");
            check(repeatedConflict.StatusCode == HttpStatusCode.Conflict && conflictDetail.StatusCode == HttpStatusCode.OK &&
                  (await conflictDetail.Content.ReadAsStringAsync()).Contains("CQ==", StringComparison.Ordinal) &&
                  (await scoped.GetAsync($"/attendance/conflict/{conflictId}")).StatusCode == HttpStatusCode.Forbidden &&
                  (await adminClient.GetAsync($"/attendance/conflicts/{rawSource}")).StatusCode == HttpStatusCode.OK,
                "HTTP collision response identifies stored restricted payload for review");
            var conflictNote = new { Note = "HTTP synthetic review" };
            check((await PostAdmin(scoped, $"/attendance/conflict/{conflictId}/notes", conflictNote)).StatusCode == HttpStatusCode.Forbidden &&
                  (await adminClient.PostAsJsonAsync($"/attendance/conflict/{conflictId}/notes", conflictNote)).StatusCode == HttpStatusCode.BadRequest,
                  "HTTP conflict notes require permission and antiforgery");
            await CreateId($"/attendance/conflict/{conflictId}/notes", conflictNote);
            using var notesResponse = await adminClient.GetAsync($"/attendance/conflict/{conflictId}/notes");
            check(notesResponse.StatusCode == HttpStatusCode.OK &&
                  (await notesResponse.Content.ReadAsStringAsync()).Contains("\"resolved\":false", StringComparison.Ordinal) &&
                  (await scoped.GetAsync($"/attendance/conflict/{conflictId}/notes")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP conflict notes are restricted and do not resolve the conflict");
            using var notedConflicts = await adminClient.GetAsync($"/attendance/conflicts/{rawSource}");
            check((await notedConflicts.Content.ReadAsStringAsync()).Contains("\"noteCount\":1", StringComparison.Ordinal),
                  "HTTP collision list includes note count without resolving the collision");
            var newUnit = await CreateId("/org/units", new { CompanyId = newCompany, ParentId = (Guid?)null, Name = "HTTP unit" });
            var newEmployee = await CreateId("/org/employees", new { CompanyId = newCompany, PersonnelNumber = "HTTP-1", DisplayName = "HTTP synthetic employee" });
            var map = new { SourceId = rawSource, SourcePersonId = "test-person", EmployeeId = newEmployee, ValidFrom = "2026-01-01T00:00:00Z", ValidUntilExclusive = (string?)null };
            check((await PostAdmin(scoped, "/attendance/mappings", map)).StatusCode == HttpStatusCode.Forbidden &&
                  (await PostAdmin(adminClient, "/attendance/mappings", new { SourceId = rawSource, SourcePersonId = "test-person", EmployeeId = newEmployee, ValidFrom = "2026-01-01T00:00:00", ValidUntilExclusive = (string?)null })).StatusCode == HttpStatusCode.BadRequest &&
                  (await PostAdmin(adminClient, "/attendance/mappings", map)).StatusCode == HttpStatusCode.OK &&
                  (await PostAdmin(adminClient, "/attendance/mappings", map)).StatusCode == HttpStatusCode.Conflict &&
                  (await adminClient.GetAsync($"/attendance/resolution/{rawSource}")).StatusCode == HttpStatusCode.OK &&
                  (await scoped.GetAsync($"/attendance/resolution/{rawSource}")).StatusCode == HttpStatusCode.Forbidden,
                "HTTP identity mapping checks permission, UTC offset, overlap and review access");
            check((await adminClient.GetAsync($"/attendance/mappings/{rawSource}")).StatusCode == HttpStatusCode.OK &&
                  (await scoped.GetAsync($"/attendance/mappings/{rawSource}")).StatusCode == HttpStatusCode.Forbidden &&
                  (await adminClient.GetAsync($"/attendance/mappings/{rawSource}?offset=-1")).StatusCode == HttpStatusCode.BadRequest,
                  "HTTP mapping history requires ingestion permission and bounded paging");
            var reassignedEmployee = await CreateId("/org/employees", new { CompanyId = newCompany, PersonnelNumber = "HTTP-REMAP", DisplayName = "HTTP replacement employee" });
            using var mappingListResponse = await adminClient.GetAsync($"/attendance/mappings/{rawSource}");
            using var mappingList = JsonDocument.Parse(await mappingListResponse.Content.ReadAsStringAsync());
            var mapped = mappingList.RootElement.GetProperty("items")[0];
            var replaceMapping = new { SourceId = rawSource, SourcePersonId = "test-person", MappingId = mapped.GetProperty("id").GetGuid(),
                EmployeeId = reassignedEmployee, EffectiveAt = "2026-02-01T00:00:00Z", ExpectedVersion = mapped.GetProperty("version").GetString(), Reason = "HTTP reassignment" };
            check((await PostAdmin(scoped, "/attendance/mapping-replacements", replaceMapping)).StatusCode == HttpStatusCode.Forbidden &&
                  (await adminClient.PostAsJsonAsync("/attendance/mapping-replacements", replaceMapping)).StatusCode == HttpStatusCode.BadRequest &&
                  (await PostAdmin(adminClient, "/attendance/mapping-replacements", new { SourceId = rawSource, SourcePersonId = "test-person", MappingId = mapped.GetProperty("id").GetGuid(),
                      EmployeeId = reassignedEmployee, EffectiveAt = "2026-02-01T00:00:00", ExpectedVersion = mapped.GetProperty("version").GetString(), Reason = "invalid" })).StatusCode == HttpStatusCode.BadRequest,
                  "HTTP mapping replacement enforces permission, antiforgery and explicit timestamp");
            using var replaced = await PostAdmin(adminClient, "/attendance/mapping-replacements", replaceMapping);
            using var changes = await adminClient.GetAsync($"/attendance/mapping-changes/{rawSource}");
            check(replaced.StatusCode == HttpStatusCode.OK && changes.StatusCode == HttpStatusCode.OK &&
                  (await changes.Content.ReadAsStringAsync()).Contains("HTTP reassignment", StringComparison.Ordinal) &&
                  (await PostAdmin(adminClient, "/attendance/mapping-replacements", replaceMapping)).StatusCode == HttpStatusCode.Conflict &&
                  (await scoped.GetAsync($"/attendance/mapping-changes/{rawSource}")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP mapping replacement is audited, scoped and rejects stale replay");
            async Task<string> CatalogVersion(string kind, Guid id)
            {
                using var response = await adminClient.GetAsync("/org/catalog/" + kind);
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("items").EnumerateArray()
                    .Single(x => x.GetProperty("id").GetGuid() == id).GetProperty("version").GetString()!;
            }
            var employeeVersion = await CatalogVersion("employees", newEmployee);
            check((await PostAdmin(adminClient, $"/org/employees/{newEmployee}/rename", new { DisplayName = "HTTP changed", ExpectedVersion = employeeVersion })).StatusCode == HttpStatusCode.OK &&
                (await PostAdmin(adminClient, $"/org/employees/{newEmployee}/rename", new { DisplayName = "stale", ExpectedVersion = employeeVersion })).StatusCode == HttpStatusCode.Conflict,
                "HTTP employee update enforces optimistic rowversion");
            check((await PostAdmin(scoped, $"/org/employees/{newEmployee}/rename", new { DisplayName = "unauthorized", ExpectedVersion = employeeVersion })).StatusCode == HttpStatusCode.Forbidden &&
                (await PostAdmin(adminClient, $"/org/employees/{newEmployee}/rename", new { DisplayName = "invalid", ExpectedVersion = "not-base64" })).StatusCode == HttpStatusCode.BadRequest,
                "HTTP employee update rejects unauthorized and malformed version");
            var workDay = DateOnly.FromDateTime(DateTime.UtcNow);
            var siteInput = new { EmployeeId = newEmployee, SiteId = newSite, ValidFrom = workDay, ValidUntilExclusive = (DateOnly?)null };
            var profileInput = new { EmployeeId = newEmployee, StatusCode = "test-status", Title = "test title", ValidFrom = workDay, ValidUntilExclusive = (DateOnly?)null };
            check((await PostAdmin(scoped, "/org/site-assignments", siteInput)).StatusCode == HttpStatusCode.Forbidden &&
                  (await PostAdmin(scoped, "/org/employment-profiles", profileInput)).StatusCode == HttpStatusCode.Forbidden,
                "HTTP site and employment writes deny users without management permission");
            var firstSite = await CreateId("/org/site-assignments", siteInput);
            var firstProfile = await CreateId("/org/employment-profiles", profileInput);
            check((await PostAdmin(adminClient, "/org/site-assignments", siteInput)).StatusCode == HttpStatusCode.Conflict &&
                  (await PostAdmin(adminClient, "/org/employment-profiles", profileInput)).StatusCode == HttpStatusCode.Conflict,
                "HTTP overlapping site and employment history rejected");
            var nextSite = await CreateId("/org/sites", new { CompanyId = newCompany, Name = "HTTP second site", TimeZoneId = "Asia/Tehran" });
            var siteMove = new { EmployeeId = newEmployee, AssignmentId = firstSite, SiteId = nextSite, Effective = workDay.AddDays(1) };
            var profileChange = new { EmployeeId = newEmployee, ProfileId = firstProfile, StatusCode = "other-test-status", Title = (string?)null, Effective = workDay.AddDays(1) };
            await CreateId("/org/site-transfers", siteMove);
            await CreateId("/org/employment-changes", profileChange);
            check((await PostAdmin(adminClient, "/org/site-transfers", siteMove)).StatusCode == HttpStatusCode.Conflict &&
                  (await PostAdmin(adminClient, "/org/employment-changes", profileChange)).StatusCode == HttpStatusCode.Conflict &&
                  (await adminClient.GetAsync("/org/catalog/site-assignments")).StatusCode == HttpStatusCode.OK &&
                  (await adminClient.GetAsync("/org/catalog/employment-profiles")).StatusCode == HttpStatusCode.OK &&
                  (await scoped.GetAsync("/org/catalog/employment-profiles")).StatusCode == HttpStatusCode.Forbidden,
                "HTTP history changes close old records and catalogs require authorization");
            var initialOrgAssignment = await CreateId("/org/assignments", new { EmployeeId = newEmployee, UnitId = newUnit, ValidFrom = workDay, ValidUntilExclusive = (DateOnly?)null });
            check((await PostAdmin(adminClient, "/org/assignments", new { EmployeeId = newEmployee, UnitId = newUnit, ValidFrom = workDay, ValidUntilExclusive = (DateOnly?)null })).StatusCode == HttpStatusCode.Conflict,
                "HTTP duplicate employee assignment returns conflict");
            var newCalendar = await CreateId("/scheduling/calendars", new { CompanyId = newCompany, Name = "HTTP calendar" });
            await CreateId("/scheduling/calendar-days", new { CalendarId = newCalendar, Date = workDay, Kind = 0, Note = "synthetic" });
            var newShift = await CreateId("/scheduling/shifts", new { CompanyId = newCompany, SiteId = newSite, Name = "HTTP night", StartsAt = "22:00:00", EndsAt = "06:00:00", EndDayOffset = 1 });
            var initialWorkSchedule = await CreateId("/scheduling/assignments", new { EmployeeId = newEmployee, CalendarId = newCalendar, ShiftId = newShift, ValidFrom = workDay, ValidUntilExclusive = (DateOnly?)null });
            var ruleInput = new { CompanyId = newCompany, Name = "HTTP synthetic paired rule", BeforeMinutes = 10, AfterMinutes = 10 };
            check((await PostAdmin(scoped, "/calculations/rules", ruleInput)).StatusCode == HttpStatusCode.Forbidden &&
                  (await adminClient.PostAsJsonAsync("/calculations/rules", ruleInput)).StatusCode == HttpStatusCode.BadRequest,
                "HTTP calculation rules require explicit permission and CSRF");
            var calcRule = await CreateId("/calculations/rules", ruleInput);
            var httpRun = await CreateId("/calculations/runs", new { EmployeeId = newEmployee, WorkDate = workDay, SourceId = rawSource, RuleVersionId = calcRule });
            check((await adminClient.GetAsync($"/calculations/runs/{newEmployee}")).StatusCode == HttpStatusCode.OK &&
                  (await scoped.GetAsync($"/calculations/runs/{newEmployee}")).StatusCode == HttpStatusCode.Forbidden &&
                  (await adminClient.GetAsync($"/calculations/rules/{newCompany}")).StatusCode == HttpStatusCode.OK,
                "HTTP calculation runs and rules expose only authorized historical snapshots");
            using var revisionResponse = await adminClient.GetAsync($"/calculations/revision/{httpRun}");
            var revisionText = await revisionResponse.Content.ReadAsStringAsync();
            check(revisionResponse.StatusCode == HttpStatusCode.OK && revisionText.Contains("\"freshness\":\"unknown\"", StringComparison.Ordinal) &&
                  revisionText.Contains("databaseChange", StringComparison.Ordinal) &&
                  (await scoped.GetAsync($"/calculations/revision/{httpRun}")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP revision assessment is authorized and does not promise freshness");
            var comparison = await adminClient.GetAsync($"/calculations/compare/{httpRun}/{httpRun}");
            check(comparison.StatusCode == HttpStatusCode.OK && (await comparison.Content.ReadAsStringAsync()).Contains("\"freshness\":\"unknown\"", StringComparison.Ordinal) &&
                  (await scoped.GetAsync($"/calculations/compare/{httpRun}/{httpRun}")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP historical comparison remains permissioned and never asserts freshness");
            var draftInput = new { EmployeeId = newEmployee, Start = workDay, EndExclusive = workDay.AddDays(2) };
            check((await PostAdmin(scoped, "/periods/drafts", draftInput)).StatusCode == HttpStatusCode.Forbidden &&
                  (await adminClient.PostAsJsonAsync("/periods/drafts", draftInput)).StatusCode == HttpStatusCode.BadRequest,
                  "HTTP draft registration requires permission and CSRF");
            var draftId = await CreateId("/periods/drafts", draftInput);
            var retriedDraftId = await CreateId("/periods/drafts", draftInput);
            using var draftList = await adminClient.GetAsync($"/periods/drafts/{newEmployee}");
            check(draftId == retriedDraftId && draftList.StatusCode == HttpStatusCode.OK &&
                  (await draftList.Content.ReadAsStringAsync()).Contains("diagnostic-draft-only", StringComparison.Ordinal) &&
                  (await scoped.GetAsync($"/periods/drafts/{newEmployee}")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP period drafts retry idempotently and remain restricted diagnostic records");
            var diagnosticsPath = $"/periods/drafts/{draftId}/diagnostics";
            check((await PostAdmin(scoped, diagnosticsPath)).StatusCode == HttpStatusCode.Forbidden &&
                  (await adminClient.PostAsync(diagnosticsPath, null)).StatusCode == HttpStatusCode.BadRequest,
                  "HTTP period diagnostic capture requires explicit permission and antiforgery");
            using var diagnosticResponse = await PostAdmin(adminClient, diagnosticsPath);
            diagnosticResponse.EnsureSuccessStatusCode();
            using var diagnosticJson = JsonDocument.Parse(await diagnosticResponse.Content.ReadAsStringAsync());
            var diagnosticId = diagnosticJson.RootElement.GetProperty("result").GetGuid();
            using var diagnosticAssessment = await adminClient.GetAsync($"/periods/diagnostics/{diagnosticId}");
            check(diagnosticAssessment.StatusCode == HttpStatusCode.OK &&
                  (await diagnosticAssessment.Content.ReadAsStringAsync()).Contains("\"approvable\":false", StringComparison.Ordinal) &&
                  (await adminClient.GetAsync(diagnosticsPath)).StatusCode == HttpStatusCode.OK &&
                  (await scoped.GetAsync($"/periods/diagnostics/{diagnosticId}")).StatusCode == HttpStatusCode.Forbidden,
                  "HTTP diagnostic evidence is restricted and cannot approve a period");
            var preflight = await adminClient.GetAsync($"/periods/preflight/{newEmployee}?start={workDay:yyyy-MM-dd}&endExclusive={workDay.AddDays(1):yyyy-MM-dd}");
            check(preflight.StatusCode == HttpStatusCode.OK && (await preflight.Content.ReadAsStringAsync()).Contains("\"approvable\":false", StringComparison.Ordinal) &&
                  (await scoped.GetAsync($"/periods/preflight/{newEmployee}?start={workDay:yyyy-MM-dd}&endExclusive={workDay.AddDays(1):yyyy-MM-dd}")).StatusCode == HttpStatusCode.Forbidden &&
                  (await adminClient.GetAsync($"/periods/preflight/{newEmployee}?start={workDay:yyyy-MM-dd}&endExclusive={workDay.AddDays(63):yyyy-MM-dd}")).StatusCode == HttpStatusCode.BadRequest,
                "HTTP period diagnostic denies unauthorized and oversized requests and is never approvable");
            var scheduleResponse = await adminClient.GetAsync($"/schedules/{newEmployee}/{workDay:yyyy-MM-dd}");
            check(scheduleResponse.StatusCode == HttpStatusCode.OK && (await scheduleResponse.Content.ReadAsStringAsync()).Contains("HTTP night", StringComparison.Ordinal) &&
                (await own.GetAsync($"/schedules/{newEmployee}/{workDay:yyyy-MM-dd}")).StatusCode == HttpStatusCode.NotFound,
                "HTTP schedule read enforces explicit permission and target scope");
            check((await PostAdmin(adminClient, "/scheduling/assignments", new { EmployeeId = newEmployee, CalendarId = newCalendar, ShiftId = newShift, ValidFrom = workDay, ValidUntilExclusive = (DateOnly?)null })).StatusCode == HttpStatusCode.Conflict,
                "HTTP duplicate schedule assignment returns conflict");
            var secondOrgUnit = await CreateId("/org/units", new { CompanyId = newCompany, ParentId = newUnit, Name = "HTTP second unit" });
            var unitVersion = await CatalogVersion("units", newUnit);
            check((await PostAdmin(adminClient, $"/org/units/{newUnit}/reparent", new { CompanyId = newCompany, ParentId = secondOrgUnit, ExpectedVersion = unitVersion })).StatusCode == HttpStatusCode.Conflict,
                "HTTP organization tree rejects cycles");
            await CreateId("/org/transfers", new { EmployeeId = newEmployee, AssignmentId = initialOrgAssignment, UnitId = secondOrgUnit, Effective = workDay.AddDays(1) });
            check((await PostAdmin(adminClient, "/org/transfers", new { EmployeeId = newEmployee, AssignmentId = initialOrgAssignment, UnitId = secondOrgUnit, Effective = workDay.AddDays(2) })).StatusCode == HttpStatusCode.Conflict,
                "HTTP transfer preserves history and cannot repeat closed assignment");
            var nextCalendar = await CreateId($"/scheduling/calendars/{newCalendar}/fork", new { Name = "HTTP calendar version two" });
            check((await PostAdmin(adminClient, "/scheduling/calendar-days/revise", new { CalendarId = newCalendar, Date = workDay, Kind = 1, Note = "no" })).StatusCode == HttpStatusCode.Conflict &&
                (await PostAdmin(adminClient, "/scheduling/calendar-days/revise", new { CalendarId = nextCalendar, Date = workDay, Kind = 1, Note = "yes" })).StatusCode == HttpStatusCode.OK,
                "HTTP calendar versions prevent modification of assigned history");
            await CreateId("/scheduling/calendar-days", new { CalendarId = nextCalendar, Date = workDay.AddDays(1), Kind = 1, Note = "next day" });
            await CreateId("/scheduling/replacements", new { EmployeeId = newEmployee, AssignmentId = initialWorkSchedule, CalendarId = nextCalendar, ShiftId = newShift, Effective = workDay.AddDays(1) });
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
            var gregorian = await adminClient.GetAsync("/dates/gregorian/2024-03-20");
            using var gregorianJson = JsonDocument.Parse(await gregorian.Content.ReadAsStringAsync());
            check(gregorian.StatusCode == HttpStatusCode.OK && gregorianJson.RootElement.GetProperty("year").GetInt32() == 1403 &&
                gregorianJson.RootElement.GetProperty("month").GetInt32() == 1 && gregorianJson.RootElement.GetProperty("day").GetInt32() == 1,
                "HTTP Gregorian date converts to Persian year boundary");
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
                (await createdClient.GetAsync("/workspace/account")).StatusCode == HttpStatusCode.OK &&
                (await createdClient.GetAsync("/assets/account.js")).StatusCode == HttpStatusCode.OK &&
                (await createdClient.GetAsync("/assets/employees.css")).StatusCode == HttpStatusCode.OK &&
                (await createdClient.GetAsync("/workspace/my-requests")).StatusCode == HttpStatusCode.Forbidden &&
                (await createdClient.GetAsync("/assets/theme.js")).StatusCode == HttpStatusCode.OK &&
                (await createdClient.GetAsync("/assets/theme.css")).StatusCode == HttpStatusCode.OK &&
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
            // The administration cookie has a fixed 30-minute lifetime without sliding
            // renewal; re-authenticate before the late user-administration checks.
            if ((await LoginWithRetry(adminClient, adminName, Password)).StatusCode != HttpStatusCode.NoContent)
                throw new InvalidOperationException("Synthetic administrator re-login failed.");
            if ((await LoginWithRetry(deputyClient, deputyName, "Deputy!Password-2027x")).StatusCode != HttpStatusCode.NoContent)
                throw new InvalidOperationException("Synthetic deputy re-login failed.");
            using var userDetailResponse = await adminClient.GetAsync("/admin/users/" + deputyUserId);
            using var userDetailJson = await ParseOrThrow(userDetailResponse);
            using var userSearchResponse = await adminClient.GetAsync("/admin/catalog/users?q=" + Uri.EscapeDataString(deputyName));
            using var userSearchJson = await ParseOrThrow(userSearchResponse);
            check(userDetailResponse.StatusCode == HttpStatusCode.OK &&
                userDetailJson.RootElement.GetProperty("userName").GetString() == deputyName &&
                userDetailJson.RootElement.GetProperty("employeeId").GetGuid() == second &&
                userDetailJson.RootElement.GetProperty("roles").EnumerateArray().Any(x => x.GetProperty("roleId").GetGuid() == testManagerRole) &&
                userSearchJson.RootElement.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == deputyUserId) &&
                (await deputyClient.GetAsync("/admin/users/" + deputyUserId)).StatusCode == HttpStatusCode.Forbidden &&
                (await deputyClient.GetAsync("/admin/catalog/users?q=deputy")).StatusCode == HttpStatusCode.Forbidden,
                "HTTP user detail and user search restricted to identity administrators");
            check((await PostAdmin(deputyClient, "/admin/users/" + deputyUserId + "/password", new { NewPassword = "Reset!Password-2026z" })).StatusCode == HttpStatusCode.Forbidden &&
                (await PostAdmin(adminClient, "/admin/users/" + deputyUserId + "/password", new { NewPassword = "" })).StatusCode == HttpStatusCode.BadRequest,
                "HTTP password reset restricted to identity administrators with a real password");
            using var resetHttpResponse = await PostAdmin(adminClient, "/admin/users/" + deputyUserId + "/password", new { NewPassword = "Reset!Password-2026z" });
            if (resetHttpResponse.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Reset HTTP " + (int)resetHttpResponse.StatusCode + ": " + await resetHttpResponse.Content.ReadAsStringAsync());
            var meAfterReset = await deputyClient.GetAsync("/auth/me");
            var oldPasswordLogin = await Login(deputyClient, deputyName, "Deputy!Password-2027x");
            var tempLogin = await Login(deputyClient, deputyName, "Reset!Password-2026z");
            var tempInbox = await deputyClient.GetAsync("/corrections/inbox");
            if (meAfterReset.StatusCode != HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("After reset /auth/me was " + (int)meAfterReset.StatusCode + ", expected 401.");
            if (oldPasswordLogin.StatusCode != HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Old password login after reset was " + (int)oldPasswordLogin.StatusCode + ", expected 401.");
            if (tempLogin.StatusCode != HttpStatusCode.NoContent)
                throw new InvalidOperationException("Temporary password login was " + (int)tempLogin.StatusCode + ", expected 204.");
            if (tempInbox.StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException("Temporary password inbox was " + (int)tempInbox.StatusCode + ", expected 403.");
            check(true, "HTTP admin password reset rotates stamp and enforces the temporary password");
            await using (var resetAudit = provider.CreateAsyncScope())
                check(await resetAudit.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().SecurityAudit
                    .AnyAsync(x => x.Action == "identity.user.password.reset:" + deputyUserId.ToString("N")), "HTTP password reset audited without credentials");
            if ((await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = featureRole, Code = "executive.dashboard.read" })).StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Synthetic executive permission grant failed.");
            using var executiveDenied = Client();
            // The inactive-account scenario above deactivates web-self; restore the
            // seeded state so the executive denial reflects permission, not activity.
            await using (var reactivate = provider.CreateAsyncScope())
            {
                var db = reactivate.ServiceProvider.GetRequiredService<IoDbContext>();
                var selfRecord = await db.Users.SingleAsync(x => x.Id == self);
                selfRecord.IsActive = true;
                await db.SaveChangesAsync();
            }
            if ((await LoginWithRetry(executiveDenied, "web-self", Password)).StatusCode != HttpStatusCode.NoContent)
                throw new InvalidOperationException("Synthetic executive denied re-login failed.");
            using var companiesResponse = await adminClient.GetAsync("/executive/companies");
            using var companiesJson = await ParseOrThrow(companiesResponse);
            var now = DateTimeOffset.UtcNow;
            string Window(int fromOffset, int untilOffset) => "/executive/overview/" + testCompany +
                "?fromUtc=" + now.AddDays(fromOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z" +
                "&untilUtcExclusive=" + now.AddDays(untilOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z";
            using var executiveWindow = await adminClient.GetAsync(Window(-10, 10));
            using var executiveJson = await ParseOrThrow(executiveWindow);
            check(companiesResponse.StatusCode == HttpStatusCode.OK &&
                companiesJson.RootElement.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == testCompany) &&
                (await executiveDenied.GetAsync("/executive/companies")).StatusCode == HttpStatusCode.Forbidden &&
                executiveWindow.StatusCode == HttpStatusCode.OK &&
                executiveJson.RootElement.GetProperty("companyId").GetGuid() == testCompany &&
                executiveJson.RootElement.GetProperty("employeeCount").GetInt32() >= 1 &&
                executiveJson.RootElement.GetProperty("correctionDecisionCount").GetInt32() >= 1 &&
                (await adminClient.GetAsync(Window(-100, -30))).StatusCode == HttpStatusCode.BadRequest,
                "HTTP executive company list and enriched overview restricted to executive permission");
            if ((await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = featureRole, Code = "leave.manage" })).StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Synthetic leave permission grant failed.");
            Guid leaveType = await CreateId("/leave/types", new { CompanyId = testCompany, Name = "HTTP leave type", Symbol = "http-leave", MinutesPerDay = 480, CarryOverAllowed = false });
            if ((await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testSelfRole, Code = "leave.request.submit" })).StatusCode != HttpStatusCode.OK ||
                (await PostAdmin(adminClient, "/admin/role-permissions", new { RoleId = testManagerRole, Code = "leave.request.decide" })).StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Synthetic leave workflow permission grant failed.");
            // The requester cookie has the fixed 30-minute lifetime too; re-authenticate.
            var ownReLogin = await LoginWithRetry(own, "web-self", Password);
            if (ownReLogin.StatusCode != HttpStatusCode.NoContent)
                throw new InvalidOperationException("Synthetic requester re-login status " + (int)ownReLogin.StatusCode + ".");
            var leaveOperation = Guid.NewGuid();
            using var leaveSubmitResponse = await PostAdmin(own, "/leave/requests", new { OperationId = leaveOperation, LeaveTypeId = leaveType, Kind = 0, WorkDate = "2027-06-01", FromAtUtc = (string?)null, UntilAtUtcExclusive = (string?)null, Reason = "daily leave flow" });
            using var leaveSubmitJson = await ParseOrThrow(leaveSubmitResponse);
            var leaveRequestId = leaveSubmitJson.RootElement.GetProperty("result").GetGuid();
            using var leaveReplayResponse = await PostAdmin(own, "/leave/requests", new { OperationId = leaveOperation, LeaveTypeId = leaveType, Kind = 0, WorkDate = "2027-06-01", FromAtUtc = (string?)null, UntilAtUtcExclusive = (string?)null, Reason = "daily leave flow" });
            using var leaveReplayJson = await ParseOrThrow(leaveReplayResponse);
            using var ownInbox = await own.GetAsync("/leave/inbox");
            check(leaveSubmitResponse.StatusCode == HttpStatusCode.OK &&
                leaveReplayResponse.StatusCode == HttpStatusCode.OK && leaveReplayJson.RootElement.GetProperty("result").GetGuid() == leaveRequestId &&
                (await PostAdmin(own, "/leave/requests", new { OperationId = leaveOperation, LeaveTypeId = leaveType, Kind = 0, WorkDate = "2027-06-01", FromAtUtc = (string?)null, UntilAtUtcExclusive = (string?)null, Reason = "changed" })).StatusCode == HttpStatusCode.Conflict &&
                (await PostAdmin(own, "/leave/requests", new { OperationId = Guid.NewGuid(), LeaveTypeId = leaveType, Kind = 1, WorkDate = "2027-06-02", FromAtUtc = "2027-06-02T10:00:00Z", UntilAtUtcExclusive = "2027-06-02T08:00:00Z", Reason = "inverted" })).StatusCode == HttpStatusCode.BadRequest &&
                (await own.GetAsync("/leave/requests?offset=0")).StatusCode == HttpStatusCode.OK &&
                ownInbox.StatusCode == HttpStatusCode.Forbidden,
                "HTTP leave submission is idempotent, validated and workflow inbox is permission bound");
            using var hourlyResponse = await PostAdmin(own, "/leave/requests", new { OperationId = Guid.NewGuid(), LeaveTypeId = leaveType, Kind = 1, WorkDate = "2027-06-02", FromAtUtc = "2027-06-02T08:00:00Z", UntilAtUtcExclusive = "2027-06-02T10:00:00Z", Reason = "hourly leave flow" });
            using var hourlyJson = await ParseOrThrow(hourlyResponse);
            var hourlyRequestId = hourlyJson.RootElement.GetProperty("result").GetGuid();
            // The manager session dates from the password scenarios; refresh it. The
            // role-revocation scenario removed the manager's only role grant, so the
            // decide role is granted again for the workflow checks.
            if ((await LoginWithRetry(scoped, "web-manager", "Synthetic!Changed-Password-2026")).StatusCode != HttpStatusCode.NoContent)
                throw new InvalidOperationException("Synthetic manager decision re-login failed.");
            if ((await PostAdmin(adminClient, "/admin/user-roles", new { UserId = manager, RoleId = testManagerRole })).StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Synthetic manager role re-grant failed.");
            using var managerInboxJson = await ParseOrThrow(await scoped.GetAsync("/leave/inbox?offset=0"));
            var selfDecisionStatus = (int)(await PostAdmin(own, "/leave/requests/" + leaveRequestId + "/decision", new { Decision = 1, Reason = "self" })).StatusCode;
            var approveStatus = (int)(await PostAdmin(scoped, "/leave/requests/" + leaveRequestId + "/decision", new { Decision = 1, Reason = "approve daily" })).StatusCode;
            var reDecisionStatus = (int)(await PostAdmin(scoped, "/leave/requests/" + leaveRequestId + "/decision", new { Decision = 2, Reason = "second time" })).StatusCode;
            var hourlyStatus = (int)(await PostAdmin(scoped, "/leave/requests/" + hourlyRequestId + "/decision", new { Decision = 2, Reason = "reject hourly" })).StatusCode;
            // The requester lacks decide, so its self-approval stops at the permission
            // boundary (403) before the explicit self-approval guard (400).
            if (selfDecisionStatus != 403 || approveStatus != 200 || reDecisionStatus != 409 || hourlyStatus != 200)
                throw new InvalidOperationException("Leave decision statuses self=" + selfDecisionStatus + " approve=" + approveStatus +
                    " second=" + reDecisionStatus + " hourly=" + hourlyStatus + " inbox=" +
                    managerInboxJson.RootElement.GetProperty("items").GetRawText()[..Math.Min(300, managerInboxJson.RootElement.GetProperty("items").GetRawText().Length)]);
            check(true, "HTTP leave decisions forbid self-approval, allow the scoped manager once and reject afterwards");
            using var overtimeResponse = await PostAdmin(own, "/leave/requests", new { OperationId = Guid.NewGuid(), LeaveTypeId = leaveType, Category = 1, Kind = 1, WorkDate = "2027-06-02", FromAtUtc = "2027-06-02T18:00:00Z", UntilAtUtcExclusive = "2027-06-02T21:00:00Z", Reason = "overtime flow" });
            using var overtimeJson = await ParseOrThrow(overtimeResponse);
            using var missionResponse = await PostAdmin(own, "/leave/requests", new { OperationId = Guid.NewGuid(), LeaveTypeId = leaveType, Category = 2, Kind = 0, WorkDate = "2027-06-04", FromAtUtc = (string?)null, UntilAtUtcExclusive = (string?)null, Reason = "mission flow" });
            using var missionJson = await ParseOrThrow(missionResponse);
            using var dailyOvertimeResponse = await PostAdmin(own, "/leave/requests", new { OperationId = Guid.NewGuid(), LeaveTypeId = leaveType, Category = 1, Kind = 0, WorkDate = "2027-06-05", FromAtUtc = (string?)null, UntilAtUtcExclusive = (string?)null, Reason = "overtime must be hourly" });
            var overtimeApprove = (int)(await PostAdmin(scoped, "/leave/requests/" + overtimeJson.RootElement.GetProperty("result").GetGuid() + "/decision", new { Decision = 1, Reason = "approve overtime" })).StatusCode;
            var missionApprove = (int)(await PostAdmin(scoped, "/leave/requests/" + missionJson.RootElement.GetProperty("result").GetGuid() + "/decision", new { Decision = 1, Reason = "approve mission" })).StatusCode;
            if (overtimeApprove != 200 || missionApprove != 200 || dailyOvertimeResponse.StatusCode != HttpStatusCode.BadRequest)
                throw new InvalidOperationException("Extra work statuses overtime=" + overtimeApprove + " mission=" + missionApprove + " dailyOvertime=" + (int)dailyOvertimeResponse.StatusCode);
            await using (var extraScope = provider.CreateAsyncScope())
            {
                var db = extraScope.ServiceProvider.GetRequiredService<IoDbContext>();
                check(await db.LeaveDecisions.AnyAsync(x => x.RequestId == overtimeJson.RootElement.GetProperty("result").GetGuid() && x.Decision == 1) &&
                    await db.LeaveDecisions.AnyAsync(x => x.RequestId == missionJson.RootElement.GetProperty("result").GetGuid() && x.Decision == 1) &&
                    !await db.LeaveLedgerEntries.AnyAsync(x => x.EmployeeId == first && x.EffectiveDate == new DateOnly(2027, 6, 4)) &&
                    !await db.LeaveLedgerEntries.AnyAsync(x => x.EmployeeId == first && x.EffectiveDate == new DateOnly(2027, 6, 2) && x.Minutes == -180),
                    "HTTP overtime and mission approvals record decisions without leave ledger consumption");
            }
            await using (var leaveScope = provider.CreateAsyncScope())
            {
                var db = leaveScope.ServiceProvider.GetRequiredService<IoDbContext>();
                check(await db.LeaveLedgerEntries.AnyAsync(x => x.EmployeeId == first && x.Kind == 2 && x.Minutes == -480 &&
                        x.EffectiveDate == new DateOnly(2027, 6, 1)) &&
                    !await db.LeaveLedgerEntries.AnyAsync(x => x.EmployeeId == first && x.Kind == 2 &&
                        x.EffectiveDate == new DateOnly(2027, 6, 2)) &&
                    await db.LeaveDecisions.AnyAsync(x => x.RequestId == leaveRequestId && x.Decision == 1) &&
                    await db.LeaveDecisions.AnyAsync(x => x.RequestId == hourlyRequestId && x.Decision == 2),
                    "HTTP leave approval posts one ledger usage movement while rejection posts none");
            }
            Guid expectedLeaveDecision;
            await using(var reversalScope=provider.CreateAsyncScope())
                expectedLeaveDecision=await reversalScope.ServiceProvider.GetRequiredService<IoDbContext>().LeaveDecisions.Where(d=>d.RequestId==leaveRequestId).Select(d=>d.Id).SingleAsync();
            var leaveReversalPayload=new {OperationId=Guid.NewGuid(),ExpectedDecisionId=expectedLeaveDecision,Reason="HTTP reconsider leave"};
            check((await PostAdmin(scoped,$"/leave/requests/{leaveRequestId}/reverse",leaveReversalPayload)).StatusCode==HttpStatusCode.Forbidden &&
                  (await PostAdmin(own,$"/leave/requests/{leaveRequestId}/reverse",leaveReversalPayload)).StatusCode==HttpStatusCode.Forbidden,
                  "HTTP leave reversal does not inherit decision or submission permission");
            (await PostAdmin(adminClient,"/admin/role-permissions",new {RoleId=testManagerRole,Code="leave.request.reverse"})).EnsureSuccessStatusCode();
            (await PostAdmin(scoped,$"/leave/requests/{leaveRequestId}/reverse",leaveReversalPayload)).EnsureSuccessStatusCode();
            (await PostAdmin(scoped,$"/leave/requests/{leaveRequestId}/reverse",leaveReversalPayload)).EnsureSuccessStatusCode();
            using(var leaveHistoryResponse=await own.GetAsync($"/leave/requests/{leaveRequestId}/history"))
            using(var leaveHistoryJson=JsonDocument.Parse(await leaveHistoryResponse.Content.ReadAsStringAsync()))
                check(leaveHistoryResponse.IsSuccessStatusCode && leaveHistoryJson.RootElement.GetProperty("items")[0].GetProperty("compensationLedgerEntryId").ValueKind==JsonValueKind.String &&
                      (await PostAdmin(scoped,$"/leave/requests/{leaveRequestId}/decision",new {Decision=1,Reason="reapproved after reversal"})).StatusCode==HttpStatusCode.OK,
                      "HTTP leave reversal retries expose compensation evidence and reapproval succeeds");
            (await PostAdmin(adminClient,"/admin/role-permissions/revoke",new {RoleId=testManagerRole,Code="leave.request.reverse"})).EnsureSuccessStatusCode();
            var cancelOperation = Guid.NewGuid();
            using var cancelTargetResponse = await PostAdmin(own, "/leave/requests", new { OperationId = cancelOperation, LeaveTypeId = leaveType, Kind = 0, WorkDate = "2027-06-03", FromAtUtc = (string?)null, UntilAtUtcExclusive = (string?)null, Reason = "cancellable leave" });
            using var cancelTargetJson = await ParseOrThrow(cancelTargetResponse);
            check((await PostAdmin(own, "/leave/requests/" + cancelTargetJson.RootElement.GetProperty("result").GetGuid() + "/cancel", new { Reason = "plans changed" })).StatusCode == HttpStatusCode.OK &&
                (await PostAdmin(own, "/leave/requests/" + cancelTargetJson.RootElement.GetProperty("result").GetGuid() + "/cancel", new { Reason = "again" })).StatusCode == HttpStatusCode.Conflict,
                "HTTP requester cancels a pending leave request exactly once");
            using var ownNotifications = await own.GetAsync("/notifications?offset=0");
            using var ownNotificationsJson = await ParseOrThrow(ownNotifications);
            var unreadBefore = ownNotificationsJson.RootElement.GetProperty("unread").GetInt32();
            using var readAllResponse = await PostAdmin(own, "/notifications/read-all");
            using var readAllJson = await ParseOrThrow(readAllResponse);
            using var anonNotifications = Client();
            var unreadAfter = await own.GetAsync("/notifications?offset=0");
            using var unreadAfterJson = await ParseOrThrow(unreadAfter);
            using var companyBalances = await adminClient.GetAsync("/leave/balances/" + testCompany + "?asOf=2027-06-30&offset=0");
            using var companyBalancesJson = await ParseOrThrow(companyBalances);
            if (unreadBefore < 4)
                throw new InvalidOperationException("Notification unread count was " + unreadBefore + ", expected at least 4.");
            check(ownNotifications.StatusCode == HttpStatusCode.OK &&
                unreadBefore >= 4 &&
                (await anonNotifications.GetAsync("/notifications?offset=0")).StatusCode == HttpStatusCode.Unauthorized &&
                readAllResponse.StatusCode == HttpStatusCode.OK &&
                readAllJson.RootElement.GetProperty("result").GetInt32() == unreadBefore &&
                unreadAfterJson.RootElement.GetProperty("unread").GetInt32() == 0,
                "HTTP request decisions notify the requester and notifications mark read");
            var balanceRows = companyBalancesJson.RootElement.GetProperty("items").EnumerateArray()
                .Where(x => x.GetProperty("id").GetGuid() == first)
                .SelectMany(x => x.GetProperty("balances").EnumerateArray()).ToList();
            var ownBalanceStatus = (int)(await own.GetAsync("/leave/balances/" + testCompany + "?asOf=2027-06-30&offset=0")).StatusCode;
            // Both approval consumptions remain immutable usage evidence. The
            // reversal adds a +480 adjustment rather than deleting the first -480;
            // reapproval adds a second -480. Net balance must still be -480.
            if (ownBalanceStatus != 403 || companyBalances.StatusCode != HttpStatusCode.OK ||
                !balanceRows.Any(b => b.GetProperty("leaveTypeId").GetGuid() == leaveType &&
                    b.GetProperty("used").GetInt64() == 960 && b.GetProperty("adjustment").GetInt64() == 480 &&
                    b.GetProperty("opening").GetInt64() == 0 && b.GetProperty("granted").GetInt64() == 0 &&
                    b.GetProperty("balance").GetInt64() == -480))
            {
                var dump = companyBalancesJson.RootElement.GetProperty("items").GetRawText();
                throw new InvalidOperationException("Balance own=" + ownBalanceStatus + " company=" + (int)companyBalances.StatusCode +
                    " rows=" + balanceRows.Count + " items=" + dump[..Math.Min(900, dump.Length)]);
            }
            check(true, "HTTP company leave balance report exposes usage and stays permission bound");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
