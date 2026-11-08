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
    "Users", "Roles", "Permissions", "UserRoles", "RolePermissions", "UserOrgScopes", "EventSources", "RawAttendanceEvents"];
try
{
    await using (var db = NewDb())
    {
        Check(!db.Database.HasPendingModelChanges(), "model matches migration snapshot");
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Check((await db.Database.GetAppliedMigrationsAsync()).Count() == 1, "initial migration applied idempotently");
        var created = 0;
        foreach (var table in expectedTables) if (await HasTable(table)) created++;
        Check(created == expectedTables.Length, "all 14 foundation tables created");
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
        Check(remaining == 0, "rollback removed all 14 foundation tables");
        await migrator.MigrateAsync();
        var recreated = 0;
        foreach (var table in expectedTables) if (await HasTable(table)) recreated++;
        Check(recreated == expectedTables.Length, "migration recreated all 14 foundation tables");
        Check((await db.Database.GetAppliedMigrationsAsync()).Count() == 1, "migration history restored");
    }
    Console.WriteLine("PASS disposable SQL Server integration checks");
    return 0;
}
finally
{
    await using var cleanup = NewDb();
    await cleanup.Database.EnsureDeletedAsync();
}
