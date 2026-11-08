using System.Net;
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
            var token = await Csrf(scoped);
            using var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
            logout.Headers.Add("X-CSRF-TOKEN", token);
            check((await scoped.SendAsync(logout)).StatusCode == HttpStatusCode.NoContent &&
                  (await scoped.GetAsync("/auth/me")).StatusCode == HttpStatusCode.Unauthorized, "HTTP logout ends session");
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
