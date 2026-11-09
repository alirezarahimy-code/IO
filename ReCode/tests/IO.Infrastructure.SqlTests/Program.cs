using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

// Destructive migration tests are restricted to the disposable CI runner and
// its loopback-only SQL Server. No caller-provided server or database is used.
if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
    Environment.GetEnvironmentVariable("IO_SQL_DISPOSABLE_TEST") != "yes" ||
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD")))
{
    Console.Error.WriteLine("BLOCKED: this test requires the disposable GitHub Actions SQL Server.");
    return 2;
}
var dbName = "IO_Phase03_" + Guid.NewGuid().ToString("N");
var connection = new SqlConnectionStringBuilder
{
    DataSource = "127.0.0.1,1433", InitialCatalog = dbName,
    UserID = "sa", Password = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD")!,
    Encrypt = true, TrustServerCertificate = true, ConnectTimeout = 10,
}.ConnectionString;
var options = new DbContextOptionsBuilder<IoDbContext>().UseSqlServer(connection).Options;
IoDbContext NewDb() => new(options);
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}
async Task ExpectSqlFailure(Func<Task> action, params int[] numbers)
{
    try { await action(); }
    catch (DbUpdateException e) when (e.InnerException is SqlException sql && numbers.Contains(sql.Number)) { return; }
    catch (SqlException e) when (numbers.Contains(e.Number)) { return; }
    throw new InvalidOperationException("Expected SQL error: " + string.Join(",", numbers));
}
async Task<bool> HasTable(string name)
{
    await using var conn = new SqlConnection(connection);
    await conn.OpenAsync();
    await using var command = conn.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = @name AND schema_id = SCHEMA_ID(N'dbo')";
    command.Parameters.AddWithValue("@name", name);
    return (int)(await command.ExecuteScalarAsync() ?? 0) == 1;
}
RawAttendanceEventRecord Raw(Guid source, string? external, DateTimeOffset? occurred = null) => new()
{
    Id = Guid.NewGuid(), SourceId = source, ExternalEventId = external,
    ReceivedAtUtc = DateTimeOffset.UtcNow, OccurredAtUtc = occurred,
    SourcePayload = [0x01, 0x02, 0x03]
};
var orgId = Guid.NewGuid(); var companyId = Guid.NewGuid(); var otherCompanyId = Guid.NewGuid(); var siteId = Guid.NewGuid();
var unitId = Guid.NewGuid(); var otherUnitId = Guid.NewGuid(); var employeeId = Guid.NewGuid();
var sourceId = Guid.NewGuid(); var otherSourceId = Guid.NewGuid();
string[] expectedTables = ["Organizations", "Companies", "Sites", "OrgUnits", "Employees", "EmployeeAssignments",
    "Users", "Roles", "Permissions", "UserRoles", "RolePermissions", "UserOrgScopes", "EventSources", "RawAttendanceEvents",
    "Calendars", "CalendarDays", "ShiftDefinitions", "WorkScheduleAssignments", "EmploymentProfiles", "EmployeeSiteAssignments", "SourceEmployeeMappings", "RawEventConflicts", "ObservedRuleVersions", "DailyCalculationRuns", "CorrectionPolicies", "CorrectionRequests", "CorrectionDecisions", "PeriodDrafts", "RawConflictNotes", "SourceMappingChanges", "CalculationInputRevision", "PeriodDiagnostics", "LeaveTypes", "LeaveLedgerEntries", "Delegations", "DelegationRevocations", "CorrectionSettings", "CorrectionDelegationGrants", "DelegatedCorrectionDecisions"];
