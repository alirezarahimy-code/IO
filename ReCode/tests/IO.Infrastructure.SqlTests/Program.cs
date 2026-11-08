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
var orgId = Guid.NewGuid(); var companyId = Guid.NewGuid(); var siteId = Guid.NewGuid();
var unitId = Guid.NewGuid(); var employeeId = Guid.NewGuid();
var sourceId = Guid.NewGuid(); var otherSourceId = Guid.NewGuid();
try
{
    await using (var db = NewDb())
    {
        await db.Database.MigrateAsync();
        Check((await db.Database.GetAppliedMigrationsAsync()).Count() == 1, "initial migration applied");
        Check(await HasTable("RawAttendanceEvents"), "raw table exists");
        db.Organizations.Add(new() { Id = orgId, Name = "Synthetic organization" });
        db.Companies.Add(new() { Id = companyId, OrganizationId = orgId, Name = "Synthetic company" });
        db.Sites.Add(new() { Id = siteId, CompanyId = companyId, Name = "Synthetic site", TimeZoneId = "Asia/Tehran" });
        db.OrgUnits.Add(new() { Id = unitId, CompanyId = companyId, Name = "Synthetic unit" });
        db.Employees.Add(new() { Id = employeeId, CompanyId = companyId, PersonnelNumber = "SYN-1", DisplayName = "Synthetic person" });
        db.EventSources.AddRange(new EventSourceRecord { Id = sourceId, SiteId = siteId, Name = "Synthetic source 1" },
            new EventSourceRecord { Id = otherSourceId, SiteId = siteId, Name = "Synthetic source 2" });
        await db.SaveChangesAsync();
    }
    await using (var db = NewDb())
    {
        db.EmployeeAssignments.Add(new() { Id = Guid.NewGuid(), EmployeeId = employeeId, OrgUnitId = unitId,
            ValidFrom = new DateOnly(2026, 1, 1), ValidUntilExclusive = new DateOnly(2026, 2, 1) });
        await db.SaveChangesAsync();
        Check(true, "synthetic organization and valid assignment saved");
    }
    await ExpectSqlFailure(async () =>
    {
        await using var db = NewDb();
        db.EmployeeAssignments.Add(new() { Id = Guid.NewGuid(), EmployeeId = employeeId, OrgUnitId = unitId,
            ValidFrom = new DateOnly(2026, 2, 1), ValidUntilExclusive = new DateOnly(2026, 2, 1) });
        await db.SaveChangesAsync();
    }, 547);
    Check(true, "invalid date interval rejected");
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
            Raw(sourceId, "abc "), Raw(sourceId, "ABC"), Raw(otherSourceId, "abc"), Raw(sourceId, null), Raw(sourceId, null));
        await db.SaveChangesAsync();
    }
    Check(true, "raw IDs retain exact bytes; different sources and missing IDs allowed");
    await ExpectSqlFailure(async () =>
    {
        await using var db = NewDb(); db.RawAttendanceEvents.Add(Raw(sourceId, "abc")); await db.SaveChangesAsync();
    }, 2601, 2627);
    Check(true, "duplicate raw source and exact external ID rejected");
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
        Check(!await HasTable("RawAttendanceEvents"), "rollback removed foundation tables");
        await migrator.MigrateAsync();
        Check(await HasTable("RawAttendanceEvents"), "migration reapplied after rollback");
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