try
{
    await using (var db = NewDb())
    {
        Check(!db.Database.HasPendingModelChanges(), "model matches migration snapshot");
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Check((await db.Database.GetAppliedMigrationsAsync()).Count() == 18, "foundation and seventeen later migrations applied idempotently");
        var created = 0;
        foreach (var table in expectedTables) if (await HasTable(table)) created++;
        Check(created == expectedTables.Length, "all 39 foundation and later tables created");
        // Upgrade an existing settings row across the limits migration, not only an empty database.
        var legacyOrg=Guid.NewGuid(); var legacyCompany=Guid.NewGuid(); var legacyUser=Guid.NewGuid(); var legacySetting=Guid.NewGuid();
        var migrator=db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261109201550_CorrectionSettings");
        db.Organizations.Add(new OrganizationRecord {Id=legacyOrg,Name="Synthetic migration organization"});
        db.Companies.Add(new CompanyRecord {Id=legacyCompany,OrganizationId=legacyOrg,Name="Synthetic migration company"});
        db.Users.Add(new UserRecord {Id=legacyUser,UserName="synthetic-migration-settings",IsActive=true});
        await db.SaveChangesAsync();
        var createdUtc=DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.CorrectionSettings (Id,CompanyId,Revision,MaxRequestAgeDays,Reason,CreatedByUserId,CreatedAtUtc) VALUES ({legacySetting},{legacyCompany},{1},{5},{"legacy"},{legacyUser},{createdUtc})");
        await migrator.MigrateAsync();
        var upgraded=await db.CorrectionSettings.AsNoTracking().SingleAsync(x=>x.Id==legacySetting);
        Check(upgraded.Revision==1 && upgraded.MaxRequestAgeDays==5 && upgraded.DailyRequestLimit==0 && upgraded.MonthlyRequestLimit==0,
            "settings migration preserves old values and leaves new limits unlimited");
        db.Organizations.Add(new() { Id = orgId, Name = "Synthetic organization" });
        db.Companies.AddRange(new CompanyRecord { Id = companyId, OrganizationId = orgId, Name = "Synthetic company" },
            new CompanyRecord { Id = otherCompanyId, OrganizationId = orgId, Name = "Other synthetic company" });
        db.Sites.Add(new() { Id = siteId, CompanyId = companyId, Name = "Synthetic site", TimeZoneId = "Asia/Tehran" });
        db.OrgUnits.AddRange(new OrgUnitRecord { Id = unitId, CompanyId = companyId, Name = "Synthetic unit" },
            new OrgUnitRecord { Id = otherUnitId, CompanyId = otherCompanyId, Name = "Other synthetic unit" });
        db.Employees.Add(new() { Id = employeeId, CompanyId = companyId, PersonnelNumber = "SYN-1", DisplayName = "Synthetic person" });
        db.EventSources.AddRange(new EventSourceRecord { Id = sourceId, SiteId = siteId, Name = "Synthetic source 1" },
            new EventSourceRecord { Id = otherSourceId, SiteId = siteId, Name = "Synthetic source 2" });
        await db.SaveChangesAsync();
    }
    await using (var db = NewDb())
    {
        db.EmployeeAssignments.Add(new() { Id = Guid.NewGuid(), CompanyId = companyId, EmployeeId = employeeId, OrgUnitId = unitId,
            ValidFrom = new DateOnly(2026, 1, 1), ValidUntilExclusive = new DateOnly(2026, 2, 1) });
        await db.SaveChangesAsync();
        Check(true, "synthetic organization and valid assignment saved");
    }
    await ExpectSqlFailure(async () =>
    {
        await using var db = NewDb();
        db.EmployeeAssignments.Add(new() { Id = Guid.NewGuid(), CompanyId = companyId, EmployeeId = employeeId, OrgUnitId = unitId,
            ValidFrom = new DateOnly(2026, 2, 1), ValidUntilExclusive = new DateOnly(2026, 2, 1) });
        await db.SaveChangesAsync();
    }, 547);
    Check(true, "invalid date interval rejected");
    // These synthetic actors exercise the access reader directly. They do not
    // represent authenticated HTTP sessions or a complete authorization feature.
    var selfUser = Guid.NewGuid(); var scopedUser = Guid.NewGuid(); var globalUser = Guid.NewGuid();
    var disabledUser = Guid.NewGuid(); var noRoleUser = Guid.NewGuid(); var otherEmployee = Guid.NewGuid();
    var selfRole = Guid.NewGuid(); var scopeRole = Guid.NewGuid(); var allRole = Guid.NewGuid(); var emptyRole = Guid.NewGuid();
    var selfPermission = Guid.NewGuid(); var scopePermission = Guid.NewGuid(); var allPermission = Guid.NewGuid();
    await using (var db = NewDb())
    {
        db.Employees.Add(new() { Id = otherEmployee, CompanyId = otherCompanyId, PersonnelNumber = "SYN-2", DisplayName = "Other synthetic person" });
        db.Users.AddRange(
            new UserRecord { Id = selfUser, UserName = "self", EmployeeId = employeeId, IsActive = true },
            new UserRecord { Id = scopedUser, UserName = "scoped", IsActive = true },
            new UserRecord { Id = globalUser, UserName = "global", IsActive = true },
            new UserRecord { Id = disabledUser, UserName = "disabled", EmployeeId = employeeId, IsActive = false },
            new UserRecord { Id = noRoleUser, UserName = "no-role", EmployeeId = employeeId, IsActive = true });
        db.Roles.AddRange(new RoleRecord { Id = selfRole, Name = "self" },
            new RoleRecord { Id = scopeRole, Name = "scoped" }, new RoleRecord { Id = allRole, Name = "global" },
            new RoleRecord { Id = emptyRole, Name = "Administrator" });
        db.Permissions.AddRange(new PermissionRecord { Id = selfPermission, Code = EmployeeAccessReader.ReadSelf },
            new PermissionRecord { Id = scopePermission, Code = EmployeeAccessReader.ReadScoped },
            new PermissionRecord { Id = allPermission, Code = EmployeeAccessReader.ReadAll });
        db.UserRoles.AddRange(new UserRoleRecord { UserId = selfUser, RoleId = selfRole },
            new UserRoleRecord { UserId = disabledUser, RoleId = selfRole },
            new UserRoleRecord { UserId = scopedUser, RoleId = scopeRole },
            new UserRoleRecord { UserId = globalUser, RoleId = allRole },
            new UserRoleRecord { UserId = noRoleUser, RoleId = emptyRole });
        db.RolePermissions.AddRange(new RolePermissionRecord { RoleId = selfRole, PermissionId = selfPermission },
            new RolePermissionRecord { RoleId = scopeRole, PermissionId = scopePermission },
            new RolePermissionRecord { RoleId = allRole, PermissionId = allPermission });
        db.UserOrgScopes.AddRange(new UserOrgScopeRecord { Id = Guid.NewGuid(), UserId = scopedUser, OrgUnitId = unitId,
            ValidFrom = new DateOnly(2026, 1, 1), ValidUntilExclusive = new DateOnly(2026, 2, 1) },
            new UserOrgScopeRecord { Id = Guid.NewGuid(), UserId = noRoleUser, OrgUnitId = unitId,
                ValidFrom = new DateOnly(2026, 1, 1) });
        db.EmployeeAssignments.Add(new() { Id = Guid.NewGuid(), CompanyId = otherCompanyId, EmployeeId = otherEmployee,
            OrgUnitId = otherUnitId, ValidFrom = new DateOnly(2026, 1, 1) });
        await db.SaveChangesAsync();
    }
    await using (var db = NewDb())
    {
        var reader = new EmployeeAccessReader(db);
        var at = new DateOnly(2026, 1, 15);
        async Task<bool> Allowed(Guid actor, Guid target, DateOnly date) => await reader.FindAuthorizedAsync(actor, target, date) is not null;
        Check(await Allowed(selfUser, employeeId, at) && !await Allowed(selfUser, otherEmployee, at), "self permission only reads linked employee");
        Check(!await Allowed(noRoleUser, employeeId, at) && !await Allowed(disabledUser, employeeId, at), "role name and scope without permission, and inactive account, denied");
        Check(await Allowed(scopedUser, employeeId, at) && !await Allowed(scopedUser, otherEmployee, at), "scope only reads assigned exact unit");
        Check(!await Allowed(scopedUser, employeeId, new DateOnly(2025, 12, 31)) && !await Allowed(scopedUser, employeeId, new DateOnly(2026, 2, 1)), "scope and assignment dates enforce half-open boundaries");
        Check(await Allowed(globalUser, employeeId, at) && await Allowed(globalUser, otherEmployee, at), "explicit global permission reads across companies");
        Check(!await Allowed(Guid.Empty, employeeId, at) && !await Allowed(Guid.NewGuid(), employeeId, at) && !await Allowed(globalUser, Guid.NewGuid(), at), "invalid actor and unknown target denied");
    }
    await using (var db = NewDb())
    {
        var permission = await db.Permissions.SingleAsync(x => x.Id == selfPermission);
        permission.Code = EmployeeAccessReader.ReadSelf.ToUpperInvariant();
        await db.SaveChangesAsync();
        var caseDenied = await new EmployeeAccessReader(db).FindAuthorizedAsync(selfUser, employeeId, new DateOnly(2026, 1, 15)) is null;
        permission.Code = EmployeeAccessReader.ReadSelf + " ";
        await db.SaveChangesAsync();
        var paddingDenied = await new EmployeeAccessReader(db).FindAuthorizedAsync(selfUser, employeeId, new DateOnly(2026, 1, 15)) is null;
        permission.Code = EmployeeAccessReader.ReadSelf;
        await db.SaveChangesAsync();
        Check(caseDenied && paddingDenied, "permission codes are exact despite SQL collation and padding");
    }
    await using (var db = NewDb())
    {
        var scope = await db.UserOrgScopes.SingleAsync(x => x.UserId == scopedUser);
        scope.ValidUntilExclusive = null;
        await db.SaveChangesAsync();
        Check(await new EmployeeAccessReader(db).FindAuthorizedAsync(scopedUser, employeeId, new DateOnly(2026, 2, 1)) is null,
            "expired assignment denied even with active scope");
        var assignment = await db.EmployeeAssignments.SingleAsync(x => x.EmployeeId == employeeId);
        assignment.ValidUntilExclusive = null;
        scope.ValidUntilExclusive = new DateOnly(2026, 2, 1);
        await db.SaveChangesAsync();
        Check(await new EmployeeAccessReader(db).FindAuthorizedAsync(scopedUser, employeeId, new DateOnly(2026, 2, 1)) is null,
            "expired scope denied even with active assignment");
    }
    await using (var db = NewDb())
    {
        var grant = await db.UserRoles.SingleAsync(x => x.UserId == scopedUser && x.RoleId == scopeRole);
        db.UserRoles.Remove(grant);
        await db.SaveChangesAsync();
        Check(await new EmployeeAccessReader(db).FindAuthorizedAsync(scopedUser, employeeId, new DateOnly(2026, 1, 15)) is null,
            "permission revocation observed without stale claims");
    }
    await ExpectSqlFailure(async () =>
    {
        await using var db = NewDb();
        db.EmployeeAssignments.Add(new() { Id = Guid.NewGuid(), CompanyId = companyId, EmployeeId = employeeId,
            OrgUnitId = otherUnitId, ValidFrom = new DateOnly(2026, 3, 1) });
        await db.SaveChangesAsync();
    }, 547);
    await ExpectSqlFailure(async () =>
    {
        await using var db = NewDb();
        db.OrgUnits.Add(new() { Id = Guid.NewGuid(), CompanyId = companyId, ParentId = otherUnitId, Name = "Invalid parent" });
        await db.SaveChangesAsync();
    }, 547);
    Check(true, "cross-company assignment and parent rejected");
    await ExpectSqlFailure(async () =>
    {
        await using var db = NewDb(); db.RawAttendanceEvents.Add(Raw(Guid.NewGuid(), "unknown-source"));
        await db.SaveChangesAsync();
    }, 547);
    Check(true, "unknown source foreign key rejected");
    await ExpectSqlFailure(async () =>
    {
        await using var db = NewDb();
        db.Employees.Add(new() { Id = Guid.NewGuid(), CompanyId = companyId, PersonnelNumber = "SYN-1", DisplayName = "Other" });
        await db.SaveChangesAsync();
    }, 2601, 2627);
    Check(true, "duplicate personnel number within company rejected");

    var rawId = Guid.NewGuid();
    await using (var db = NewDb())
    {
        db.RawAttendanceEvents.AddRange(
            new RawAttendanceEventRecord { Id = rawId, SourceId = sourceId, ExternalEventId = "abc", ReceivedAtUtc = DateTimeOffset.UtcNow, SourcePayload = [1] },
            Raw(sourceId, "abc "), Raw(sourceId, "ABC"), Raw(sourceId, "abc\0"), Raw(otherSourceId, "abc"), Raw(sourceId, null), Raw(sourceId, null));
        await db.SaveChangesAsync();
    }
    Check(true, "raw IDs retain exact bytes; different sources and missing IDs allowed");
    await ExpectSqlFailure(async () =>
    {
        await using var db = NewDb(); db.RawAttendanceEvents.Add(Raw(sourceId, "abc")); await db.SaveChangesAsync();
    }, 2601, 2627);
    Check(true, "duplicate raw source and exact external ID rejected");
    async Task<bool> TryConcurrentInsert()
    {
        await using var db = NewDb();
        db.RawAttendanceEvents.Add(Raw(sourceId, "concurrent-event"));
        try { await db.SaveChangesAsync(); return true; }
        catch (DbUpdateException e) when (e.InnerException is SqlException sql && sql.Number is 2601 or 2627) { return false; }
    }
    var concurrent = await Task.WhenAll(TryConcurrentInsert(), TryConcurrentInsert());
    Check(concurrent.Count(x => x) == 1, "concurrent duplicate key inserts store exactly one event");
    await ExpectSqlFailure(async () =>
    {
        await using var db = NewDb(); db.RawAttendanceEvents.Add(Raw(sourceId, "")); await db.SaveChangesAsync();
    }, 547);
    Check(true, "empty external ID rejected");
    await ExpectSqlFailure(async () =>
    {
        await using var db = NewDb();
        db.RawAttendanceEvents.Add(Raw(sourceId, "non-utc", new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.FromHours(1))));
        await db.SaveChangesAsync();
    }, 547);
    Check(true, "non-UTC raw timestamp rejected");
    await ExpectSqlFailure(async () =>
    {
        await using var db = NewDb();
        var newType = "synthetic";
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.RawAttendanceEvents SET ReportedEventType = {newType} WHERE Id = {rawId}");
    }, 51000);
    await ExpectSqlFailure(async () =>
    {
        await using var db = NewDb();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.RawAttendanceEvents WHERE Id = {rawId}");
    }, 51000);
    Check(true, "raw update and delete blocked by trigger");
    await using (var first = NewDb())
    await using (var second = NewDb())
    {
        var a = await first.Employees.SingleAsync(x => x.Id == employeeId);
        var b = await second.Employees.SingleAsync(x => x.Id == employeeId);
        a.DisplayName = "First update"; await first.SaveChangesAsync();
        b.DisplayName = "Stale update";
        try { await second.SaveChangesAsync(); throw new InvalidOperationException("Concurrency conflict not detected"); }
        catch (DbUpdateConcurrencyException) { Check(true, "rowversion detects stale update"); }
    }
    await using (var db = NewDb())
    {
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("0");
        var remaining = 0;
        foreach (var table in expectedTables) if (await HasTable(table)) remaining++;
        Check(remaining == 0, "rollback removed all 39 foundation and later tables");
        await migrator.MigrateAsync();
        var recreated = 0;
        foreach (var table in expectedTables) if (await HasTable(table)) recreated++;
        Check(recreated == expectedTables.Length, "migrations recreated all 39 foundation and later tables");
        Check((await db.Database.GetAppliedMigrationsAsync()).Count() == 18, "migration history restored");
    }
    // Independent Identity schema and bootstrap: the foundation DB was just
    // recreated empty above. Never run this against non-disposable data.
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddLogging();
    serviceCollection.AddIoIdentityStorage(connection);
    await using (var provider = serviceCollection.BuildServiceProvider())
    {
        await using (var scope = provider.CreateAsyncScope())
        {
            var idDb = scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>();
            Check(!idDb.Database.HasPendingModelChanges(), "identity model matches migration snapshot");
            await idDb.Database.MigrateAsync();
            await idDb.Database.MigrateAsync();
            Check((await idDb.Database.GetAppliedMigrationsAsync()).Count() == 1, "identity migration applied idempotently");
            try
            {
                await scope.ServiceProvider.GetRequiredService<InitialAdministrator>().CreateAsync("invalid-admin", "short");
                throw new InvalidOperationException("Weak password was accepted");
            }
            catch (InvalidOperationException e) when (e.Message.StartsWith("Administrator creation rejected by identity validation:", StringComparison.Ordinal)) { }
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var idDb = scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>();
            var foundation = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            Check(!await idDb.Users.AnyAsync() && !await foundation.Users.AnyAsync() && !await idDb.SecurityAudit.AnyAsync(),
                "weak bootstrap password rejected without partial writes");
        }
        async Task<bool> TryInitialize(string name)
        {
            await using var scope = provider.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<InitialAdministrator>().CreateAsync(name, "Synthetic!Password-2026");
                return true;
            }
            catch (InvalidOperationException e) when (e.Message.StartsWith("The database already contains an account", StringComparison.Ordinal)) { return false; }
        }
        var results = await Task.WhenAll(TryInitialize("administrator-one"), TryInitialize("administrator-two"));
        Check(results.Count(x => x) == 1, "concurrent initial administrator attempts create exactly one account");
        await using (var scope = provider.CreateAsyncScope())
        {
            var idDb = scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>();
            var foundation = scope.ServiceProvider.GetRequiredService<IoDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<LoginAccount>>();
            var login = await idDb.Users.SingleAsync();
            Check(await foundation.Users.AnyAsync(x => x.Id == login.Id && x.IsActive) &&
                await idDb.SecurityAudit.CountAsync(x => x.ActorUserId == login.Id && x.Action == "identity.initial-administrator.created") == 1,
                "bootstrap links active foundation user and writes audit atomically");
            Check(login.PasswordHash is not null && login.PasswordHash != "Synthetic!Password-2026" &&
                await manager.CheckPasswordAsync(login, "Synthetic!Password-2026") && !await manager.CheckPasswordAsync(login, "wrong"),
                "Identity stores hashed password and verifies credentials");
            for (var i = 0; i < 5; i++) await manager.AccessFailedAsync(login);
            Check(await manager.IsLockedOutAsync(login), "Identity locks account after five failed attempts");
        }
        Check(!await TryInitialize("third-admin"), "initial administrator creation cannot be repeated");
    }
    await HttpSecurityTests.RunAsync(connection, Check);
    await OrganizationServiceTests.RunAsync(connection, Check);
    await SchedulingServiceTests.RunAsync(connection, Check);
    await AttendanceIngestionTests.RunAsync(connection, Check);
    await PersonalAttendanceTests.RunAsync(connection, Check);
    await ExecutiveOverviewTests.RunAsync(connection, Check);
    await LeaveLedgerTests.RunAsync(connection, Check);
    await DelegationRegistryTests.RunAsync(connection, Check);
    await ObservedCalculationTests.RunAsync(connection, Check);
    await CorrectionWorkflowTests.RunAsync(connection, Check);
    await CorrectionSettingsTests.RunAsync(connection, Check);
    await PeriodDraftTests.RunAsync(connection, Check);
    await PeriodDiagnosticTests.RunAsync(connection, Check);
    await RevisionCoverageTests.RunAsync(connection, Check);
    await TransactionDeadlockTests.RunAsync(connection, Check);
    await using (var identity = new LoginIdentityDbContext(new DbContextOptionsBuilder<LoginIdentityDbContext>()
        .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__IdentityMigrationsHistory")).Options))
    {
        var migration = identity.GetService<IMigrator>();
        await migration.MigrateAsync("0");
        Check(!await HasTable("AspNetUsers") && !await HasTable("SecurityAudit") && await HasTable("Users"),
            "identity rollback removes identity tables without dropping foundation");
        await migration.MigrateAsync();
        Check(await HasTable("AspNetUsers") && await HasTable("SecurityAudit") &&
            (await identity.Database.GetAppliedMigrationsAsync()).Count() == 1,
            "identity migration reapplies and restores history");
    }
    // A separate fresh disposable DB is used to exercise the actual interactive
    // bootstrap executable through a pseudo-terminal; no production connection.
    var cliBuilder = new SqlConnectionStringBuilder(connection) { InitialCatalog = "IO_Phase04_Cli_" + Guid.NewGuid().ToString("N") };
    var cliConnection = cliBuilder.ConnectionString;
    var cliOptions = new DbContextOptionsBuilder<IoDbContext>().UseSqlServer(cliConnection).Options;
    await using (var cliFoundation = new IoDbContext(cliOptions))
    {
        try
        {
            await cliFoundation.Database.MigrateAsync();
            await using var cliIdentity = new LoginIdentityDbContext(new DbContextOptionsBuilder<LoginIdentityDbContext>()
                .UseSqlServer(cliConnection, sql => sql.MigrationsHistoryTable("__IdentityMigrationsHistory")).Options);
            await cliIdentity.Database.MigrateAsync();
            var processInfo = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            processInfo.Environment.Remove("MSSQL_SA_PASSWORD");
            processInfo.Environment["ConnectionStrings__IO"] = cliConnection;
            processInfo.ArgumentList.Add("tests/IO.Infrastructure.SqlTests/bootstrap-terminal-check.py");
            using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Cannot start terminal bootstrap test.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            var output = await stdout; var error = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException("Terminal bootstrap test failed: " + error);
            Check(output.Contains("PASS terminal bootstrap", StringComparison.Ordinal) && await cliIdentity.Users.CountAsync() == 1 &&
                await cliFoundation.Users.CountAsync() == 1 && await cliIdentity.SecurityAudit.CountAsync() == 1,
                "interactive bootstrap hides password, creates one linked account, rejects redirect and repeat");
        }
        finally { await cliFoundation.Database.EnsureDeletedAsync(); }
    }
    Console.WriteLine("PASS disposable SQL Server integration checks");
    return 0;
}
finally
{
    await using var cleanup = NewDb();
    await cleanup.Database.EnsureDeletedAsync();
}
